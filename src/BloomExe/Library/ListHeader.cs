using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Bloom.Library
{
	public partial class ListHeader : UserControl
	{
		public ListHeader()
		{
			InitializeComponent();
		}

		private void ListHeader_ForeColorChanged(object sender, EventArgs e)
		{
			Label.ForeColor = ForeColor;
		}
	}
}
