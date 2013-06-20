using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Bloom.Edit
{
	public partial class ConfirmRemovePageDialog : Form
	{
		public string LabelForThingBeingDeleted { get; set; }

		public ConfirmRemovePageDialog()
		{
			InitializeComponent();
			_messageLabel.Font = SystemFonts.MessageBoxFont;
		}

		public ConfirmRemovePageDialog(string labelForThingBeingDeleted)
			: this()
		{
			LabelForThingBeingDeleted = labelForThingBeingDeleted.Trim();
			_messageLabel.Text = string.Format(_messageLabel.Text, LabelForThingBeingDeleted);

			// Sometimes, setting the text in the previous line will force the table layout control
			// to resize itself accordingly, which will fire its SizeChanged event. However,
			// sometimes the text is not long enough to force the table layout to be resized,
			// therefore, we need to call it manually, just to be sure the form gets sized correctly.
			HandleTableLayoutSizeChanged(null, null);
		}

		private void HandleTableLayoutSizeChanged(object sender, EventArgs e)
		{
			if (!IsHandleCreated)
				CreateHandle();

			var scn = Screen.FromControl(this);
			var desiredHeight = tableLayout.Height + Padding.Top + Padding.Bottom + (Height - ClientSize.Height);
			Height = Math.Min(desiredHeight, scn.WorkingArea.Height - 20);
		}

		protected override void OnBackColorChanged(EventArgs e)
		{
			base.OnBackColorChanged(e);
			_messageLabel.BackColor = BackColor;
		}

		private void deleteBtn_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.OK;
			Close();
		}

		private void cancelBtn_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		public static bool Confirm()
		{
			using (var dlg = new ConfirmRemovePageDialog())
			{
				return DialogResult.OK == dlg.ShowDialog();
			}
		}
	}
}
