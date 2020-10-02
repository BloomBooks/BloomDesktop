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

		public static bool IsVisible => _sTheOneAccessibilityCheckerWindow != null && _sTheOneAccessibilityCheckerWindow.Visible;

		// Hide the window, if it exists, while performing the specified action. It's necessary for it actually
		// to be disposed, so if this happens, re-create it afterwards.
		// The implementation must handle not being called on the UI thread!
		// Note: we are not yet recreating it perfectly, since (a) this will bring it to the front, and
		// (b) we're not preserving its internal state, such as which tab is active. I'm hoping that the relatively
		// few users who need this window can live with this. Currently it is only needed while re-creating
		// the epub with the checker open. See BL-7807.
		public static void HideTemporarily(Action doWhileHidden)
		{
			if (_sTheOneAccessibilityCheckerWindow == null)
			{
				doWhileHidden();
				return;
			}

			var activated = _sTheOneAccessibilityCheckerWindow._onWindowActivated;
			var location = _sTheOneAccessibilityCheckerWindow.Location;
			_sTheOneAccessibilityCheckerWindow.Invoke((Action) (() => _sTheOneAccessibilityCheckerWindow.Close()));
			doWhileHidden();
			Form.ActiveForm.Invoke((Action) (() =>
			{
				StaticShow(activated);
				_sTheOneAccessibilityCheckerWindow.Location = location;
			}));
			
		}

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
