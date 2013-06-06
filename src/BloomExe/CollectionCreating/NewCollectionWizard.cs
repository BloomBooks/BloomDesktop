using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.Properties;
using DesktopAnalytics;
using L10NSharp;
using Palaso.Reporting;

namespace Bloom.CollectionCreating
{
	/* !!!!!!!!!!!!!!!!!!!!!! ABOUT DESIGN-TIME USE OF THE WIZARD !!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
	 *
	 * The AeroWizard requires the full profile (not just client). So this is what I did:
	 * 1) switched bloom to the Full Profile
	 * 2) Used the stock AeroWizard to get things designed
	 * 3) Switch Bloom to Client Profile
	 * 4) Stripped AeroWizard of all system.design dependencies (deleting two classes, removing an attribute linking the wizard to those classes)
	 * 5) Referenced that stripped down version.
	 *
	 * However, when I tried with the latest AeroWizard, I got an error of a "stack imbalance" on a native method call. So
	 * I went back to the version I had before. Looking forward to ditching this for the pure .net version we've got coming for the Linux port.
	 */
	public partial class NewCollectionWizard : Form
	{
		private NewCollectionSettings _collectionInfo;

		public static string CreateNewCollection()
		{
			bool showNewCollectionWizard = Settings.Default.MruProjects.Latest == null;
			using (var dlg = new NewCollectionWizard(showNewCollectionWizard))
			{
				dlg.ShowInTaskbar = showNewCollectionWizard;//if we're at this stage, there isn't a bloom icon there already.
				if (DialogResult.OK != dlg.ShowDialog())
				{
					return null;
				}
				//review: this is a bit weird... we clone it instead of just using it just becuase this code path
				//can handle creating the path from scratch
				return new CollectionSettings(dlg.GetNewCollectionSettings()).SettingsFilePath;
			}
		}

		public static string DefaultParentDirectoryForCollections
		{
			get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bloom"); }
		}


		public NewCollectionWizard(bool showWelcome)
		{
			InitializeComponent();

			if (ReallyDesignMode)
				return;

			_collectionInfo = new NewCollectionSettings();
			_kindOfCollectionPage.Tag = kindOfCollectionControl1;
			kindOfCollectionControl1.Init(SetNextButtonState, _collectionInfo);

			_languageLocationPage.Tag = _languageLocationControl;
			_languageLocationControl.Init(_collectionInfo);

			_collectionNamePage.Tag = _collectionNameControl;
			_collectionNameControl.Init(SetNextButtonState, _collectionInfo, DefaultParentDirectoryForCollections);

			_vernacularLanguagePage.Tag = _vernacularLanguageIdControl;
			_vernacularLanguageIdControl.Init(SetNextButtonState, _collectionInfo);

			_welcomePage.Suppress = !showWelcome;

			//The L10NSharpExtender and this wizard don't get along (they conspire to crash Visual Studio with a stack overflow)
			//so we do all of this by hand
			var chooser = new Button();// new L10NSharp.UI.UILanguageComboBox() { ShowOnlyLanguagesHavingLocalizations = false };
			chooser.Location = new Point(100,100);
			chooser.Size= new Size(50,50);
			chooser.Visible = true;
			chooser.Anchor = AnchorStyles.Top | AnchorStyles.Right;
			//chooser.SelectedValueChanged += new EventHandler(chooser_SelectedValueChanged);
			wizardControl1.Controls.Add(chooser);

			SetLocalizedStrings();
	   }

		void chooser_SelectedValueChanged(object sender, EventArgs e)
		{
			SetLocalizedStrings();
		}

