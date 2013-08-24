using System;
using System.Windows.Forms;
using Bloom.Collection;

namespace Bloom.CollectionCreating
{
	public partial class LanguageIdControl : UserControl, IPageControl
	{
		public CollectionSettings _collectionInfo;
		private Action<UserControl, bool> _setNextButtonState;

		public LanguageIdControl()
		{
			InitializeComponent();
			_lookupISOControl.ISOCode = string.Empty;
		}

		private void OnLookupISOControlReadinessChanged(object sender, EventArgs e)
		{
			if (_collectionInfo == null)
				return;

			_collectionInfo.Language1Iso639Code = _lookupISOControl.ISOCode;
			_collectionInfo.Language1Name = _lookupISOControl.LanguageInfo == null ? string.Empty : _lookupISOControl.LanguageInfo.DesiredName;
			if(_lookupISOControl.LanguageInfo!=null)
				_collectionInfo.Country = _lookupISOControl.LanguageInfo.Country;

			_setNextButtonState(this, _lookupISOControl.LanguageInfo != null);

		}

		public void Init(Action<UserControl, bool> setNextButtonState, CollectionSettings collectionInfo)
		{
			_setNextButtonState = setNextButtonState;
			_collectionInfo = collectionInfo;
			_lookupISOControl.ReadinessChanged += OnLookupISOControlReadinessChanged;
		}
		public void NowVisible()
		{
			_setNextButtonState(this, _lookupISOControl.LanguageInfo != null);
		}

		private void _lookupISOControl_Leave(object sender, EventArgs e)
		{
			_setNextButtonState(this, _lookupISOControl.LanguageInfo != null);
		}
	}
}
