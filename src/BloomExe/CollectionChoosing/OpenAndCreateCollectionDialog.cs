using System.Windows.Forms;
using Bloom.CollectionCreating;
using Bloom.Properties;
using Localization;

namespace Bloom.CollectionChoosing
{
	public partial class OpenAndCreateCollectionDialog : Form
	{
		public OpenAndCreateCollectionDialog(MostRecentPathsList mruList)
		{
			InitializeComponent();
			//_welcomeControl.TemplateLabel.ForeColor = Color.FromArgb(0x61, 0x94, 0x38);//0xa0, 0x3c, 0x50);
			_openAndCreateControl.TemplateButton.Image = Resources.library32x32;
			_openAndCreateControl.TemplateButton.Image.Tag = "testfrombloom";

			_openAndCreateControl.Init(mruList,
				 LocalizationManager.GetString("OpenCreateNewCollectionsDialog.Bloom Collections", "Bloom Collections", "This shows in the file-open dialog that you use to open a different bloom collection") + @"|*.bloomLibrary;*.bloomCollection",
				 () => NewCollectionWizard.CreateNewCollection());

			_openAndCreateControl.DoneChoosingOrCreatingLibrary += (x, y) =>
																{
																	DialogResult = DialogResult.OK;
																	Close();
																};
		}



		public string SelectedPath
		{
			get { return _openAndCreateControl.SelectedPath; }
		}
	}
}
