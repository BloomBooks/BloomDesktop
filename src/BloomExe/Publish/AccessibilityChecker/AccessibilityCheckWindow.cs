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

		public static void StaticClose()
		{
			if (_sTheOneAccessibilityCheckerWindow != null)
			{
				_sTheOneAccessibilityCheckerWindow.Close();
				_sTheOneAccessibilityCheckerWindow = null;
			}
		}

		public AccessibilityCheckWindow(Action onWindowActivated)
		{
			_onWindowActivated = onWindowActivated;
			InitializeComponent();

			var path = BloomFileLocator.GetBrowserFile(false, "publish", "accessibilityCheck", "accessibilityCheckScreen.html");
			_browser.Navigate(path.ToLocalhost(),false);
			_browser.OnBrowserClick += Browser.HandleExternalLinkClick; // See BL-9026
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
