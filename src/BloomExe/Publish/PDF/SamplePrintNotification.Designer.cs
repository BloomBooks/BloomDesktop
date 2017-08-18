using System.Drawing;
using SIL.Windows.Forms.Widgets;

namespace Bloom.Publish.PDF
{
	partial class SamplePrintNotification
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
			this.Font = SystemFonts.MessageBoxFont;
			this.label1 = new BetterLabel();
			this._stopShowingCheckBox = new System.Windows.Forms.CheckBox();
			this.okButton = new System.Windows.Forms.Button();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// label1
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "SamplePrintNotification.PleaseNotice");
			this.label1.Location = new System.Drawing.Point(13, 13);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(270, 61);
			this.label1.TabIndex = 0;
			this.label1.Text = "Please notice the sample printer settings below. Use them as a guide while you se" +
    "t up the printer.";
			// 
			// _stopShowingCheckBox
			// 
			this._stopShowingCheckBox.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._stopShowingCheckBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._stopShowingCheckBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._stopShowingCheckBox, "SamplePrintNotification.IGetIt");
			this._stopShowingCheckBox.Location = new System.Drawing.Point(13, 78);
			this._stopShowingCheckBox.Name = "_stopShowingCheckBox";
			this._stopShowingCheckBox.Size = new System.Drawing.Size(172, 17);
			this._stopShowingCheckBox.TabIndex = 1;
			this._stopShowingCheckBox.Text = "I get it. Do not show this again.";
			this._stopShowingCheckBox.UseVisualStyleBackColor = true;
			// 
			// okButton
			// 
			this.okButton.DialogResult = System.Windows.Forms.DialogResult.OK;
			this._L10NSharpExtender.SetLocalizableToolTip(this.okButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this.okButton, null);
			this._L10NSharpExtender.SetLocalizingId(this.okButton, "Common.OKButton");
			this.okButton.Location = new System.Drawing.Point(191, 74);
			this.okButton.Name = "okButton";
			this.okButton.Size = new System.Drawing.Size(75, 23);
			this.okButton.TabIndex = 2;
			this.okButton.Text = "&OK";
			this.okButton.UseVisualStyleBackColor = true;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// SamplePrintNotification
			// 
			this.AcceptButton = this.okButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(284, 119);
			this.ControlBox = false;
			this.Controls.Add(this.okButton);
			this.Controls.Add(this._stopShowingCheckBox);
			this.Controls.Add(this.label1);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "SamplePrintNotification.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "SamplePrintNotification";
			this.Text = "Sample Print Settings";
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private BetterLabel label1;
		private System.Windows.Forms.CheckBox _stopShowingCheckBox;
		private System.Windows.Forms.Button okButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}