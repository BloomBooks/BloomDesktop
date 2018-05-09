using System;
using System.Windows.Forms;
using SIL.PlatformUtilities;

namespace Bloom.Publish.Epub2
{
	/// <summary>
	/// This class implements the panel that appears in the Publish tab when the Epub button is selected.
	/// </summary>
	public partial class EpubView2 : UserControl
	{
		private Browser _browser;

		public EpubView2(NavigationIsolator isolator)
		{
			InitializeComponent();

			_browser = new Browser();
			_browser.Isolator = isolator;
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);
			// Has to be in front of the panel docked top for Fill to work.
			_browser.BringToFront();
			var path = BloomFileLocator.GetBrowserFile(false, "publish","epub", "EpubPublishUI.html");
			_browser.Navigate(path.ToLocalhost(), false);

			VisibleChanged += OnVisibleChanged;
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
