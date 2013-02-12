using System;
using System.Windows.Forms;

namespace Bloom.CollectionTab
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
