using System.Windows.Forms;

namespace Bloom.Publish.AccessibilityChecker
{
	public partial class AccessibilityCheckWindow : Form
	{
		public delegate AccessibilityCheckWindow Factory();//autofac uses this
		private static AccessibilityCheckWindow.Factory _createAccessibilityCheckerFactory;
		private static AccessibilityCheckWindow _sAccessibilityCheckerWindow;
		private bool _disposed;


		public static void StaticSetFactory(Factory createAccessibilityChecker)
		{
			_createAccessibilityCheckerFactory = createAccessibilityChecker;
		}

		public static void StaticShow()
		{
			if (_sAccessibilityCheckerWindow == null)
			{
				_sAccessibilityCheckerWindow = _createAccessibilityCheckerFactory();
				_sAccessibilityCheckerWindow.Show();
			}
			else
			{
				_sAccessibilityCheckerWindow.BringToFront();
			}
		}

		public AccessibilityCheckWindow()
		{
			InitializeComponent();
			var path = BloomFileLocator.GetBrowserFile(false, "publish", "accessibilityCheck", "accessibilityCheckScreen.html");
			_browser.Navigate(path.ToLocalhost(),false);
		}

		private void AccessibilityCheckWindow_FormClosed(object sender, FormClosedEventArgs e)
		{
			_sAccessibilityCheckerWindow = null;
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
	}
}
