namespace Bloom.Edit
{
    partial class PageListView
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PageListView));
			this._pageThumbnails = new System.Windows.Forms.ImageList(this.components);
			this.label1 = new System.Windows.Forms.Label();
			this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
			this.deletePageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._thumbNailList = new Bloom.Edit.ThumbNailList();
			this.contextMenuStrip1.SuspendLayout();
			this.SuspendLayout();
			// 
			// _pageThumbnails
			// 
			this._pageThumbnails.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_pageThumbnails.ImageStream")));
			this._pageThumbnails.TransparentColor = System.Drawing.Color.Transparent;
			this._pageThumbnails.Images.SetKeyName(0, "x-office-document.png");
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.BackColor = System.Drawing.Color.Transparent;
			this.label1.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label1.ForeColor = Palette.TextAgainstDarkBackground;
			this.label1.Location = new System.Drawing.Point(18, 3);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(50, 20);
			this.label1.TabIndex = 1;
			this.label1.Text = "Pages";
			// 
			// contextMenuStrip1
			// 
			this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.deletePageToolStripMenuItem});
			this.contextMenuStrip1.Name = "contextMenuStrip1";
			this.contextMenuStrip1.Size = new System.Drawing.Size(137, 26);
			this.contextMenuStrip1.Opening += new System.ComponentModel.CancelEventHandler(this.contextMenuStrip1_Opening);
			// 
			// deletePageToolStripMenuItem
			// 
			this.deletePageToolStripMenuItem.Name = "deletePageToolStripMenuItem";
			this.deletePageToolStripMenuItem.Size = new System.Drawing.Size(136, 22);
			this.deletePageToolStripMenuItem.Text = "&Delete Page";
			this.deletePageToolStripMenuItem.Click += new System.EventHandler(this.deletePageToolStripMenuItem_Click);
			// 
			// _thumbNailList
			// 
			this._thumbNailList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._thumbNailList.BackColor = System.Drawing.SystemColors.Control;
			this._thumbNailList.ContextMenuStrip = this.contextMenuStrip1;
			this._thumbNailList.Font = new System.Drawing.Font("Tahoma", 9F);
			this._thumbNailList.ForeColor = System.Drawing.SystemColors.WindowText;
			this._thumbNailList.ItemWhichWouldPrecedeANewPageInsertion = null;
			this._thumbNailList.Location = new System.Drawing.Point(3, 40);
			this._thumbNailList.Name = "_thumbNailList";
			this._thumbNailList.RelocatePageEvent = null;
			this._thumbNailList.Size = new System.Drawing.Size(113, 173);
			this._thumbNailList.TabIndex = 2;
			// 
			// PageListView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.Controls.Add(this._thumbNailList);
			this.Controls.Add(this.label1);
			this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.Name = "PageListView";
			this.Size = new System.Drawing.Size(116, 216);
			this.BackColorChanged += new System.EventHandler(this.PageListView_BackColorChanged);
			this.contextMenuStrip1.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ImageList _pageThumbnails;
        private Bloom.Edit.ThumbNailList _thumbNailList;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem deletePageToolStripMenuItem;
    }
}
