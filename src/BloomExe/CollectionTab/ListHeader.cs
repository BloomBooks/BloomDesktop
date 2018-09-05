using System;
using System.Drawing;
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
			Label.AutoSize = true;
			using (var g = CreateGraphics())
			{
				var size = TextRenderer.MeasureText(g, Label.Text, Label.Font);
				Width = size.Width + 6;
			}
		}

		/// <summary>
		/// This one is used to make it (and the label) the right size to fit
		/// in the specified width by wrapping the text if necessary
		/// </summary>
		public void AdjustSize(int width)
		{
			Label.AutoSize = false; // otherwise we can't change it.
			var textSize = TextRenderer.MeasureText(Label.Text, Label.Font,
				// The Label has margins of 3, so we need six pixels for that twice
				new Size(width - 6, Int32.MaxValue),
				TextFormatFlags.WordBreak);
			var oldTop = Label.Top;
			// Found experimentally to make it hold the text without clipping
			Label.Size = new Size(textSize.Width, textSize.Height + 3);
			// Keeps original design spacing.
			this.Size = new Size(Label.Width + 6, Label.Height + 14);
			// The label is anchored 'bottom' for possibly historical reasons.
			// But as we resize both controls we want to keep it at the same position.
			// If we don't enforce this it will move down as the outer control gets bigger,
			// and since the label gets bigger too it will be clipped.
			Label.Top = oldTop;
		}
	}
}
