using System;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using L10NSharp;
using SIL.Reporting;
using SIL.Windows.Forms.WritingSystems;
using SIL.Extensions;
using SIL.WritingSystems;
using Bloom.TeamCollection;
using Bloom.MiscUI;
using Bloom.web.controllers;
using Bloom.Api;

namespace Bloom.Collection
{
	public partial class CollectionSettingsDialog : SIL.Windows.Forms.Miscellaneous.FormForUsingPortableClipboard
	{
		public delegate CollectionSettingsDialog Factory();//autofac uses this

		private readonly CollectionSettings _collectionSettings;
		private readonly QueueRenameOfCollection _queueRenameOfCollection;
		private readonly XMatterPackFinder _xmatterPackFinder;
		private readonly PageRefreshEvent _pageRefreshEvent;
		private bool _restartRequired;
		private bool _loaded;
		private string _subscriptionCode;
		private string _brand;

		// Pending values edited through the CollectionSettingsApi
		private string _pendingBookshelf;
		public string PendingDefaultBookshelf
		{
			set
			{
				if (value != _collectionSettings.DefaultBookshelf)
					Invoke((Action) ChangeThatRequiresRestart);
				_pendingBookshelf = value;
			}
			get
			{
				return _pendingBookshelf;
			}
		}

		// "Internal" so CollectionSettingsApi can update these.
		internal readonly string[] PendingFontSelections = new[] { "", "", "" };
		internal string PendingNumberingStyle { get; set; }
		internal string PendingXmatter { get; set; }

