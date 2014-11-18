namespace Bloom.MiscUI
{
	partial class ReportProblemDialog
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
			this._acceptButton = new System.Windows.Forms.Button();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._linkLabel = new System.Windows.Forms.LinkLabel();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _acceptButton
			// 
			this._acceptButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._acceptButton.AutoSize = true;
			this._acceptButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._L10NSharpExtender.SetLocalizableToolTip(this._acceptButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._acceptButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._acceptButton, "Common.OKButton");
			this._acceptButton.Location = new System.Drawing.Point(309, 110);
			this._acceptButton.Margin = new System.Windows.Forms.Padding(4, 0, 0, 15);
			this._acceptButton.Name = "_acceptButton";
			this._acceptButton.Size = new System.Drawing.Size(75, 26);
			this._acceptButton.TabIndex = 1;
			this._acceptButton.Text = "&OK";
			this._acceptButton.UseVisualStyleBackColor = true;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// _linkLabel
			// 
			this._linkLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._linkLabel.LinkArea = new System.Windows.Forms.LinkArea(0, 0);
			this._L10NSharpExtender.SetLocalizableToolTip(this._linkLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._linkLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._linkLabel, "ReportProblemDialog._linkLabel");
			this._linkLabel.Location = new System.Drawing.Point(12, 19);
			this._linkLabel.Name = "_linkLabel";
			this._linkLabel.Size = new System.Drawing.Size(312, 64);
			this._linkLabel.TabIndex = 2;
			this._linkLabel.Text = "To report a problem, please email issues@bloomlibrary.org.";
			this._linkLabel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._linkLabel_LinkClicked);
			// 
			// ReportProblemDialog
			// 
			this.AcceptButton = this._acceptButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this._acceptButton;
			this.ClientSize = new System.Drawing.Size(393, 147);
			this.Controls.Add(this._linkLabel);
			this.Controls.Add(this._acceptButton);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "ReportProblemDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "ReportProblemDialog";
			this.ShowInTaskbar = false;
			this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
			this.Text = "Report a Problem";
			this.TopMost = true;
			this.Load += new System.EventHandler(this.ReportProblemDialog_Load);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		internal System.Windows.Forms.Button _acceptButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.LinkLabel _linkLabel;
	}
}