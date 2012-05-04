namespace Bloom.NewCollection
{
	partial class NewCollectionDialog
	{

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

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(NewCollectionDialog));
			this._nameCollectionLabel = new System.Windows.Forms.Label();
			this._okButton = new System.Windows.Forms.Button();
			this.btnCancel = new System.Windows.Forms.Button();
			this._textLibraryName = new System.Windows.Forms.TextBox();
			this._pathLabel = new System.Windows.Forms.Label();
			this._whatLanguageLabel = new System.Windows.Forms.Label();
			this._chooseLanguageButton = new System.Windows.Forms.Button();
			this._languageInfoLabel = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this._kindOfCollectionControl1 = new Bloom.NewCollection.KindOfCollectionControl();
			this.localizationExtender1 = new Localization.UI.LocalizationExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).BeginInit();
			this.SuspendLayout();
			//
			// _nameCollectionLabel
			//
			this._nameCollectionLabel.AutoSize = true;
			this._nameCollectionLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._nameCollectionLabel, null);
			this.localizationExtender1.SetLocalizationComment(this._nameCollectionLabel, null);
			this.localizationExtender1.SetLocalizingId(this._nameCollectionLabel, "NewCollectionDialog.label1");
			this._nameCollectionLabel.Location = new System.Drawing.Point(25, 113);
			this._nameCollectionLabel.Name = "_nameCollectionLabel";
			this._nameCollectionLabel.Size = new System.Drawing.Size(232, 15);
			this._nameCollectionLabel.TabIndex = 1;
			this._nameCollectionLabel.Text = "What would you like to call this collection?";
			//
			// _okButton
			//
			this._okButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this._okButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._okButton, null);
			this.localizationExtender1.SetLocalizationComment(this._okButton, null);
			this.localizationExtender1.SetLocalizingId(this._okButton, "NewCollectionDialog.btnOK");
			this._okButton.Location = new System.Drawing.Point(273, 281);
			this._okButton.Name = "_okButton";
			this._okButton.Size = new System.Drawing.Size(91, 29);
			this._okButton.TabIndex = 4;
			this._okButton.Text = "&OK";
			this._okButton.UseVisualStyleBackColor = true;
			this._okButton.Click += new System.EventHandler(this.btnOK_Click);
			//
			// btnCancel
			//
			this.btnCancel.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
			this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
			this.btnCancel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this.btnCancel, null);
			this.localizationExtender1.SetLocalizationComment(this.btnCancel, null);
			this.localizationExtender1.SetLocalizingId(this.btnCancel, "NewCollectionDialog.btnCancel");
			this.btnCancel.Location = new System.Drawing.Point(383, 281);
			this.btnCancel.Name = "btnCancel";
			this.btnCancel.Size = new System.Drawing.Size(88, 27);
			this.btnCancel.TabIndex = 3;
			this.btnCancel.Text = "&Cancel";
			this.btnCancel.UseVisualStyleBackColor = true;
			this.btnCancel.Click += new System.EventHandler(this.btnCancel_Click);
			//
			// _textLibraryName
			//
			this._textLibraryName.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._textLibraryName, null);
			this.localizationExtender1.SetLocalizationComment(this._textLibraryName, null);
			this.localizationExtender1.SetLocalizingId(this._textLibraryName, "NewCollectionDialog._textLibraryName");
			this._textLibraryName.Location = new System.Drawing.Point(28, 137);
			this._textLibraryName.Name = "_textLibraryName";
			this._textLibraryName.Size = new System.Drawing.Size(146, 23);
			this._textLibraryName.TabIndex = 1;
			this._textLibraryName.TextChanged += new System.EventHandler(this._textLibraryName_TextChanged);
			//
			// _pathLabel
			//
			this._pathLabel.AutoSize = true;
			this._pathLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._pathLabel.ForeColor = System.Drawing.SystemColors.ControlDark;
			this.localizationExtender1.SetLocalizableToolTip(this._pathLabel, null);
			this.localizationExtender1.SetLocalizationComment(this._pathLabel, null);
			this.localizationExtender1.SetLocalizationPriority(this._pathLabel, Localization.LocalizationPriority.InternalUseOnly);
			this.localizationExtender1.SetLocalizingId(this._pathLabel, "NewCollectionDialog._pathLabel");
			this._pathLabel.Location = new System.Drawing.Point(29, 163);
			this._pathLabel.Name = "_pathLabel";
			this._pathLabel.Size = new System.Drawing.Size(52, 15);
			this._pathLabel.TabIndex = 4;
			this._pathLabel.Text = "pathInfo";
			//
			// _whatLanguageLabel
			//
			this._whatLanguageLabel.AutoSize = true;
			this._whatLanguageLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._whatLanguageLabel, null);
			this.localizationExtender1.SetLocalizationComment(this._whatLanguageLabel, null);
			this.localizationExtender1.SetLocalizingId(this._whatLanguageLabel, "NewCollectionDialog.label2");
			this._whatLanguageLabel.Location = new System.Drawing.Point(25, 28);
			this._whatLanguageLabel.Name = "_whatLanguageLabel";
			this._whatLanguageLabel.Size = new System.Drawing.Size(267, 15);
			this._whatLanguageLabel.TabIndex = 5;
			this._whatLanguageLabel.Text = "What language are you going to make books for?";
			//
			// _chooseLanguageButton
			//
			this._chooseLanguageButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._chooseLanguageButton, null);
			this.localizationExtender1.SetLocalizationComment(this._chooseLanguageButton, null);
			this.localizationExtender1.SetLocalizingId(this._chooseLanguageButton, "NewCollectionDialog._chooseLanguageButton");
			this._chooseLanguageButton.Location = new System.Drawing.Point(28, 58);
			this._chooseLanguageButton.Name = "_chooseLanguageButton";
			this._chooseLanguageButton.Size = new System.Drawing.Size(122, 23);
			this._chooseLanguageButton.TabIndex = 0;
			this._chooseLanguageButton.Text = "Choose &Language...";
			this._chooseLanguageButton.UseVisualStyleBackColor = true;
			this._chooseLanguageButton.Click += new System.EventHandler(this._chooseLanguageButton_Click);
			//
			// _languageInfoLabel
			//
			this._languageInfoLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
			| System.Windows.Forms.AnchorStyles.Right)));
			this._languageInfoLabel.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this._languageInfoLabel.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.localizationExtender1.SetLocalizableToolTip(this._languageInfoLabel, null);
			this.localizationExtender1.SetLocalizationComment(this._languageInfoLabel, null);
			this.localizationExtender1.SetLocalizingId(this._languageInfoLabel, "NewCollectionDialog._languageInfoLabel");
			this._languageInfoLabel.Location = new System.Drawing.Point(168, 57);
			this._languageInfoLabel.Multiline = true;
			this._languageInfoLabel.Name = "_languageInfoLabel";
			this._languageInfoLabel.ReadOnly = true;
			this._languageInfoLabel.Size = new System.Drawing.Size(303, 24);
			this._languageInfoLabel.TabIndex = 8;
			this._languageInfoLabel.TabStop = false;
			//
			// _kindOfCollectionControl1
			//
			this.localizationExtender1.SetLocalizableToolTip(this._kindOfCollectionControl1, null);
			this.localizationExtender1.SetLocalizationComment(this._kindOfCollectionControl1, null);
			this.localizationExtender1.SetLocalizingId(this._kindOfCollectionControl1, "NewCollectionDialog.KidOfProjectControl");
			this._kindOfCollectionControl1.Location = new System.Drawing.Point(339, 18);
			this._kindOfCollectionControl1.Name = "_kindOfCollectionControl1";
			this._kindOfCollectionControl1.Size = new System.Drawing.Size(140, 292);
			this._kindOfCollectionControl1.TabIndex = 9;
			//
			// localizationExtender1
			//
			this.localizationExtender1.LocalizationManagerId = "Bloom";
			//
			// NewCollectionDialog
			//
			this.AcceptButton = this._okButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.AutoScroll = true;
			this.CancelButton = this.btnCancel;
			this.ClientSize = new System.Drawing.Size(491, 320);
			this.Controls.Add(this._kindOfCollectionControl1);
			this.Controls.Add(this._languageInfoLabel);
			this.Controls.Add(this._chooseLanguageButton);
			this.Controls.Add(this._whatLanguageLabel);
			this.Controls.Add(this._pathLabel);
			this.Controls.Add(this._textLibraryName);
			this.Controls.Add(this.btnCancel);
			this.Controls.Add(this._okButton);
			this.Controls.Add(this._nameCollectionLabel);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.localizationExtender1.SetLocalizableToolTip(this, null);
			this.localizationExtender1.SetLocalizationComment(this, null);
			this.localizationExtender1.SetLocalizingId(this, "NewCollectionDialog.WindowTitle");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "NewCollectionDialog";
			this.Text = "New Collection";
			((System.ComponentModel.ISupportInitialize)(this.localizationExtender1)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		protected System.Windows.Forms.Label _nameCollectionLabel;
		protected System.Windows.Forms.Button _okButton;
		protected System.Windows.Forms.Button btnCancel;
		protected System.Windows.Forms.TextBox _textLibraryName;
		protected System.Windows.Forms.Label _pathLabel;
		protected System.Windows.Forms.Label _whatLanguageLabel;
		private System.Windows.Forms.Button _chooseLanguageButton;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel _languageInfoLabel;
		private KindOfCollectionControl _kindOfCollectionControl1;
		private Localization.UI.LocalizationExtender localizationExtender1;
		private System.ComponentModel.IContainer components;
	}
}