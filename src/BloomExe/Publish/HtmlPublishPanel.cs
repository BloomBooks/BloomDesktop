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
		private Browser _browser;

		public HtmlPublishPanel(string pathToHtmlFile)
		{
			InitializeComponent();

			_browser = BrowserMaker.MakeBrowser();
			var browserControl = (UserControl)_browser;
			browserControl.Dock = DockStyle.Fill;
			Controls.Add(browserControl);
			// Has to be in front of the panel docked top for Fill to work.
			browserControl.BringToFront();
			_browser.Navigate(pathToHtmlFile.ToLocalhost() + GetUrlParams(), false);

			VisibleChanged += OnVisibleChanged;
		}

		public Browser Browser => _browser;

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
			_browser.Navigate("about:blank",false);
		}

	}
}
