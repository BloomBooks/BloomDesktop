namespace Bloom.Edit
{
    partial class ConfigurationDialog
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
			this._browser = new Bloom.Browser();
			this._cancelButton = new System.Windows.Forms.Button();
			this._okButton = new System.Windows.Forms.Button();
			this.localizationExtender1 = new Localization.UI.LocalizationExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).BeginInit();
			this.SuspendLayout();
			// 
			// _browser
			// 
			this._browser.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._browser.BackColor = System.Drawing.Color.DarkGray;
			this.localizationExtender1.SetLocalizableToolTip(this._browser, null);
			this.localizationExtender1.SetLocalizationComment(this._browser, null);
			this.localizationExtender1.SetLocalizingId(this._browser, "ConfigurationDialog.Browser");
			this._browser.Location = new System.Drawing.Point(-1, 0);
			this._browser.Name = "_browser";
			this._browser.Size = new System.Drawing.Size(670, 438);
			this._browser.TabIndex = 0;
			// 
			// _cancelButton
			// 
			this._cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.localizationExtender1.SetLocalizableToolTip(this._cancelButton, null);
			this.localizationExtender1.SetLocalizationComment(this._cancelButton, null);
			this.localizationExtender1.SetLocalizingId(this._cancelButton, "Common.CancelButton");
			this._cancelButton.Location = new System.Drawing.Point(575, 447);
			this._cancelButton.Name = "_cancelButton";
			this._cancelButton.Size = new System.Drawing.Size(75, 23);
			this._cancelButton.TabIndex = 1;
			this._cancelButton.Text = "&Cancel";
			this._cancelButton.UseVisualStyleBackColor = true;
			this._cancelButton.Click += new System.EventHandler(this._cancelButton_Click);
			// 
			// _okButton
			// 
			this._okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.localizationExtender1.SetLocalizableToolTip(this._okButton, null);
			this.localizationExtender1.SetLocalizationComment(this._okButton, null);
			this.localizationExtender1.SetLocalizingId(this._okButton, "Common.OKButton");
			this._okButton.Location = new System.Drawing.Point(484, 447);
			this._okButton.Name = "_okButton";
			this._okButton.Size = new System.Drawing.Size(75, 23);
			this._okButton.TabIndex = 2;
			this._okButton.Text = "&OK";
			this._okButton.UseVisualStyleBackColor = true;
			this._okButton.Click += new System.EventHandler(this._okButton_Click);
			// 
			// localizationExtender1
			// 
			this.localizationExtender1.LocalizationManagerId = "Bloom";
			// 
			// ConfigurationDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this._cancelButton;
			this.ClientSize = new System.Drawing.Size(669, 482);
			this.ControlBox = false;
			this.Controls.Add(this._okButton);
			this.Controls.Add(this._cancelButton);
			this.Controls.Add(this._browser);
			this.localizationExtender1.SetLocalizableToolTip(this, null);
			this.localizationExtender1.SetLocalizationComment(this, "This is the dialog used to set up the wall calendar");
			this.localizationExtender1.SetLocalizationPriority(this, Localization.LocalizationPriority.Low);
			this.localizationExtender1.SetLocalizingId(this, "WallCalendar.TitleOfSetupDialog");
			this.MinimizeBox = false;
			this.Name = "ConfigurationDialog";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Setup";
			this.Load += new System.EventHandler(this.ConfigurationDialog_Load);
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).EndInit();
			this.ResumeLayout(false);

        }

        #endregion

        private Browser _browser;
        private System.Windows.Forms.Button _cancelButton;
        private System.Windows.Forms.Button _okButton;
		private Localization.UI.LocalizationExtender localizationExtender1;
    }
}