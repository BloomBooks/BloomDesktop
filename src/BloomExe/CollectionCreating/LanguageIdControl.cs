using System;
using System.Linq;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.ToPalaso;

namespace Bloom.CollectionCreating
{
	public partial class LanguageIdControl : UserControl, IPageControl
	{
		public CollectionSettings _collectionInfo;
		private Action<UserControl, bool> _setNextButtonState;

		public LanguageIdControl()
		{
			InitializeComponent();
			_lookupISOControl.SelectedLanguage.LanguageTag = string.Empty;
		}

		private void OnLookupISOControlReadinessChanged(object sender, EventArgs e)
		{
			if (_collectionInfo == null)
				return;

			_collectionInfo.Language1Iso639Code = _lookupISOControl.SelectedLanguage.LanguageTag;
			_collectionInfo.Language1Name = _lookupISOControl.SelectedLanguage == null ? string.Empty : _lookupISOControl.SelectedLanguage.DesiredName;
			if (_lookupISOControl.SelectedLanguage != null)
			{
				_collectionInfo.Country = _lookupISOControl.SelectedLanguage.Countries.FirstOrDefault() ?? string.Empty;
				
				//If there are multiple countries, just leave it blank so they can type something in
				if (_collectionInfo.Country.Contains(","))
				{
					_collectionInfo.Country = "";
				}
			}

			_setNextButtonState(this, _lookupISOControl.SelectedLanguage != null);

		}

		public void Init(Action<UserControl, bool> setNextButtonState, CollectionSettings collectionInfo)
		{
			_setNextButtonState = setNextButtonState;
			_collectionInfo = collectionInfo;
			_lookupISOControl.ReadinessChanged += OnLookupISOControlReadinessChanged;
		}
		public void NowVisible()
		{
			_setNextButtonState(this, _lookupISOControl.SelectedLanguage != null);
		}

		private void _lookupISOControl_Leave(object sender, EventArgs e)
		{
			_setNextButtonState(this, _lookupISOControl.SelectedLanguage != null);
		}
	}
}
