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
	public partial class TemplatePagesView : UserControl
	{
		public TemplatePagesView()
		{
			this.Font = SystemFonts.MessageBoxFont;
			InitializeComponent();
		}

		private void TemplatePagesView_BackColorChanged(object sender, EventArgs e)
		{
			listView1.BackColor = BackColor;
		}
	}
}
