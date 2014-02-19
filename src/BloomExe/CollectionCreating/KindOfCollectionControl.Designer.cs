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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(KindOfCollectionControl));
            this._radioSourceCollection = new System.Windows.Forms.RadioButton();
            this._radioNormalVernacularCollection = new System.Windows.Forms.RadioButton();
            this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
            this.betterLabel1 = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
            this.betterLabel2 = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
            this.SuspendLayout();
            // 
            // _radioSourceCollection
            // 
            this._radioSourceCollection.AutoSize = true;
            this._radioSourceCollection.Font = new System.Drawing.Font("Segoe UI", 14F);
            this._L10NSharpExtender.SetLocalizableToolTip(this._radioSourceCollection, null);
            this._L10NSharpExtender.SetLocalizationComment(this._radioSourceCollection, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._radioSourceCollection, L10NSharp.LocalizationPriority.High);
            this._L10NSharpExtender.SetLocalizingId(this._radioSourceCollection, "NewCollectionWizard.KindOfCollectionPage.sourceCollection");
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
            this._L10NSharpExtender.SetLocalizableToolTip(this._radioNormalVernacularCollection, null);
            this._L10NSharpExtender.SetLocalizationComment(this._radioNormalVernacularCollection, null);
            this._L10NSharpExtender.SetLocalizationPriority(this._radioNormalVernacularCollection, L10NSharp.LocalizationPriority.High);
            this._L10NSharpExtender.SetLocalizingId(this._radioNormalVernacularCollection, "NewCollectionWizard.KindOfCollectionPage.vernacularCollection");
            this._radioNormalVernacularCollection.Location = new System.Drawing.Point(0, 3);
            this._radioNormalVernacularCollection.Name = "_radioNormalVernacularCollection";
            this._radioNormalVernacularCollection.Size = new System.Drawing.Size(212, 29);
            this._radioNormalVernacularCollection.TabIndex = 6;
            this._radioNormalVernacularCollection.TabStop = true;
            this._radioNormalVernacularCollection.Text = "Vernacular Collection";
            this._radioNormalVernacularCollection.UseVisualStyleBackColor = true;
            this._radioNormalVernacularCollection.CheckedChanged += new System.EventHandler(this._radioNormalVernacularCollection_CheckedChanged);
            // 
            // _L10NSharpExtender
            // 
            this._L10NSharpExtender.LocalizationManagerId = "Bloom";
            this._L10NSharpExtender.PrefixForNewItems = "NewCollectionWizard.";
            // 
            // betterLabel1
            // 
            this.betterLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.betterLabel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.betterLabel1.Enabled = false;
            this.betterLabel1.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._L10NSharpExtender.SetLocalizableToolTip(this.betterLabel1, null);
            this._L10NSharpExtender.SetLocalizationComment(this.betterLabel1, null);
            this._L10NSharpExtender.SetLocalizationPriority(this.betterLabel1, L10NSharp.LocalizationPriority.High);
            this._L10NSharpExtender.SetLocalizingId(this.betterLabel1, "NewCollectionWizard.KindOfCollectionPage.vernacularCollectionDescription");
            this.betterLabel1.Location = new System.Drawing.Point(19, 31);
            this.betterLabel1.Multiline = true;
            this.betterLabel1.Name = "betterLabel1";
            this.betterLabel1.ReadOnly = true;
            this.betterLabel1.Size = new System.Drawing.Size(358, 17);
            this.betterLabel1.TabIndex = 9;
            this.betterLabel1.TabStop = false;
            this.betterLabel1.Text = "A collection of books in a local language.";
            // 
            // betterLabel2
            // 
            this.betterLabel2.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.betterLabel2.Enabled = false;
            this.betterLabel2.Font = new System.Drawing.Font("Segoe UI", 9F);
            this._L10NSharpExtender.SetLocalizableToolTip(this.betterLabel2, null);
            this._L10NSharpExtender.SetLocalizationComment(this.betterLabel2, null);
            this._L10NSharpExtender.SetLocalizationPriority(this.betterLabel2, L10NSharp.LocalizationPriority.High);
            this._L10NSharpExtender.SetLocalizingId(this.betterLabel2, "NewCollectionWizard.KindOfCollectionPage.sourceCollectionDescription");
            this.betterLabel2.Location = new System.Drawing.Point(19, 106);
            this.betterLabel2.Multiline = true;
            this.betterLabel2.Name = "betterLabel2";
            this.betterLabel2.ReadOnly = true;
            this.betterLabel2.Size = new System.Drawing.Size(358, 65);
            this.betterLabel2.TabIndex = 10;
            this.betterLabel2.TabStop = false;
            this.betterLabel2.Text = resources.GetString("betterLabel2.Text");
            // 
            // KindOfCollectionControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.betterLabel2);
            this.Controls.Add(this.betterLabel1);
            this.Controls.Add(this._radioSourceCollection);
            this.Controls.Add(this._radioNormalVernacularCollection);
            this._L10NSharpExtender.SetLocalizableToolTip(this, null);
            this._L10NSharpExtender.SetLocalizationComment(this, null);
            this._L10NSharpExtender.SetLocalizingId(this, "KindOfCollectionControl.KindOfCollectionControl");
            this.Name = "KindOfCollectionControl";
            this.Size = new System.Drawing.Size(391, 213);
            ((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

		public System.Windows.Forms.RadioButton _radioSourceCollection;
        public System.Windows.Forms.RadioButton _radioNormalVernacularCollection;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel betterLabel1;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel betterLabel2;
    }
}
