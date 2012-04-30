namespace Bloom.Library
{
	partial class SettingsDialog
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SettingsDialog));
			this.tabControl1 = new System.Windows.Forms.TabControl();
			this.tabPage1 = new System.Windows.Forms.TabPage();
			this._removeSecondNationalLanguageButton = new System.Windows.Forms.LinkLabel();
			this._national2ChangeLink = new System.Windows.Forms.LinkLabel();
			this._national1ChangeLink = new System.Windows.Forms.LinkLabel();
			this._vernacularChangeLink = new System.Windows.Forms.LinkLabel();
			this._nationalLanguage2Label = new System.Windows.Forms.Label();
			this.label6 = new System.Windows.Forms.Label();
			this._nationalLanguage1Label = new System.Windows.Forms.Label();
			this.label4 = new System.Windows.Forms.Label();
			this._vernacularLanguageName = new System.Windows.Forms.Label();
			this._vernacularOrShellLanguageLabel = new System.Windows.Forms.Label();
			this.tabPage2 = new System.Windows.Forms.TabPage();
			this.label1 = new System.Windows.Forms.Label();
			this._xmatterPackCombo = new System.Windows.Forms.ComboBox();
			this.tabPage3 = new System.Windows.Forms.TabPage();
			this._districtText = new System.Windows.Forms.TextBox();
			this._provinceText = new System.Windows.Forms.TextBox();
			this._countryText = new System.Windows.Forms.TextBox();
			this.label7 = new System.Windows.Forms.Label();
			this.label5 = new System.Windows.Forms.Label();
			this.label3 = new System.Windows.Forms.Label();
			this._okButton = new System.Windows.Forms.Button();
			this._restartMessage = new System.Windows.Forms.Label();
			this.settingsProtectionLauncherButton1 = new Palaso.UI.WindowsForms.SettingProtection.SettingsProtectionLauncherButton();
			this.button1 = new System.Windows.Forms.Button();
			this.button2 = new System.Windows.Forms.Button();
			this.tabControl1.SuspendLayout();
			this.tabPage1.SuspendLayout();
			this.tabPage2.SuspendLayout();
			this.tabPage3.SuspendLayout();
			this.SuspendLayout();
			// 
			// tabControl1
			// 
			this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.tabControl1.Controls.Add(this.tabPage1);
			this.tabControl1.Controls.Add(this.tabPage2);
			this.tabControl1.Controls.Add(this.tabPage3);
			this.tabControl1.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.tabControl1.Location = new System.Drawing.Point(1, 2);
			this.tabControl1.Name = "tabControl1";
			this.tabControl1.SelectedIndex = 0;
			this.tabControl1.Size = new System.Drawing.Size(619, 344);
			this.tabControl1.TabIndex = 0;
			// 
			// tabPage1
			// 
			this.tabPage1.Controls.Add(this.button1);
			this.tabPage1.Controls.Add(this._removeSecondNationalLanguageButton);
			this.tabPage1.Controls.Add(this._national2ChangeLink);
			this.tabPage1.Controls.Add(this._national1ChangeLink);
			this.tabPage1.Controls.Add(this._vernacularChangeLink);
			this.tabPage1.Controls.Add(this._nationalLanguage2Label);
			this.tabPage1.Controls.Add(this.label6);
			this.tabPage1.Controls.Add(this._nationalLanguage1Label);
			this.tabPage1.Controls.Add(this.label4);
			this.tabPage1.Controls.Add(this._vernacularLanguageName);
			this.tabPage1.Controls.Add(this._vernacularOrShellLanguageLabel);
			this.tabPage1.Location = new System.Drawing.Point(4, 26);
			this.tabPage1.Name = "tabPage1";
			this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage1.Size = new System.Drawing.Size(611, 314);
			this.tabPage1.TabIndex = 0;
			this.tabPage1.Text = "Languages";
			this.tabPage1.UseVisualStyleBackColor = true;
			// 
			// _removeSecondNationalLanguageButton
			// 
			this._removeSecondNationalLanguageButton.AutoSize = true;
			this._removeSecondNationalLanguageButton.Location = new System.Drawing.Point(159, 243);
			this._removeSecondNationalLanguageButton.Name = "_removeSecondNationalLanguageButton";
			this._removeSecondNationalLanguageButton.Size = new System.Drawing.Size(58, 19);
			this._removeSecondNationalLanguageButton.TabIndex = 18;
			this._removeSecondNationalLanguageButton.TabStop = true;
			this._removeSecondNationalLanguageButton.Text = "Remove";
			this._removeSecondNationalLanguageButton.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._removeSecondNationalLanguageButton_LinkClicked);
			// 
			// _national2ChangeLink
			// 
			this._national2ChangeLink.AutoSize = true;
			this._national2ChangeLink.Location = new System.Drawing.Point(27, 243);
			this._national2ChangeLink.Name = "_national2ChangeLink";
			this._national2ChangeLink.Size = new System.Drawing.Size(65, 19);
			this._national2ChangeLink.TabIndex = 17;
			this._national2ChangeLink.TabStop = true;
			this._national2ChangeLink.Text = "Change...";
			this._national2ChangeLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._national2ChangeLink_LinkClicked);
			// 
			// _national1ChangeLink
			// 
			this._national1ChangeLink.AutoSize = true;
			this._national1ChangeLink.Location = new System.Drawing.Point(27, 158);
			this._national1ChangeLink.Name = "_national1ChangeLink";
			this._national1ChangeLink.Size = new System.Drawing.Size(65, 19);
			this._national1ChangeLink.TabIndex = 16;
			this._national1ChangeLink.TabStop = true;
			this._national1ChangeLink.Text = "Change...";
			this._national1ChangeLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._national1ChangeLink_LinkClicked);
			// 
			// _vernacularChangeLink
			// 
			this._vernacularChangeLink.AutoSize = true;
			this._vernacularChangeLink.Location = new System.Drawing.Point(27, 69);
			this._vernacularChangeLink.Name = "_vernacularChangeLink";
			this._vernacularChangeLink.Size = new System.Drawing.Size(65, 19);
			this._vernacularChangeLink.TabIndex = 15;
			this._vernacularChangeLink.TabStop = true;
			this._vernacularChangeLink.Text = "Change...";
			this._vernacularChangeLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._vernacularChangeLink_LinkClicked);
			// 
			// _nationalLanguage2Label
			// 
			this._nationalLanguage2Label.AutoSize = true;
			this._nationalLanguage2Label.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._nationalLanguage2Label.Location = new System.Drawing.Point(26, 218);
			this._nationalLanguage2Label.Name = "_nationalLanguage2Label";
			this._nationalLanguage2Label.Size = new System.Drawing.Size(49, 19);
			this._nationalLanguage2Label.TabIndex = 14;
			this._nationalLanguage2Label.Text = "foobar";
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label6.Location = new System.Drawing.Point(26, 198);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(308, 19);
			this.label6.TabIndex = 13;
			this.label6.Text = "Language 2 (Optional) (e.g. Regional Language)";
			// 
			// _nationalLanguage1Label
			// 
			this._nationalLanguage1Label.AutoSize = true;
			this._nationalLanguage1Label.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._nationalLanguage1Label.Location = new System.Drawing.Point(26, 133);
			this._nationalLanguage1Label.Name = "_nationalLanguage1Label";
			this._nationalLanguage1Label.Size = new System.Drawing.Size(49, 19);
			this._nationalLanguage1Label.TabIndex = 11;
			this._nationalLanguage1Label.Text = "foobar";
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label4.Location = new System.Drawing.Point(26, 113);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(227, 19);
			this.label4.TabIndex = 10;
			this.label4.Text = "Language 1 (e.g. Nation Language)";
			this.label4.Click += new System.EventHandler(this.label4_Click);
			// 
			// _vernacularLanguageName
			// 
			this._vernacularLanguageName.AutoSize = true;
			this._vernacularLanguageName.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._vernacularLanguageName.Location = new System.Drawing.Point(26, 44);
			this._vernacularLanguageName.Name = "_vernacularLanguageName";
			this._vernacularLanguageName.Size = new System.Drawing.Size(49, 19);
			this._vernacularLanguageName.TabIndex = 8;
			this._vernacularLanguageName.Text = "foobar";
			// 
			// _vernacularOrShellLanguageLabel
			// 
			this._vernacularOrShellLanguageLabel.AutoSize = true;
			this._vernacularOrShellLanguageLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._vernacularOrShellLanguageLabel.Location = new System.Drawing.Point(26, 24);
			this._vernacularOrShellLanguageLabel.Name = "_vernacularOrShellLanguageLabel";
			this._vernacularOrShellLanguageLabel.Size = new System.Drawing.Size(140, 19);
			this._vernacularOrShellLanguageLabel.TabIndex = 7;
			this._vernacularOrShellLanguageLabel.Text = "Vernacular Language";
			// 
			// tabPage2
			// 
			this.tabPage2.Controls.Add(this.button2);
			this.tabPage2.Controls.Add(this.label1);
			this.tabPage2.Controls.Add(this._xmatterPackCombo);
			this.tabPage2.Location = new System.Drawing.Point(4, 26);
			this.tabPage2.Name = "tabPage2";
			this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage2.Size = new System.Drawing.Size(611, 314);
			this.tabPage2.TabIndex = 1;
			this.tabPage2.Text = "Book Making";
			this.tabPage2.UseVisualStyleBackColor = true;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label1.Location = new System.Drawing.Point(25, 32);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(156, 19);
			this.label1.TabIndex = 1;
			this.label1.Text = "Front/Back Matter Pack";
			// 
			// _xmatterPackCombo
			// 
			this._xmatterPackCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this._xmatterPackCombo.FormattingEnabled = true;
			this._xmatterPackCombo.Location = new System.Drawing.Point(29, 54);
			this._xmatterPackCombo.Name = "_xmatterPackCombo";
			this._xmatterPackCombo.Size = new System.Drawing.Size(146, 25);
			this._xmatterPackCombo.TabIndex = 0;
			// 
			// tabPage3
			// 
			this.tabPage3.Controls.Add(this._districtText);
			this.tabPage3.Controls.Add(this._provinceText);
			this.tabPage3.Controls.Add(this._countryText);
			this.tabPage3.Controls.Add(this.label7);
			this.tabPage3.Controls.Add(this.label5);
			this.tabPage3.Controls.Add(this.label3);
			this.tabPage3.Location = new System.Drawing.Point(4, 26);
			this.tabPage3.Name = "tabPage3";
			this.tabPage3.Size = new System.Drawing.Size(611, 314);
			this.tabPage3.TabIndex = 2;
			this.tabPage3.Text = "Project Information";
			this.tabPage3.UseVisualStyleBackColor = true;
			// 
			// _districtText
			// 
			this._districtText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._districtText.Location = new System.Drawing.Point(32, 177);
			this._districtText.Name = "_districtText";
			this._districtText.Size = new System.Drawing.Size(214, 25);
			this._districtText.TabIndex = 5;
			// 
			// _provinceText
			// 
			this._provinceText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._provinceText.Location = new System.Drawing.Point(32, 112);
			this._provinceText.Name = "_provinceText";
			this._provinceText.Size = new System.Drawing.Size(214, 25);
			this._provinceText.TabIndex = 4;
			// 
			// _countryText
			// 
			this._countryText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._countryText.Location = new System.Drawing.Point(32, 45);
			this._countryText.Name = "_countryText";
			this._countryText.Size = new System.Drawing.Size(214, 25);
			this._countryText.TabIndex = 3;
			// 
			// label7
			// 
			this.label7.AutoSize = true;
			this.label7.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label7.Location = new System.Drawing.Point(28, 23);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(59, 19);
			this.label7.TabIndex = 2;
			this.label7.Text = "Country";
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label5.Location = new System.Drawing.Point(28, 155);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(55, 19);
			this.label5.TabIndex = 1;
			this.label5.Text = "District";
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label3.Location = new System.Drawing.Point(28, 90);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(63, 19);
			this.label3.TabIndex = 0;
			this.label3.Text = "Province";
			// 
			// _okButton
			// 
			this._okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._okButton.Location = new System.Drawing.Point(534, 382);
			this._okButton.Name = "_okButton";
			this._okButton.Size = new System.Drawing.Size(75, 23);
			this._okButton.TabIndex = 1;
			this._okButton.Text = "&OK";
			this._okButton.UseVisualStyleBackColor = true;
			this._okButton.Click += new System.EventHandler(this._okButton_Click);
			// 
			// _restartMessage
			// 
			this._restartMessage.AutoSize = true;
			this._restartMessage.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._restartMessage.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(0)))), ((int)(((byte)(0)))));
			this._restartMessage.Location = new System.Drawing.Point(9, 388);
			this._restartMessage.Name = "_restartMessage";
			this._restartMessage.Size = new System.Drawing.Size(290, 20);
			this._restartMessage.TabIndex = 19;
			this._restartMessage.Text = "Restart Bloom to use new settings.";
			this._restartMessage.Visible = false;
			// 
			// settingsProtectionLauncherButton1
			// 
			this.settingsProtectionLauncherButton1.Location = new System.Drawing.Point(13, 349);
			this.settingsProtectionLauncherButton1.Margin = new System.Windows.Forms.Padding(0);
			this.settingsProtectionLauncherButton1.Name = "settingsProtectionLauncherButton1";
			this.settingsProtectionLauncherButton1.Size = new System.Drawing.Size(257, 37);
			this.settingsProtectionLauncherButton1.TabIndex = 20;
			// 
			// button1
			// 
			this.button1.FlatAppearance.BorderSize = 0;
			this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button1.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.button1.Image = global::Bloom.Properties.Resources.help24x24;
			this.button1.Location = new System.Drawing.Point(478, 24);
			this.button1.Name = "button1";
			this.button1.Size = new System.Drawing.Size(113, 73);
			this.button1.TabIndex = 19;
			this.button1.Text = "Help With Language Settings";
			this.button1.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.button1.UseVisualStyleBackColor = true;
			// 
			// button2
			// 
			this.button2.FlatAppearance.BorderSize = 0;
			this.button2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button2.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.button2.Image = global::Bloom.Properties.Resources.help24x24;
			this.button2.Location = new System.Drawing.Point(468, 30);
			this.button2.Name = "button2";
			this.button2.Size = new System.Drawing.Size(113, 73);
			this.button2.TabIndex = 20;
			this.button2.Text = "Help With Book Making Settings";
			this.button2.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.button2.UseVisualStyleBackColor = true;
			// 
			// SettingsDialog
			// 
			this.AcceptButton = this._okButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(621, 417);
			this.Controls.Add(this.settingsProtectionLauncherButton1);
			this.Controls.Add(this._restartMessage);
			this.Controls.Add(this._okButton);
			this.Controls.Add(this.tabControl1);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "SettingsDialog";
			this.Text = "Settings";
			this.tabControl1.ResumeLayout(false);
			this.tabPage1.ResumeLayout(false);
			this.tabPage1.PerformLayout();
			this.tabPage2.ResumeLayout(false);
			this.tabPage2.PerformLayout();
			this.tabPage3.ResumeLayout(false);
			this.tabPage3.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TabControl tabControl1;
		private System.Windows.Forms.TabPage tabPage1;
		private System.Windows.Forms.TabPage tabPage2;
		private System.Windows.Forms.TabPage tabPage3;
		private System.Windows.Forms.Button _okButton;
		protected System.Windows.Forms.Label _vernacularOrShellLanguageLabel;
		private System.Windows.Forms.LinkLabel _national2ChangeLink;
		private System.Windows.Forms.LinkLabel _national1ChangeLink;
		private System.Windows.Forms.LinkLabel _vernacularChangeLink;
		protected System.Windows.Forms.Label _nationalLanguage2Label;
		protected System.Windows.Forms.Label label6;
		protected System.Windows.Forms.Label _nationalLanguage1Label;
		protected System.Windows.Forms.Label label4;
		protected System.Windows.Forms.Label _vernacularLanguageName;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.ComboBox _xmatterPackCombo;
		private System.Windows.Forms.TextBox _districtText;
		private System.Windows.Forms.TextBox _provinceText;
		private System.Windows.Forms.TextBox _countryText;
		private System.Windows.Forms.Label label7;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.LinkLabel _removeSecondNationalLanguageButton;
		private System.Windows.Forms.Label _restartMessage;
		private Palaso.UI.WindowsForms.SettingProtection.SettingsProtectionLauncherButton settingsProtectionLauncherButton1;
		private System.Windows.Forms.Button button1;
		private System.Windows.Forms.Button button2;
	}
}