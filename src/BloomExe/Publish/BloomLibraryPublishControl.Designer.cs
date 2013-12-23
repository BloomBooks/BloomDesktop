namespace Bloom.Publish
{
	partial class BloomLibraryPublishControl
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

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this._loginButton = new System.Windows.Forms.Button();
			this._uploadButton = new System.Windows.Forms.Button();
			this.label1 = new System.Windows.Forms.Label();
			this._progressBox = new System.Windows.Forms.TextBox();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _loginButton
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._loginButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._loginButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._loginButton, "PublishWeb.LoginButton");
			this._loginButton.Location = new System.Drawing.Point(38, 62);
			this._loginButton.Name = "_loginButton";
			this._loginButton.Size = new System.Drawing.Size(173, 23);
			this._loginButton.TabIndex = 0;
			this._loginButton.Text = "Login to BloomLibrary.org";
			this._loginButton.UseVisualStyleBackColor = true;
			this._loginButton.Click += new System.EventHandler(this._loginButton_Click);
			// 
			// _uploadButton
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._uploadButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uploadButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._uploadButton, "PublishWeb.UploadButton");
			this._uploadButton.Location = new System.Drawing.Point(264, 62);
			this._uploadButton.Name = "_uploadButton";
			this._uploadButton.Size = new System.Drawing.Size(101, 23);
			this._uploadButton.TabIndex = 1;
			this._uploadButton.Text = "Upload Book";
			this._uploadButton.UseVisualStyleBackColor = true;
			this._uploadButton.Click += new System.EventHandler(this._uploadButton_Click);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "PublishWeb.UploadProgress");
			this.label1.Location = new System.Drawing.Point(42, 112);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(85, 13);
			this.label1.TabIndex = 2;
			this.label1.Text = "Upload Progress";
			// 
			// _progressBox
			// 
			this._progressBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._progressBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._progressBox, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._progressBox, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._progressBox, "PublishWeb.BloomLibraryPublishControl._progressBox");
			this._progressBox.Location = new System.Drawing.Point(43, 145);
			this._progressBox.Multiline = true;
			this._progressBox.Name = "_progressBox";
			this._progressBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this._progressBox.Size = new System.Drawing.Size(616, 175);
			this._progressBox.TabIndex = 3;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "PublishWeb";
			// 
			// BloomLibraryPublishControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._progressBox);
			this.Controls.Add(this.label1);
			this.Controls.Add(this._uploadButton);
			this.Controls.Add(this._loginButton);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "PublishWeb.BloomLibraryPublishControl.BloomLibraryPublishControl");
			this.Name = "BloomLibraryPublishControl";
			this.Size = new System.Drawing.Size(694, 472);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Button _loginButton;
		private System.Windows.Forms.Button _uploadButton;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox _progressBox;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}
