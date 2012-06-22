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
		private NewCollectionInfo _collectionInfo;

		public NewCollectionWizard(string pathToNewLibraryDirectory)
		{
			_pathToNewLibraryDirectory = pathToNewLibraryDirectory;
			InitializeComponent();
			_collectionInfo = new NewCollectionInfo();
			_kindOfCollectionPage.Tag = kindOfCollectionControl1;
			kindOfCollectionControl1.Init(SetNextButtonState, _collectionInfo);

			_languageLocationPage.Tag = _languageLocationControl;
			_languageLocationControl.Init(_collectionInfo);

			_collectionNamePage.Tag = _collectionNameControl;
			_collectionNameControl.Init(SetNextButtonState, _collectionInfo, pathToNewLibraryDirectory);

			_vernacularLanguagePage.Tag = vernacularLanguageInfoControl;
			vernacularLanguageInfoControl.Init(SetNextButtonState, _collectionInfo);
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
				_collectionInfo.PathToSettingsFile = CollectionSettings.GetPathForNewSettings(_pathToNewLibraryDirectory, _collectionInfo.LanguageName);


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

		public NewCollectionInfo GetNewCollectionSettings()
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

	}

	internal interface IPageControl
	{
		void NowVisible();
	}
}
