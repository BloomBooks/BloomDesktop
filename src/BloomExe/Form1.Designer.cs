namespace Bloom
{
    partial class Form1
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.pdfPage = new System.Windows.Forms.TabPage();
            this.htmlPreviewPage = new System.Windows.Forms.TabPage();
            this.editPage = new System.Windows.Forms.TabPage();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this._documentThumnails = new System.Windows.Forms.ListView();
            this.button1 = new System.Windows.Forms.Button();
            this._documentThumbnailImages = new System.Windows.Forms.ImageList(this.components);
            this.tabControl1.SuspendLayout();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.SuspendLayout();
            // 
            // pdfPage
            // 
            this.pdfPage.Location = new System.Drawing.Point(4, 22);
            this.pdfPage.Name = "pdfPage";
            this.pdfPage.Size = new System.Drawing.Size(677, 506);
            this.pdfPage.TabIndex = 2;
            this.pdfPage.Text = "PDF Preview";
            this.pdfPage.UseVisualStyleBackColor = true;
            // 
            // htmlPreviewPage
            // 
            this.htmlPreviewPage.Location = new System.Drawing.Point(4, 22);
            this.htmlPreviewPage.Name = "htmlPreviewPage";
            this.htmlPreviewPage.Size = new System.Drawing.Size(677, 506);
            this.htmlPreviewPage.TabIndex = 1;
            this.htmlPreviewPage.Text = "Html Preview";
            this.htmlPreviewPage.UseVisualStyleBackColor = true;
            // 
            // editPage
            // 
            this.editPage.Location = new System.Drawing.Point(4, 22);
            this.editPage.Name = "editPage";
            this.editPage.Padding = new System.Windows.Forms.Padding(3);
            this.editPage.Size = new System.Drawing.Size(709, 546);
            this.editPage.TabIndex = 0;
            this.editPage.Text = "Html Edit";
            this.editPage.UseVisualStyleBackColor = true;
            // 
            // tabControl1
            // 
            this.tabControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.tabControl1.Controls.Add(this.editPage);
            this.tabControl1.Controls.Add(this.htmlPreviewPage);
            this.tabControl1.Controls.Add(this.pdfPage);
            this.tabControl1.Location = new System.Drawing.Point(2, 12);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(717, 572);
            this.tabControl1.TabIndex = 1;
            // 
            // splitContainer1
            // 
            this.splitContainer1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.splitContainer1.Location = new System.Drawing.Point(725, 34);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this._documentThumnails);
            this.splitContainer1.Size = new System.Drawing.Size(98, 546);
            this.splitContainer1.SplitterDistance = 396;
            this.splitContainer1.TabIndex = 3;
            // 
            // _documentThumnails
            // 
            this._documentThumnails.BackColor = System.Drawing.Color.DimGray;
            this._documentThumnails.Dock = System.Windows.Forms.DockStyle.Fill;
            this._documentThumnails.LargeImageList = this._documentThumbnailImages;
            this._documentThumnails.Location = new System.Drawing.Point(0, 0);
            this._documentThumnails.Name = "_documentThumnails";
            this._documentThumnails.Size = new System.Drawing.Size(98, 396);
            this._documentThumnails.TabIndex = 3;
            this._documentThumnails.UseCompatibleStateImageBehavior = false;
            this._documentThumnails.SelectedIndexChanged += new System.EventHandler(this._documentThumnails_SelectedIndexChanged);
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(337, 4);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 4;
            this.button1.Text = "button1";
            this.button1.UseVisualStyleBackColor = true;
            // 
            // _documentThumbnailImages
            // 
            this._documentThumbnailImages.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_documentThumbnailImages.ImageStream")));
            this._documentThumbnailImages.TransparentColor = System.Drawing.Color.Transparent;
            this._documentThumbnailImages.Images.SetKeyName(0, "x-office-document.png");
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(826, 583);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.tabControl1);
            this.Name = "Form1";
            this.Text = "Bloom Test";
            this.WindowState = System.Windows.Forms.FormWindowState.Maximized;
            this.tabControl1.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabPage pdfPage;
        private System.Windows.Forms.TabPage htmlPreviewPage;
        private System.Windows.Forms.TabPage editPage;
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.ListView _documentThumnails;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.ImageList _documentThumbnailImages;

    }
}

