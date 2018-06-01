﻿using System.Windows.Forms;

namespace Bloom.Publish.AccessibilityChecker
{
	/// <summary>
	/// This is a separate window that the AccessibilityApi opens. In contains a web browser
	/// that loads up some html that then shows some tabs related to ensuring that the epub is accessible.
	/// </summary>
	public partial class AccessibilityCheckWindow : Form
	{
		private static AccessibilityCheckWindow _sTheOneAccessibilityCheckerWindow;
		private bool _disposed;

		public static void StaticShow()
		{
			if (_sTheOneAccessibilityCheckerWindow == null)
			{
				_sTheOneAccessibilityCheckerWindow = new AccessibilityCheckWindow();
				_sTheOneAccessibilityCheckerWindow.Show();
			}
			else
			{
				_sTheOneAccessibilityCheckerWindow.BringToFront();
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
	}
}
