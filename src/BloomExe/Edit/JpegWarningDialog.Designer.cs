namespace Bloom.Edit
{
	partial class JpegWarningDialog
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
			if(disposing && (components != null))
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(JpegWarningDialog));
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._photographRadioButton = new System.Windows.Forms.RadioButton();
			this._cancelRadioButton = new System.Windows.Forms.RadioButton();
			this._okButton = new System.Windows.Forms.Button();
			this._warningText = new System.Windows.Forms.TextBox();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "EditTab.JpegWarningDialog";
			// 
			// _photographRadioButton
			// 
			this._photographRadioButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._photographRadioButton.AutoSize = true;
			this._photographRadioButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._photographRadioButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._photographRadioButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._photographRadioButton, "EditTab.JpegWarningDialog.Photograph");
			this._photographRadioButton.Location = new System.Drawing.Point(12, 213);
			this._photographRadioButton.Name = "_photographRadioButton";
			this._photographRadioButton.Size = new System.Drawing.Size(120, 19);
			this._photographRadioButton.TabIndex = 0;
			this._photographRadioButton.Text = "Use the JPEG file";
			this._photographRadioButton.UseVisualStyleBackColor = true;
			// 
			// _cancelRadioButton
			// 
			this._cancelRadioButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._cancelRadioButton.AutoSize = true;
			this._cancelRadioButton.Checked = true;
			this._cancelRadioButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._cancelRadioButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._cancelRadioButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._cancelRadioButton, "EditTab.JpegWarningDialog.DoNotUse");
			this._cancelRadioButton.Location = new System.Drawing.Point(12, 235);
			this._cancelRadioButton.Name = "_cancelRadioButton";
			this._cancelRadioButton.Size = new System.Drawing.Size(123, 19);
			this._cancelRadioButton.TabIndex = 1;
			this._cancelRadioButton.TabStop = true;
			this._cancelRadioButton.Text = "Cancel this import";
			this._cancelRadioButton.UseVisualStyleBackColor = true;
			// 
			// _okButton
			// 
			this._okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._okButton.AutoSize = true;
			this._okButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._okButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._okButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._okButton, "");
			this._L10NSharpExtender.SetLocalizingId(this._okButton, "Common.OK");
			this._okButton.Location = new System.Drawing.Point(484, 257);
			this._okButton.Name = "_okButton";
			this._okButton.Size = new System.Drawing.Size(86, 26);
			this._okButton.TabIndex = 3;
			this._okButton.Text = "OK";
			this._okButton.UseVisualStyleBackColor = true;
			this._okButton.Click += new System.EventHandler(this._okButton_Click);
			// 
			// _warningText
			// 
			this._warningText.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._warningText.BackColor = System.Drawing.SystemColors.Control;
			this._warningText.Font = new System.Drawing.Font("Segoe UI", 9F);
			this._L10NSharpExtender.SetLocalizableToolTip(this._warningText, null);
			this._L10NSharpExtender.SetLocalizationComment(this._warningText, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._warningText, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._warningText, "notused");
			this._warningText.Location = new System.Drawing.Point(12, 12);
			this._warningText.Multiline = true;
			this._warningText.Name = "_warningText";
			this._warningText.ReadOnly = true;
			this._warningText.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this._warningText.Size = new System.Drawing.Size(558, 195);
			this._warningText.TabIndex = 6;
			this._warningText.Text = "This is set in code.";
			// 
			// JpegWarningDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(582, 295);
			this.Controls.Add(this._warningText);
			this.Controls.Add(this._okButton);
			this.Controls.Add(this._cancelRadioButton);
			this.Controls.Add(this._photographRadioButton);
			this.Icon = global::Bloom.Properties.Resources.BloomIcon;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "EditTab.JpegWarningDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "JpegWarningDialog";
			this.Text = "JPEG Warning";
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.RadioButton _photographRadioButton;
		private System.Windows.Forms.RadioButton _cancelRadioButton;
		private System.Windows.Forms.Button _okButton;
		private System.Windows.Forms.TextBox _warningText;
	}
}