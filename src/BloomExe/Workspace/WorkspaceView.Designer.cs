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
			this._settingsLauncherHelper = new Palaso.UI.WindowsForms.SettingProtection.SettingsLauncherHelper(this.components);
			this._containerPanel = new System.Windows.Forms.Panel();
			this._toolSpecificPanel = new System.Windows.Forms.Panel();
			this._panelHoldingToolStrip = new System.Windows.Forms.Panel();
			this._toolStrip = new System.Windows.Forms.ToolStrip();
			this._tabStrip = new Messir.Windows.Forms.TabStrip();
			this.toolStripButton1 = new System.Windows.Forms.ToolStripButton();
			this.toolStripButton2 = new System.Windows.Forms.ToolStripButton();
			this._helpMenu = new System.Windows.Forms.ToolStripDropDownButton();
			this._documentationMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this._creditsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._releaseNotesMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.deepBloomPaperToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
			this._makeASuggestionMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._webSiteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._collectionTab = new Messir.Windows.Forms.TabStripButton();
			this._editTab = new Messir.Windows.Forms.TabStripButton();
			this._publishTab = new Messir.Windows.Forms.TabStripButton();
			this._panelHoldingToolStrip.SuspendLayout();
			this._toolStrip.SuspendLayout();
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
			this._toolSpecificPanel.Size = new System.Drawing.Size(517, 66);
			this._toolSpecificPanel.TabIndex = 17;
			// 
			// _panelHoldingToolStrip
			// 
			this._panelHoldingToolStrip.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._panelHoldingToolStrip.BackColor = System.Drawing.Color.Transparent;
			this._panelHoldingToolStrip.Controls.Add(this._toolStrip);
			this._panelHoldingToolStrip.Location = new System.Drawing.Point(856, 3);
			this._panelHoldingToolStrip.Name = "_panelHoldingToolStrip";
			this._panelHoldingToolStrip.Size = new System.Drawing.Size(239, 66);
			this._panelHoldingToolStrip.TabIndex = 29;
			// 
			// _toolStrip
			// 
			this._toolStrip.BackColor = System.Drawing.Color.Transparent;
			this._toolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._toolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripButton1,
            this.toolStripButton2,
            this._helpMenu});
			this._toolStrip.Location = new System.Drawing.Point(0, 0);
			this._toolStrip.Name = "_toolStrip";
			this._toolStrip.RightToLeft = System.Windows.Forms.RightToLeft.No;
			this._toolStrip.Size = new System.Drawing.Size(239, 46);
			this._toolStrip.TabIndex = 28;
			this._toolStrip.Text = "_toolStrip";
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
			// toolStripButton1
			// 
			this.toolStripButton1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.toolStripButton1.Image = global::Bloom.Properties.Resources.settings24x24;
			this.toolStripButton1.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this.toolStripButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.toolStripButton1.Name = "toolStripButton1";
			this.toolStripButton1.Size = new System.Drawing.Size(53, 43);
			this.toolStripButton1.Text = "Settings";
			this.toolStripButton1.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.toolStripButton1.Click += new System.EventHandler(this.OnSettingsButton_Click);
			// 
			// toolStripButton2
			// 
			this.toolStripButton2.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.toolStripButton2.Image = global::Bloom.Properties.Resources.OpenCreateLibrary24x24;
			this.toolStripButton2.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this.toolStripButton2.ImageTransparentColor = System.Drawing.Color.Magenta;
			this.toolStripButton2.Name = "toolStripButton2";
			this.toolStripButton2.Size = new System.Drawing.Size(136, 43);
			this.toolStripButton2.Text = "Open/Create Collection";
			this.toolStripButton2.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.toolStripButton2.Click += new System.EventHandler(this.OnOpenCreateLibrary_Click);
			// 
			// _helpMenu
			// 
			this._helpMenu.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._documentationMenuItem,
            this.deepBloomPaperToolStripMenuItem,
            this.toolStripSeparator1,
            this._creditsMenuItem,
            this._releaseNotesMenuItem,
            this.toolStripSeparator2,
            this._makeASuggestionMenuItem,
            this._webSiteMenuItem});
			this._helpMenu.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._helpMenu.Image = global::Bloom.Properties.Resources.help24x24;
			this._helpMenu.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._helpMenu.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._helpMenu.Name = "_helpMenu";
			this._helpMenu.Size = new System.Drawing.Size(45, 43);
			this._helpMenu.Text = "Help";
			this._helpMenu.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._helpMenu.ToolTipText = "Get Help";
			// 
			// _documentationMenuItem
			// 
			this._documentationMenuItem.Image = global::Bloom.Properties.Resources.help24x24;
			this._documentationMenuItem.Name = "_documentationMenuItem";
			this._documentationMenuItem.Size = new System.Drawing.Size(174, 22);
			this._documentationMenuItem.Text = "Documentation";
			this._documentationMenuItem.Click += new System.EventHandler(this.toolStripMenuItem3_Click);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			this.toolStripSeparator1.Size = new System.Drawing.Size(171, 6);
			// 
			// _creditsMenuItem
			// 
			this._creditsMenuItem.Name = "_creditsMenuItem";
			this._creditsMenuItem.Size = new System.Drawing.Size(174, 22);
			this._creditsMenuItem.Text = "Credits";
			this._creditsMenuItem.Click += new System.EventHandler(this.toolStripMenuItem1_Click);
			// 
			// _releaseNotesMenuItem
			// 
			this._releaseNotesMenuItem.Name = "_releaseNotesMenuItem";
			this._releaseNotesMenuItem.Size = new System.Drawing.Size(174, 22);
			this._releaseNotesMenuItem.Text = "Release Notes";
			this._releaseNotesMenuItem.Click += new System.EventHandler(this._releaseNotesMenuItem_Click);
			// 
			// deepBloomPaperToolStripMenuItem
			// 
			this.deepBloomPaperToolStripMenuItem.Image = global::Bloom.Properties.Resources.pdf16x16;
			this.deepBloomPaperToolStripMenuItem.Name = "deepBloomPaperToolStripMenuItem";
			this.deepBloomPaperToolStripMenuItem.Size = new System.Drawing.Size(174, 22);
			this.deepBloomPaperToolStripMenuItem.Text = "Deep Bloom Paper";
			this.deepBloomPaperToolStripMenuItem.Click += new System.EventHandler(this.deepBloomPaperToolStripMenuItem_Click);
			// 
			// toolStripSeparator2
			// 
			this.toolStripSeparator2.Name = "toolStripSeparator2";
			this.toolStripSeparator2.Size = new System.Drawing.Size(171, 6);
			// 
			// _makeASuggestionMenuItem
			// 
			this._makeASuggestionMenuItem.Image = global::Bloom.Properties.Resources.uservoice16x161;
			this._makeASuggestionMenuItem.Name = "_makeASuggestionMenuItem";
			this._makeASuggestionMenuItem.Size = new System.Drawing.Size(174, 22);
			this._makeASuggestionMenuItem.Text = "Make a Suggestion";
			this._makeASuggestionMenuItem.Click += new System.EventHandler(this._makeASuggestionMenuItem_Click);
			// 
			// _webSiteMenuItem
			// 
			this._webSiteMenuItem.Name = "_webSiteMenuItem";
			this._webSiteMenuItem.Size = new System.Drawing.Size(174, 22);
			this._webSiteMenuItem.Text = "Web Site";
			this._webSiteMenuItem.Click += new System.EventHandler(this._webSiteMenuItem_Click);
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
			this._collectionTab.Margin = new System.Windows.Forms.Padding(0);
			this._collectionTab.Name = "_collectionTab";
			this._collectionTab.Padding = new System.Windows.Forms.Padding(0);
			this._collectionTab.SelectedFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._collectionTab.SelectedTextColor = System.Drawing.Color.WhiteSmoke;
			this._collectionTab.Size = new System.Drawing.Size(98, 71);
			this._collectionTab.Text = "Collection";
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
			this.Name = "WorkspaceView";
			this.Size = new System.Drawing.Size(1098, 540);
			this._panelHoldingToolStrip.ResumeLayout(false);
			this._panelHoldingToolStrip.PerformLayout();
			this._toolStrip.ResumeLayout(false);
			this._toolStrip.PerformLayout();
			this._tabStrip.ResumeLayout(false);
			this._tabStrip.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		private System.Windows.Forms.ToolTip toolTip1;
		private Palaso.UI.WindowsForms.SettingProtection.SettingsLauncherHelper _settingsLauncherHelper;
		private System.Windows.Forms.Panel _containerPanel;
		private System.Windows.Forms.Panel _toolSpecificPanel;
		private System.Windows.Forms.Panel _panelHoldingToolStrip;
		private System.Windows.Forms.ToolStrip _toolStrip;
		private System.Windows.Forms.ToolStripDropDownButton _helpMenu;
		private System.Windows.Forms.ToolStripMenuItem _creditsMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _webSiteMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _documentationMenuItem;
		private System.Windows.Forms.ToolStripMenuItem _makeASuggestionMenuItem;
		private System.Windows.Forms.ToolStripButton toolStripButton1;
		private System.Windows.Forms.ToolStripButton toolStripButton2;
		private Messir.Windows.Forms.TabStripButton _collectionTab;
		private Messir.Windows.Forms.TabStripButton _editTab;
		private Messir.Windows.Forms.TabStripButton _publishTab;
		private Messir.Windows.Forms.TabStrip _tabStrip;
		private System.Windows.Forms.ToolStripMenuItem _releaseNotesMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
		private System.Windows.Forms.ToolStripMenuItem deepBloomPaperToolStripMenuItem;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;


    }
}