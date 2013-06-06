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
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _lookupISOControl
			// 
			this._lookupISOControl.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
			this._lookupISOControl.ISOCode = "";
			this._lookupISOControl.LanguageInfo = null;
			this._L10NSharpExtender.SetLocalizableToolTip(this._lookupISOControl, null);
			this._L10NSharpExtender.SetLocalizationComment(this._lookupISOControl, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._lookupISOControl, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._lookupISOControl, "NewCollectionWizard.LanguageIdControl.LookupISOControl");
			this._lookupISOControl.Location = new System.Drawing.Point(3, 3);
			this._lookupISOControl.Name = "_lookupISOControl";
			this._lookupISOControl.Size = new System.Drawing.Size(551, 231);
			this._lookupISOControl.TabIndex = 11;
			this._lookupISOControl.Leave += new System.EventHandler(this._lookupISOControl_Leave);
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "NewCollectionWizard.LanguageIdControl";
			// 
			// LanguageIdControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._lookupISOControl);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "NewCollectionWizard.LanguageIdControl.LanguageIdControl");
			this.Name = "LanguageIdControl";
			this.Size = new System.Drawing.Size(615, 260);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private Palaso.UI.WindowsForms.WritingSystems.LookupISOControl _lookupISOControl;
        private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}
