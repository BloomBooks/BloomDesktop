namespace TestGeckoPdf
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
            this.adobeReaderControl1 = new Bloom.Publish.AdobeReaderControl();
            this._geckoPdfMaker = new Bloom.Publish.GeckoPdfComponent(this.components);
            this.browser1 = new Bloom.Browser();
            this.SuspendLayout();
            // 
            // adobeReaderControl1
            // 
            this.adobeReaderControl1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.adobeReaderControl1.BackColor = System.Drawing.Color.White;
            this.adobeReaderControl1.Location = new System.Drawing.Point(393, 12);
            this.adobeReaderControl1.Name = "adobeReaderControl1";
            this.adobeReaderControl1.Size = new System.Drawing.Size(324, 541);
            this.adobeReaderControl1.TabIndex = 0;
            // 
            // _geckoPdfMaker
            // 
            this._geckoPdfMaker.PdfReady += new System.EventHandler(this.OnPdfReady);
            // 
            // browser1
            // 
            this.browser1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.browser1.BackColor = System.Drawing.Color.DarkGray;
            this.browser1.Location = new System.Drawing.Point(12, 12);
            this.browser1.Name = "browser1";
            this.browser1.Size = new System.Drawing.Size(365, 541);
            this.browser1.TabIndex = 1;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(739, 593);
            this.Controls.Add(this.browser1);
            this.Controls.Add(this.adobeReaderControl1);
            this.Name = "Form1";
            this.Text = "Form1";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);

        }

        #endregion

        private Bloom.Publish.GeckoPdfComponent _geckoPdfMaker;
        private Bloom.Publish.AdobeReaderControl adobeReaderControl1;
        private Bloom.Browser browser1;
    }
}

