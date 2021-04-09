namespace Bloom.CollectionTab
{
    partial class LibraryBookView
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
            if (disposing)
            {
                components?.Dispose();
                components = null;
                _previousPageFile?.Dispose();
                _previousPageFile = null;
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
			this._addToCollectionButton = new System.Windows.Forms.Button();
			this._editBookButton = new System.Windows.Forms.Button();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._splitContainerForPreviewAndAboutBrowsers = new Bloom.ToPalaso.BetterSplitContainer(this.components);
			this._readmeBrowser = new Bloom.Browser();
			this._reactBookPreviewControl = new Bloom.web.ReactControl();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._splitContainerForPreviewAndAboutBrowsers)).BeginInit();
			this._splitContainerForPreviewAndAboutBrowsers.Panel1.SuspendLayout();
			this._splitContainerForPreviewAndAboutBrowsers.Panel2.SuspendLayout();
			this._splitContainerForPreviewAndAboutBrowsers.SuspendLayout();
			this.SuspendLayout();
			// 
			// _addToCollectionButton
			// 
			this._addToCollectionButton.BackColor = System.Drawing.SystemColors.ControlLightLight;
			this._addToCollectionButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._addToCollectionButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._addToCollectionButton.Image = global::Bloom.Properties.Resources.newBook;
			this._addToCollectionButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
			this._L10NSharpExtender.SetLocalizableToolTip(this._addToCollectionButton, "Create a book in my language using this source book");
			this._L10NSharpExtender.SetLocalizationComment(this._addToCollectionButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._addToCollectionButton, "CollectionTab.MakeBookUsingThisTemplate");
			this._addToCollectionButton.Location = new System.Drawing.Point(12, 6);
			this._addToCollectionButton.Name = "_addToCollectionButton";
			this._addToCollectionButton.Size = new System.Drawing.Size(255, 48);
			this._addToCollectionButton.TabIndex = 0;
			this._addToCollectionButton.Text = "Make a book using this source";
			this._addToCollectionButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._addToCollectionButton.UseVisualStyleBackColor = false;
			this._addToCollectionButton.Click += new System.EventHandler(this.OnAddToLibraryClick);
			this._addToCollectionButton.MouseEnter += new System.EventHandler(this._addToLibraryButton_MouseEnter);
			this._addToCollectionButton.MouseLeave += new System.EventHandler(this._addToLibraryButton_MouseLeave);
			// 
			// _editBookButton
			// 
			this._editBookButton.BackColor = System.Drawing.SystemColors.ControlLightLight;
			this._editBookButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._editBookButton.Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._editBookButton.Image = global::Bloom.Properties.Resources.edit;
			this._L10NSharpExtender.SetLocalizableToolTip(this._editBookButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._editBookButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._editBookButton, "CollectionTab.EditBookButton");
			this._editBookButton.Location = new System.Drawing.Point(12, 6);
			this._editBookButton.Name = "_editBookButton";
			this._editBookButton.Size = new System.Drawing.Size(170, 42);
			this._editBookButton.TabIndex = 2;
			this._editBookButton.Text = "Edit this book";
			this._editBookButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._editBookButton.UseVisualStyleBackColor = false;
			this._editBookButton.Click += new System.EventHandler(this._editBookButton_Click);
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// _splitContainerForPreviewAndAboutBrowsers
			// 
			this._splitContainerForPreviewAndAboutBrowsers.Dock = System.Windows.Forms.DockStyle.Fill;
			this._L10NSharpExtender.SetLocalizableToolTip(this._splitContainerForPreviewAndAboutBrowsers, null);
			this._L10NSharpExtender.SetLocalizationComment(this._splitContainerForPreviewAndAboutBrowsers, null);
			this._L10NSharpExtender.SetLocalizingId(this._splitContainerForPreviewAndAboutBrowsers, "betterSplitContainer1");
			this._splitContainerForPreviewAndAboutBrowsers.Location = new System.Drawing.Point(0, 0);
			this._splitContainerForPreviewAndAboutBrowsers.Name = "_splitContainerForPreviewAndAboutBrowsers";
			this._splitContainerForPreviewAndAboutBrowsers.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// _splitContainerForPreviewAndAboutBrowsers.Panel1
			// 
			this._splitContainerForPreviewAndAboutBrowsers.Panel1.Controls.Add(this._reactBookPreviewControl);
			this._splitContainerForPreviewAndAboutBrowsers.Panel1.Controls.Add(this._addToCollectionButton);
			// 
			// _splitContainerForPreviewAndAboutBrowsers.Panel2
			// 
			this._splitContainerForPreviewAndAboutBrowsers.Panel2.Controls.Add(this._readmeBrowser);
			this._splitContainerForPreviewAndAboutBrowsers.Size = new System.Drawing.Size(900, 450);
			this._splitContainerForPreviewAndAboutBrowsers.SplitterDistance = 193;
			this._splitContainerForPreviewAndAboutBrowsers.TabIndex = 3;
			this._splitContainerForPreviewAndAboutBrowsers.TabStop = false;
			this._splitContainerForPreviewAndAboutBrowsers.SplitterMoved += new System.Windows.Forms.SplitterEventHandler(this._splitContainerForPreviewAndAboutBrowsers_SplitterMoved);
			// 
			// _readmeBrowser
			// 
			this._readmeBrowser.BackColor = System.Drawing.Color.DarkGray;
			this._readmeBrowser.ContextMenuProvider = null;
			this._readmeBrowser.ControlKeyEvent = null;
			this._readmeBrowser.Dock = System.Windows.Forms.DockStyle.Fill;
			this._L10NSharpExtender.SetLocalizableToolTip(this._readmeBrowser, null);
			this._L10NSharpExtender.SetLocalizationComment(this._readmeBrowser, null);
			this._L10NSharpExtender.SetLocalizingId(this._readmeBrowser, "CollectionTab.Browser");
			this._readmeBrowser.Location = new System.Drawing.Point(0, 0);
			this._readmeBrowser.Name = "_readmeBrowser";
			this._readmeBrowser.Size = new System.Drawing.Size(900, 253);
			this._readmeBrowser.TabIndex = 2;
			this._readmeBrowser.VerticalScrollDistance = 0;
			this._readmeBrowser.OnBrowserClick += new System.EventHandler(this._readmeBrowser_OnBrowserClick);
			// 
			// _reactBookPreviewControl
			// 
			this._reactBookPreviewControl.BackColor = System.Drawing.Color.DarkGray;
			this._reactBookPreviewControl.JavascriptBundleName = null;
			this._reactBookPreviewControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this._L10NSharpExtender.SetLocalizableToolTip(this._reactBookPreviewControl, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reactBookPreviewControl, null);
			this._L10NSharpExtender.SetLocalizingId(this._reactBookPreviewControl, "ReactControl");
			this._reactBookPreviewControl.Location = new System.Drawing.Point(0, 0);
			this._reactBookPreviewControl.Name = "_reactBookPreviewControl";
			this._reactBookPreviewControl.ReactComponentName = "BookPreviewPanel";
			this._reactBookPreviewControl.Size = new System.Drawing.Size(900, 193);
			this._reactBookPreviewControl.TabIndex = 2;
			this._reactBookPreviewControl.UrlQueryString = null;
			// 
			// LibraryBookView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.White;
			this.Controls.Add(this._editBookButton);
			this.Controls.Add(this._splitContainerForPreviewAndAboutBrowsers);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "CollectionTab.LibraryBookView");
			this.Name = "LibraryBookView";
			this.Size = new System.Drawing.Size(900, 450);
			this.Resize += new System.EventHandler(this.LibraryBookView_Resize);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this._splitContainerForPreviewAndAboutBrowsers.Panel1.ResumeLayout(false);
			this._splitContainerForPreviewAndAboutBrowsers.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this._splitContainerForPreviewAndAboutBrowsers)).EndInit();
			this._splitContainerForPreviewAndAboutBrowsers.ResumeLayout(false);
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button _addToCollectionButton;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.Button _editBookButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
        private ToPalaso.BetterSplitContainer _splitContainerForPreviewAndAboutBrowsers;
        private Browser _readmeBrowser;
		private web.ReactControl _reactBookPreviewControl;
	}
}
