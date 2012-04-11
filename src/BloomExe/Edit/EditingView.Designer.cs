namespace Bloom.Edit
{
    partial class EditingView
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EditingView));
			this._splitContainer1 = new System.Windows.Forms.SplitContainer();
			this._splitContainer2 = new System.Windows.Forms.SplitContainer();
			this._topBarPanel = new System.Windows.Forms.Panel();
			this._splitTemplateAndSource = new System.Windows.Forms.SplitContainer();
			this._editButtonsUpdateTimer = new System.Windows.Forms.Timer(this.components);
			this._handleMessageTimer = new System.Windows.Forms.Timer(this.components);
			this.settingsLauncherHelper1 = new Palaso.UI.WindowsForms.SettingProtection.SettingsLauncherHelper(this.components);
			this._contentLanguagesDropdown = new System.Windows.Forms.ToolStripDropDownButton();
			this._menusToolStrip = new System.Windows.Forms.ToolStrip();
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this._deletePageButton = new System.Windows.Forms.Button();
			this._pageSizeAndOrientationChoices = new System.Windows.Forms.ToolStripDropDownButton();
			this._undoButton = new System.Windows.Forms.Button();
			this._pasteButton = new System.Windows.Forms.Button();
			this._copyButton = new System.Windows.Forms.Button();
			this._cutButton = new System.Windows.Forms.Button();
			this._browser1 = new Bloom.Browser();
			((System.ComponentModel.ISupportInitialize)(this._splitContainer1)).BeginInit();
			this._splitContainer1.Panel2.SuspendLayout();
			this._splitContainer1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._splitContainer2)).BeginInit();
			this._splitContainer2.Panel1.SuspendLayout();
			this._splitContainer2.Panel2.SuspendLayout();
			this._splitContainer2.SuspendLayout();
			this._topBarPanel.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._splitTemplateAndSource)).BeginInit();
			this._splitTemplateAndSource.SuspendLayout();
			this._menusToolStrip.SuspendLayout();
			this.SuspendLayout();
			// 
			// _splitContainer1
			// 
			this._splitContainer1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this._splitContainer1.Location = new System.Drawing.Point(0, 0);
			this._splitContainer1.Margin = new System.Windows.Forms.Padding(4);
			this._splitContainer1.Name = "_splitContainer1";
			// 
			// _splitContainer1.Panel1
			// 
			this._splitContainer1.Panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			// 
			// _splitContainer1.Panel2
			// 
			this._splitContainer1.Panel2.Controls.Add(this._splitContainer2);
			this._splitContainer1.Size = new System.Drawing.Size(1200, 738);
			this._splitContainer1.SplitterDistance = 279;
			this._splitContainer1.SplitterWidth = 1;
			this._splitContainer1.TabIndex = 0;
			// 
			// _splitContainer2
			// 
			this._splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
			this._splitContainer2.Location = new System.Drawing.Point(0, 0);
			this._splitContainer2.Margin = new System.Windows.Forms.Padding(4);
			this._splitContainer2.Name = "_splitContainer2";
			// 
			// _splitContainer2.Panel1
			// 
			this._splitContainer2.Panel1.Controls.Add(this._topBarPanel);
			this._splitContainer2.Panel1.Controls.Add(this._browser1);
			// 
			// _splitContainer2.Panel2
			// 
			this._splitContainer2.Panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(63)))), ((int)(((byte)(64)))));
			this._splitContainer2.Panel2.Controls.Add(this._splitTemplateAndSource);
			this._splitContainer2.Size = new System.Drawing.Size(920, 738);
			this._splitContainer2.SplitterDistance = 753;
			this._splitContainer2.SplitterWidth = 1;
			this._splitContainer2.TabIndex = 0;
			// 
			// _topBarPanel
			// 
			this._topBarPanel.Controls.Add(this._deletePageButton);
			this._topBarPanel.Controls.Add(this._menusToolStrip);
			this._topBarPanel.Controls.Add(this._undoButton);
			this._topBarPanel.Controls.Add(this._pasteButton);
			this._topBarPanel.Controls.Add(this._copyButton);
			this._topBarPanel.Controls.Add(this._cutButton);
			this._topBarPanel.Location = new System.Drawing.Point(97, 225);
			this._topBarPanel.Name = "_topBarPanel";
			this._topBarPanel.Size = new System.Drawing.Size(563, 66);
			this._topBarPanel.TabIndex = 3;
			this._topBarPanel.Paint += new System.Windows.Forms.PaintEventHandler(this._topBarPanel_Paint);
			// 
			// _splitTemplateAndSource
			// 
			this._splitTemplateAndSource.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._splitTemplateAndSource.Dock = System.Windows.Forms.DockStyle.Fill;
			this._splitTemplateAndSource.Location = new System.Drawing.Point(0, 0);
			this._splitTemplateAndSource.Margin = new System.Windows.Forms.Padding(4);
			this._splitTemplateAndSource.Name = "_splitTemplateAndSource";
			this._splitTemplateAndSource.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// _splitTemplateAndSource.Panel1
			// 
			this._splitTemplateAndSource.Panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			// 
			// _splitTemplateAndSource.Panel2
			// 
			this._splitTemplateAndSource.Panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._splitTemplateAndSource.Size = new System.Drawing.Size(166, 738);
			this._splitTemplateAndSource.SplitterDistance = 303;
			this._splitTemplateAndSource.SplitterWidth = 5;
			this._splitTemplateAndSource.TabIndex = 0;
			// 
			// _editButtonsUpdateTimer
			// 
			this._editButtonsUpdateTimer.Enabled = true;
			this._editButtonsUpdateTimer.Tick += new System.EventHandler(this._editButtonsUpdateTimer_Tick);
			// 
			// _handleMessageTimer
			// 
			this._handleMessageTimer.Tick += new System.EventHandler(this._handleMessageTimer_Tick);
			// 
			// _contentLanguagesDropdown
			// 
			this._contentLanguagesDropdown.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._contentLanguagesDropdown.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._contentLanguagesDropdown.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._contentLanguagesDropdown.Margin = new System.Windows.Forms.Padding(50, 1, 0, 2);
			this._contentLanguagesDropdown.Name = "_contentLanguagesDropdown";
			this._contentLanguagesDropdown.Size = new System.Drawing.Size(129, 19);
			this._contentLanguagesDropdown.Text = "Multilingual Settings";
			this._contentLanguagesDropdown.ToolTipText = "Choose language to make this a bilingual or trilingual book";
			this._contentLanguagesDropdown.DropDownClosed += new System.EventHandler(this._contentLanguagesDropdown_DropDownClosed);
			this._contentLanguagesDropdown.DropDownItemClicked += new System.Windows.Forms.ToolStripItemClickedEventHandler(this._contentLanguagesDropdown_DropDownItemClicked);
			// 
			// _menusToolStrip
			// 
			this._menusToolStrip.AutoSize = false;
			this._menusToolStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._menusToolStrip.CanOverflow = false;
			this._menusToolStrip.Dock = System.Windows.Forms.DockStyle.None;
			this._menusToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._menusToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._contentLanguagesDropdown,
            this._pageSizeAndOrientationChoices});
			this._menusToolStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Flow;
			this._menusToolStrip.Location = new System.Drawing.Point(195, 3);
			this._menusToolStrip.Name = "_menusToolStrip";
			this._menusToolStrip.Size = new System.Drawing.Size(226, 42);
			this._menusToolStrip.TabIndex = 2;
			this._menusToolStrip.Text = "toolStrip1";
			// 
			// _deletePageButton
			// 
			this._deletePageButton.FlatAppearance.BorderSize = 0;
			this._deletePageButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._deletePageButton.Font = new System.Drawing.Font("Segoe UI", 9F);
			this._deletePageButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._deletePageButton.Image = global::Bloom.Properties.Resources.deletePage24x24;
			this._deletePageButton.Location = new System.Drawing.Point(430, 0);
			this._deletePageButton.Name = "_deletePageButton";
			this._deletePageButton.Size = new System.Drawing.Size(92, 49);
			this._deletePageButton.TabIndex = 5;
			this._deletePageButton.Text = "Remove Page";
			this._deletePageButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._deletePageButton.UseVisualStyleBackColor = true;
			// 
			// _pageSizeAndOrientationChoices
			// 
			this._pageSizeAndOrientationChoices.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._pageSizeAndOrientationChoices.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._pageSizeAndOrientationChoices.Image = ((System.Drawing.Image)(resources.GetObject("_pageSizeAndOrientationChoices.Image")));
			this._pageSizeAndOrientationChoices.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._pageSizeAndOrientationChoices.Margin = new System.Windows.Forms.Padding(50, 1, 0, 2);
			this._pageSizeAndOrientationChoices.Name = "_pageSizeAndOrientationChoices";
			this._pageSizeAndOrientationChoices.Size = new System.Drawing.Size(50, 19);
			this._pageSizeAndOrientationChoices.Text = "Paper";
			this._pageSizeAndOrientationChoices.ToolTipText = "Choose a page size and orientation";
			this._pageSizeAndOrientationChoices.DropDownItemClicked += new System.Windows.Forms.ToolStripItemClickedEventHandler(this._pageSizeAndOrientationChoices_DropDownItemClicked);
			// 
			// _undoButton
			// 
			this._undoButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(((int)(((byte)(150)))), ((int)(((byte)(102)))), ((int)(((byte)(143)))));
			this._undoButton.FlatAppearance.BorderSize = 0;
			this._undoButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._undoButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._undoButton.Image = global::Bloom.Properties.Resources.undo32x32;
			this._undoButton.Location = new System.Drawing.Point(125, -3);
			this._undoButton.Name = "_undoButton";
			this._undoButton.Size = new System.Drawing.Size(51, 59);
			this._undoButton.TabIndex = 4;
			this._undoButton.Text = "Undo";
			this._undoButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.toolTip1.SetToolTip(this._undoButton, "Undo (Ctrl-z)");
			this._undoButton.UseVisualStyleBackColor = true;
			this._undoButton.Click += new System.EventHandler(this._undoButton_Click);
			// 
			// _pasteButton
			// 
			this._pasteButton.FlatAppearance.BorderSize = 0;
			this._pasteButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._pasteButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._pasteButton.Image = global::Bloom.Properties.Resources.paste32x32;
			this._pasteButton.Location = new System.Drawing.Point(3, 3);
			this._pasteButton.Name = "_pasteButton";
			this._pasteButton.Size = new System.Drawing.Size(44, 55);
			this._pasteButton.TabIndex = 3;
			this._pasteButton.Text = "Paste";
			this._pasteButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.toolTip1.SetToolTip(this._pasteButton, "Paste (Ctrl-v)");
			this._pasteButton.UseVisualStyleBackColor = true;
			this._pasteButton.Click += new System.EventHandler(this._pasteButton_Click);
			// 
			// _copyButton
			// 
			this._copyButton.FlatAppearance.BorderSize = 0;
			this._copyButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._copyButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._copyButton.Image = global::Bloom.Properties.Resources.Copy16x16;
			this._copyButton.Location = new System.Drawing.Point(53, 27);
			this._copyButton.Name = "_copyButton";
			this._copyButton.Size = new System.Drawing.Size(69, 23);
			this._copyButton.TabIndex = 2;
			this._copyButton.Text = "  Copy";
			this._copyButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this.toolTip1.SetToolTip(this._copyButton, "Copy (Ctrl-c)");
			this._copyButton.UseVisualStyleBackColor = true;
			this._copyButton.Click += new System.EventHandler(this._copyButton_Click);
			// 
			// _cutButton
			// 
			this._cutButton.FlatAppearance.BorderSize = 0;
			this._cutButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._cutButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._cutButton.Image = global::Bloom.Properties.Resources.Cut16x16;
			this._cutButton.Location = new System.Drawing.Point(53, 5);
			this._cutButton.Name = "_cutButton";
			this._cutButton.Size = new System.Drawing.Size(69, 23);
			this._cutButton.TabIndex = 1;
			this._cutButton.Text = "  Cut";
			this._cutButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this.toolTip1.SetToolTip(this._cutButton, "Cut (Ctrl-x)");
			this._cutButton.UseVisualStyleBackColor = true;
			this._cutButton.Click += new System.EventHandler(this._cutButton_Click);
			// 
			// _browser1
			// 
			this._browser1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._browser1.BackColor = System.Drawing.Color.DarkGray;
			this._browser1.Location = new System.Drawing.Point(0, 0);
			this._browser1.Margin = new System.Windows.Forms.Padding(5);
			this._browser1.Name = "_browser1";
			this._browser1.Size = new System.Drawing.Size(753, 742);
			this._browser1.TabIndex = 1;
			this._browser1.OnBrowserClick += new System.EventHandler(this._browser1_OnBrowserClick);
			this._browser1.Validating += new System.ComponentModel.CancelEventHandler(this._browser1_Validating);
			// 
			// EditingView
			// 
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
			this.Controls.Add(this._splitContainer1);
			this.Margin = new System.Windows.Forms.Padding(4);
			this.Name = "EditingView";
			this.Size = new System.Drawing.Size(1200, 738);
			this._splitContainer1.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this._splitContainer1)).EndInit();
			this._splitContainer1.ResumeLayout(false);
			this._splitContainer2.Panel1.ResumeLayout(false);
			this._splitContainer2.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this._splitContainer2)).EndInit();
			this._splitContainer2.ResumeLayout(false);
			this._topBarPanel.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this._splitTemplateAndSource)).EndInit();
			this._splitTemplateAndSource.ResumeLayout(false);
			this._menusToolStrip.ResumeLayout(false);
			this._menusToolStrip.PerformLayout();
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer _splitContainer1;
        private System.Windows.Forms.SplitContainer _splitContainer2;
        private Browser _browser1;
		private System.Windows.Forms.SplitContainer _splitTemplateAndSource;
        private System.Windows.Forms.Timer _editButtonsUpdateTimer;
		private System.Windows.Forms.Timer _handleMessageTimer;
		private System.Windows.Forms.Panel _topBarPanel;
		private System.Windows.Forms.Button _undoButton;
		private System.Windows.Forms.Button _pasteButton;
		private System.Windows.Forms.Button _copyButton;
		private System.Windows.Forms.Button _cutButton;
		private Palaso.UI.WindowsForms.SettingProtection.SettingsLauncherHelper settingsLauncherHelper1;
		private System.Windows.Forms.Button _deletePageButton;
		private System.Windows.Forms.ToolStrip _menusToolStrip;
		private System.Windows.Forms.ToolStripDropDownButton _contentLanguagesDropdown;
		private System.Windows.Forms.ToolStripDropDownButton _pageSizeAndOrientationChoices;
		private System.Windows.Forms.ToolTip toolTip1;


    }
}