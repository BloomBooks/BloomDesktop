using System.Windows.Forms;
using Bloom.MiscUI;
using Bloom.Wizard;

namespace Bloom.CollectionCreating
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
			if (disposing)
				_wizardControl.Dispose();
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
			this._wizardControl = new Bloom.Wizard.WizardAdapterControl();
            this._welcomePage = new Bloom.Wizard.WizardAdapterPage();
            this._vernacularLanguagePage = new Bloom.Wizard.WizardAdapterPage();
            this._languageLocationPage = new Bloom.Wizard.WizardAdapterPage();
			this._languageFontPage = new Bloom.Wizard.WizardAdapterPage();
            this._finishPage = new Bloom.Wizard.WizardAdapterPage();
			this._finalMessage = new SIL.Windows.Forms.Widgets.BetterLabel();
            this._collectionNamePage = new Bloom.Wizard.WizardAdapterPage();
            this._collectionNameProblemPage = new Bloom.Wizard.WizardAdapterPage();
			this._welcomeHtml = new SIL.Windows.Forms.Widgets.BetterLabel();
			this._vernacularLanguageIdControl = new Bloom.CollectionCreating.LanguageIdControl();
			this._fontDetails = new Bloom.MiscUI.LanguageFontDetails();
			this._languageLocationControl = new Bloom.CollectionCreating.LanguageLocationControl();
			this._collectionNameControl = new Bloom.CollectionCreating.CollectionNameControl();
			((System.ComponentModel.ISupportInitialize)(this._wizardControl)).BeginInit();
			this._welcomePage.SuspendLayout();
			this._vernacularLanguagePage.SuspendLayout();
			this._languageLocationPage.SuspendLayout();
			this._languageFontPage.SuspendLayout();
			this._finishPage.SuspendLayout();
			this._collectionNamePage.SuspendLayout();
			this.SuspendLayout();
			//
			// _wizardControl
			//
			this._wizardControl.Location = new System.Drawing.Point(0, 0);
			this._wizardControl.Name = "_wizardControl";
			this._wizardControl.Pages.Add(this._welcomePage);
			this._wizardControl.Pages.Add(this._vernacularLanguagePage);
			this._wizardControl.Pages.Add(this._languageLocationPage);
			this._wizardControl.Pages.Add(this._languageFontPage);
			this._wizardControl.Pages.Add(this._collectionNamePage);
			this._wizardControl.Pages.Add(this._finishPage);
			this._wizardControl.Pages.Add(this._collectionNameProblemPage);
			this._wizardControl.Size = new System.Drawing.Size(759, 464);
			this._wizardControl.TabIndex = 0;
			this._wizardControl.Title = "Create New Bloom Collection";
			this._wizardControl.TitleIcon = ((System.Drawing.Icon)(resources.GetObject("_wizardControl.TitleIcon")));
			this._wizardControl.Cancelled += new System.EventHandler(this.OnCancel);
			this._wizardControl.Finished += new System.EventHandler(this.OnFinish);
			this._wizardControl.SelectedPageChanged += new System.EventHandler(this.OnSelectedPageChanged);
			//
			// _welcomePage
			//
			this._welcomePage.Controls.Add(this._welcomeHtml);
			this._welcomePage.Name = "_welcomePage";
			this._welcomePage.Size = new System.Drawing.Size(846, 309);
			this._welcomePage.TabIndex = 6;
			this._welcomePage.Text = "Welcome To Bloom!";
			//
			// _vernacularLanguagePage
			//
			this._vernacularLanguagePage.Controls.Add(this._vernacularLanguageIdControl);
			this._vernacularLanguagePage.Name = "_vernacularLanguagePage";
			this._vernacularLanguagePage.NextPage = this._languageFontPage;
			this._vernacularLanguagePage.Size = new System.Drawing.Size(712, 309);
			this._vernacularLanguagePage.TabIndex = 1;
			this._vernacularLanguagePage.Text = "Choose the main language for this collection.";
			//
			// _languageFontPage
			//
			this._languageFontPage.Controls.Add(this._fontDetails);
			this._languageFontPage.Name = "_languageFontPage";
			this._languageFontPage.NextPage = this._languageLocationPage;
			this._languageFontPage.Size = new System.Drawing.Size(637, 310);
			this._languageFontPage.TabIndex = 7;
			this._languageFontPage.Text = "Font and Script";
			//
			// _languageLocationPage
			//
			this._languageLocationPage.Controls.Add(this._languageLocationControl);
			this._languageLocationPage.Name = "_languageLocationPage";
			this._languageLocationPage.NextPage = this._finishPage;
			this._languageLocationPage.Size = new System.Drawing.Size(637, 310);
			this._languageLocationPage.TabIndex = 5;
			this._languageLocationPage.Text = "Give Language Location";
			//
			// _finishPage
			//
			this._finishPage.Controls.Add(this._finalMessage);
			this._finishPage.IsFinishPage = true;
			this._finishPage.Name = "_finishPage";
			this._finishPage.Size = new System.Drawing.Size(637, 310);
			this._finishPage.TabIndex = 3;
			this._finishPage.Text = "Ready To Create New Collection";
			this._finishPage.Initialize += this._finishPage_Initialize;
			//
			// betterLabel1
			//
			this._finalMessage.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
			this._finalMessage.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this._finalMessage.Enabled = false;
			this._finalMessage.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._finalMessage.Location = new System.Drawing.Point(120, 60);
			this._finalMessage.Name = "_finalMessage";
			this._finalMessage.Size = new System.Drawing.Size(631, 23);
			this._finalMessage.TabIndex = 0;
			this._finalMessage.TabStop = false;
			this._finalMessage.Text = "<Text>";
			//
			// _collectionNamePage
			//
			this._collectionNamePage.Controls.Add(this._collectionNameControl);
			this._collectionNamePage.Name = "_collectionNamePage";
			this._collectionNamePage.NextPage = this._finishPage;
			this._collectionNamePage.Size = new System.Drawing.Size(637, 310);
			this._collectionNamePage.TabIndex = 2;
			this._collectionNamePage.Text = "Collection Name";
			//
			// _collectionNameProblemPage
			//
			this._collectionNameProblemPage.Name = "_collectionNameProblemPage";
			this._collectionNameProblemPage.NextPage = this._finishPage;
			this._collectionNameProblemPage.Size = new System.Drawing.Size(637, 310);
			this._collectionNameProblemPage.TabIndex = 4;
			this._collectionNameProblemPage.Text = "Collection Name Problem";
			//
			// _welcomeHtml
			//
			this._welcomeHtml.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._welcomeHtml.Text = "hi there";
			this._welcomeHtml.Location = new System.Drawing.Point(0, 0);
			this._welcomeHtml.Margin = new System.Windows.Forms.Padding(0);
			this._welcomeHtml.Name = "_welcomeHtml";
			this._welcomeHtml.Size = new System.Drawing.Size(637, 310);
			this._welcomeHtml.TabIndex = 1;
			//
			// _vernacularLanguageIdControl
			//
			this._vernacularLanguageIdControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
			this._vernacularLanguageIdControl.Location = new System.Drawing.Point(0, 3);
			this._vernacularLanguageIdControl.Name = "_vernacularLanguageIdControl";
			this._vernacularLanguageIdControl.Size = new System.Drawing.Size(656, 267);
			this._vernacularLanguageIdControl.TabIndex = 0;
			//
			// _languageLocationControl
			//
			this._languageLocationControl.BackColor = System.Drawing.Color.White;
			this._languageLocationControl.Location = new System.Drawing.Point(0, 0);
			this._languageLocationControl.Name = "_languageLocationControl";
			this._languageLocationControl.Size = new System.Drawing.Size(615, 310);
			this._languageLocationControl.TabIndex = 0;
			this._languageLocationControl.Load += new System.EventHandler(this._languageLocationControl_Load);
			//
			// _collectionNameControl
			//
			this._collectionNameControl.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
			this._collectionNameControl.Location = new System.Drawing.Point(120, 60);
			this._collectionNameControl.Name = "_collectionNameControl";
			this._collectionNameControl.Size = new System.Drawing.Size(619, 307);
			this._collectionNameControl.TabIndex = 0;
			//
			// NewCollectionWizard
			//
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
			this.ClientSize = new System.Drawing.Size(759, 464);
			this.ControlBox = true;
			this.Controls.Add(this._wizardControl);
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "NewCollectionWizard";
			this.ShowIcon = true;
			this.ShowInTaskbar = false;
			this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
			((System.ComponentModel.ISupportInitialize)(this._wizardControl)).EndInit();
			this._welcomePage.ResumeLayout(false);
			this._vernacularLanguagePage.ResumeLayout(false);
			this._languageLocationPage.ResumeLayout(false);
			this._finishPage.ResumeLayout(false);
			this._finishPage.PerformLayout();
			this._collectionNamePage.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

        private Bloom.Wizard.WizardAdapterControl _wizardControl;
        private Bloom.Wizard.WizardAdapterPage _vernacularLanguagePage;
		private LanguageIdControl _vernacularLanguageIdControl;
        private Bloom.Wizard.WizardAdapterPage _collectionNamePage;
		private CollectionNameControl _collectionNameControl;
        private Bloom.Wizard.WizardAdapterPage _finishPage;
        private Bloom.Wizard.WizardAdapterPage _collectionNameProblemPage;
        private Bloom.Wizard.WizardAdapterPage _languageLocationPage;
		private Bloom.Wizard.WizardAdapterPage _languageFontPage;
		private Bloom.MiscUI.LanguageFontDetails _fontDetails;
		private LanguageLocationControl _languageLocationControl;
		private SIL.Windows.Forms.Widgets.BetterLabel _finalMessage;
        private Bloom.Wizard.WizardAdapterPage _welcomePage;
		private SIL.Windows.Forms.Widgets.BetterLabel _welcomeHtml;
	}
}
