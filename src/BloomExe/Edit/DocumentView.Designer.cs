namespace Bloom
{
    partial class DocumentView
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(DocumentView));
            this.modeImages = new System.Windows.Forms.ImageList(this.components);
            this.edittingView1 = new Bloom.EdittingView();
            this.SuspendLayout();
            // 
            // modeImages
            // 
            this.modeImages.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("modeImages.ImageStream")));
            this.modeImages.TransparentColor = System.Drawing.Color.Transparent;
            this.modeImages.Images.SetKeyName(0, "accessories-text-editor.png");
            this.modeImages.Images.SetKeyName(1, "internet-news-reader.png");
            this.modeImages.Images.SetKeyName(2, "Pdf_16x16.png");
            // 
            // edittingView1
            // 
            this.edittingView1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.edittingView1.Location = new System.Drawing.Point(0, 29);
            this.edittingView1.Name = "edittingView1";
            this.edittingView1.Size = new System.Drawing.Size(740, 520);
            this.edittingView1.TabIndex = 0;
            // 
            // DocumentView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.Controls.Add(this.edittingView1);
            this.Name = "DocumentView";
            this.Size = new System.Drawing.Size(743, 552);
            this.ResumeLayout(false);

        }

        #endregion

        private EdittingView edittingView1;
        private System.Windows.Forms.ImageList modeImages;
    }
}
