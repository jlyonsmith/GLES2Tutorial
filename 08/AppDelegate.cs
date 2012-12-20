using System;
using System.Collections.Generic;
using System.Linq;
using MonoTouch.Foundation;
using MonoTouch.UIKit;

namespace OpenGLES
{
	[Register ("AppDelegate")]
	public partial class AppDelegate : UIApplicationDelegate
	{
		UIWindow window;
		OpenGLView glView;

		public override bool FinishedLaunching(UIApplication app, NSDictionary options)
		{
			app.SetStatusBarHidden(true, UIStatusBarAnimation.None);

			window = new UIWindow(UIScreen.MainScreen.Bounds);
			glView = new OpenGLView(window.Frame);
			window.AddSubview(glView);
			window.MakeKeyAndVisible();
			return true;
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);

			glView.Dispose();
			glView = null;
		}
	}
}

