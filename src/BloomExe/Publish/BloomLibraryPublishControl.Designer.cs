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
			this.label2 = new System.Windows.Forms.Label();
			this._uploadedByTextBox = new System.Windows.Forms.TextBox();
			this._authorTextBox = new System.Windows.Forms.TextBox();
			this.label3 = new System.Windows.Forms.Label();
			this.label4 = new System.Windows.Forms.Label();
			this.label5 = new System.Windows.Forms.Label();
			this._licenseImageBox = new System.Windows.Forms.PictureBox();
			this._licenseNotesLabel = new System.Windows.Forms.Label();
			this.label7 = new System.Windows.Forms.Label();
			this.label8 = new System.Windows.Forms.Label();
			this._ccDescriptionButton = new System.Windows.Forms.Button();
			this.label6 = new System.Windows.Forms.Label();
			this._titleLabel = new System.Windows.Forms.Label();
			this._copyrightLabel = new System.Windows.Forms.Label();
			this.label9 = new System.Windows.Forms.Label();
			this._languagesLabel = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._licenseImageBox)).BeginInit();
			this.SuspendLayout();
			// 
			// _loginButton
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._loginButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._loginButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._loginButton, "PublishWeb.LoginButton");
			this._loginButton.Location = new System.Drawing.Point(31, 233);
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
			this._uploadButton.Location = new System.Drawing.Point(252, 233);
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
			this.label1.Location = new System.Drawing.Point(32, 268);
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
			this._progressBox.Location = new System.Drawing.Point(31, 284);
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
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label2, null);
			this._L10NSharpExtender.SetLocalizingId(this.label2, "PublishWeb.UploadedBy");
			this.label2.Location = new System.Drawing.Point(32, 182);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(136, 13);
			this.label2.TabIndex = 4;
			this.label2.Text = "Show book as uploaded by";
			// 
			// _uploadedByTextBox
			// 
			this._uploadedByTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._uploadedByTextBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uploadedByTextBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._uploadedByTextBox, "PublishWeb.textBox1");
			this._uploadedByTextBox.Location = new System.Drawing.Point(194, 179);
			this._uploadedByTextBox.Name = "_uploadedByTextBox";
			this._uploadedByTextBox.Size = new System.Drawing.Size(453, 20);
			this._uploadedByTextBox.TabIndex = 5;
			this._uploadedByTextBox.TextChanged += new System.EventHandler(this._uploadedByTextBox_TextChanged);
			// 
			// _authorTextBox
			// 
			this._authorTextBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._authorTextBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._authorTextBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._authorTextBox, "PublishWeb.textBox1");
			this._authorTextBox.Location = new System.Drawing.Point(194, 153);
			this._authorTextBox.Name = "_authorTextBox";
			this._authorTextBox.Size = new System.Drawing.Size(453, 20);
			this._authorTextBox.TabIndex = 7;
			this._authorTextBox.TextChanged += new System.EventHandler(this._authorTextBox_TextChanged);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label3, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label3, null);
			this._L10NSharpExtender.SetLocalizingId(this.label3, "PublishWeb.Authors");
			this.label3.Location = new System.Drawing.Point(119, 156);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(49, 13);
			this.label3.TabIndex = 6;
			this.label3.Text = "Author(s)";
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label4, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label4, null);
			this._L10NSharpExtender.SetLocalizingId(this.label4, "PublishWeb.Copyright");
			this.label4.Location = new System.Drawing.Point(119, 102);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(51, 13);
			this.label4.TabIndex = 8;
			this.label4.Text = "Copyright";
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label5, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label5, null);
			this._L10NSharpExtender.SetLocalizingId(this.label5, "PublishWeb.License");
			this.label5.Location = new System.Drawing.Point(90, 65);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(80, 13);
			this.label5.TabIndex = 10;
			this.label5.Text = "Usage/License";
			// 
			// _licenseImageBox
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._licenseImageBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._licenseImageBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._licenseImageBox, "PublishWeb.pictureBox1");
			this._licenseImageBox.Location = new System.Drawing.Point(194, 65);
			this._licenseImageBox.Name = "_licenseImageBox";
			this._licenseImageBox.Size = new System.Drawing.Size(86, 33);
			this._licenseImageBox.TabIndex = 12;
			this._licenseImageBox.TabStop = false;
			// 
			// _licenseNotesLabel
			// 
			this._licenseNotesLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._licenseNotesLabel.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this._licenseNotesLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._licenseNotesLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._licenseNotesLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._licenseNotesLabel, "PublishWeb.LicenseNotes");
			this._licenseNotesLabel.Location = new System.Drawing.Point(329, 64);
			this._licenseNotesLabel.Name = "_licenseNotesLabel";
			this._licenseNotesLabel.Size = new System.Drawing.Size(318, 51);
			this._licenseNotesLabel.TabIndex = 13;
			this._licenseNotesLabel.Text = "License Notes";
			// 
			// label7
			// 
			this.label7.AutoSize = true;
			this.label7.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label7, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label7, null);
			this._L10NSharpExtender.SetLocalizingId(this.label7, "PublishWeb.Step1");
			this.label7.Location = new System.Drawing.Point(12, 13);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(127, 13);
			this.label7.TabIndex = 15;
			this.label7.Text = "Step 1: Confirm Metadata";
			// 
			// label8
			// 
			this.label8.AutoSize = true;
			this.label8.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label8, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label8, null);
			this._L10NSharpExtender.SetLocalizingId(this.label8, "PublishWeb.Step2");
			this.label8.Location = new System.Drawing.Point(12, 217);
			this.label8.Name = "label8";
			this.label8.Size = new System.Drawing.Size(78, 13);
			this.label8.TabIndex = 16;
			this.label8.Text = "Step 2: Upload";
			// 
			// _ccDescriptionButton
			// 
			this._ccDescriptionButton.FlatAppearance.BorderSize = 0;
			this._ccDescriptionButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._ccDescriptionButton.Image = global::Bloom.Properties.Resources.info16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this._ccDescriptionButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._ccDescriptionButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._ccDescriptionButton, "PublishWeb.button1");
			this._ccDescriptionButton.Location = new System.Drawing.Point(286, 60);
			this._ccDescriptionButton.Name = "_ccDescriptionButton";
			this._ccDescriptionButton.Size = new System.Drawing.Size(21, 24);
			this._ccDescriptionButton.TabIndex = 17;
			this._ccDescriptionButton.UseVisualStyleBackColor = true;
			this._ccDescriptionButton.Click += new System.EventHandler(this._ccDescriptionButton_Click);
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label6, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label6, null);
			this._L10NSharpExtender.SetLocalizingId(this.label6, "PublishWeb.Title");
			this.label6.Location = new System.Drawing.Point(119, 37);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(27, 13);
			this.label6.TabIndex = 18;
			this.label6.Text = "Title";
			// 
			// _titleLabel
			// 
			this._titleLabel.AutoSize = true;
			this._titleLabel.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this._titleLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._titleLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._titleLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._titleLabel, "PublishWeb.BloomLibraryPublishControl._titleLabel");
			this._titleLabel.Location = new System.Drawing.Point(191, 37);
			this._titleLabel.Name = "_titleLabel";
			this._titleLabel.Size = new System.Drawing.Size(27, 13);
			this._titleLabel.TabIndex = 19;
			this._titleLabel.Text = "Title";
			// 
			// _copyrightLabel
			// 
			this._copyrightLabel.AutoSize = true;
			this._copyrightLabel.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this._copyrightLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._copyrightLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._copyrightLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._copyrightLabel, "PublishWeb.BloomLibraryPublishControl.label9");
			this._copyrightLabel.Location = new System.Drawing.Point(191, 102);
			this._copyrightLabel.Name = "_copyrightLabel";
			this._copyrightLabel.Size = new System.Drawing.Size(51, 13);
			this._copyrightLabel.TabIndex = 20;
			this._copyrightLabel.Text = "Copyright";
			// 
			// label9
			// 
			this.label9.AutoSize = true;
			this.label9.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label9, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label9, null);
			this._L10NSharpExtender.SetLocalizingId(this.label9, "PublishWeb.Languages");
			this.label9.Location = new System.Drawing.Point(119, 130);
			this.label9.Name = "label9";
			this.label9.Size = new System.Drawing.Size(60, 13);
			this.label9.TabIndex = 21;
			this.label9.Text = "Languages";
			// 
			// _languagesLabel
			// 
			this._languagesLabel.AutoSize = true;
			this._languagesLabel.ForeColor = System.Drawing.SystemColors.ControlLightLight;
			this._L10NSharpExtender.SetLocalizableToolTip(this._languagesLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._languagesLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._languagesLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._languagesLabel, "PublishWeb.BloomLibraryPublishControl.label10");
			this._languagesLabel.Location = new System.Drawing.Point(191, 130);
			this._languagesLabel.Name = "_languagesLabel";
			this._languagesLabel.Size = new System.Drawing.Size(36, 13);
			this._languagesLabel.TabIndex = 22;
			this._languagesLabel.Text = "Langs";
			// 
			// BloomLibraryPublishControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._languagesLabel);
			this.Controls.Add(this.label9);
			this.Controls.Add(this._copyrightLabel);
			this.Controls.Add(this._titleLabel);
			this.Controls.Add(this.label6);
			this.Controls.Add(this._ccDescriptionButton);
			this.Controls.Add(this.label8);
			this.Controls.Add(this.label7);
			this.Controls.Add(this._licenseNotesLabel);
			this.Controls.Add(this._licenseImageBox);
			this.Controls.Add(this.label5);
			this.Controls.Add(this.label4);
			this.Controls.Add(this._authorTextBox);
			this.Controls.Add(this.label3);
			this.Controls.Add(this._uploadedByTextBox);
			this.Controls.Add(this.label2);
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
			((System.ComponentModel.ISupportInitialize)(this._licenseImageBox)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Button _loginButton;
		private System.Windows.Forms.Button _uploadButton;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox _progressBox;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox _uploadedByTextBox;
		private System.Windows.Forms.TextBox _authorTextBox;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.PictureBox _licenseImageBox;
		private System.Windows.Forms.Label _licenseNotesLabel;
		private System.Windows.Forms.Label label7;
		private System.Windows.Forms.Label label8;
		private System.Windows.Forms.Button _ccDescriptionButton;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.Label _titleLabel;
		private System.Windows.Forms.Label _copyrightLabel;
		private System.Windows.Forms.Label label9;
		private System.Windows.Forms.Label _languagesLabel;
	}
}
