using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Bloom.CollectionTab
{
    partial class LibraryListView
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;
        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(LibraryListView));
			this._bookContextMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
			this._copyBook = new System.Windows.Forms.ToolStripMenuItem();
			this._makeBloomPackOfBookToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._openFolderOnDisk = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator3 = new System.Windows.Forms.ToolStripSeparator();
			this._exportToXMLForInDesignToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.exportToWordOrLibreOfficeToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.exportToSpreadsheetToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.SaveAsBloomToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator4 = new System.Windows.Forms.ToolStripSeparator();			
			this._leveledReaderMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._decodableReaderMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
			this._updateThumbnailMenu = new System.Windows.Forms.ToolStripMenuItem();
			this._updateFrontMatterToolStripMenu = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
			this.deleteMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._bookThumbnails = new System.Windows.Forms.ImageList(this.components);
			this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
			this._sourcePaneMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
			this.toolStripMenuItem1 = new System.Windows.Forms.ToolStripMenuItem();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._vernacularCollectionMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
			this.openCreateCollectionToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.makeReaderTemplateBloomPackToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.advancedToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._showHistoryMenu = new System.Windows.Forms.ToolStripMenuItem();
			this._showNotesMenu = new System.Windows.Forms.ToolStripMenuItem();
			this._doChecksOfAllBooksToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._rescueMissingImagesToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.toolStripMenuItem3 = new System.Windows.Forms.ToolStripSeparator();
			this._doChecksAndUpdatesOfAllBooksToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.splitContainer1 = new Bloom.ToPalaso.BetterSplitContainer(this.components);
			this._primaryCollectionFlow = new System.Windows.Forms.FlowLayoutPanel();
			this.label1 = new System.Windows.Forms.Label();
			this.label2 = new System.Windows.Forms.Label();
			this.label3 = new System.Windows.Forms.Label();
			this._menuTriangle = new System.Windows.Forms.PictureBox();
			this.button1 = new System.Windows.Forms.Button();
			this.button4 = new System.Windows.Forms.Button();
			this.button5 = new System.Windows.Forms.Button();
			this.label4 = new System.Windows.Forms.Label();
			this.label5 = new System.Windows.Forms.Label();
			this.button6 = new System.Windows.Forms.Button();
			this._sourceBooksFlow = new System.Windows.Forms.FlowLayoutPanel();
			this.label7 = new System.Windows.Forms.Label();
			this.pretendLabel = new System.Windows.Forms.Label();
			this.label9 = new System.Windows.Forms.Label();
			this._dividerPanel = new System.Windows.Forms.Panel();
			this._settingsProtectionHelper = new SIL.Windows.Forms.SettingProtection.SettingsProtectionHelper(this.components);
			this.renameToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this.importContentFromSpreadsheetToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._bookContextMenu.SuspendLayout();
			this._sourcePaneMenuStrip.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this._vernacularCollectionMenuStrip.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
			this.splitContainer1.Panel1.SuspendLayout();
			this.splitContainer1.Panel2.SuspendLayout();
			this.splitContainer1.SuspendLayout();
			this._primaryCollectionFlow.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._menuTriangle)).BeginInit();
			this._sourceBooksFlow.SuspendLayout();
			this.SuspendLayout();
			// 
			// _bookContextMenu
			// 
			this._bookContextMenu.ImageScalingSize = new System.Drawing.Size(20, 20);

			var toolStripItems = new List<System.Windows.Forms.ToolStripItem>();
			toolStripItems.AddRange(new ToolStripItem[] {
				this._copyBook,
				this._makeBloomPackOfBookToolStripMenuItem,
				this._openFolderOnDisk,
				this.toolStripSeparator3,
				this._exportToXMLForInDesignToolStripMenuItem,
				this.exportToWordOrLibreOfficeToolStripMenuItem,
            });

			if (ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kSpreadsheetImportExport))
			{
				toolStripItems.Add(this.exportToSpreadsheetToolStripMenuItem);
				toolStripItems.Add(this.importContentFromSpreadsheetToolStripMenuItem);
            }

			toolStripItems.AddRange(new ToolStripItem[] {
				this.SaveAsBloomToolStripMenuItem,
				this.toolStripSeparator4,
				this._leveledReaderMenuItem,
				this._decodableReaderMenuItem,
				this.toolStripSeparator1,
				this._updateThumbnailMenu,
				this._updateFrontMatterToolStripMenu,
				this.renameToolStripMenuItem,
				this.toolStripSeparator2,
				this.deleteMenuItem	
            });
			this._bookContextMenu.Items.AddRange(toolStripItems.ToArray());
            
			this._L10NSharpExtender.SetLocalizableToolTip(this._bookContextMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bookContextMenu, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._bookContextMenu, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._bookContextMenu, "CollectionTab.BookMenu.strip");
			this._bookContextMenu.Name = "contextMenuStrip1";
			this._bookContextMenu.Size = new System.Drawing.Size(243, 382);
			// 
			// _copyBook
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._copyBook, null);
			this._L10NSharpExtender.SetLocalizationComment(this._copyBook, null);
			this._L10NSharpExtender.SetLocalizingId(this._copyBook, "CollectionTab.BookMenu.DuplicateBook");
			this._copyBook.Name = "_copyBook";
			this._copyBook.Size = new System.Drawing.Size(242, 26);
			this._copyBook.Text = "Duplicate Book";
			this._copyBook.Click += new System.EventHandler(this._copyBook_Click);
			// 
			// _makeBloomPackOfBookToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._makeBloomPackOfBookToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._makeBloomPackOfBookToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._makeBloomPackOfBookToolStripMenuItem, "CollectionTab.BookMenu.MakeBloomPack");
			this._makeBloomPackOfBookToolStripMenuItem.Name = "_makeBloomPackOfBookToolStripMenuItem";
			this._makeBloomPackOfBookToolStripMenuItem.Size = new System.Drawing.Size(242, 26);
			this._makeBloomPackOfBookToolStripMenuItem.Text = "Make Bloom Pack...";
			this._makeBloomPackOfBookToolStripMenuItem.Click += new System.EventHandler(this.OnMakeBloomPackOfBook);
			// 
			// _openFolderOnDisk
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._openFolderOnDisk, null);
			this._L10NSharpExtender.SetLocalizationComment(this._openFolderOnDisk, null);
			this._L10NSharpExtender.SetLocalizingId(this._openFolderOnDisk, "CollectionTab.ContextMenu.OpenFolderOnDisk");
			this._openFolderOnDisk.Name = "_openFolderOnDisk";
			this._openFolderOnDisk.Size = new System.Drawing.Size(242, 26);
			this._openFolderOnDisk.Text = "Open Folder on Disk";
			this._openFolderOnDisk.Click += new System.EventHandler(this._openFolderOnDisk_Click);
			// 
			// toolStripSeparator3
			// 
			this.toolStripSeparator3.Name = "toolStripSeparator3";
			this.toolStripSeparator3.Size = new System.Drawing.Size(239, 6);
			// 
			// _exportToXMLForInDesignToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._exportToXMLForInDesignToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._exportToXMLForInDesignToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._exportToXMLForInDesignToolStripMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._exportToXMLForInDesignToolStripMenuItem, "CollectionTab.BookMenu.ExportToXMLForInDesign");
			this._exportToXMLForInDesignToolStripMenuItem.Name = "_exportToXMLForInDesignToolStripMenuItem";
			this._exportToXMLForInDesignToolStripMenuItem.Size = new System.Drawing.Size(242, 26);
			this._exportToXMLForInDesignToolStripMenuItem.Text = "Export to XML for InDesign...";
			this._exportToXMLForInDesignToolStripMenuItem.Click += new System.EventHandler(this.OnExportToXmlForInDesign);
			// 
			// exportToWordOrLibreOfficeToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.exportToWordOrLibreOfficeToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.exportToWordOrLibreOfficeToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.exportToWordOrLibreOfficeToolStripMenuItem, "CollectionTab.BookMenu.ExportToWordOrLibreOffice");
			this.exportToWordOrLibreOfficeToolStripMenuItem.Name = "exportToWordOrLibreOfficeToolStripMenuItem";
			this.exportToWordOrLibreOfficeToolStripMenuItem.Size = new System.Drawing.Size(242, 26);
			this.exportToWordOrLibreOfficeToolStripMenuItem.Text = "Export to Word or LibreOffice...";
			this.exportToWordOrLibreOfficeToolStripMenuItem.Click += new System.EventHandler(this.exportToWordOrLibreOfficeToolStripMenuItem_Click);
			// 
			// exportToSpreadsheetToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.exportToSpreadsheetToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.exportToSpreadsheetToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.exportToSpreadsheetToolStripMenuItem, "CollectionTab.BookMenu.ExportToSpreadsheet");
			this.exportToSpreadsheetToolStripMenuItem.Name = "exportToSpreadsheetToolStripMenuItem";
			this.exportToSpreadsheetToolStripMenuItem.Size = new System.Drawing.Size(242, 26);
			this.exportToSpreadsheetToolStripMenuItem.Text = "Export to Spreadsheet...";
			this.exportToSpreadsheetToolStripMenuItem.Click += new System.EventHandler(this.exportToSpreadsheetToolStripMenuItem_Click);
			// 
			// SaveAsBloomToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.SaveAsBloomToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.SaveAsBloomToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.SaveAsBloomToolStripMenuItem, "CollectionTab.BookMenu.SaveAsBloomToolStripMenuItem");
			this.SaveAsBloomToolStripMenuItem.Name = "SaveAsBloomToolStripMenuItem";
			this.SaveAsBloomToolStripMenuItem.Size = new System.Drawing.Size(242, 26);
			this.SaveAsBloomToolStripMenuItem.Text = "Save as single file (.bloom)...";
			this.SaveAsBloomToolStripMenuItem.Click += new System.EventHandler(this.SaveAsBloomToolStripMenuItem_Click);
			// 
			// toolStripSeparator4
			// 
			this.toolStripSeparator4.Name = "toolStripSeparator4";
			this.toolStripSeparator4.Size = new System.Drawing.Size(239, 6);
			// 
			// _leveledReaderMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._leveledReaderMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._leveledReaderMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._leveledReaderMenuItem, ".toolStripMenuItem2");
			this._leveledReaderMenuItem.Name = "_leveledReaderMenuItem";
			this._leveledReaderMenuItem.Size = new System.Drawing.Size(242, 26);
			this._leveledReaderMenuItem.Text = "Leveled Reader";
			this._leveledReaderMenuItem.Click += new System.EventHandler(this._leveledReaderMenuItem_Click);
			// 
			// _decodableReaderMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._decodableReaderMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._decodableReaderMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._decodableReaderMenuItem, ".toolStripMenuItem2");
			this._decodableReaderMenuItem.Name = "_decodableReaderMenuItem";
			this._decodableReaderMenuItem.Size = new System.Drawing.Size(242, 26);
			this._decodableReaderMenuItem.Text = "Decodable Reader";
			this._decodableReaderMenuItem.Click += new System.EventHandler(this._decodableReaderMenuItem_Click);
			// 
			// toolStripSeparator1
			// 
			this.toolStripSeparator1.Name = "toolStripSeparator1";
			this.toolStripSeparator1.Size = new System.Drawing.Size(239, 6);
			// 
			// _updateThumbnailMenu
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._updateThumbnailMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._updateThumbnailMenu, null);
			this._L10NSharpExtender.SetLocalizingId(this._updateThumbnailMenu, "CollectionTab.BookMenu.UpdateThumbnail");
			this._updateThumbnailMenu.Name = "_updateThumbnailMenu";
			this._updateThumbnailMenu.Size = new System.Drawing.Size(242, 26);
			this._updateThumbnailMenu.Text = "Update Thumbnail";
			this._updateThumbnailMenu.ToolTipText = "Did Bloom fail to update the thumbnail you see here? This command makes it try ag" +
    "ain.";
			this._updateThumbnailMenu.Click += new System.EventHandler(this._updateThumbnailMenu_Click);
			// 
			// _updateFrontMatterToolStripMenu
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._updateFrontMatterToolStripMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._updateFrontMatterToolStripMenu, null);
			this._L10NSharpExtender.SetLocalizingId(this._updateFrontMatterToolStripMenu, "CollectionTab.BookMenu.UpdateFrontMatterToolStrip");
			this._updateFrontMatterToolStripMenu.Name = "_updateFrontMatterToolStripMenu";
			this._updateFrontMatterToolStripMenu.Size = new System.Drawing.Size(242, 26);
			this._updateFrontMatterToolStripMenu.Text = "Update Book";
			this._updateFrontMatterToolStripMenu.ToolTipText = resources.GetString("_updateFrontMatterToolStripMenu.ToolTipText");
			this._updateFrontMatterToolStripMenu.Click += new System.EventHandler(this.OnBringBookUpToDate_Click);
			// 
			// toolStripSeparator2
			// 
			this.toolStripSeparator2.Name = "toolStripSeparator2";
			this.toolStripSeparator2.Size = new System.Drawing.Size(239, 6);
			// 
			// deleteMenuItem
			// 
			this.deleteMenuItem.Image = global::Bloom.Properties.Resources.DeleteMessageBoxButtonImage;
			this._L10NSharpExtender.SetLocalizableToolTip(this.deleteMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.deleteMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.deleteMenuItem, "CollectionTab.BookMenu.DeleteBook");
			this.deleteMenuItem.Name = "deleteMenuItem";
			this.deleteMenuItem.Size = new System.Drawing.Size(242, 26);
			this.deleteMenuItem.Text = "Delete Book";
			this.deleteMenuItem.Click += new System.EventHandler(this.deleteMenuItem_Click);
			// 
			// _bookThumbnails
			// 
			this._bookThumbnails.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_bookThumbnails.ImageStream")));
			this._bookThumbnails.TransparentColor = System.Drawing.Color.Transparent;
			this._bookThumbnails.Images.SetKeyName(0, "booklet70x70.png");
			// 
			// toolTip1
			// 
			this.toolTip1.AutoPopDelay = 15000;
			this.toolTip1.InitialDelay = 500;
			this.toolTip1.ReshowDelay = 100;
			// 
			// _sourcePaneMenuStrip
			// 
			this._sourcePaneMenuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
			this._sourcePaneMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripMenuItem1});
			this._L10NSharpExtender.SetLocalizableToolTip(this._sourcePaneMenuStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._sourcePaneMenuStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._sourcePaneMenuStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._sourcePaneMenuStrip, "CollectionTab.SourceBooksMenu.strip");
			this._sourcePaneMenuStrip.Name = "_sourcePaneMenuStrip";
			this._sourcePaneMenuStrip.Size = new System.Drawing.Size(260, 26);
			// 
			// toolStripMenuItem1
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.toolStripMenuItem1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.toolStripMenuItem1, null);
			this._L10NSharpExtender.SetLocalizingId(this.toolStripMenuItem1, "CollectionTab.SourceBooksMenu.OpenFolderContainingSourceCollections");
			this.toolStripMenuItem1.Name = "toolStripMenuItem1";
			this.toolStripMenuItem1.Size = new System.Drawing.Size(259, 22);
			this.toolStripMenuItem1.Text = "Open Additional Collections Folder";
			this.toolStripMenuItem1.Click += new System.EventHandler(this.OnOpenAdditionalCollectionsFolderClick);
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// _vernacularCollectionMenuStrip
			// 
			this._vernacularCollectionMenuStrip.ImageScalingSize = new System.Drawing.Size(20, 20);
			this._vernacularCollectionMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.openCreateCollectionToolStripMenuItem,
            this.makeReaderTemplateBloomPackToolStripMenuItem,
            this.advancedToolStripMenuItem});
			this._L10NSharpExtender.SetLocalizableToolTip(this._vernacularCollectionMenuStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._vernacularCollectionMenuStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._vernacularCollectionMenuStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._vernacularCollectionMenuStrip, "CollectionTab.CollectionMenu.contextMenuStrip2");
			this._vernacularCollectionMenuStrip.Name = "_vernacularCollectionMenuStrip";
			this._vernacularCollectionMenuStrip.Size = new System.Drawing.Size(269, 70);
			// 
			// openCreateCollectionToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.openCreateCollectionToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.openCreateCollectionToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.openCreateCollectionToolStripMenuItem, "CollectionTab.OpenCreateCollectionMenuItem");
			this.openCreateCollectionToolStripMenuItem.Name = "openCreateCollectionToolStripMenuItem";
			this.openCreateCollectionToolStripMenuItem.Size = new System.Drawing.Size(268, 22);
			this.openCreateCollectionToolStripMenuItem.Text = "Open or Create Another Collection";
			this.openCreateCollectionToolStripMenuItem.Click += new System.EventHandler(this.openCreateCollectionToolStripMenuItem_Click);
			// 
			// makeReaderTemplateBloomPackToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.makeReaderTemplateBloomPackToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.makeReaderTemplateBloomPackToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.makeReaderTemplateBloomPackToolStripMenuItem, "CollectionTab.AddMakeReaderTemplateBloomPackToolStripMenuItem");
			this.makeReaderTemplateBloomPackToolStripMenuItem.Name = "makeReaderTemplateBloomPackToolStripMenuItem";
			this.makeReaderTemplateBloomPackToolStripMenuItem.Size = new System.Drawing.Size(268, 22);
			this.makeReaderTemplateBloomPackToolStripMenuItem.Text = "Make Reader Template Bloom Pack...";
			this.makeReaderTemplateBloomPackToolStripMenuItem.Click += new System.EventHandler(this.makeReaderTemplateBloomPackToolStripMenuItem_Click);
			// 
			// advancedToolStripMenuItem
			// 
			this.advancedToolStripMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._showHistoryMenu,
            this._showNotesMenu,
            this._doChecksOfAllBooksToolStripMenuItem,
            this._rescueMissingImagesToolStripMenuItem,
            this.toolStripMenuItem3,
            this._doChecksAndUpdatesOfAllBooksToolStripMenuItem});
			this._L10NSharpExtender.SetLocalizableToolTip(this.advancedToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.advancedToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.advancedToolStripMenuItem, "CollectionTab.AdvancedToolStripMenuItem");
			this.advancedToolStripMenuItem.Name = "advancedToolStripMenuItem";
			this.advancedToolStripMenuItem.Size = new System.Drawing.Size(268, 22);
			this.advancedToolStripMenuItem.Text = "Advanced";
			// 
			// _showHistoryMenu
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._showHistoryMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showHistoryMenu, null);
			this._L10NSharpExtender.SetLocalizingId(this._showHistoryMenu, "CollectionTab.CollectionMenu.showHistory");
			this._showHistoryMenu.Name = "_showHistoryMenu";
			this._showHistoryMenu.Size = new System.Drawing.Size(205, 22);
			this._showHistoryMenu.Text = "Collection History...";
			this._showHistoryMenu.Click += new System.EventHandler(this.OnVernacularProjectHistoryClick);
			// 
			// _showNotesMenu
			// 
			this._showNotesMenu.Enabled = false;
			this._L10NSharpExtender.SetLocalizableToolTip(this._showNotesMenu, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showNotesMenu, null);
			this._L10NSharpExtender.SetLocalizingId(this._showNotesMenu, "CollectionTab.CollectionMenu.showNotes");
			this._showNotesMenu.Name = "_showNotesMenu";
			this._showNotesMenu.Size = new System.Drawing.Size(205, 22);
			this._showNotesMenu.Text = "Collection Notes...";
			this._showNotesMenu.Click += new System.EventHandler(this.OnShowNotesMenu);
			// 
			// _doChecksOfAllBooksToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._doChecksOfAllBooksToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._doChecksOfAllBooksToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._doChecksOfAllBooksToolStripMenuItem, "CollectionTab.CollectionMenu.doChecksOfAllBooks");
			this._doChecksOfAllBooksToolStripMenuItem.Name = "_doChecksOfAllBooksToolStripMenuItem";
			this._doChecksOfAllBooksToolStripMenuItem.Size = new System.Drawing.Size(205, 22);
			this._doChecksOfAllBooksToolStripMenuItem.Text = "Do Checks of All Books";
			this._doChecksOfAllBooksToolStripMenuItem.Click += new System.EventHandler(this._doChecksOfAllBooksToolStripMenuItem_Click);
			// 
			// _rescueMissingImagesToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._rescueMissingImagesToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._rescueMissingImagesToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._rescueMissingImagesToolStripMenuItem, "CollectionTab.CollectionMenu.rescueMissingImages");
			this._rescueMissingImagesToolStripMenuItem.Name = "_rescueMissingImagesToolStripMenuItem";
			this._rescueMissingImagesToolStripMenuItem.Size = new System.Drawing.Size(205, 22);
			this._rescueMissingImagesToolStripMenuItem.Text = "Rescue Missing Images...";
			this._rescueMissingImagesToolStripMenuItem.Click += new System.EventHandler(this._rescueMissingImagesToolStripMenuItem_Click);
			// 
			// toolStripMenuItem3
			// 
			this.toolStripMenuItem3.Name = "toolStripMenuItem3";
			this.toolStripMenuItem3.Size = new System.Drawing.Size(202, 6);
			// 
			// _doChecksAndUpdatesOfAllBooksToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._doChecksAndUpdatesOfAllBooksToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._doChecksAndUpdatesOfAllBooksToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this._doChecksAndUpdatesOfAllBooksToolStripMenuItem, "CollectionTab.CollectionMenu.doChecksAndUpdatesOfAllBooks");
			this._doChecksAndUpdatesOfAllBooksToolStripMenuItem.Name = "_doChecksAndUpdatesOfAllBooksToolStripMenuItem";
			this._doChecksAndUpdatesOfAllBooksToolStripMenuItem.Size = new System.Drawing.Size(205, 22);
			this._doChecksAndUpdatesOfAllBooksToolStripMenuItem.Text = "Do Updates of All Books";
			this._doChecksAndUpdatesOfAllBooksToolStripMenuItem.Click += new System.EventHandler(this._doChecksAndUpdatesOfAllBooksToolStripMenuItem_Click);
			// 
			// splitContainer1
			// 
			this.splitContainer1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(54)))), ((int)(((byte)(51)))), ((int)(((byte)(51)))));
			this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
			this._L10NSharpExtender.SetLocalizableToolTip(this.splitContainer1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.splitContainer1, null);
			this._L10NSharpExtender.SetLocalizingId(this.splitContainer1, "LibraryListView.splitContainer1");
			this.splitContainer1.Location = new System.Drawing.Point(0, 0);
			this.splitContainer1.Name = "splitContainer1";
			this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
			// 
			// splitContainer1.Panel1
			// 
			this.splitContainer1.Panel1.Controls.Add(this._primaryCollectionFlow);
			// 
			// splitContainer1.Panel2
			// 
			this.splitContainer1.Panel2.Controls.Add(this._sourceBooksFlow);
			this.splitContainer1.Panel2.Controls.Add(this._dividerPanel);
			this.splitContainer1.Size = new System.Drawing.Size(350, 562);
			this.splitContainer1.SplitterDistance = 302;
			this.splitContainer1.SplitterWidth = 10;
			this.splitContainer1.TabIndex = 1;
			this.splitContainer1.TabStop = false;
			// 
			// _primaryCollectionFlow
			// 
			this._primaryCollectionFlow.AutoScroll = true;
			this._primaryCollectionFlow.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._primaryCollectionFlow.ContextMenuStrip = this._vernacularCollectionMenuStrip;
			this._primaryCollectionFlow.Controls.Add(this.label1);
			this._primaryCollectionFlow.Controls.Add(this.label2);
			this._primaryCollectionFlow.Controls.Add(this.label3);
			this._primaryCollectionFlow.Controls.Add(this._menuTriangle);
			this._primaryCollectionFlow.Controls.Add(this.button1);
			this._primaryCollectionFlow.Controls.Add(this.button4);
			this._primaryCollectionFlow.Controls.Add(this.button5);
			this._primaryCollectionFlow.Controls.Add(this.label4);
			this._primaryCollectionFlow.Controls.Add(this.label5);
			this._primaryCollectionFlow.Controls.Add(this.button6);
			this._primaryCollectionFlow.Dock = System.Windows.Forms.DockStyle.Fill;
			this._primaryCollectionFlow.Location = new System.Drawing.Point(0, 0);
			this._primaryCollectionFlow.Name = "_primaryCollectionFlow";
			this._primaryCollectionFlow.Size = new System.Drawing.Size(350, 302);
			this._primaryCollectionFlow.TabIndex = 0;
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Dock = System.Windows.Forms.DockStyle.Bottom;
			this._primaryCollectionFlow.SetFlowBreak(this.label1, true);
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "LibraryListView.label1");
			this.label1.Location = new System.Drawing.Point(3, 0);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(0, 13);
			this.label1.TabIndex = 0;
			// 
			// label2
			// 
			this.label2.AutoSize = true;
			this.label2.ForeColor = System.Drawing.Color.White;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label2, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label2, null);
			this._L10NSharpExtender.SetLocalizingId(this.label2, "LibraryListView.label2");
			this.label2.Location = new System.Drawing.Point(0, 13);
			this.label2.Margin = new System.Windows.Forms.Padding(0);
			this.label2.Name = "label2";
			this.label2.Size = new System.Drawing.Size(0, 13);
			this.label2.TabIndex = 3;
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.label3.ForeColor = System.Drawing.Color.WhiteSmoke;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label3, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label3, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.label3, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this.label3, "LibraryListView.label3");
			this.label3.Location = new System.Drawing.Point(0, 13);
			this.label3.Margin = new System.Windows.Forms.Padding(0);
			this.label3.Name = "label3";
			this.label3.Padding = new System.Windows.Forms.Padding(10, 10, 0, 0);
			this.label3.Size = new System.Drawing.Size(69, 29);
			this.label3.TabIndex = 6;
			this.label3.Text = "Header";
			// 
			// _menuTriangle
			// 
			this._primaryCollectionFlow.SetFlowBreak(this._menuTriangle, true);
			this._menuTriangle.Image = global::Bloom.Properties.Resources.greyTriangleButton;
			this._L10NSharpExtender.SetLocalizableToolTip(this._menuTriangle, null);
			this._L10NSharpExtender.SetLocalizationComment(this._menuTriangle, null);
			this._L10NSharpExtender.SetLocalizingId(this._menuTriangle, "pictureBox1");
			this._menuTriangle.Location = new System.Drawing.Point(72, 16);
			this._menuTriangle.Name = "_menuTriangle";
			this._menuTriangle.Size = new System.Drawing.Size(10, 28);
			this._menuTriangle.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this._menuTriangle.TabIndex = 14;
			this._menuTriangle.TabStop = false;
			this._menuTriangle.Click += new System.EventHandler(this._menuButton_Click);
			// 
			// button1
			// 
			this.button1.AutoSize = true;
			this.button1.Dock = System.Windows.Forms.DockStyle.Top;
			this.button1.FlatAppearance.BorderSize = 0;
			this.button1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button1.Image = global::Bloom.Properties.Resources.edit;
			this.button1.ImageAlign = System.Drawing.ContentAlignment.TopCenter;
			this._L10NSharpExtender.SetLocalizableToolTip(this.button1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button1, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.button1, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this.button1, "LibraryListView.button1");
			this.button1.Location = new System.Drawing.Point(3, 50);
			this.button1.Name = "button1";
			this.button1.Size = new System.Drawing.Size(211, 93);
			this.button1.TabIndex = 1;
			this.button1.Text = "The amazing\r\nadventures \r\nof aunt altimony";
			this.button1.TextAlign = System.Drawing.ContentAlignment.BottomCenter;
			this.button1.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.button1.UseVisualStyleBackColor = true;
			// 
			// button4
			// 
			this.button4.AutoSize = true;
			this.button4.Dock = System.Windows.Forms.DockStyle.Top;
			this.button4.FlatAppearance.BorderSize = 0;
			this.button4.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this.button4.Image = global::Bloom.Properties.Resources.edit;
			this._L10NSharpExtender.SetLocalizableToolTip(this.button4, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button4, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.button4, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this.button4, "LibraryListView.button4");
			this.button4.Location = new System.Drawing.Point(220, 50);
			this.button4.Name = "button4";
			this.button4.Size = new System.Drawing.Size(75, 59);
			this.button4.TabIndex = 7;
			this.button4.Text = "button4";
			this.button4.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.button4.UseVisualStyleBackColor = true;
			// 
			// button5
			// 
			this.button5.AutoSize = true;
			this.button5.Dock = System.Windows.Forms.DockStyle.Top;
			this.button5.FlatAppearance.BorderSize = 0;
			this.button5.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._primaryCollectionFlow.SetFlowBreak(this.button5, true);
			this.button5.Image = global::Bloom.Properties.Resources.edit;
			this._L10NSharpExtender.SetLocalizableToolTip(this.button5, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button5, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.button5, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this.button5, "LibraryListView.button5");
			this.button5.Location = new System.Drawing.Point(3, 149);
			this.button5.Name = "button5";
			this.button5.Size = new System.Drawing.Size(75, 59);
			this.button5.TabIndex = 8;
			this.button5.Text = "button5";
			this.button5.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.button5.UseVisualStyleBackColor = true;
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.ForeColor = System.Drawing.Color.White;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label4, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label4, null);
			this._L10NSharpExtender.SetLocalizingId(this.label4, "LibraryListView.label4");
			this.label4.Location = new System.Drawing.Point(0, 211);
			this.label4.Margin = new System.Windows.Forms.Padding(0);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(0, 13);
			this.label4.TabIndex = 9;
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this._primaryCollectionFlow.SetFlowBreak(this.label5, true);
			this.label5.ForeColor = System.Drawing.Color.White;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label5, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label5, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.label5, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this.label5, "LibraryListView.label5");
			this.label5.Location = new System.Drawing.Point(0, 211);
			this.label5.Margin = new System.Windows.Forms.Padding(0);
			this.label5.Name = "label5";
			this.label5.Padding = new System.Windows.Forms.Padding(0, 20, 0, 0);
			this.label5.Size = new System.Drawing.Size(42, 33);
			this.label5.TabIndex = 10;
			this.label5.Text = "Header";
			// 
			// button6
			// 
			this.button6.AutoSize = true;
			this.button6.Dock = System.Windows.Forms.DockStyle.Top;
			this.button6.FlatAppearance.BorderSize = 0;
			this.button6.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._primaryCollectionFlow.SetFlowBreak(this.button6, true);
			this.button6.Image = global::Bloom.Properties.Resources.edit;
			this._L10NSharpExtender.SetLocalizableToolTip(this.button6, null);
			this._L10NSharpExtender.SetLocalizationComment(this.button6, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.button6, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this.button6, "LibraryListView.button6");
			this.button6.Location = new System.Drawing.Point(3, 247);
			this.button6.Name = "button6";
			this.button6.Size = new System.Drawing.Size(176, 59);
			this.button6.TabIndex = 11;
			this.button6.Text = "rat na pik i painim mango";
			this.button6.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this.button6.UseVisualStyleBackColor = true;
			// 
			// _sourceBooksFlow
			// 
			this._sourceBooksFlow.AutoScroll = true;
			this._sourceBooksFlow.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._sourceBooksFlow.ContextMenuStrip = this._sourcePaneMenuStrip;
			this._sourceBooksFlow.Controls.Add(this.label7);
			this._sourceBooksFlow.Controls.Add(this.pretendLabel);
			this._sourceBooksFlow.Controls.Add(this.label9);
			this._sourceBooksFlow.Dock = System.Windows.Forms.DockStyle.Fill;
			this._sourceBooksFlow.Location = new System.Drawing.Point(0, 1);
			this._sourceBooksFlow.Name = "_sourceBooksFlow";
			this._sourceBooksFlow.Size = new System.Drawing.Size(350, 249);
			this._sourceBooksFlow.TabIndex = 0;
			// 
			// label7
			// 
			this.label7.AutoSize = true;
			this.label7.ForeColor = System.Drawing.Color.White;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label7, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label7, null);
			this._L10NSharpExtender.SetLocalizingId(this.label7, "LibraryListView.label7");
			this.label7.Location = new System.Drawing.Point(0, 0);
			this.label7.Margin = new System.Windows.Forms.Padding(0);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(0, 13);
			this.label7.TabIndex = 3;
			// 
			// pretendLabel
			// 
			this.pretendLabel.AutoSize = true;
			this._sourceBooksFlow.SetFlowBreak(this.pretendLabel, true);
			this.pretendLabel.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.pretendLabel.ForeColor = System.Drawing.Color.WhiteSmoke;
			this._L10NSharpExtender.SetLocalizableToolTip(this.pretendLabel, null);
			this._L10NSharpExtender.SetLocalizationComment(this.pretendLabel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this.pretendLabel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this.pretendLabel, "LibraryListView.label8");
			this.pretendLabel.Location = new System.Drawing.Point(0, 0);
			this.pretendLabel.Margin = new System.Windows.Forms.Padding(0);
			this.pretendLabel.Name = "pretendLabel";
			this.pretendLabel.Padding = new System.Windows.Forms.Padding(10, 10, 0, 0);
			this.pretendLabel.Size = new System.Drawing.Size(110, 29);
			this.pretendLabel.TabIndex = 6;
			this.pretendLabel.Text = "Source Books";
			// 
			// label9
			// 
			this.label9.AutoSize = true;
			this.label9.ForeColor = System.Drawing.Color.White;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label9, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label9, null);
			this._L10NSharpExtender.SetLocalizingId(this.label9, "LibraryListView.label9");
			this.label9.Location = new System.Drawing.Point(0, 29);
			this.label9.Margin = new System.Windows.Forms.Padding(0);
			this.label9.Name = "label9";
			this.label9.Size = new System.Drawing.Size(0, 13);
			this.label9.TabIndex = 9;
			// 
			// _dividerPanel
			// 
			this._dividerPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(54)))), ((int)(((byte)(51)))), ((int)(((byte)(51)))));
			this._dividerPanel.Dock = System.Windows.Forms.DockStyle.Top;
			this._dividerPanel.Location = new System.Drawing.Point(0, 0);
			this._dividerPanel.Margin = new System.Windows.Forms.Padding(0);
			this._dividerPanel.Name = "_dividerPanel";
			this._dividerPanel.Size = new System.Drawing.Size(350, 1);
			this._dividerPanel.TabIndex = 6;
			// 
			// renameToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.renameToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.renameToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.renameToolStripMenuItem, "CollectionTab.BookMenu.Rename");
			this.renameToolStripMenuItem.Name = "renameToolStripMenuItem";
			this.renameToolStripMenuItem.Size = new System.Drawing.Size(242, 26);
			this.renameToolStripMenuItem.Text = "Rename";
			this.renameToolStripMenuItem.Click += new System.EventHandler(this.renameToolStripMenuItem_Click);
			// 
			// importContentFromSpreadsheetToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizationComment(this.importContentFromSpreadsheetToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizableToolTip(this.importContentFromSpreadsheetToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.importContentFromSpreadsheetToolStripMenuItem, "CollectionTab.BookMenu.ImportContentFromSpreadsheet");
			this.importContentFromSpreadsheetToolStripMenuItem.Name = "importContentFromSpreadsheetToolStripMenuItem";
			this.importContentFromSpreadsheetToolStripMenuItem.Text = "Import Content from Spreadsheet...";
			this.importContentFromSpreadsheetToolStripMenuItem.Size = new System.Drawing.Size(287, 26);
			this.importContentFromSpreadsheetToolStripMenuItem.Click += new System.EventHandler(this.importContentFromSpreadsheetToolStripMenuItem_Click);
			// 
			// LibraryListView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.Controls.Add(this.splitContainer1);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "LibraryListView.LibraryListView");
			this.Name = "LibraryListView";
			this.Size = new System.Drawing.Size(350, 562);
			this.BackColorChanged += new System.EventHandler(this.OnBackColorChanged);
			this._bookContextMenu.ResumeLayout(false);
			this._sourcePaneMenuStrip.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this._vernacularCollectionMenuStrip.ResumeLayout(false);
			this.splitContainer1.Panel1.ResumeLayout(false);
			this.splitContainer1.Panel2.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
			this.splitContainer1.ResumeLayout(false);
			this._primaryCollectionFlow.ResumeLayout(false);
			this._primaryCollectionFlow.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._menuTriangle)).EndInit();
			this._sourceBooksFlow.ResumeLayout(false);
			this._sourceBooksFlow.PerformLayout();
			this.ResumeLayout(false);

        }

        #endregion

		private System.Windows.Forms.ImageList _bookThumbnails;
        private System.Windows.Forms.ContextMenuStrip _bookContextMenu;
        private System.Windows.Forms.ToolStripMenuItem deleteMenuItem;
        private System.Windows.Forms.ToolTip toolTip1;
		private System.Windows.Forms.ToolStripMenuItem _updateThumbnailMenu;
		private System.Windows.Forms.ToolStripMenuItem _updateFrontMatterToolStripMenu;
		private System.Windows.Forms.ToolStripMenuItem _openFolderOnDisk;
		private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
		private Bloom.ToPalaso.BetterSplitContainer splitContainer1;
		private System.Windows.Forms.FlowLayoutPanel _primaryCollectionFlow;
		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Label label2;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Button button1;
		private System.Windows.Forms.Button button4;
		private System.Windows.Forms.Button button5;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Button button6;
		private System.Windows.Forms.FlowLayoutPanel _sourceBooksFlow;
		private System.Windows.Forms.Label label7;
		private System.Windows.Forms.Label pretendLabel;
		private System.Windows.Forms.Label label9;
		private System.Windows.Forms.Panel _dividerPanel;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private SIL.Windows.Forms.SettingProtection.SettingsProtectionHelper _settingsProtectionHelper;
		private System.Windows.Forms.ContextMenuStrip _sourcePaneMenuStrip;
		private System.Windows.Forms.ToolStripMenuItem toolStripMenuItem1;
		private System.Windows.Forms.ContextMenuStrip _vernacularCollectionMenuStrip;
		private System.Windows.Forms.ToolStripMenuItem _showHistoryMenu;
		private System.Windows.Forms.ToolStripMenuItem _showNotesMenu;
        private System.Windows.Forms.ToolStripMenuItem _doChecksAndUpdatesOfAllBooksToolStripMenuItem;
		private ToolStripMenuItem _exportToXMLForInDesignToolStripMenuItem;
		private ToolStripMenuItem _doChecksOfAllBooksToolStripMenuItem;
		private ToolStripMenuItem _rescueMissingImagesToolStripMenuItem;
		private ToolStripSeparator toolStripMenuItem3;
		private ToolStripMenuItem makeReaderTemplateBloomPackToolStripMenuItem;
		private ToolStripMenuItem advancedToolStripMenuItem;
		private ToolStripMenuItem exportToWordOrLibreOfficeToolStripMenuItem;
		private ToolStripMenuItem _makeBloomPackOfBookToolStripMenuItem;
		private PictureBox _menuTriangle;
		private ToolStripMenuItem openCreateCollectionToolStripMenuItem;
		private ToolStripMenuItem _copyBook;
		private ToolStripSeparator toolStripSeparator1;
		private ToolStripSeparator toolStripSeparator3;
		private ToolStripSeparator toolStripSeparator4;
		private ToolStripMenuItem _leveledReaderMenuItem;
		private ToolStripMenuItem _decodableReaderMenuItem;
		private ToolStripMenuItem SaveAsBloomToolStripMenuItem;
		private ToolStripMenuItem exportToSpreadsheetToolStripMenuItem;
		private ToolStripMenuItem renameToolStripMenuItem;
		private ToolStripMenuItem importContentFromSpreadsheetToolStripMenuItem;
	}
}
