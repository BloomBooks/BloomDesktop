using System;
using System.Windows.Forms;
using SIL.PlatformUtilities;

namespace Bloom.Publish
{
	/// <summary>
	/// This configurable class provides a C# wrapper for several of the publish option 
	/// panels that appear in the Publish tab, for which the UI is an html component
	/// </summary>
	public partial class HtmlPublishPanel : UserControl
	{
		private GeckoFxBrowser _browser;

		public HtmlPublishPanel(string pathToHtmlFile)
		{
			InitializeComponent();

			_browser = new GeckoFxBrowser();
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);
			// Has to be in front of the panel docked top for Fill to work.
			_browser.BringToFront();
			_browser.Navigate(pathToHtmlFile.ToLocalhost() + GetUrlParams(), false);
			_browser.OnBrowserClick += GeckoFxBrowser.HandleExternalLinkClick;

			VisibleChanged += OnVisibleChanged;
		}

		public GeckoFxBrowser Browser => _browser;

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

		private void Deactivate()
		{
			// This is important so the react stuff can do its cleanup
			_browser.WebBrowser.Navigate("about:blank");
		}

	}
}
