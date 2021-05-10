namespace Bloom.Workspace
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
			this._settingsLauncherHelper = new SIL.Windows.Forms.SettingProtection.SettingsProtectionHelper(this.components);
			this._containerPanel = new System.Windows.Forms.Panel();
			this._toolSpecificPanel = new System.Windows.Forms.Panel();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._tabStrip = new Messir.Windows.Forms.TabStrip();
			this._collectionTab = new Messir.Windows.Forms.TabStripButton();
			this._editTab = new Messir.Windows.Forms.TabStripButton();
			this._publishTab = new Messir.Windows.Forms.TabStripButton();
			this._toolStrip = new System.Windows.Forms.ToolStrip();
			this._uiLanguageMenu = new System.Windows.Forms.ToolStripDropDownButton();
			this._helpMenu = new System.Windows.Forms.ToolStripDropDownButton();
			this._documentationMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._trainingVideosMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.buildingReaderTemplatesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.usingReaderTemplatesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this._askAQuestionMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._requestAFeatureMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._reportAProblemMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._divider1 = new System.Windows.Forms.ToolStripSeparator();
			this._releaseNotesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._checkForNewVersionMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._registrationMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._divider2 = new System.Windows.Forms.ToolStripSeparator();
			this._webSiteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._aboutBloomMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._panelHoldingToolStrip = new Bloom.Workspace.NestedDockedChildPanel();
			this._applicationUpdateCheckTimer = new System.Windows.Forms.Timer(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this._tabStrip.SuspendLayout();
			this._toolStrip.SuspendLayout();
			this._panelHoldingToolStrip.SuspendLayout();
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
			this._toolSpecificPanel.Location = new System.Drawing.Point(291, 2);
			this._toolSpecificPanel.Name = "_toolSpecificPanel";
			this._toolSpecificPanel.Size = new System.Drawing.Size(718, 66);
			this._toolSpecificPanel.TabIndex = 17;
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
			this._collectionTab.TextChanged += new System.EventHandler(this.HandleTabTextChanged);
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
			this._editTab.TextChanged += new System.EventHandler(this.HandleTabTextChanged);
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
			this._publishTab.TextChanged += new System.EventHandler(this.HandleTabTextChanged);
			// 
			// _toolStrip
			// 
			this._toolStrip.BackColor = System.Drawing.Color.Transparent;
			this._toolStrip.Dock = System.Windows.Forms.DockStyle.Right;
			this._toolStrip.GripMargin = new System.Windows.Forms.Padding(0);
			this._toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._uiLanguageMenu,
            this._helpMenu});
			this._L10NSharpExtender.SetLocalizableToolTip(this._toolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._toolStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._toolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._toolStrip, "WorkspaceView._toolStrip");
			this._toolStrip.Location = new System.Drawing.Point(30, 0);
			this._toolStrip.Name = "_toolStrip";
			this._toolStrip.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this._toolStrip.Size = new System.Drawing.Size(59, 66);
			this._toolStrip.TabIndex = 28;
			this._toolStrip.Text = "_toolStrip";
			// 
			// _uiLanguageMenu
			// 
			this._uiLanguageMenu.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._uiLanguageMenu.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._uiLanguageMenu.Image = global::Bloom.Properties.Resources.multilingualSettings;
			this._uiLanguageMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._uiLanguageMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uiLanguageMenu, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._uiLanguageMenu, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._uiLanguageMenu, ".toolStripDropDownButton1");
			this._uiLanguageMenu.Name = "_uiLanguageMenu";
			this._uiLanguageMenu.Size = new System.Drawing.Size(56, 19);
			this._uiLanguageMenu.Text = "English";
			// 
			// _helpMenu
			// 
			this._helpMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._documentationMenuItem,
            this._trainingVideosMenuItem,
            this.buildingReaderTemplatesMenuItem,
            this.usingReaderTemplatesMenuItem,
            this.toolStripSeparator1,
            this._askAQuestionMenuItem,
            this._requestAFeatureMenuItem,
            this._reportAProblemMenuItem,
            this._divider1,
            this._releaseNotesMenuItem,
            this._checkForNewVersionMenuItem,
            this._registrationMenuItem,
            this._divider2,
            this._webSiteMenuItem,
            this._aboutBloomMenuItem});
			this._helpMenu.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(49)))), ((int)(((byte)(32)))), ((int)(((byte)(46)))));
			this._helpMenu.Image = global::Bloom.Properties.Resources.help16x16Darker;
			this._helpMenu.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._helpMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._helpMenu, "Get Help");
			this._L10NSharpExtender.SetLocalizationComment(this._helpMenu, null);
			this._L10NSharpExtender.SetLocalizingId(this._helpMenu, "HelpMenu.Help Menu");
			this._helpMenu.Name = "_helpMenu";
			this._helpMenu.Size = new System.Drawing.Size(56, 20);
			this._helpMenu.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			// 
			// _documentationMenuItem
			// 
			this._documentationMenuItem.Image = global::Bloom.Properties.Resources.help24x24;
			this._L10NSharpExtender.SetLocalizableToolTip(this._documentationMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._documentationMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._documentationMenuItem, "HelpMenu.DocumentationMenuItem");
			this._documentationMenuItem.Name = "_documentationMenuItem";
			this._documentationMenuItem.Size = new System.Drawing.Size(213, 22);
			this._documentationMenuItem.Text = "Browse Help...";
			this._documentationMenuItem.Click += new System.EventHandler(this.toolStripMenuItem3_Click);
			// 
			// _trainingVideosMenuItem
			// 
			this._trainingVideosMenuItem.Image = global::Bloom.Properties.Resources.videos;
			this._L10NSharpExtender.SetLocalizableToolTip(this._trainingVideosMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._trainingVideosMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._trainingVideosMenuItem, "HelpMenu.trainingVideos");
			this._trainingVideosMenuItem.Name = "_trainingVideosMenuItem";
			this._trainingVideosMenuItem.Size = new System.Drawing.Size(213, 22);
			this._trainingVideosMenuItem.Text = "Training Videos";
			this._trainingVideosMenuItem.Click += new System.EventHandler(this._trainingVideosMenuItem_Click);
			// 
			// buildingReaderTemplatesMenuItem
			// 
			this.buildingReaderTemplatesMenuItem.Image = global::Bloom.Properties.Resources.pdf16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this.buildingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.buildingReaderTemplatesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.buildingReaderTemplatesMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this.buildingReaderTemplatesMenuItem, "HelpMenu.BuildingReaderTemplatesMenuItem");
			this.buildingReaderTemplatesMenuItem.Name = "buildingReaderTemplatesMenuItem";
			this.buildingReaderTemplatesMenuItem.Size = new System.Drawing.Size(213, 22);
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
			this.usingReaderTemplatesMenuItem.Size = new System.Drawing.Size(213, 22);
			this.usingReaderTemplatesMenuItem.Text = "Using Reader Templates ";
			this.usingReaderTemplatesMenuItem.Click += new System.EventHandler(this.usingReaderTemplatesMenuItem_Click);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			this.toolStripSeparator1.Size = new System.Drawing.Size(210, 6);
			// 
			// _askAQuestionMenuItem
			// 
			this._askAQuestionMenuItem.Image = global::Bloom.Properties.Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._askAQuestionMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._askAQuestionMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._askAQuestionMenuItem, "HelpMenu.AskAQuestionMenuItem");
			this._askAQuestionMenuItem.Name = "_askAQuestionMenuItem";
			this._askAQuestionMenuItem.Size = new System.Drawing.Size(213, 22);
			this._askAQuestionMenuItem.Text = "Ask a Question";
			this._askAQuestionMenuItem.Click += new System.EventHandler(this._askAQuestionMenuItem_Click);
			// 
			// _requestAFeatureMenuItem
			// 
			this._requestAFeatureMenuItem.Image = global::Bloom.Properties.Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._requestAFeatureMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._requestAFeatureMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._requestAFeatureMenuItem, "HelpMenu.MakeASuggestionMenuItem");
			this._requestAFeatureMenuItem.Name = "_requestAFeatureMenuItem";
			this._requestAFeatureMenuItem.Size = new System.Drawing.Size(213, 22);
			this._requestAFeatureMenuItem.Text = "Request a Feature";
			this._requestAFeatureMenuItem.Click += new System.EventHandler(this._requestAFeatureMenuItem_Click);
			// 
			// _reportAProblemMenuItem
			// 
			this._reportAProblemMenuItem.Image = global::Bloom.Properties.Resources.sad16x16;
			this._L10NSharpExtender.SetLocalizableToolTip(this._reportAProblemMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reportAProblemMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._reportAProblemMenuItem, "HelpMenu.ReportAProblemToolStripMenuItem");
			this._reportAProblemMenuItem.Name = "_reportAProblemMenuItem";
			this._reportAProblemMenuItem.Size = new System.Drawing.Size(213, 22);
			this._reportAProblemMenuItem.Text = "Report a Problem...";
			this._reportAProblemMenuItem.Click += new System.EventHandler(this._reportAProblemMenuItem_Click);
			// 
			// _divider1
			// 
			this._divider1.Name = "_divider1";
			this._divider1.Size = new System.Drawing.Size(210, 6);
			// 
			// _releaseNotesMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._releaseNotesMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._releaseNotesMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._releaseNotesMenuItem, "HelpMenu.ReleaseNotesMenuItem");
			this._releaseNotesMenuItem.Name = "_releaseNotesMenuItem";
			this._releaseNotesMenuItem.Size = new System.Drawing.Size(213, 22);
			this._releaseNotesMenuItem.Text = "Release Notes...";
			this._releaseNotesMenuItem.Click += new System.EventHandler(this._releaseNotesMenuItem_Click);
			// 
			// _checkForNewVersionMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._checkForNewVersionMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._checkForNewVersionMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._checkForNewVersionMenuItem, "HelpMenu.CheckForNewVersionMenuItem");
			this._checkForNewVersionMenuItem.Name = "_checkForNewVersionMenuItem";
			this._checkForNewVersionMenuItem.Size = new System.Drawing.Size(213, 22);
			this._checkForNewVersionMenuItem.Text = "Check For New Version";
			this._checkForNewVersionMenuItem.Click += new System.EventHandler(this._checkForNewVersionMenuItem_Click);
			// 
			// _registrationMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._registrationMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._registrationMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._registrationMenuItem, "HelpMenu.RegistrationMenuItem");
			this._registrationMenuItem.Name = "_registrationMenuItem";
			this._registrationMenuItem.Size = new System.Drawing.Size(213, 22);
			this._registrationMenuItem.Text = "Registration...";
			this._registrationMenuItem.Click += new System.EventHandler(this.OnRegistrationMenuItem_Click);
			// 
			// _divider2
			// 
			this._divider2.Name = "_divider2";
			this._divider2.Size = new System.Drawing.Size(210, 6);
			// 
			// _webSiteMenuItem
			// 
			this._webSiteMenuItem.Image = global::Bloom.Properties.Resources.weblink;
			this._L10NSharpExtender.SetLocalizableToolTip(this._webSiteMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._webSiteMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._webSiteMenuItem, "HelpMenu.WebSiteMenuItem");
			this._webSiteMenuItem.Name = "_webSiteMenuItem";
			this._webSiteMenuItem.Size = new System.Drawing.Size(213, 22);
			this._webSiteMenuItem.Text = "BloomLibrary.org";
			this._webSiteMenuItem.Click += new System.EventHandler(this._webSiteMenuItem_Click);
			// 
			// _aboutBloomMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._aboutBloomMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._aboutBloomMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._aboutBloomMenuItem, "HelpMenu.CreditsMenuItem");
			this._aboutBloomMenuItem.Name = "_aboutBloomMenuItem";
			this._aboutBloomMenuItem.Size = new System.Drawing.Size(213, 22);
			this._aboutBloomMenuItem.Text = "About Bloom...";
			this._aboutBloomMenuItem.Click += new System.EventHandler(this.OnAboutBoxClick);
			// 
			// _panelHoldingToolStrip
			// 
			this._panelHoldingToolStrip.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._panelHoldingToolStrip.BackColor = System.Drawing.Color.Transparent;
			this._panelHoldingToolStrip.Controls.Add(this._toolStrip);
			this._L10NSharpExtender.SetLocalizableToolTip(this._panelHoldingToolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._panelHoldingToolStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._panelHoldingToolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._panelHoldingToolStrip, "HelpMenu.WorkspaceView._panelHoldingToolStrip");
			this._panelHoldingToolStrip.Location = new System.Drawing.Point(1006, 3);
			this._panelHoldingToolStrip.Name = "_panelHoldingToolStrip";
			this._panelHoldingToolStrip.Size = new System.Drawing.Size(89, 66);
			this._panelHoldingToolStrip.TabIndex = 29;
			// 
			// _applicationUpdateCheckTimer
			// 
			this._applicationUpdateCheckTimer.Interval = 60000;
			this._applicationUpdateCheckTimer.Tick += new System.EventHandler(this._applicationUpdateCheckTimer_Tick);
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
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
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

		private System.Windows.Forms.ToolTip toolTip1;
		private SIL.Windows.Forms.SettingProtection.SettingsProtectionHelper _settingsLauncherHelper;
		private System.Windows.Forms.Panel _containerPanel;
		private System.Windows.Forms.Panel _toolSpecificPanel;
		private Messir.Windows.Forms.TabStripButton _collectionTab;
		private Messir.Windows.Forms.TabStripButton _editTab;
		private Messir.Windows.Forms.TabStripButton _publishTab;
		private Messir.Windows.Forms.TabStrip _tabStrip;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private System.Windows.Forms.Timer _applicationUpdateCheckTimer;
		private System.Windows.Forms.ToolStrip _toolStrip;
		private System.Windows.Forms.ToolStripDropDownButton _uiLanguageMenu;
		private System.Windows.Forms.ToolStripDropDownButton _helpMenu;
		private System.Windows.Forms.ToolStripMenuItem _documentationMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _trainingVideosMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
		private System.Windows.Forms.ToolStripMenuItem _releaseNotesMenuItem;
		private System.Windows.Forms.ToolStripMenuItem buildingReaderTemplatesMenuItem;
		private System.Windows.Forms.ToolStripMenuItem usingReaderTemplatesMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _reportAProblemMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _requestAFeatureMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _webSiteMenuItem;
		private System.Windows.Forms.ToolStripSeparator _divider1;
		private System.Windows.Forms.ToolStripSeparator _divider2;
		private System.Windows.Forms.ToolStripMenuItem _checkForNewVersionMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _registrationMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _aboutBloomMenuItem;
		private NestedDockedChildPanel _panelHoldingToolStrip;
		private System.Windows.Forms.ToolStripMenuItem _askAQuestionMenuItem;
	}
}
