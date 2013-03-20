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
            this._pagesLabel = new System.Windows.Forms.Label();
            this._thumbNailList = new Bloom.Edit.ThumbNailList();
            this.localizationExtender1 = new Localization.UI.LocalizationExtender(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).BeginInit();
            this.SuspendLayout();
            // 
            // _pageThumbnails
            // 
            this._pageThumbnails.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_pageThumbnails.ImageStream")));
            this._pageThumbnails.TransparentColor = System.Drawing.Color.Transparent;
            this._pageThumbnails.Images.SetKeyName(0, "x-office-document.png");
            // 
            // _pagesLabel
            // 
            this._pagesLabel.AutoSize = true;
            this._pagesLabel.BackColor = System.Drawing.Color.Transparent;
            this._pagesLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._pagesLabel.ForeColor = System.Drawing.Color.WhiteSmoke;
            this.localizationExtender1.SetLocalizableToolTip(this._pagesLabel, null);
            this.localizationExtender1.SetLocalizationComment(this._pagesLabel, null);
            this.localizationExtender1.SetLocalizingId(this._pagesLabel, "EditTab.PageList.Heading");
            this._pagesLabel.Location = new System.Drawing.Point(18, 3);
            this._pagesLabel.Name = "_pagesLabel";
            this._pagesLabel.Size = new System.Drawing.Size(50, 20);
            this._pagesLabel.TabIndex = 1;
            this._pagesLabel.Text = "Pages";
            // 
            // _thumbNailList
            // 
            this._thumbNailList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._thumbNailList.BackColor = System.Drawing.SystemColors.Control;
            this._thumbNailList.Font = new System.Drawing.Font("Tahoma", 9F);
            this._thumbNailList.ForeColor = System.Drawing.SystemColors.WindowText;
            this._thumbNailList.ItemWhichWouldPrecedeANewPageInsertion = null;
            this.localizationExtender1.SetLocalizableToolTip(this._thumbNailList, null);
            this.localizationExtender1.SetLocalizationComment(this._thumbNailList, null);
            this.localizationExtender1.SetLocalizingId(this._thumbNailList, "PageListView.ThumbNailList");
            this._thumbNailList.Location = new System.Drawing.Point(0, 40);
            this._thumbNailList.Margin = new System.Windows.Forms.Padding(0, 3, 0, 3);
            this._thumbNailList.Name = "_thumbNailList";
            this._thumbNailList.PreferPageNumbers = false;
            this._thumbNailList.RelocatePageEvent = null;
            this._thumbNailList.Size = new System.Drawing.Size(113, 173);
            this._thumbNailList.TabIndex = 2;
            // 
            // localizationExtender1
            // 
            this.localizationExtender1.LocalizationManagerId = "Bloom";
            // 
            // PageListView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.Controls.Add(this._thumbNailList);
            this.Controls.Add(this._pagesLabel);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.localizationExtender1.SetLocalizableToolTip(this, null);
            this.localizationExtender1.SetLocalizationComment(this, null);
            this.localizationExtender1.SetLocalizingId(this, "EditTab.PageList");
            this.Name = "PageListView";
            this.Size = new System.Drawing.Size(116, 216);
            this.BackColorChanged += new System.EventHandler(this.PageListView_BackColorChanged);
            ((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label _pagesLabel;
        private System.Windows.Forms.ImageList _pageThumbnails;
		private Bloom.Edit.ThumbNailList _thumbNailList;
        private Localization.UI.LocalizationExtender localizationExtender1;
    }
}
