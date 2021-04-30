using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Bloom.Properties;
using L10NSharp;
using L10NSharp.UI;
using Messir.Windows.Forms;
using SIL.Windows.Forms.SettingProtection;

namespace Bloom.Workspace
{
    partial class WorkspaceView
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;

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
			this.components = new Container();
			this.toolTip1 = new ToolTip(this.components);
			this._settingsLauncherHelper = new SettingsProtectionHelper(this.components);
			this._containerPanel = new Panel();
			this._toolSpecificPanel = new Panel();
			this._L10NSharpExtender = new L10NSharpExtender(this.components);
			this._tabStrip = new TabStrip();
			this._collectionTab = new TabStripButton();
			this._editTab = new TabStripButton();
			this._publishTab = new TabStripButton();
			this._toolStrip = new ToolStrip();
			this._uiLanguageMenu = new ToolStripDropDownButton();
			this._helpMenu = new ToolStripDropDownButton();
			this._documentationMenuItem = new ToolStripMenuItem();
			this._trainingVideosMenuItem = new ToolStripMenuItem();
			this._keyBloomConceptsMenuItem = new ToolStripMenuItem();
			this.buildingReaderTemplatesMenuItem = new ToolStripMenuItem();
			this.usingReaderTemplatesMenuItem = new ToolStripMenuItem();
			this.toolStripSeparator1 = new ToolStripSeparator();
			this._askAQuestionMenuItem = new ToolStripMenuItem();
			this._requestAFeatureMenuItem = new ToolStripMenuItem();
			this._reportAProblemMenuItem = new ToolStripMenuItem();
			this._showLogMenuItem = new ToolStripMenuItem();
			this._divider1 = new ToolStripSeparator();
			this._releaseNotesMenuItem = new ToolStripMenuItem();
			this._checkForNewVersionMenuItem = new ToolStripMenuItem();
			this._registrationMenuItem = new ToolStripMenuItem();
			this._divider2 = new ToolStripSeparator();
			this._webSiteMenuItem = new ToolStripMenuItem();
			this._aboutBloomMenuItem = new ToolStripMenuItem();
			this._panelHoldingToolStrip = new NestedDockedChildPanel();
			this._applicationUpdateCheckTimer = new Timer(this.components);
			((ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this._tabStrip.SuspendLayout();
			this._toolStrip.SuspendLayout();
			this._panelHoldingToolStrip.SuspendLayout();
			this.SuspendLayout();
			// 
			// _containerPanel
			// 
			this._containerPanel.Anchor = ((AnchorStyles)((((AnchorStyles.Top | AnchorStyles.Bottom) 
			                                                | AnchorStyles.Left) 
			                                               | AnchorStyles.Right)));
			this._containerPanel.Location = new Point(0, 74);
			this._containerPanel.Name = "_containerPanel";
			this._containerPanel.Size = new Size(1098, 463);
			this._containerPanel.TabIndex = 16;
			// 
			// _toolSpecificPanel
			// 
			this._toolSpecificPanel.Anchor = ((AnchorStyles)(((AnchorStyles.Top | AnchorStyles.Left) 
			                                                  | AnchorStyles.Right)));
			this._toolSpecificPanel.BackColor = Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._toolSpecificPanel.Location = new Point(291, 2);
			this._toolSpecificPanel.Name = "_toolSpecificPanel";
			this._toolSpecificPanel.Size = new Size(718, 66);
			this._toolSpecificPanel.TabIndex = 17;
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "HelpMenu";
			// 
			// _tabStrip
			// 
			this._tabStrip.BackColor = Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._tabStrip.FlipButtons = false;
			this._tabStrip.GripStyle = ToolStripGripStyle.Hidden;
			this._tabStrip.ImageScalingSize = new Size(32, 32);
			this._tabStrip.Items.AddRange(new ToolStripItem[] {
            this._collectionTab,
            this._editTab,
            this._publishTab});
			this._L10NSharpExtender.SetLocalizableToolTip(this._tabStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._tabStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._tabStrip, LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._tabStrip, "WorkspaceView._tabStrip");
			this._tabStrip.Location = new Point(0, 0);
			this._tabStrip.Name = "_tabStrip";
			this._tabStrip.RenderStyle = ToolStripRenderMode.ManagerRenderMode;
			this._tabStrip.SelectedTab = this._publishTab;
			this._tabStrip.Size = new Size(1098, 71);
			this._tabStrip.TabIndex = 15;
			this._tabStrip.Text = "tabStrip1";
			this._tabStrip.UseVisualStyles = false;
			this._tabStrip.SelectedTabChanged += new EventHandler<SelectedTabChangedEventArgs>(this._tabStrip_SelectedTabChanged);
			this._tabStrip.BackColorChanged += new EventHandler(this._tabStrip_BackColorChanged);
			// 
			// _collectionTab
			// 
			this._collectionTab.BackColor = Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._collectionTab.BarColor = Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._collectionTab.ForeColor = Color.Black;
			this._collectionTab.HotTextColor = Color.Black;
			this._collectionTab.Image = Resources.library32x32;
			this._collectionTab.ImageTransparentColor = Color.Magenta;
			this._collectionTab.IsSelected = false;
			this._L10NSharpExtender.SetLocalizableToolTip(this._collectionTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._collectionTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._collectionTab, "CollectionTab.Collections");
			this._collectionTab.Margin = new Padding(0);
			this._collectionTab.Name = "_collectionTab";
			this._collectionTab.Padding = new Padding(0);
			this._collectionTab.SelectedFont = new Font("Segoe UI", 9F, FontStyle.Bold);
			this._collectionTab.SelectedTextColor = Color.WhiteSmoke;
			this._collectionTab.Size = new Size(103, 71);
			this._collectionTab.Text = "Collections";
			this._collectionTab.TextImageRelation = TextImageRelation.ImageAboveText;
			this._collectionTab.TextChanged += new EventHandler(this.HandleTabTextChanged);
			// 
			// _editTab
			// 
			this._editTab.BackColor = Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._editTab.BarColor = Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._editTab.ForeColor = Color.Black;
			this._editTab.HotTextColor = Color.Black;
			this._editTab.Image = Resources.edit;
			this._editTab.ImageTransparentColor = Color.Magenta;
			this._editTab.IsSelected = false;
			this._L10NSharpExtender.SetLocalizableToolTip(this._editTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._editTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._editTab, "EditTab.Edit");
			this._editTab.Margin = new Padding(0);
			this._editTab.Name = "_editTab";
			this._editTab.Padding = new Padding(0);
			this._editTab.SelectedFont = new Font("Segoe UI", 9F, FontStyle.Bold);
			this._editTab.SelectedTextColor = Color.WhiteSmoke;
			this._editTab.Size = new Size(69, 71);
			this._editTab.Text = "Edit";
			this._editTab.TextImageRelation = TextImageRelation.ImageAboveText;
			this._editTab.TextChanged += new EventHandler(this.HandleTabTextChanged);
			// 
			// _publishTab
			// 
			this._publishTab.BackColor = Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._publishTab.BarColor = Color.FromArgb(((int)(((byte)(214)))), ((int)(((byte)(86)))), ((int)(((byte)(73)))));
			this._publishTab.Checked = true;
			this._publishTab.ForeColor = Color.Black;
			this._publishTab.HotTextColor = Color.Black;
			this._publishTab.Image = Resources.publish32x32;
			this._publishTab.ImageScaling = ToolStripItemImageScaling.None;
			this._publishTab.ImageTransparentColor = Color.Magenta;
			this._publishTab.IsSelected = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._publishTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._publishTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._publishTab, "PublishTab.Publish");
			this._publishTab.Margin = new Padding(0);
			this._publishTab.Name = "_publishTab";
			this._publishTab.Padding = new Padding(0);
			this._publishTab.SelectedFont = new Font("Segoe UI", 9F, FontStyle.Bold);
			this._publishTab.SelectedTextColor = Color.WhiteSmoke;
			this._publishTab.Size = new Size(83, 71);
			this._publishTab.Text = "Publish";
			this._publishTab.TextImageRelation = TextImageRelation.ImageAboveText;
			this._publishTab.TextChanged += new EventHandler(this.HandleTabTextChanged);
			// 
			// _toolStrip
			// 
			this._toolStrip.BackColor = Color.Transparent;
			this._toolStrip.Dock = DockStyle.Right;
			this._toolStrip.GripMargin = new Padding(0);
			this._toolStrip.GripStyle = ToolStripGripStyle.Hidden;
			this._toolStrip.Items.AddRange(new ToolStripItem[] {
            this._uiLanguageMenu,
            this._helpMenu});
			this._L10NSharpExtender.SetLocalizableToolTip(this._toolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._toolStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._toolStrip, LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._toolStrip, "WorkspaceView._toolStrip");
			this._toolStrip.Location = new Point(30, 0);
			this._toolStrip.Name = "_toolStrip";
			this._toolStrip.RightToLeft = RightToLeft.No;
			this._toolStrip.Size = new Size(59, 66);
			this._toolStrip.TabIndex = 28;
			this._toolStrip.Text = "_toolStrip";
			// 
			// _uiLanguageMenu
			// 
			this._uiLanguageMenu.DisplayStyle = ToolStripItemDisplayStyle.Text;
			this._uiLanguageMenu.ForeColor = Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._uiLanguageMenu.Image = Resources.multilingualSettings;
			this._uiLanguageMenu.ImageTransparentColor = Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._uiLanguageMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uiLanguageMenu, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._uiLanguageMenu, LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._uiLanguageMenu, ".toolStripDropDownButton1");
			this._uiLanguageMenu.Name = "_uiLanguageMenu";
			this._uiLanguageMenu.Size = new Size(56, 19);
			this._uiLanguageMenu.Text = "English";
			// 
			// _helpMenu
			// 
			this._helpMenu.DropDownItems.AddRange(new ToolStripItem[] {
            this._documentationMenuItem,
            this._trainingVideosMenuItem,
            this._keyBloomConceptsMenuItem,
            this.buildingReaderTemplatesMenuItem,
            this.usingReaderTemplatesMenuItem,
            this.toolStripSeparator1,
            this._askAQuestionMenuItem,
            this._requestAFeatureMenuItem,
            this._reportAProblemMenuItem,
            this._showLogMenuItem,
            this._divider1,
            this._releaseNotesMenuItem,
            this._checkForNewVersionMenuItem,
            this._registrationMenuItem,
            this._divider2,
            this._webSiteMenuItem,
            this._aboutBloomMenuItem});
			this._helpMenu.ForeColor = Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._helpMenu.Image = Resources.help16x16Darker;
			this._helpMenu.ImageScaling = ToolStripItemImageScaling.None;
			this._helpMenu.ImageTransparentColor = Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._helpMenu, "Get Help");
			this._L10NSharpExtender.SetLocalizationComment(this._helpMenu, null);
			this._L10NSharpExtender.SetLocalizingId(this._helpMenu, "HelpMenu.Help Menu");
			this._helpMenu.Name = "_helpMenu";
			this._helpMenu.Size = new Size(56, 20);
			this._helpMenu.TextImageRelation = TextImageRelation.ImageAboveText;
			// 
			// _documentationMenuItem
			// 
			this._documentationMenuItem.Image = Resources.help24x24;
			this._L10NSharpExtender.SetLocalizableToolTip(this._documentationMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._documentationMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._documentationMenuItem, "HelpMenu.DocumentationMenuItem");
			this._documentationMenuItem.Name = "_documentationMenuItem";
			this._documentationMenuItem.Size = new Size(213, 22);
			this._documentationMenuItem.Text = "Documentation";
			this._documentationMenuItem.Click += new EventHandler(this.toolStripMenuItem3_Click);
			// 
			// _trainingVideosMenuItem
			// 
			this._trainingVideosMenuItem.Image = Resources.videos;
			this._L10NSharpExtender.SetLocalizableToolTip(this._trainingVideosMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._trainingVideosMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._trainingVideosMenuItem, "HelpMenu.trainingVideos");
			this._trainingVideosMenuItem.Name = "_trainingVideosMenuItem";
			this._trainingVideosMenuItem.Size = new Size(213, 22);
			this._trainingVideosMenuItem.Text = "Training Videos";
			this._trainingVideosMenuItem.Click += new EventHandler(this._trainingVideosMenuItem_Click);
			// 
			// _keyBloomConceptsMenuItem
			// 
			this._keyBloomConceptsMenuItem.Image = Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this._keyBloomConceptsMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._keyBloomConceptsMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._keyBloomConceptsMenuItem, LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._keyBloomConceptsMenuItem, "HelpMenu.KeyBloomConceptsToolStripMenuItem");
			this._keyBloomConceptsMenuItem.Name = "_keyBloomConceptsMenuItem";
			this._keyBloomConceptsMenuItem.Size = new Size(213, 22);
			this._keyBloomConceptsMenuItem.Text = "Key Bloom Concepts";
			this._keyBloomConceptsMenuItem.Click += new EventHandler(this.keyBloomConceptsMenuItem_Click);
			// 
			// buildingReaderTemplatesMenuItem
			// 
			this.buildingReaderTemplatesMenuItem.Image = Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this.buildingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.buildingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.buildingReaderTemplatesMenuItem, LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this.buildingReaderTemplatesMenuItem, "HelpMenu.BuildingReaderTemplatesMenuItem");
			this.buildingReaderTemplatesMenuItem.Name = "buildingReaderTemplatesMenuItem";
			this.buildingReaderTemplatesMenuItem.Size = new Size(213, 22);
			this.buildingReaderTemplatesMenuItem.Text = "Building Reader Templates";
			this.buildingReaderTemplatesMenuItem.Click += new EventHandler(this.buildingReaderTemplatesMenuItem_Click);
			// 
			// usingReaderTemplatesMenuItem
			// 
			this.usingReaderTemplatesMenuItem.Image = Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this.usingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.usingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.usingReaderTemplatesMenuItem, LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this.usingReaderTemplatesMenuItem, "HelpMenu.UsingReaderTemplatesMenuItem");
			this.usingReaderTemplatesMenuItem.Name = "usingReaderTemplatesMenuItem";
			this.usingReaderTemplatesMenuItem.Size = new Size(213, 22);
			this.usingReaderTemplatesMenuItem.Text = "Using Reader Templates ";
			this.usingReaderTemplatesMenuItem.Click += new EventHandler(this.usingReaderTemplatesMenuItem_Click);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			this.toolStripSeparator1.Size = new Size(210, 6);
			// 
			// _askAQuestionMenuItem
			// 
			this._askAQuestionMenuItem.Image = Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._askAQuestionMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._askAQuestionMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._askAQuestionMenuItem, "HelpMenu.AskAQuestionMenuItem");
			this._askAQuestionMenuItem.Name = "_askAQuestionMenuItem";
			this._askAQuestionMenuItem.Size = new Size(213, 22);
			this._askAQuestionMenuItem.Text = "Ask a Question";
			this._askAQuestionMenuItem.Click += new EventHandler(this._askAQuestionMenuItem_Click);
			// 
			// _requestAFeatureMenuItem
			// 
			this._requestAFeatureMenuItem.Image = Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._requestAFeatureMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._requestAFeatureMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._requestAFeatureMenuItem, "HelpMenu.MakeASuggestionMenuItem");
			this._requestAFeatureMenuItem.Name = "_requestAFeatureMenuItem";
			this._requestAFeatureMenuItem.Size = new Size(213, 22);
			this._requestAFeatureMenuItem.Text = "Request a Feature";
			this._requestAFeatureMenuItem.Click += new EventHandler(this._requestAFeatureMenuItem_Click);
			// 
			// _reportAProblemMenuItem
			// 
			this._reportAProblemMenuItem.Image = Resources.sad16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this._reportAProblemMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reportAProblemMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._reportAProblemMenuItem, "HelpMenu.ReportAProblemToolStripMenuItem");
			this._reportAProblemMenuItem.Name = "_reportAProblemMenuItem";
			this._reportAProblemMenuItem.Size = new Size(213, 22);
			this._reportAProblemMenuItem.Text = "Report a Problem...";
			this._reportAProblemMenuItem.Click += new EventHandler(this._reportAProblemMenuItem_Click);
			// 
			// _showLogMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._showLogMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showLogMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._showLogMenuItem, LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._showLogMenuItem, "HelpMenu.ShowEventLogMenuItem");
			this._showLogMenuItem.Name = "_showLogMenuItem";
			this._showLogMenuItem.Size = new Size(213, 22);
			this._showLogMenuItem.Text = "Show Event Log";
			this._showLogMenuItem.Click += new EventHandler(this._showLogMenuItem_Click);
			// 
			// _divider1
			// 
			this._divider1.Name = "_divider1";
			this._divider1.Size = new Size(210, 6);
			// 
			// _releaseNotesMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._releaseNotesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._releaseNotesMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._releaseNotesMenuItem, "HelpMenu.ReleaseNotesMenuItem");
			this._releaseNotesMenuItem.Name = "_releaseNotesMenuItem";
			this._releaseNotesMenuItem.Size = new Size(213, 22);
			this._releaseNotesMenuItem.Text = "Release Notes...";
			this._releaseNotesMenuItem.Click += new EventHandler(this._releaseNotesMenuItem_Click);
			// 
			// _checkForNewVersionMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._checkForNewVersionMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._checkForNewVersionMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._checkForNewVersionMenuItem, "HelpMenu.CheckForNewVersionMenuItem");
			this._checkForNewVersionMenuItem.Name = "_checkForNewVersionMenuItem";
			this._checkForNewVersionMenuItem.Size = new Size(213, 22);
			this._checkForNewVersionMenuItem.Text = "Check For New Version";
			this._checkForNewVersionMenuItem.Click += new EventHandler(this._checkForNewVersionMenuItem_Click);
			// 
			// _registrationMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._registrationMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._registrationMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._registrationMenuItem, "HelpMenu.RegistrationMenuItem");
			this._registrationMenuItem.Name = "_registrationMenuItem";
			this._registrationMenuItem.Size = new Size(213, 22);
			this._registrationMenuItem.Text = "Registration...";
			this._registrationMenuItem.Click += new EventHandler(this.OnRegistrationMenuItem_Click);
			// 
			// _divider2
			// 
			this._divider2.Name = "_divider2";
			this._divider2.Size = new Size(210, 6);
			// 
			// _webSiteMenuItem
			// 
			this._webSiteMenuItem.Image = Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._webSiteMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._webSiteMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._webSiteMenuItem, "HelpMenu.WebSiteMenuItem");
			this._webSiteMenuItem.Name = "_webSiteMenuItem";
			this._webSiteMenuItem.Size = new Size(213, 22);
			this._webSiteMenuItem.Text = "BloomLibrary.org";
			this._webSiteMenuItem.Click += new EventHandler(this._webSiteMenuItem_Click);
			// 
			// _aboutBloomMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._aboutBloomMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._aboutBloomMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._aboutBloomMenuItem, "HelpMenu.CreditsMenuItem");
			this._aboutBloomMenuItem.Name = "_aboutBloomMenuItem";
			this._aboutBloomMenuItem.Size = new Size(213, 22);
			this._aboutBloomMenuItem.Text = "About Bloom...";
			this._aboutBloomMenuItem.Click += new EventHandler(this.OnAboutBoxClick);
			// 
			// _panelHoldingToolStrip
			// 
			this._panelHoldingToolStrip.Anchor = ((AnchorStyles)((AnchorStyles.Top | AnchorStyles.Right)));
			this._panelHoldingToolStrip.BackColor = Color.Transparent;
			this._panelHoldingToolStrip.Controls.Add(this._toolStrip);
			this._L10NSharpExtender.SetLocalizableToolTip(this._panelHoldingToolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._panelHoldingToolStrip, null);
	        this._L10NSharpExtender.SetLocalizationPriority(this._panelHoldingToolStrip, LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._panelHoldingToolStrip, "HelpMenu.WorkspaceView._panelHoldingToolStrip");
			this._panelHoldingToolStrip.Location = new Point(1006, 3);
			this._panelHoldingToolStrip.Name = "_panelHoldingToolStrip";
			this._panelHoldingToolStrip.Size = new Size(89, 66);
			this._panelHoldingToolStrip.TabIndex = 29;
			// 
			// _applicationUpdateCheckTimer
			// 
			this._applicationUpdateCheckTimer.Enabled = false;
			this._applicationUpdateCheckTimer.Interval = 60000;
			this._applicationUpdateCheckTimer.Tick += new EventHandler(this._applicationUpdateCheckTimer_Tick);
			// 
			// WorkspaceView
			// 
			this.AutoScaleDimensions = new SizeF(6F, 13F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.Controls.Add(this._panelHoldingToolStrip);
			this.Controls.Add(this._toolSpecificPanel);
			this.Controls.Add(this._containerPanel);
			this.Controls.Add(this._tabStrip);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "WorkspaceView.WorkspaceView");
			this.Name = "WorkspaceView";
			this.Size = new Size(1098, 540);
			this.Load += new EventHandler(this.WorkspaceView_Load);
			this.Resize += new EventHandler(this.WorkspaceView_Resize);
			((ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this._tabStrip.ResumeLayout(false);
			this._tabStrip.PerformLayout();
			this._toolStrip.ResumeLayout(false);
			this._toolStrip.PerformLayout();
			this._panelHoldingToolStrip.ResumeLayout(false);
			this._panelHoldingToolStrip.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		private ToolTip toolTip1;
		private SettingsProtectionHelper _settingsLauncherHelper;
		private Panel _containerPanel;
		private Panel _toolSpecificPanel;
		private TabStripButton _collectionTab;
		private TabStripButton _editTab;
		private TabStripButton _publishTab;
		private TabStrip _tabStrip;
		private L10NSharpExtender _L10NSharpExtender;
		private Timer _applicationUpdateCheckTimer;
		private ToolStrip _toolStrip;
		private ToolStripDropDownButton _uiLanguageMenu;
		private ToolStripDropDownButton _helpMenu;
		private ToolStripMenuItem _documentationMenuItem;
		private ToolStripMenuItem _trainingVideosMenuItem;
		private ToolStripSeparator toolStripSeparator1;
		private ToolStripMenuItem _releaseNotesMenuItem;
		private ToolStripMenuItem _keyBloomConceptsMenuItem;
		private ToolStripMenuItem buildingReaderTemplatesMenuItem;
		private ToolStripMenuItem usingReaderTemplatesMenuItem;
		private ToolStripMenuItem _reportAProblemMenuItem;
		private ToolStripMenuItem _requestAFeatureMenuItem;
		private ToolStripMenuItem _webSiteMenuItem;
		private ToolStripSeparator _divider1;
		private ToolStripMenuItem _showLogMenuItem;
		private ToolStripSeparator _divider2;
		private ToolStripMenuItem _checkForNewVersionMenuItem;
		private ToolStripMenuItem _registrationMenuItem;
		private ToolStripMenuItem _aboutBloomMenuItem;
		private NestedDockedChildPanel _panelHoldingToolStrip;
		private ToolStripMenuItem _askAQuestionMenuItem;

		public static string MustBeAdminMessage => LocalizationManager.GetString("TeamCollection.MustBeAdmin",
			"You must be an administrator to change collection settings");
    }
}
