﻿namespace Bloom.Workspace
{
    partial class WorkspaceView
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
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this._settingsLauncherHelper = new Palaso.UI.WindowsForms.SettingProtection.SettingsProtectionHelper(this.components);
			this._containerPanel = new System.Windows.Forms.Panel();
			this._toolSpecificPanel = new System.Windows.Forms.Panel();
			this._panelHoldingToolStrip = new System.Windows.Forms.Panel();
			this._toolStrip = new System.Windows.Forms.ToolStrip();
			this._settingsButton = new System.Windows.Forms.ToolStripButton();
			this._openCreateCollectionButton = new System.Windows.Forms.ToolStripButton();
			this._helpMenu = new System.Windows.Forms.ToolStripDropDownButton();
			this._documentationMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._trainingVideosMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this._releaseNotesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._keyBloomConceptsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._divider1 = new System.Windows.Forms.ToolStripSeparator();
			this.buildingReaderTemplatesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.usingReaderTemplatesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._divider2 = new System.Windows.Forms.ToolStripSeparator();
			this._reportAProblemMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._makeASuggestionMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._webSiteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._divider3 = new System.Windows.Forms.ToolStripSeparator();
			this._showLogMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._divider4 = new System.Windows.Forms.ToolStripSeparator();
			this._checkForNewVersionMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._registrationMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._aboutBloomMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._uiLanguageMenu = new System.Windows.Forms.ToolStripDropDownButton();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._tabStrip = new Messir.Windows.Forms.TabStrip();
			this._collectionTab = new Messir.Windows.Forms.TabStripButton();
			this._editTab = new Messir.Windows.Forms.TabStripButton();
			this._publishTab = new Messir.Windows.Forms.TabStripButton();
			this._panelHoldingToolStrip.SuspendLayout();
			this._toolStrip.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this._tabStrip.SuspendLayout();
			this.SuspendLayout();
			// 
			// _containerPanel
			// 
			this._containerPanel.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._containerPanel.Location = new System.Drawing.Point(0, 74);
			this._containerPanel.Name = "_containerPanel";
			this._containerPanel.Size = new System.Drawing.Size(1098, 463);
			this._containerPanel.TabIndex = 16;
			// 
			// _toolSpecificPanel
			// 
			this._toolSpecificPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._toolSpecificPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._toolSpecificPanel.Location = new System.Drawing.Point(333, 2);
			this._toolSpecificPanel.Name = "_toolSpecificPanel";
			this._toolSpecificPanel.Size = new System.Drawing.Size(700, 66);
			this._toolSpecificPanel.TabIndex = 17;
			// 
			// _panelHoldingToolStrip
			// 
			this._panelHoldingToolStrip.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._panelHoldingToolStrip.BackColor = System.Drawing.Color.Transparent;
			this._panelHoldingToolStrip.Controls.Add(this._toolStrip);
			this._panelHoldingToolStrip.Location = new System.Drawing.Point(823, 3);
			this._panelHoldingToolStrip.Name = "_panelHoldingToolStrip";
			this._panelHoldingToolStrip.Size = new System.Drawing.Size(272, 66);
			this._panelHoldingToolStrip.TabIndex = 29;
			// 
			// _toolStrip
			// 
			this._toolStrip.BackColor = System.Drawing.Color.Transparent;
			this._toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._settingsButton,
            this._openCreateCollectionButton,
            this._helpMenu,
            this._uiLanguageMenu});
			this._toolStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Flow;
			this._L10NSharpExtender.SetLocalizableToolTip(this._toolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._toolStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._toolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._toolStrip, "WorkspaceView._toolStrip");
			this._toolStrip.Location = new System.Drawing.Point(0, 0);
			this._toolStrip.Name = "_toolStrip";
			this._toolStrip.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this._toolStrip.Size = new System.Drawing.Size(272, 46);
			this._toolStrip.TabIndex = 28;
			this._toolStrip.Text = "_toolStrip";
			// 
			// _settingsButton
			// 
			this._settingsButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._settingsButton.Image = global::Bloom.Properties.Resources.settings24x24;
			this._settingsButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._settingsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._settingsButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._settingsButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._settingsButton, "CollectionTab.SettingsButton");
			this._settingsButton.Name = "_settingsButton";
			this._settingsButton.Size = new System.Drawing.Size(53, 43);
			this._settingsButton.Text = "Settings";
			this._settingsButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._settingsButton.Click += new System.EventHandler(this.OnSettingsButton_Click);
			// 
			// _openCreateCollectionButton
			// 
			this._openCreateCollectionButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._openCreateCollectionButton.Image = global::Bloom.Properties.Resources.OpenCreateLibrary24x24;
			this._openCreateCollectionButton.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._openCreateCollectionButton.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._openCreateCollectionButton, "Open/Create/Get Collection");
			this._L10NSharpExtender.SetLocalizationComment(this._openCreateCollectionButton, "This is is the button you use to create a new collection, open a new one, or get " +
        "one from a repository somewhere");
			this._L10NSharpExtender.SetLocalizingId(this._openCreateCollectionButton, "CollectionTab.Open/CreateCollectionButton");
			this._openCreateCollectionButton.Name = "_openCreateCollectionButton";
			this._openCreateCollectionButton.Size = new System.Drawing.Size(98, 43);
			this._openCreateCollectionButton.Text = "Other Collection";
			this._openCreateCollectionButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._openCreateCollectionButton.Click += new System.EventHandler(this.OnOpenCreateLibrary_Click);
			// 
			// _helpMenu
			// 
			this._helpMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._documentationMenuItem,
            this._trainingVideosMenuItem,
            this.toolStripSeparator1,
            this._releaseNotesMenuItem,
            this._keyBloomConceptsMenuItem,
            this._divider1,
            this.buildingReaderTemplatesMenuItem,
            this.usingReaderTemplatesMenuItem,
            this._divider2,
            this._reportAProblemMenuItem,
            this._makeASuggestionMenuItem,
            this._webSiteMenuItem,
            this._divider3,
            this._showLogMenuItem,
            this._divider4,
            this._checkForNewVersionMenuItem,
            this._registrationMenuItem,
            this._aboutBloomMenuItem});
			this._helpMenu.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._helpMenu.Image = global::Bloom.Properties.Resources.help24x24;
			this._helpMenu.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._helpMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._helpMenu, "Get Help");
			this._L10NSharpExtender.SetLocalizationComment(this._helpMenu, null);
			this._L10NSharpExtender.SetLocalizingId(this._helpMenu, "HelpMenu.Help Menu");
			this._helpMenu.Name = "_helpMenu";
			this._helpMenu.Size = new System.Drawing.Size(45, 43);
			this._helpMenu.Text = "Help";
			this._helpMenu.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			// 
			// _documentationMenuItem
			// 
			this._documentationMenuItem.Image = global::Bloom.Properties.Resources.help24x24;
			this._L10NSharpExtender.SetLocalizableToolTip(this._documentationMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._documentationMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._documentationMenuItem, "HelpMenu.documentationMenuItem");
			this._documentationMenuItem.Name = "_documentationMenuItem";
			this._documentationMenuItem.Size = new System.Drawing.Size(215, 22);
			this._documentationMenuItem.Text = "Great Help. No, Really!";
			this._documentationMenuItem.Click += new System.EventHandler(this.toolStripMenuItem3_Click);
			// 
			// _trainingVideosMenuItem
			// 
			this._trainingVideosMenuItem.Image = global::Bloom.Properties.Resources.videos;
			this._L10NSharpExtender.SetLocalizableToolTip(this._trainingVideosMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._trainingVideosMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._trainingVideosMenuItem, "HelpMenu.trainingVideos");
			this._trainingVideosMenuItem.Name = "_trainingVideosMenuItem";
			this._trainingVideosMenuItem.Size = new System.Drawing.Size(215, 22);
			this._trainingVideosMenuItem.Text = "Training Videos";
			this._trainingVideosMenuItem.Click += new System.EventHandler(this._trainingVideosMenuItem_Click);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			this.toolStripSeparator1.Size = new System.Drawing.Size(212, 6);
			// 
			// _releaseNotesMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._releaseNotesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._releaseNotesMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._releaseNotesMenuItem, "HelpMenu.releaseNotesMenuItem");
			this._releaseNotesMenuItem.Name = "_releaseNotesMenuItem";
			this._releaseNotesMenuItem.Size = new System.Drawing.Size(215, 22);
			this._releaseNotesMenuItem.Text = "Release Notes...";
			this._releaseNotesMenuItem.Click += new System.EventHandler(this._releaseNotesMenuItem_Click);
			// 
			// _keyBloomConceptsMenuItem
			// 
			this._keyBloomConceptsMenuItem.Image = global::Bloom.Properties.Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this._keyBloomConceptsMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._keyBloomConceptsMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._keyBloomConceptsMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._keyBloomConceptsMenuItem, "HelpMenu.keyBloomConceptsToolStripMenuItem");
			this._keyBloomConceptsMenuItem.Name = "_keyBloomConceptsMenuItem";
			this._keyBloomConceptsMenuItem.Size = new System.Drawing.Size(215, 22);
			this._keyBloomConceptsMenuItem.Text = "Key Bloom Concepts";
			this._keyBloomConceptsMenuItem.Click += new System.EventHandler(this.keyBloomConceptsMenuItem_Click);
			// 
			// _divider1
			// 
			this._divider1.Name = "_divider1";
			this._divider1.Size = new System.Drawing.Size(212, 6);
			// 
			// buildingReaderTemplatesMenuItem
			// 
			this.buildingReaderTemplatesMenuItem.Image = global::Bloom.Properties.Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this.buildingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.buildingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.buildingReaderTemplatesMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this.buildingReaderTemplatesMenuItem, "HelpMenu.BuildingReaderTemplatesMenuItem");
			this.buildingReaderTemplatesMenuItem.Name = "buildingReaderTemplatesMenuItem";
			this.buildingReaderTemplatesMenuItem.Size = new System.Drawing.Size(215, 22);
			this.buildingReaderTemplatesMenuItem.Text = "Building Reader Templates";
			this.buildingReaderTemplatesMenuItem.Click += new System.EventHandler(this.buildingReaderTemplatesMenuItem_Click);
			// 
			// usingReaderTemplatesMenuItem
			// 
			this.usingReaderTemplatesMenuItem.Image = global::Bloom.Properties.Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this.usingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.usingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.usingReaderTemplatesMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this.usingReaderTemplatesMenuItem, "HelpMenu.UsingReaderTemplatesMenuItem");
			this.usingReaderTemplatesMenuItem.Name = "usingReaderTemplatesMenuItem";
			this.usingReaderTemplatesMenuItem.Size = new System.Drawing.Size(215, 22);
			this.usingReaderTemplatesMenuItem.Text = "Using Reader Templates ";
			this.usingReaderTemplatesMenuItem.Click += new System.EventHandler(this.usingReaderTemplatesMenuItem_Click);
			// 
			// _divider2
			// 
			this._divider2.Name = "_divider2";
			this._divider2.Size = new System.Drawing.Size(212, 6);
			// 
			// _reportAProblemMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._reportAProblemMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reportAProblemMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._reportAProblemMenuItem, "HelpMenu.reportAProblemToolStripMenuItem");
			this._reportAProblemMenuItem.Name = "_reportAProblemMenuItem";
			this._reportAProblemMenuItem.Size = new System.Drawing.Size(215, 22);
			this._reportAProblemMenuItem.Text = "Report a Problem...";
			this._reportAProblemMenuItem.Click += new System.EventHandler(this._reportAProblemMenuItem_Click);
			// 
			// _makeASuggestionMenuItem
			// 
			this._makeASuggestionMenuItem.Image = global::Bloom.Properties.Resources.uservoice16x161;
			this._L10NSharpExtender.SetLocalizableToolTip(this._makeASuggestionMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._makeASuggestionMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._makeASuggestionMenuItem, "HelpMenu._makeASuggestionMenuItem");
			this._makeASuggestionMenuItem.Name = "_makeASuggestionMenuItem";
			this._makeASuggestionMenuItem.Size = new System.Drawing.Size(215, 22);
			this._makeASuggestionMenuItem.Text = "Make a Suggestion";
			this._makeASuggestionMenuItem.Click += new System.EventHandler(this._makeASuggestionMenuItem_Click);
			// 
			// _webSiteMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._webSiteMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._webSiteMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._webSiteMenuItem, "HelpMenu._webSiteMenuItem");
			this._webSiteMenuItem.Name = "_webSiteMenuItem";
			this._webSiteMenuItem.Size = new System.Drawing.Size(215, 22);
			this._webSiteMenuItem.Text = "Web Site";
			this._webSiteMenuItem.Click += new System.EventHandler(this._webSiteMenuItem_Click);
			// 
			// _divider3
			// 
			this._divider3.Name = "_divider3";
			this._divider3.Size = new System.Drawing.Size(212, 6);
			// 
			// _showLogMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._showLogMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showLogMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._showLogMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._showLogMenuItem, "HelpMenu.showEventLogMenuItem");
			this._showLogMenuItem.Name = "_showLogMenuItem";
			this._showLogMenuItem.Size = new System.Drawing.Size(215, 22);
			this._showLogMenuItem.Text = "Show Event Log";
			this._showLogMenuItem.Click += new System.EventHandler(this._showLogMenuItem_Click);
			// 
			// _divider4
			// 
			this._divider4.Name = "_divider4";
			this._divider4.Size = new System.Drawing.Size(212, 6);
			// 
			// _checkForNewVersionMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._checkForNewVersionMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._checkForNewVersionMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._checkForNewVersionMenuItem, "HelpMenu.checkForNewVersionToolStripMenuItem");
			this._checkForNewVersionMenuItem.Name = "_checkForNewVersionMenuItem";
			this._checkForNewVersionMenuItem.Size = new System.Drawing.Size(215, 22);
			this._checkForNewVersionMenuItem.Text = "Check For New Version";
			this._checkForNewVersionMenuItem.Click += new System.EventHandler(this._checkForNewVersionMenuItem_Click);
			// 
			// _registrationMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._registrationMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._registrationMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._registrationMenuItem, "HelpMenu.registrationMenuItem");
			this._registrationMenuItem.Name = "_registrationMenuItem";
			this._registrationMenuItem.Size = new System.Drawing.Size(215, 22);
			this._registrationMenuItem.Text = "Registration";
			this._registrationMenuItem.Click += new System.EventHandler(this.OnRegistrationMenuItem_Click);
			// 
			// _aboutBloomMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._aboutBloomMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._aboutBloomMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._aboutBloomMenuItem, "HelpMenu.creditsMenuItem");
			this._aboutBloomMenuItem.Name = "_aboutBloomMenuItem";
			this._aboutBloomMenuItem.Size = new System.Drawing.Size(215, 22);
			this._aboutBloomMenuItem.Text = "About Bloom";
			this._aboutBloomMenuItem.Click += new System.EventHandler(this.OnAboutBoxClick);
			// 
			// _uiLanguageMenu
			// 
			this._uiLanguageMenu.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._uiLanguageMenu.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._uiLanguageMenu.Image = global::Bloom.Properties.Resources.multilingualSettings;
			this._uiLanguageMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._uiLanguageMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uiLanguageMenu, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._uiLanguageMenu, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._uiLanguageMenu, ".toolStripDropDownButton1");
			this._uiLanguageMenu.Name = "_uiLanguageMenu";
			this._uiLanguageMenu.Size = new System.Drawing.Size(58, 19);
			this._uiLanguageMenu.Text = "English";
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "HelpMenu";
			// 
			// _tabStrip
			// 
			this._tabStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._tabStrip.FlipButtons = false;
			this._tabStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._tabStrip.ImageScalingSize = new System.Drawing.Size(32, 32);
			this._tabStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._collectionTab,
            this._editTab,
            this._publishTab});
			this._L10NSharpExtender.SetLocalizableToolTip(this._tabStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._tabStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._tabStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._tabStrip, "WorkspaceView._tabStrip");
			this._tabStrip.Location = new System.Drawing.Point(0, 0);
			this._tabStrip.Name = "_tabStrip";
			this._tabStrip.RenderStyle = System.Windows.Forms.ToolStripRenderMode.ManagerRenderMode;
			this._tabStrip.SelectedTab = this._publishTab;
			this._tabStrip.Size = new System.Drawing.Size(1098, 71);
			this._tabStrip.TabIndex = 15;
			this._tabStrip.Text = "tabStrip1";
			this._tabStrip.UseVisualStyles = false;
			this._tabStrip.SelectedTabChanged += new System.EventHandler<Messir.Windows.Forms.SelectedTabChangedEventArgs>(this._tabStrip_SelectedTabChanged);
			this._tabStrip.BackColorChanged += new System.EventHandler(this._tabStrip_BackColorChanged);
			// 
			// _collectionTab
			// 
			this._collectionTab.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._collectionTab.BarColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._collectionTab.ForeColor = System.Drawing.Color.Black;
			this._collectionTab.HotTextColor = System.Drawing.Color.Black;
			this._collectionTab.Image = global::Bloom.Properties.Resources.library32x32;
			this._collectionTab.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._collectionTab.IsSelected = false;
			this._L10NSharpExtender.SetLocalizableToolTip(this._collectionTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._collectionTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._collectionTab, "CollectionTab.Collections");
			this._collectionTab.Margin = new System.Windows.Forms.Padding(0);
			this._collectionTab.Name = "_collectionTab";
			this._collectionTab.Padding = new System.Windows.Forms.Padding(0);
			this._collectionTab.SelectedFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._collectionTab.SelectedTextColor = System.Drawing.Color.WhiteSmoke;
			this._collectionTab.Size = new System.Drawing.Size(103, 71);
			this._collectionTab.Text = "Collections";
			this._collectionTab.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			// 
			// _editTab
			// 
			this._editTab.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._editTab.BarColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._editTab.ForeColor = System.Drawing.Color.Black;
			this._editTab.HotTextColor = System.Drawing.Color.Black;
			this._editTab.Image = global::Bloom.Properties.Resources.edit;
			this._editTab.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._editTab.IsSelected = false;
			this._L10NSharpExtender.SetLocalizableToolTip(this._editTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._editTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._editTab, "EditTab.Edit");
			this._editTab.Margin = new System.Windows.Forms.Padding(0);
			this._editTab.Name = "_editTab";
			this._editTab.Padding = new System.Windows.Forms.Padding(0);
			this._editTab.SelectedFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._editTab.SelectedTextColor = System.Drawing.Color.WhiteSmoke;
			this._editTab.Size = new System.Drawing.Size(69, 71);
			this._editTab.Text = "Edit";
			this._editTab.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			// 
			// _publishTab
			// 
			this._publishTab.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._publishTab.BarColor = System.Drawing.Color.FromArgb(((int)(((byte)(214)))), ((int)(((byte)(86)))), ((int)(((byte)(73)))));
			this._publishTab.Checked = true;
			this._publishTab.ForeColor = System.Drawing.Color.Black;
			this._publishTab.HotTextColor = System.Drawing.Color.Black;
			this._publishTab.Image = global::Bloom.Properties.Resources.publish32x32;
			this._publishTab.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._publishTab.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._publishTab.IsSelected = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._publishTab, null);
			this._L10NSharpExtender.SetLocalizationComment(this._publishTab, null);
			this._L10NSharpExtender.SetLocalizingId(this._publishTab, "PublishTab.Publish");
			this._publishTab.Margin = new System.Windows.Forms.Padding(0);
			this._publishTab.Name = "_publishTab";
			this._publishTab.Padding = new System.Windows.Forms.Padding(0);
			this._publishTab.SelectedFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._publishTab.SelectedTextColor = System.Drawing.Color.WhiteSmoke;
			this._publishTab.Size = new System.Drawing.Size(83, 71);
			this._publishTab.Text = "Publish";
			this._publishTab.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			// 
			// WorkspaceView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._panelHoldingToolStrip);
			this.Controls.Add(this._toolSpecificPanel);
			this.Controls.Add(this._containerPanel);
			this.Controls.Add(this._tabStrip);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "WorkspaceView.WorkspaceView");
			this.Name = "WorkspaceView";
			this.Size = new System.Drawing.Size(1098, 540);
			this.Load += new System.EventHandler(this.WorkspaceView_Load);
			this.Resize += new System.EventHandler(this.WorkspaceView_Resize);
			this._panelHoldingToolStrip.ResumeLayout(false);
			this._panelHoldingToolStrip.PerformLayout();
			this._toolStrip.ResumeLayout(false);
			this._toolStrip.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this._tabStrip.ResumeLayout(false);
			this._tabStrip.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		private System.Windows.Forms.ToolTip toolTip1;
		private Palaso.UI.WindowsForms.SettingProtection.SettingsProtectionHelper _settingsLauncherHelper;
		private System.Windows.Forms.Panel _containerPanel;
		private System.Windows.Forms.Panel _toolSpecificPanel;
		private System.Windows.Forms.Panel _panelHoldingToolStrip;
		private System.Windows.Forms.ToolStrip _toolStrip;
		private System.Windows.Forms.ToolStripDropDownButton _helpMenu;
		private System.Windows.Forms.ToolStripMenuItem _aboutBloomMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _webSiteMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _documentationMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _makeASuggestionMenuItem;
		private System.Windows.Forms.ToolStripButton _settingsButton;
		private System.Windows.Forms.ToolStripButton _openCreateCollectionButton;
		private Messir.Windows.Forms.TabStripButton _collectionTab;
		private Messir.Windows.Forms.TabStripButton _editTab;
		private Messir.Windows.Forms.TabStripButton _publishTab;
		private Messir.Windows.Forms.TabStrip _tabStrip;
		private System.Windows.Forms.ToolStripMenuItem _releaseNotesMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
		private System.Windows.Forms.ToolStripSeparator _divider2;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.ToolStripDropDownButton _uiLanguageMenu;
		private System.Windows.Forms.ToolStripSeparator _divider3;
		private System.Windows.Forms.ToolStripMenuItem _showLogMenuItem;
        private System.Windows.Forms.ToolStripSeparator _divider4;
        private System.Windows.Forms.ToolStripMenuItem _checkForNewVersionMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _registrationMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _keyBloomConceptsMenuItem;
		private System.Windows.Forms.ToolStripMenuItem buildingReaderTemplatesMenuItem;
		private System.Windows.Forms.ToolStripMenuItem usingReaderTemplatesMenuItem;
		private System.Windows.Forms.ToolStripSeparator _divider1;
		private System.Windows.Forms.ToolStripMenuItem _reportAProblemMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _trainingVideosMenuItem;


    }
}