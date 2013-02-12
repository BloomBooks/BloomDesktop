namespace Bloom.CollectionCreating
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
			this._radioSourceCollection = new System.Windows.Forms.RadioButton();
			this._radioNormalVernacularCollection = new System.Windows.Forms.RadioButton();
			this.localizationExtender1 = new Localization.UI.LocalizationExtender(this.components);
			this.betterLabel1 = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this.betterLabel2 = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).BeginInit();
			this.SuspendLayout();
			// 
			// _radioSourceCollection
			// 
			this._radioSourceCollection.AutoSize = true;
			this._radioSourceCollection.Font = new System.Drawing.Font("Segoe UI", 14F);
			this.localizationExtender1.SetLocalizableToolTip(this._radioSourceCollection, null);
			this.localizationExtender1.SetLocalizationComment(this._radioSourceCollection, null);
			this.localizationExtender1.SetLocalizationPriority(this._radioSourceCollection, Localization.LocalizationPriority.Medium);
			this.localizationExtender1.SetLocalizingId(this._radioSourceCollection, "newCollectionWizard.sourceCollection");
			this._radioSourceCollection.Location = new System.Drawing.Point(0, 71);
			this._radioSourceCollection.Name = "_radioSourceCollection";
			this._radioSourceCollection.Size = new System.Drawing.Size(178, 29);
			this._radioSourceCollection.TabIndex = 7;
			this._radioSourceCollection.Text = "Source Collection";
			this._radioSourceCollection.UseVisualStyleBackColor = true;
			this._radioSourceCollection.CheckedChanged += new System.EventHandler(this._radioSourceCollection_CheckedChanged);
			// 
			// _radioNormalVernacularCollection
			// 
			this._radioNormalVernacularCollection.AutoSize = true;
			this._radioNormalVernacularCollection.Checked = true;
			this._radioNormalVernacularCollection.Font = new System.Drawing.Font("Segoe UI", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._radioNormalVernacularCollection, null);
			this.localizationExtender1.SetLocalizationComment(this._radioNormalVernacularCollection, null);
			this.localizationExtender1.SetLocalizingId(this._radioNormalVernacularCollection, "newCollectionWizard.vernacularCollection");
			this._radioNormalVernacularCollection.Location = new System.Drawing.Point(0, 3);
			this._radioNormalVernacularCollection.Name = "_radioNormalVernacularCollection";
			this._radioNormalVernacularCollection.Size = new System.Drawing.Size(212, 29);
			this._radioNormalVernacularCollection.TabIndex = 6;
			this._radioNormalVernacularCollection.TabStop = true;
			this._radioNormalVernacularCollection.Text = "Vernacular Collection";
			this._radioNormalVernacularCollection.UseVisualStyleBackColor = true;
			this._radioNormalVernacularCollection.CheckedChanged += new System.EventHandler(this._radioNormalVernacularCollection_CheckedChanged);
			// 
			// localizationExtender1
			// 
			this.localizationExtender1.LocalizationManagerId = "Bloom";
			// 
			// betterLabel1
			// 
			this.betterLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.betterLabel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.betterLabel1.Font = new System.Drawing.Font("Segoe UI", 9F);
			this.localizationExtender1.SetLocalizableToolTip(this.betterLabel1, null);
			this.localizationExtender1.SetLocalizationComment(this.betterLabel1, null);
			this.localizationExtender1.SetLocalizingId(this.betterLabel1, "newCollectionWizard.vernacularCollectionDescription");
			this.betterLabel1.Location = new System.Drawing.Point(19, 31);
			this.betterLabel1.Multiline = true;
			this.betterLabel1.Name = "betterLabel1";
			this.betterLabel1.ReadOnly = true;
			this.betterLabel1.Size = new System.Drawing.Size(358, 34);
			this.betterLabel1.TabIndex = 9;
			this.betterLabel1.TabStop = false;
			this.betterLabel1.Text = "A collection of books in a local language.";
			// 
			// betterLabel2
			// 
			this.betterLabel2.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.betterLabel2.Font = new System.Drawing.Font("Segoe UI", 9F);
			this.localizationExtender1.SetLocalizableToolTip(this.betterLabel2, null);
			this.localizationExtender1.SetLocalizationComment(this.betterLabel2, null);
			this.localizationExtender1.SetLocalizingId(this.betterLabel2, "newCollectionWizard.sourceCollectionDescription");
			this.betterLabel2.Location = new System.Drawing.Point(19, 106);
			this.betterLabel2.Multiline = true;
			this.betterLabel2.Name = "betterLabel2";
			this.betterLabel2.ReadOnly = true;
			this.betterLabel2.Size = new System.Drawing.Size(358, 83);
			this.betterLabel2.TabIndex = 10;
			this.betterLabel2.TabStop = false;
			this.betterLabel2.Text = "A collection of shell or template books in one or more languages of wider communi" +
    "cation. From this, you can make a BloomPack to give to others so that they can m" +
    "ake vernacular books with it.";
			// 
			// KindOfCollectionControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this.betterLabel2);
			this.Controls.Add(this.betterLabel1);
			this.Controls.Add(this._radioSourceCollection);
			this.Controls.Add(this._radioNormalVernacularCollection);
			this.localizationExtender1.SetLocalizableToolTip(this, null);
			this.localizationExtender1.SetLocalizationComment(this, null);
			this.localizationExtender1.SetLocalizingId(this, "KindOfCollectionControl.KindOfCollectionControl");
			this.Name = "KindOfCollectionControl";
			this.Size = new System.Drawing.Size(391, 213);
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

        }

        #endregion

		public System.Windows.Forms.RadioButton _radioSourceCollection;
        public System.Windows.Forms.RadioButton _radioNormalVernacularCollection;
		private Localization.UI.LocalizationExtender localizationExtender1;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel betterLabel1;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel betterLabel2;
    }
}
