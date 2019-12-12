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
			_lookupISOControl.SelectedLanguage = null;
			_lookupISOControl.IsShowRegionalDialectsCheckBoxVisible = true;

			// Following should be consistent with CollectionSettingsDialog.ChangeLanguage()
			_lookupISOControl.UseSimplifiedChinese();
			_lookupISOControl.IsScriptAndVariantLinkVisible = true;
			_lookupISOControl.IncludeScriptMarkers = false;
		}

		private void OnLookupISOControlReadinessChanged(object sender, EventArgs e)
		{
			if (_collectionInfo == null)
				return;

			if (_lookupISOControl.SelectedLanguage != null)
			{
				_collectionInfo.Language1.Iso639Code = _lookupISOControl.SelectedLanguage.LanguageTag;
				_collectionInfo.Language1.Name = _lookupISOControl.SelectedLanguage.DesiredName;
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
