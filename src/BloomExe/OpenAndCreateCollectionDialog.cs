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
	public partial class OpenAndCreateCollectionDialog : Form
	{
		public OpenAndCreateCollectionDialog(MostRecentPathsList mruLibraryPaths)
		{
			InitializeComponent();
			_versionInfo.Text = Shell.GetVersionInfo();
			//_welcomeControl.TemplateLabel.ForeColor = Color.FromArgb(0x61, 0x94, 0x38);//0xa0, 0x3c, 0x50);
			_openAndCreateControl.TemplateButton.Image = Resources.library32x32;
			_openAndCreateControl.TemplateButton.Image.Tag = "testfrombloom";
			_openAndCreateControl.Init(mruLibraryPaths, DefaultParentDirectoryForLibraries(),
				"Create new collection",
				"Browse for other collections on this computer...",
				"Bloom Collections|*.bloomLibrary;*.bloomCollection",
				dir=>true,
				CreateNewCollection);

			_openAndCreateControl.DoneChoosingOrCreatingLibrary += (x, y) =>
																{
																	DialogResult = DialogResult.OK;
																	Close();
																};
		}

		private static string DefaultParentDirectoryForLibraries()
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bloom");
		}

		public static string CreateNewCollection()
		{
			bool showWelcomePage = Settings.Default.MruProjects.Latest == null;
			using (var dlg = new NewCollectionWizard(showWelcomePage, DefaultParentDirectoryForLibraries()))
			{
				dlg.ShowInTaskbar = showWelcomePage;//if we're at this stage, there isn't a bloom icon there already.
				if (DialogResult.OK != dlg.ShowDialog())
				{
					return null;
				}
				//review: this is a bit weird... we clone it instead of just using it just becuase this code path
				//can handle creating the path from scratch
				return new CollectionSettings(dlg.GetNewCollectionSettings()).SettingsFilePath;
			}
		}

		public string SelectedPath
		{
			get { return _openAndCreateControl.SelectedPath; }
		}

		private void _broughtToYouBy_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			System.Diagnostics.Process.Start("http://bloom.palaso.org");
		}

	}
}
