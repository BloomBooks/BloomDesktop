
using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.Windows.Forms;
using SIL.PlatformUtilities;

namespace Bloom.Edit
{
	/// <summary>
	/// This class implements the panel that appears in the Edit tab at the bottom of the thumbnail list.
	/// </summary>
	public partial class PageControlsView : UserControl
	{
		private Browser _browser;

		/// <summary>
		/// Need a ctor with no params for Designer to work
		/// </summary>
		public PageControlsView()
		{
			InitializeComponent();

			if (!ReallyDesignMode)
			{
				_browser = new Browser();
				_browser.BackColor = Color.DarkGray;
				_browser.Dock = DockStyle.Bottom;
				_browser.Location = new Point(0, 0);
				_browser.Name = "_pageControlsBrowser";
				_browser.VerticalScroll.Visible = false;
				Controls.Add(_browser);
			}
		}

		public void Initialize(NavigationIsolator isolator)
		{
			_browser.Isolator = isolator;

			BloomFileLocator.GetBrowserFile(false, "gulpfile.js");
			var path = BloomFileLocator.GetBrowserFile(false, "bookEdit","pageThumbnailList","pageControls","pageControlsUI.html");
			_browser.Navigate(path.ToLocalhost() + GetUrlParams(), false);

			VisibleChanged += OnVisibleChanged;
		}

		private bool ReallyDesignMode
		{
			get
			{
				return (base.DesignMode || GetService(typeof(IDesignerHost)) != null) ||
				       (LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			}
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
