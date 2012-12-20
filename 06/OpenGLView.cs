using System;
using System.Drawing;
using MonoTouch.Foundation;
using MonoTouch.UIKit;
using MonoTouch.CoreAnimation;
using MonoTouch.OpenGLES;
using MonoTouch.ObjCRuntime;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.ES20;
using System.IO;
using System.Text;

namespace OpenGLES
{
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
		private float[] vertices;
		private int vertexSize = sizeof(float) * 7;
		private byte[] indices;
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

			GL.FrontFace(All.Ccw);
			GL.CullFace(All.Back);

			// Create CADisplayLink for animation loop

			displayLink = CADisplayLink.Create(this, new Selector("render"));
			displayLink.FrameInterval = 1;
			displayLink.AddToRunLoop(NSRunLoop.Current, NSRunLoop.NSDefaultRunLoopMode);
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

			projectionSlot = GL.GetUniformLocation(program, "Projection");
			modelViewSlot = GL.GetUniformLocation(program, "ModelView");
		}

		private void SetupVertexBufferObjects()
		{
			vertices = new float[]
			{
				1, -1, 0, 	1, 0, 0, 1, // 0
				1, 1, 0, 	0, 1, 0, 1, // 1
				-1, 1, 0, 	0, 0, 1, 1, // 2
				-1, -1, 0,	0, 0, 0, 1, // 3
				-1, -1, 1,  1, 0, 0, 1, // 4
				1, -1, 1,	0, 1, 0, 1, // 5
				1, 1, 1,	0, 0, 1, 1, // 6
				-1, 1, 1,	0, 0, 0, 1	// 7
			};

			// PAY ATTENTION! A face is considered front facing if the 
			// winding order of the vertices of the face are in CCW 
			// order LOOKING FROM INSIDE THE SHAPE.  So from outside
			// the vertices will be CW.  OpenGL makes this very unobvious.
			// Note that the shape should be convex (no holes).  If you
			// start seeing chunks of you shape missing you probably 
			// moved the viewpoint inside the shape.

			indices = new byte[]
			{
				// Front
				0, 3, 2,
				2, 1, 0,

				// Right
				5, 0, 1,
				1, 6, 5,

				// Back
				4, 5, 6,
				6, 7, 4,

				// Left
				3, 4, 7,
				7, 2, 3,

				// Top
				1, 2, 7,
				7, 6, 1,

				// Bottom
				5, 4, 3,
				3, 0, 5
			};

			int vertexBuffer = 0;

			GL.GenBuffers(1, ref vertexBuffer);
			GL.BindBuffer(All.ArrayBuffer, vertexBuffer);
			GL.BufferData(All.ArrayBuffer, (IntPtr)(vertices.Length * sizeof(float)), vertices, All.StaticDraw);

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

			Matrix4 modelViewMatrix;
			Matrix4.CreateTranslation((float)Math.Sin(CABasicAnimation.CurrentMediaTime()), 0f, -7f, out modelViewMatrix);
			modelViewMatrix = Matrix4.CreateRotationX(currentRotation) * modelViewMatrix;

			if (lastTimestamp != 0)
				currentRotation += (float)(displayLink.Timestamp - lastTimestamp);

			lastTimestamp = displayLink.Timestamp;

			// Render
			GL.ClearColor(0f, 0f, 0f, 1f);
			GL.Clear((int)All.ColorBufferBit | (int)All.DepthBufferBit);

			GL.Enable(All.CullFace);
			GL.Enable(All.DepthTest);

			GL.VertexAttribPointer(positionSlot, 3, All.Float, false, vertexSize, (IntPtr)0);
			GL.VertexAttribPointer(colorSlot, 4, All.Float, false, vertexSize, (IntPtr)(sizeof(float) * 3));

			GL.UniformMatrix4(projectionSlot, 1, false, ref projectionMatrix.Row0.X);
			GL.UniformMatrix4(modelViewSlot, 1, false, ref modelViewMatrix.Row0.X);

			GL.DrawElements(All.Triangles, indices.Length, All.UnsignedByte, (IntPtr)0);

			context.PresentRenderBuffer((uint)All.Renderbuffer);
		}
	}
}

