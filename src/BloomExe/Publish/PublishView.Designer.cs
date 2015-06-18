namespace Bloom.Publish
{
    partial class PublishView
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
			Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper5 = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfoWrapper();
			Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfo superToolTipInfo5 = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfo();
			Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper6 = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfoWrapper();
			Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfo superToolTipInfo6 = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfo();
			Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper7 = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfoWrapper();
			Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfo superToolTipInfo7 = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfo();
			Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfoWrapper superToolTipInfoWrapper8 = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfoWrapper();
			Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfo superToolTipInfo8 = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTipInfo();
			this._loadTimer = new System.Windows.Forms.Timer(this.components);
			this._makePdfBackgroundWorker = new System.ComponentModel.BackgroundWorker();
			this._workingIndicator = new System.Windows.Forms.Panel();
			this._topBarPanel = new System.Windows.Forms.Panel();
			this._saveButton = new System.Windows.Forms.Button();
			this._printButton = new System.Windows.Forms.Button();
			this._epubButton = new System.Windows.Forms.Button();
			this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
			this._contextMenuStrip = new System.Windows.Forms.ContextMenuStrip(this.components);
			this._openinBrowserMenuItem = new System.Windows.Forms.ToolStripMenuItem();
			this._openPDF = new System.Windows.Forms.ToolStripMenuItem();
			this._menusToolStrip = new System.Windows.Forms.ToolStrip();
			this._layoutChoices = new System.Windows.Forms.ToolStripDropDownButton();
			this._bookletCoverRadio = new System.Windows.Forms.RadioButton();
			this.label1 = new System.Windows.Forms.Label();
			this._uploadRadio = new System.Windows.Forms.RadioButton();
			this._showCropMarks = new System.Windows.Forms.CheckBox();
			this._bookletBodyRadio = new System.Windows.Forms.RadioButton();
			this._simpleAllPagesRadio = new System.Windows.Forms.RadioButton();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			this._pdfViewer = new Bloom.Publish.PdfViewer();
			this._superToolTip = new Palaso.UI.WindowsForms.SuperToolTip.SuperToolTip(this.components);
			this._workingIndicator.SuspendLayout();
			this._topBarPanel.SuspendLayout();
			this.tableLayoutPanel1.SuspendLayout();
			this._contextMenuStrip.SuspendLayout();
			this._menusToolStrip.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _makePdfBackgroundWorker
			// 
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
			this._workingIndicator.Size = new System.Drawing.Size(714, 516);
			this._workingIndicator.TabIndex = 8;
			// 
			// _topBarPanel
			// 
			this._topBarPanel.Controls.Add(this._saveButton);
			this._topBarPanel.Controls.Add(this._printButton);
			this._topBarPanel.Controls.Add(this._epubButton);
			this._topBarPanel.Location = new System.Drawing.Point(296, 320);
			this._topBarPanel.Name = "_topBarPanel";
			this._topBarPanel.Size = new System.Drawing.Size(527, 70);
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
			this._L10NSharpExtender.SetLocalizingId(this._saveButton, "PublishTab.SaveButton");
			this._saveButton.Location = new System.Drawing.Point(139, 0);
			this._saveButton.Name = "_saveButton";
			this._saveButton.Size = new System.Drawing.Size(185, 71);
			this._saveButton.TabIndex = 15;
			this._saveButton.Text = "&Save PDF...";
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
			// _epubButton
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._epubButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._epubButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._epubButton, "button1");
			this._epubButton.Location = new System.Drawing.Point(327, 0);
			this._epubButton.Name = "_epubButton";
			this._epubButton.Size = new System.Drawing.Size(191, 23);
			this._epubButton.TabIndex = 15;
			this._epubButton.Text = "Save as Epub";
			this._epubButton.UseVisualStyleBackColor = true;
			this._epubButton.Click += new System.EventHandler(this._epubButton_Click);
			// 
			// tableLayoutPanel1
			// 
			this.tableLayoutPanel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.tableLayoutPanel1.ColumnCount = 1;
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel1.ContextMenuStrip = this._contextMenuStrip;
			this.tableLayoutPanel1.Controls.Add(this._menusToolStrip, 0, 0);
			this.tableLayoutPanel1.Controls.Add(this._bookletCoverRadio, 0, 2);
			this.tableLayoutPanel1.Controls.Add(this.label1, 0, 4);
			this.tableLayoutPanel1.Controls.Add(this._uploadRadio, 0, 5);
			this.tableLayoutPanel1.Controls.Add(this._showCropMarks, 0, 6);
			this.tableLayoutPanel1.Controls.Add(this._bookletBodyRadio, 0, 3);
			this.tableLayoutPanel1.Controls.Add(this._simpleAllPagesRadio, 0, 1);
			this.tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Left;
			this.tableLayoutPanel1.ForeColor = System.Drawing.Color.White;
			this.tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
			this.tableLayoutPanel1.Name = "tableLayoutPanel1";
			this.tableLayoutPanel1.RowCount = 7;
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.Size = new System.Drawing.Size(114, 540);
			this.tableLayoutPanel1.TabIndex = 10;
			this.tableLayoutPanel1.Paint += new System.Windows.Forms.PaintEventHandler(this.tableLayoutPanel1_Paint);
			// 
			// _contextMenuStrip
			// 
			this._contextMenuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._openinBrowserMenuItem,
            this._openPDF});
			this._L10NSharpExtender.SetLocalizableToolTip(this._contextMenuStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._contextMenuStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._contextMenuStrip, L10NSharp.LocalizationPriority.InternalUseOnly);
			this._L10NSharpExtender.SetLocalizingId(this._contextMenuStrip, "_contextMenuStrip._contextMenuStrip");
			this._contextMenuStrip.Name = "_contextMenuStrip";
			this._contextMenuStrip.Size = new System.Drawing.Size(426, 48);
			// 
			// _openinBrowserMenuItem
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._openinBrowserMenuItem, null);
			this._L10NSharpExtender.SetLocalizationComment(this._openinBrowserMenuItem, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._openinBrowserMenuItem, L10NSharp.LocalizationPriority.Low);
			this._L10NSharpExtender.SetLocalizingId(this._openinBrowserMenuItem, "EditTab.BookContextMenu.openHtmlInBrowser");
			this._openinBrowserMenuItem.Name = "_openinBrowserMenuItem";
			this._openinBrowserMenuItem.Size = new System.Drawing.Size(425, 22);
			this._openinBrowserMenuItem.Text = "Open the Html used to make this PDF, in Firefox (must be on path)";
			this._openinBrowserMenuItem.ToolTipText = "Will show in chrome rather than firefox because it is closest to the html engine " +
    "the htmltopdf engine uses.";
			this._openinBrowserMenuItem.Click += new System.EventHandler(this._openinBrowserMenuItem_Click);
			// 
			// _openPDF
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._openPDF, null);
			this._L10NSharpExtender.SetLocalizationComment(this._openPDF, null);
			this._L10NSharpExtender.SetLocalizingId(this._openPDF, "PublishTab.OpenThePDFInTheSystemPDFViewer");
			this._openPDF.Name = "_openPDF";
			this._openPDF.Size = new System.Drawing.Size(425, 22);
			this._openPDF.Text = "Open the PDF in the default system pdf viewer";
			this._openPDF.Click += new System.EventHandler(this._openPDF_Click);
			// 
			// _menusToolStrip
			// 
			this._menusToolStrip.AutoSize = false;
			this._menusToolStrip.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this._menusToolStrip.CanOverflow = false;
			this._menusToolStrip.Dock = System.Windows.Forms.DockStyle.None;
			this._menusToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
			this._menusToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._layoutChoices});
			this._menusToolStrip.LayoutStyle = System.Windows.Forms.ToolStripLayoutStyle.Flow;
			this._L10NSharpExtender.SetLocalizableToolTip(this._menusToolStrip, null);
			this._L10NSharpExtender.SetLocalizationComment(this._menusToolStrip, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._menusToolStrip, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._menusToolStrip, "EditTab._menusToolStrip");
			this._menusToolStrip.Location = new System.Drawing.Point(0, 0);
			this._menusToolStrip.Name = "_menusToolStrip";
			this._menusToolStrip.Size = new System.Drawing.Size(100, 24);
			this._menusToolStrip.TabIndex = 13;
			this._menusToolStrip.Text = "toolStrip1";
			// 
			// _layoutChoices
			// 
			this._layoutChoices.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
			this._layoutChoices.ForeColor = System.Drawing.Color.White;
			this._layoutChoices.ImageTransparentColor = System.Drawing.Color.Magenta;
			this._L10NSharpExtender.SetLocalizableToolTip(this._layoutChoices, null);
			this._L10NSharpExtender.SetLocalizationComment(this._layoutChoices, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._layoutChoices, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._layoutChoices, "._layoutChoices");
			this._layoutChoices.Name = "_layoutChoices";
			this._layoutChoices.Size = new System.Drawing.Size(85, 19);
			this._layoutChoices.Text = "Page Layout";
			this._layoutChoices.ToolTipText = "Choose a page size and orientation";
			// 
			// _bookletCoverRadio
			// 
			this._bookletCoverRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._bookletCoverRadio.Image = global::Bloom.Properties.Resources.coverOnly;
			this._L10NSharpExtender.SetLocalizableToolTip(this._bookletCoverRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bookletCoverRadio, null);
			this._L10NSharpExtender.SetLocalizingId(this._bookletCoverRadio, "PublishTab.CoverOnlyRadio");
			this._bookletCoverRadio.Location = new System.Drawing.Point(3, 130);
			this._bookletCoverRadio.Name = "_bookletCoverRadio";
			this._bookletCoverRadio.Size = new System.Drawing.Size(94, 88);
			superToolTipInfo5.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
			superToolTipInfo5.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(218)))), ((int)(((byte)(239)))));
			superToolTipInfo5.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(246)))), ((int)(((byte)(251)))));
			superToolTipInfo5.BodyText = "Make a PDF of just the front and back (both sides), so you can  print on colored " +
    "paper.";
			superToolTipInfo5.HeaderText = "Cover";
			superToolTipInfo5.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo5.ShowHeader = false;
			superToolTipInfoWrapper5.SuperToolTipInfo = superToolTipInfo5;
			superToolTipInfoWrapper5.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._bookletCoverRadio, superToolTipInfoWrapper5);
			this._bookletCoverRadio.TabIndex = 8;
			this._bookletCoverRadio.TabStop = true;
			this._bookletCoverRadio.Text = "Booklet Cover";
			this._bookletCoverRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._bookletCoverRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._bookletCoverRadio.UseVisualStyleBackColor = true;
			this._bookletCoverRadio.CheckedChanged += new System.EventHandler(this.OnBookletRadioChanged);
			// 
			// label1
			// 
			this.label1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this._L10NSharpExtender.SetLocalizableToolTip(this.label1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.label1, null);
			this._L10NSharpExtender.SetLocalizingId(this.label1, "label1");
			this.label1.Location = new System.Drawing.Point(3, 323);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(108, 1);
			this.label1.TabIndex = 17;
			// 
			// _uploadRadio
			// 
			this._uploadRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._uploadRadio.Image = global::Bloom.Properties.Resources.upload;
			this._L10NSharpExtender.SetLocalizableToolTip(this._uploadRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._uploadRadio, null);
			this._L10NSharpExtender.SetLocalizingId(this._uploadRadio, "PublishTab.ButtonThatShowsUploadForm");
			this._uploadRadio.Location = new System.Drawing.Point(3, 327);
			this._uploadRadio.Name = "_uploadRadio";
			this._uploadRadio.Size = new System.Drawing.Size(105, 82);
			superToolTipInfo6.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
			superToolTipInfo6.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(218)))), ((int)(((byte)(239)))));
			superToolTipInfo6.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(246)))), ((int)(((byte)(251)))));
			superToolTipInfo6.BodyText = "Upload to BloomLibrary.org, where others can download and localize into their own" +
    " language.";
			superToolTipInfo6.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo6.ShowHeader = false;
			superToolTipInfoWrapper6.SuperToolTipInfo = superToolTipInfo6;
			superToolTipInfoWrapper6.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._uploadRadio, superToolTipInfoWrapper6);
			this._uploadRadio.TabIndex = 16;
			this._uploadRadio.TabStop = true;
			this._uploadRadio.Text = "Upload";
			this._uploadRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._uploadRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._uploadRadio.UseVisualStyleBackColor = true;
			this._uploadRadio.CheckedChanged += new System.EventHandler(this.OnBookletRadioChanged);
			// 
			// _showCropMarks
			// 
			this._showCropMarks.AutoSize = true;
			this._showCropMarks.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._showCropMarks.Image = global::Bloom.Properties.Resources.cropMarks;
			this._L10NSharpExtender.SetLocalizableToolTip(this._showCropMarks, null);
			this._L10NSharpExtender.SetLocalizationComment(this._showCropMarks, null);
			this._L10NSharpExtender.SetLocalizingId(this._showCropMarks, "PublishTab.ShowCropMarks");
			this._showCropMarks.Location = new System.Drawing.Point(3, 427);
			this._showCropMarks.Margin = new System.Windows.Forms.Padding(3, 15, 3, 3);
			this._showCropMarks.Name = "_showCropMarks";
			this._showCropMarks.Size = new System.Drawing.Size(85, 62);
			this._showCropMarks.TabIndex = 15;
			this._showCropMarks.Text = "Crop Marks";
			this._showCropMarks.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._showCropMarks.UseVisualStyleBackColor = true;
			this._showCropMarks.Visible = false;
			this._showCropMarks.CheckedChanged += new System.EventHandler(this.OnShowCropMarks_CheckedChanged);
			// 
			// _bookletBodyRadio
			// 
			this._bookletBodyRadio.Font = new System.Drawing.Font("Segoe UI", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._bookletBodyRadio.Image = global::Bloom.Properties.Resources.insideBookletPages;
			this._L10NSharpExtender.SetLocalizableToolTip(this._bookletBodyRadio, null);
			this._L10NSharpExtender.SetLocalizationComment(this._bookletBodyRadio, null);
			this._L10NSharpExtender.SetLocalizingId(this._bookletBodyRadio, "PublishTab.BodyOnlyRadio");
			this._bookletBodyRadio.Location = new System.Drawing.Point(3, 224);
			this._bookletBodyRadio.Name = "_bookletBodyRadio";
			this._bookletBodyRadio.Size = new System.Drawing.Size(94, 96);
			superToolTipInfo7.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
			superToolTipInfo7.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(218)))), ((int)(((byte)(239)))));
			superToolTipInfo7.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(246)))), ((int)(((byte)(251)))));
			superToolTipInfo7.BodyText = "Make a booklet from the inside pages of the book.\r Pages will be laid out and reo" +
    "rdered so that when you fold it, you\'ll have a booklet.\r\n";
			superToolTipInfo7.HeaderText = "Booklet Inside Pages";
			superToolTipInfo7.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo7.ShowHeader = false;
			superToolTipInfoWrapper7.SuperToolTipInfo = superToolTipInfo7;
			superToolTipInfoWrapper7.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._bookletBodyRadio, superToolTipInfoWrapper7);
			this._bookletBodyRadio.TabIndex = 14;
			this._bookletBodyRadio.TabStop = true;
			this._bookletBodyRadio.Text = "Booklet Insides";
			this._bookletBodyRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._bookletBodyRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._bookletBodyRadio.UseVisualStyleBackColor = true;
			this._bookletBodyRadio.CheckedChanged += new System.EventHandler(this.OnBookletRadioChanged);
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
			superToolTipInfo8.BackgroundGradientBegin = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(255)))), ((int)(((byte)(255)))));
			superToolTipInfo8.BackgroundGradientEnd = System.Drawing.Color.FromArgb(((int)(((byte)(202)))), ((int)(((byte)(218)))), ((int)(((byte)(239)))));
			superToolTipInfo8.BackgroundGradientMiddle = System.Drawing.Color.FromArgb(((int)(((byte)(242)))), ((int)(((byte)(246)))), ((int)(((byte)(251)))));
			superToolTipInfo8.BodyText = "Make a PDF of every page of the book, one page per piece of paper.";
			superToolTipInfo8.OffsetForWhereToDisplay = new System.Drawing.Point(120, 0);
			superToolTipInfo8.ShowHeader = false;
			superToolTipInfoWrapper8.SuperToolTipInfo = superToolTipInfo8;
			superToolTipInfoWrapper8.UseSuperToolTip = true;
			this._superToolTip.SetSuperStuff(this._simpleAllPagesRadio, superToolTipInfoWrapper8);
			this._simpleAllPagesRadio.TabIndex = 10;
			this._simpleAllPagesRadio.TabStop = true;
			this._simpleAllPagesRadio.Text = "Simple";
			this._simpleAllPagesRadio.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			this._simpleAllPagesRadio.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._simpleAllPagesRadio.UseVisualStyleBackColor = true;
			this._simpleAllPagesRadio.CheckedChanged += new System.EventHandler(this.OnBookletRadioChanged);
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = null;
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
			this._pdfViewer.Size = new System.Drawing.Size(719, 540);
			this._pdfViewer.TabIndex = 16;
			// 
			// _superToolTip
			// 
			this._superToolTip.FadingInterval = 10;
			// 
			// PublishView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
			this.Controls.Add(this._pdfViewer);
			this.Controls.Add(this.tableLayoutPanel1);
			this.Controls.Add(this._workingIndicator);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "PublishView.PublishView");
			this.Name = "PublishView";
			this.Size = new System.Drawing.Size(833, 540);
			this._workingIndicator.ResumeLayout(false);
			this._topBarPanel.ResumeLayout(false);
			this._topBarPanel.PerformLayout();
			this.tableLayoutPanel1.ResumeLayout(false);
			this.tableLayoutPanel1.PerformLayout();
			this._contextMenuStrip.ResumeLayout(false);
			this._menusToolStrip.ResumeLayout(false);
			this._menusToolStrip.PerformLayout();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

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
		private System.Windows.Forms.ToolStripDropDownButton _layoutChoices;
		private Palaso.UI.WindowsForms.SuperToolTip.SuperToolTip _superToolTip;
		private System.Windows.Forms.CheckBox _showCropMarks;
		private System.Windows.Forms.RadioButton _uploadRadio;
		private System.Windows.Forms.ToolStripMenuItem _openPDF;
        private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Button _epubButton;
    }
}
