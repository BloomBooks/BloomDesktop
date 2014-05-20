namespace Bloom.Edit
{
    partial class WebThumbNailList
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WebThumbNailList));
            this._thumbnailImageList = new System.Windows.Forms.ImageList(this.components);
            this.SuspendLayout();
            // 
            // _thumbnailImageList
            // 
            this._thumbnailImageList.ImageStream = ((System.Windows.Forms.ImageListStreamer)(resources.GetObject("_thumbnailImageList.ImageStream")));
            this._thumbnailImageList.TransparentColor = System.Drawing.Color.Transparent;
            this._thumbnailImageList.Images.SetKeyName(0, "x-office-document.png");
            // 
            // WebThumbNailList
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Control;
            this.ForeColor = System.Drawing.SystemColors.WindowText;
            this.Name = "WebThumbNailList";
            this.Size = new System.Drawing.Size(150, 491);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ImageList _thumbnailImageList;
    }
}
