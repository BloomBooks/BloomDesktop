using System.Windows.Forms;
using Bloom.CollectionCreating;
using Bloom.Properties;

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
				 "Create new collection",
				 "Browse for other collections on this computer...",
				 "Bloom Collections|*.bloomLibrary;*.bloomCollection",
				 dir => true,
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
