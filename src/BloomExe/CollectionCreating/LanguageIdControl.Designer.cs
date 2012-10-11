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
			this._lookupISOControl = new Palaso.UI.WindowsForms.WritingSystems.LookupISOControl();
			this._selectedLanguage = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this.SuspendLayout();
			// 
			// _lookupISOControl
			// 
			this._lookupISOControl.ISOCode = "qaa";
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
			this._selectedLanguage.Location = new System.Drawing.Point(248, 33);
			this._selectedLanguage.Multiline = true;
			this._selectedLanguage.Name = "_selectedLanguage";
			this._selectedLanguage.ReadOnly = true;
			this._selectedLanguage.Size = new System.Drawing.Size(209, 131);
			this._selectedLanguage.TabIndex = 12;
			this._selectedLanguage.TabStop = false;
			this._selectedLanguage.Text = "<Language Name>";
			// 
			// LanguageIdControl
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.Controls.Add(this._selectedLanguage);
			this.Controls.Add(this._lookupISOControl);
			this.Name = "LanguageIdControl";
			this.Size = new System.Drawing.Size(521, 260);
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private Palaso.UI.WindowsForms.WritingSystems.LookupISOControl _lookupISOControl;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel _selectedLanguage;
	}
}
