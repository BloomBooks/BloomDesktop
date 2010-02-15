namespace Bloom
{
    partial class TemplateBookView
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
            this.button1 = new System.Windows.Forms.Button();
            this._browser = new Bloom.Browser();
            this.SuspendLayout();
            // 
            // button1
            // 
            this.button1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button1.Image = global::Bloom.Properties.Resources.Logo27x32;
            this.button1.Location = new System.Drawing.Point(821, 332);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(76, 115);
            this.button1.TabIndex = 0;
            this.button1.Text = "Create a book in my language using this template";
            this.button1.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
            this.button1.UseVisualStyleBackColor = true;
            // 
            // _browser
            // 
            this._browser.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this._browser.BackColor = System.Drawing.Color.Blue;
            this._browser.Location = new System.Drawing.Point(0, 0);
            this._browser.Name = "_browser";
            this._browser.Size = new System.Drawing.Size(801, 447);
            this._browser.TabIndex = 1;
            // 
            // TemplateBookView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._browser);
            this.Controls.Add(this.button1);
            this.Name = "TemplateBookView";
            this.Size = new System.Drawing.Size(900, 450);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button button1;
        private Browser _browser;
    }
}
