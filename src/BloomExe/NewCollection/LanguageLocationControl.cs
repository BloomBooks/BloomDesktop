using System.Windows.Forms;
using Bloom.Collection;

namespace Bloom.NewCollection
{
	public partial class LanguageLocationControl : UserControl, IPageControl
	{
		private NewCollectionInfo _collectionInfo;

		public LanguageLocationControl()
		{
			InitializeComponent();
		}

		public void NowVisible()
		{

		}

		public void Init(NewCollectionInfo collectionInfo)
		{
			_collectionInfo = collectionInfo;
		}

		private void LanguageLocationControl_Leave(object sender, System.EventArgs e)
		{
			//_collectionInfo.
		}
	}
}
