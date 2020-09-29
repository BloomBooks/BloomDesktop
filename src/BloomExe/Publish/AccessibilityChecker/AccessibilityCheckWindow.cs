using System;
using System.Windows.Forms;
using SIL.Program;

namespace Bloom.Publish.AccessibilityChecker
{
	/// <summary>
	/// This is a separate window that the AccessibilityApi opens. In contains a web browser
	/// that loads up some html that then shows some tabs related to ensuring that the epub is accessible.
	/// </summary>
	public partial class AccessibilityCheckWindow : Form
	{
		private readonly Action _onWindowActivated;

		private static AccessibilityCheckWindow _sTheOneAccessibilityCheckerWindow;
		private bool _disposed;

		public static void StaticShow(Action onWindowActivated)
		{
			if (_sTheOneAccessibilityCheckerWindow == null)
			{
				_sTheOneAccessibilityCheckerWindow = new AccessibilityCheckWindow(onWindowActivated);
				_sTheOneAccessibilityCheckerWindow.Show();
			}
			else
			{
				_sTheOneAccessibilityCheckerWindow.BringToFront();
			}
		}

		public AccessibilityCheckWindow(Action onWindowActivated)
		{
			_onWindowActivated = onWindowActivated;
			InitializeComponent();

			var path = BloomFileLocator.GetBrowserFile(false, "publish", "accessibilityCheck", "accessibilityCheckScreen.html");
			_browser.Navigate(path.ToLocalhost(),false);

			_browser.GeckoReady += (sender, args) =>
			{
				// There are some external links in the checker report.
				// They will attempt to open a new window which has loading and display issues.
				// Just open them in the system browser instead. See BL-9026.
				_browser.WebBrowser.CreateWindow += (geckoBrowser, eventArgs) =>
				{
					eventArgs.Cancel = true;
					Process.SafeStart(eventArgs.Uri);
				};
			};
		}

		private void AccessibilityCheckWindow_FormClosed(object sender, FormClosedEventArgs e)
		{
			_sTheOneAccessibilityCheckerWindow = null;
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				components?.Dispose();
			}
			base.Dispose(disposing);
			_disposed = true;
		}

		private void AccessibilityCheckWindow_Activated(object sender, System.EventArgs e)
		{
			_onWindowActivated();
		}
	}
}
