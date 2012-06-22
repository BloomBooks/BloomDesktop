namespace Bloom.NewCollection
{
	partial class NewCollectionWizard
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(NewCollectionWizard));
			this.wizardControl1 = new AeroWizard.WizardControl();
			this._kindOfCollectionPage = new AeroWizard.WizardPage();
			this.kindOfCollectionControl1 = new Bloom.NewCollection.KindOfCollectionControl();
			this._vernacularLanguagePage = new AeroWizard.WizardPage();
			this.vernacularLanguageInfoControl = new Bloom.NewCollection.LanguageIdControl();
			this._finishPage = new AeroWizard.WizardPage();
			this._languageLocationPage = new AeroWizard.WizardPage();
			this._languageLocationControl = new Bloom.NewCollection.LanguageLocationControl();
			this._collectionNamePage = new AeroWizard.WizardPage();
			this._collectionNameControl = new Bloom.NewCollection.ProjectStorageControl();
			this._collectionNameProblemPage = new AeroWizard.WizardPage();
			((System.ComponentModel.ISupportInitialize)(this.wizardControl1)).BeginInit();
			this._kindOfCollectionPage.SuspendLayout();
			this._vernacularLanguagePage.SuspendLayout();
			this._languageLocationPage.SuspendLayout();
			this._collectionNamePage.SuspendLayout();
			this.SuspendLayout();
			// 
			// wizardControl1
			// 
			this.wizardControl1.Location = new System.Drawing.Point(0, 0);
			this.wizardControl1.Name = "wizardControl1";
			this.wizardControl1.Pages.Add(this._kindOfCollectionPage);
			this.wizardControl1.Pages.Add(this._languageLocationPage);
			this.wizardControl1.Pages.Add(this._vernacularLanguagePage);
			this.wizardControl1.Pages.Add(this._collectionNamePage);
			this.wizardControl1.Pages.Add(this._finishPage);
			this.wizardControl1.Pages.Add(this._collectionNameProblemPage);
			this.wizardControl1.Size = new System.Drawing.Size(684, 464);
			this.wizardControl1.TabIndex = 0;
			this.wizardControl1.Title = "Create New Bloom Collection";
			this.wizardControl1.TitleIcon = ((System.Drawing.Icon)(resources.GetObject("wizardControl1.TitleIcon")));
			this.wizardControl1.Cancelled += new System.EventHandler(this.OnCancel);
			this.wizardControl1.Finished += new System.EventHandler(this.OnFinish);
			this.wizardControl1.SelectedPageChanged += new System.EventHandler(this.OnSelectedPageChanged);
			// 
			// _kindOfCollectionPage
			// 
			this._kindOfCollectionPage.Controls.Add(this.kindOfCollectionControl1);
			this._kindOfCollectionPage.Name = "_kindOfCollectionPage";
			this._kindOfCollectionPage.NextPage = this._vernacularLanguagePage;
			this._kindOfCollectionPage.Size = new System.Drawing.Size(637, 310);
			this._kindOfCollectionPage.TabIndex = 0;
			this._kindOfCollectionPage.Text = "What Kind of Collection?";
			// 
			// kindOfCollectionControl1
			// 
			this.kindOfCollectionControl1.Location = new System.Drawing.Point(0, 3);
			this.kindOfCollectionControl1.Name = "kindOfCollectionControl1";
			this.kindOfCollectionControl1.Size = new System.Drawing.Size(608, 278);
			this.kindOfCollectionControl1.TabIndex = 0;
			// 
			// _vernacularLanguagePage
			// 
			this._vernacularLanguagePage.Controls.Add(this.vernacularLanguageInfoControl);
			this._vernacularLanguagePage.Name = "_vernacularLanguagePage";
			this._vernacularLanguagePage.NextPage = this._languageLocationPage;
			this._vernacularLanguagePage.Size = new System.Drawing.Size(637, 310);
			this._vernacularLanguagePage.TabIndex = 1;
			this._vernacularLanguagePage.Text = "Vernacular Language Information";
			// 
			// vernacularLanguageInfoControl
			// 
			this.vernacularLanguageInfoControl.Location = new System.Drawing.Point(3, 0);
			this.vernacularLanguageInfoControl.Name = "vernacularLanguageInfoControl";
			this.vernacularLanguageInfoControl.Size = new System.Drawing.Size(451, 425);
			this.vernacularLanguageInfoControl.TabIndex = 0;
			// 
			// _finishPage
			// 
			this._finishPage.IsFinishPage = true;
			this._finishPage.Name = "_finishPage";
			this._finishPage.Size = new System.Drawing.Size(637, 310);
			this._finishPage.TabIndex = 3;
			this._finishPage.Text = "Ready To Create New Collection";
			// 
			// _languageLocationPage
			// 
			this._languageLocationPage.Controls.Add(this._languageLocationControl);
			this._languageLocationPage.Name = "_languageLocationPage";
			this._languageLocationPage.NextPage = this._finishPage;
			this._languageLocationPage.Size = new System.Drawing.Size(637, 310);
			this._languageLocationPage.TabIndex = 5;
			this._languageLocationPage.Text = "Language Location";
			// 
			// _languageLocationControl
			// 
			this._languageLocationControl.Dock = System.Windows.Forms.DockStyle.Fill;
			this._languageLocationControl.Location = new System.Drawing.Point(0, 0);
			this._languageLocationControl.Name = "_languageLocationControl";
			this._languageLocationControl.Size = new System.Drawing.Size(637, 310);
			this._languageLocationControl.TabIndex = 0;
			// 
			// _collectionNamePage
			// 
			this._collectionNamePage.Controls.Add(this._collectionNameControl);
			this._collectionNamePage.Name = "_collectionNamePage";
			this._collectionNamePage.NextPage = this._finishPage;
			this._collectionNamePage.Size = new System.Drawing.Size(637, 310);
			this._collectionNamePage.TabIndex = 2;
			this._collectionNamePage.Text = "Project Name";
			// 
			// _collectionNameControl
			// 
			this._collectionNameControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._collectionNameControl.Location = new System.Drawing.Point(17, 13);
			this._collectionNameControl.Name = "_collectionNameControl";
			this._collectionNameControl.Size = new System.Drawing.Size(604, 247);
			this._collectionNameControl.TabIndex = 0;
			// 
			// _collectionNameProblemPage
			// 
			this._collectionNameProblemPage.Name = "_collectionNameProblemPage";
			this._collectionNameProblemPage.NextPage = this._finishPage;
			this._collectionNameProblemPage.Size = new System.Drawing.Size(637, 310);
			this._collectionNameProblemPage.TabIndex = 4;
			this._collectionNameProblemPage.Text = "Collection Name Problem";
			// 
			// NewCollectionWizard
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(684, 464);
			this.ControlBox = false;
			this.Controls.Add(this.wizardControl1);
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "NewCollectionWizard";
			this.ShowIcon = false;
			this.ShowInTaskbar = false;
			this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
			((System.ComponentModel.ISupportInitialize)(this.wizardControl1)).EndInit();
			this._kindOfCollectionPage.ResumeLayout(false);
			this._vernacularLanguagePage.ResumeLayout(false);
			this._languageLocationPage.ResumeLayout(false);
			this._collectionNamePage.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private AeroWizard.WizardControl wizardControl1;
		private AeroWizard.WizardPage _kindOfCollectionPage;
		private KindOfCollectionControl kindOfCollectionControl1;
		private AeroWizard.WizardPage _vernacularLanguagePage;
		private LanguageIdControl vernacularLanguageInfoControl;
		private AeroWizard.WizardPage _collectionNamePage;
		private ProjectStorageControl _collectionNameControl;
		private AeroWizard.WizardPage _finishPage;
		private AeroWizard.WizardPage _collectionNameProblemPage;
		private AeroWizard.WizardPage _languageLocationPage;
		private LanguageLocationControl _languageLocationControl;
	}
}
