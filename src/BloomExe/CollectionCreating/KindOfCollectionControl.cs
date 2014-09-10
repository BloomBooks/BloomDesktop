using System;
using System.Windows.Forms;
using Bloom.Collection;
using L10NSharp;
using Palaso.UI.WindowsForms.i18n;

namespace Bloom.CollectionCreating
{
	public partial class KindOfCollectionControl : UserControl, IPageControl
    {
		private CollectionSettings _collectionInfo;
		private Action<UserControl, bool> _setNextButtonState;

		public KindOfCollectionControl()
        {
            InitializeComponent();

			//this is a work-around https://jira.sil.org/browse/BL-316 until we figure out why l10nsharp is dropping this one item
			_sourceCollectionDescription.Text =
				LocalizationManager.GetString("NewCollectionWizard.KindOfCollectionPage.sourceCollectionDescription", 
						"A collection of shell or template books in one or more languages of wider communication. You will be able to upload these shells to BloomLibrary.org and optionally make a BloomPack to give to others so that they can make vernacular books with your shells.");
        }

		private void _radioNormalVernacularCollection_CheckedChanged(object sender, System.EventArgs e)
		{
			_collectionInfo.IsSourceCollection = _radioSourceCollection.Checked;
			_setNextButtonState(this,true);//will update where next goes based on this IsSourceCollection value
		}
		private void _radioSourceCollection_CheckedChanged(object sender, EventArgs e)
		{
			_collectionInfo.IsSourceCollection = _radioSourceCollection.Checked;
			_setNextButtonState(this,true); //will update where next goes based on this IsSourceCollection value
		}

		public void Init(Action<UserControl, bool> SetNextButtonState, CollectionSettings collectionInfo)
		{
			_setNextButtonState = SetNextButtonState;
			_collectionInfo = collectionInfo;
		}
		public void NowVisible()
		{
			_setNextButtonState(this,true);
		}


    }
}
