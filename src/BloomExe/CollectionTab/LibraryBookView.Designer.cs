using Bloom.web;

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
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._splitContainerForPreviewAndAboutBrowsers = new Bloom.ToPalaso.BetterSplitContainer(this.components);
			this._readmeBrowser = new Bloom.Browser();
			this._reactBookPreviewControl = new ReactControl();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._splitContainerForPreviewAndAboutBrowsers)).BeginInit();
			this._splitContainerForPreviewAndAboutBrowsers.Panel1.SuspendLayout();
			this._splitContainerForPreviewAndAboutBrowsers.Panel2.SuspendLayout();
			this._splitContainerForPreviewAndAboutBrowsers.SuspendLayout();
			this.SuspendLayout();
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
			// _reactBookPreviewControl
			// 
			this._reactBookPreviewControl.BackColor = System.Drawing.Color.DarkGray;
			this._reactBookPreviewControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this._reactBookPreviewControl.JavascriptBundleName = "legacyBookPreviewBundle";
			this._L10NSharpExtender.SetLocalizableToolTip(this._reactBookPreviewControl, null);
			this._L10NSharpExtender.SetLocalizationComment(this._reactBookPreviewControl, null);
			this._L10NSharpExtender.SetLocalizingId(this._reactBookPreviewControl, "ReactControl");
			this._reactBookPreviewControl.Location = new System.Drawing.Point(0, 0);
			this._reactBookPreviewControl.Name = "_reactBookPreviewControl";
			this._reactBookPreviewControl.Size = new System.Drawing.Size(900, 193);
			this._reactBookPreviewControl.TabIndex = 2;
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
			// LibraryBookView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.White;
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

        private System.Windows.Forms.ToolTip toolTip1;
        private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
        private ToPalaso.BetterSplitContainer _splitContainerForPreviewAndAboutBrowsers;
        private Browser _readmeBrowser;
		private web.ReactControl _reactBookPreviewControl;
	}
}
