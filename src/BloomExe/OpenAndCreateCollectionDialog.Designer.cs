using Bloom.ToPalaso;

namespace Bloom
{
	partial class OpenAndCreateCollectionDialog
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OpenAndCreateCollectionDialog));
			this._broughtToYouBy = new System.Windows.Forms.LinkLabel();
			this._versionInfo = new System.Windows.Forms.Label();
			this.pictureBox1 = new System.Windows.Forms.PictureBox();
			this._openAndCreateControl = new Bloom.ToPalaso.WelcomeControl();
			((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
			this.SuspendLayout();
			// 
			// _broughtToYouBy
			// 
			this._broughtToYouBy.AccessibleRole = System.Windows.Forms.AccessibleRole.None;
			this._broughtToYouBy.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._broughtToYouBy.AutoSize = true;
			this._broughtToYouBy.Font = new System.Drawing.Font("Segoe UI", 8.25F);
			this._broughtToYouBy.ImeMode = System.Windows.Forms.ImeMode.NoControl;
			this._broughtToYouBy.LinkArea = new System.Windows.Forms.LinkArea(53, 18);
			this._broughtToYouBy.Location = new System.Drawing.Point(33, 354);
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
			this._versionInfo.Location = new System.Drawing.Point(577, 361);
			this._versionInfo.Name = "_versionInfo";
			this._versionInfo.Size = new System.Drawing.Size(70, 13);
			this._versionInfo.TabIndex = 12;
			this._versionInfo.Text = "Version Info";
			// 
			// pictureBox1
			// 
			this.pictureBox1.BackColor = System.Drawing.Color.Transparent;
			this.pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.None;
			this.pictureBox1.Image = global::Bloom.Properties.Resources.LogoForLibraryChoosingDialog;
			this.pictureBox1.InitialImage = null;
			this.pictureBox1.Location = new System.Drawing.Point(33, 12);
			this.pictureBox1.Name = "pictureBox1";
			this.pictureBox1.Size = new System.Drawing.Size(383, 91);
			this.pictureBox1.TabIndex = 10;
			this.pictureBox1.TabStop = false;
			// 
			// _welcomeControl
			// 
			this._openAndCreateControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._openAndCreateControl.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._openAndCreateControl.BackColor = System.Drawing.Color.White;
			this._openAndCreateControl.Font = new System.Drawing.Font("Segoe UI", 9F);
			this._openAndCreateControl.Location = new System.Drawing.Point(0, 105);
			this._openAndCreateControl.Name = "_openAndCreateControl";
			this._openAndCreateControl.Size = new System.Drawing.Size(768, 278);
			this._openAndCreateControl.TabIndex = 0;
			// 
			// WelcomeDialog
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.White;
			this.ClientSize = new System.Drawing.Size(768, 383);
			this.Controls.Add(this._versionInfo);
			this.Controls.Add(this._broughtToYouBy);
			this.Controls.Add(this.pictureBox1);
			this.Controls.Add(this._openAndCreateControl);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "WelcomeDialog";
			this.Text = "Open/Create Collections";
			((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private WelcomeControl _openAndCreateControl;
        private System.Windows.Forms.LinkLabel _broughtToYouBy;
        private System.Windows.Forms.Label _versionInfo;
		private System.Windows.Forms.PictureBox pictureBox1;

	}
}