namespace Bloom.CollectionTab
{
	partial class MakeReaderTemplateBloomPackDlg
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MakeReaderTemplateBloomPackDlg));
			this.label1 = new System.Windows.Forms.Label();
			this._willCarrySettingsLabel = new System.Windows.Forms.Label();
			this._bookList = new System.Windows.Forms.ListBox();
			this._btnSaveBloomPack = new System.Windows.Forms.Button();
			this._btnCancel = new System.Windows.Forms.Button();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Location = new System.Drawing.Point(28, 21);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(234, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "The following books will be made into templates:";
			// 
			// _willCarrySettingsLabel
			// 
			this._willCarrySettingsLabel.Location = new System.Drawing.Point(28, 170);
			this._willCarrySettingsLabel.Name = "_willCarrySettingsLabel";
			this._willCarrySettingsLabel.Size = new System.Drawing.Size(326, 76);
			this._willCarrySettingsLabel.TabIndex = 1;
			this._willCarrySettingsLabel.Text = resources.GetString("_willCarrySettingsLabel.Text");
			// 
			// _bookList
			// 
			this._bookList.FormattingEnabled = true;
			this._bookList.Location = new System.Drawing.Point(31, 53);
			this._bookList.Name = "_bookList";
			this._bookList.Size = new System.Drawing.Size(260, 108);
			this._bookList.TabIndex = 2;
			// 
			// _btnSaveBloomPack
			// 
			this._btnSaveBloomPack.DialogResult = System.Windows.Forms.DialogResult.OK;
			this._btnSaveBloomPack.Location = new System.Drawing.Point(149, 254);
			this._btnSaveBloomPack.Name = "_btnSaveBloomPack";
			this._btnSaveBloomPack.Size = new System.Drawing.Size(113, 23);
			this._btnSaveBloomPack.TabIndex = 3;
			this._btnSaveBloomPack.Text = "Save BloomPack";
			this._btnSaveBloomPack.UseVisualStyleBackColor = true;
			// 
			// _btnCancel
			// 
			this._btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._btnCancel.Location = new System.Drawing.Point(279, 254);
			this._btnCancel.Name = "_btnCancel";
			this._btnCancel.Size = new System.Drawing.Size(75, 23);
			this._btnCancel.TabIndex = 4;
			this._btnCancel.Text = "Cancel";
			this._btnCancel.UseVisualStyleBackColor = true;
			// 
			// MakeReaderTemplateBloomPackDlg
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(366, 289);
			this.Controls.Add(this._btnCancel);
			this.Controls.Add(this._btnSaveBloomPack);
			this.Controls.Add(this._bookList);
			this.Controls.Add(this._willCarrySettingsLabel);
			this.Controls.Add(this.label1);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "MakeReaderTemplateBloomPackDlg";
			this.Text = "MakeReaderTemplateBloomPackDlg";
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label _willCarrySettingsLabel;
		private System.Windows.Forms.ListBox _bookList;
		private System.Windows.Forms.Button _btnSaveBloomPack;
		private System.Windows.Forms.Button _btnCancel;
	}
}