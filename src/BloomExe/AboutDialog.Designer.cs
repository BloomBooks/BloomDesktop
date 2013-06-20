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
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this._versionInfo = new System.Windows.Forms.Label();
            this._okButton = new System.Windows.Forms.Button();
            this._browser = new Bloom.Browser();
            this.localizationExtender1 = new Localization.UI.LocalizationExtender(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            this.pictureBox1.Image = global::Bloom.Properties.Resources.LogoForLibraryChoosingDialog;
            this.localizationExtender1.SetLocalizableToolTip(this.pictureBox1, null);
            this.localizationExtender1.SetLocalizationComment(this.pictureBox1, null);
            this.localizationExtender1.SetLocalizingId(this.pictureBox1, "AboutDialog.pictureBox1");
            this.pictureBox1.Location = new System.Drawing.Point(13, 9);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(264, 89);
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // _versionInfo
            // 
            this._versionInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this._versionInfo.AutoSize = true;
            this._versionInfo.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.localizationExtender1.SetLocalizableToolTip(this._versionInfo, null);
            this.localizationExtender1.SetLocalizationComment(this._versionInfo, null);
            this.localizationExtender1.SetLocalizationPriority(this._versionInfo, Localization.LocalizationPriority.NotLocalizable);
            this.localizationExtender1.SetLocalizingId(this._versionInfo, "AboutDialog._versionInfo");
            this._versionInfo.Location = new System.Drawing.Point(12, 417);
            this._versionInfo.Name = "_versionInfo";
            this._versionInfo.Size = new System.Drawing.Size(43, 17);
            this._versionInfo.TabIndex = 1;
            this._versionInfo.Text = "label1";
            // 
            // _okButton
            // 
            this._okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._okButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.localizationExtender1.SetLocalizableToolTip(this._okButton, null);
            this.localizationExtender1.SetLocalizationComment(this._okButton, null);
            this.localizationExtender1.SetLocalizingId(this._okButton, "Common.OKButton");
            this._okButton.Location = new System.Drawing.Point(604, 417);
            this._okButton.Name = "_okButton";
            this._okButton.Size = new System.Drawing.Size(75, 23);
            this._okButton.TabIndex = 3;
            this._okButton.Text = "&OK";
            this._okButton.UseVisualStyleBackColor = true;
            this._okButton.Click += new System.EventHandler(this._okButton_Click);
            // 
            // _browser
            // 
            this._browser.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._browser.BackColor = System.Drawing.Color.DarkGray;
            this.localizationExtender1.SetLocalizableToolTip(this._browser, null);
            this.localizationExtender1.SetLocalizationComment(this._browser, null);
            this.localizationExtender1.SetLocalizingId(this._browser, "AboutDialog.Browser");
            this._browser.Location = new System.Drawing.Point(13, 97);
            this._browser.Name = "_browser";
            this._browser.Size = new System.Drawing.Size(666, 297);
            this._browser.TabIndex = 2;
            this._browser.Load += new System.EventHandler(this._browser_Load);
            // 
            // localizationExtender1
            // 
            this.localizationExtender1.LocalizationManagerId = "Bloom";
            // 
            // AboutDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.CancelButton = this._okButton;
            this.ClientSize = new System.Drawing.Size(691, 452);
            this.Controls.Add(this._okButton);
            this.Controls.Add(this._browser);
            this.Controls.Add(this._versionInfo);
            this.Controls.Add(this.pictureBox1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.localizationExtender1.SetLocalizableToolTip(this, null);
            this.localizationExtender1.SetLocalizationComment(this, null);
            this.localizationExtender1.SetLocalizationPriority(this, Localization.LocalizationPriority.MediumLow);
            this.localizationExtender1.SetLocalizingId(this, "AboutDialog.AboutDialogWindowTitle");
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "AboutDialog";
            this.Padding = new System.Windows.Forms.Padding(9);
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "About Bloom";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label _versionInfo;
        private Browser _browser;
        private System.Windows.Forms.Button _okButton;
        private Localization.UI.LocalizationExtender localizationExtender1;

    }
}
