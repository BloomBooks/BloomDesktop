using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom
{
	public partial class PageListView : UserControl
	{
		public PageListView()
		{
			this.Font= SystemFonts.MessageBoxFont;
			InitializeComponent();
		}

		private void PageListView_BackColorChanged(object sender, EventArgs e)
		{
			listView1.BackColor = BackColor;
		}
	}
}
