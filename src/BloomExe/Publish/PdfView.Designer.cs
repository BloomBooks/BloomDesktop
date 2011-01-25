namespace Bloom.Publish
{
    partial class PdfView
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
            this._browser = new Bloom.Browser();
            this._loadTimer = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            // 
            // _browser
            // 
            this._browser.BackColor = System.Drawing.Color.DarkGray;
            this._browser.Dock = System.Windows.Forms.DockStyle.Fill;
            this._browser.Location = new System.Drawing.Point(0, 0);
            this._browser.Name = "_browser";
            this._browser.Size = new System.Drawing.Size(150, 150);
            this._browser.TabIndex = 0;
            this._browser.VisibleChanged += new System.EventHandler(this._browser_VisibleChanged);
            // 
            // _loadTimer
            // 
            this._loadTimer.Tick += new System.EventHandler(this._loadTimer_Tick);
            // 
            // PdfView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._browser);
            this.Name = "PdfView";
            this.ResumeLayout(false);

        }

        #endregion

        private Browser _browser;
        private System.Windows.Forms.Timer _loadTimer;
    }
}