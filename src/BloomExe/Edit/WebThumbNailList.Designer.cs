using System;

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

        private void WebThumbnailDesigner_BackColorChanged(object sender, EventArgs e)
		{
            if (_browser != null)
            {
                _browser.BackColor = BackColor;
            }
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
            this.SuspendLayout();
            // 
            // WebThumbNailList
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.Control;
			this.BackColorChanged += new System.EventHandler(this.WebThumbnailDesigner_BackColorChanged);
			this.ForeColor = System.Drawing.SystemColors.WindowText;
            this.Name = "WebThumbNailList";
            this.Size = new System.Drawing.Size(150, 491);
            this.ResumeLayout(false);
        }

        #endregion
    }
}
