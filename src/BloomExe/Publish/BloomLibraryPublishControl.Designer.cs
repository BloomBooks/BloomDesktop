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
			this._uploadButton = new System.Windows.Forms.Button();
			this.label1 = new System.Windows.Forms.Label();
			this._progressBox = new System.Windows.Forms.TextBox();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this.label2 = new System.Windows.Forms.Label();
			this._uploadedByTextBox = new System.Windows.Forms.TextBox();
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
			this._loginLink = new System.Windows.Forms.LinkLabel();
			this._creditsLabel = new System.Windows.Forms.Label();
			this.label10 = new System.Windows.Forms.Label();
			this._summaryBox = new System.Windows.Forms.TextBox();
			this._signUpLink = new System.Windows.Forms.LinkLabel();
			this._optional1 = new System.Windows.Forms.Label();
			this._optional2 = new System.Windows.Forms.Label();
			this._pleaseSetUploadedByLabel = new System.Windows.Forms.Label();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._licenseImageBox)).BeginInit();
			this.SuspendLayout();
			// 
			// _uploadButton
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._uploadButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uploadButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._uploadButton, "PublishWeb.UploadButton");
			this._uploadButton.Location = new System.Drawing.Point(35, 631);
			this._uploadButton.Name = "_uploadButton";
			this._uploadButton.Size = new System.Drawing.Size(101, 23);
			this._uploadButton.TabIndex = 17;
			this._uploadButton.Text = "Upload Book";
			this._uploadButton.UseVisualStyleBackColor = true;
			this._uploadButton.Click += new System.EventHandler(this._uploadButton_Click);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "PublishWeb.UploadProgress");
			this.label1.Location = new System.Drawing.Point(32, 666);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(85, 13);
			this.label1.TabIndex = 19;
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
			this._progressBox.Location = new System.Drawing.Point(35, 682);
			this._progressBox.Multiline = true;
			this._progressBox.Name = "_progressBox";
			this._progressBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
			this._progressBox.Size = new System.Drawing.Size(616, 175);
			this._progressBox.TabIndex = 20;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "PublishWeb";
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label2.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label2, null);
			this._L10NSharpExtender.SetLocalizingId(this.label2, "PublishWeb.UploadedBy");
			this.label2.Location = new System.Drawing.Point(43, 568);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(160, 13);
			this.label2.TabIndex = 14;
			this.label2.Text = "Show book as uploaded by";
			// 
			// _uploadedByTextBox
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._uploadedByTextBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uploadedByTextBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._uploadedByTextBox, "PublishWeb.textBox1");
			this._uploadedByTextBox.Location = new System.Drawing.Point(66, 584);
			this._uploadedByTextBox.Name = "_uploadedByTextBox";
			this._uploadedByTextBox.Size = new System.Drawing.Size(219, 20);
			this._uploadedByTextBox.TabIndex = 15;
			this._uploadedByTextBox.TextChanged += new System.EventHandler(this._uploadedByTextBox_TextChanged);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label3.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label3, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label3, null);
			this._L10NSharpExtender.SetLocalizingId(this.label3, "PublishWeb.Credits");
			this.label3.Location = new System.Drawing.Point(43, 489);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(46, 13);
			this.label3.TabIndex = 12;
			this.label3.Text = "Credits";
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label4.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label4, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label4, null);
			this._L10NSharpExtender.SetLocalizingId(this.label4, "PublishWeb.Copyright");
			this.label4.Location = new System.Drawing.Point(43, 392);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(60, 13);
			this.label4.TabIndex = 8;
			this.label4.Text = "Copyright";
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label5.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label5, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label5, null);
			this._L10NSharpExtender.SetLocalizingId(this.label5, "PublishWeb.License");
			this.label5.Location = new System.Drawing.Point(44, 312);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(93, 13);
			this.label5.TabIndex = 5;
			this.label5.Text = "Usage/License";
			// 
			// _licenseImageBox
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._licenseImageBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._licenseImageBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._licenseImageBox, "PublishWeb.pictureBox1");
			this._licenseImageBox.Location = new System.Drawing.Point(56, 336);
			this._licenseImageBox.Name = "_licenseImageBox";
			this._licenseImageBox.Size = new System.Drawing.Size(86, 33);
			this._licenseImageBox.TabIndex = 12;
			this._licenseImageBox.TabStop = false;
			// 
			// _licenseNotesLabel
			// 
			this._licenseNotesLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._licenseNotesLabel.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this._licenseNotesLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._licenseNotesLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._licenseNotesLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._licenseNotesLabel, "PublishWeb.LicenseNotes");
			this._licenseNotesLabel.Location = new System.Drawing.Point(191, 335);
			this._licenseNotesLabel.Name = "_licenseNotesLabel";
			this._licenseNotesLabel.Size = new System.Drawing.Size(456, 51);
			this._licenseNotesLabel.TabIndex = 7;
			this._licenseNotesLabel.Text = "License Notes";
			// 
			// label7
			// 
			this.label7.AutoSize = true;
			this.label7.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label7.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label7, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label7, null);
			this._L10NSharpExtender.SetLocalizingId(this.label7, "PublishWeb.Step1");
			this.label7.Location = new System.Drawing.Point(12, 13);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(151, 13);
			this.label7.TabIndex = 0;
			this.label7.Text = "Step 1: Confirm Metadata";
			// 
			// label8
			// 
			this.label8.AutoSize = true;
			this.label8.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label8.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label8, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label8, null);
			this._L10NSharpExtender.SetLocalizingId(this.label8, "PublishWeb.Step2");
			this.label8.Location = new System.Drawing.Point(12, 615);
			this.label8.Name = "label8";
			this.label8.Size = new System.Drawing.Size(92, 13);
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
			this._ccDescriptionButton.Location = new System.Drawing.Point(148, 331);
			this._ccDescriptionButton.Name = "_ccDescriptionButton";
			this._ccDescriptionButton.Size = new System.Drawing.Size(21, 24);
			this._ccDescriptionButton.TabIndex = 6;
			this._ccDescriptionButton.UseVisualStyleBackColor = true;
			this._ccDescriptionButton.Click += new System.EventHandler(this._ccDescriptionButton_Click);
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label6.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label6, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label6, null);
			this._L10NSharpExtender.SetLocalizingId(this.label6, "PublishWeb.Title");
			this.label6.Location = new System.Drawing.Point(44, 37);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(32, 13);
			this.label6.TabIndex = 1;
			this.label6.Text = "Title";
			// 
			// _titleLabel
			// 
			this._titleLabel.AutoSize = true;
			this._titleLabel.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this._titleLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._titleLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._titleLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._titleLabel, "PublishWeb.BloomLibraryPublishControl._titleLabel");
			this._titleLabel.Location = new System.Drawing.Point(53, 52);
			this._titleLabel.Name = "_titleLabel";
			this._titleLabel.Size = new System.Drawing.Size(27, 13);
			this._titleLabel.TabIndex = 2;
			this._titleLabel.Text = "Title";
			// 
			// _copyrightLabel
			// 
			this._copyrightLabel.AutoSize = true;
			this._copyrightLabel.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this._copyrightLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._copyrightLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._copyrightLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._copyrightLabel, "PublishWeb.BloomLibraryPublishControl.label9");
			this._copyrightLabel.Location = new System.Drawing.Point(63, 406);
			this._copyrightLabel.Name = "_copyrightLabel";
			this._copyrightLabel.Size = new System.Drawing.Size(51, 13);
			this._copyrightLabel.TabIndex = 9;
			this._copyrightLabel.Text = "Copyright";
			// 
			// label9
			// 
			this.label9.AutoSize = true;
			this.label9.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label9.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label9, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label9, null);
			this._L10NSharpExtender.SetLocalizingId(this.label9, "PublishWeb.Languages");
			this.label9.Location = new System.Drawing.Point(44, 438);
			this.label9.Name = "label9";
			this.label9.Size = new System.Drawing.Size(69, 13);
			this.label9.TabIndex = 10;
			this.label9.Text = "Languages";
			// 
			// _languagesLabel
			// 
			this._languagesLabel.AutoSize = true;
			this._languagesLabel.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this._languagesLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._languagesLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._languagesLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._languagesLabel, "PublishWeb.BloomLibraryPublishControl.label10");
			this._languagesLabel.Location = new System.Drawing.Point(63, 451);
			this._languagesLabel.Name = "_languagesLabel";
			this._languagesLabel.Size = new System.Drawing.Size(36, 13);
			this._languagesLabel.TabIndex = 11;
			this._languagesLabel.Text = "Langs";
			// 
			// _loginLink
			// 
			this._loginLink.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._loginLink.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._loginLink, null);
			this._L10NSharpExtender.SetLocalizationComment(this._loginLink, null);
			this._L10NSharpExtender.SetLocalizingId(this._loginLink, "PublishWeb.loginLink");
			this._loginLink.Location = new System.Drawing.Point(522, 641);
			this._loginLink.Name = "_loginLink";
			this._loginLink.Size = new System.Drawing.Size(129, 13);
			this._loginLink.TabIndex = 18;
			this._loginLink.TabStop = true;
			this._loginLink.Text = "Log in to BloomLibrary.org";
			this._loginLink.TextAlign = System.Drawing.ContentAlignment.TopRight;
			this._loginLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._loginLink_LinkClicked);
			// 
			// _creditsLabel
			// 
			this._creditsLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._creditsLabel.AutoEllipsis = true;
			this._creditsLabel.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this._creditsLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._creditsLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._creditsLabel, "PublishWeb.label10");
			this._creditsLabel.Location = new System.Drawing.Point(63, 503);
			this._creditsLabel.Name = "_creditsLabel";
			this._creditsLabel.Size = new System.Drawing.Size(584, 56);
			this._creditsLabel.TabIndex = 13;
			this._creditsLabel.Text = "credits";
			// 
			// label10
			// 
			this.label10.AutoSize = true;
			this.label10.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label10.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label10, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label10, null);
			this._L10NSharpExtender.SetLocalizingId(this.label10, "PublishWeb.Summary");
			this.label10.Location = new System.Drawing.Point(44, 84);
			this.label10.Name = "label10";
			this.label10.Size = new System.Drawing.Size(57, 13);
			this.label10.TabIndex = 3;
			this.label10.Text = "Summary";
			// 
			// _summaryBox
			// 
			this._summaryBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._summaryBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this._summaryBox, null);
			this._L10NSharpExtender.SetLocalizingId(this._summaryBox, "PublishWeb.textBox1");
			this._summaryBox.Location = new System.Drawing.Point(56, 100);
			this._summaryBox.Multiline = true;
			this._summaryBox.Name = "_summaryBox";
			this._summaryBox.Size = new System.Drawing.Size(591, 197);
			this._summaryBox.TabIndex = 4;
			this._summaryBox.TextChanged += new System.EventHandler(this._summaryBox_TextChanged);
			// 
			// _signUpLink
			// 
			this._signUpLink.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._signUpLink.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._signUpLink, null);
			this._L10NSharpExtender.SetLocalizationComment(this._signUpLink, null);
			this._L10NSharpExtender.SetLocalizingId(this._signUpLink, "PublishWeb.signupLink");
			this._signUpLink.Location = new System.Drawing.Point(512, 615);
			this._signUpLink.Name = "_signUpLink";
			this._signUpLink.Size = new System.Drawing.Size(139, 13);
			this._signUpLink.TabIndex = 21;
			this._signUpLink.TabStop = true;
			this._signUpLink.Text = "Sign up for BloomLibrary.org";
			this._signUpLink.TextAlign = System.Drawing.ContentAlignment.TopRight;
			this._signUpLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._signUpLink_LinkClicked);
			// 
			// _optional1
			// 
			this._optional1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._optional1.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._optional1, null);
			this._L10NSharpExtender.SetLocalizationComment(this._optional1, null);
			this._L10NSharpExtender.SetLocalizingId(this._optional1, "Common.Optional");
			this._optional1.Location = new System.Drawing.Point(603, 84);
			this._optional1.Name = "_optional1";
			this._optional1.Size = new System.Drawing.Size(44, 13);
			this._optional1.TabIndex = 22;
			this._optional1.Text = "optional";
			// 
			// _optional2
			// 
			this._optional2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._optional2.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._optional2, null);
			this._L10NSharpExtender.SetLocalizationComment(this._optional2, null);
			this._L10NSharpExtender.SetLocalizingId(this._optional2, "Common.Optional");
			this._optional2.Location = new System.Drawing.Point(603, 489);
			this._optional2.Name = "_optional2";
			this._optional2.Size = new System.Drawing.Size(44, 13);
			this._optional2.TabIndex = 23;
			this._optional2.Text = "optional";
			// 
			// _pleaseSetUploadedByLabel
			// 
			this._pleaseSetUploadedByLabel.AutoSize = true;
			this._pleaseSetUploadedByLabel.ForeColor = System.Drawing.Color.Red;
			this._L10NSharpExtender.SetLocalizableToolTip(this._pleaseSetUploadedByLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._pleaseSetUploadedByLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._pleaseSetUploadedByLabel, "PublishWeb.PleaseSet");
			this._pleaseSetUploadedByLabel.Location = new System.Drawing.Point(307, 587);
			this._pleaseSetUploadedByLabel.Name = "_pleaseSetUploadedByLabel";
			this._pleaseSetUploadedByLabel.Size = new System.Drawing.Size(136, 13);
			this._pleaseSetUploadedByLabel.TabIndex = 24;
			this._pleaseSetUploadedByLabel.Text = "Please provide an uploader";
			// 
			// BloomLibraryPublishControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoScroll = true;
			this.Controls.Add(this._pleaseSetUploadedByLabel);
			this.Controls.Add(this._optional2);
			this.Controls.Add(this._optional1);
			this.Controls.Add(this._signUpLink);
			this.Controls.Add(this.label3);
			this.Controls.Add(this._summaryBox);
			this.Controls.Add(this.label10);
			this.Controls.Add(this._creditsLabel);
			this.Controls.Add(this._loginLink);
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
			this.Controls.Add(this._uploadedByTextBox);
			this.Controls.Add(this.label2);
			this.Controls.Add(this._progressBox);
			this.Controls.Add(this.label1);
			this.Controls.Add(this._uploadButton);
			this.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "PublishWeb.BloomLibraryPublishControl.BloomLibraryPublishControl");
			this.Name = "BloomLibraryPublishControl";
			this.Size = new System.Drawing.Size(694, 879);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this._licenseImageBox)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Button _uploadButton;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.TextBox _progressBox;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.TextBox _uploadedByTextBox;
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
		private System.Windows.Forms.LinkLabel _loginLink;
		private System.Windows.Forms.Label _creditsLabel;
		private System.Windows.Forms.Label label10;
		private System.Windows.Forms.TextBox _summaryBox;
		private System.Windows.Forms.LinkLabel _signUpLink;
		private System.Windows.Forms.Label _optional1;
		private System.Windows.Forms.Label _optional2;
		private System.Windows.Forms.Label _pleaseSetUploadedByLabel;
	}
}
