using System;
using System.Linq;
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
			_lookupModel.SelectedLanguage = null;
			_lookupModel.IsShowRegionalDialectsCheckBoxVisible = true;

			// Following should be consistent with CollectionSettingsDialog.ChangeLanguage()
			_lookupModel.UseSimplifiedChinese();
			_lookupModel.IsScriptAndVariantLinkVisible = true;
			_lookupModel.IncludeScriptMarkers = false;
		}

		private void OnLookupModelControlReadinessChanged(object sender, EventArgs e)
		{
			if (_collectionInfo == null)
				return;

			if (_lookupModel.SelectedLanguage != null)
			{
				var selectedLanguageInfo = _lookupModel.SelectedLanguage;
				_collectionInfo.Language1.Tag = selectedLanguageInfo.LanguageTag;
				_collectionInfo.Language1.SetName(selectedLanguageInfo.DesiredName,
					selectedLanguageInfo.DesiredName != selectedLanguageInfo.Names.FirstOrDefault());
				_collectionInfo.Country = selectedLanguageInfo.PrimaryCountry ?? string.Empty;

				//If there are multiple countries, just leave it blank so they can type something in
				if (_collectionInfo.Country.Contains(","))
				{
					_collectionInfo.Country = "";
				}
			}

			_setNextButtonState(this, _lookupModel.SelectedLanguage != null);

		}

		public void Init(Action<UserControl, bool> setNextButtonState, CollectionSettings collectionInfo)
		{
			_setNextButtonState = setNextButtonState;
			_collectionInfo = collectionInfo;
			_lookupModel.ReadinessChanged += OnLookupModelControlReadinessChanged;
		}

		public void NowVisible()
		{
			_setNextButtonState(this, _lookupModel.SelectedLanguage != null);
		}

		private void _lookupModelControl_Leave(object sender, EventArgs e)
		{
			_setNextButtonState(this, _lookupModel.SelectedLanguage != null);
		}
	}
}
