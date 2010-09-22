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
			this._addToLibraryButton = new System.Windows.Forms.Button();
			this._browser = new Bloom.Browser();
			this.SuspendLayout();
			// 
			// _addToLibraryButton
			// 
			this._addToLibraryButton.Location = new System.Drawing.Point(3, 3);
			this._addToLibraryButton.Name = "_addToLibraryButton";
			this._addToLibraryButton.Size = new System.Drawing.Size(259, 21);
			this._addToLibraryButton.TabIndex = 0;
			this._addToLibraryButton.Text = "Create a book in my language using this template";
			this._addToLibraryButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;
			this._addToLibraryButton.UseVisualStyleBackColor = true;
			this._addToLibraryButton.Click += new System.EventHandler(this.OnAddToLibraryClick);
			// 
			// _browser
			// 
			this._browser.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
						| System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this._browser.BackColor = System.Drawing.Color.DarkGray;
			this._browser.Location = new System.Drawing.Point(0, 33);
			this._browser.Name = "_browser";
			this._browser.Size = new System.Drawing.Size(897, 417);
			this._browser.TabIndex = 1;
			// 
			// TemplateBookView
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._browser);
			this.Controls.Add(this._addToLibraryButton);
			this.Name = "TemplateBookView";
			this.Size = new System.Drawing.Size(900, 450);
			this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button _addToLibraryButton;
        private Browser _browser;
    }
}
