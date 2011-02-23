using System;
using System.Drawing;
using System.Windows.Forms;

namespace Bloom.Library
{
	public partial class ConfirmDelete : Form
	{
		public ConfirmDelete()
		{
			Font = SystemFonts.MessageBoxFont;
			InitializeComponent();
			textBox1.BackColor = this.BackColor;
		}

		private void deleteBtn_Click(object sender, EventArgs e)
		{
			this.DialogResult = System.Windows.Forms.DialogResult.OK;
			this.Close();
		}

		private void cancelBtn_Click(object sender, EventArgs e)
		{
			this.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.Close();

		}

		private void ConfirmDelete_BackColorChanged(object sender, EventArgs e)
		{
			textBox1.BackColor = this.BackColor;
		}

	}
}
