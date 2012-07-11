using System;
using System.IO;
using System.Windows.Forms;
using Bloom.Collection;
using Palaso.Reporting;

namespace Bloom.NewCollection
{
	public partial class NewCollectionWizard : Form
	{
		private readonly string _pathToNewLibraryDirectory;
		private NewCollectionSettings _collectionInfo;

		public NewCollectionWizard(bool showWelcome, string pathToNewLibraryDirectory)
		{
			_pathToNewLibraryDirectory = pathToNewLibraryDirectory;
			InitializeComponent();
			_collectionInfo = new NewCollectionSettings();
			_kindOfCollectionPage.Tag = kindOfCollectionControl1;
			kindOfCollectionControl1.Init(SetNextButtonState, _collectionInfo);

			_languageLocationPage.Tag = _languageLocationControl;
			_languageLocationControl.Init(_collectionInfo);

			_collectionNamePage.Tag = _collectionNameControl;
			_collectionNameControl.Init(SetNextButtonState, _collectionInfo, pathToNewLibraryDirectory);

			_vernacularLanguagePage.Tag = _vernacularLanguageIdControl;
			_vernacularLanguageIdControl.Init(SetNextButtonState, _collectionInfo);

			_welcomePage.Suppress = !showWelcome;
			if (showWelcome)
				_welcomeHtml.HTML = File.ReadAllText(BloomFileLocator.GetFileDistributedWithApplication("welcome.htm"));
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
				var pattern = Localization.LocalizationManager.GetString("NewBookPattern", "{0} Books", "The {0} is replaced by the name of the language.");
				_collectionInfo.PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_pathToNewLibraryDirectory, string.Format(pattern,_collectionInfo.Language1Name));
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

			if (_collectionInfo.IsSourceCollection)
				UsageReporter.SendNavigationNotice("NewSourceCollection");
			else
				UsageReporter.SendNavigationNotice("NewVernacularCollection");
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
			var pattern = "OK, that's all we need to get started with your new '{0}' collection.\r\nClick on the 'Finish' button.";
			betterLabel1.Text = String.Format(pattern, Path.GetFileNameWithoutExtension(_collectionInfo.PathToSettingsFile));
		}
	}

	internal interface IPageControl
	{
		void NowVisible();
	}
}