		public CollectionSettingsDialog(CollectionSettings collectionSettings,
			QueueRenameOfCollection queueRenameOfCollection, PageRefreshEvent pageRefreshEvent,
			TeamCollectionManager tcManager, XMatterPackFinder xmatterPackFinder)
		{
			_collectionSettings = collectionSettings;
			_queueRenameOfCollection = queueRenameOfCollection;
			_pageRefreshEvent = pageRefreshEvent;
			_xmatterPackFinder = xmatterPackFinder;
			InitializeComponent();

			_language1Name.UseMnemonic = false; // Allow & to be part of the language display names.
			_language2Name.UseMnemonic = false; // This may be unlikely, but can't be ruled out.
			_language3Name.UseMnemonic = false; // See https://issues.bloomlibrary.org/youtrack/issue/BL-9919.

			PendingFontSelections[0] = _collectionSettings.LanguagesZeroBased[0].FontName;
			PendingFontSelections[1] = _collectionSettings.LanguagesZeroBased[1].FontName;
			var have3rdLanguage = _collectionSettings.LanguagesZeroBased[2] != null;
			PendingFontSelections[2] = have3rdLanguage ?
				_collectionSettings.LanguagesZeroBased[2].FontName :
				"";
			PendingNumberingStyle = _collectionSettings.PageNumberStyle;
			PendingXmatter = _collectionSettings.XMatterPackName;
			CollectionSettingsApi.DialogBeingEdited = this;

			if (_collectionSettings.IsSourceCollection)
			{
				_language1Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language1InSourceCollection", "Language 1", "In a local language collection, we say 'Local Language', but in a source collection, Local Language has no relevance, so we use this different label");
				_language2Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language2InSourceCollection", "Language 2", "In a local language collection, we say 'Language 2 (e.g. National Language)', but in a source collection, National Language has no relevance, so we use this different label");
				_language3Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language3InSourceCollection", "Language 3", "In a local language collection, we say 'Language 3 (e.g. Regional Language)', but in a source collection, National Language has no relevance, so we use this different label");
			}

			_showExperimentalBookSources.Checked = ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kExperimentalSourceBooks);
			_allowTeamCollection.Checked = ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections);
			_allowSpreadsheetImportExport.Checked = ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kSpreadsheetImportExport);

			if (!ExperimentalFeatures.IsFeatureEnabled(ExperimentalFeatures.kTeamCollections) && tcManager.CurrentCollectionEvenIfDisconnected == null)
			{
				this._tab.Controls.Remove(this._teamCollectionTab);
			}
			// Don't allow the user to disable the Team Collection feature if we're currently in a Team Collection.
			_allowTeamCollection.Enabled = !(_allowTeamCollection.Checked && tcManager.CurrentCollectionEvenIfDisconnected != null);

			// AutoUpdate applies only to Windows: see https://silbloom.myjetbrains.com/youtrack/issue/BL-2317.
			if (SIL.PlatformUtilities.Platform.IsWindows)
				_automaticallyUpdate.Checked = Settings.Default.AutoUpdate;
			else
				_automaticallyUpdate.Hide();

			// Without this, PendingDefaultBookshelf stays null unless the user changes it.
			// The result is the bookshelf selection gets cleared when other collection settings are saved. See BL-10093.
			PendingDefaultBookshelf = _collectionSettings.DefaultBookshelf;

			CollectionSettingsApi.BrandingChangeHandler = ChangeBranding;

			TeamCollectionApi.TheOneInstance.SetCallbackToReopenCollection(() =>
			{
				_restartRequired = true;
				ReactDialog.CloseCurrentModal(); // close the top Create dialog
				_okButton_Click(null, null); // close this dialog
			});

			UpdateDisplay();

			if (CollectionSettingsApi.FixEnterpriseSubscriptionCodeMode)
			{
				_tab.SelectedTab = _enterpriseTab;
			}

			if (tcManager.CurrentCollectionEvenIfDisconnected == null)
			{
				_noRenameTeamCollectionLabel.Visible = false;
			}
			else
			{
				_bloomCollectionName.Enabled = false;
			}
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			// BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
			this.Icon = global::Bloom.Properties.Resources.BloomIcon;
		}

		public void SetDesiredTab(string tab)
		{
			if (tab == "enterprise")
				_tab.SelectedTab = _enterpriseTab;
		}

		private void UpdateDisplay()
		{
			var lang1UiName = _collectionSettings.Language1.Name;
			var lang2UiName = _collectionSettings.Language2.Name;
			_language1Name.Text = string.Format("{0} ({1})", lang1UiName, _collectionSettings.Language1Tag);
			_language2Name.Text = string.Format("{0} ({1})", lang2UiName, _collectionSettings.Language2Tag);
			const string unsetLanguageName = "--";
			if (string.IsNullOrEmpty(_collectionSettings.Language3Tag))
			{
				_language3Name.Text = unsetLanguageName;
				_removeLanguage3Link.Visible = false;
				_changeLanguage3Link.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink", "Set...", "If there is no third or sign language specified, the link changes to this.");
			}
			else
			{
				var lang3UiName = _collectionSettings.Language3.Name;
				_language3Name.Text = string.Format("{0} ({1})", lang3UiName, _collectionSettings.Language3Tag);
				_removeLanguage3Link.Visible = true;
				_changeLanguage3Link.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.ChangeLanguageLink", "Change...");
			}

			if (string.IsNullOrEmpty(_collectionSettings.SignLanguageTag))
			{
				_signLanguageName.Text = unsetLanguageName;
				_removeSignLanguageLink.Visible = false;
				_changeSignLanguageLink.Text = LocalizationManager.GetString(
					"CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink", "Set...",
					"If there is no third or sign language specified, the link changes to this.");
			}
			else
			{
				var signLangUiName = _collectionSettings.SignLanguage.Name;
				_signLanguageName.Text = string.Format("{0} ({1})", signLangUiName, _collectionSettings.SignLanguageTag);
				_removeSignLanguageLink.Visible = true;
				_changeSignLanguageLink.Text =
					LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.ChangeLanguageLink", "Change...");
			}

			_restartReminder.Visible = AnyReasonToRestart();
			_okButton.Text = AnyReasonToRestart() ? LocalizationManager.GetString("CollectionSettingsDialog.Restart", "Restart", "If you make certain changes in the settings dialog, the OK button changes to this.") : LocalizationManager.GetString("Common.OKButton", "&OK");
		}

		private void _language1ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var potentiallyCustomName = _collectionSettings.Language1.Name;

			var l = ChangeLanguage(_collectionSettings.Language1Tag, potentiallyCustomName);

			if (l != null)
			{
				_collectionSettings.Language1.Tag = l.LanguageTag;
				_collectionSettings.Language1.SetName(l.DesiredName, l.DesiredName != l.Names.FirstOrDefault());
				ChangeThatRequiresRestart();
			}
		}
		private void _language2ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var potentiallyCustomName = _collectionSettings.Language2.Name;
			var l = ChangeLanguage(_collectionSettings.Language2Tag, potentiallyCustomName);
			if (l != null)
			{
				_collectionSettings.Language2Tag = l.LanguageTag;
				_collectionSettings.Language2.SetName(l.DesiredName, l.DesiredName != l.Names.FirstOrDefault());
				ChangeThatRequiresRestart();
			}
		}

		private void _language3ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var potentiallyCustomName = _collectionSettings.Language3.Name;
			var l = ChangeLanguage(_collectionSettings.Language3Tag, potentiallyCustomName);
			if (l != null)
			{
				_collectionSettings.Language3Tag = l.LanguageTag;
				_collectionSettings.Language3.SetName(l.DesiredName, l.DesiredName != l.Names.FirstOrDefault());
				ChangeThatRequiresRestart();
			}
		}
		private void _removeSecondNationalLanguageButton_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.Language3Tag = string.Empty;	// null causes a crash in trying to set it again (BL-5795)
			ChangeThatRequiresRestart();
		}

		private void _signLanguageChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var potentiallyCustomName = _collectionSettings.SignLanguage.Name;
			var l = ChangeLanguage(_collectionSettings.SignLanguageTag, potentiallyCustomName, true);
			if (l != null)
			{
				// How to know if the new sign language name is custom or not!?
				// 1- set the Tag (which also sets the Name to the non-custom default
				// 2- read the Name
				// 3- if it's not the same as DesiredName, the new name is custom
				_collectionSettings.SignLanguageTag = l.LanguageTag;
				var slIsCustom = _collectionSettings.SignLanguage.Name != l.DesiredName;
				_collectionSettings.SignLanguage.SetName(l.DesiredName, slIsCustom);
				ChangeThatRequiresRestart();
			}
		}
		private void _removeSignLanguageButton_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.SignLanguageTag = string.Empty;
			ChangeThatRequiresRestart();
		}

		public static LanguageInfo ChangeLanguage(string languageIdentifier, string potentiallyCustomName = null,
			bool showScriptAndVariantLink = true)
		{
			using (var dlg = new LanguageLookupDialog())
			{
				//at this point, we don't let them customize the national languages
				dlg.IsDesiredLanguageNameFieldVisible = potentiallyCustomName != null;
				dlg.IsShowRegionalDialectsCheckBoxVisible = true;
				dlg.IsScriptAndVariantLinkVisible = showScriptAndVariantLink;

				var language = new LanguageInfo() { LanguageTag = languageIdentifier};
				if (!string.IsNullOrEmpty(potentiallyCustomName))
				{
					language.DesiredName = potentiallyCustomName; // to be noticed, must set before dlg.SelectedLanguage
				}
				dlg.SelectedLanguage = language;
				// if languageIdentifier includes Script/Region/Variant codes... which it might now...
				// limit the SearchText to the part before the first hyphen (the iso 639 code).
				dlg.SearchText = languageIdentifier.Split('-')[0];

				// Following should be consistent with LanguageIdControl constructor.
				dlg.UseSimplifiedChinese();

				// Avoid showing gratuitous script markers in language tags.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-7641.
				dlg.IncludeScriptMarkers = false;

				if (DialogResult.OK != dlg.ShowDialog(Shell.GetShellOrOtherOpenForm()))
				{
					return null;
				}
				return  dlg.SelectedLanguage;
			}
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			Logger.WriteMinorEvent("Settings Dialog OK Clicked");

			CollectionSettingsApi.DialogBeingEdited = null;
			_collectionSettings.Country = _countryText.Text.Trim();
			_collectionSettings.Province = _provinceText.Text.Trim();
			_collectionSettings.District = _districtText.Text.Trim();

			var languages = _collectionSettings.LanguagesZeroBased;
			for(int i = 0; i < 3; i++)
			{
				if (languages[i] == null)
					continue;
				languages[i].FontName = PendingFontSelections[i];
			}

			_collectionSettings.PageNumberStyle = PendingNumberingStyle; // non-localized key
			

			_collectionSettings.BrandingProjectKey = _brand;
			_collectionSettings.SubscriptionCode = _subscriptionCode;

			string xmatterKeyForcedByBranding = _collectionSettings.GetXMatterPackNameSpecifiedByBrandingOrNull();
			PendingXmatter =
				this._xmatterPackFinder.GetValidXmatter(xmatterKeyForcedByBranding, PendingXmatter);
			_collectionSettings.XMatterPackName = PendingXmatter;

			//no point in letting them have the Nat lang 2 be the same as 1
			if (_collectionSettings.Language2Tag == _collectionSettings.Language3Tag)
				_collectionSettings.Language3Tag = null;

			if(_bloomCollectionName.Text.Trim()!=_collectionSettings.CollectionName)
			{
				_queueRenameOfCollection.Raise(_bloomCollectionName.Text.SanitizeFilename('-'));
				//_collectionSettings.PrepareToRenameCollection(_bloomCollectionName.Text.SanitizeFilename('-'));
			}
			Logger.WriteEvent("Closing Settings Dialog");

			_collectionSettings.DefaultBookshelf = PendingDefaultBookshelf;
			_collectionSettings.Save();
			Close();

			DialogResult = AnyReasonToRestart() ? DialogResult.Yes : DialogResult.OK;
		}

		private bool XMatterChangePending
		{
			get
			{
				return PendingXmatter != _collectionSettings.XMatterPackName;
			}
		}

		/// <summary>
		/// Internal so api can trigger this.
		/// </summary>
		internal void ChangeThatRequiresRestart()
		{
			if (!_loaded)//ignore false events that come while setting upt the dialog
				return;

			_restartRequired = true;
			UpdateDisplay();
		}

		private bool AnyReasonToRestart()
		{
			return _restartRequired || XMatterChangePending;
		}

		private void OnLoad(object sender, EventArgs e)
		{
			_countryText.Text = _collectionSettings.Country;
			_provinceText.Text = _collectionSettings.Province;
			_districtText.Text = _collectionSettings.District;
			_bloomCollectionName.Text = _collectionSettings.CollectionName;
			_brand = _collectionSettings.BrandingProjectKey;
			_subscriptionCode = _collectionSettings.SubscriptionCode;
			// Set the branding as an (incomplete) code if we are running with a legacy branding
			if (CollectionSettingsApi.LegacyBrandingName != null && string.IsNullOrEmpty(_subscriptionCode))
			{
				_subscriptionCode = CollectionSettingsApi.LegacyBrandingName;
			}
			CollectionSettingsApi.SetSubscriptionCode(_subscriptionCode, _collectionSettings.IsSubscriptionCodeKnown(), _collectionSettings.GetEnterpriseStatus());
			_loaded = true;
			Logger.WriteEvent("Entered Settings Dialog");
		}

		private void _cancelButton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			CollectionSettingsApi.DialogBeingEdited = null;
			Close();
		}

		private void _helpButton_Click(object sender, EventArgs e)
		{
			if (_tab.SelectedTab == tabPage1)
				HelpLauncher.Show(this, "Tasks/Basic_tasks/Change_languages.htm");
			else if (_tab.SelectedTab == _bookMakingTab)
				HelpLauncher.Show(this, "Tasks/Basic_tasks/Select_front_matter_or_back_matter_from_a_pack.htm");
			else if (_tab.SelectedTab == tabPage3)
				HelpLauncher.Show(this, "Tasks/Basic_tasks/Enter_project_information.htm");
			else if (_tab.SelectedTab == _enterpriseTab)
				HelpLauncher.Show(this, "Tasks/Basic_tasks/Select_Bloom_Enterprise_Status.htm");
			else
				HelpLauncher.Show(this, "User_Interface/Dialog_boxes/Settings_dialog_box.htm");
		}

		private void _bloomCollectionName_TextChanged(object sender, EventArgs e)
		{
			if (_bloomCollectionName.Text.Trim() == _collectionSettings.CollectionName)
				return;

			ChangeThatRequiresRestart();
		}

		private void _showExperimentalBookSources_CheckedChanged(object sender, EventArgs e)
		{
			ExperimentalFeatures.SetValue(ExperimentalFeatures.kExperimentalSourceBooks, _showExperimentalBookSources.Checked);
			ChangeThatRequiresRestart();
		}

		private void _automaticallyUpdate_CheckedChanged(object sender, EventArgs e)
		{
			Settings.Default.AutoUpdate = _automaticallyUpdate.Checked;
		}

		public static bool FontSettingsLinkClicked(CollectionSettings settings, string langName, int langNum1Based)
		{ 
			var langSpec = settings.LanguagesZeroBased[langNum1Based - 1];
			using (var frm = new ScriptSettingsDialog())
			{
				frm.LanguageName = langName;
				frm.LanguageRightToLeft = langSpec.IsRightToLeft;
				frm.LanguageLineSpacing = langSpec.LineHeight;
				frm.UIFontSize = langSpec.BaseUIFontSizeInPoints;
				frm.BreakLinesOnlyAtSpaces = langSpec.BreaksLinesOnlyAtSpaces;
				frm.ShowDialog();

				// get the changes

				// We usually don't need to restart, just gather the changes up. The caller
				// will save the .bloomCollection file. Later when a book
				// is edited, defaultLangStyles.css will be written out in the book's folder, which is all
				// that is needed for this setting to take effect.
				langSpec.LineHeight = frm.LanguageLineSpacing;
				langSpec.BreaksLinesOnlyAtSpaces = frm.BreakLinesOnlyAtSpaces;
				langSpec.BaseUIFontSizeInPoints = frm.UIFontSize;
				if (frm.LanguageRightToLeft != langSpec.IsRightToLeft) 
				{
					langSpec.IsRightToLeft = frm.LanguageRightToLeft;
					return true;
				}
				return false;
			}
		}

		/// <summary>
		/// We configure the SettingsApi to use this method to notify this (as the manager of the whole dialog
		/// including the "need to reload" message and the Ok/Cancel buttons) of changes the user makes
		/// in the Enterprise tab.
		/// </summary>
		/// <param name="fullBrandingName"></param>
		/// <param name="subscriptionCode"></param>
		/// <returns></returns>
		public bool ChangeBranding(string fullBrandingName, string subscriptionCode)
		{
			if (_brand != fullBrandingName || DifferentSubscriptionCodes(subscriptionCode, _subscriptionCode))
			{
				Invoke((Action) ChangeThatRequiresRestart);
				_brand = fullBrandingName;
				_subscriptionCode = subscriptionCode;
				//if (BrandingProject.HaveFilesForBranding(fullBrandingName))
				//{
				//	// if the branding.json specifies an xmatter, set the default for this collection to that.
				//	var correspondingXMatterPack = BrandingSettings.GetSettingsOrNull(fullBrandingName).GetXmatterToUse();
				//	if (!string.IsNullOrEmpty(correspondingXMatterPack))
				//	{
				//		_collectionSettings.XMatterPackName = correspondingXMatterPack;
				//	}
				//}
				return true;
			}
			return false;
		}

		private bool DifferentSubscriptionCodes(string code1, string code2)
		{
			if (string.IsNullOrEmpty(code1) && string.IsNullOrEmpty(code2))
				return false;
			return code1 != code2;
		}

		private void _numberStyleCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			ChangeThatRequiresRestart();
		}

		private void _allowTeamCollection_CheckedChanged(object sender, EventArgs e)
		{
			ExperimentalFeatures.SetValue(ExperimentalFeatures.kTeamCollections, _allowTeamCollection.Checked);
			ChangeThatRequiresRestart();
		}

		private void _allowSpreadsheetImportExport_CheckedChanged(object sender, EventArgs e)
		{
			ExperimentalFeatures.SetValue(ExperimentalFeatures.kSpreadsheetImportExport, _allowSpreadsheetImportExport.Checked);
			ChangeThatRequiresRestart();
		}
	}
}
