namespace Bloom.CollectionTab
{
    partial class LibraryBookView
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
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this._addToCollectionButton = new System.Windows.Forms.Button();
            this._browser = new Bloom.Browser();
            this._editBookButton = new System.Windows.Forms.Button();
            this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
            this.SuspendLayout();
            // 
            // _addToCollectionButton
            // 
            this._addToCollectionButton.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this._addToCollectionButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._addToCollectionButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._addToCollectionButton.Image = global::Bloom.Properties.Resources.newBook1;
            this._addToCollectionButton.ImageAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this._L10NSharpExtender.SetLocalizableToolTip(this._addToCollectionButton, null);
            this._L10NSharpExtender.SetLocalizationComment(this._addToCollectionButton, null);
            this._L10NSharpExtender.SetLocalizingId(this._addToCollectionButton, "CollectionTab.MakeBookUsingThisTemplate");
            this._addToCollectionButton.Location = new System.Drawing.Point(12, 3);
            this._addToCollectionButton.Name = "_addToCollectionButton";
            this._addToCollectionButton.Size = new System.Drawing.Size(250, 42);
            this._addToCollectionButton.TabIndex = 0;
            this._addToCollectionButton.Text = "Make a book using this source";
            this._addToCollectionButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this.toolTip1.SetToolTip(this._addToCollectionButton, "Create a book in my language using this source book");
            this._addToCollectionButton.UseVisualStyleBackColor = false;
            this._addToCollectionButton.Click += new System.EventHandler(this.OnAddToLibraryClick);
            this._addToCollectionButton.MouseEnter += new System.EventHandler(this._addToLibraryButton_MouseEnter);
            this._addToCollectionButton.MouseLeave += new System.EventHandler(this._addToLibraryButton_MouseLeave);
            // 
            // _browser
            // 
            this._browser.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this._browser.BackColor = System.Drawing.Color.DarkGray;
            this._L10NSharpExtender.SetLocalizableToolTip(this._browser, null);
            this._L10NSharpExtender.SetLocalizationComment(this._browser, null);
            this._L10NSharpExtender.SetLocalizingId(this._browser, "CollectionTab.Browser");
            this._browser.Location = new System.Drawing.Point(0, 3);
            this._browser.Name = "_browser";
            this._browser.Size = new System.Drawing.Size(897, 447);
            this._browser.TabIndex = 1;
            // 
            // _editBookButton
            // 
            this._editBookButton.BackColor = System.Drawing.SystemColors.ControlLightLight;
            this._editBookButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this._editBookButton.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._editBookButton.Image = global::Bloom.Properties.Resources.edit;
            this._L10NSharpExtender.SetLocalizableToolTip(this._editBookButton, null);
            this._L10NSharpExtender.SetLocalizationComment(this._editBookButton, null);
            this._L10NSharpExtender.SetLocalizingId(this._editBookButton, "CollectionTab._editBookButton");
            this._editBookButton.Location = new System.Drawing.Point(12, 3);
            this._editBookButton.Name = "_editBookButton";
            this._editBookButton.Size = new System.Drawing.Size(147, 42);
            this._editBookButton.TabIndex = 2;
            this._editBookButton.Text = "Edit this book";
            this._editBookButton.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageBeforeText;
            this._editBookButton.UseVisualStyleBackColor = false;
            this._editBookButton.Click += new System.EventHandler(this._editBookButton_Click);
            // 
            // _L10NSharpExtender
            // 
            this._L10NSharpExtender.LocalizationManagerId = "Bloom";
            // 
            // LibraryBookView
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.White;
            this.Controls.Add(this._editBookButton);
            this.Controls.Add(this._addToCollectionButton);
            this.Controls.Add(this._browser);
            this._L10NSharpExtender.SetLocalizableToolTip(this, null);
            this._L10NSharpExtender.SetLocalizationComment(this, null);
            this._L10NSharpExtender.SetLocalizingId(this, "CollectionTab.LibraryBookView");
            this.Name = "LibraryBookView";
            this.Size = new System.Drawing.Size(900, 450);
            this.Resize += new System.EventHandler(this.LibraryBookView_Resize);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.Button _addToCollectionButton;
        private Browser _browser;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.Button _editBookButton;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
    }
}
