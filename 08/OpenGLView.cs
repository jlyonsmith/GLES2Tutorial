using System;
using System.Drawing;
using MonoTouch.Foundation;
using MonoTouch.CoreGraphics;
using MonoTouch.UIKit;
using MonoTouch.CoreAnimation;
using MonoTouch.OpenGLES;
using MonoTouch.ObjCRuntime;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.ES20;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace OpenGLES
{
	[StructLayout(LayoutKind.Sequential)]
	struct Vertex
	{
		public Vertex(int x, int y, int z, int r, int g, int b, int a, int s, int t)
		{
			X = (float)x;
			Y = (float)y;
			Z = (float)z;
			R = (float)r;
			G = (float)g;
			B = (float)b;
			A = (float)a;
			S = (float)s;
			T = (float)t;
		}
		
		public float X;
		public float Y;
		public float Z;
		public float R;
		public float G;
		public float B;
		public float A;
		public float S;
		public float T;
	}
	
	public class OpenGLView : UIView
	{
		private CAEAGLLayer eaglLayer;
		private EAGLContext context;
		private uint colorRenderBuffer;
		private uint depthRenderBuffer;
		private CADisplayLink displayLink;
		private Size size;
		private int colorSlot;
		private int positionSlot;
		private int projectionSlot;
		private int modelViewSlot;
		private int texCoordSlot;
		private int textureSlot;
		private Vertex[] vertices;
		private byte[] indices;
		private uint roosterTexture;
		private float currentRotation;
		private double lastTimestamp;

		[Export("layerClass")]
		public static Class LayerClass()
		{
			return new Class(typeof(CAEAGLLayer));
		}

		[Export("initWithFrame:")]
		public OpenGLView(RectangleF frame) : base(frame)
		{
			CreateFrameBuffer();
			CompileShaders();
			SetupVertexBufferObjects();

			roosterTexture = CreateTexture("Rooster");
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);

			if (context != null)
			{
				context.Dispose();
				context = null;
			}

			if (displayLink != null)
			{
				displayLink.Dispose();
				displayLink = null;
			}
		}

		private void CreateFrameBuffer()
		{
			// Setup layer

			eaglLayer = (CAEAGLLayer)this.Layer;
			eaglLayer.Opaque = false;
			
			bool layerRetainsBacking = false;
			NSString layerColorFormat = EAGLColorFormat.RGBA8;
			
			eaglLayer.DrawableProperties = NSDictionary.FromObjectsAndKeys(new NSObject[]
			                                                               {
				NSNumber.FromBoolean(layerRetainsBacking),
				layerColorFormat
			}, new NSObject[]
			{
				EAGLDrawableProperty.RetainedBacking,
				EAGLDrawableProperty.ColorFormat
			});

			// Create OpenGL drawing context

			EAGLRenderingAPI api = EAGLRenderingAPI.OpenGLES2;
			
			context = new EAGLContext(api);
			
			if (context == null)
			{
				throw new InvalidOperationException("Failed to initialize OpenGLES 2.0 context");
			}
			
			if (!EAGLContext.SetCurrentContext(context))
			{
				throw new InvalidOperationException("Failed to set current OpenGL context");
			}

			// Create size for use later

			size = new Size((int)Math.Round((double)eaglLayer.Bounds.Size.Width), (int)Math.Round((double)eaglLayer.Bounds.Size.Height));

			// Create depth buffer (you have to do this BEFORE creating the color buffer)
			GL.GenRenderbuffers(1, ref depthRenderBuffer);
			GL.BindRenderbuffer(All.Renderbuffer, depthRenderBuffer);
			GL.RenderbufferStorage(All.Renderbuffer, All.DepthComponent16, size.Width, size.Height);
			
			// Create color buffer and allocate storage through the context (as a special case for this particular buffer)

			GL.GenRenderbuffers(1, ref colorRenderBuffer);
			GL.BindRenderbuffer(All.Renderbuffer, colorRenderBuffer);
			context.RenderBufferStorage((uint)All.Renderbuffer, eaglLayer);

			// Create frame buffer

			uint frameBuffer = 0;

			GL.GenFramebuffers(1, ref frameBuffer);
			GL.BindFramebuffer(All.Framebuffer, frameBuffer);
			GL.FramebufferRenderbuffer(All.Framebuffer, All.ColorAttachment0, All.Renderbuffer, colorRenderBuffer);
			GL.FramebufferRenderbuffer(All.Framebuffer, All.DepthAttachment, All.Renderbuffer, depthRenderBuffer);

			// Set viewport 
			GL.Viewport(0, 0, size.Width, size.Height);

			// Create CADisplayLink for animation loop

			displayLink = CADisplayLink.Create(this, new Selector("render"));
			displayLink.FrameInterval = 1;
			displayLink.AddToRunLoop(NSRunLoop.Current, NSRunLoop.NSDefaultRunLoopMode);
		}

		private uint CreateTexture(string imageName)
		{
			string imagePath = NSBundle.PathForResourceAbsolute(imageName, ".png", "Content");
			CGImage image = UIImage.FromFile(imagePath).CGImage;

			if (image == null)
				throw new InvalidOperationException("Failed to load image");

			int width = image.Width;
			int height = image.Height;
			byte[] data = new byte[width * height * 4];

			// See http://blogs.msdn.com/b/shawnhar/archive/2009/11/06/premultiplied-alpha.aspx for an explanation of pre-multiplied alpha

			using (CGBitmapContext context = new CGBitmapContext(data, width, height, 8, width * 4, image.ColorSpace, CGImageAlphaInfo.PremultipliedLast))
			{
				// CoreGraphics flipped the image when it loaded it, so set up a transform to flip it back
				context.ScaleCTM(1f, -1f);
				context.TranslateCTM(0f, -height);
				context.DrawImage(new RectangleF(0, 0, (float)width, (float)height), image);
			}

			uint texture = 0;

			GL.GenTextures(1, ref texture);
			GL.BindTexture(All.Texture2D, texture);

			GL.TexParameter(All.Texture2D, All.TextureMinFilter, (int)All.Nearest);
			GL.TexImage2D(All.Texture2D, 0, (int)All.Rgba, width, height, 0, All.Rgba, All.UnsignedByte, data);

			return texture;
		}

		private int CompileShader(string shaderName, All shaderType)
		{
			string shaderPath = NSBundle.PathForResourceAbsolute(shaderName, ".glsl", "Content");
			string shaderProgram = File.ReadAllText(shaderPath);

			int shader = GL.CreateShader(shaderType);
			int length = shaderProgram.Length;

			GL.ShaderSource(shader, 1, new string[] { shaderProgram }, ref length);
			GL.CompileShader(shader);

			int compileStatus = 0;

			GL.GetShader(shader, All.CompileStatus, ref compileStatus);

			if (compileStatus == (int)All.False)
			{
				StringBuilder sb = new StringBuilder(256);
				length = 0;
				GL.GetShaderInfoLog(shader, sb.Capacity, ref length, sb);
				Console.WriteLine(sb.ToString());
				throw new InvalidOperationException();
			}

			return shader;
		}

		private void CompileShaders()
		{
			int vertexShader = CompileShader("SimpleVertex", All.VertexShader);
			int fragmentShader = CompileShader("SimpleFragment", All.FragmentShader);

			int program = GL.CreateProgram();
			GL.AttachShader(program, vertexShader);
			GL.AttachShader(program, fragmentShader);
			GL.LinkProgram(program);

			int linkStatus = 0;

			GL.GetProgram(program, All.LinkStatus, ref linkStatus);

			if (linkStatus == (int)All.False)
			{
				StringBuilder sb = new StringBuilder(256);
				int length = 0;
				GL.GetProgramInfoLog(program, sb.Capacity, ref length, sb);
				Console.WriteLine(sb.ToString());
				throw new InvalidOperationException();
			}

			GL.UseProgram(program);

			positionSlot = GL.GetAttribLocation(program, "Position");
			GL.EnableVertexAttribArray(positionSlot);

			colorSlot = GL.GetAttribLocation(program, "SourceColor");
			GL.EnableVertexAttribArray(colorSlot);

			texCoordSlot = GL.GetAttribLocation(program, "TexCoordIn");
			GL.EnableVertexAttribArray(texCoordSlot);

			projectionSlot = GL.GetUniformLocation(program, "Projection");
			modelViewSlot = GL.GetUniformLocation(program, "ModelView");
			textureSlot = GL.GetUniformLocation(program, "Texture");
		}

		private void SetupVertexBufferObjects()
		{
			vertices = new Vertex[]
			{
				new Vertex(-1, -1, 0, 	1, 1, 1, 1, 	0, 0), // 0
				new Vertex(-1, 1, 0, 	1, 1, 1, 1, 	0, 1), // 1
				new Vertex(1, 1, 0, 	1, 1, 1, 1, 	1, 1), // 2
				new Vertex(1, -1, 0, 	1, 1, 1, 1, 	1, 0) // 3
			};

			// PAY ATTENTION! A face is considered front facing if the 
			// winding order of the vertices of the face are in CCW 
			// order LOOKING FROM INSIDE THE SHAPE.  So from outside
			// the vertices will be CW.  OpenGL makes this very non-obvious.
			// Also, if you start seeing chunks of you shape missing you
			// probably moved the viewpoint inside the shape.

			indices = new byte[]
			{
				// Front
				0, 1, 2,
				2, 3, 0,
			};

			int vertexBuffer = 0;

			GL.GenBuffers(1, ref vertexBuffer);
			GL.BindBuffer(All.ArrayBuffer, vertexBuffer);
			GL.BufferData(All.ArrayBuffer, (IntPtr)(vertices.Length * Marshal.SizeOf(typeof(Vertex))), vertices, All.StaticDraw);

			int indexBuffer = 0;

			GL.GenBuffers(1, ref indexBuffer);
			GL.BindBuffer(All.ElementArrayBuffer, indexBuffer);
			GL.BufferData(All.ElementArrayBuffer, (IntPtr)(indices.Length * sizeof(byte)), indices, All.StaticDraw);
		}

		[Export("render")]
		private void Render()
		{
			// Update
			Matrix4 projectionMatrix;
			float h = 4f * size.Height / size.Width;
			Matrix4.CreatePerspectiveOffCenter(-2f, 2f, -h/2f, h/2f, 4f, 10f, out projectionMatrix);

			// http://www.songho.ca/opengl/gl_projectionmatrix.html has the math behind this projection matrix and
			// http://rbwhitaker.wikidot.com/basic-matrices has some helpful background.

			Matrix4 modelViewMatrix;
			Matrix4.CreateTranslation((float)Math.Sin(CABasicAnimation.CurrentMediaTime()), 0f, -7f, out modelViewMatrix);
			modelViewMatrix = Matrix4.CreateRotationX(currentRotation) * modelViewMatrix;

			if (lastTimestamp != 0)
				currentRotation += (float)(displayLink.Timestamp - lastTimestamp);

			lastTimestamp = displayLink.Timestamp;

			// Render
			GL.ClearColor(0f, 0.3f, 0f, 1f);
			GL.Clear((int)All.ColorBufferBit | (int)All.DepthBufferBit);

			GL.Enable(All.DepthTest);

			GL.VertexAttribPointer(positionSlot, 3, All.Float, false, Marshal.SizeOf(typeof(Vertex)), (IntPtr)0);
			GL.VertexAttribPointer(colorSlot, 4, All.Float, false, Marshal.SizeOf(typeof(Vertex)), (IntPtr)(sizeof(float) * 3));
			GL.VertexAttribPointer(texCoordSlot, 2, All.Float, false, Marshal.SizeOf(typeof(Vertex)), (IntPtr)(sizeof(float) * 7));

			GL.UniformMatrix4(projectionSlot, 1, false, ref projectionMatrix.Row0.X);
			GL.UniformMatrix4(modelViewSlot, 1, false, ref modelViewMatrix.Row0.X);

			GL.ActiveTexture(All.Texture0);
			GL.BindTexture(All.Texture2D, roosterTexture);
			GL.Uniform1(textureSlot, 0);

			GL.DrawElements(All.Triangles, indices.Length, All.UnsignedByte, (IntPtr)0);

			context.PresentRenderBuffer((uint)All.Renderbuffer);
		}
	}
}

