using System.Drawing;
using System.Windows.Forms;

namespace Bloom.Collection
{
	partial class CollectionSettingsDialog
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
			this._tab = new System.Windows.Forms.TabControl();
			this.tabPage1 = new System.Windows.Forms.TabPage();
			this._removeLanguage3Link = new System.Windows.Forms.LinkLabel();
			this._changeLanguage3Link = new System.Windows.Forms.LinkLabel();
			this._changeLanguage2Link = new System.Windows.Forms.LinkLabel();
			this._changeLanguage1Link = new System.Windows.Forms.LinkLabel();
			this._language3Name = new System.Windows.Forms.Label();
			this._language3Label = new System.Windows.Forms.Label();
			this._language2Name = new System.Windows.Forms.Label();
			this._language2Label = new System.Windows.Forms.Label();
			this._language1Name = new System.Windows.Forms.Label();
			this._language1Label = new System.Windows.Forms.Label();
			this.tabPage2 = new System.Windows.Forms.TabPage();
			this._numberStyleCombo = new System.Windows.Forms.ComboBox();
			this.label3 = new System.Windows.Forms.Label();
			this._fontSettings3Link = new System.Windows.Forms.LinkLabel();
			this._fontSettings2Link = new System.Windows.Forms.LinkLabel();
			this._fontSettings1Link = new System.Windows.Forms.LinkLabel();
			this._xmatterList = new System.Windows.Forms.ListView();
			this._xmatterDescription = new System.Windows.Forms.TextBox();
			this._fontComboLanguage3 = new System.Windows.Forms.ComboBox();
			this._fontComboLanguage2 = new System.Windows.Forms.ComboBox();
			this._fontComboLanguage1 = new System.Windows.Forms.ComboBox();
			this._language3FontLabel = new System.Windows.Forms.Label();
			this._language2FontLabel = new System.Windows.Forms.Label();
			this._language1FontLabel = new System.Windows.Forms.Label();
			this._xmatterPackLabel = new System.Windows.Forms.Label();
			this.tabPage3 = new System.Windows.Forms.TabPage();
			this._bloomCollectionName = new System.Windows.Forms.TextBox();
			this.label1 = new System.Windows.Forms.Label();
			this._districtText = new System.Windows.Forms.TextBox();
			this._provinceText = new System.Windows.Forms.TextBox();
			this._countryText = new System.Windows.Forms.TextBox();
			this._countryLabel = new System.Windows.Forms.Label();
			this._districtLabel = new System.Windows.Forms.Label();
			this._provinceLabel = new System.Windows.Forms.Label();
			this._enterpriseTab = new System.Windows.Forms.TabPage();
			this._sharingTab = new System.Windows.Forms.TabPage();
			this.tabPage4 = new System.Windows.Forms.TabPage();
			this.showTroubleShooterCheckBox = new System.Windows.Forms.CheckBox();
			this._automaticallyUpdate = new System.Windows.Forms.CheckBox();
			this._showExperimentalFeatures = new System.Windows.Forms.CheckBox();
			this._okButton = new System.Windows.Forms.Button();
			this._restartReminder = new System.Windows.Forms.Label();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._cancelButton = new System.Windows.Forms.Button();
			this.settingsProtectionLauncherButton1 = new SIL.Windows.Forms.SettingProtection.SettingsProtectionLauncherButton();
			this._helpButton = new System.Windows.Forms.Button();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this._signLanguageLabel = new System.Windows.Forms.Label();
			this._signLanguageName = new System.Windows.Forms.Label();
			this._removeSignLanguageLink = new System.Windows.Forms.LinkLabel();
			this._changeSignLanguageLink = new System.Windows.Forms.LinkLabel();
			this._tab.SuspendLayout();
			this.tabPage1.SuspendLayout();
			this.tabPage2.SuspendLayout();
			this.tabPage3.SuspendLayout();
			this.tabPage4.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _tab
			// 
			this._tab.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._tab.Controls.Add(this.tabPage1);
			this._tab.Controls.Add(this.tabPage2);
			this._tab.Controls.Add(this.tabPage3);
			this._tab.Controls.Add(this._enterpriseTab);
			this._tab.Controls.Add(this._sharingTab);
			this._tab.Controls.Add(this.tabPage4);
			this._tab.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._tab.Location = new System.Drawing.Point(1, 2);
			this._tab.Name = "_tab";
			this._tab.SelectedIndex = 0;
			this._tab.Size = new System.Drawing.Size(650, 482);
			this._tab.TabIndex = 0;
			this._tab.SelectedIndexChanged += new System.EventHandler(this._tab_SelectedIndexChanged);
			// 
			// tabPage1
			// 
			this.tabPage1.Controls.Add(this._removeSignLanguageLink);
			this.tabPage1.Controls.Add(this._changeSignLanguageLink);
			this.tabPage1.Controls.Add(this._signLanguageName);
			this.tabPage1.Controls.Add(this._signLanguageLabel);
			this.tabPage1.Controls.Add(this._removeLanguage3Link);
			this.tabPage1.Controls.Add(this._changeLanguage3Link);
			this.tabPage1.Controls.Add(this._changeLanguage2Link);
			this.tabPage1.Controls.Add(this._changeLanguage1Link);
			this.tabPage1.Controls.Add(this._language3Name);
			this.tabPage1.Controls.Add(this._language3Label);
			this.tabPage1.Controls.Add(this._language2Name);
			this.tabPage1.Controls.Add(this._language2Label);
			this.tabPage1.Controls.Add(this._language1Name);
			this.tabPage1.Controls.Add(this._language1Label);
			this._L10NSharpExtender.SetLocalizableToolTip(this.tabPage1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.tabPage1, null);
			this._L10NSharpExtender.SetLocalizingId(this.tabPage1, "CollectionSettingsDialog.LanguageTab.LanguageTabLabel");
			this.tabPage1.Location = new System.Drawing.Point(4, 26);
			this.tabPage1.Name = "tabPage1";
			this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage1.Size = new System.Drawing.Size(642, 452);
			this.tabPage1.TabIndex = 0;
			this.tabPage1.Text = "Languages";
			this.tabPage1.UseVisualStyleBackColor = true;
			// 
			// _signLanguageLabel
			// 
			this._signLanguageLabel.AutoSize = true;
			this._signLanguageLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._signLanguageLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._signLanguageLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._signLanguageLabel, "CollectionSettingsDialog.LanguageTab.SignLanguageOptional");
			this._signLanguageLabel.Location = new System.Drawing.Point(27, 288);
			this._signLanguageLabel.Name = "_signLanguageLabel";
			this._signLanguageLabel.Size = new System.Drawing.Size(101, 19);
			this._signLanguageLabel.TabIndex = 19;
			this._signLanguageLabel.Text = "Sign Language   (Optional)";
			// 
			// _signLanguageName
			// 
			this._signLanguageName.AutoSize = true;
			this._signLanguageName.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._signLanguageName, null);
			this._L10NSharpExtender.SetLocalizationComment(this._signLanguageName, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._signLanguageName, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._signLanguageName, "CollectionSettingsDialog._signLanguageLabel");
			this._signLanguageName.Location = new System.Drawing.Point(26, 307);
			this._signLanguageName.Name = "_signLanguageName";
			this._signLanguageName.Size = new System.Drawing.Size(49, 19);
			this._signLanguageName.TabIndex = 20;
			this._signLanguageName.Text = "foobar";
			// 
			// _removeSignLanguageLink
			// 
			this._removeSignLanguageLink.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._removeSignLanguageLink, null);
			this._L10NSharpExtender.SetLocalizationComment(this._removeSignLanguageLink, null);
			this._L10NSharpExtender.SetLocalizingId(this._removeSignLanguageLink, "CollectionSettingsDialog.LanguageTab.RemoveLanguageLink");
			this._removeSignLanguageLink.Location = new System.Drawing.Point(159, 329);
			this._removeSignLanguageLink.Name = "_removeSignLanguageLink";
			this._removeSignLanguageLink.Size = new System.Drawing.Size(58, 19);
			this._removeSignLanguageLink.TabIndex = 22;
			this._removeSignLanguageLink.TabStop = true;
			this._removeSignLanguageLink.Text = "Remove";
			this._removeSignLanguageLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._removeSignLanguageButton_LinkClicked);
			// 
			// _changeSignLanguageLink
			// 
			this._changeSignLanguageLink.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._changeSignLanguageLink, null);
			this._L10NSharpExtender.SetLocalizationComment(this._changeSignLanguageLink, null);
			this._L10NSharpExtender.SetLocalizingId(this._changeSignLanguageLink, "CollectionSettingsDialog.LanguageTab.ChangeLanguageLink");
			this._changeSignLanguageLink.Location = new System.Drawing.Point(27, 329);
			this._changeSignLanguageLink.Name = "_changeSignLanguageLink";
			this._changeSignLanguageLink.Size = new System.Drawing.Size(65, 19);
			this._changeSignLanguageLink.TabIndex = 21;
			this._changeSignLanguageLink.TabStop = true;
			this._changeSignLanguageLink.Text = "Change...";
			this._changeSignLanguageLink.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._signLanguageChangeLink_LinkClicked);
			// 
			// _removeLanguage3Link
			// 
			this._removeLanguage3Link.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._removeLanguage3Link, null);
			this._L10NSharpExtender.SetLocalizationComment(this._removeLanguage3Link, null);
			this._L10NSharpExtender.SetLocalizingId(this._removeLanguage3Link, "CollectionSettingsDialog.LanguageTab.RemoveLanguageLink");
			this._removeLanguage3Link.Location = new System.Drawing.Point(159, 243);
			this._removeLanguage3Link.Name = "_removeLanguage3Link";
			this._removeLanguage3Link.Size = new System.Drawing.Size(58, 19);
			this._removeLanguage3Link.TabIndex = 18;
			this._removeLanguage3Link.TabStop = true;
			this._removeLanguage3Link.Text = "Remove";
			this._removeLanguage3Link.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._removeSecondNationalLanguageButton_LinkClicked);
			// 
			// _changeLanguage3Link
			// 
			this._changeLanguage3Link.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._changeLanguage3Link, null);
			this._L10NSharpExtender.SetLocalizationComment(this._changeLanguage3Link, null);
			this._L10NSharpExtender.SetLocalizingId(this._changeLanguage3Link, "CollectionSettingsDialog.LanguageTab.ChangeLanguageLink");
			this._changeLanguage3Link.Location = new System.Drawing.Point(27, 243);
			this._changeLanguage3Link.Name = "_changeLanguage3Link";
			this._changeLanguage3Link.Size = new System.Drawing.Size(65, 19);
			this._changeLanguage3Link.TabIndex = 17;
			this._changeLanguage3Link.TabStop = true;
			this._changeLanguage3Link.Text = "Change...";
			this._changeLanguage3Link.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._language3ChangeLink_LinkClicked);
			// 
			// _changeLanguage2Link
			// 
			this._changeLanguage2Link.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._changeLanguage2Link, null);
			this._L10NSharpExtender.SetLocalizationComment(this._changeLanguage2Link, null);
			this._L10NSharpExtender.SetLocalizingId(this._changeLanguage2Link, "CollectionSettingsDialog.LanguageTab.ChangeLanguageLink");
			this._changeLanguage2Link.Location = new System.Drawing.Point(27, 158);
			this._changeLanguage2Link.Name = "_changeLanguage2Link";
			this._changeLanguage2Link.Size = new System.Drawing.Size(65, 19);
			this._changeLanguage2Link.TabIndex = 16;
			this._changeLanguage2Link.TabStop = true;
			this._changeLanguage2Link.Text = "Change...";
			this._changeLanguage2Link.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._language2ChangeLink_LinkClicked);
			// 
			// _changeLanguage1Link
			// 
			this._changeLanguage1Link.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._changeLanguage1Link, null);
			this._L10NSharpExtender.SetLocalizationComment(this._changeLanguage1Link, null);
			this._L10NSharpExtender.SetLocalizingId(this._changeLanguage1Link, "CollectionSettingsDialog.LanguageTab.ChangeLanguageLink");
			this._changeLanguage1Link.Location = new System.Drawing.Point(27, 69);
			this._changeLanguage1Link.Name = "_changeLanguage1Link";
			this._changeLanguage1Link.Size = new System.Drawing.Size(65, 19);
			this._changeLanguage1Link.TabIndex = 15;
			this._changeLanguage1Link.TabStop = true;
			this._changeLanguage1Link.Text = "Change...";
			this._changeLanguage1Link.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._language1ChangeLink_LinkClicked);
			// 
			// _language3Name
			// 
			this._language3Name.AutoSize = true;
			this._language3Name.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language3Name, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language3Name, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._language3Name, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._language3Name, "CollectionSettingsDialog._nationalLanguage2Label");
			this._language3Name.Location = new System.Drawing.Point(26, 218);
			this._language3Name.Name = "_language3Name";
			this._language3Name.Size = new System.Drawing.Size(49, 19);
			this._language3Name.TabIndex = 14;
			this._language3Name.Text = "foobar";
			// 
			// _language3Label
			// 
			this._language3Label.AutoSize = true;
			this._language3Label.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language3Label, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language3Label, null);
			this._L10NSharpExtender.SetLocalizingId(this._language3Label, "CollectionSettingsDialog.LanguageTab._language3Label");
			this._language3Label.Location = new System.Drawing.Point(26, 198);
			this._language3Label.Name = "_language3Label";
			this._language3Label.Size = new System.Drawing.Size(316, 19);
			this._language3Label.TabIndex = 13;
			this._language3Label.Text = "Language 3 (e.g. Regional Language)   (Optional)";
			// 
			// _language2Name
			// 
			this._language2Name.AutoSize = true;
			this._language2Name.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language2Name, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language2Name, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._language2Name, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._language2Name, "CollectionSettingsDialog._nationalLanguage1Label");
			this._language2Name.Location = new System.Drawing.Point(26, 133);
			this._language2Name.Name = "_language2Name";
			this._language2Name.Size = new System.Drawing.Size(49, 19);
			this._language2Name.TabIndex = 11;
			this._language2Name.Text = "foobar";
			// 
			// _language2Label
			// 
			this._language2Label.AutoSize = true;
			this._language2Label.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language2Label, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language2Label, null);
			this._L10NSharpExtender.SetLocalizingId(this._language2Label, "CollectionSettingsDialog.LanguageTab._language2Label");
			this._language2Label.Location = new System.Drawing.Point(26, 113);
			this._language2Label.Name = "_language2Label";
			this._language2Label.Size = new System.Drawing.Size(238, 19);
			this._language2Label.TabIndex = 10;
			this._language2Label.Text = "Language 2 (e.g. National Language)";
			// 
			// _language1Name
			// 
			this._language1Name.AutoSize = true;
			this._language1Name.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language1Name, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language1Name, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._language1Name, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._language1Name, "CollectionSettingsDialog._vernacularLanguageName");
			this._language1Name.Location = new System.Drawing.Point(26, 44);
			this._language1Name.Name = "_language1Name";
			this._language1Name.Size = new System.Drawing.Size(49, 19);
			this._language1Name.TabIndex = 8;
			this._language1Name.Text = "foobar";
			// 
			// _language1Label
			// 
			this._language1Label.AutoSize = true;
			this._language1Label.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language1Label, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language1Label, null);
			this._L10NSharpExtender.SetLocalizingId(this._language1Label, "CollectionSettingsDialog.LanguageTab.VernacularLanguageLabel");
			this._language1Label.Location = new System.Drawing.Point(26, 24);
			this._language1Label.Name = "_language1Label";
			this._language1Label.Size = new System.Drawing.Size(106, 19);
			this._language1Label.TabIndex = 7;
			this._language1Label.Text = "Local Language";
			// 
			// tabPage2
			// 
			this.tabPage2.Controls.Add(this._numberStyleCombo);
			this.tabPage2.Controls.Add(this.label3);
			this.tabPage2.Controls.Add(this._fontSettings3Link);
			this.tabPage2.Controls.Add(this._fontSettings2Link);
			this.tabPage2.Controls.Add(this._fontSettings1Link);
			this.tabPage2.Controls.Add(this._xmatterList);
			this.tabPage2.Controls.Add(this._xmatterDescription);
			this.tabPage2.Controls.Add(this._fontComboLanguage3);
			this.tabPage2.Controls.Add(this._fontComboLanguage2);
			this.tabPage2.Controls.Add(this._fontComboLanguage1);
			this.tabPage2.Controls.Add(this._language3FontLabel);
			this.tabPage2.Controls.Add(this._language2FontLabel);
			this.tabPage2.Controls.Add(this._language1FontLabel);
			this.tabPage2.Controls.Add(this._xmatterPackLabel);
			this._L10NSharpExtender.SetLocalizableToolTip(this.tabPage2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.tabPage2, null);
			this._L10NSharpExtender.SetLocalizingId(this.tabPage2, "CollectionSettingsDialog.BookMakingTab.BookMakingTabLabel");
			this.tabPage2.Location = new System.Drawing.Point(4, 26);
			this.tabPage2.Name = "tabPage2";
			this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage2.Size = new System.Drawing.Size(610, 426);
			this.tabPage2.TabIndex = 1;
			this.tabPage2.Text = "Book Making";
			this.tabPage2.UseVisualStyleBackColor = true;
			// 
			// _numberStyleCombo
			// 
			this._numberStyleCombo.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this._numberStyleCombo.FormattingEnabled = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._numberStyleCombo, null);
			this._L10NSharpExtender.SetLocalizationComment(this._numberStyleCombo, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._numberStyleCombo, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._numberStyleCombo, "CollectionSettingsDialog._numberStyleCombo");
			this._numberStyleCombo.Location = new System.Drawing.Point(32, 302);
			this._numberStyleCombo.Name = "_numberStyleCombo";
			this._numberStyleCombo.Size = new System.Drawing.Size(189, 25);
			this._numberStyleCombo.TabIndex = 35;
			this._numberStyleCombo.SelectedIndexChanged += new System.EventHandler(this._numberStyleCombo_SelectedIndexChanged);
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.label3, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label3, null);
			this._L10NSharpExtender.SetLocalizingId(this.label3, "CollectionSettingsDialog.BookMakingTab.PageNumberingStyle.PageNumberingStyleLabel" +
        "");
			this.label3.Location = new System.Drawing.Point(28, 280);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(149, 19);
			this.label3.TabIndex = 34;
			this.label3.Text = "Page Numbering Style";
			// 
			// _fontSettings3Link
			// 
			this._fontSettings3Link.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._fontSettings3Link, null);
			this._L10NSharpExtender.SetLocalizationComment(this._fontSettings3Link, null);
			this._L10NSharpExtender.SetLocalizingId(this._fontSettings3Link, "CollectionSettingsDialog.BookMakingTab.SpecialScriptSettingsLink");
			this._fontSettings3Link.Location = new System.Drawing.Point(28, 248);
			this._fontSettings3Link.Name = "_fontSettings3Link";
			this._fontSettings3Link.Size = new System.Drawing.Size(141, 19);
			this._fontSettings3Link.TabIndex = 33;
			this._fontSettings3Link.TabStop = true;
			this._fontSettings3Link.Text = "Special Script Settings";
			this._fontSettings3Link.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._fontSettings3Link_LinkClicked);
			// 
			// _fontSettings2Link
			// 
			this._fontSettings2Link.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._fontSettings2Link, null);
			this._L10NSharpExtender.SetLocalizationComment(this._fontSettings2Link, null);
			this._L10NSharpExtender.SetLocalizingId(this._fontSettings2Link, "CollectionSettingsDialog.BookMakingTab.SpecialScriptSettingsLink");
			this._fontSettings2Link.Location = new System.Drawing.Point(28, 161);
			this._fontSettings2Link.Name = "_fontSettings2Link";
			this._fontSettings2Link.Size = new System.Drawing.Size(141, 19);
			this._fontSettings2Link.TabIndex = 32;
			this._fontSettings2Link.TabStop = true;
			this._fontSettings2Link.Text = "Special Script Settings";
			this._fontSettings2Link.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._fontSettings2Link_LinkClicked);
			// 
			// _fontSettings1Link
			// 
			this._fontSettings1Link.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._fontSettings1Link, null);
			this._L10NSharpExtender.SetLocalizationComment(this._fontSettings1Link, null);
			this._L10NSharpExtender.SetLocalizingId(this._fontSettings1Link, "CollectionSettingsDialog.BookMakingTab.SpecialScriptSettingsLink");
			this._fontSettings1Link.Location = new System.Drawing.Point(28, 74);
			this._fontSettings1Link.Name = "_fontSettings1Link";
			this._fontSettings1Link.Size = new System.Drawing.Size(141, 19);
			this._fontSettings1Link.TabIndex = 31;
			this._fontSettings1Link.TabStop = true;
			this._fontSettings1Link.Text = "Special Script Settings";
			this._fontSettings1Link.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this._fontSettings1Link_LinkClicked);
			// 
			// _xmatterList
			// 
			this._xmatterList.FullRowSelect = true;
			this._xmatterList.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
			this._xmatterList.HideSelection = false;
			this._xmatterList.Location = new System.Drawing.Point(293, 46);
			this._xmatterList.MultiSelect = false;
			this._xmatterList.Name = "_xmatterList";
			this._xmatterList.Size = new System.Drawing.Size(310, 88);
			this._xmatterList.TabIndex = 30;
			this._xmatterList.UseCompatibleStateImageBehavior = false;
			this._xmatterList.View = System.Windows.Forms.View.Details;
			this._xmatterList.SelectedIndexChanged += new System.EventHandler(this._xmatterList_SelectedIndexChanged);
			// 
			// _xmatterDescription
			// 
			this._xmatterDescription.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this._L10NSharpExtender.SetLocalizableToolTip(this._xmatterDescription, null);
			this._L10NSharpExtender.SetLocalizationComment(this._xmatterDescription, null);
			this._L10NSharpExtender.SetLocalizingId(this._xmatterDescription, "textBox1");
			this._xmatterDescription.Location = new System.Drawing.Point(293, 149);
			this._xmatterDescription.Multiline = true;
			this._xmatterDescription.Name = "_xmatterDescription";
			this._xmatterDescription.Size = new System.Drawing.Size(310, 68);
			this._xmatterDescription.TabIndex = 29;
			// 
			// _fontComboLanguage3
			// 
			this._fontComboLanguage3.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this._fontComboLanguage3.FormattingEnabled = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._fontComboLanguage3, null);
			this._L10NSharpExtender.SetLocalizationComment(this._fontComboLanguage3, null);
			this._L10NSharpExtender.SetLocalizingId(this._fontComboLanguage3, "CollectionSettingsDialog._fontComboLanguage3");
			this._fontComboLanguage3.Location = new System.Drawing.Point(31, 220);
			this._fontComboLanguage3.Name = "_fontComboLanguage3";
			this._fontComboLanguage3.Size = new System.Drawing.Size(190, 25);
			this._fontComboLanguage3.TabIndex = 25;
			this._fontComboLanguage3.SelectedIndexChanged += new System.EventHandler(this._fontComboLanguage3_SelectedIndexChanged);
			// 
			// _fontComboLanguage2
			// 
			this._fontComboLanguage2.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this._fontComboLanguage2.FormattingEnabled = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._fontComboLanguage2, null);
			this._L10NSharpExtender.SetLocalizationComment(this._fontComboLanguage2, null);
			this._L10NSharpExtender.SetLocalizingId(this._fontComboLanguage2, "CollectionSettingsDialog._fontComboLanguage2");
			this._fontComboLanguage2.Location = new System.Drawing.Point(31, 133);
			this._fontComboLanguage2.Name = "_fontComboLanguage2";
			this._fontComboLanguage2.Size = new System.Drawing.Size(190, 25);
			this._fontComboLanguage2.TabIndex = 23;
			this._fontComboLanguage2.SelectedIndexChanged += new System.EventHandler(this._fontComboLanguage2_SelectedIndexChanged);
			// 
			// _fontComboLanguage1
			// 
			this._fontComboLanguage1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this._fontComboLanguage1.FormattingEnabled = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._fontComboLanguage1, null);
			this._L10NSharpExtender.SetLocalizationComment(this._fontComboLanguage1, null);
			this._L10NSharpExtender.SetLocalizingId(this._fontComboLanguage1, "CollectionSettingsDialog._fontComboLanguage1");
			this._fontComboLanguage1.Location = new System.Drawing.Point(31, 46);
			this._fontComboLanguage1.Name = "_fontComboLanguage1";
			this._fontComboLanguage1.Size = new System.Drawing.Size(190, 25);
			this._fontComboLanguage1.TabIndex = 21;
			this._fontComboLanguage1.SelectedIndexChanged += new System.EventHandler(this._fontComboLanguage1_SelectedIndexChanged);
			// 
			// _language3FontLabel
			// 
			this._language3FontLabel.AutoSize = true;
			this._language3FontLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language3FontLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language3FontLabel, "{0} is a language name.");
			this._L10NSharpExtender.SetLocalizingId(this._language3FontLabel, "CollectionSettingsDialog.BookMakingTab.DefaultFontFor");
			this._language3FontLabel.Location = new System.Drawing.Point(27, 198);
			this._language3FontLabel.Name = "_language3FontLabel";
			this._language3FontLabel.Size = new System.Drawing.Size(131, 19);
			this._language3FontLabel.TabIndex = 24;
			this._language3FontLabel.Text = "Default Font for {0}";
			// 
			// _language2FontLabel
			// 
			this._language2FontLabel.AutoSize = true;
			this._language2FontLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language2FontLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language2FontLabel, "{0} is a language name.");
			this._L10NSharpExtender.SetLocalizingId(this._language2FontLabel, "CollectionSettingsDialog.BookMakingTab.DefaultFontFor");
			this._language2FontLabel.Location = new System.Drawing.Point(27, 111);
			this._language2FontLabel.Name = "_language2FontLabel";
			this._language2FontLabel.Size = new System.Drawing.Size(131, 19);
			this._language2FontLabel.TabIndex = 23;
			this._language2FontLabel.Text = "Default Font for {0}";
			// 
			// _language1FontLabel
			// 
			this._language1FontLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._language1FontLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._language1FontLabel, "{0} is a language name.");
			this._L10NSharpExtender.SetLocalizingId(this._language1FontLabel, "CollectionSettingsDialog.BookMakingTab.DefaultFontFor");
			this._language1FontLabel.Location = new System.Drawing.Point(27, 24);
			this._language1FontLabel.Name = "_language1FontLabel";
			this._language1FontLabel.Size = new System.Drawing.Size(250, 19);
			this._language1FontLabel.TabIndex = 22;
			this._language1FontLabel.Text = "Default Font for {0}";
			// 
			// _xmatterPackLabel
			// 
			this._xmatterPackLabel.AutoSize = true;
			this._xmatterPackLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._xmatterPackLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._xmatterPackLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._xmatterPackLabel, "CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack");
			this._xmatterPackLabel.Location = new System.Drawing.Point(289, 24);
			this._xmatterPackLabel.Name = "_xmatterPackLabel";
			this._xmatterPackLabel.Size = new System.Drawing.Size(156, 19);
			this._xmatterPackLabel.TabIndex = 1;
			this._xmatterPackLabel.Text = "Front/Back Matter Pack";
			// 
			// tabPage3
			// 
			this.tabPage3.Controls.Add(this._bloomCollectionName);
			this.tabPage3.Controls.Add(this.label1);
			this.tabPage3.Controls.Add(this._districtText);
			this.tabPage3.Controls.Add(this._provinceText);
			this.tabPage3.Controls.Add(this._countryText);
			this.tabPage3.Controls.Add(this._countryLabel);
			this.tabPage3.Controls.Add(this._districtLabel);
			this.tabPage3.Controls.Add(this._provinceLabel);
			this._L10NSharpExtender.SetLocalizableToolTip(this.tabPage3, null);
			this._L10NSharpExtender.SetLocalizationComment(this.tabPage3, null);
			this._L10NSharpExtender.SetLocalizingId(this.tabPage3, "CollectionSettingsDialog.ProjectInformationTab.ProjectInformationTabLabel");
			this.tabPage3.Location = new System.Drawing.Point(4, 26);
			this.tabPage3.Name = "tabPage3";
			this.tabPage3.Size = new System.Drawing.Size(610, 426);
			this.tabPage3.TabIndex = 2;
			this.tabPage3.Text = "Project Information";
			this.tabPage3.UseVisualStyleBackColor = true;
			// 
			// _bloomCollectionName
			// 
			this._bloomCollectionName.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._bloomCollectionName, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bloomCollectionName, null);
			this._L10NSharpExtender.SetLocalizingId(this._bloomCollectionName, "CollectionSettingsDialog.BloomProjectName");
			this._bloomCollectionName.Location = new System.Drawing.Point(32, 246);
			this._bloomCollectionName.Name = "_bloomCollectionName";
			this._bloomCollectionName.Size = new System.Drawing.Size(291, 25);
			this._bloomCollectionName.TabIndex = 22;
			this._bloomCollectionName.TextChanged += new System.EventHandler(this._bloomCollectionName_TextChanged);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "CollectionSettingsDialog.ProjectInformationTab.BloomCollectionName");
			this.label1.Location = new System.Drawing.Point(28, 224);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(158, 19);
			this.label1.TabIndex = 21;
			this.label1.Text = "Bloom Collection Name";
			// 
			// _districtText
			// 
			this._districtText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._districtText, null);
			this._L10NSharpExtender.SetLocalizationComment(this._districtText, null);
			this._L10NSharpExtender.SetLocalizingId(this._districtText, "CollectionSettingsDialog._districtText");
			this._districtText.Location = new System.Drawing.Point(32, 177);
			this._districtText.Name = "_districtText";
			this._districtText.Size = new System.Drawing.Size(291, 25);
			this._districtText.TabIndex = 5;
			// 
			// _provinceText
			// 
			this._provinceText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._provinceText, null);
			this._L10NSharpExtender.SetLocalizationComment(this._provinceText, null);
			this._L10NSharpExtender.SetLocalizingId(this._provinceText, "CollectionSettingsDialog._provinceText");
			this._provinceText.Location = new System.Drawing.Point(32, 112);
			this._provinceText.Name = "_provinceText";
			this._provinceText.Size = new System.Drawing.Size(291, 25);
			this._provinceText.TabIndex = 4;
			// 
			// _countryText
			// 
			this._countryText.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._countryText, null);
			this._L10NSharpExtender.SetLocalizationComment(this._countryText, null);
			this._L10NSharpExtender.SetLocalizingId(this._countryText, "CollectionSettingsDialog._countryText");
			this._countryText.Location = new System.Drawing.Point(32, 45);
			this._countryText.Name = "_countryText";
			this._countryText.Size = new System.Drawing.Size(291, 25);
			this._countryText.TabIndex = 3;
			// 
			// _countryLabel
			// 
			this._countryLabel.AutoSize = true;
			this._countryLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._countryLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._countryLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._countryLabel, "CollectionSettingsDialog.ProjectInformationTab.Country");
			this._countryLabel.Location = new System.Drawing.Point(28, 23);
			this._countryLabel.Name = "_countryLabel";
			this._countryLabel.Size = new System.Drawing.Size(60, 19);
			this._countryLabel.TabIndex = 2;
			this._countryLabel.Text = "Country";
			// 
			// _districtLabel
			// 
			this._districtLabel.AutoSize = true;
			this._districtLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._districtLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._districtLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._districtLabel, "CollectionSettingsDialog.ProjectInformationTab.District");
			this._districtLabel.Location = new System.Drawing.Point(28, 155);
			this._districtLabel.Name = "_districtLabel";
			this._districtLabel.Size = new System.Drawing.Size(55, 19);
			this._districtLabel.TabIndex = 1;
			this._districtLabel.Text = "District";
			// 
			// _provinceLabel
			// 
			this._provinceLabel.AutoSize = true;
			this._provinceLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._provinceLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._provinceLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._provinceLabel, "CollectionSettingsDialog.ProjectInformationTab.Province");
			this._provinceLabel.Location = new System.Drawing.Point(28, 90);
			this._provinceLabel.Name = "_provinceLabel";
			this._provinceLabel.Size = new System.Drawing.Size(63, 19);
			this._provinceLabel.TabIndex = 0;
			this._provinceLabel.Text = "Province";
			// 
			// _enterpriseTab
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._enterpriseTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._enterpriseTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._enterpriseTab, "CollectionSettingsDialog.EnterpriseTab.TabLabel");
			this._enterpriseTab.Location = new System.Drawing.Point(4, 26);
			this._enterpriseTab.Name = "_enterpriseTab";
			this._enterpriseTab.Padding = new System.Windows.Forms.Padding(3);
			this._enterpriseTab.Size = new System.Drawing.Size(610, 426);
			this._enterpriseTab.TabIndex = 4;
			this._enterpriseTab.Text = "Bloom Enterprise";
			this._enterpriseTab.UseVisualStyleBackColor = true;
			// 
			// _sharingTab
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._sharingTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._sharingTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._sharingTab, "CollectionSettingsDialog.SharingTab.TabLabel");
			this._sharingTab.Location = new System.Drawing.Point(4, 26);
			this._sharingTab.Name = "_sharingTab";
			this._sharingTab.Padding = new System.Windows.Forms.Padding(3);
			this._sharingTab.Size = new System.Drawing.Size(610, 426);
			this._sharingTab.TabIndex = 4;
			this._sharingTab.Text = "Team Collection";
			this._sharingTab.UseVisualStyleBackColor = true;
			// 
			// tabPage4
			// 
			this.tabPage4.Controls.Add(this.showTroubleShooterCheckBox);
			this.tabPage4.Controls.Add(this._automaticallyUpdate);
			this.tabPage4.Controls.Add(this._showExperimentalFeatures);
			this._L10NSharpExtender.SetLocalizableToolTip(this.tabPage4, null);
			this._L10NSharpExtender.SetLocalizationComment(this.tabPage4, null);
			this._L10NSharpExtender.SetLocalizingId(this.tabPage4, "CollectionSettingsDialog.AdvancedTab.AdvancedProgramSettingsTabLabel");
			this.tabPage4.Location = new System.Drawing.Point(4, 26);
			this.tabPage4.Name = "tabPage4";
			this.tabPage4.Padding = new System.Windows.Forms.Padding(3);
			this.tabPage4.Size = new System.Drawing.Size(610, 426);
			this.tabPage4.TabIndex = 3;
			this.tabPage4.Text = "Advanced Program Settings";
			this.tabPage4.UseVisualStyleBackColor = true;
			// 
			// showTroubleShooterCheckBox
			// 
			this.showTroubleShooterCheckBox.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this.showTroubleShooterCheckBox, null);
			this._L10NSharpExtender.SetLocalizationComment(this.showTroubleShooterCheckBox, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.showTroubleShooterCheckBox, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this.showTroubleShooterCheckBox, "showTroubleshooter");
			this.showTroubleShooterCheckBox.Location = new System.Drawing.Point(50, 170);
			this.showTroubleShooterCheckBox.Name = "showTroubleShooterCheckBox";
			this.showTroubleShooterCheckBox.Size = new System.Drawing.Size(237, 23);
			this.showTroubleShooterCheckBox.TabIndex = 6;
			this.showTroubleShooterCheckBox.Text = "Show Performance Troubleshooter";
			this.showTroubleShooterCheckBox.UseVisualStyleBackColor = true;
			this.showTroubleShooterCheckBox.CheckedChanged += new System.EventHandler(this.showTroubleShooterCheckBox_CheckedChanged);
			// 
			// _automaticallyUpdate
			// 
			this._automaticallyUpdate.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._automaticallyUpdate, null);
			this._L10NSharpExtender.SetLocalizationComment(this._automaticallyUpdate, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._automaticallyUpdate, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._automaticallyUpdate, "CollectionSettingsDialog.AdvancedTab.AutoUpdate");
			this._automaticallyUpdate.Location = new System.Drawing.Point(50, 124);
			this._automaticallyUpdate.Name = "_automaticallyUpdate";
			this._automaticallyUpdate.Size = new System.Drawing.Size(201, 23);
			this._automaticallyUpdate.TabIndex = 5;
			this._automaticallyUpdate.Text = "Automatically update Bloom";
			this._automaticallyUpdate.UseVisualStyleBackColor = true;
			this._automaticallyUpdate.CheckedChanged += new System.EventHandler(this._automaticallyUpdate_CheckedChanged);
			// 
			// _showExperimentalFeatures
			// 
			this._showExperimentalFeatures.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._showExperimentalFeatures, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showExperimentalFeatures, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._showExperimentalFeatures, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._showExperimentalFeatures, "CollectionSettingsDialog.AdvancedTab.Experimental.ShowExperimentalCommands");
			this._showExperimentalFeatures.Location = new System.Drawing.Point(50, 37);
			this._showExperimentalFeatures.Name = "_showExperimentalFeatures";
			this._showExperimentalFeatures.Size = new System.Drawing.Size(199, 23);
			this._showExperimentalFeatures.TabIndex = 4;
			this._showExperimentalFeatures.Text = "Show Experimental Features";
			this._showExperimentalFeatures.UseVisualStyleBackColor = true;
			this._showExperimentalFeatures.CheckedChanged += new System.EventHandler(this._showExperimentalFeatures_CheckedChanged);
			// 
			// _okButton
			// 
			this._okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._okButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._okButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._okButton, "Common.OKButton");
			this._okButton.Location = new System.Drawing.Point(454, 534);
			this._okButton.Name = "_okButton";
			this._okButton.Size = new System.Drawing.Size(91, 23);
			this._okButton.TabIndex = 1;
			this._okButton.Text = "&OK";
			this._okButton.UseVisualStyleBackColor = true;
			this._okButton.Click += new System.EventHandler(this._okButton_Click);
			// 
			// _restartReminder
			// 
			this._restartReminder.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._restartReminder.AutoSize = true;
			this._restartReminder.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._restartReminder.ForeColor = System.Drawing.Color.Firebrick;
			this._L10NSharpExtender.SetLocalizableToolTip(this._restartReminder, null);
			this._L10NSharpExtender.SetLocalizationComment(this._restartReminder, null);
			this._L10NSharpExtender.SetLocalizingId(this._restartReminder, "CollectionSettingsDialog.RestartMessage");
			this._restartReminder.Location = new System.Drawing.Point(305, 489);
			this._restartReminder.MaximumSize = new System.Drawing.Size(380, 0);
			this._restartReminder.Name = "_restartReminder";
			this._restartReminder.Size = new System.Drawing.Size(344, 38);
			this._restartReminder.TabIndex = 19;
			this._restartReminder.Text = "Bloom will close and re-open this project with the new settings.";
			this._restartReminder.Visible = false;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// _cancelButton
			// 
			this._cancelButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._cancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this._L10NSharpExtender.SetLocalizableToolTip(this._cancelButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._cancelButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._cancelButton, "Common.CancelButton");
			this._cancelButton.Location = new System.Drawing.Point(565, 534);
			this._cancelButton.Name = "_cancelButton";
			this._cancelButton.Size = new System.Drawing.Size(75, 23);
			this._cancelButton.TabIndex = 21;
			this._cancelButton.Text = "&Cancel";
			this._cancelButton.UseVisualStyleBackColor = true;
			this._cancelButton.Click += new System.EventHandler(this._cancelButton_Click);
			// 
			// settingsProtectionLauncherButton1
			// 
			this.settingsProtectionLauncherButton1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.settingsProtectionLauncherButton1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.settingsProtectionLauncherButton1, null);
			this._L10NSharpExtender.SetLocalizingId(this.settingsProtectionLauncherButton1, "CollectionSettingsDialog.SettingsProtectionLauncherButton");
			this.settingsProtectionLauncherButton1.Location = new System.Drawing.Point(13, 486);
			this.settingsProtectionLauncherButton1.Margin = new System.Windows.Forms.Padding(0);
			this.settingsProtectionLauncherButton1.Name = "settingsProtectionLauncherButton1";
			this.settingsProtectionLauncherButton1.Size = new System.Drawing.Size(257, 37);
			this.settingsProtectionLauncherButton1.TabIndex = 20;
			// 
			// _helpButton
			// 
			this._helpButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._helpButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._helpButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._helpButton, "Common.HelpButton");
			this._helpButton.Location = new System.Drawing.Point(13, 534);
			this._helpButton.Name = "_helpButton";
			this._helpButton.Size = new System.Drawing.Size(75, 23);
			this._helpButton.TabIndex = 22;
			this._helpButton.Text = "&Help";
			this._helpButton.UseVisualStyleBackColor = true;
			this._helpButton.Click += new System.EventHandler(this._helpButton_Click);
			// 
			// CollectionSettingsDialog
			// 
			this.AcceptButton = this._okButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.CancelButton = this._cancelButton;
			this.ClientSize = new System.Drawing.Size(652, 572);
			this.ControlBox = false;
			this.Controls.Add(this._helpButton);
			this.Controls.Add(this._cancelButton);
			this.Controls.Add(this.settingsProtectionLauncherButton1);
			this.Controls.Add(this._restartReminder);
			this.Controls.Add(this._okButton);
			this.Controls.Add(this._tab);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "CollectionSettingsDialog.CollectionSettingsWindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "CollectionSettingsDialog";
			this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
			this.Text = "Settings";
			this.Load += new System.EventHandler(this.OnLoad);
			this._tab.ResumeLayout(false);
			this.tabPage1.ResumeLayout(false);
			this.tabPage1.PerformLayout();
			this.tabPage2.ResumeLayout(false);
			this.tabPage2.PerformLayout();
			this.tabPage3.ResumeLayout(false);
			this.tabPage3.PerformLayout();
			this.tabPage4.ResumeLayout(false);
			this.tabPage4.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.TabControl _tab;
		private System.Windows.Forms.TabPage tabPage1;
		private System.Windows.Forms.TabPage tabPage2;
		private System.Windows.Forms.TabPage tabPage3;
		private System.Windows.Forms.Button _okButton;
		protected System.Windows.Forms.Label _language1Label;
		private System.Windows.Forms.LinkLabel _changeLanguage3Link;
		private System.Windows.Forms.LinkLabel _changeLanguage2Link;
		private System.Windows.Forms.LinkLabel _changeLanguage1Link;
		protected System.Windows.Forms.Label _language3Name;
		protected System.Windows.Forms.Label _language3Label;
		protected System.Windows.Forms.Label _language2Name;
        protected System.Windows.Forms.Label _language2Label;
		private System.Windows.Forms.Label _xmatterPackLabel;
		private System.Windows.Forms.TextBox _districtText;
		private System.Windows.Forms.TextBox _provinceText;
		private System.Windows.Forms.TextBox _countryText;
		private System.Windows.Forms.Label _countryLabel;
		private System.Windows.Forms.Label _districtLabel;
		private System.Windows.Forms.Label _provinceLabel;
		private System.Windows.Forms.LinkLabel _removeLanguage3Link;
		private System.Windows.Forms.Label _restartReminder;
		private SIL.Windows.Forms.SettingProtection.SettingsProtectionLauncherButton settingsProtectionLauncherButton1;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.TabPage tabPage4;
		private System.Windows.Forms.ToolTip toolTip1;
        protected System.Windows.Forms.Label _language1Name;
        private System.Windows.Forms.TextBox _bloomCollectionName;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Button _cancelButton;
        private System.Windows.Forms.Label _language1FontLabel;
        private System.Windows.Forms.ComboBox _fontComboLanguage1;
		private System.Windows.Forms.Label _language2FontLabel;
		private System.Windows.Forms.ComboBox _fontComboLanguage2;
		private System.Windows.Forms.Label _language3FontLabel;
		private System.Windows.Forms.ComboBox _fontComboLanguage3;
		private System.Windows.Forms.CheckBox _showExperimentalFeatures;
		private Button _helpButton;
		private TextBox _xmatterDescription;
		private ListView _xmatterList;
		private CheckBox _automaticallyUpdate;
		private LinkLabel _fontSettings3Link;
		private LinkLabel _fontSettings2Link;
		private LinkLabel _fontSettings1Link;
		private ComboBox _numberStyleCombo;
		private Label label3;
		private CheckBox showTroubleShooterCheckBox;
		private TabPage _enterpriseTab;
		private TabPage _sharingTab;
		private LinkLabel _removeSignLanguageLink;
		private LinkLabel _changeSignLanguageLink;
		protected Label _signLanguageName;
		protected Label _signLanguageLabel;
	}
}
