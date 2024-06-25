namespace Bloom
{
    partial class SplashScreen
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SplashScreen));
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this._fadeOutTimer = new System.Windows.Forms.Timer(this.components);
            this._longVersionInfo = new System.Windows.Forms.Label();
            this._feedbackStatusLabel = new System.Windows.Forms.Label();
            this._copyrightlabel = new System.Windows.Forms.Label();
            this._shortVersionLabel = new System.Windows.Forms.Label();
            this.pictureBox2 = new System.Windows.Forms.PictureBox();
            this._channelLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).BeginInit();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            this.pictureBox1.BackColor = System.Drawing.Color.Transparent;
            this.pictureBox1.Image = global::Bloom.Properties.Resources.Bloom_For_Splash;
            this.pictureBox1.Location = new System.Drawing.Point(81, 85);
            this.pictureBox1.Margin = new System.Windows.Forms.Padding(0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(476, 116);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // _fadeOutTimer
            // 
            this._fadeOutTimer.Tick += new System.EventHandler(this._fadeOutTimer_Tick);
            // 
            // _longVersionInfo
            // 
            this._longVersionInfo.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this._longVersionInfo.AutoSize = true;
            this._longVersionInfo.BackColor = System.Drawing.Color.Transparent;
            this._longVersionInfo.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._longVersionInfo.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(69)))), ((int)(((byte)(161)))), ((int)(((byte)(255)))));
            this._longVersionInfo.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this._longVersionInfo.Location = new System.Drawing.Point(73, 365);
            this._longVersionInfo.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this._longVersionInfo.Name = "_longVersionInfo";
            this._longVersionInfo.Size = new System.Drawing.Size(107, 25);
            this._longVersionInfo.TabIndex = 13;
            this._longVersionInfo.Text = "Version Info";
            // 
            // _feedbackStatusLabel
            // 
            this._feedbackStatusLabel.Anchor = System.Windows.Forms.AnchorStyles.None;
            this._feedbackStatusLabel.AutoSize = true;
            this._feedbackStatusLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._feedbackStatusLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(69)))), ((int)(((byte)(161)))), ((int)(((byte)(255)))));
            this._feedbackStatusLabel.Location = new System.Drawing.Point(73, 318);
            this._feedbackStatusLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this._feedbackStatusLabel.Name = "_feedbackStatusLabel";
            this._feedbackStatusLabel.Size = new System.Drawing.Size(161, 25);
            this._feedbackStatusLabel.TabIndex = 17;
            this._feedbackStatusLabel.Text = "Feedback Disabled";
            // 
            // _copyrightlabel
            // 
            this._copyrightlabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this._copyrightlabel.AutoSize = true;
            this._copyrightlabel.BackColor = System.Drawing.Color.Transparent;
            this._copyrightlabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._copyrightlabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(69)))), ((int)(((byte)(161)))), ((int)(((byte)(255)))));
            this._copyrightlabel.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this._copyrightlabel.Location = new System.Drawing.Point(73, 388);
            this._copyrightlabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this._copyrightlabel.Name = "_copyrightlabel";
            this._copyrightlabel.Size = new System.Drawing.Size(255, 25);
            this._copyrightlabel.TabIndex = 19;
            this._copyrightlabel.Text = "© 2011-{year} SIL International";
            this._copyrightlabel.Click += new System.EventHandler(this.label2_Click);
            // 
            // _shortVersionLabel
            // 
            this._shortVersionLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this._shortVersionLabel.AutoSize = true;
            this._shortVersionLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._shortVersionLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(69)))), ((int)(((byte)(161)))), ((int)(((byte)(255)))));
            this._shortVersionLabel.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this._shortVersionLabel.Location = new System.Drawing.Point(73, 342);
            this._shortVersionLabel.Margin = new System.Windows.Forms.Padding(4, 0, 0, 0);
            this._shortVersionLabel.Name = "_shortVersionLabel";
            this._shortVersionLabel.Size = new System.Drawing.Size(80, 25);
            this._shortVersionLabel.TabIndex = 23;
            this._shortVersionLabel.Text = "19.8.710";
            this._shortVersionLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // pictureBox2
            // 
            this.pictureBox2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.pictureBox2.Image = ((System.Drawing.Image)(resources.GetObject("pictureBox2.Image")));
            this.pictureBox2.Location = new System.Drawing.Point(373, 318);
            this.pictureBox2.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.pictureBox2.Name = "pictureBox2";
            this.pictureBox2.Size = new System.Drawing.Size(171, 95);
            this.pictureBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.pictureBox2.TabIndex = 0;
            this.pictureBox2.TabStop = false;
            // 
            // _channelLabel
            // 
            this._channelLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._channelLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(69)))), ((int)(((byte)(161)))), ((int)(((byte)(255)))));
            this._channelLabel.Location = new System.Drawing.Point(260, 201);
            this._channelLabel.Margin = new System.Windows.Forms.Padding(0);
            this._channelLabel.Name = "_channelLabel";
            this._channelLabel.RightToLeft = System.Windows.Forms.RightToLeft.No;
            this._channelLabel.Size = new System.Drawing.Size(267, 37);
            this._channelLabel.TabIndex = 25;
            this._channelLabel.Text = "channel";
            this._channelLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            // 
            // SplashScreen
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(50)))), ((int)(((byte)(50)))));
            this.ClientSize = new System.Drawing.Size(618, 475);
            this.ControlBox = false;
            this.Controls.Add(this._shortVersionLabel);
            this.Controls.Add(this._channelLabel);
            this.Controls.Add(this.pictureBox2);
            this.Controls.Add(this.pictureBox1);
            this.Controls.Add(this._copyrightlabel);
            this.Controls.Add(this._feedbackStatusLabel);
            this.Controls.Add(this._longVersionInfo);
            this.Cursor = System.Windows.Forms.Cursors.AppStarting;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.MinimizeBox = false;
            this.Name = "SplashScreen";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Bloom";
            this.Load += new System.EventHandler(this.SplashScreen_Load);
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.SplashScreen_Paint);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox2)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

		private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Timer _fadeOutTimer;
		private System.Windows.Forms.Label _longVersionInfo;
		private System.Windows.Forms.Label _feedbackStatusLabel;
		private System.Windows.Forms.Label _copyrightlabel;
		private System.Windows.Forms.Label _shortVersionLabel;
		private System.Windows.Forms.PictureBox pictureBox2;
		private System.Windows.Forms.Label _channelLabel;
    }
}
