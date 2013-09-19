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
			this.pictureBox3 = new System.Windows.Forms.PictureBox();
			this.label2 = new System.Windows.Forms.Label();
			this.flowLayoutPanel1 = new System.Windows.Forms.FlowLayoutPanel();
			this._shortVersionLabel = new System.Windows.Forms.Label();
			this.label1 = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this.pictureBox3)).BeginInit();
			this.flowLayoutPanel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// pictureBox1
			// 
			this.pictureBox1.Image = global::Bloom.Properties.Resources.LogoForSplashScreen;
			this.pictureBox1.Location = new System.Drawing.Point(35, 43);
			this.pictureBox1.Name = "pictureBox1";
			this.pictureBox1.Size = new System.Drawing.Size(302, 116);
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
			this._longVersionInfo.Font = new System.Drawing.Font("Segoe UI", 9F);
			this._longVersionInfo.ForeColor = System.Drawing.Color.WhiteSmoke;
			this._longVersionInfo.ImeMode = System.Windows.Forms.ImeMode.NoControl;
			this._longVersionInfo.Location = new System.Drawing.Point(32, 221);
			this._longVersionInfo.Name = "_longVersionInfo";
			this._longVersionInfo.Size = new System.Drawing.Size(70, 15);
			this._longVersionInfo.TabIndex = 13;
			this._longVersionInfo.Text = "Version Info";
			// 
			// _feedbackStatusLabel
			// 
			this._feedbackStatusLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._feedbackStatusLabel.AutoSize = true;
			this._feedbackStatusLabel.Font = new System.Drawing.Font("Segoe UI", 9F);
			this._feedbackStatusLabel.ForeColor = System.Drawing.Color.Gold;
			this._feedbackStatusLabel.Location = new System.Drawing.Point(32, 9);
			this._feedbackStatusLabel.Name = "_feedbackStatusLabel";
			this._feedbackStatusLabel.Size = new System.Drawing.Size(105, 15);
			this._feedbackStatusLabel.TabIndex = 17;
			this._feedbackStatusLabel.Text = "Feedback Disabled";
			this._feedbackStatusLabel.Click += new System.EventHandler(this._feedbackStatusLabel_Click);
			// 
			// pictureBox3
			// 
			this.pictureBox3.Image = global::Bloom.Properties.Resources.sil_logo_on_dark_132x127;
			this.pictureBox3.Location = new System.Drawing.Point(214, 165);
			this.pictureBox3.Name = "pictureBox3";
			this.pictureBox3.Size = new System.Drawing.Size(122, 71);
			this.pictureBox3.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this.pictureBox3.TabIndex = 18;
			this.pictureBox3.TabStop = false;
			// 
			// label2
			// 
			this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this.label2.AutoSize = true;
			this.label2.Font = new System.Drawing.Font("Segoe UI", 9F);
			this.label2.ForeColor = System.Drawing.Color.WhiteSmoke;
			this.label2.ImeMode = System.Windows.Forms.ImeMode.NoControl;
			this.label2.Location = new System.Drawing.Point(32, 206);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(162, 15);
			this.label2.TabIndex = 19;
			this.label2.Text = "© 2011-2013 SIL International";
			this.label2.Click += new System.EventHandler(this.label2_Click);
			// 
			// flowLayoutPanel1
			// 
			this.flowLayoutPanel1.Controls.Add(this._shortVersionLabel);
			this.flowLayoutPanel1.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft;
			this.flowLayoutPanel1.Location = new System.Drawing.Point(163, 31);
			this.flowLayoutPanel1.Margin = new System.Windows.Forms.Padding(0);
			this.flowLayoutPanel1.Name = "flowLayoutPanel1";
			this.flowLayoutPanel1.Size = new System.Drawing.Size(157, 34);
			this.flowLayoutPanel1.TabIndex = 22;
			// 
			// _shortVersionLabel
			// 
			this._shortVersionLabel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._shortVersionLabel.AutoSize = true;
			this._shortVersionLabel.Font = new System.Drawing.Font("Segoe UI", 20F);
			this._shortVersionLabel.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(214)))), ((int)(((byte)(86)))), ((int)(((byte)(73)))));
			this._shortVersionLabel.ImeMode = System.Windows.Forms.ImeMode.NoControl;
			this._shortVersionLabel.Location = new System.Drawing.Point(68, 0);
			this._shortVersionLabel.Margin = new System.Windows.Forms.Padding(3, 0, 0, 0);
			this._shortVersionLabel.Name = "_shortVersionLabel";
			this._shortVersionLabel.Size = new System.Drawing.Size(89, 37);
			this._shortVersionLabel.TabIndex = 23;
			this._shortVersionLabel.Text = "9.8.71";
			this._shortVersionLabel.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
			// 
			// label1
			// 
			this.label1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Segoe UI", 9F);
			this.label1.ForeColor = System.Drawing.Color.Gold;
			this.label1.Location = new System.Drawing.Point(309, 49);
			this.label1.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(30, 15);
			this.label1.TabIndex = 23;
			this.label1.Text = "beta";
			// 
			// SplashScreen
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(42)))), ((int)(((byte)(42)))), ((int)(((byte)(42)))));
			this.ClientSize = new System.Drawing.Size(348, 267);
			this.ControlBox = false;
			this.Controls.Add(this.label1);
			this.Controls.Add(this.flowLayoutPanel1);
			this.Controls.Add(this.label2);
			this.Controls.Add(this.pictureBox3);
			this.Controls.Add(this._feedbackStatusLabel);
			this.Controls.Add(this._longVersionInfo);
			this.Controls.Add(this.pictureBox1);
			this.Cursor = System.Windows.Forms.Cursors.AppStarting;
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MinimizeBox = false;
			this.Name = "SplashScreen";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
			this.Text = "Bloom";
			this.Load += new System.EventHandler(this.SplashScreen_Load);
			((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this.pictureBox3)).EndInit();
			this.flowLayoutPanel1.ResumeLayout(false);
			this.flowLayoutPanel1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Timer _fadeOutTimer;
		private System.Windows.Forms.Label _longVersionInfo;
		private System.Windows.Forms.Label _feedbackStatusLabel;
		private System.Windows.Forms.PictureBox pictureBox3;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.FlowLayoutPanel flowLayoutPanel1;
		private System.Windows.Forms.Label _shortVersionLabel;
		private System.Windows.Forms.Label label1;
    }
}