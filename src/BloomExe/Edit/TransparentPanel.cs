using System.Windows.Forms;

namespace Bloom.Edit
{
	class TransparentPanel : Panel
	{
		public TransparentPanel(string name, Control controlToCover)
		{
			Name = name;
			Top = 0;
			Left = 0;
			Size = controlToCover.Size;
		}

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams createParams = base.CreateParams;
				createParams.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
				return createParams;
			}
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			// Do not paint background
		}
	}
}
