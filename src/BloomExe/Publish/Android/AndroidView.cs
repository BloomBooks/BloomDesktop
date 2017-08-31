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
			BloomFileLocator.GetBrowserFile("gulpfile.js");
			var path = BloomFileLocator.GetBrowserFile("publish","android","androidPublishUI.html");
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
			// This allows the WebSocketManager to clean up all the listeners and close the socket.
			// This prevents various JS errors that get raised (BL-4901) if we wait for it to get
			// closed as the page goes away.
			_webSocketServer.Send("closeAndroidUISocket", "");
			// This is important so the react stuff can do its cleanup
			_browser.WebBrowser.Navigate("about:blank");
		}
	}
}
