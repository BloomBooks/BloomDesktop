using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Api;
using SIL.IO;
using SIL.Reporting;
using Gecko;

namespace Bloom.Library
{
	public partial class WebLibraryView : UserControl
	{
		private IBrowser _browser;

		public delegate WebLibraryView Factory();//autofac uses this

		public WebLibraryView()
		{
			InitializeComponent();
			//_browser.BrowserReady += new EventHandler(OnLod);
			UserControl b = new  WebView2Browser();//GeckoFxBrowser();//
			b.Parent = this;
			b.Dock = DockStyle.Fill;
			Controls.Add(b);
			_browser = (IBrowser)b;
			Load+=new EventHandler(WebLibraryView_Load);
		}

		private void LibraryView_VisibleChanged(object sender, EventArgs e)
		{
			if(Visible)
			{
				UsageReporter.SendNavigationNotice("Library");
			}
		}

		private void WebLibraryView_Load(object sender, EventArgs e)
		{
			_browser.Navigate(BloomServer.ServerUrlWithBloomPrefixEndingInSlash+"library/library.htm",false);
		}
	}
}
