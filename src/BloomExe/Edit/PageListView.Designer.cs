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
            this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
            this._thumbNailList = new Bloom.Edit.WebThumbNailList();
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
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
            this._L10NSharpExtender.SetLocalizableToolTip(this._pagesLabel, null);
            this._L10NSharpExtender.SetLocalizationComment(this._pagesLabel, null);
            this._L10NSharpExtender.SetLocalizingId(this._pagesLabel, "EditTab.PageList.Heading");
            this._pagesLabel.Location = new System.Drawing.Point(18, 3);
            this._pagesLabel.Name = "_pagesLabel";
            this._pagesLabel.Size = new System.Drawing.Size(50, 20);
            this._pagesLabel.TabIndex = 1;
            this._pagesLabel.Text = "Pages";
            // 
            // _L10NSharpExtender
            // 
            this._L10NSharpExtender.LocalizationManagerId = "Bloom";
            this._L10NSharpExtender.PrefixForNewItems = null;
            // 
            // _thumbNailList
            // 
            this._thumbNailList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._thumbNailList.BackColor = System.Drawing.SystemColors.Control;
            this._thumbNailList.ForeColor = System.Drawing.SystemColors.WindowText;
            this._L10NSharpExtender.SetLocalizableToolTip(this._thumbNailList, null);
            this._L10NSharpExtender.SetLocalizationComment(this._thumbNailList, null);
            this._L10NSharpExtender.SetLocalizingId(this._thumbNailList, "WebThumbNailList");
            this._thumbNailList.Location = new System.Drawing.Point(0, 36);
            this._thumbNailList.Name = "_thumbNailList";
            this._thumbNailList.PreferPageNumbers = false;
            this._thumbNailList.RelocatePageEvent = null;
            this._thumbNailList.Size = new System.Drawing.Size(116, 177);
            this._thumbNailList.TabIndex = 2;
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
            this._L10NSharpExtender.SetLocalizableToolTip(this, null);
            this._L10NSharpExtender.SetLocalizationComment(this, null);
            this._L10NSharpExtender.SetLocalizingId(this, "EditTab.PageList");
            this.Name = "PageListView";
            this.Size = new System.Drawing.Size(116, 216);
            this.BackColorChanged += new System.EventHandler(this.PageListView_BackColorChanged);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label _pagesLabel;
        private System.Windows.Forms.ImageList _pageThumbnails;
        private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
        private WebThumbNailList _thumbNailList;
    }
}
