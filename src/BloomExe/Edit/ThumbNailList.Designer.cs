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
			System.Windows.Forms.ListViewItem listViewItem1 = new System.Windows.Forms.ListViewItem("Page 1", 0);
			System.Windows.Forms.ListViewItem listViewItem2 = new System.Windows.Forms.ListViewItem("", 0);
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ThumbNailList));
			this._listView = new System.Windows.Forms.ListView();
			this._thumbnailImageList = new System.Windows.Forms.ImageList(this.components);
			this._clearSelectionTimer = new System.Windows.Forms.Timer(this.components);
			this.SuspendLayout();
			// 
			// _listView
			// 
			this._listView.Activation = System.Windows.Forms.ItemActivation.OneClick;
			this._listView.BackColor = System.Drawing.SystemColors.Control;
			this._listView.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this._listView.Dock = System.Windows.Forms.DockStyle.Fill;
			this._listView.ForeColor = System.Drawing.Color.White;
			this._listView.HideSelection = false;
			this._listView.Items.AddRange(new System.Windows.Forms.ListViewItem[] {
            listViewItem1,
            listViewItem2});
			this._listView.LargeImageList = this._thumbnailImageList;
			this._listView.Location = new System.Drawing.Point(0, 0);
			this._listView.MultiSelect = false;
			this._listView.Name = "_listView";
			this._listView.ShowGroups = false;
			this._listView.Size = new System.Drawing.Size(150, 150);
			this._listView.TabIndex = 1;
			this._listView.UseCompatibleStateImageBehavior = false;
			this._listView.SelectedIndexChanged += new System.EventHandler(this.listView1_SelectedIndexChanged);
			this._listView.MouseUp += new System.Windows.Forms.MouseEventHandler(this._listView_MouseUp);
			this._listView.MouseDown += new System.Windows.Forms.MouseEventHandler(this._listView_MouseDown);
			this._listView.BackColorChanged += new System.EventHandler(this.listView1_BackColorChanged);
			// 
			// _thumbnailImageList
			// 
			this._thumbnailImageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_thumbnailImageList.ImageStream")));
			this._thumbnailImageList.TransparentColor = System.Drawing.Color.Transparent;
			this._thumbnailImageList.Images.SetKeyName(0, "x-office-document.png");
			// 
			// _clearSelectionTimer
			// 
			this._clearSelectionTimer.Enabled = true;
			this._clearSelectionTimer.Interval = 1000;
			this._clearSelectionTimer.Tick += new System.EventHandler(this._clearSelectionTimer_Tick);
			// 
			// ThumbNailList
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._listView);
			this.Name = "ThumbNailList";
			this.BackColorChanged += new System.EventHandler(this.ThumbNailList_BackColorChanged);
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ListView _listView;
        private System.Windows.Forms.ImageList _thumbnailImageList;
		private System.Windows.Forms.Timer _clearSelectionTimer;
    }
}
