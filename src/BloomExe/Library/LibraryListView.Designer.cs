namespace Bloom.Library
{
    partial class LibraryListView
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
			System.Windows.Forms.ListViewGroup listViewGroup1 = new System.Windows.Forms.ListViewGroup("Our Books", System.Windows.Forms.HorizontalAlignment.Left);
			System.Windows.Forms.ListViewGroup listViewGroup2 = new System.Windows.Forms.ListViewGroup("Blank Books", System.Windows.Forms.HorizontalAlignment.Left);
			System.Windows.Forms.ListViewGroup listViewGroup3 = new System.Windows.Forms.ListViewGroup("SIL Health Books", System.Windows.Forms.HorizontalAlignment.Left);
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LibraryListView));
			this._listView = new System.Windows.Forms.ListView();
			this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
			this._updateThumbnailMenu = new System.Windows.Forms.ToolStripMenuItem();
			this._updateFrontMatterToolStripMenu = new System.Windows.Forms.ToolStripMenuItem();
			this._openFolderOnDisk = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem2 = new System.Windows.Forms.ToolStripSeparator();
			this.deleteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._bookThumbnails = new System.Windows.Forms.ImageList(this.components);
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this.contextMenuStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// _listView
			// 
			this._listView.BackColor = System.Drawing.Color.White;
			this._listView.ContextMenuStrip = this.contextMenuStrip1;
			this._listView.Dock = System.Windows.Forms.DockStyle.Fill;
			this._listView.ForeColor = System.Drawing.Color.Black;
			listViewGroup1.Header = "Our Books";
			listViewGroup1.Name = "Our Books";
			listViewGroup2.Header = "Blank Books";
			listViewGroup2.Name = "Blank Books";
			listViewGroup3.Header = "SIL Health Books";
			listViewGroup3.Name = "SIL Health Books";
			this._listView.Groups.AddRange(new System.Windows.Forms.ListViewGroup[] {
            listViewGroup1,
            listViewGroup2,
            listViewGroup3});
			this._listView.HeaderStyle = System.Windows.Forms.ColumnHeaderStyle.None;
			this._listView.HideSelection = false;
			this._listView.LargeImageList = this._bookThumbnails;
			this._listView.Location = new System.Drawing.Point(0, 0);
			this._listView.MultiSelect = false;
			this._listView.Name = "_listView";
			this._listView.Size = new System.Drawing.Size(167, 468);
			this._listView.TabIndex = 1;
			this._listView.UseCompatibleStateImageBehavior = false;
			this._listView.SelectedIndexChanged += new System.EventHandler(this.listView1_SelectedIndexChanged);
			this._listView.DoubleClick += new System.EventHandler(this._listView_DoubleClick);
			this._listView.MouseDown += new System.Windows.Forms.MouseEventHandler(this._listView_MouseDown);
			this._listView.MouseEnter += new System.EventHandler(this._listView_MouseEnter);
			// 
			// contextMenuStrip1
			// 
			this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._updateThumbnailMenu,
            this._updateFrontMatterToolStripMenu,
            this._openFolderOnDisk,
            this.toolStripMenuItem2,
            this.deleteMenuItem});
			this.contextMenuStrip1.Name = "contextMenuStrip1";
			this.contextMenuStrip1.Size = new System.Drawing.Size(182, 98);
			// 
			// _updateThumbnailMenu
			// 
			this._updateThumbnailMenu.Name = "_updateThumbnailMenu";
			this._updateThumbnailMenu.Size = new System.Drawing.Size(181, 22);
			this._updateThumbnailMenu.Text = "Update Thumbnail";
			this._updateThumbnailMenu.Click += new System.EventHandler(this._updateThumbnailMenu_Click);
			// 
			// _updateFrontMatterToolStripMenu
			// 
			this._updateFrontMatterToolStripMenu.Name = "_updateFrontMatterToolStripMenu";
			this._updateFrontMatterToolStripMenu.Size = new System.Drawing.Size(181, 22);
			this._updateFrontMatterToolStripMenu.Text = "Update Front Matter";
			this._updateFrontMatterToolStripMenu.Click += new System.EventHandler(this._updateFrontMatterToolStripMenu_Click);
			// 
			// _openFolderOnDisk
			// 
			this._openFolderOnDisk.Name = "_openFolderOnDisk";
			this._openFolderOnDisk.Size = new System.Drawing.Size(181, 22);
			this._openFolderOnDisk.Text = "Open Folder on Disk";
			this._openFolderOnDisk.Click += new System.EventHandler(this._openFolderOnDisk_Click);
			// 
			// toolStripMenuItem2
			// 
			this.toolStripMenuItem2.Name = "toolStripMenuItem2";
			this.toolStripMenuItem2.Size = new System.Drawing.Size(178, 6);
			// 
			// deleteMenuItem
			// 
			this.deleteMenuItem.Image = global::Bloom.Properties.Resources.DeleteMessageBoxButtonImage;
			this.deleteMenuItem.Name = "deleteMenuItem";
			this.deleteMenuItem.Size = new System.Drawing.Size(181, 22);
			this.deleteMenuItem.Text = "Delete";
			this.deleteMenuItem.Click += new System.EventHandler(this.deleteMenuItem_Click);
			// 
			// _bookThumbnails
			// 
			this._bookThumbnails.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_bookThumbnails.ImageStream")));
			this._bookThumbnails.TransparentColor = System.Drawing.Color.Transparent;
			this._bookThumbnails.Images.SetKeyName(0, "booklet70x70.png");
			// 
			// LibraryListView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(58)))), ((int)(((byte)(63)))), ((int)(((byte)(51)))));
			this.Controls.Add(this._listView);
			this.Name = "LibraryListView";
			this.Size = new System.Drawing.Size(167, 468);
			this.BackColorChanged += new System.EventHandler(this.OnBackColorChanged);
			this.VisibleChanged += new System.EventHandler(this.OnVisibleChanged);
			this.contextMenuStrip1.ResumeLayout(false);
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ListView _listView;
        private System.Windows.Forms.ImageList _bookThumbnails;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem deleteMenuItem;
        private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.ToolStripMenuItem _updateThumbnailMenu;
		private System.Windows.Forms.ToolStripMenuItem _updateFrontMatterToolStripMenu;
		private System.Windows.Forms.ToolStripMenuItem _openFolderOnDisk;
		private System.Windows.Forms.ToolStripSeparator toolStripMenuItem2;
    }
}