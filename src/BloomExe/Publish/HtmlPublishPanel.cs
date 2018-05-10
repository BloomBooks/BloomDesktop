using System;
using System.Windows.Forms;
using SIL.PlatformUtilities;

namespace Bloom.Publish
{
	/// <summary>
	/// This class implements a panel that appears in the Publish tab, for which the UI is an html component.
	/// </summary>
	public partial class HtmlPublishPanel : UserControl
	{
		private Browser _browser;

		public HtmlPublishPanel(NavigationIsolator isolator, string path)
		{
			InitializeComponent();

			_browser = new Browser();
			_browser.Isolator = isolator;
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);
			// Has to be in front of the panel docked top for Fill to work.
			_browser.BringToFront();
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

		private void Deactivate()
		{
			// This is important so the react stuff can do its cleanup
			_browser.WebBrowser.Navigate("about:blank");
		}

	}
}
