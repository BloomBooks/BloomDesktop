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

		public AndroidView(NavigationIsolator isolator, BloomWebSocketServer webSocketServer)
		{
			_webSocketServer = webSocketServer;
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

		private string GetUrlParams()
		{
			return $"?isLinux={Platform.IsLinux}";
		}

		private void OnVisibleChanged(object sender, EventArgs eventArgs)
		{
			if (!Visible)
			{
				Deactivate();
			}
		}

		public void Deactivate()
		{
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
