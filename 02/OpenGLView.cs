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

namespace OpenGLES
{
	public class OpenGLView : UIView
	{
		private CAEAGLLayer eaglLayer;
		private EAGLContext context;
		private uint colorRenderBuffer;
		private CADisplayLink displayLink;

		// Used for the animation
		private float red = 0f;
		private float green = 0f;
		private float blue = 0f;
		private readonly float increment = 5f/255f;

		[Export("layerClass")]
		public static Class LayerClass()
		{
			return new Class(typeof(CAEAGLLayer));
		}

		[Export("initWithFrame:")]
		public OpenGLView(RectangleF frame) : base(frame)
		{
			CreateFrameBuffer();
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

			// Create render buffer and assign it to the context

			GL.GenRenderbuffers(1, ref colorRenderBuffer);
			GL.BindRenderbuffer(All.Renderbuffer, colorRenderBuffer);
			context.RenderBufferStorage((uint)All.Renderbuffer, eaglLayer);

			// Create frame buffer

			uint frameBuffer = 0;

			GL.GenFramebuffers(1, ref frameBuffer);
			GL.BindFramebuffer(All.Framebuffer, frameBuffer);
			GL.FramebufferRenderbuffer(All.Framebuffer, All.ColorAttachment0, All.Renderbuffer, colorRenderBuffer);

			// Create CADisplayLink for animation loop

			displayLink = CADisplayLink.Create(this, new Selector("render"));
			displayLink.FrameInterval = 1;
			displayLink.AddToRunLoop(NSRunLoop.Current, NSRunLoop.NSDefaultRunLoopMode);
		}

		[Export("render")]
		private void Render()
		{
			// Update

			red += increment;

			if (red > 1f)
			{
				red = 0f;
				green += increment;

				if (green > 1f)
				{
					green = 0f;
					blue += increment;

					if (blue > 1f)
					{
						red = green = blue = 0f;
					}
				}
			}

			// Render

			GL.ClearColor(red, green, blue, 1f);
			GL.Clear((int)All.ColorBufferBit);
			context.PresentRenderBuffer((uint)All.Renderbuffer);
		}
	}
}

