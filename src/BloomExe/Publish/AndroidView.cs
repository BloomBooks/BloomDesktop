using System.Windows.Forms;

namespace Bloom.Publish
{
	/// <summary>
	/// This class implements the panel that appears in the Publish tab when the Android button is selected.
	/// </summary>
	public partial class AndroidView : UserControl
	{
		private Browser _browser;

		public AndroidView(NavigationIsolator isolator)
		{
			InitializeComponent();

			_browser = new Browser();
			_browser.Isolator = isolator;
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);
			// Has to be in front of the panel docked top for Fill to work.
			_browser.BringToFront();
			//TODO localization
			BloomFileLocator.GetBrowserFile("gulpfile.js");
			var path = BloomFileLocator.GetBrowserFile("publish","android","androidPublishUI.html");
			_browser.Navigate(path.ToLocalhost(), false);
		}
	}
}
