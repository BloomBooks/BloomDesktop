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
			this._removeSignLanguageLink = new System.Windows.Forms.LinkLabel();
			this._changeSignLanguageLink = new System.Windows.Forms.LinkLabel();
			this._signLanguageName = new System.Windows.Forms.Label();
			this._signLanguageLabel = new System.Windows.Forms.Label();
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
			this._bookMakingTab = new System.Windows.Forms.TabPage();
			this.bookMakingSettingsReactControl = new Bloom.web.ReactControl();
			this.tabPage3 = new System.Windows.Forms.TabPage();
			this._noRenameTeamCollectionLabel = new System.Windows.Forms.Label();
			this._bloomCollectionName = new System.Windows.Forms.TextBox();
			this.label1 = new System.Windows.Forms.Label();
			this._districtText = new System.Windows.Forms.TextBox();
			this._provinceText = new System.Windows.Forms.TextBox();
			this._countryText = new System.Windows.Forms.TextBox();
			this._countryLabel = new System.Windows.Forms.Label();
			this._districtLabel = new System.Windows.Forms.Label();
			this._provinceLabel = new System.Windows.Forms.Label();
			this._enterpriseTab = new System.Windows.Forms.TabPage();
			this._enterpriseSettingsControl = new web.ReactControl();
			this._teamCollectionTab = new System.Windows.Forms.TabPage();
			this.teamCollectionSettingsReactControl = new Bloom.web.ReactControl();
			this.tabPage4 = new System.Windows.Forms.TabPage();
			this._automaticallyUpdate = new System.Windows.Forms.CheckBox();
			this.label2 = new System.Windows.Forms.Label();
			this._showExperimentalBookSources = new System.Windows.Forms.CheckBox();
			this._enterpriseRequiredForTeamCollection = new System.Windows.Forms.Label();
			this._allowTeamCollection = new System.Windows.Forms.CheckBox();
			this._enterpriseRequiredForSpreadsheetImportExport = new System.Windows.Forms.Label();
			this._allowSpreadsheetImportExport = new System.Windows.Forms.CheckBox();
			this._okButton = new System.Windows.Forms.Button();
			this._restartReminder = new System.Windows.Forms.Label();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._cancelButton = new System.Windows.Forms.Button();
			this.settingsProtectionLauncherButton1 = new SIL.Windows.Forms.SettingProtection.SettingsProtectionLauncherButton();
			this._helpButton = new System.Windows.Forms.Button();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this._tab.SuspendLayout();
			this.tabPage1.SuspendLayout();
			this._bookMakingTab.SuspendLayout();
			this.tabPage3.SuspendLayout();
			this._teamCollectionTab.SuspendLayout();
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
			this._tab.Controls.Add(this._bookMakingTab);
			this._tab.Controls.Add(this.tabPage3);
			this._tab.Controls.Add(this._enterpriseTab);
			this._tab.Controls.Add(this._teamCollectionTab);
			this._tab.Controls.Add(this.tabPage4);
			this._tab.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._tab.Location = new System.Drawing.Point(1, 2);
			this._tab.Name = "_tab";
			this._tab.SelectedIndex = 0;
			this._tab.Size = new System.Drawing.Size(650, 482);
			this._tab.TabIndex = 0;
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
			this.tabPage1.UseVisualStyleBackColor = false;
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
			// _signLanguageLabel
			// 
			this._signLanguageLabel.AutoSize = true;
			this._signLanguageLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._signLanguageLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._signLanguageLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._signLanguageLabel, "CollectionSettingsDialog.LanguageTab.SignLanguageOptional");
			this._signLanguageLabel.Location = new System.Drawing.Point(27, 288);
			this._signLanguageLabel.Name = "_signLanguageLabel";
			this._signLanguageLabel.Size = new System.Drawing.Size(178, 19);
			this._signLanguageLabel.TabIndex = 19;
			this._signLanguageLabel.Text = "Sign Language   (Optional)";
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
			// _bookMakingTab
			//
			this._bookMakingTab.Controls.Add(this.bookMakingSettingsReactControl);
			this._L10NSharpExtender.SetLocalizableToolTip(this._bookMakingTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bookMakingTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._bookMakingTab, "CollectionSettingsDialog.BookMakingTab.BookMakingTabLabel");
			this._bookMakingTab.Location = new System.Drawing.Point(4, 26);
			this._bookMakingTab.Name = "_bookMakingTab";
			this._bookMakingTab.Padding = new System.Windows.Forms.Padding(3);
			this._bookMakingTab.Size = new System.Drawing.Size(642, 452);
			this._bookMakingTab.TabIndex = 1;
			this._bookMakingTab.Text = "Book Making";
			this._bookMakingTab.UseVisualStyleBackColor = false;
			this._bookMakingTab.BackColor = SystemColors.Control;
			// 
			// bookMakingSettingsReactControl
			//
			this.bookMakingSettingsReactControl.BackColor = SystemColors.Control;
			this.bookMakingSettingsReactControl.Dock = DockStyle.Fill;
			this.bookMakingSettingsReactControl.JavascriptBundleName = "bookMakingSettingsBundle";
			this._L10NSharpExtender.SetLocalizableToolTip(this.bookMakingSettingsReactControl, null);
			this._L10NSharpExtender.SetLocalizationComment(this.bookMakingSettingsReactControl, null);
			this._L10NSharpExtender.SetLocalizingId(this.bookMakingSettingsReactControl, "ReactControl");
			this.bookMakingSettingsReactControl.Location = new System.Drawing.Point(3, 3);
			this.bookMakingSettingsReactControl.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
			this.bookMakingSettingsReactControl.Name = "bookMakingSettingsReactControl";
			this.bookMakingSettingsReactControl.Size = new System.Drawing.Size(636, 446);
			this.bookMakingSettingsReactControl.TabIndex = 0;
			// 
			// tabPage3
			// 
			this.tabPage3.Controls.Add(this._noRenameTeamCollectionLabel);
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
			this.tabPage3.Size = new System.Drawing.Size(642, 452);
			this.tabPage3.TabIndex = 2;
			this.tabPage3.Text = "Project Information";
			this.tabPage3.UseVisualStyleBackColor = false;
			// 
			// _noRenameTeamCollectionLabel
			// 
			this._noRenameTeamCollectionLabel.ForeColor = System.Drawing.SystemColors.ControlDark;
			this._L10NSharpExtender.SetLocalizableToolTip(this._noRenameTeamCollectionLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._noRenameTeamCollectionLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._noRenameTeamCollectionLabel, "NoRenameTeamCollection");
			this._noRenameTeamCollectionLabel.Location = new System.Drawing.Point(32, 278);
			this._noRenameTeamCollectionLabel.Name = "_noRenameTeamCollectionLabel";
			this._noRenameTeamCollectionLabel.Size = new System.Drawing.Size(291, 95);
			this._noRenameTeamCollectionLabel.TabIndex = 23;
			this._noRenameTeamCollectionLabel.Text = "The collection name cannot be changed because this is a Team Collection. Contact " +
    "the Bloom team for more information.";
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
			this._enterpriseTab.Controls.Add(this._enterpriseSettingsControl);
			this._L10NSharpExtender.SetLocalizableToolTip(this._enterpriseTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._enterpriseTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._enterpriseTab, "CollectionSettingsDialog.EnterpriseTab.TabLabel");
			this._enterpriseTab.Location = new System.Drawing.Point(4, 26);
			this._enterpriseTab.Name = "_enterpriseTab";
			this._enterpriseTab.Padding = new System.Windows.Forms.Padding(3);
			this._enterpriseTab.Size = new System.Drawing.Size(642, 452);
			this._enterpriseTab.TabIndex = 3;
			this._enterpriseTab.Text = "Bloom Enterprise";
			this._enterpriseTab.UseVisualStyleBackColor = false;
			this._enterpriseTab.BackColor = SystemColors.Control;
			// 
			// _enterpriseSettingsControl
			// 
			this._enterpriseSettingsControl.BackColor = SystemColors.Control;
			this._enterpriseSettingsControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this._enterpriseSettingsControl.JavascriptBundleName = "enterpriseSettingsBundle";
			this._L10NSharpExtender.SetLocalizableToolTip(this._enterpriseSettingsControl, null);
			this._L10NSharpExtender.SetLocalizationComment(this._enterpriseSettingsControl, null);
			this._L10NSharpExtender.SetLocalizingId(this._enterpriseSettingsControl, "ReactControl");
			this._enterpriseSettingsControl.Location = new System.Drawing.Point(3, 3);
			this._enterpriseSettingsControl.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
			this._enterpriseSettingsControl.Name = "enterpriseSettingsControl";
			this._enterpriseSettingsControl.Size = new System.Drawing.Size(636, 446);
			this._enterpriseSettingsControl.TabIndex = 0;
			// 
			// _teamCollectionTab
			// 
			this._teamCollectionTab.Controls.Add(this.teamCollectionSettingsReactControl);
			this._L10NSharpExtender.SetLocalizableToolTip(this._teamCollectionTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._teamCollectionTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._teamCollectionTab, "TeamCollection.TeamCollection");
			this._teamCollectionTab.Location = new System.Drawing.Point(4, 26);
			this._teamCollectionTab.Name = "_teamCollectionTab";
			this._teamCollectionTab.Padding = new System.Windows.Forms.Padding(3);
			this._teamCollectionTab.Size = new System.Drawing.Size(642, 452);
			this._teamCollectionTab.TabIndex = 4;
			this._teamCollectionTab.Text = "Team Collection";
			this._teamCollectionTab.UseVisualStyleBackColor = true;
			// 
			// teamCollectionSettingsReactControl
			// 
			this.teamCollectionSettingsReactControl.BackColor = System.Drawing.Color.White;
			this.teamCollectionSettingsReactControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this.teamCollectionSettingsReactControl.JavascriptBundleName = "teamCollectionSettingsBundle";
			this._L10NSharpExtender.SetLocalizableToolTip(this.teamCollectionSettingsReactControl, null);
			this._L10NSharpExtender.SetLocalizationComment(this.teamCollectionSettingsReactControl, null);
			this._L10NSharpExtender.SetLocalizingId(this.teamCollectionSettingsReactControl, "ReactControl");
			this.teamCollectionSettingsReactControl.Location = new System.Drawing.Point(3, 3);
			this.teamCollectionSettingsReactControl.Margin = new System.Windows.Forms.Padding(3, 4, 3, 4);
			this.teamCollectionSettingsReactControl.Name = "teamCollectionSettingsReactControl";
			this.teamCollectionSettingsReactControl.Size = new System.Drawing.Size(636, 446);
			this.teamCollectionSettingsReactControl.TabIndex = 0;
            // 
            // tabPage4
            // 
            this.tabPage4.Controls.Add(this._automaticallyUpdate);
            this.tabPage4.Controls.Add(this.label2);
            this.tabPage4.Controls.Add(this._showExperimentalBookSources);
			this.tabPage4.Controls.Add(this._enterpriseRequiredForTeamCollection);
			this.tabPage4.Controls.Add(this._allowTeamCollection);
			this.tabPage4.Controls.Add(this._enterpriseRequiredForSpreadsheetImportExport);
			this.tabPage4.Controls.Add(this._allowSpreadsheetImportExport);
			this._L10NSharpExtender.SetLocalizableToolTip(this.tabPage4, null);
			this._L10NSharpExtender.SetLocalizationComment(this.tabPage4, null);
			this._L10NSharpExtender.SetLocalizingId(this.tabPage4, "CollectionSettingsDialog.AdvancedTab.AdvancedProgramSettingsTabLabel");
			this.tabPage4.Location = new System.Drawing.Point(4, 26);
			this.tabPage4.Name = "tabPage4";
			this.tabPage4.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage4.Size = new System.Drawing.Size(642, 452);
            this.tabPage4.TabIndex = 5;
            this.tabPage4.Text = "Advanced Program Settings";
            // 
            // _automaticallyUpdate
            // 
			this._automaticallyUpdate.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._automaticallyUpdate, null);
			this._L10NSharpExtender.SetLocalizationComment(this._automaticallyUpdate, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._automaticallyUpdate, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._automaticallyUpdate, "CollectionSettingsDialog.AdvancedTab.AutoUpdate");
			this._automaticallyUpdate.Location = new System.Drawing.Point(27, 24);
			this._automaticallyUpdate.Name = "_automaticallyUpdate";
			this._automaticallyUpdate.Size = new System.Drawing.Size(203, 23);
			this._automaticallyUpdate.TabIndex = 5;
			this._automaticallyUpdate.Text = "Automatically Update Bloom";
			this._automaticallyUpdate.UseVisualStyleBackColor = false;
			this._automaticallyUpdate.CheckedChanged += new System.EventHandler(this._automaticallyUpdate_CheckedChanged);
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.Font = new System.Drawing.Font("Segoe UI Semibold", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this.label2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label2, null);
			this._L10NSharpExtender.SetLocalizingId(this.label2, "CollectionSettingsDialog.AdvancedTab.ExperimentalFeaturesLabel");
			this.label2.Location = new System.Drawing.Point(23, 70);
			this.label2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(146, 19);
			this.label2.TabIndex = 6;
			this.label2.Text = "Experimental Features";
			// 
			// _showExperimentalBookSources
			// 
			this._showExperimentalBookSources.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._showExperimentalBookSources, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showExperimentalBookSources, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._showExperimentalBookSources, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._showExperimentalBookSources, "CollectionSettingsDialog.AdvancedTab.Experimental.ShowExperimentalBookSources");
			this._showExperimentalBookSources.Location = new System.Drawing.Point(27, 100);
			this._showExperimentalBookSources.Name = "_showExperimentalBookSources";
			this._showExperimentalBookSources.Size = new System.Drawing.Size(229, 23);
			this._showExperimentalBookSources.TabIndex = 7;
			this._showExperimentalBookSources.Text = "Show Experimental Book Sources";
			this._showExperimentalBookSources.UseVisualStyleBackColor = false;
			this._showExperimentalBookSources.CheckedChanged += new System.EventHandler(this._showExperimentalBookSources_CheckedChanged);
			// 
			// _enterpriseRequiredForTeamCollection
			// 
			this._enterpriseRequiredForTeamCollection.Image = global::Bloom.Properties.Resources.enterpriseBadge;
			this._L10NSharpExtender.SetLocalizableToolTip(this._enterpriseRequiredForTeamCollection, "To use this feature, you\'ll need to enable Bloom Enterprise.");
			this._L10NSharpExtender.SetLocalizationComment(this._enterpriseRequiredForTeamCollection, null);
			this._L10NSharpExtender.SetLocalizingId(this._enterpriseRequiredForTeamCollection, "CollectionSettingsDialog.RequiresEnterprise");
			this._enterpriseRequiredForTeamCollection.Location = new System.Drawing.Point(0, 129);
			this._enterpriseRequiredForTeamCollection.Name = "_enterpriseRequiredForTeamCollection";
			this._enterpriseRequiredForTeamCollection.Size = new System.Drawing.Size(23, 23);
			this._enterpriseRequiredForTeamCollection.TabIndex = 8;
			// 
			// _allowTeamCollection
			// 
			this._allowTeamCollection.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._allowTeamCollection, null);
			this._L10NSharpExtender.SetLocalizationComment(this._allowTeamCollection, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._allowTeamCollection, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._allowTeamCollection, "TeamCollection.TeamCollections");
			this._allowTeamCollection.Location = new System.Drawing.Point(27, 129);
			this._allowTeamCollection.Name = "_allowTeamCollection";
			this._allowTeamCollection.Size = new System.Drawing.Size(130, 23);
			this._allowTeamCollection.TabIndex = 9;
			this._allowTeamCollection.Text = "Team Collections";
			this._allowTeamCollection.UseVisualStyleBackColor = false;
			this._allowTeamCollection.CheckedChanged += new System.EventHandler(this._allowTeamCollection_CheckedChanged);
			// 
			// _enterpriseRequiredForSpreadsheetImportExport
			// 
			this._enterpriseRequiredForSpreadsheetImportExport.Image = global::Bloom.Properties.Resources.enterpriseBadge;
			this._L10NSharpExtender.SetLocalizableToolTip(this._enterpriseRequiredForSpreadsheetImportExport, "To use this feature, you\'ll need to enable Bloom Enterprise.");
			this._L10NSharpExtender.SetLocalizationComment(this._enterpriseRequiredForSpreadsheetImportExport, null);
			this._L10NSharpExtender.SetLocalizingId(this._enterpriseRequiredForSpreadsheetImportExport, "CollectionSettingsDialog.RequiresEnterprise");
			this._enterpriseRequiredForSpreadsheetImportExport.Location = new System.Drawing.Point(0, 158);
			this._enterpriseRequiredForSpreadsheetImportExport.Name = "_enterpriseRequiredForSpreadsheetImportExport";
			this._enterpriseRequiredForSpreadsheetImportExport.Size = new System.Drawing.Size(23, 23);
			this._enterpriseRequiredForSpreadsheetImportExport.TabIndex = 10;
			// 
			// _allowSpreadsheetImportExport
			// 
			this._allowSpreadsheetImportExport.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._allowSpreadsheetImportExport, null);
			this._L10NSharpExtender.SetLocalizationComment(this._allowSpreadsheetImportExport, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._allowSpreadsheetImportExport, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._allowSpreadsheetImportExport, "CollectionSettingsDialog.AdvancedTab.Experimental.SpreadsheetImportExport");
			this._allowSpreadsheetImportExport.Location = new System.Drawing.Point(27, 158);
			this._allowSpreadsheetImportExport.Name = "_allowSpreadsheetImportExport";
			this._allowSpreadsheetImportExport.Size = new System.Drawing.Size(193, 23);
			this._allowSpreadsheetImportExport.TabIndex = 11;
			this._allowSpreadsheetImportExport.Text = "Spreadsheet Import/Export";
			this._allowSpreadsheetImportExport.UseVisualStyleBackColor = false;
			this._allowSpreadsheetImportExport.CheckedChanged += new System.EventHandler(this._allowSpreadsheetImportExport_CheckedChanged);
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
			this._okButton.UseVisualStyleBackColor = false;
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
			this._cancelButton.UseVisualStyleBackColor = false;
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
            this._helpButton.UseVisualStyleBackColor = false;
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
			this._bookMakingTab.ResumeLayout(false);
			this.tabPage3.ResumeLayout(false);
			this.tabPage3.PerformLayout();
			this._teamCollectionTab.ResumeLayout(false);
			this.tabPage4.ResumeLayout(false);
			this.tabPage4.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();
		}

		#endregion

		private System.Windows.Forms.TabControl _tab;
		private System.Windows.Forms.TabPage tabPage1;
		private TabPage _bookMakingTab;
		private web.ReactControl bookMakingSettingsReactControl;
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
		private CheckBox _automaticallyUpdate;
		private Label label2;
		private System.Windows.Forms.CheckBox _showExperimentalBookSources;
		private Label _enterpriseRequiredForTeamCollection;
		private CheckBox _allowTeamCollection;
		private Label _enterpriseRequiredForSpreadsheetImportExport;
		private CheckBox _allowSpreadsheetImportExport;
		private Button _helpButton;
		private TabPage _enterpriseTab;
		private web.ReactControl _enterpriseSettingsControl;
		private TabPage _teamCollectionTab;
		private web.ReactControl teamCollectionSettingsReactControl;
		private LinkLabel _removeSignLanguageLink;
		private LinkLabel _changeSignLanguageLink;
		protected Label _signLanguageName;
		protected Label _signLanguageLabel;
		private Label _noRenameTeamCollectionLabel;
	}
}
