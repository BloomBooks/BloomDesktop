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

		public void AdjustWidth()
		{
			using (var g = CreateGraphics())
			{
				var size = g.MeasureString(Label.Text, Label.Font);
				Width = (int)size.Width + 6;
			}
		}
	}
}
