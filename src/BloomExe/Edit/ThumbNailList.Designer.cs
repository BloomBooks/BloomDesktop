namespace Bloom.Edit
{
    partial class ThumbNailList
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ThumbNailList));
			this._thumbnailImageList = new System.Windows.Forms.ImageList(this.components);
			this._listView = new System.Windows.Forms.ListView();
			this.SuspendLayout();
			// 
			// _thumbnailImageList
			// 
			this._thumbnailImageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_thumbnailImageList.ImageStream")));
			this._thumbnailImageList.TransparentColor = System.Drawing.Color.Transparent;
			this._thumbnailImageList.Images.SetKeyName(0, "x-office-document.png");
			// 
			// _listView
			// 
			this._listView.BackColor = System.Drawing.SystemColors.MenuHighlight;
			this._listView.Dock = System.Windows.Forms.DockStyle.Fill;
			this._listView.ForeColor = System.Drawing.Color.White;
			this._listView.LargeImageList = this._thumbnailImageList;
			this._listView.Location = new System.Drawing.Point(0, 0);
			this._listView.MultiSelect = false;
			this._listView.Name = "_listView";
			this._listView.OwnerDraw = true;
			this._listView.Size = new System.Drawing.Size(150, 150);
			this._listView.TabIndex = 0;
			this._listView.UseCompatibleStateImageBehavior = false;
			this._listView.DrawItem += new System.Windows.Forms.DrawListViewItemEventHandler(this._listView_DrawItem);
			this._listView.SelectedIndexChanged += new System.EventHandler(this.listView1_SelectedIndexChanged);
			this._listView.MouseUp += new System.Windows.Forms.MouseEventHandler(this._listView_MouseUp);
			this._listView.MouseMove += new System.Windows.Forms.MouseEventHandler(this._listView_MouseMove);
			this._listView.MouseDown += new System.Windows.Forms.MouseEventHandler(this._listView_MouseDown);
			// 
			// ThumbNailList
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.SystemColors.Control;
			this.Controls.Add(this._listView);
			this.ForeColor = System.Drawing.SystemColors.WindowText;
			this.Name = "ThumbNailList";
			this.BackColorChanged += new System.EventHandler(this.ThumbNailList_BackColorChanged);
			this.ResumeLayout(false);

        }

        #endregion

		private System.Windows.Forms.ImageList _thumbnailImageList;
		private System.Windows.Forms.ListView _listView;
    }
}
