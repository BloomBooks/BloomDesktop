namespace Bloom.NewCollection
{
    partial class KindOfCollectionControl
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
            this._nextButton = new System.Windows.Forms.Button();
            this._radioShellbookLibrary = new System.Windows.Forms.RadioButton();
            this._radioNormalVernacularLibrary = new System.Windows.Forms.RadioButton();
            this.SuspendLayout();
            // 
            // _nextButton
            // 
            this._nextButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this._nextButton.Location = new System.Drawing.Point(277, 112);
            this._nextButton.Name = "_nextButton";
            this._nextButton.Size = new System.Drawing.Size(93, 29);
            this._nextButton.TabIndex = 8;
            this._nextButton.Text = "&Next";
            this._nextButton.UseVisualStyleBackColor = true;
            // 
            // _radioShellbookLibrary
            // 
            this._radioShellbookLibrary.AutoSize = true;
            this._radioShellbookLibrary.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._radioShellbookLibrary.Location = new System.Drawing.Point(0, 47);
            this._radioShellbookLibrary.Name = "_radioShellbookLibrary";
            this._radioShellbookLibrary.Size = new System.Drawing.Size(381, 19);
            this._radioShellbookLibrary.TabIndex = 7;
            this._radioShellbookLibrary.Text = "I will be making shellbooks in a national language for use by others.";
            this._radioShellbookLibrary.UseVisualStyleBackColor = true;
            // 
            // _radioNormalVernacularLibrary
            // 
            this._radioNormalVernacularLibrary.AutoSize = true;
            this._radioNormalVernacularLibrary.Checked = true;
            this._radioNormalVernacularLibrary.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this._radioNormalVernacularLibrary.Location = new System.Drawing.Point(0, 3);
            this._radioNormalVernacularLibrary.Name = "_radioNormalVernacularLibrary";
            this._radioNormalVernacularLibrary.Size = new System.Drawing.Size(356, 29);
            this._radioNormalVernacularLibrary.TabIndex = 6;
            this._radioNormalVernacularLibrary.TabStop = true;
            this._radioNormalVernacularLibrary.Text = "I will be making books in my language.";
            this._radioNormalVernacularLibrary.UseVisualStyleBackColor = true;
            // 
            // KidOfProjectControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._nextButton);
            this.Controls.Add(this._radioShellbookLibrary);
            this.Controls.Add(this._radioNormalVernacularLibrary);
            this.Name = "KidOfProjectControl";
            this.Size = new System.Drawing.Size(383, 162);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.Button _nextButton;
        public System.Windows.Forms.RadioButton _radioShellbookLibrary;
        public System.Windows.Forms.RadioButton _radioNormalVernacularLibrary;
    }
}
