using Palaso.UI.WindowsForms.ImageToolbox;

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
            this._progressBox = new Palaso.UI.WindowsForms.Progress.LogBox();
            this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this._licenseImageBox = new System.Windows.Forms.PictureBox();
            this._licenseNotesLabel = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this._ccDescriptionButton = new System.Windows.Forms.Button();
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
            this.horizontalLine = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label11 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.label13 = new System.Windows.Forms.Label();
            this.label14 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this._licenseImageBox)).BeginInit();
            this.SuspendLayout();
            // 
            // _uploadButton
            // 
            this._L10NSharpExtender.SetLocalizableToolTip(this._uploadButton, null);
            this._L10NSharpExtender.SetLocalizationComment(this._uploadButton, null);
            this._L10NSharpExtender.SetLocalizingId(this._uploadButton, "Publish.Upload.UploadButton");
            this._uploadButton.Location = new System.Drawing.Point(15, 443);
            this._uploadButton.Name = "_uploadButton";
            this._uploadButton.Size = new System.Drawing.Size(101, 23);
            this._uploadButton.TabIndex = 17;
            this._uploadButton.Text = "Upload Book";
            this._uploadButton.UseVisualStyleBackColor = true;
            this._uploadButton.Click += new System.EventHandler(this._uploadButton_Click);
            // 
            // _progressBox
            // 
            this._progressBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._progressBox.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this._progressBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(171)))), ((int)(((byte)(173)))), ((int)(((byte)(179)))));
            this._progressBox.CancelRequested = false;
            this._progressBox.ErrorEncountered = false;
            this._progressBox.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._progressBox.GetDiagnosticsMethod = null;
            this._L10NSharpExtender.SetLocalizableToolTip(this._progressBox, null);
            this._L10NSharpExtender.SetLocalizationComment(this._progressBox, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._progressBox, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._progressBox, "Publish.Upload.LogBox");
            this._progressBox.Location = new System.Drawing.Point(17, 477);
            this._progressBox.Name = "_progressBox";
            this._progressBox.ProgressIndicator = null;
            this._progressBox.ShowCopyToClipboardMenuItem = false;
            this._progressBox.ShowDetailsMenuItem = false;
            this._progressBox.ShowDiagnosticsMenuItem = false;
            this._progressBox.ShowFontMenuItem = false;
            this._progressBox.ShowMenu = true;
            this._progressBox.Size = new System.Drawing.Size(651, 180);
            this._progressBox.TabIndex = 25;
            // 
            // _L10NSharpExtender
            // 
            this._L10NSharpExtender.LocalizationManagerId = "Bloom";
            this._L10NSharpExtender.PrefixForNewItems = "Publish.Upload";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label3, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label3, null);
            this._L10NSharpExtender.SetLocalizingId(this.label3, "Publish.Upload.Credits");
            this.label3.Location = new System.Drawing.Point(15, 307);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(43, 13);
            this.label3.TabIndex = 12;
            this.label3.Text = "Credits";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label4, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label4, null);
            this._L10NSharpExtender.SetLocalizingId(this.label4, "Publish.Upload.Copyright");
            this.label4.Location = new System.Drawing.Point(15, 210);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(59, 13);
            this.label4.TabIndex = 8;
            this.label4.Text = "Copyright";
            // 
            // _licenseImageBox
            // 
            this._L10NSharpExtender.SetLocalizableToolTip(this._licenseImageBox, null);
            this._L10NSharpExtender.SetLocalizationComment(this._licenseImageBox, null);
            this._L10NSharpExtender.SetLocalizingId(this._licenseImageBox, "Publish.Upload.pictureBox1");
            this._licenseImageBox.Location = new System.Drawing.Point(15, 156);
            this._licenseImageBox.Name = "_licenseImageBox";
            this._licenseImageBox.Size = new System.Drawing.Size(86, 33);
            this._licenseImageBox.TabIndex = 12;
            this._licenseImageBox.TabStop = false;
            // 
            // _licenseNotesLabel
            // 
            this._licenseNotesLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._licenseNotesLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._licenseNotesLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this._licenseNotesLabel, null);
            this._L10NSharpExtender.SetLocalizationComment(this._licenseNotesLabel, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._licenseNotesLabel, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._licenseNotesLabel, "Publish.Upload.LicenseNotes");
            this._licenseNotesLabel.Location = new System.Drawing.Point(145, 155);
            this._licenseNotesLabel.Name = "_licenseNotesLabel";
            this._licenseNotesLabel.Size = new System.Drawing.Size(438, 51);
            this._licenseNotesLabel.TabIndex = 7;
            this._licenseNotesLabel.Text = "License Notes";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label8.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label8, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label8, null);
            this._L10NSharpExtender.SetLocalizingId(this.label8, "Publish.Upload.Step2");
            this.label8.Location = new System.Drawing.Point(15, 404);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(121, 21);
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
            this._L10NSharpExtender.SetLocalizingId(this._ccDescriptionButton, "Publish.Upload.button1");
            this._ccDescriptionButton.Location = new System.Drawing.Point(105, 151);
            this._ccDescriptionButton.Name = "_ccDescriptionButton";
            this._ccDescriptionButton.Size = new System.Drawing.Size(21, 24);
            this._ccDescriptionButton.TabIndex = 6;
            this._ccDescriptionButton.UseVisualStyleBackColor = true;
            this._ccDescriptionButton.Click += new System.EventHandler(this._ccDescriptionButton_Click);
            // 
            // _titleLabel
            // 
            this._titleLabel.AutoSize = true;
            this._titleLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this._titleLabel, null);
            this._L10NSharpExtender.SetLocalizationComment(this._titleLabel, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._titleLabel, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._titleLabel, "Publish.Upload.BloomLibraryPublishControl._titleLabel");
            this._titleLabel.Location = new System.Drawing.Point(15, 55);
            this._titleLabel.Name = "_titleLabel";
            this._titleLabel.Size = new System.Drawing.Size(27, 13);
            this._titleLabel.TabIndex = 2;
            this._titleLabel.Text = "Title";
            // 
            // _copyrightLabel
            // 
            this._copyrightLabel.AutoSize = true;
            this._copyrightLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._copyrightLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this._copyrightLabel, null);
            this._L10NSharpExtender.SetLocalizationComment(this._copyrightLabel, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._copyrightLabel, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._copyrightLabel, "Publish.Upload.BloomLibraryPublishControl.label9");
            this._copyrightLabel.Location = new System.Drawing.Point(15, 223);
            this._copyrightLabel.Name = "_copyrightLabel";
            this._copyrightLabel.Size = new System.Drawing.Size(58, 13);
            this._copyrightLabel.TabIndex = 9;
            this._copyrightLabel.Text = "Copyright";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label9.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label9, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label9, null);
            this._L10NSharpExtender.SetLocalizingId(this.label9, "Publish.Upload.Languages");
            this.label9.Location = new System.Drawing.Point(15, 256);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(64, 13);
            this.label9.TabIndex = 10;
            this.label9.Text = "Languages";
            // 
            // _languagesLabel
            // 
            this._languagesLabel.AutoSize = true;
            this._languagesLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._languagesLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this._languagesLabel, null);
            this._L10NSharpExtender.SetLocalizationComment(this._languagesLabel, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._languagesLabel, L10NSharp.LocalizationPriority.NotLocalizable);
            this._L10NSharpExtender.SetLocalizingId(this._languagesLabel, "Publish.Upload.BloomLibraryPublishControl.label10");
            this._languagesLabel.Location = new System.Drawing.Point(15, 272);
            this._languagesLabel.Name = "_languagesLabel";
            this._languagesLabel.Size = new System.Drawing.Size(37, 13);
            this._languagesLabel.TabIndex = 11;
            this._languagesLabel.Text = "Langs";
            // 
            // _loginLink
            // 
            this._loginLink.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this._loginLink.AutoSize = true;
            this._loginLink.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._L10NSharpExtender.SetLocalizableToolTip(this._loginLink, null);
            this._L10NSharpExtender.SetLocalizationComment(this._loginLink, null);
            this._L10NSharpExtender.SetLocalizingId(this._loginLink, "Publish.Upload.loginLink");
            this._loginLink.Location = new System.Drawing.Point(525, 445);
            this._loginLink.Name = "_loginLink";
            this._loginLink.Size = new System.Drawing.Size(144, 13);
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
            this._creditsLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._creditsLabel.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this._creditsLabel, null);
            this._L10NSharpExtender.SetLocalizationComment(this._creditsLabel, null);
            this._L10NSharpExtender.SetLocalizingId(this._creditsLabel, "Publish.Upload.label10");
            this._creditsLabel.Location = new System.Drawing.Point(15, 320);
            this._creditsLabel.Name = "_creditsLabel";
            this._creditsLabel.Size = new System.Drawing.Size(655, 56);
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
            this._L10NSharpExtender.SetLocalizingId(this.label10, "Publish.Upload.Summary");
            this.label10.Location = new System.Drawing.Point(15, 84);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(57, 13);
            this.label10.TabIndex = 3;
            this.label10.Text = "Summary";
            // 
            // _summaryBox
            // 
            this._summaryBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._summaryBox.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._L10NSharpExtender.SetLocalizableToolTip(this._summaryBox, null);
            this._L10NSharpExtender.SetLocalizationComment(this._summaryBox, null);
            this._L10NSharpExtender.SetLocalizingId(this._summaryBox, "Publish.Upload.textBox1");
            this._summaryBox.Location = new System.Drawing.Point(15, 100);
            this._summaryBox.Name = "_summaryBox";
            this._summaryBox.Size = new System.Drawing.Size(655, 22);
            this._summaryBox.TabIndex = 4;
            this._summaryBox.TextChanged += new System.EventHandler(this._summaryBox_TextChanged);
            // 
            // _signUpLink
            // 
            this._signUpLink.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this._signUpLink.AutoSize = true;
            this._signUpLink.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._L10NSharpExtender.SetLocalizableToolTip(this._signUpLink, null);
            this._L10NSharpExtender.SetLocalizationComment(this._signUpLink, null);
            this._L10NSharpExtender.SetLocalizingId(this._signUpLink, "Publish.Upload.signupLink");
            this._signUpLink.Location = new System.Drawing.Point(515, 419);
            this._signUpLink.Name = "_signUpLink";
            this._signUpLink.Size = new System.Drawing.Size(156, 13);
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
            this._optional1.ForeColor = System.Drawing.Color.Gray;
            this._L10NSharpExtender.SetLocalizableToolTip(this._optional1, null);
            this._L10NSharpExtender.SetLocalizationComment(this._optional1, null);
            this._L10NSharpExtender.SetLocalizingId(this._optional1, "Common.Optional");
            this._optional1.Location = new System.Drawing.Point(626, 84);
            this._optional1.Name = "_optional1";
            this._optional1.Size = new System.Drawing.Size(44, 13);
            this._optional1.TabIndex = 22;
            this._optional1.Text = "optional";
            // 
            // horizontalLine
            // 
            this.horizontalLine.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.horizontalLine.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this._L10NSharpExtender.SetLocalizableToolTip(this.horizontalLine, null);
            this._L10NSharpExtender.SetLocalizationComment(this.horizontalLine, null);
            this._L10NSharpExtender.SetLocalizingId(this.horizontalLine, "PublishWeb.label1");
            this.horizontalLine.Location = new System.Drawing.Point(15, 385);
            this.horizontalLine.Name = "horizontalLine";
            this.horizontalLine.Size = new System.Drawing.Size(652, 2);
            this.horizontalLine.TabIndex = 26;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label2, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label2, null);
            this._L10NSharpExtender.SetLocalizingId(this.label2, "PublishWeb.label2");
            this.label2.Location = new System.Drawing.Point(12, 13);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(204, 21);
            this.label2.TabIndex = 0;
            this.label2.Text = "Step 1: Confirm Metadata";
            // 
            // label11
            // 
            this.label11.AutoSize = true;
            this.label11.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label11.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label11, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label11, null);
            this._L10NSharpExtender.SetLocalizingId(this.label11, "Publish.Upload.TitleLabel");
            this.label11.Location = new System.Drawing.Point(12, 42);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(29, 13);
            this.label11.TabIndex = 1;
            this.label11.Text = "Title";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label12.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label12, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label12, null);
            this._L10NSharpExtender.SetLocalizingId(this.label12, "PublishWeb.label12");
            this.label12.Location = new System.Drawing.Point(12, 55);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(28, 13);
            this.label12.TabIndex = 2;
            this.label12.Text = "Title";
            // 
            // label13
            // 
            this.label13.AutoSize = true;
            this.label13.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label13.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label13, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label13, null);
            this._L10NSharpExtender.SetLocalizingId(this.label13, "Publish.Upload.Summary");
            this.label13.Location = new System.Drawing.Point(12, 84);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(56, 13);
            this.label13.TabIndex = 3;
            this.label13.Text = "Summary";
            // 
            // label14
            // 
            this.label14.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.label14.AutoSize = true;
            this.label14.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label14.ForeColor = System.Drawing.Color.Gray;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label14, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label14, null);
            this._L10NSharpExtender.SetLocalizingId(this.label14, "Common.Optional");
            this.label14.Location = new System.Drawing.Point(623, 84);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(51, 13);
            this.label14.TabIndex = 22;
            this.label14.Text = "optional";
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label5.ForeColor = System.Drawing.SystemColors.ControlText;
            this._L10NSharpExtender.SetLocalizableToolTip(this.label5, null);
            this._L10NSharpExtender.SetLocalizationComment(this.label5, null);
            this._L10NSharpExtender.SetLocalizingId(this.label5, "Publish.Upload.License");
            this.label5.Location = new System.Drawing.Point(15, 130);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(82, 13);
            this.label5.TabIndex = 5;
            this.label5.Text = "Usage/License";
            // 
            // BloomLibraryPublishControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoScroll = true;
            this.Controls.Add(this.horizontalLine);
            this.Controls.Add(this.label14);
            this.Controls.Add(this._optional1);
            this.Controls.Add(this._signUpLink);
            this.Controls.Add(this.label3);
            this.Controls.Add(this._summaryBox);
            this.Controls.Add(this.label13);
            this.Controls.Add(this.label10);
            this.Controls.Add(this._creditsLabel);
            this.Controls.Add(this._loginLink);
            this.Controls.Add(this._languagesLabel);
            this.Controls.Add(this.label9);
            this.Controls.Add(this._copyrightLabel);
            this.Controls.Add(this.label12);
            this.Controls.Add(this._titleLabel);
            this.Controls.Add(this.label11);
            this.Controls.Add(this._ccDescriptionButton);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label2);
            this.Controls.Add(this._licenseNotesLabel);
            this.Controls.Add(this._licenseImageBox);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this._progressBox);
            this.Controls.Add(this._uploadButton);
            this.ForeColor = System.Drawing.Color.Gray;
            this._L10NSharpExtender.SetLocalizableToolTip(this, null);
            this._L10NSharpExtender.SetLocalizationComment(this, null);
            this._L10NSharpExtender.SetLocalizingId(this, "Publish.Upload.BloomLibraryPublishControl.BloomLibraryPublishControl");
            this.MinimumSize = new System.Drawing.Size(0, 678);
            this.Name = "BloomLibraryPublishControl";
            this.Size = new System.Drawing.Size(694, 678);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this._licenseImageBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

        private System.Windows.Forms.Button _uploadButton;
		private Palaso.UI.WindowsForms.Progress.LogBox _progressBox;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
		private System.Windows.Forms.PictureBox _licenseImageBox;
        private System.Windows.Forms.Label _licenseNotesLabel;
		private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Button _ccDescriptionButton;
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
        private System.Windows.Forms.Label horizontalLine;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.Label label13;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.Label label5;
	}
}
