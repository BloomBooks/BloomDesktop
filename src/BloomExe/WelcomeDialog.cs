using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.NewCollection;
using Bloom.Properties;
using Bloom.ToPalaso;
using Palaso.IO;

namespace Bloom
{
	public partial class WelcomeDialog : Form
	{
		public WelcomeDialog(MostRecentPathsList mruLibraryPaths)
		{
			InitializeComponent();
			_versionInfo.Text = Shell.GetVersionInfo();
			//_welcomeControl.TemplateLabel.ForeColor = Color.FromArgb(0x61, 0x94, 0x38);//0xa0, 0x3c, 0x50);
			_welcomeControl.TemplateButton.Image = Resources.library32x32;
			_welcomeControl.TemplateButton.Image.Tag = "testfrombloom";
			_welcomeControl.Init(mruLibraryPaths, DefaultParentDirectoryForLibrarys(),
				"Create new collection",
				"Browse for other collections on this computer...",
				"Bloom Collections|*.bloomLibrary;*.bloomCollection",
				dir=>true,
				CreateNewLibrary);

			_welcomeControl.DoneChoosingOrCreatingLibrary += (x, y) =>
																{
																	DialogResult = DialogResult.OK;
																	Close();
																};
		}

		private string DefaultParentDirectoryForLibrarys()
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bloom");
		}

		private NewCollectionInfo CreateNewLibrary()
		{
			NewCollectionDialog dlg = new NewCollectionDialog(DefaultParentDirectoryForLibrarys());
			if (DialogResult.OK != dlg.ShowDialog() || string.IsNullOrEmpty(dlg.PathToNewLibraryDirectory))
			{
				return null;
			}
			return new NewCollectionInfo()
					   {
						   PathToSettingsFile =
							   CollectionSettings.GetPathForNewSettings(dlg.PathToNewLibraryDirectory, dlg.LibraryName),
						   VernacularIso639Code = dlg.Iso639Code,
						   NationalLanguage1Iso639Code = "en",//TODO
						   LanguageName = dlg.LanguageName,
						   IsShellLibary = dlg.IsShellMakingLibrary
					   };
		}

		public string SelectedPath
		{
			get { return _welcomeControl.SelectedPath; }
		}

		private void _broughtToYouBy_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			System.Diagnostics.Process.Start("http://bloom.palaso.org");
		}

	}
}
