namespace Bloom.WebLibraryIntegration
{
	partial class OverwriteWarningDialog
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
            this.label1 = new System.Windows.Forms.Label();
            this._replaceExistingButton = new System.Windows.Forms.Button();
            this._cancelButton = new System.Windows.Forms.Button();
            this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
            this._L10NSharpExtender.SetLocalizingId(this.label1, "PublishTab.Upload.ConfirmReplaceExisting");
            this.label1.Location = new System.Drawing.Point(22, 19);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(217, 105);
            this.label1.TabIndex = 0;
            this.label1.Text = "BloomLibrary.org already has a previous version of this book from you.  If you up" +
    "load it again, it will be replaced with your current version.";
            // 
            // _replaceExistingButton
            // 
            this._replaceExistingButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._replaceExistingButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this._L10NSharpExtender.SetLocalizableToolTip(this._replaceExistingButton, null);
            this._L10NSharpExtender.SetLocalizationComment(this._replaceExistingButton, null);
            this._L10NSharpExtender.SetLocalizingId(this._replaceExistingButton, "OverwriteWarning.ReplaceExistingButton");
            this._replaceExistingButton.Location = new System.Drawing.Point(32, 127);
            this._replaceExistingButton.Name = "_replaceExistingButton";
            this._replaceExistingButton.Size = new System.Drawing.Size(126, 23);
            this._replaceExistingButton.TabIndex = 1;
            this._replaceExistingButton.Text = "Replace Existing";
            this._replaceExistingButton.UseVisualStyleBackColor = true;
            // 
            // _cancelButton
            // 
            this._cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this._L10NSharpExtender.SetLocalizableToolTip(this._cancelButton, null);
            this._L10NSharpExtender.SetLocalizationComment(this._cancelButton, null);
            this._L10NSharpExtender.SetLocalizingId(this._cancelButton, "Common.CancelButton");
            this._cancelButton.Location = new System.Drawing.Point(164, 127);
            this._cancelButton.Name = "_cancelButton";
            this._cancelButton.Size = new System.Drawing.Size(75, 23);
            this._cancelButton.TabIndex = 2;
            this._cancelButton.Text = "&Cancel";
            this._cancelButton.UseVisualStyleBackColor = true;
            // 
            // _L10NSharpExtender
            // 
            this._L10NSharpExtender.LocalizationManagerId = "Bloom";
            this._L10NSharpExtender.PrefixForNewItems = "OverwriteWarning";
            // 
            // OverwriteWarningDialog
            // 
            this.AcceptButton = this._replaceExistingButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this._cancelButton;
            this.ClientSize = new System.Drawing.Size(258, 162);
            this.ControlBox = false;
            this.Controls.Add(this._cancelButton);
            this.Controls.Add(this._replaceExistingButton);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this._L10NSharpExtender.SetLocalizableToolTip(this, null);
            this._L10NSharpExtender.SetLocalizationComment(this, null);
            this._L10NSharpExtender.SetLocalizingId(this, "OverwriteWarning.WindowTitle");
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "OverwriteWarningDialog";
            this.ShowIcon = false;
            this.Text = "Notice";
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Button _replaceExistingButton;
		private System.Windows.Forms.Button _cancelButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}