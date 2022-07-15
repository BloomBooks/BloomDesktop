using Bloom.Publish.PDF;

namespace Bloom.Publish
{
    partial class PublishView
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

		private bool disposed = false;
        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
			if (disposed)
				return;
			disposed = true;
            if (disposing)
            {
	            components?.Dispose();

		        _publishApi?.Dispose();

				if (_htmlControl != null)
				{
					_htmlControl.Dispose();
					_htmlControl = null;
				}
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
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper1 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo superToolTipInfo1 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper2 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo superToolTipInfo2 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper3 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo superToolTipInfo3 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper4 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo superToolTipInfo4 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper5 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo superToolTipInfo5 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper6 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo superToolTipInfo6 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper7 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo superToolTipInfo7 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper8 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfoWrapper();
			SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo superToolTipInfo8 = new SIL.Windows.Forms.SuperToolTip.SuperToolTipInfo();
			this._loadTimer = new System.Windows.Forms.Timer(this.components);
			this._makePdfBackgroundWorker = new System.ComponentModel.BackgroundWorker();
			this._workingIndicator = new System.Windows.Forms.Panel();
			this._topBarPanel = new System.Windows.Forms.Panel();
			this._saveButton = new System.Windows.Forms.Button();
			this._printButton = new System.Windows.Forms.Button();
			this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
			this._contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
			this._openinBrowserMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._openPDF = new System.Windows.Forms.ToolStripMenuItem();
			this.exportAudioFiles1PerPageToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._bloomPUBRadio = new System.Windows.Forms.RadioButton();
			this._recordVideoRadio = new System.Windows.Forms.RadioButton();
			this._uploadRadio = new System.Windows.Forms.RadioButton();
			this._pdfPrintRadio = new System.Windows.Forms.RadioButton();
			this._menusToolStrip = new System.Windows.Forms.ToolStrip();
			this._pdfOptions = new System.Windows.Forms.ToolStripDropDownButton();
			this._bookletCoverRadio = new System.Windows.Forms.RadioButton();
			this.label1 = new System.Windows.Forms.Label();
			this._bookletBodyRadio = new System.Windows.Forms.RadioButton();
			this._simpleAllPagesRadio = new System.Windows.Forms.RadioButton();
			this._epubRadio = new System.Windows.Forms.RadioButton();
			this._noBookletsMessage = new System.Windows.Forms.Label();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._publishReqEntTitle = new System.Windows.Forms.Label();
			this._publishReqEntProblem = new System.Windows.Forms.Label();
			this._publishReqEntOptions = new System.Windows.Forms.Label();
			this._superToolTip = new SIL.Windows.Forms.SuperToolTip.SuperToolTip(this.components);
			this._publishRequiresEnterprisePanel = new System.Windows.Forms.Panel();
			this._publishReqEntOverlayPage = new System.Windows.Forms.Label();
			this._pdfViewer = new Bloom.Publish.PDF.PdfViewer();
			this._workingIndicator.SuspendLayout();
			this._topBarPanel.SuspendLayout();
			this.tableLayoutPanel1.SuspendLayout();
			this._contextMenuStrip.SuspendLayout();
			this._menusToolStrip.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this._publishRequiresEnterprisePanel.SuspendLayout();
			this.SuspendLayout();
			// 
			// _makePdfBackgroundWorker
			// 
			this._makePdfBackgroundWorker.WorkerReportsProgress = true;
			this._makePdfBackgroundWorker.WorkerSupportsCancellation = true;
			this._makePdfBackgroundWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(this._makePdfBackgroundWorker_DoWork);
			// 
			// _workingIndicator
			// 
			this._workingIndicator.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._workingIndicator.BackColor = System.Drawing.Color.White;
			this._workingIndicator.Controls.Add(this._topBarPanel);
			this._workingIndicator.Location = new System.Drawing.Point(114, 24);
			this._workingIndicator.Name = "_workingIndicator";
			this._workingIndicator.Size = new System.Drawing.Size(714, 653);
			this._workingIndicator.TabIndex = 8;
			// 
			// _topBarPanel
			// 
			this._topBarPanel.AutoSize = true;
			this._topBarPanel.Controls.Add(this._saveButton);
			this._topBarPanel.Controls.Add(this._printButton);
			this._topBarPanel.Location = new System.Drawing.Point(296, 320);
			this._topBarPanel.Name = "_topBarPanel";
			this._topBarPanel.Size = new System.Drawing.Size(327, 74);
			this._topBarPanel.TabIndex = 14;
			// 
			// _saveButton
			// 
			this._saveButton.AutoSize = true;
			this._saveButton.BackColor = System.Drawing.Color.Transparent;
			this._saveButton.FlatAppearance.BorderSize = 0;
			this._saveButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._saveButton.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._saveButton.ForeColor = System.Drawing.Color.Black;
			this._saveButton.Image = global::Bloom.Properties.Resources.Usb;
			this._L10NSharpExtender.SetLocalizableToolTip(this._saveButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._saveButton, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._saveButton, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._saveButton, "PublishView._saveButton");
			this._saveButton.Location = new System.Drawing.Point(139, 0);
			this._saveButton.Name = "_saveButton";
			this._saveButton.Size = new System.Drawing.Size(185, 71);
			this._saveButton.TabIndex = 15;
			this._saveButton.Text = "Save stub";
			this._saveButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._saveButton.UseVisualStyleBackColor = false;
			this._saveButton.Click += new System.EventHandler(this.OnSave_Click);
			// 
			// _printButton
			// 
			this._printButton.AutoSize = true;
			this._printButton.BackColor = System.Drawing.Color.Transparent;
			this._printButton.FlatAppearance.BorderSize = 0;
			this._printButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
			this._printButton.Font = new System.Drawing.Font("Segoe UI", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._printButton.ForeColor = System.Drawing.Color.Black;
			this._printButton.Image = global::Bloom.Properties.Resources.print;
			this._L10NSharpExtender.SetLocalizableToolTip(this._printButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._printButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._printButton, "PublishTab.PrintButton");
			this._printButton.Location = new System.Drawing.Point(0, 0);
			this._printButton.Name = "_printButton";
			this._printButton.Size = new System.Drawing.Size(160, 64);
			this._printButton.TabIndex = 14;
			this._printButton.Text = "&Print...";
			this._printButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
			this._printButton.UseVisualStyleBackColor = false;
			this._printButton.Click += new System.EventHandler(this.OnPrint_Click);
			// 
			// tableLayoutPanel1
			// 
			this.tableLayoutPanel1.AutoScroll = true;
			this.tableLayoutPanel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(192)))), ((int)(((byte)(255)))));
			this.tableLayoutPanel1.ColumnCount = 1;
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel1.ContextMenuStrip = this._contextMenuStrip;
			this.tableLayoutPanel1.Controls.Add(this._bloomPUBRadio, 0, 9);
			this.tableLayoutPanel1.Controls.Add(this._recordVideoRadio, 0, 10);
			this.tableLayoutPanel1.Controls.Add(this._uploadRadio, 0, 8);
			this.tableLayoutPanel1.Controls.Add(this._pdfPrintRadio, 0, 7);
			this.tableLayoutPanel1.Controls.Add(this._menusToolStrip, 0, 0);
			this.tableLayoutPanel1.Controls.Add(this._bookletCoverRadio, 0, 3);
			this.tableLayoutPanel1.Controls.Add(this.label1, 0, 5);
			this.tableLayoutPanel1.Controls.Add(this._bookletBodyRadio, 0, 4);
			this.tableLayoutPanel1.Controls.Add(this._simpleAllPagesRadio, 0, 1);
			this.tableLayoutPanel1.Controls.Add(this._epubRadio, 0, 10);
			this.tableLayoutPanel1.Controls.Add(this._noBookletsMessage, 0, 2);
			this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Left;
			this.tableLayoutPanel1.ForeColor = System.Drawing.Color.White;
			this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
			this.tableLayoutPanel1.Name = "tableLayoutPanel1";
			this.tableLayoutPanel1.RowCount = 11;
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.Size = new System.Drawing.Size(150, 677);
			// This line stops horizontal scrolling when vertical scrolling starts.
			// https://stackoverflow.com/questions/2197452/how-to-disable-horizontal-scrollbar-for-table-panel-in-winforms
			this.tableLayoutPanel1.Padding = new System.Windows.Forms.Padding(0, 0, System.Windows.Forms.SystemInformation.VerticalScrollBarWidth + 4, 0);
			this.tableLayoutPanel1.TabIndex = 10;
			// 
			// _contextMenuStrip
			// 
			this._contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._openinBrowserMenuItem,
            this._openPDF,
            this.exportAudioFiles1PerPageToolStripMenuItem});
			this._L10NSharpExtender.SetLocalizableToolTip(this._contextMenuStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._contextMenuStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._contextMenuStrip, L10NSharp.LocalizationPriority.InternalUseOnly);
			this._L10NSharpExtender.SetLocalizingId(this._contextMenuStrip, "_contextMenuStrip._contextMenuStrip");
			this._contextMenuStrip.Name = "_contextMenuStrip";
			this._contextMenuStrip.Size = new System.Drawing.Size(432, 70);
			// 
			// _openinBrowserMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._openinBrowserMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._openinBrowserMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._openinBrowserMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._openinBrowserMenuItem, "EditTab.BookContextMenu.openHtmlInBrowser");
			this._openinBrowserMenuItem.Name = "_openinBrowserMenuItem";
			this._openinBrowserMenuItem.Size = new System.Drawing.Size(431, 22);
			this._openinBrowserMenuItem.Text = "Open the HTML used to make this PDF, in Firefox (must be on path)";
			this._openinBrowserMenuItem.Click += new System.EventHandler(this._openinBrowserMenuItem_Click);
			// 
			// _openPDF
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._openPDF, null);
			this._L10NSharpExtender.SetLocalizationComment(this._openPDF, null);
			this._L10NSharpExtender.SetLocalizingId(this._openPDF, "PublishTab.OpenThePDFInTheSystemPDFViewer");
			this._openPDF.Name = "_openPDF";
			this._openPDF.Size = new System.Drawing.Size(431, 22);
			this._openPDF.Text = "Open the PDF in the default system PDF viewer";
			this._openPDF.Click += new System.EventHandler(this._openPDF_Click);
			// 
			// exportAudioFiles1PerPageToolStripMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this.exportAudioFiles1PerPageToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this.exportAudioFiles1PerPageToolStripMenuItem, null);
			this._L10NSharpExtender.SetLocalizingId(this.exportAudioFiles1PerPageToolStripMenuItem, ".exportAudioFiles1PerPageToolStripMenuItem");
			this.exportAudioFiles1PerPageToolStripMenuItem.Name = "exportAudioFiles1PerPageToolStripMenuItem";
			this.exportAudioFiles1PerPageToolStripMenuItem.Size = new System.Drawing.Size(431, 22);
			this.exportAudioFiles1PerPageToolStripMenuItem.Text = "Export audio files, 1 per page";
			this.exportAudioFiles1PerPageToolStripMenuItem.Click += new System.EventHandler(this.ExportAudioFiles1PerPageToolStripMenuItem_Click);
			// 
			// _bloomPUBRadio
			// 
			this._bloomPUBRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._bloomPUBRadio.Image = global::Bloom.Properties.Resources.BloomPUB;
			this._L10NSharpExtender.SetLocalizableToolTip(this._bloomPUBRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bloomPUBRadio, "This is the label under the BloomPUB icon on the left side of the Publish screen.");
			this._L10NSharpExtender.SetLocalizingId(this._bloomPUBRadio, "PublishTab.bloomPUBButton");
			this._bloomPUBRadio.Location = new System.Drawing.Point(3, 602);
			this._bloomPUBRadio.Name = "_bloomPUBRadio";
			this._bloomPUBRadio.Size = new System.Drawing.Size(105, 71);
			superToolTipInfo1.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(252)))), ((int)(((byte)(253)))), ((int)(((byte)(254)))));
			superToolTipInfo1.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(166)))), ((int)(((byte)(227)))), ((int)(((byte)(235)))));
			superToolTipInfo1.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(240)))), ((int)(((byte)(245)))));
			superToolTipInfo1.BodyText = "Make a BloomPUB for Bloom Reader or Reading App Builder.";
			superToolTipInfo1.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo1.ShowHeader = false;
			superToolTipInfoWrapper1.SuperToolTipInfo = superToolTipInfo1;
			superToolTipInfoWrapper1.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._bloomPUBRadio, superToolTipInfoWrapper1);
			this._bloomPUBRadio.TabIndex = 23;
			this._bloomPUBRadio.TabStop = true;
			this._bloomPUBRadio.Text = "BloomPUB";
			this._bloomPUBRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._bloomPUBRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._bloomPUBRadio.UseVisualStyleBackColor = true;
			this._bloomPUBRadio.CheckedChanged += new System.EventHandler(this.OnPublishRadioChanged);
			// 
			// _recordVideoRadio
			// 
			this._recordVideoRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._recordVideoRadio.Image = global::Bloom.Properties.Resources.publish_video;
			this._L10NSharpExtender.SetLocalizableToolTip(this._recordVideoRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._recordVideoRadio, null);
			this._L10NSharpExtender.SetLocalizingId(this._recordVideoRadio, "PublishTab.RecordVideoButton");
			this._recordVideoRadio.Location = new System.Drawing.Point(3, 679);
			this._recordVideoRadio.Name = "_recordVideoRadio";
			this._recordVideoRadio.Size = new System.Drawing.Size(105, 71);
			superToolTipInfo7.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(252)))), ((int)(((byte)(253)))), ((int)(((byte)(254)))));
			superToolTipInfo7.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(166)))), ((int)(((byte)(227)))), ((int)(((byte)(235)))));
			superToolTipInfo7.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(240)))), ((int)(((byte)(245)))));
			superToolTipInfo7.BodyText = "Make an audio or video file of the book being read.";
			superToolTipInfo7.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo7.ShowHeader = false;
			superToolTipInfoWrapper7.SuperToolTipInfo = superToolTipInfo7;
			superToolTipInfoWrapper7.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._recordVideoRadio, superToolTipInfoWrapper7);
			this._recordVideoRadio.TabIndex = 24;
			this._recordVideoRadio.TabStop = true;
			this._recordVideoRadio.Text = "Audio or Video";
			this._recordVideoRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._recordVideoRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._recordVideoRadio.UseVisualStyleBackColor = true;
			this._recordVideoRadio.CheckedChanged += new System.EventHandler(this.OnPublishRadioChanged);
			// 
			// _uploadRadio
			// 
			this._uploadRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._uploadRadio.Image = global::Bloom.Properties.Resources.upload;
			this._L10NSharpExtender.SetLocalizableToolTip(this._uploadRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uploadRadio, null);
			this._L10NSharpExtender.SetLocalizingId(this._uploadRadio, "PublishTab.ButtonThatShowsUploadForm");
			this._uploadRadio.Location = new System.Drawing.Point(3, 525);
			this._uploadRadio.Name = "_uploadRadio";
			this._uploadRadio.Size = new System.Drawing.Size(105, 89);
			superToolTipInfo2.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(252)))), ((int)(((byte)(253)))), ((int)(((byte)(254)))));
			superToolTipInfo2.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(166)))), ((int)(((byte)(227)))), ((int)(((byte)(235)))));
			superToolTipInfo2.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(240)))), ((int)(((byte)(245)))));
			superToolTipInfo2.BodyText = "Upload your book to BloomLibrary.org so that other people can read it, download i" +
	"t to their devices, and share it with other people.";
			superToolTipInfo2.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo2.ShowHeader = false;
			superToolTipInfoWrapper2.SuperToolTipInfo = superToolTipInfo2;
			superToolTipInfoWrapper2.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._uploadRadio, superToolTipInfoWrapper2);
			this._uploadRadio.TabIndex = 22;
			this._uploadRadio.TabStop = true;
			this._uploadRadio.Text = "Web";
			this._uploadRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._uploadRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._uploadRadio.UseVisualStyleBackColor = true;
			this._uploadRadio.CheckedChanged += new System.EventHandler(this.OnPublishRadioChanged);
			// 
			// _pdfPrintRadio
			// 
			this._pdfPrintRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._pdfPrintRadio.Image = global::Bloom.Properties.Resources.PdfPrint;
			this._L10NSharpExtender.SetLocalizableToolTip(this._pdfPrintRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._pdfPrintRadio,
				"Text under button to select Print and PDF preview");
			this._L10NSharpExtender.SetLocalizingId(this._pdfPrintRadio, "PublishTab.PdfPrint.Button");
			this._pdfPrintRadio.Location = new System.Drawing.Point(3, 448);
			this._pdfPrintRadio.Name = "_pdfPrintRadio";
			this._pdfPrintRadio.Size = new System.Drawing.Size(105, 89);
			superToolTipInfo8.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(252)))), ((int)(((byte)(253)))), ((int)(((byte)(254)))));
			superToolTipInfo8.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(166)))), ((int)(((byte)(227)))), ((int)(((byte)(235)))));
			superToolTipInfo8.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(240)))), ((int)(((byte)(245)))));
			superToolTipInfo8.BodyText = "Generate a PDF document and/or print your book.";
			superToolTipInfo8.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo8.ShowHeader = false;
			superToolTipInfoWrapper8.SuperToolTipInfo = superToolTipInfo8;
			superToolTipInfoWrapper8.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._pdfPrintRadio, superToolTipInfoWrapper8);
			this._pdfPrintRadio.TabIndex = 21;
			this._pdfPrintRadio.TabStop = true;
			this._pdfPrintRadio.Text = "PDF and Print";
			this._pdfPrintRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._pdfPrintRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._pdfPrintRadio.UseVisualStyleBackColor = true;
			this._pdfPrintRadio.CheckedChanged += new System.EventHandler(this.OnPublishRadioChanged);
			// 
			// _menusToolStrip
			// 
			this._menusToolStrip.AutoSize = false;
			this._menusToolStrip.BackColor = System.Drawing.Color.Aqua;
			this._menusToolStrip.CanOverflow = false;
			this._menusToolStrip.Dock = System.Windows.Forms.DockStyle.None;
			this._menusToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._pdfOptions});
			this._menusToolStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Flow;
			this._L10NSharpExtender.SetLocalizableToolTip(this._menusToolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._menusToolStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._menusToolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._menusToolStrip, "EditTab.MenusToolStrip");
			this._menusToolStrip.Location = new System.Drawing.Point(0, 0);
			this._menusToolStrip.Name = "_menusToolStrip";
			this._menusToolStrip.Size = new System.Drawing.Size(100, 24);
			this._menusToolStrip.TabIndex = 13;
			// 
			// _pdfOptions
			// 
			this._pdfOptions.BackColor = System.Drawing.Color.Magenta;
			this._pdfOptions.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._pdfOptions.ForeColor = System.Drawing.Color.White;
			this._pdfOptions.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._pdfOptions, "Choose print shop setup");
			this._L10NSharpExtender.SetLocalizationComment(this._pdfOptions, "menu header in the Publish tab");
			this._L10NSharpExtender.SetLocalizationPriority(this._pdfOptions, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._pdfOptions, "PublishTab.Options");
			this._pdfOptions.Name = "_pdfOptions";
			this._pdfOptions.Size = new System.Drawing.Size(86, 19);
			this._pdfOptions.Text = "PDF Options";
			this._pdfOptions.ToolTipText = "Choose print shop setup";
			// 
			// _bookletCoverRadio
			// 
			this._bookletCoverRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._bookletCoverRadio.Image = global::Bloom.Properties.Resources.coverOnly;
			this._L10NSharpExtender.SetLocalizableToolTip(this._bookletCoverRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bookletCoverRadio, null);
			this._L10NSharpExtender.SetLocalizingId(this._bookletCoverRadio, "PublishTab.CoverOnlyRadio");
			this._bookletCoverRadio.Location = new System.Drawing.Point(3, 169);
			this._bookletCoverRadio.Name = "_bookletCoverRadio";
			this._bookletCoverRadio.Size = new System.Drawing.Size(94, 88);
			superToolTipInfo3.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(252)))), ((int)(((byte)(253)))), ((int)(((byte)(254)))));
			superToolTipInfo3.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(166)))), ((int)(((byte)(227)))), ((int)(((byte)(235)))));
			superToolTipInfo3.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(240)))), ((int)(((byte)(245)))));
			superToolTipInfo3.BodyText = "Make a PDF of just the front and back (both sides), so you can  print on colored " +
    "paper.";
			superToolTipInfo3.HeaderText = "Cover";
			superToolTipInfo3.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo3.ShowHeader = false;
			superToolTipInfoWrapper3.SuperToolTipInfo = superToolTipInfo3;
			superToolTipInfoWrapper3.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._bookletCoverRadio, superToolTipInfoWrapper3);
			this._bookletCoverRadio.TabIndex = 8;
			this._bookletCoverRadio.TabStop = true;
			this._bookletCoverRadio.Text = "Booklet Cover";
			this._bookletCoverRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._bookletCoverRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._bookletCoverRadio.UseVisualStyleBackColor = true;
			this._bookletCoverRadio.CheckedChanged += new System.EventHandler(this.OnPublishRadioChanged);
			// 
			// label1
			// 
			this.label1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "label1");
			this.label1.Location = new System.Drawing.Point(3, 362);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(108, 1);
			this.label1.TabIndex = 17;
			// 
			// _bookletBodyRadio
			// 
			this._bookletBodyRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._bookletBodyRadio.Image = global::Bloom.Properties.Resources.insideBookletPages;
			this._L10NSharpExtender.SetLocalizableToolTip(this._bookletBodyRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bookletBodyRadio, null);
			this._L10NSharpExtender.SetLocalizingId(this._bookletBodyRadio, "PublishTab.BodyOnlyRadio");
			this._bookletBodyRadio.Location = new System.Drawing.Point(3, 263);
			this._bookletBodyRadio.Name = "_bookletBodyRadio";
			this._bookletBodyRadio.Size = new System.Drawing.Size(94, 96);
			superToolTipInfo4.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(252)))), ((int)(((byte)(253)))), ((int)(((byte)(254)))));
			superToolTipInfo4.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(166)))), ((int)(((byte)(227)))), ((int)(((byte)(235)))));
			superToolTipInfo4.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(240)))), ((int)(((byte)(245)))));
			superToolTipInfo4.BodyText = "Make a booklet from the inside pages of the book.\r\nPages will be laid out and reo" +
    "rdered so that when you fold it, you\'ll have a booklet.\r\n";
			superToolTipInfo4.HeaderText = "Booklet Inside Pages";
			superToolTipInfo4.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo4.ShowHeader = false;
			superToolTipInfoWrapper4.SuperToolTipInfo = superToolTipInfo4;
			superToolTipInfoWrapper4.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._bookletBodyRadio, superToolTipInfoWrapper4);
			this._bookletBodyRadio.TabIndex = 14;
			this._bookletBodyRadio.TabStop = true;
			this._bookletBodyRadio.Text = "Booklet Insides";
			this._bookletBodyRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._bookletBodyRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._bookletBodyRadio.UseVisualStyleBackColor = true;
			this._bookletBodyRadio.CheckedChanged += new System.EventHandler(this.OnPublishRadioChanged);
			// 
			// _simpleAllPagesRadio
			// 
			this._simpleAllPagesRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._simpleAllPagesRadio.Image = global::Bloom.Properties.Resources.simplePages;
			this._L10NSharpExtender.SetLocalizableToolTip(this._simpleAllPagesRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._simpleAllPagesRadio, "Instead of making a booklet, just make normal pages");
			this._L10NSharpExtender.SetLocalizingId(this._simpleAllPagesRadio, "PublishTab.OnePagePerPaperRadio");
			this._simpleAllPagesRadio.Location = new System.Drawing.Point(3, 27);
			this._simpleAllPagesRadio.Name = "_simpleAllPagesRadio";
			this._simpleAllPagesRadio.Size = new System.Drawing.Size(94, 97);
			superToolTipInfo5.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(252)))), ((int)(((byte)(253)))), ((int)(((byte)(254)))));
			superToolTipInfo5.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(166)))), ((int)(((byte)(227)))), ((int)(((byte)(235)))));
			superToolTipInfo5.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(240)))), ((int)(((byte)(245)))));
			superToolTipInfo5.BodyText = "Make a PDF of every page of the book, one page per piece of paper.";
			superToolTipInfo5.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo5.ShowHeader = false;
			superToolTipInfoWrapper5.SuperToolTipInfo = superToolTipInfo5;
			superToolTipInfoWrapper5.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._simpleAllPagesRadio, superToolTipInfoWrapper5);
			this._simpleAllPagesRadio.TabIndex = 10;
			this._simpleAllPagesRadio.TabStop = true;
			this._simpleAllPagesRadio.Text = "Simple";
			this._simpleAllPagesRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._simpleAllPagesRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._simpleAllPagesRadio.UseVisualStyleBackColor = true;
			this._simpleAllPagesRadio.CheckedChanged += new System.EventHandler(this.OnPublishRadioChanged);
			// 
			// _epubRadio
			// 
			this._epubRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._epubRadio.Image = global::Bloom.Properties.Resources.epubPublishButton;
			this._L10NSharpExtender.SetLocalizableToolTip(this._epubRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._epubRadio, null);
			this._L10NSharpExtender.SetLocalizingId(this._epubRadio, "PublishTab.EpubButton");
			this._epubRadio.Location = new System.Drawing.Point(3, 756);
			this._epubRadio.Name = "_epubRadio";
			this._epubRadio.Size = new System.Drawing.Size(105, 76);
			superToolTipInfo6.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(252)))), ((int)(((byte)(253)))), ((int)(((byte)(254)))));
			superToolTipInfo6.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(166)))), ((int)(((byte)(227)))), ((int)(((byte)(235)))));
			superToolTipInfo6.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(208)))), ((int)(((byte)(240)))), ((int)(((byte)(245)))));
			superToolTipInfo6.BodyText = "Make an ePUB (electronic book) out of this book, allowing it to be read on variou" +
    "s electronic reading devices.";
			superToolTipInfo6.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo6.ShowHeader = false;
			superToolTipInfoWrapper6.SuperToolTipInfo = superToolTipInfo6;
			superToolTipInfoWrapper6.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._epubRadio, superToolTipInfoWrapper6);
			this._epubRadio.TabIndex = 25;
			this._epubRadio.TabStop = true;
			this._epubRadio.Text = "ePUB";
			this._epubRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._epubRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._epubRadio.UseVisualStyleBackColor = true;
			this._epubRadio.CheckedChanged += new System.EventHandler(this.OnPublishRadioChanged);
			// 
			// _noBookletsMessage
			// 
			this._noBookletsMessage.AutoSize = true;
			this._L10NSharpExtender.SetLocalizableToolTip(this._noBookletsMessage, null);
			this._L10NSharpExtender.SetLocalizationComment(this._noBookletsMessage, "If a book has a paper size that is too big (like A4), Bloom does not know how to " +
        "make booklets with it. So the controls for booklets get disabled and this messag" +
        "e is shown.");
			this._L10NSharpExtender.SetLocalizingId(this._noBookletsMessage, "PublishTab.NoBookletsMessage");
			this._noBookletsMessage.Location = new System.Drawing.Point(3, 127);
			this._noBookletsMessage.Name = "_noBookletsMessage";
			this._noBookletsMessage.Size = new System.Drawing.Size(104, 39);
			this._noBookletsMessage.TabIndex = 24;
			this._noBookletsMessage.Text = "Bloom cannot make booklets using the current page size.";
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
			// 
			// _publishReqEntTitle
			// 
			this._publishReqEntTitle.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._publishReqEntTitle, null);
			this._L10NSharpExtender.SetLocalizationComment(this._publishReqEntTitle, null);
			this._L10NSharpExtender.SetLocalizingId(this._publishReqEntTitle, "Common.EnterpriseRequired");
			this._publishReqEntTitle.Location = new System.Drawing.Point(9, 13);
			this._publishReqEntTitle.Name = "_publishReqEntTitle";
			this._publishReqEntTitle.Size = new System.Drawing.Size(600, 40);
			this._publishReqEntTitle.TabIndex = 0;
			this._publishReqEntTitle.Text = "Enterprise Required";
			// 
			// _publishReqEntProblem
			// 
			this._publishReqEntProblem.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._publishReqEntProblem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._publishReqEntProblem, "{0} will be replaced with the book\'s title.");
			this._L10NSharpExtender.SetLocalizingId(this._publishReqEntProblem, "PublishTab.PublishRequiresEnterprise.ProblemExplanation");
			this._publishReqEntProblem.Location = new System.Drawing.Point(10, 49);
			this._publishReqEntProblem.Name = "_publishReqEntProblem";
			this._publishReqEntProblem.Size = new System.Drawing.Size(600, 35);
			this._publishReqEntProblem.TabIndex = 1;
			this._publishReqEntProblem.Text = "The book titled \'{0}\' adds new Overlay elements. Overlay elements are a Bloom Ent" +
    "erprise feature.";
			// 
			// _publishReqEntOptions
			// 
			this._publishReqEntOptions.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._publishReqEntOptions, null);
			this._L10NSharpExtender.SetLocalizationComment(this._publishReqEntOptions, null);
			this._L10NSharpExtender.SetLocalizingId(this._publishReqEntOptions, "PublishTab.PublishRequiresEnterprise.Options");
			this._publishReqEntOptions.Location = new System.Drawing.Point(13, 109);
			this._publishReqEntOptions.Name = "_publishReqEntOptions";
			this._publishReqEntOptions.Size = new System.Drawing.Size(600, 35);
			this._publishReqEntOptions.TabIndex = 2;
			this._publishReqEntOptions.Text = "In order to publish your book, you need to either activate Bloom Enterprise, or r" +
    "emove the Overlay elements from your book.";
			// 
			// _superToolTip
			// 
			this._superToolTip.FadingInterval = 10;
			// 
			// _publishRequiresEnterprisePanel
			// 
			this._publishRequiresEnterprisePanel.AutoSize = true;
			this._publishRequiresEnterprisePanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._publishRequiresEnterprisePanel.BackColor = System.Drawing.Color.White;
			this._publishRequiresEnterprisePanel.Controls.Add(this._publishReqEntOverlayPage);
			this._publishRequiresEnterprisePanel.Controls.Add(this._publishReqEntOptions);
			this._publishRequiresEnterprisePanel.Controls.Add(this._publishReqEntProblem);
			this._publishRequiresEnterprisePanel.Controls.Add(this._publishReqEntTitle);
			this._publishRequiresEnterprisePanel.Location = new System.Drawing.Point(30, 30);
			this._publishRequiresEnterprisePanel.Name = "_publishRequiresEnterprisePanel";
			this._publishRequiresEnterprisePanel.Size = new System.Drawing.Size(616, 204);
			this._publishRequiresEnterprisePanel.TabIndex = 17;
			this._publishRequiresEnterprisePanel.Visible = false;
			// 
			// _publishReqEntOverlayPage
			// 
			this._publishReqEntOverlayPage.Font = new System.Drawing.Font("Microsoft Sans Serif", 10F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._publishReqEntOverlayPage, null);
			this._L10NSharpExtender.SetLocalizationComment(this._publishReqEntOverlayPage, "The {0} will be replaced by a page number.");
			this._L10NSharpExtender.SetLocalizingId(this._publishReqEntOverlayPage, "PublishTab.PublishRequiresEnterprise.FirstOverlayPage");
			this._publishReqEntOverlayPage.Location = new System.Drawing.Point(13, 169);
			this._publishReqEntOverlayPage.Name = "_publishReqEntOverlayPage";
			this._publishReqEntOverlayPage.Size = new System.Drawing.Size(600, 35);
			this._publishReqEntOverlayPage.TabIndex = 3;
			this._publishReqEntOverlayPage.Text = "Page {0} is the first page that uses Overlay elements.";
			// 
			// _pdfViewer
			// 
			this._pdfViewer.BackColor = System.Drawing.Color.White;
			this._pdfViewer.Dock = System.Windows.Forms.DockStyle.Fill;
			this._L10NSharpExtender.SetLocalizableToolTip(this._pdfViewer, null);
			this._L10NSharpExtender.SetLocalizationComment(this._pdfViewer, null);
			this._L10NSharpExtender.SetLocalizingId(this._pdfViewer, "PdfViewer.PdfViewer");
			this._pdfViewer.Location = new System.Drawing.Point(114, 0);
			this._pdfViewer.Name = "_pdfViewer";
			this._pdfViewer.Size = new System.Drawing.Size(719, 677);
			this._pdfViewer.TabIndex = 16;
			// 
			// PublishView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.Controls.Add(this._publishRequiresEnterprisePanel);
			this.Controls.Add(this._pdfViewer);
			this.Controls.Add(this.tableLayoutPanel1);
			this.Controls.Add(this._workingIndicator);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "PublishView.PublishView");
			this.Name = "PublishView";
			this.Size = new System.Drawing.Size(833, 677);
			this._workingIndicator.ResumeLayout(false);
			this._workingIndicator.PerformLayout();
			this._topBarPanel.ResumeLayout(false);
			this._topBarPanel.PerformLayout();
			this.tableLayoutPanel1.ResumeLayout(false);
			this.tableLayoutPanel1.PerformLayout();
			this._contextMenuStrip.ResumeLayout(false);
			this._menusToolStrip.ResumeLayout(false);
			this._menusToolStrip.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this._publishRequiresEnterprisePanel.ResumeLayout(false);
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		private System.Windows.Forms.Timer _loadTimer;

		private System.ComponentModel.BackgroundWorker _makePdfBackgroundWorker;
		private System.Windows.Forms.Panel _workingIndicator;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
		private System.Windows.Forms.Panel _topBarPanel;
		private System.Windows.Forms.Button _saveButton;
		private System.Windows.Forms.Button _printButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private PdfViewer _pdfViewer;
		private System.Windows.Forms.ContextMenuStrip _contextMenuStrip;
		private System.Windows.Forms.ToolStripMenuItem _openinBrowserMenuItem;
		private System.Windows.Forms.RadioButton _simpleAllPagesRadio;
		private System.Windows.Forms.RadioButton _bookletCoverRadio;
		private System.Windows.Forms.RadioButton _bookletBodyRadio;
		private System.Windows.Forms.ToolStrip _menusToolStrip;
		private System.Windows.Forms.ToolStripDropDownButton _pdfOptions;
		private SIL.Windows.Forms.SuperToolTip.SuperToolTip _superToolTip;
		private System.Windows.Forms.ToolStripMenuItem _openPDF;
        private System.Windows.Forms.Label label1;
		private System.Windows.Forms.RadioButton _bloomPUBRadio;
		private System.Windows.Forms.RadioButton _recordVideoRadio;
		private System.Windows.Forms.RadioButton _pdfPrintRadio;
		private System.Windows.Forms.RadioButton _uploadRadio;
		private System.Windows.Forms.RadioButton _epubRadio;
		private System.Windows.Forms.Label _noBookletsMessage;
		private System.Windows.Forms.ToolStripMenuItem exportAudioFiles1PerPageToolStripMenuItem;
		private System.Windows.Forms.Panel _publishRequiresEnterprisePanel;
		private System.Windows.Forms.Label _publishReqEntTitle;
		private System.Windows.Forms.Label _publishReqEntOptions;
		private System.Windows.Forms.Label _publishReqEntProblem;
		private System.Windows.Forms.Label _publishReqEntOverlayPage;
	}
}
