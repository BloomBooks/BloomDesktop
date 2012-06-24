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
			this._welcomePage = new AeroWizard.WizardPage();
			this._kindOfCollectionPage = new AeroWizard.WizardPage();
			this._vernacularLanguagePage = new AeroWizard.WizardPage();
			this._languageLocationPage = new AeroWizard.WizardPage();
			this._finishPage = new AeroWizard.WizardPage();
			this.betterLabel1 = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this._collectionNamePage = new AeroWizard.WizardPage();
			this._collectionNameProblemPage = new AeroWizard.WizardPage();
			this._welcomeHtml = new Bloom.HtmlLabel();
			this.kindOfCollectionControl1 = new Bloom.NewCollection.KindOfCollectionControl();
			this._vernacularLanguageIdControl = new Bloom.NewCollection.LanguageIdControl();
			this._languageLocationControl = new Bloom.NewCollection.LanguageLocationControl();
			this._collectionNameControl = new Bloom.NewCollection.CollectionNameControl();
			((System.ComponentModel.ISupportInitialize)(this.wizardControl1)).BeginInit();
			this._welcomePage.SuspendLayout();
			this._kindOfCollectionPage.SuspendLayout();
			this._vernacularLanguagePage.SuspendLayout();
			this._languageLocationPage.SuspendLayout();
			this._finishPage.SuspendLayout();
			this._collectionNamePage.SuspendLayout();
			this.SuspendLayout();
			// 
			// wizardControl1
			// 
			this.wizardControl1.Location = new System.Drawing.Point(0, 0);
			this.wizardControl1.Name = "wizardControl1";
			this.wizardControl1.Pages.Add(this._welcomePage);
			this.wizardControl1.Pages.Add(this._kindOfCollectionPage);
			this.wizardControl1.Pages.Add(this._vernacularLanguagePage);
			this.wizardControl1.Pages.Add(this._languageLocationPage);
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
			// _welcomePage
			// 
			this._welcomePage.Controls.Add(this._welcomeHtml);
			this._welcomePage.Name = "_welcomePage";
			this._welcomePage.Size = new System.Drawing.Size(637, 310);
			this._welcomePage.TabIndex = 6;
			this._welcomePage.Text = "Welcome To Bloom!";
			// 
			// _kindOfCollectionPage
			// 
			this._kindOfCollectionPage.Controls.Add(this.kindOfCollectionControl1);
			this._kindOfCollectionPage.Name = "_kindOfCollectionPage";
			this._kindOfCollectionPage.NextPage = this._vernacularLanguagePage;
			this._kindOfCollectionPage.Size = new System.Drawing.Size(637, 310);
			this._kindOfCollectionPage.TabIndex = 0;
			this._kindOfCollectionPage.Text = "Choose the Collection Type";
			// 
			// _vernacularLanguagePage
			// 
			this._vernacularLanguagePage.Controls.Add(this._vernacularLanguageIdControl);
			this._vernacularLanguagePage.Name = "_vernacularLanguagePage";
			this._vernacularLanguagePage.NextPage = this._languageLocationPage;
			this._vernacularLanguagePage.Size = new System.Drawing.Size(637, 310);
			this._vernacularLanguagePage.TabIndex = 1;
			this._vernacularLanguagePage.Text = "Choose the Main Language For This Collection";
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
			this._finishPage.Controls.Add(this.betterLabel1);
			this._finishPage.IsFinishPage = true;
			this._finishPage.Name = "_finishPage";
			this._finishPage.Size = new System.Drawing.Size(637, 310);
			this._finishPage.TabIndex = 3;
			this._finishPage.Text = "Ready To Create New Collection";
			this._finishPage.Initialize += new System.EventHandler<AeroWizard.WizardPageInitEventArgs>(this._finishPage_Initialize);
			// 
			// betterLabel1
			// 
			this.betterLabel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this.betterLabel1.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.betterLabel1.Enabled = false;
			this.betterLabel1.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.betterLabel1.Location = new System.Drawing.Point(3, 3);
			this.betterLabel1.Multiline = true;
			this.betterLabel1.Name = "betterLabel1";
			this.betterLabel1.ReadOnly = true;
			this.betterLabel1.Size = new System.Drawing.Size(631, 304);
			this.betterLabel1.TabIndex = 0;
			this.betterLabel1.TabStop = false;
			this.betterLabel1.Text = "<Text>";
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
			this._welcomeHtml.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
			this._welcomeHtml.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this._welcomeHtml.HTML = "hi there";
			this._welcomeHtml.Location = new System.Drawing.Point(0, 0);
			this._welcomeHtml.Margin = new System.Windows.Forms.Padding(0);
			this._welcomeHtml.Name = "_welcomeHtml";
			this._welcomeHtml.Size = new System.Drawing.Size(637, 310);
			this._welcomeHtml.TabIndex = 1;
			// 
			// kindOfCollectionControl1
			// 
			this.kindOfCollectionControl1.Location = new System.Drawing.Point(0, 3);
			this.kindOfCollectionControl1.Name = "kindOfCollectionControl1";
			this.kindOfCollectionControl1.Size = new System.Drawing.Size(608, 278);
			this.kindOfCollectionControl1.TabIndex = 0;
			// 
			// _vernacularLanguageIdControl
			// 
			this._vernacularLanguageIdControl.Anchor = System.Windows.Forms.AnchorStyles.None;
			this._vernacularLanguageIdControl.Location = new System.Drawing.Point(0, 3);
			this._vernacularLanguageIdControl.Name = "_vernacularLanguageIdControl";
			this._vernacularLanguageIdControl.Size = new System.Drawing.Size(634, 304);
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
			this._collectionNameControl.Location = new System.Drawing.Point(0, 0);
			this._collectionNameControl.Name = "_collectionNameControl";
			this._collectionNameControl.Size = new System.Drawing.Size(619, 307);
			this._collectionNameControl.TabIndex = 0;
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
			this._welcomePage.ResumeLayout(false);
			this._kindOfCollectionPage.ResumeLayout(false);
			this._vernacularLanguagePage.ResumeLayout(false);
			this._languageLocationPage.ResumeLayout(false);
			this._finishPage.ResumeLayout(false);
			this._finishPage.PerformLayout();
			this._collectionNamePage.ResumeLayout(false);
			this.ResumeLayout(false);

		}

		#endregion

		private AeroWizard.WizardControl wizardControl1;
		private AeroWizard.WizardPage _kindOfCollectionPage;
		private KindOfCollectionControl kindOfCollectionControl1;
		private AeroWizard.WizardPage _vernacularLanguagePage;
		private LanguageIdControl _vernacularLanguageIdControl;
		private AeroWizard.WizardPage _collectionNamePage;
		private CollectionNameControl _collectionNameControl;
		private AeroWizard.WizardPage _finishPage;
		private AeroWizard.WizardPage _collectionNameProblemPage;
		private AeroWizard.WizardPage _languageLocationPage;
		private LanguageLocationControl _languageLocationControl;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel betterLabel1;
		private AeroWizard.WizardPage _welcomePage;
		private HtmlLabel _welcomeHtml;
	}
}
