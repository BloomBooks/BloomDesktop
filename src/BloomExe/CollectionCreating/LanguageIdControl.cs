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
			_lookupISOControl.SelectedLanguage = null;
			_lookupISOControl.IsShowRegionalDialectsCheckBoxVisible = false;

			// Following should be consistent with CollectionSettingsDialog.ChangeLanguage()
			// per BL-4780 we don't offer these codes, which are to generic to be useful.
			_lookupISOControl.MatchingLanguageFilter = info => info.LanguageTag != "zh" && info.LanguageTag != "cmn";
			// per BL-4780 we prefer these names for the common Chinese codes
			_lookupISOControl.SetLanguageAlias("zh-Hans", "Simplified Chinese (简体中文)");
			_lookupISOControl.SetLanguageAlias("zh-CN", "Simplified Chinese (简体中文)");
			_lookupISOControl.SetLanguageAlias("zh-Hant", "Traditional Chinese (繁体中文)");
			_lookupISOControl.SetLanguageAlias("zh-TW", "Traditional Chinese (繁体中文)");

		}

		private void OnLookupISOControlReadinessChanged(object sender, EventArgs e)
		{
			if (_collectionInfo == null)
				return;

			if (_lookupISOControl.SelectedLanguage != null)
			{
				_collectionInfo.Language1Iso639Code = _lookupISOControl.SelectedLanguage.LanguageTag;
				_collectionInfo.Language1Name = _lookupISOControl.SelectedLanguage.DesiredName;
				_collectionInfo.Country = _lookupISOControl.SelectedLanguage.PrimaryCountry ?? string.Empty;

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