		private void SetLocalizedStrings()
		{
			this.wizardControl1.Title = LocalizationManager.GetString("NewCollectionWizard.NewCollectionWindowTitle",
																	  "Create New Bloom Collection");
			this._welcomePage.Text = LocalizationManager.GetString("NewCollectionWizard.WelcomePage",
																   "Welcome To Bloom!");
			this._kindOfCollectionPage.Text = LocalizationManager.GetString("NewCollectionWizard.KindOfCollectionPage",
																			"Choose the Collection Type");
			this._languageLocationPage.Text = LocalizationManager.GetString("NewCollectionWizard.LocationPage",
																			"Give Language Location");
			this._vernacularLanguagePage.Text = LocalizationManager.GetString("NewCollectionWizard.ChooseLanguagePage",
																			  "Choose the Main Language For This Collection");
			this._finishPage.Text = LocalizationManager.GetString("NewCollectionWizard.FinishPage",
																  "Ready To Create New Collection");
			wizardControl1.NextButtonText = LocalizationManager.GetString("Common.Next", "&Next",
																		  "Used for the Next button in wizards, like that used for making a New Collection");
			wizardControl1.FinishButtonText = LocalizationManager.GetString("Common.Finish", "&Finish",
																			"Used for the Finish button in wizards, like that used for making a New Collection");

			var one = L10NSharp.LocalizationManager.GetString("NewCollectionWizard.WelcomePage.WelcomeLine1",
																 "You are almost ready to start making books.");
			var two = L10NSharp.LocalizationManager.GetString("NewCollectionWizard.WelcomePage.WelcomeLine2",
																 "In order to keep things simple and organized, Bloom keeps all the books you make in one or more <i>Collections</i>. So the first thing we need to do is make one for you.");
			var three = L10NSharp.LocalizationManager.GetString("NewCollectionWizard.WelcomePage.WelcomeLine3",
																   "Click 'Next' to get started.");
			_welcomeHtml.HTML = one + "<br/>" + two + "<br/>" + three;
		}

		protected new bool ReallyDesignMode
		{
			get
			{
				return (base.DesignMode || GetService(typeof(IDesignerHost)) != null) ||
					(LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			}
		}

		public void SetNextButtonState(UserControl caller, bool enabled)
		{
			wizardControl1.SelectedPage.AllowNext = enabled;

			if (caller is KindOfCollectionControl)
			{
				_kindOfCollectionPage.NextPage = _collectionInfo.IsSourceCollection
													? _collectionNamePage
													: _vernacularLanguagePage;

				if(_collectionInfo.IsSourceCollection)
				{
					_collectionInfo.Language1Iso639Code = "en";
				}
			}

			if (caller is LanguageIdControl)
			{
				var pattern = L10NSharp.LocalizationManager.GetString("NewCollectionWizard.NewBookPattern", "{0} Books", "The {0} is replaced by the name of the language.");
				_collectionInfo.PathToSettingsFile = CollectionSettings.GetPathForNewSettings(DefaultParentDirectoryForCollections, string.Format(pattern, _collectionInfo.Language1Name));
				//_collectionInfo.CollectionName = ;


				_languageLocationPage.NextPage = DefaultCollectionPathWouldHaveProblems
													? _collectionNamePage	//go ahead to the language location page for now, but then divert to the page
																		//we use for fixing up the name
													: _finishPage;
			}
		}

		private bool DefaultCollectionPathWouldHaveProblems
		{
			get
			{
				try
				{
					return Path.GetFileName(_collectionInfo.PathToSettingsFile).IndexOfAny(Path.GetInvalidFileNameChars()) > -1
						|| Directory.Exists(_collectionInfo.PathToSettingsFile)
						|| File.Exists(_collectionInfo.PathToSettingsFile);
				}
				catch (Exception)
				{
					return true;
				}
			}
		}

		public NewCollectionSettings GetNewCollectionSettings()
		{
			return _collectionInfo;
		}

		private void OnSelectedPageChanged(object sender, EventArgs e)
		{
			IPageControl control = wizardControl1.SelectedPage.Tag as IPageControl;
			if(control!=null)
				control.NowVisible();
		}

		private void OnFinish(object sender, EventArgs e)
		{
			DialogResult = DialogResult.OK;

			Logger.WriteEvent("Finshed New Collection Wizard");
			if (_collectionInfo.IsSourceCollection)
				Analytics.Track("Created New Source Collection");
			else
				Analytics.Track("Create New Vernacular Collection");
			Close();
		}


		private void OnCancel(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			_collectionInfo = null;
			Close();
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == Keys.Escape)
			{
				OnCancel(this, null);
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		private void _languageLocationControl_Load(object sender, EventArgs e)
		{

		}

		private void _finishPage_Initialize(object sender, AeroWizard.WizardPageInitEventArgs e)
		{
			var pattern = LocalizationManager.GetString("NewCollectionWizard.FinishPage","OK, that's all we need to get started with your new '{0}' collection.\r\nClick on the 'Finish' button.");
			betterLabel1.Text = String.Format(pattern, Path.GetFileNameWithoutExtension(_collectionInfo.PathToSettingsFile));
		}
	}

	internal interface IPageControl
	{
		void NowVisible();
	}
}
