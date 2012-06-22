using System;
using System.Windows.Forms;
using Bloom.Collection;

namespace Bloom.NewCollection
{
	public partial class KindOfCollectionControl : UserControl, IPageControl
	{
		private CollectionSettings _collectionInfo;
		private Action<UserControl, bool> _setNextButtonState;

		public KindOfCollectionControl()
		{
			InitializeComponent();
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
