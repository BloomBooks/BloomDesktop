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
			System.Windows.Forms.ListViewItem listViewItem1 = new System.Windows.Forms.ListViewItem("Edolo Ado Beba Agedu", 0);
			System.Windows.Forms.ListViewItem listViewItem2 = new System.Windows.Forms.ListViewItem("A5 Booklet", 0);
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LibraryListView));
			this._listView = new System.Windows.Forms.ListView();
			this._pageThumbnails = new System.Windows.Forms.ImageList(this.components);
			this.SuspendLayout();
			// 
			// _listView
			// 
			this._listView.BackColor = System.Drawing.Color.White;
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
			listViewItem1.Group = listViewGroup1;
			listViewItem2.Group = listViewGroup2;
			this._listView.Items.AddRange(new System.Windows.Forms.ListViewItem[] {
            listViewItem1,
            listViewItem2});
			this._listView.LargeImageList = this._pageThumbnails;
			this._listView.Location = new System.Drawing.Point(0, 0);
			this._listView.MultiSelect = false;
			this._listView.Name = "_listView";
			this._listView.Size = new System.Drawing.Size(167, 468);
			this._listView.TabIndex = 1;
			this._listView.UseCompatibleStateImageBehavior = false;
			this._listView.SelectedIndexChanged += new System.EventHandler(this.listView1_SelectedIndexChanged);
			// 
			// _pageThumbnails
			// 
			this._pageThumbnails.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_pageThumbnails.ImageStream")));
			this._pageThumbnails.TransparentColor = System.Drawing.Color.Transparent;
			this._pageThumbnails.Images.SetKeyName(0, "booklet70x70.png");
			// 
			// LibraryListView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(58)))), ((int)(((byte)(63)))), ((int)(((byte)(51)))));
			this.Controls.Add(this._listView);
			this.Name = "LibraryListView";
			this.Size = new System.Drawing.Size(167, 468);
			this.BackColorChanged += new System.EventHandler(this.LibraryListView_BackColorChanged);
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ListView _listView;
        private System.Windows.Forms.ImageList _pageThumbnails;
    }
}