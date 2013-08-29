namespace Bloom
{
    partial class AboutDialog
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
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
			this._buildDate = new System.Windows.Forms.Label();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this.pictureBox2 = new System.Windows.Forms.PictureBox();
			this._versionNumber = new System.Windows.Forms.Label();
			this._browser = new Bloom.Browser();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
			this.SuspendLayout();
			// 
			// _buildDate
			// 
			this._buildDate.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._buildDate.AutoSize = true;
			this._buildDate.Font = new System.Drawing.Font("Segoe UI", 8F);
			this._L10NSharpExtender.SetLocalizableToolTip(this._buildDate, null);
			this._L10NSharpExtender.SetLocalizationComment(this._buildDate, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._buildDate, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._buildDate, "AboutDialog._versionInfo");
			this._buildDate.Location = new System.Drawing.Point(12, 422);
			this._buildDate.Name = "_buildDate";
			this._buildDate.Size = new System.Drawing.Size(38, 13);
			this._buildDate.TabIndex = 1;
			this._buildDate.Text = "label1";
			this._buildDate.Click += new System.EventHandler(this._versionInfo_Click);
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "AboutDialog";
			// 
			// pictureBox2
			// 
			this.pictureBox2.Image = global::Bloom.Properties.Resources.SILLogoBlue132x184;
			this._L10NSharpExtender.SetLocalizableToolTip(this.pictureBox2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.pictureBox2, null);
			this._L10NSharpExtender.SetLocalizingId(this.pictureBox2, "AboutDialog.pictureBox2");
			this.pictureBox2.Location = new System.Drawing.Point(15, 31);
			this.pictureBox2.Name = "pictureBox2";
			this.pictureBox2.Size = new System.Drawing.Size(132, 190);
			this.pictureBox2.TabIndex = 5;
			this.pictureBox2.TabStop = false;
			// 
			// _versionNumber
			// 
			this._versionNumber.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._versionNumber.AutoSize = true;
			this._versionNumber.Font = new System.Drawing.Font("Segoe UI", 8F);
			this._L10NSharpExtender.SetLocalizableToolTip(this._versionNumber, null);
			this._L10NSharpExtender.SetLocalizationComment(this._versionNumber, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._versionNumber, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._versionNumber, "AboutDialog._versionInfo");
			this._versionNumber.Location = new System.Drawing.Point(12, 409);
			this._versionNumber.Name = "_versionNumber";
			this._versionNumber.Size = new System.Drawing.Size(38, 13);
			this._versionNumber.TabIndex = 6;
			this._versionNumber.Text = "label1";
			// 
			// _browser
			// 
			this._browser.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._browser.BackColor = System.Drawing.Color.DarkGray;
			this._L10NSharpExtender.SetLocalizableToolTip(this._browser, null);
			this._L10NSharpExtender.SetLocalizationComment(this._browser, null);
			this._L10NSharpExtender.SetLocalizingId(this._browser, "AboutDialog.Browser");
			this._browser.Location = new System.Drawing.Point(166, 31);
			this._browser.Name = "_browser";
			this._browser.Size = new System.Drawing.Size(407, 408);
			this._browser.TabIndex = 2;
			this._browser.Load += new System.EventHandler(this._browser_Load);
			// 
			// AboutDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.White;
			this.ClientSize = new System.Drawing.Size(585, 451);
			this.Controls.Add(this._versionNumber);
			this.Controls.Add(this.pictureBox2);
			this.Controls.Add(this._browser);
			this.Controls.Add(this._buildDate);
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizationPriority(this, L10NSharp.LocalizationPriority.MediumLow);
			this._L10NSharpExtender.SetLocalizingId(this, "AboutDialog.AboutDialogWindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "AboutDialog";
			this.Padding = new System.Windows.Forms.Padding(9);
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "About Bloom";
			this.Load += new System.EventHandler(this.AboutDialog_Load);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		private System.Windows.Forms.Label _buildDate;
		private Browser _browser;
        private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.PictureBox pictureBox2;
		private System.Windows.Forms.Label _versionNumber;

    }
}
