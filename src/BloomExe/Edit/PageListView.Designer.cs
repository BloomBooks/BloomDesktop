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
			System.Drawing.Imaging.ImageAttributes imageAttributes2 = new System.Drawing.Imaging.ImageAttributes();
			this._pageThumbnails = new System.Windows.Forms.ImageList(this.components);
			this._pagesLabel = new System.Windows.Forms.Label();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._addPageButton = new Palaso.UI.WindowsForms.Widgets.BitmapButton();
			this._pageControlsPanel = new System.Windows.Forms.Panel();
			this._thumbNailList = new Bloom.Edit.WebThumbNailList();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this._pageControlsPanel.SuspendLayout();
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
			this._pagesLabel.Dock = System.Windows.Forms.DockStyle.Top;
			this._pagesLabel.Font = new System.Drawing.Font("Segoe UI", 11F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._pagesLabel.ForeColor = System.Drawing.Color.WhiteSmoke;
			this._L10NSharpExtender.SetLocalizableToolTip(this._pagesLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._pagesLabel, null);
			this._L10NSharpExtender.SetLocalizingId(this._pagesLabel, "EditTab.PageList.Heading");
			this._pagesLabel.Location = new System.Drawing.Point(0, 0);
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
			// _addPageButton
			// 
			this._addPageButton.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._addPageButton.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._addPageButton.BorderColor = System.Drawing.Color.Transparent;
			this._addPageButton.DisabledTextColor = System.Drawing.Color.FromArgb(((int)(((byte)(87)))), ((int)(((byte)(87)))), ((int)(((byte)(87)))));
			this._addPageButton.FlatAppearance.BorderSize = 0;
			this._addPageButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._addPageButton.FocusRectangleEnabled = true;
			this._addPageButton.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._addPageButton.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(129)))), ((int)(((byte)(146)))));
			this._addPageButton.Image = null;
			this._addPageButton.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
			this._addPageButton.ImageAttributes = imageAttributes2;
			this._addPageButton.ImageBorderColor = System.Drawing.Color.Transparent;
			this._addPageButton.ImageBorderEnabled = false;
			this._addPageButton.ImageDropShadow = false;
			this._addPageButton.ImageFocused = null;
			this._addPageButton.ImageInactive = null;
			this._addPageButton.ImageMouseOver = null;
			this._addPageButton.ImageNormal = global::Bloom.Properties.Resources.AddPageButton;
			this._addPageButton.ImagePressed = null;
			this._addPageButton.InnerBorderColor = System.Drawing.Color.Transparent;
			this._addPageButton.InnerBorderColor_Focus = System.Drawing.Color.LightBlue;
			this._addPageButton.InnerBorderColor_MouseOver = System.Drawing.Color.Gold;
			this._L10NSharpExtender.SetLocalizableToolTip(this._addPageButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._addPageButton, "This is for the button that LAUNCHES the dialog, not the \"Add this page\" button that is IN the dialog.");
			this._L10NSharpExtender.SetLocalizingId(this._addPageButton, "EditTab.AddPageDialog.AddPageButton");
			this._addPageButton.Location = new System.Drawing.Point(23, 17);
			this._addPageButton.Name = "_addPageButton";
			this._addPageButton.OffsetPressedContent = true;
			this._addPageButton.Size = new System.Drawing.Size(68, 60);
			this._addPageButton.StretchImage = false;
			this._addPageButton.TabIndex = 10;
			this._addPageButton.Text = "Add Page";
			this._addPageButton.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
			this._addPageButton.TextDropShadow = false;
			this._addPageButton.UseVisualStyleBackColor = false;
			this._addPageButton.Click += new System.EventHandler(this._addPageButton_Click);
			// 
			// _pageControlsPanel
			// 
			this._pageControlsPanel.Controls.Add(this._addPageButton);
			this._pageControlsPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
			this._pageControlsPanel.Location = new System.Drawing.Point(0, 199);
			this._pageControlsPanel.Name = "_pageControlsPanel";
			this._pageControlsPanel.Size = new System.Drawing.Size(137, 80);
			this._pageControlsPanel.TabIndex = 3;
			// 
			// _thumbNailList
			// 
			this._thumbNailList.BackColor = System.Drawing.SystemColors.Control;
			this._thumbNailList.Dock = System.Windows.Forms.DockStyle.Fill;
			this._thumbNailList.ForeColor = System.Drawing.SystemColors.WindowText;
			this._thumbNailList.Isolator = null;
			this._L10NSharpExtender.SetLocalizableToolTip(this._thumbNailList, null);
			this._L10NSharpExtender.SetLocalizationComment(this._thumbNailList, null);
			this._L10NSharpExtender.SetLocalizingId(this._thumbNailList, "WebThumbNailList");
			this._thumbNailList.Location = new System.Drawing.Point(0, 20);
			this._thumbNailList.Name = "_thumbNailList";
			this._thumbNailList.PreferPageNumbers = false;
			this._thumbNailList.RelocatePageEvent = null;
			this._thumbNailList.Size = new System.Drawing.Size(137, 179);
			this._thumbNailList.TabIndex = 4;
			// 
			// PageListView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.Controls.Add(this._thumbNailList);
			this.Controls.Add(this._pageControlsPanel);
			this.Controls.Add(this._pagesLabel);
			this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "EditTab.PageList");
			this.Name = "PageListView";
			this.Size = new System.Drawing.Size(137, 279);
			this.BackColorChanged += new System.EventHandler(this.PageListView_BackColorChanged);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this._pageControlsPanel.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label _pagesLabel;
        private System.Windows.Forms.ImageList _pageThumbnails;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private WebThumbNailList _thumbNailList;
		internal System.Windows.Forms.Panel _pageControlsPanel;
		private Palaso.UI.WindowsForms.Widgets.BitmapButton _addPageButton;
	}
}
