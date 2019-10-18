using System;
using System.Windows.Forms;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ClearShare.WinFormsUI;
using L10NSharp;

namespace Bloom.Edit
{
	public class BloomMetadataEditorDialog : MetadataEditorDialog
	{
		public CheckBox _replaceCopyrightCheckbox;

		public BloomMetadataEditorDialog(Metadata metadata, bool isDerivedBook) : base(metadata)
		{
			ShowCreator = false;
			if (isDerivedBook)
			{
				this.SuspendLayout();

				_replaceCopyrightCheckbox = new CheckBox();
				_replaceCopyrightCheckbox.Text = LocalizationManager.GetString("EditTab.CopyrightThisDerivedVersion", "Copyright and license this translated version");
				_replaceCopyrightCheckbox.Checked = true;
				_replaceCopyrightCheckbox.CheckedChanged += ReplaceCopyrightCheckboxChanged;
				_replaceCopyrightCheckbox.Location = new System.Drawing.Point(20,10);
				_replaceCopyrightCheckbox.AutoSize = true;

				var topPanel = new Panel();
				topPanel.SuspendLayout();
				topPanel.Dock = System.Windows.Forms.DockStyle.Top;
				topPanel.AutoSize = true;
				topPanel.Controls.Add(_replaceCopyrightCheckbox);

				this.Controls.Add(topPanel);
				this.Size = new System.Drawing.Size(this.Width, this.Height + topPanel.Height);
				MetadataControl.BringToFront();
				topPanel.ResumeLayout(false);
				topPanel.PerformLayout();
				this.ResumeLayout(false);
			}
		}

		private void ReplaceCopyrightCheckboxChanged(Object sender, EventArgs e)
		{
			MetadataControl.Enabled = _replaceCopyrightCheckbox.Checked;
		}

		protected override void _minimallyCompleteCheckTimer_Tick(object sender, EventArgs e)
		{
			if (_replaceCopyrightCheckbox == null || _replaceCopyrightCheckbox.Checked)
				base._minimallyCompleteCheckTimer_Tick(sender, e);
			else
				OkButton.Enabled = true;
		}

		public bool ReplaceOriginalCopyright
		{
			get { return (_replaceCopyrightCheckbox != null) ? _replaceCopyrightCheckbox.Checked : true; }
			set
			{
				if (_replaceCopyrightCheckbox != null)
				{
					_replaceCopyrightCheckbox.Checked = value;
					MetadataControl.Enabled = value;
				}
			}
		}
	}
}
