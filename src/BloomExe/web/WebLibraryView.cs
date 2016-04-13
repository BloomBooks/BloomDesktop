﻿using System;
using System.Drawing;
using System.Windows.Forms;
using Bloom.web;
using SIL.IO;
using SIL.Reporting;
using Gecko;

namespace Bloom.Library
{
	public partial class WebLibraryView : UserControl
	{
		private Browser b;

		public delegate WebLibraryView Factory();//autofac uses this

		public WebLibraryView(NavigationIsolator isolator)
		{
			InitializeComponent();
			//_browser.GeckoReady += new EventHandler(OnLod);
			b = new Browser();
			b.Isolator = isolator;
			b.Parent = this;
			b.Dock = DockStyle.Fill;
			Controls.Add(b);
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
			b.Navigate(ServerBase.ServerUrlWithBloomPrefixEndingInSlash+"library/library.htm",false);
		}
	}
}
