namespace Bloom.Edit
{
    partial class EditingView
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(EditingView));
            this._splitContainer1 = new System.Windows.Forms.SplitContainer();
            this._splitContainer2 = new System.Windows.Forms.SplitContainer();
            this.toolStrip1 = new System.Windows.Forms.ToolStrip();
            this._cutButton = new System.Windows.Forms.ToolStripButton();
            this._copyButton = new System.Windows.Forms.ToolStripButton();
            this._pasteButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this._undoButton = new System.Windows.Forms.ToolStripButton();
            this._deletePageButton = new System.Windows.Forms.ToolStripButton();
            this._splitTemplateAndSource = new System.Windows.Forms.SplitContainer();
            this._editButtonsUpdateTimer = new System.Windows.Forms.Timer(this.components);
            this._browser1 = new Bloom.Browser();
            this._translationSourcesControl = new Bloom.Edit.SourceTextControl();
            this._splitContainer1.Panel2.SuspendLayout();
            this._splitContainer1.SuspendLayout();
            this._splitContainer2.Panel1.SuspendLayout();
            this._splitContainer2.Panel2.SuspendLayout();
            this._splitContainer2.SuspendLayout();
            this.toolStrip1.SuspendLayout();
            this._splitTemplateAndSource.Panel2.SuspendLayout();
            this._splitTemplateAndSource.SuspendLayout();
            this.SuspendLayout();
            // 
            // _splitContainer1
            // 
            this._splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this._splitContainer1.Location = new System.Drawing.Point(0, 0);
            this._splitContainer1.Name = "_splitContainer1";
            // 
            // _splitContainer1.Panel1
            // 
            this._splitContainer1.Panel1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(63)))), ((int)(((byte)(64)))));
            // 
            // _splitContainer1.Panel2
            // 
            this._splitContainer1.Panel2.Controls.Add(this._splitContainer2);
            this._splitContainer1.Size = new System.Drawing.Size(900, 600);
            this._splitContainer1.SplitterDistance = 212;
            this._splitContainer1.SplitterWidth = 1;
            this._splitContainer1.TabIndex = 0;
            // 
            // _splitContainer2
            // 
            this._splitContainer2.Dock = System.Windows.Forms.DockStyle.Fill;
            this._splitContainer2.Location = new System.Drawing.Point(0, 0);
            this._splitContainer2.Name = "_splitContainer2";
            // 
            // _splitContainer2.Panel1
            // 
            this._splitContainer2.Panel1.Controls.Add(this.toolStrip1);
            this._splitContainer2.Panel1.Controls.Add(this._browser1);
            // 
            // _splitContainer2.Panel2
            // 
            this._splitContainer2.Panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(17)))), ((int)(((byte)(63)))), ((int)(((byte)(64)))));
            this._splitContainer2.Panel2.Controls.Add(this._splitTemplateAndSource);
            this._splitContainer2.Size = new System.Drawing.Size(687, 600);
            this._splitContainer2.SplitterDistance = 564;
            this._splitContainer2.SplitterWidth = 1;
            this._splitContainer2.TabIndex = 0;
            // 
            // toolStrip1
            // 
            this.toolStrip1.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.toolStrip1.CanOverflow = false;
            this.toolStrip1.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.toolStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this._cutButton,
            this._copyButton,
            this._pasteButton,
            this.toolStripSeparator1,
            this._undoButton,
            this._deletePageButton});
            this.toolStrip1.Location = new System.Drawing.Point(0, 0);
            this.toolStrip1.Name = "toolStrip1";
            this.toolStrip1.Size = new System.Drawing.Size(564, 25);
            this.toolStrip1.TabIndex = 2;
            this.toolStrip1.Text = "toolStrip1";
            // 
            // _cutButton
            // 
            this._cutButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this._cutButton.Image = ((System.Drawing.Image)(resources.GetObject("_cutButton.Image")));
            this._cutButton.ImageTransparentColor = System.Drawing.Color.Transparent;
            this._cutButton.Name = "_cutButton";
            this._cutButton.Size = new System.Drawing.Size(23, 22);
            this._cutButton.Text = "Cut";
            this._cutButton.Click += new System.EventHandler(this._cutButton_Click);
            // 
            // _copyButton
            // 
            this._copyButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this._copyButton.Image = ((System.Drawing.Image)(resources.GetObject("_copyButton.Image")));
            this._copyButton.ImageTransparentColor = System.Drawing.Color.Transparent;
            this._copyButton.Name = "_copyButton";
            this._copyButton.Size = new System.Drawing.Size(23, 22);
            this._copyButton.Text = "Copy";
            this._copyButton.Click += new System.EventHandler(this._copyButton_Click);
            // 
            // _pasteButton
            // 
            this._pasteButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this._pasteButton.Image = ((System.Drawing.Image)(resources.GetObject("_pasteButton.Image")));
            this._pasteButton.ImageTransparentColor = System.Drawing.Color.Transparent;
            this._pasteButton.Name = "_pasteButton";
            this._pasteButton.Size = new System.Drawing.Size(23, 22);
            this._pasteButton.Text = "Paste";
            this._pasteButton.Click += new System.EventHandler(this._pasteButton_Click);
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // _undoButton
            // 
            this._undoButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this._undoButton.Image = ((System.Drawing.Image)(resources.GetObject("_undoButton.Image")));
            this._undoButton.ImageTransparentColor = System.Drawing.Color.Transparent;
            this._undoButton.Name = "_undoButton";
            this._undoButton.Size = new System.Drawing.Size(23, 22);
            this._undoButton.Text = "Undo";
            this._undoButton.Click += new System.EventHandler(this._undoButton_Click);
            // 
            // _deletePageButton
            // 
            this._deletePageButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this._deletePageButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this._deletePageButton.Image = global::Bloom.Properties.Resources.DeleteMessageBoxButtonImage;
            this._deletePageButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this._deletePageButton.Name = "_deletePageButton";
            this._deletePageButton.Size = new System.Drawing.Size(23, 22);
            this._deletePageButton.Text = "Delete Page";
            this._deletePageButton.Click += new System.EventHandler(this._deletePageButton_Click);
            // 
            // _splitTemplateAndSource
            // 
            this._splitTemplateAndSource.Dock = System.Windows.Forms.DockStyle.Fill;
            this._splitTemplateAndSource.Location = new System.Drawing.Point(0, 0);
            this._splitTemplateAndSource.Name = "_splitTemplateAndSource";
            this._splitTemplateAndSource.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // _splitTemplateAndSource.Panel2
            // 
            this._splitTemplateAndSource.Panel2.Controls.Add(this._translationSourcesControl);
            this._splitTemplateAndSource.Size = new System.Drawing.Size(122, 600);
            this._splitTemplateAndSource.SplitterDistance = 247;
            this._splitTemplateAndSource.TabIndex = 0;
            // 
            // _editButtonsUpdateTimer
            // 
            this._editButtonsUpdateTimer.Enabled = true;
            this._editButtonsUpdateTimer.Tick += new System.EventHandler(this._editButtonsUpdateTimer_Tick);
            // 
            // _browser1
            // 
            this._browser1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this._browser1.BackColor = System.Drawing.Color.DarkGray;
            this._browser1.Location = new System.Drawing.Point(0, 25);
            this._browser1.Name = "_browser1";
            this._browser1.Size = new System.Drawing.Size(564, 578);
            this._browser1.TabIndex = 1;
            this._browser1.OnBrowserClick += new System.EventHandler(this._browser1_OnBrowserClick);
            this._browser1.Validating += new System.ComponentModel.CancelEventHandler(this._browser1_Validating);
            // 
            // _translationSourcesControl
            // 
            this._translationSourcesControl.Dock = System.Windows.Forms.DockStyle.Fill;
            this._translationSourcesControl.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._translationSourcesControl.Location = new System.Drawing.Point(0, 0);
            this._translationSourcesControl.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this._translationSourcesControl.Name = "_translationSourcesControl";
            this._translationSourcesControl.Size = new System.Drawing.Size(122, 349);
            this._translationSourcesControl.TabIndex = 3;
            // 
            // EditingView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._splitContainer1);
            this.Name = "EditingView";
            this.Size = new System.Drawing.Size(900, 600);
            this._splitContainer1.Panel2.ResumeLayout(false);
            this._splitContainer1.ResumeLayout(false);
            this._splitContainer2.Panel1.ResumeLayout(false);
            this._splitContainer2.Panel1.PerformLayout();
            this._splitContainer2.Panel2.ResumeLayout(false);
            this._splitContainer2.ResumeLayout(false);
            this.toolStrip1.ResumeLayout(false);
            this.toolStrip1.PerformLayout();
            this._splitTemplateAndSource.Panel2.ResumeLayout(false);
            this._splitTemplateAndSource.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer _splitContainer1;
        private System.Windows.Forms.SplitContainer _splitContainer2;
        private Browser _browser1;
        private System.Windows.Forms.SplitContainer _splitTemplateAndSource;
        private SourceTextControl _translationSourcesControl;
        private System.Windows.Forms.ToolStrip toolStrip1;
        private System.Windows.Forms.ToolStripButton _cutButton;
        private System.Windows.Forms.ToolStripButton _copyButton;
        private System.Windows.Forms.ToolStripButton _pasteButton;
        private System.Windows.Forms.ToolStripButton _undoButton;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton _deletePageButton;
        private System.Windows.Forms.Timer _editButtonsUpdateTimer;


    }
}