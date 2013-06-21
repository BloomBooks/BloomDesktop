namespace Bloom.CollectionCreating
{
	partial class LanguageIdControl
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
            this._lookupISOControl = new Palaso.UI.WindowsForms.WritingSystems.LookupISOControl();
            this._selectedLanguage = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
            this.localizationExtender1 = new L10NSharp.UI.LocalizationExtender(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).BeginInit();
            this.SuspendLayout();
            // 
            // _lookupISOControl
            // 
            this._lookupISOControl.ISOCode = "qaa";
            this.localizationExtender1.SetLocalizableToolTip(this._lookupISOControl, null);
            this.localizationExtender1.SetLocalizationComment(this._lookupISOControl, null);
            this.localizationExtender1.SetLocalizationPriority(this._lookupISOControl, L10NSharp.LocalizationPriority.NotLocalizable);
            this.localizationExtender1.SetLocalizingId(this._lookupISOControl, "NewCollectionWizard.LanguageIdControl.LookupISOControl");
            this._lookupISOControl.Location = new System.Drawing.Point(3, 3);
            this._lookupISOControl.Name = "_lookupISOControl";
            this._lookupISOControl.Size = new System.Drawing.Size(242, 252);
            this._lookupISOControl.TabIndex = 11;
            this._lookupISOControl.Leave += new System.EventHandler(this._lookupISOControl_Leave);
            // 
            // _selectedLanguage
            // 
            this._selectedLanguage.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this._selectedLanguage.Enabled = false;
            this._selectedLanguage.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Bold);
            this.localizationExtender1.SetLocalizableToolTip(this._selectedLanguage, null);
            this.localizationExtender1.SetLocalizationComment(this._selectedLanguage, null);
            this.localizationExtender1.SetLocalizationPriority(this._selectedLanguage, L10NSharp.LocalizationPriority.NotLocalizable);
            this.localizationExtender1.SetLocalizingId(this._selectedLanguage, "NewCollectionWizard.LanguageIdControl._selectedLanguage");
            this._selectedLanguage.Location = new System.Drawing.Point(248, 33);
            this._selectedLanguage.Multiline = true;
            this._selectedLanguage.Name = "_selectedLanguage";
            this._selectedLanguage.ReadOnly = true;
            this._selectedLanguage.Size = new System.Drawing.Size(209, 23);
            this._selectedLanguage.TabIndex = 12;
            this._selectedLanguage.TabStop = false;
            this._selectedLanguage.Text = "<Language Name>";
            // 
            // localizationExtender1
            // 
            this.localizationExtender1.LocalizationManagerId = "Bloom";
            this.localizationExtender1.PrefixForNewItems = "NewCollectionWizard.LanguageIdControl";
            // 
            // LanguageIdControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this._selectedLanguage);
            this.Controls.Add(this._lookupISOControl);
            this.localizationExtender1.SetLocalizableToolTip(this, null);
            this.localizationExtender1.SetLocalizationComment(this, null);
            this.localizationExtender1.SetLocalizingId(this, "NewCollectionWizard.LanguageIdControl.LanguageIdControl");
            this.Name = "LanguageIdControl";
            this.Size = new System.Drawing.Size(521, 260);
            ((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

		}

		#endregion

		private Palaso.UI.WindowsForms.WritingSystems.LookupISOControl _lookupISOControl;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel _selectedLanguage;
        private L10NSharp.UI.LocalizationExtender localizationExtender1;
	}
}
