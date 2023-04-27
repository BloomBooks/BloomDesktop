using System;
using System.ComponentModel;
using System.ComponentModel.Design;
using System.IO;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.Properties;
using DesktopAnalytics;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.CollectionCreating
{
	public partial class NewCollectionWizard : SIL.Windows.Forms.Miscellaneous.FormForUsingPortableClipboard
	{
		public Action UiLanguageChanged;

		private NewCollectionSettings _collectionInfo;

		public static string CreateNewCollection(Action uiLanguageChangedAction)
		{
			bool showNewCollectionWizard = Settings.Default.MruProjects.Latest == null;
			using (var dlg = new NewCollectionWizard(showNewCollectionWizard))
			{
				dlg.UiLanguageChanged = uiLanguageChangedAction;
				dlg.ShowInTaskbar = showNewCollectionWizard;//if we're at this stage, there isn't a bloom icon there already.
				if (DialogResult.OK != dlg.ShowDialog())
				{
					return null;
				}
				//review: this is a bit weird... we clone it instead of just using it just because this code path
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

			Icon = Resources.BloomIcon;

			if (ReallyDesignMode)
				return;

			_collectionInfo = new NewCollectionSettings();
			// WizardPage.AllowNext defaults to true, but we want to force a language choice.
			_vernacularLanguagePage.AllowNext = false;

			_languageLocationPage.Tag = _languageLocationControl;
			_languageLocationControl.Init(_collectionInfo);

			_collectionNamePage.Tag = _collectionNameControl;
			_collectionNameControl.Init(SetNextButtonState, _collectionInfo, DefaultParentDirectoryForCollections);

			_vernacularLanguagePage.Tag = _vernacularLanguageIdControl;
			_vernacularLanguageIdControl.Init(SetNextButtonState, _collectionInfo);

			_welcomePage.Suppress = !showWelcome;

			//The L10NSharpExtender and this wizard don't get along (they conspire to crash Visual Studio with a stack overflow)
			//so we do all of this by hand
			SetLocalizedStrings();

			_wizardControl.AfterInitialization();
		}

		public void ChangeLocalization()
		{
			// By the time we get here, L10NSharp has already changed the UI language underneath us, so
			// all we need to do is redisplay everything.  We don't even need to know the new UI language!
			SetLocalizedStrings();
			if (UiLanguageChanged != null)
				UiLanguageChanged();
		}

		private void SetLocalizedStrings()
		{
			Text = LocalizationManager.GetString("NewCollectionWizard.NewCollectionWindowTitle", "Create New Bloom Collection");
			_welcomePage.Text = LocalizationManager.GetString("NewCollectionWizard.WelcomePage", "Welcome To Bloom!");
			_collectionNamePage.Text = LocalizationManager.GetString("NewCollectionWizard.CollectionName", "Collection Name");
			_collectionNameProblemPage.Text = LocalizationManager.GetString("NewCollectionWizard.CollectionNameProblem", "Collection Name Problem");
			_languageLocationPage.Text = LocalizationManager.GetString("NewCollectionWizard.LocationPage", "Give Language Location");
			_languageFontPage.Text = LocalizationManager.GetString("NewCollectionWizard.FontAndScriptPage", "Font and Script");
			_vernacularLanguagePage.Text = LocalizationManager.GetString("NewCollectionWizard.ChooseLanguagePage", "Choose the main language for this collection.");
			_finishPage.Text = LocalizationManager.GetString("NewCollectionWizard.FinishPage", "Ready To Create New Collection");
			_wizardControl.NextButtonText = LocalizationManager.GetString("Common.Next", "&Next", "Used for the Next button in wizards, like that used for making a New Collection");
			_wizardControl.FinishButtonText = LocalizationManager.GetString("Common.Finish", "&Finish", "Used for the Finish button in wizards, like that used for making a New Collection");
			_wizardControl.UpdateNextAndFinishButtonText();
			_wizardControl.CancelButtonText = LocalizationManager.GetString("Common.CancelButton", "&Cancel");

			var one = LocalizationManager.GetString("NewCollectionWizard.WelcomePage.WelcomeLine1", "You are almost ready to start making books.");
			var two = LocalizationManager.GetString("NewCollectionWizard.WelcomePage.WelcomeLine2.V2", "In order to keep things simple and organized, Bloom keeps all the books you make in one or more 'Collections'. The first thing we need to do is make one for you.");
			var three = LocalizationManager.GetString("NewCollectionWizard.WelcomePage.WelcomeLine3", "Click 'Next' to get started.");
			_welcomeHtml.Text = one + Environment.NewLine + two + Environment.NewLine + three;
		}

		protected bool ReallyDesignMode
		{
			get
			{
				return (base.DesignMode || GetService(typeof(IDesignerHost)) != null) ||
					(LicenseManager.UsageMode == LicenseUsageMode.Designtime);
			}
		}

		public void SetNextButtonState(UserControl caller, bool enabled)
		{
			_wizardControl.SelectedPage.AllowNext = enabled;

			if (caller is LanguageIdControl)
			{
				var pattern = LocalizationManager.GetString("NewCollectionWizard.NewBookPattern", "{0} Books", "The {0} is replaced by the name of the language.");
				// GetPathForNewSettings uses Path.Combine which can fail with certain characters that are illegal in paths, but not in language names.
				// The characters we ran into were two pipe characters ("|") at the front of the language name.
				var tentativeCollectionName = string.Format(pattern, _collectionInfo.Language1.Name);
				var sanitizedCollectionName = tentativeCollectionName.SanitizePath('.');
				_collectionInfo.PathToSettingsFile = CollectionSettings.GetPathForNewSettings(DefaultParentDirectoryForCollections, sanitizedCollectionName);

				// An earlier version went direct to finish if the proposed name was OK (unless DefaultCollectionPathWouldHaveProblems || (tentativeCollectionName != sanitizedCollectionName))
				// but per BL-2649 we now want to always let the user check the name.
				_languageLocationPage.NextPage = _collectionNamePage;
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
						|| RobustFile.Exists(_collectionInfo.PathToSettingsFile);
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
			IPageControl control = _wizardControl.SelectedPage.Tag as IPageControl;
			if(control!=null)
				control.NowVisible();
		}

		private void OnFinish(object sender, EventArgs e)
		{
			DialogResult = DialogResult.OK;

			// Collect the data from the Font and Script page.
			_collectionInfo.Language1.FontName = _fontDetails.SelectedFont;
			// refactoring note: this is not needed, this will be set to zero by default:_collectionInfo.Language1LineHeight = new decimal(0);
			if (_fontDetails.ExtraLineHeight)
			{
				// The LineHeight settings from the LanguageFontDetails control are in the current culture,
				// so we don't need to specify a culture in the TryParse.
				double height;
				if (double.TryParse(_fontDetails.LineHeight, out height))
					_collectionInfo.Language1.LineHeight = new decimal(height);
			}
			_collectionInfo.Language1.IsRightToLeft = _fontDetails.RightToLeft;
			_collectionInfo.Language1.BreaksLinesOnlyAtSpaces = false;

			//this both saves a step for the country with the most languages, but also helps get the order between en and tpi to what will be most useful
			if (_collectionInfo.Country == "Papua New Guinea")
			{
				_collectionInfo.Language2.Tag = "en";
				_collectionInfo.Language3.Tag = "tpi";
			}
			_collectionInfo.SetAnalyticsProperties();

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
			Close();
			_collectionInfo = null;
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

		private void _finishPage_Initialize(object sender, EventArgs e)
		{
			var format = LocalizationManager.GetString("NewCollectionWizard.FinishPageContent",
				"OK, that's all we need to get started with your new '{0}' collection.\r\nClick on the 'Finish' button.",
				"{0} is the name of the new collection");
			_finalMessage.Text = String.Format(format, Path.GetFileNameWithoutExtension(_collectionInfo.PathToSettingsFile));
		}
	}

	internal interface IPageControl
	{
		void NowVisible();
	}
}
