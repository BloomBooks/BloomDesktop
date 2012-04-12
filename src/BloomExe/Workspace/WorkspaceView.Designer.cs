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
			this._feedbackButton = new System.Windows.Forms.Button();
			this._settingsButton = new System.Windows.Forms.Button();
			this._openButton1 = new System.Windows.Forms.Button();
			this._infoButton = new System.Windows.Forms.Button();
			this._settingsLauncherHelper = new Palaso.UI.WindowsForms.SettingProtection.SettingsLauncherHelper(this.components);
			this._containerPanel = new System.Windows.Forms.Panel();
			this._topBarButtonTable = new System.Windows.Forms.TableLayoutPanel();
			this._tabStrip = new Messir.Windows.Forms.TabStrip();
			this._libraryTab = new Messir.Windows.Forms.TabStripButton();
			this._editTab = new Messir.Windows.Forms.TabStripButton();
			this._publishTab = new Messir.Windows.Forms.TabStripButton();
			this._infoTab = new Messir.Windows.Forms.TabStripButton();
			this._toolSpecificPanel = new System.Windows.Forms.Panel();
			this._topBarButtonTable.SuspendLayout();
			this._tabStrip.SuspendLayout();
			this.SuspendLayout();
			// 
			// _feedbackButton
			// 
			this._feedbackButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._feedbackButton.AutoSize = true;
			this._feedbackButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._feedbackButton.BackColor = System.Drawing.Color.Transparent;
			this._feedbackButton.FlatAppearance.BorderSize = 0;
			this._feedbackButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._feedbackButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._feedbackButton.Image = global::Bloom.Properties.Resources.feedback24x24;
			this._feedbackButton.Location = new System.Drawing.Point(73, 3);
			this._feedbackButton.Name = "_feedbackButton";
			this._feedbackButton.Size = new System.Drawing.Size(65, 47);
			this._feedbackButton.TabIndex = 26;
			this._feedbackButton.Text = "Feedback";
			this._feedbackButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.toolTip1.SetToolTip(this._feedbackButton, "Settings");
			this._feedbackButton.UseVisualStyleBackColor = false;
			this._feedbackButton.Click += new System.EventHandler(this._feedbackButton_Click);
			// 
			// _settingsButton
			// 
			this._settingsButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._settingsButton.AutoSize = true;
			this._settingsButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._settingsButton.BackColor = System.Drawing.Color.Transparent;
			this._settingsButton.FlatAppearance.BorderSize = 0;
			this._settingsButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._settingsButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._settingsButton.Image = global::Bloom.Properties.Resources.settings24x24;
			this._settingsButton.Location = new System.Drawing.Point(12, 3);
			this._settingsButton.Name = "_settingsButton";
			this._settingsButton.Size = new System.Drawing.Size(55, 47);
			this._settingsButton.TabIndex = 25;
			this._settingsButton.Text = "Settings";
			this._settingsButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.toolTip1.SetToolTip(this._settingsButton, "Settings");
			this._settingsButton.UseVisualStyleBackColor = false;
			this._settingsButton.Click += new System.EventHandler(this.OnSettingsButton_Click);
			// 
			// _openButton1
			// 
			this._openButton1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._openButton1.AutoSize = true;
			this._openButton1.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._openButton1.BackColor = System.Drawing.Color.Transparent;
			this._openButton1.FlatAppearance.BorderSize = 0;
			this._openButton1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._openButton1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._openButton1.Image = global::Bloom.Properties.Resources.OpenCreateLibrary24x24;
			this._openButton1.Location = new System.Drawing.Point(144, 3);
			this._openButton1.Name = "_openButton1";
			this._openButton1.Size = new System.Drawing.Size(113, 47);
			this._openButton1.TabIndex = 24;
			this._openButton1.Text = "Open/Create Library";
			this._openButton1.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.toolTip1.SetToolTip(this._openButton1, "Open or Create Another Library");
			this._openButton1.UseVisualStyleBackColor = false;
			this._openButton1.Click += new System.EventHandler(this.OnOpenCreateLibrary_Click);
			// 
			// _infoButton
			// 
			this._infoButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._infoButton.AutoSize = true;
			this._infoButton.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._infoButton.BackColor = System.Drawing.Color.Transparent;
			this._infoButton.FlatAppearance.BorderSize = 0;
			this._infoButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._infoButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._infoButton.Image = global::Bloom.Properties.Resources.help24x24;
			this._infoButton.Location = new System.Drawing.Point(263, 3);
			this._infoButton.Name = "_infoButton";
			this._infoButton.Size = new System.Drawing.Size(39, 47);
			this._infoButton.TabIndex = 22;
			this._infoButton.Text = "Help";
			this._infoButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.toolTip1.SetToolTip(this._infoButton, "Get Information About Bloom");
			this._infoButton.UseVisualStyleBackColor = false;
			this._infoButton.Click += new System.EventHandler(this.OnInfoButton_Click);
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
			// _topBarButtonTable
			// 
			this._topBarButtonTable.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
			this._topBarButtonTable.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._topBarButtonTable.ColumnCount = 5;
			this._topBarButtonTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this._topBarButtonTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this._topBarButtonTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this._topBarButtonTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this._topBarButtonTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this._topBarButtonTable.Controls.Add(this._feedbackButton, 2, 0);
			this._topBarButtonTable.Controls.Add(this._settingsButton, 2, 0);
			this._topBarButtonTable.Controls.Add(this._openButton1, 1, 0);
			this._topBarButtonTable.Controls.Add(this._infoButton, 0, 0);
			this._topBarButtonTable.GrowStyle = System.Windows.Forms.TableLayoutPanelGrowStyle.AddColumns;
			this._topBarButtonTable.Location = new System.Drawing.Point(790, 3);
			this._topBarButtonTable.Name = "_topBarButtonTable";
			this._topBarButtonTable.RightToLeft = System.Windows.Forms.RightToLeft.Yes;
			this._topBarButtonTable.RowCount = 1;
			this._topBarButtonTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this._topBarButtonTable.Size = new System.Drawing.Size(305, 60);
			this._topBarButtonTable.TabIndex = 0;
			// 
			// _tabStrip
			// 
			this._tabStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._tabStrip.FlipButtons = false;
			this._tabStrip.ImageScalingSize = new System.Drawing.Size(32, 32);
			this._tabStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._libraryTab,
            this._editTab,
            this._publishTab,
            this._infoTab});
			this._tabStrip.Location = new System.Drawing.Point(0, 0);
			this._tabStrip.Name = "_tabStrip";
			this._tabStrip.RenderStyle = System.Windows.Forms.ToolStripRenderMode.Custom;
			this._tabStrip.SelectedTab = this._infoTab;
			this._tabStrip.Size = new System.Drawing.Size(1098, 71);
			this._tabStrip.TabIndex = 15;
			this._tabStrip.Text = "tabStrip1";
			this._tabStrip.UseVisualStyles = false;
			this._tabStrip.SelectedTabChanged += new System.EventHandler<Messir.Windows.Forms.SelectedTabChangedEventArgs>(this._tabStrip_SelectedTabChanged);
			this._tabStrip.BackColorChanged += new System.EventHandler(this._tabStrip_BackColorChanged);
			// 
			// _libraryTab
			// 
			this._libraryTab.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._libraryTab.BarColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._libraryTab.ForeColor = System.Drawing.Color.Black;
			this._libraryTab.HotTextColor = System.Drawing.Color.Black;
			this._libraryTab.Image = global::Bloom.Properties.Resources.library32x32;
			this._libraryTab.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._libraryTab.IsSelected = false;
			this._libraryTab.Margin = new System.Windows.Forms.Padding(0);
			this._libraryTab.Name = "_libraryTab";
			this._libraryTab.Padding = new System.Windows.Forms.Padding(0);
			this._libraryTab.SelectedFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._libraryTab.SelectedTextColor = Palette.TextAgainstDarkBackground;
			this._libraryTab.Size = new System.Drawing.Size(80, 71);
			this._libraryTab.Text = "Library";
			this._libraryTab.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
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
			this._editTab.SelectedTextColor = Palette.TextAgainstDarkBackground;
			this._editTab.Size = new System.Drawing.Size(69, 71);
			this._editTab.Text = "Edit";
			this._editTab.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			// 
			// _publishTab
			// 
			this._publishTab.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._publishTab.BarColor = System.Drawing.Color.FromArgb(((int)(((byte)(214)))), ((int)(((byte)(86)))), ((int)(((byte)(73)))));
			this._publishTab.ForeColor = System.Drawing.Color.Black;
			this._publishTab.HotTextColor = System.Drawing.Color.Black;
			this._publishTab.Image = global::Bloom.Properties.Resources.publish32x32;
			this._publishTab.ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None;
			this._publishTab.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._publishTab.IsSelected = false;
			this._publishTab.Margin = new System.Windows.Forms.Padding(0);
			this._publishTab.Name = "_publishTab";
			this._publishTab.Padding = new System.Windows.Forms.Padding(0);
			this._publishTab.SelectedFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._publishTab.SelectedTextColor = Palette.TextAgainstDarkBackground;
			this._publishTab.Size = new System.Drawing.Size(83, 71);
			this._publishTab.Text = "Publish";
			this._publishTab.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			// 
			// _infoTab
			// 
			this._infoTab.BarColor = System.Drawing.Color.Empty;
			this._infoTab.Checked = true;
			this._infoTab.ForeColor = System.Drawing.Color.Black;
			this._infoTab.HotTextColor = System.Drawing.SystemColors.ControlText;
			this._infoTab.Image = global::Bloom.Properties.Resources.helpTab32x32;
			this._infoTab.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._infoTab.IsSelected = true;
			this._infoTab.Margin = new System.Windows.Forms.Padding(0);
			this._infoTab.Name = "_infoTab";
			this._infoTab.Padding = new System.Windows.Forms.Padding(0);
			this._infoTab.SelectedFont = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
			this._infoTab.SelectedTextColor = Palette.TextAgainstDarkBackground;
			this._infoTab.Size = new System.Drawing.Size(69, 71);
			this._infoTab.Text = "Help";
			this._infoTab.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			// 
			// _toolSpecificPanel
			// 
			this._toolSpecificPanel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._toolSpecificPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(29)))), ((int)(((byte)(148)))), ((int)(((byte)(164)))));
			this._toolSpecificPanel.Location = new System.Drawing.Point(316, 4);
			this._toolSpecificPanel.Name = "_toolSpecificPanel";
			this._toolSpecificPanel.Size = new System.Drawing.Size(474, 66);
			this._toolSpecificPanel.TabIndex = 17;
			// 
			// WorkspaceView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._toolSpecificPanel);
			this.Controls.Add(this._topBarButtonTable);
			this.Controls.Add(this._containerPanel);
			this.Controls.Add(this._tabStrip);
			this.Name = "WorkspaceView";
			this.Size = new System.Drawing.Size(1098, 540);
			this._topBarButtonTable.ResumeLayout(false);
			this._topBarButtonTable.PerformLayout();
			this._tabStrip.ResumeLayout(false);
			this._tabStrip.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		private System.Windows.Forms.ToolTip toolTip1;
		private Palaso.UI.WindowsForms.SettingProtection.SettingsLauncherHelper _settingsLauncherHelper;
		private Messir.Windows.Forms.TabStrip _tabStrip;
		private Messir.Windows.Forms.TabStripButton _libraryTab;
		private Messir.Windows.Forms.TabStripButton _editTab;
		private Messir.Windows.Forms.TabStripButton _publishTab;
		private Messir.Windows.Forms.TabStripButton _infoTab;
		private System.Windows.Forms.Panel _containerPanel;
		private System.Windows.Forms.TableLayoutPanel _topBarButtonTable;
		private System.Windows.Forms.Button _feedbackButton;
		private System.Windows.Forms.Button _settingsButton;
		private System.Windows.Forms.Button _openButton1;
		private System.Windows.Forms.Button _infoButton;
		private System.Windows.Forms.Panel _toolSpecificPanel;


    }
}