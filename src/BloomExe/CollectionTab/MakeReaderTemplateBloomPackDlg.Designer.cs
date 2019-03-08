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
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MakeReaderTemplateBloomPackDlg));
			this.label1 = new System.Windows.Forms.Label();
			this._willCarrySettingsLabel = new System.Windows.Forms.Label();
			this._bookList = new System.Windows.Forms.ListBox();
			this._btnSaveBloomPack = new System.Windows.Forms.Button();
			this._btnCancel = new System.Windows.Forms.Button();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._confirmationCheckBox = new System.Windows.Forms.CheckBox();
			this._helpButton = new System.Windows.Forms.Button();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "ReaderTemplateBloomPackDialog.IntroLabel");
			this.label1.Location = new System.Drawing.Point(28, 21);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(234, 13);
			this.label1.TabIndex = 0;
			this.label1.Text = "The following books will be made into templates:";
			// 
			// _willCarrySettingsLabel
			// 
			this._willCarrySettingsLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._willCarrySettingsLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._willCarrySettingsLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._willCarrySettingsLabel, "ReaderTemplateBloomPackDialog.ExplanationParagraph");
			this._willCarrySettingsLabel.Location = new System.Drawing.Point(28, 175);
			this._willCarrySettingsLabel.Name = "_willCarrySettingsLabel";
			this._willCarrySettingsLabel.Size = new System.Drawing.Size(326, 132);
			this._willCarrySettingsLabel.TabIndex = 1;
			this._willCarrySettingsLabel.Text = resources.GetString("_willCarrySettingsLabel.Text");
			// 
			// _bookList
			// 
			this._bookList.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._bookList.FormattingEnabled = true;
			this._bookList.Location = new System.Drawing.Point(31, 53);
			this._bookList.Name = "_bookList";
			this._bookList.Size = new System.Drawing.Size(323, 108);
			this._bookList.TabIndex = 2;
			// 
			// _btnSaveBloomPack
			// 
			this._btnSaveBloomPack.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._btnSaveBloomPack.DialogResult = System.Windows.Forms.DialogResult.OK;
			this._L10NSharpExtender.SetLocalizableToolTip(this._btnSaveBloomPack, null);
			this._L10NSharpExtender.SetLocalizationComment(this._btnSaveBloomPack, null);
			this._L10NSharpExtender.SetLocalizingId(this._btnSaveBloomPack, "ReaderTemplateBloomPackDialog.SaveBloomPackButton");
			this._btnSaveBloomPack.Location = new System.Drawing.Point(57, 348);
			this._btnSaveBloomPack.Name = "_btnSaveBloomPack";
			this._btnSaveBloomPack.Size = new System.Drawing.Size(113, 23);
			this._btnSaveBloomPack.TabIndex = 3;
			this._btnSaveBloomPack.Text = "Save Bloom Pack";
			this._btnSaveBloomPack.UseVisualStyleBackColor = true;
			// 
			// _btnCancel
			// 
			this._btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._L10NSharpExtender.SetLocalizableToolTip(this._btnCancel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._btnCancel, null);
			this._L10NSharpExtender.SetLocalizingId(this._btnCancel, "Common.CancelButton");
			this._btnCancel.Location = new System.Drawing.Point(187, 348);
			this._btnCancel.Name = "_btnCancel";
			this._btnCancel.Size = new System.Drawing.Size(75, 23);
			this._btnCancel.TabIndex = 4;
			this._btnCancel.Text = "Cancel";
			this._btnCancel.UseVisualStyleBackColor = true;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "ReaderTemplateBloomPackDialog";
			// 
			// _confirmationCheckBox
			// 
			this._confirmationCheckBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._confirmationCheckBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._confirmationCheckBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._confirmationCheckBox, "ReaderTemplateBloomPackDialog.IUnderstandCheckboxLabel");
			this._confirmationCheckBox.Location = new System.Drawing.Point(31, 310);
			this._confirmationCheckBox.Name = "_confirmationCheckBox";
			this._confirmationCheckBox.Size = new System.Drawing.Size(335, 32);
			this._confirmationCheckBox.TabIndex = 5;
			this._confirmationCheckBox.Text = "I understand what a template is and this is really what I want to do";
			this._confirmationCheckBox.UseVisualStyleBackColor = true;
			this._confirmationCheckBox.CheckedChanged += new System.EventHandler(this._confirmationCheckBox_CheckedChanged);
			// 
			// _helpButton
			// 
			this._helpButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._helpButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._helpButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._helpButton, "ReaderTemplateBloomPackDialog.button1");
			this._helpButton.Location = new System.Drawing.Point(279, 348);
			this._helpButton.Name = "_helpButton";
			this._helpButton.Size = new System.Drawing.Size(75, 23);
			this._helpButton.TabIndex = 6;
			this._helpButton.Text = "Help";
			this._helpButton.UseVisualStyleBackColor = true;
			this._helpButton.Click += new System.EventHandler(this._helpButton_Click);
			// 
			// MakeReaderTemplateBloomPackDlg
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(386, 389);
			this.ControlBox = false;
			this.Controls.Add(this._helpButton);
			this.Controls.Add(this._confirmationCheckBox);
			this.Controls.Add(this._btnCancel);
			this.Controls.Add(this._btnSaveBloomPack);
			this.Controls.Add(this._bookList);
			this.Controls.Add(this._willCarrySettingsLabel);
			this.Controls.Add(this.label1);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "ReaderTemplateBloomPackDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "MakeReaderTemplateBloomPackDlg";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.Text = "Make Reader Template Bloom Pack";
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label _willCarrySettingsLabel;
		private System.Windows.Forms.ListBox _bookList;
		private System.Windows.Forms.Button _btnSaveBloomPack;
		private System.Windows.Forms.Button _btnCancel;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.CheckBox _confirmationCheckBox;
		private System.Windows.Forms.Button _helpButton;
	}
}
