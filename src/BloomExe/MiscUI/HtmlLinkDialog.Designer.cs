namespace Bloom.MiscUI
{
	partial class HtmlLinkDialog
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
			this.htmlLabel = new SIL.Windows.Forms.Widgets.HtmlLabel();
			this.okButton = new System.Windows.Forms.Button();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// htmlLabel
			// 
			this.htmlLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.htmlLabel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.htmlLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.htmlLabel.HTML = "Need to set the property \"htmlLabel.HTML\"";
			this._L10NSharpExtender.SetLocalizableToolTip(this.htmlLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this.htmlLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.htmlLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this.htmlLabel.Location = new System.Drawing.Point(8, 8);
			this.htmlLabel.Margin = new System.Windows.Forms.Padding(0);
			this.htmlLabel.MinimumSize = new System.Drawing.Size(368, 57);
			this.htmlLabel.Name = "htmlLabel";
			this.htmlLabel.Size = new System.Drawing.Size(368, 57);
			this.htmlLabel.TabIndex = 0;
			// 
			// okButton
			// 
			this.okButton.Anchor = System.Windows.Forms.AnchorStyles.Bottom;
			this.okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
			this._L10NSharpExtender.SetLocalizableToolTip(this.okButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this.okButton, null);
			this._L10NSharpExtender.SetLocalizingId(this.okButton, "Common.OKButton");
			this.okButton.Location = new System.Drawing.Point(154, 75);
			this.okButton.Name = "okButton";
			this.okButton.Size = new System.Drawing.Size(76, 23);
			this.okButton.TabIndex = 1;
			this.okButton.Text = "&OK";
			this.okButton.UseVisualStyleBackColor = true;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "HtmlMessageBox";
			// 
			// HtmlLinkDialog
			// 
			this.AcceptButton = this.okButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(384, 104);
			this.Controls.Add(this.okButton);
			this.Controls.Add(this.htmlLabel);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizationPriority(this, L10NSharp.LocalizationPriority.NotLocalizable);
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.MinimumSize = new System.Drawing.Size(400, 143);
			this.Name = "HtmlLinkDialog";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.Text = "Need to set the property Text (window title)";
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private SIL.Windows.Forms.Widgets.HtmlLabel htmlLabel;
		private System.Windows.Forms.Button okButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}
