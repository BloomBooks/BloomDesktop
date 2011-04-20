using Bloom.ToPalaso;

namespace Bloom
{
	partial class WelcomeDialog
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WelcomeDialog));
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this._welcomeControl = new Bloom.ToPalaso.WelcomeControl();
            this._broughtToYouBy = new System.Windows.Forms.LinkLabel();
            this._versionInfo = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.Transparent;
            this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
            this.pictureBox1.Image = global::Bloom.Properties.Resources.WelcomeHeader;
            this.pictureBox1.InitialImage = null;
            this.pictureBox1.Location = new System.Drawing.Point(0, 1);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(895, 69);
            this.pictureBox1.TabIndex = 10;
            this.pictureBox1.TabStop = false;
            // 
            // _welcomeControl
            // 
            this._welcomeControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this._welcomeControl.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this._welcomeControl.BackColor = System.Drawing.Color.White;
            this._welcomeControl.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._welcomeControl.Location = new System.Drawing.Point(0, 76);
            this._welcomeControl.Name = "_welcomeControl";
            this._welcomeControl.Size = new System.Drawing.Size(768, 237);
            this._welcomeControl.TabIndex = 0;
            // 
            // _broughtToYouBy
            // 
            this._broughtToYouBy.AccessibleRole = System.Windows.Forms.AccessibleRole.None;
            this._broughtToYouBy.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this._broughtToYouBy.AutoSize = true;
            this._broughtToYouBy.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this._broughtToYouBy.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this._broughtToYouBy.LinkArea = new System.Windows.Forms.LinkArea(53, 18);
            this._broughtToYouBy.Location = new System.Drawing.Point(33, 284);
            this._broughtToYouBy.Name = "_broughtToYouBy";
            this._broughtToYouBy.Size = new System.Drawing.Size(362, 20);
            this._broughtToYouBy.TabIndex = 11;
            this._broughtToYouBy.TabStop = true;
            this._broughtToYouBy.Text = "Bloom is brought to you by SIL International.  Visit the Bloom web site.\r\n";
            this._broughtToYouBy.UseCompatibleTextRendering = true;
            this._broughtToYouBy.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._broughtToYouBy_LinkClicked);
            // 
            // _versionInfo
            // 
            this._versionInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._versionInfo.AutoSize = true;
            this._versionInfo.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this._versionInfo.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this._versionInfo.Location = new System.Drawing.Point(577, 291);
            this._versionInfo.Name = "_versionInfo";
            this._versionInfo.Size = new System.Drawing.Size(70, 13);
            this._versionInfo.TabIndex = 12;
            this._versionInfo.Text = "Version Info";
            // 
            // WelcomeDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(768, 313);
            this.Controls.Add(this._versionInfo);
            this.Controls.Add(this._broughtToYouBy);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this._welcomeControl);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "WelcomeDialog";
            this.Text = "Bloom Libraries";
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

		private WelcomeControl _welcomeControl;
		private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.LinkLabel _broughtToYouBy;
        private System.Windows.Forms.Label _versionInfo;

	}
}