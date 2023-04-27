namespace Bloom.Registration
{
	partial class LicenseDialog
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		private bool disposed = false;
		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposed)
				return;
			disposed = true;
			if (disposing)
			{
				if (components != null)
					components.Dispose();
				if (_licenseBrowser != null)
				{
					_licenseBrowser.Dispose();
					_licenseBrowser = null;
				}
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LicenseDialog));
			this.l10NSharpExtender1 = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._acceptButton = new System.Windows.Forms.Button();
			((System.ComponentModel.ISupportInitialize)(this.l10NSharpExtender1)).BeginInit();
			this.SuspendLayout();
			// 
			// l10NSharpExtender1
			// 
			this.l10NSharpExtender1.LocalizationManagerId = null;
			this.l10NSharpExtender1.PrefixForNewItems = null;
			// 
			// _acceptButton
			// 
			this._acceptButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._acceptButton.DialogResult = System.Windows.Forms.DialogResult.OK;
			this.l10NSharpExtender1.SetLocalizableToolTip(this._acceptButton, null);
			this.l10NSharpExtender1.SetLocalizationComment(this._acceptButton, null);
			this.l10NSharpExtender1.SetLocalizationPriority(this._acceptButton, L10NSharp.LocalizationPriority.High);
			this.l10NSharpExtender1.SetLocalizingId(this._acceptButton, "LicenseDialog._acceptButton");
			this._acceptButton.Location = new System.Drawing.Point(253, 267);
			this._acceptButton.Name = "_acceptButton";
			this._acceptButton.Size = new System.Drawing.Size(250, 23);
			this._acceptButton.TabIndex = 0;
			this._acceptButton.Text = "I accept the terms of the license agreement";
			this._acceptButton.UseVisualStyleBackColor = true;
			// 
			// LicenseDialog
			// 
			this.AcceptButton = this._acceptButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.SystemColors.Window;
			this.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
			this.ClientSize = new System.Drawing.Size(515, 302);
			this.Controls.Add(this._acceptButton);
			this.Icon = global::Bloom.Properties.Resources.BloomIcon;
			this.l10NSharpExtender1.SetLocalizableToolTip(this, null);
			this.l10NSharpExtender1.SetLocalizationComment(this, null);
			this.l10NSharpExtender1.SetLocalizingId(this, "LicenseDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "LicenseDialog";
			this.Text = "Bloom {0}";
			((System.ComponentModel.ISupportInitialize)(this.l10NSharpExtender1)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Button _acceptButton;
		private L10NSharp.UI.L10NSharpExtender l10NSharpExtender1;
	}
}