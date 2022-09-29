using System.Windows.Forms;

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
			_lookupModel.ReadinessChanged -= OnLookupModelControlReadinessChanged;
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
			this._lookupModel = new SIL.Windows.Forms.WritingSystems.LanguageLookupControl();
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// _lookupModelControl
			// 
			this._lookupModel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
			this._L10NSharpExtender.SetLocalizableToolTip(this._lookupModel, null);
			this._L10NSharpExtender.SetLocalizationComment(this._lookupModel, null);
			this._L10NSharpExtender.SetLocalizationPriority(this._lookupModel, L10NSharp.LocalizationPriority.NotLocalizable);
			this._L10NSharpExtender.SetLocalizingId(this._lookupModel, "NewCollectionWizard.LanguageIdControl.LookupISOControl");
			this._lookupModel.Location = new System.Drawing.Point(3, 3);
			this._lookupModel.Name = "_lookupModelControl";
			this._lookupModel.Size = new System.Drawing.Size(560, 231);
			this._lookupModel.TabIndex = 11;
			this._lookupModel.Leave += new System.EventHandler(this._lookupModelControl_Leave);
			this._lookupModel.Dock = DockStyle.Fill;
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
			this.Controls.Add(this._lookupModel);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "NewCollectionWizard.LanguageIdControl.LanguageIdControl");
			this.Name = "LanguageIdControl";
			this.Size = new System.Drawing.Size(615, 260);
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private SIL.Windows.Forms.WritingSystems.LanguageLookupControl _lookupModel;
        private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}
