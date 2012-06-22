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
	public partial class OpenCreateCollectionsDialog : Form
	{
		public OpenCreateCollectionsDialog(MostRecentPathsList mruLibraryPaths)
		{
			InitializeComponent();
			_versionInfo.Text = Shell.GetVersionInfo();
			//_welcomeControl.TemplateLabel.ForeColor = Color.FromArgb(0x61, 0x94, 0x38);//0xa0, 0x3c, 0x50);
			_welcomeControl.TemplateButton.Image = Resources.library32x32;
			_welcomeControl.TemplateButton.Image.Tag = "testfrombloom";
			_welcomeControl.Init(mruLibraryPaths, DefaultParentDirectoryForLibraries(),
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

		private string DefaultParentDirectoryForLibraries()
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Bloom");
		}

		private NewCollectionInfo CreateNewLibrary()
		{
			using (var dlg = new NewCollectionWizard(DefaultParentDirectoryForLibraries()))
			{
				if (DialogResult.OK != dlg.ShowDialog())
				{
					return null;
				}
				return dlg.GetNewCollectionSettings();
			}
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
