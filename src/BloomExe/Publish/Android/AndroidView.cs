using System;
using System.Windows.Forms;
using Bloom.Api;
using SIL.PlatformUtilities;

namespace Bloom.Publish.Android
{
	/// <summary>
	/// This class implements the panel that appears in the Publish tab when the Android button is selected.
	/// </summary>
	public partial class AndroidView : UserControl
	{
		private Browser _browser;
		private BloomWebSocketServer _webSocketServer;
		private PublishToAndroidApi _publishToAndroidApi;

		public AndroidView(NavigationIsolator isolator, BloomWebSocketServer webSocketServer, PublishToAndroidApi publishToAndroidApi)
		{
			_webSocketServer = webSocketServer;
			_publishToAndroidApi = publishToAndroidApi;
			InitializeComponent();

			_browser = new Browser();
			_browser.Isolator = isolator;
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);
			// Has to be in front of the panel docked top for Fill to work.
			_browser.BringToFront();
			BloomFileLocator.GetBrowserFile(false, "gulpfile.js");
			var path = BloomFileLocator.GetBrowserFile(false, "publish","android","androidPublishUI.html");
			_browser.Navigate(path.ToLocalhost() + GetUrlParams(), false);

			VisibleChanged += OnVisibleChanged;
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			// We're trying to catch all the ways this control can be hidden from the user.
			// Unfortunately the VisibleChanged event is only raised if the control's own local
			// visibility setting changes. So we need to consider all its parents, as well as
			// the possibilty that the whole app is going away.
			// There's probably a few of these that we don't need to do it to, but it's fairly
			// harmless (and very hard to predict which might somehow get hidden).
			Control control = this;
			while (!(control is Form))
			{
				control.VisibleChanged += OnVisibleChanged;
				control = control.Parent;
			}
			var form = control as Form;
			form.Closing += (sender, args) => Deactivate();
		}

		private string GetUrlParams()
		{
			return $"?isLinux={Platform.IsLinux}";
		}

		private void OnVisibleChanged(object sender, EventArgs eventArgs)
		{
			if (IsDisposed)
				return; // too late.
			if (!Visible)
			{
				Deactivate();
			}
		}

		public void Deactivate()
		{
			// It's harmless to do the various stop things an extra time.
			// But it's important to do them at least once when this view is switched away from,
			// especially if we're quitting the program, so that any sends in progress get
			// aborted rather than keeping Bloom running a background thread forever.
			// And the notifications that result from
			// navigating to about:blank may be delayed by the navigation isolator, conceivably
			// might never happen if the browser is disposed too soon.
			if (_publishToAndroidApi != null)
				_publishToAndroidApi.Stop();
			// This is important so the react stuff can do its cleanup
			_browser.WebBrowser.Navigate("about:blank");
		}

		/// <summary>
		/// Check for either "Device16x9Portrait" or "Device16x9Landscape" layout.
		/// Complain to the user if another layout is currently chosen.
		/// </summary>
		/// <remarks>
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-5274.
		/// </remarks>
		static internal void CheckBookLayout(Bloom.Book.Book book, Bloom.web.WebSocketProgress progress)
		{
			var layout = book.GetLayout();
			var desiredLayoutSize = "Device16x9";
			if (layout.SizeAndOrientation.PageSizeName != desiredLayoutSize)
			{
				// The progress object has been initialized to use an id prefix.  So we'll access L10NSharp explicitly here.  We also want to make the string blue,
				// which requires a special argument.
				var msgFormat = L10NSharp.LocalizationManager.GetString("PublishTab.Android.WrongLayout.Message",
					"The layout of this book is currently \"{0}\". Bloom Reader will display it using \"{1}\", so text might not fit. To see if anything needs adjusting, go back to the Edit Tab and change the layout to \"{1}\".",
					"{0} and {1} are book layout tags.");
				var desiredLayout = desiredLayoutSize + layout.SizeAndOrientation.OrientationName;
				var msg = String.Format(msgFormat, layout.SizeAndOrientation.ToString(), desiredLayout, Environment.NewLine);
				progress.MessageWithStyleWithoutLocalizing(msg, "color:blue");
			}
		}
	}
}
