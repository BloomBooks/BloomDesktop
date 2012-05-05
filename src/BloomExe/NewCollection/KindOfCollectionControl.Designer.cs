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
			this.components = new System.ComponentModel.Container();
			this._nextButton = new System.Windows.Forms.Button();
			this._radioSourceCollection = new System.Windows.Forms.RadioButton();
			this._radioNormalVernacularCollection = new System.Windows.Forms.RadioButton();
			this.localizationExtender1 = new Localization.UI.LocalizationExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).BeginInit();
			this.SuspendLayout();
			// 
			// _nextButton
			// 
			this._nextButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.localizationExtender1.SetLocalizableToolTip(this._nextButton, null);
			this.localizationExtender1.SetLocalizationComment(this._nextButton, null);
			this.localizationExtender1.SetLocalizingId(this._nextButton, "Common.NextButton");
			this._nextButton.Location = new System.Drawing.Point(277, 112);
			this._nextButton.Name = "_nextButton";
			this._nextButton.Size = new System.Drawing.Size(93, 29);
			this._nextButton.TabIndex = 8;
			this._nextButton.Text = "&Next";
			this._nextButton.UseVisualStyleBackColor = true;
			// 
			// _radioSourceCollection
			// 
			this._radioSourceCollection.AutoSize = true;
			this._radioSourceCollection.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._radioSourceCollection, null);
			this.localizationExtender1.SetLocalizationComment(this._radioSourceCollection, null);
			this.localizationExtender1.SetLocalizationPriority(this._radioSourceCollection, Localization.LocalizationPriority.Medium);
			this.localizationExtender1.SetLocalizingId(this._radioSourceCollection, "KindOfCollectionControl._radioShellbookLibrary");
			this._radioSourceCollection.Location = new System.Drawing.Point(0, 47);
			this._radioSourceCollection.Name = "_radioSourceCollection";
			this._radioSourceCollection.Size = new System.Drawing.Size(384, 19);
			this._radioSourceCollection.TabIndex = 7;
			this._radioSourceCollection.Text = "I will be making shell books in a national language for use by others.";
			this._radioSourceCollection.UseVisualStyleBackColor = true;
			// 
			// _radioNormalVernacularCollection
			// 
			this._radioNormalVernacularCollection.AutoSize = true;
			this._radioNormalVernacularCollection.Checked = true;
			this._radioNormalVernacularCollection.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._radioNormalVernacularCollection, null);
			this.localizationExtender1.SetLocalizationComment(this._radioNormalVernacularCollection, null);
			this.localizationExtender1.SetLocalizingId(this._radioNormalVernacularCollection, "KindOfCollectionControl._radioNormalVernacularLibrary");
			this._radioNormalVernacularCollection.Location = new System.Drawing.Point(0, 3);
			this._radioNormalVernacularCollection.Name = "_radioNormalVernacularCollection";
			this._radioNormalVernacularCollection.Size = new System.Drawing.Size(356, 29);
			this._radioNormalVernacularCollection.TabIndex = 6;
			this._radioNormalVernacularCollection.TabStop = true;
			this._radioNormalVernacularCollection.Text = "I will be making books in my language.";
			this._radioNormalVernacularCollection.UseVisualStyleBackColor = true;
			// 
			// localizationExtender1
			// 
			this.localizationExtender1.LocalizationManagerId = "Bloom";
			// 
			// KindOfCollectionControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._nextButton);
			this.Controls.Add(this._radioSourceCollection);
			this.Controls.Add(this._radioNormalVernacularCollection);
			this.localizationExtender1.SetLocalizableToolTip(this, null);
			this.localizationExtender1.SetLocalizationComment(this, null);
			this.localizationExtender1.SetLocalizingId(this, "KindOfCollectionControl.KindOfCollectionControl");
			this.Name = "KindOfCollectionControl";
			this.Size = new System.Drawing.Size(383, 162);
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

        public System.Windows.Forms.Button _nextButton;
        public System.Windows.Forms.RadioButton _radioSourceCollection;
        public System.Windows.Forms.RadioButton _radioNormalVernacularCollection;
		private Localization.UI.LocalizationExtender localizationExtender1;
    }
}
