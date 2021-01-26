using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using L10NSharp;
using SIL.Reporting;
using SIL.Windows.Forms.WritingSystems;
using SIL.Extensions;
using SIL.WritingSystems;
using System.Collections.Generic;
using Bloom.Api;
using Bloom.TeamCollection;
using Bloom.MiscUI;
using Bloom.web.controllers;

namespace Bloom.Collection
{
	public partial class CollectionSettingsDialog : SIL.Windows.Forms.Miscellaneous.FormForUsingPortableClipboard
	{
		public delegate CollectionSettingsDialog Factory();//autofac uses this

		private readonly CollectionSettings _collectionSettings;
		private XMatterPackFinder _xmatterPackFinder;
		private readonly QueueRenameOfCollection _queueRenameOfCollection;
		private readonly PageRefreshEvent _pageRefreshEvent;
		private bool _restartRequired;
		private bool _loaded;
		private List<string> _styleNames = new List<string>();
		// Enhance: when all the tabs are in Typescript, we should be able to move the tabs themselves there and
		// have one browser for the whole dialog.
		private Browser _enterpriseBrowser;
		private Browser _sharingBrowser;
		private string _subscriptionCode;
		private string _brand;

		public CollectionSettingsDialog(CollectionSettings collectionSettings, XMatterPackFinder xmatterPackFinder, QueueRenameOfCollection queueRenameOfCollection, PageRefreshEvent pageRefreshEvent)
		{
			_collectionSettings = collectionSettings;
			_xmatterPackFinder = xmatterPackFinder;
			_queueRenameOfCollection = queueRenameOfCollection;
			_pageRefreshEvent = pageRefreshEvent;
			InitializeComponent();
			// moved from the Designer where it was deleted if the Designer was touched
			_xmatterList.Columns.AddRange(new[] { new ColumnHeader() { Width = 250 } });

			if (_collectionSettings.IsSourceCollection)
			{
				_language1Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language1InSourceCollection", "Language 1", "In a local language collection, we say 'Local Language', but in a source collection, Local Language has no relevance, so we use this different label");
				_language2Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language2InSourceCollection", "Language 2", "In a local language collection, we say 'Language 2 (e.g. National Language)', but in a source collection, National Language has no relevance, so we use this different label");
				_language3Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language3InSourceCollection", "Language 3", "In a local language collection, we say 'Language 3 (e.g. Regional Language)', but in a source collection, National Language has no relevance, so we use this different label");
			}

			_showExperimentalFeatures.Checked = Settings.Default.ShowExperimentalFeatures;
			// AutoUpdate applies only to Windows: see https://silbloom.myjetbrains.com/youtrack/issue/BL-2317.
			if (SIL.PlatformUtilities.Platform.IsWindows)
				_automaticallyUpdate.Checked = Settings.Default.AutoUpdate;
			else
				_automaticallyUpdate.Hide();

//		    _showSendReceive.CheckStateChanged += (sender, args) =>
//		                                              {
//		                                                  Settings.Default.ShowSendReceive = _showSendReceive.CheckState ==
//		                                                                                     CheckState.Checked;
//
//                                                          _restartRequired = true;
//		                                                  UpdateDisplay();
//		                                              };

			CollectionSettingsApi.BrandingChangeHandler = ChangeBranding;

			SetupEnterpriseBrowser();
			SetupSharingBrowser();

			UpdateDisplay();

			if (CollectionSettingsApi.FixEnterpriseSubscriptionCodeMode)
			{
				_tab.SelectedTab = _enterpriseTab;
			}
		}

		private void SetupEnterpriseBrowser()
		{
			if (_enterpriseBrowser != null)
				return; // Seems to help performance.
			// The Size setting is needed on Linux to keep the browser from coming up as a small
			// rectangle in the upper left corner when the dialog is initialized to open on the
			// Enterprise tab.
			_enterpriseBrowser = new Browser {Dock = DockStyle.Fill, Location=new Point(3,3), Size=new Size(_enterpriseTab.Width-6, _enterpriseTab.Height-6)};
			_enterpriseBrowser.BackColor = Color.White;
			
			var rootFile = BloomFileLocator.GetBrowserFile(false, "collection", "enterpriseSettings.html");
			var dummy = _enterpriseBrowser.Handle; // gets the WebBrowser created
			_enterpriseBrowser.WebBrowser.DocumentCompleted += (sender, args) =>
			{
				// If the control gets added to the tab before it has navigated somewhere,
				// it shows as solid black, despite setting the BackColor to white.
				// So just don't show it at all until it contains what we want to see.
				_enterpriseTab.Controls.Add(_enterpriseBrowser);
			};
			_enterpriseBrowser.Navigate(rootFile.ToLocalhost(), false);
		}

		private void SetupSharingBrowser()
		{
			if (_sharingBrowser != null)
				return; // Seems to help performance.
			// The Size setting is needed on Linux to keep the browser from coming up as a small
			// rectangle in the upper left corner when the dialog is initialized to open on the
			// Enterprise tab.
			_sharingBrowser = new Browser { Dock = DockStyle.Fill, Location = new Point(3, 3), Size = new Size(_sharingTab.Width - 6, _sharingTab.Height - 6) };
			_sharingBrowser.BackColor = Color.White;

			var rootFile = BloomFileLocator.GetBrowserFile(false, "teamCollection", "teamCollectionSettings.html");
			var dummy = _sharingBrowser.Handle; // gets the WebBrowser created
			_sharingBrowser.WebBrowser.DocumentCompleted += (sender, args) =>
			{
				// If the control gets added to the tab before it has navigated somewhere,
				// it shows as solid black, despite setting the BackColor to white.
				// So just don't show it at all until it contains what we want to see.
				_sharingTab.Controls.Add(_sharingBrowser);
			};
			_sharingBrowser.Navigate(rootFile.ToLocalhost(), false);
			// If the SharingApi gets a callback from HTML that results in setting up
			// a team collection for this collection, we need to restart afterwards.
			TeamCollectionApi.TheOneInstance.SetCreateCallback(() => _restartRequired = true);
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
			string defaultFontText =
				LocalizationManager.GetString("CollectionSettingsDialog.BookMakingTab.DefaultFontFor", "Default Font for {0}", "{0} is a language name.");
			var lang1UiName = _collectionSettings.Language1.Name;
			var lang2UiName = _collectionSettings.Language2.Name;
			_language1Name.Text = string.Format("{0} ({1})", lang1UiName, _collectionSettings.Language1Iso639Code);
			_language2Name.Text = string.Format("{0} ({1})", lang2UiName, _collectionSettings.Language2Iso639Code);
			_language1FontLabel.Text = string.Format(defaultFontText, lang1UiName);
			_language2FontLabel.Text = string.Format(defaultFontText, lang2UiName);

			var lang3UiName = string.Empty;
			const string unsetLanguageName = "--";
			if (string.IsNullOrEmpty(_collectionSettings.Language3Iso639Code))
			{
				_language3Name.Text = unsetLanguageName;
				_removeLanguage3Link.Visible = false;
				_language3FontLabel.Visible = false;
				_fontComboLanguage3.Visible = false;
				_fontSettings3Link.Visible = false;
				_changeLanguage3Link.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink", "Set...", "If there is no third or sign language specified, the link changes to this.");
			}
			else
			{
				lang3UiName = _collectionSettings.Language3.Name;
				_language3Name.Text = string.Format("{0} ({1})", lang3UiName, _collectionSettings.Language3Iso639Code);
				_language3FontLabel.Text = string.Format(defaultFontText, lang3UiName);
				_removeLanguage3Link.Visible = true;
				_language3FontLabel.Visible = true;
				_fontComboLanguage3.Visible = true;
				_fontSettings3Link.Visible = true;
				_changeLanguage3Link.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.ChangeLanguageLink", "Change...");
			}

			var signLangUiName = string.Empty;
			if (string.IsNullOrEmpty(_collectionSettings.SignLanguageIso639Code))
			{
				_signLanguageName.Text = unsetLanguageName;
				_removeSignLanguageLink.Visible = false;
				_changeSignLanguageLink.Text = LocalizationManager.GetString(
					"CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink", "Set...",
					"If there is no third or sign language specified, the link changes to this.");
			}
			else
			{
				signLangUiName = _collectionSettings.GetSignLanguageName();
				_signLanguageName.Text = string.Format("{0} ({1})", signLangUiName, _collectionSettings.SignLanguageIso639Code);
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

			var l = ChangeLanguage(_collectionSettings.Language1Iso639Code, potentiallyCustomName);

			if (l != null)
			{
				_collectionSettings.Language1.Iso639Code = l.LanguageTag;
				_collectionSettings.Language1.SetName(l.DesiredName, l.DesiredName != l.Names.FirstOrDefault());
				ChangeThatRequiresRestart();
			}
		}
		private void _language2ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var potentiallyCustomName = _collectionSettings.Language2.Name;
			var l = ChangeLanguage(_collectionSettings.Language2Iso639Code, potentiallyCustomName);
			if (l != null)
			{
				_collectionSettings.Language2Iso639Code = l.LanguageTag;
				_collectionSettings.Language2.SetName(l.DesiredName, l.DesiredName != l.Names.FirstOrDefault());
				ChangeThatRequiresRestart();
			}
		}

		private void _language3ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var potentiallyCustomName = _collectionSettings.Language3.Name;
			var l = ChangeLanguage(_collectionSettings.Language3Iso639Code, potentiallyCustomName);
			if (l != null)
			{
				_collectionSettings.Language3Iso639Code = l.LanguageTag;
				_collectionSettings.Language3.SetName(l.DesiredName, l.DesiredName != l.Names.FirstOrDefault());
				ChangeThatRequiresRestart();
			}
		}
		private void _removeSecondNationalLanguageButton_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.Language3Iso639Code = string.Empty;	// null causes a crash in trying to set it again (BL-5795)
			ChangeThatRequiresRestart();
		}

		private void _signLanguageChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var potentiallyCustomName = _collectionSettings.SignLanguageName;
			var l = ChangeLanguage(_collectionSettings.SignLanguageIso639Code, potentiallyCustomName, false);
			if (l != null)
			{
				_collectionSettings.SignLanguageIso639Code = l.LanguageTag;
				_collectionSettings.SignLanguageName = l.DesiredName;
				ChangeThatRequiresRestart();
			}
		}
		private void _removeSignLanguageButton_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.SignLanguageIso639Code = string.Empty;
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

				if (DialogResult.OK != dlg.ShowDialog())
				{
					return null;
				}
				return  dlg.SelectedLanguage;
			}
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			Logger.WriteMinorEvent("Settings Dialog OK Clicked");

			_collectionSettings.Country = _countryText.Text.Trim();
			_collectionSettings.Province = _provinceText.Text.Trim();
			_collectionSettings.District = _districtText.Text.Trim();
			if (_fontComboLanguage1.SelectedItem != null)
			{
				_collectionSettings.Language1.FontName = _fontComboLanguage1.SelectedItem.ToString();
			}
			if (_fontComboLanguage2.SelectedItem != null)
			{
				_collectionSettings.Language2.FontName = _fontComboLanguage2.SelectedItem.ToString();
			}
			if (_fontComboLanguage3.SelectedItem != null)
			{
				_collectionSettings.Language3.FontName = _fontComboLanguage3.SelectedItem.ToString();
			}
			if (_numberStyleCombo.SelectedItem != null)
			{
				// have to do this lookup because we need the non-localized version of the name, and
				// we can't get at the original dictionary by index
				var styleName = _styleNames[_numberStyleCombo.SelectedIndex];
				_collectionSettings.PageNumberStyle = styleName;
			}

			_collectionSettings.BrandingProjectKey = _brand;
			_collectionSettings.SubscriptionCode = _subscriptionCode;

			//no point in letting them have the Nat lang 2 be the same as 1
			if (_collectionSettings.Language2Iso639Code == _collectionSettings.Language3Iso639Code)
				_collectionSettings.Language3Iso639Code = null;

			if(_bloomCollectionName.Text.Trim()!=_collectionSettings.CollectionName)
			{
				_queueRenameOfCollection.Raise(_bloomCollectionName.Text.SanitizeFilename('-'));
				//_collectionSettings.PrepareToRenameCollection(_bloomCollectionName.Text.SanitizeFilename('-'));
			}
			Logger.WriteEvent("Closing Settings Dialog");
			if (_xmatterList.SelectedItems.Count > 0 &&
			    ((XMatterInfo) _xmatterList.SelectedItems[0].Tag).Key != _collectionSettings.XMatterPackName)
			{
				_collectionSettings.XMatterPackName = ((XMatterInfo)_xmatterList.SelectedItems[0].Tag).Key;
				_restartRequired = true;// now that we've made them match, we won't detect by the normal means, so set this hard flag
			}
			_collectionSettings.Save();
			Close();
			if (!AnyReasonToRestart())
			{
				_pageRefreshEvent.Raise(PageRefreshEvent.SaveBehavior.SaveBeforeRefresh);
			}
			DialogResult = AnyReasonToRestart() ? DialogResult.Yes : DialogResult.OK;
		}

		private bool XMatterChangePending
		{
			get
			{
				return _xmatterList.SelectedItems.Count > 0 && ((XMatterInfo)_xmatterList.SelectedItems[0].Tag).Key != _collectionSettings.XMatterPackName;
			}
		}

		private void ChangeThatRequiresRestart()
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
			LoadFontCombo();
			LoadPageNumberStyleCombo();
			LoadBrandingCombo();
			AdjustFontComboDropdownWidth();
			_brand = _collectionSettings.BrandingProjectKey;
			_subscriptionCode = _collectionSettings.SubscriptionCode;
			// Set the branding as an (incomplete) code if we are running with a legacy branding
			if (CollectionSettingsApi.LegacyBrandingName != null && string.IsNullOrEmpty(_subscriptionCode))
			{
				_subscriptionCode = CollectionSettingsApi.LegacyBrandingName;
			}
			CollectionSettingsApi.SetSubscriptionCode(_subscriptionCode, IsSubscriptionCodeKnown(), GetEnterpriseStatus());
			_loaded = true;
			Logger.WriteEvent("Entered Settings Dialog");
		}

		bool IsSubscriptionCodeKnown()
		{
			return BrandingProject.HaveFilesForBranding(_brand);
		}

		/// <summary>
		/// NB The selection stuff is flaky if we attempt to select things before the control is all created, settled down, bored.
		/// </summary>
		private void SetupXMatterList()
		{
			var packsToSkip = new string[] {"null", "bigbook", "SHRP", "SHARP", "ForUnitTest", "TemplateStarter"};
			_xmatterList.Items.Clear();
			ListViewItem itemForFactoryDefault = null;
			foreach(var pack in _xmatterPackFinder.All)
			{
				if (packsToSkip.Any(s => pack.Key.ToLowerInvariant().Contains(s.ToLower())))
					continue;

				var labelToShow = LocalizationManager.GetDynamicString("Bloom","CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack."+pack.EnglishLabel, pack.EnglishLabel, "Name of a Front/Back Matter Pack");
				var item = _xmatterList.Items.Add(labelToShow);
				item.Tag = pack;
				if(pack.Key == _collectionSettings.XMatterPackName)
					item.Selected = true;
				if(pack.Key == _xmatterPackFinder.FactoryDefault.Key)
				{
					itemForFactoryDefault = item;
				}
			}
			if(itemForFactoryDefault != null && _xmatterList.SelectedItems.Count == 0) //if the xmatter they used to have selected is gone or was renamed or something
				itemForFactoryDefault.Selected = true;

			if(_xmatterList.SelectedIndices.Count > 0)
				_xmatterList.EnsureVisible(_xmatterList.SelectedIndices[0]);
		}

		/*
		 * If changes are made to have different values in each combobox, need to modify AdjustFontComboDropdownWidth.
		 */
		private void LoadFontCombo()
		{
			// Display the fonts in sorted order.  See https://jira.sil.org/browse/BL-864.
			var fontNames = new List<string>();
			fontNames.AddRange(Browser.NamesOfFontsThatBrowserCanRender());
			fontNames.Sort();
			foreach (var font in fontNames)
			{
				_fontComboLanguage1.Items.Add(font);
				_fontComboLanguage2.Items.Add(font);
				_fontComboLanguage3.Items.Add(font);
				if (font == _collectionSettings.Language1.FontName)
					_fontComboLanguage1.SelectedIndex = _fontComboLanguage1.Items.Count-1;
				if (font == _collectionSettings.Language2.FontName)
					_fontComboLanguage2.SelectedIndex = _fontComboLanguage2.Items.Count - 1;
				if (font == _collectionSettings.Language3.FontName)
					_fontComboLanguage3.SelectedIndex = _fontComboLanguage3.Items.Count - 1;
			}
		}

		private void LoadPageNumberStyleCombo()
		{
			_styleNames.Clear();
			foreach (var styleKey in CollectionSettings.CssNumberStylesToCultureOrDigits.Keys)
			{
				_styleNames.Add(styleKey);
				var localizedStyle =
					LocalizationManager.GetString("CollectionSettingsDialog.BookMakingTab.PageNumberingStyle." + styleKey, styleKey);
				_numberStyleCombo.Items.Add(localizedStyle);
				if (styleKey == _collectionSettings.PageNumberStyle)
					_numberStyleCombo.SelectedIndex = _numberStyleCombo.Items.Count - 1;
			}
		}

		private void LoadBrandingCombo()
		{
		}

		/*
		 * Makes the combobox wide enough to display the longest value.
		 * Assumes all three font comboboxes have the same items.
		 */
		private void AdjustFontComboDropdownWidth()
		{
			int width = _fontComboLanguage1.DropDownWidth;
			using (Graphics g = _fontComboLanguage1.CreateGraphics())
			{
				Font font = _fontComboLanguage1.Font;
				int vertScrollBarWidth = (_fontComboLanguage1.Items.Count > _fontComboLanguage1.MaxDropDownItems) ? SystemInformation.VerticalScrollBarWidth : 0;

				width = (from string s in _fontComboLanguage1.Items select TextRenderer.MeasureText(g, s, font).Width + vertScrollBarWidth).Concat(new[] { width }).Max();
			}
			_fontComboLanguage1.DropDownWidth = width;
			_fontComboLanguage2.DropDownWidth = width;
			_fontComboLanguage3.DropDownWidth = width;
		}

		private void _cancelButton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}

		private void _helpButton_Click(object sender, EventArgs e)
		{
			if (_tab.SelectedTab == tabPage1)
				HelpLauncher.Show(this, "Tasks/Basic_tasks/Change_languages.htm");
			else if (_tab.SelectedTab == tabPage2)
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

		private void _fontComboLanguage1_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_fontComboLanguage1.SelectedItem.ToString().ToLowerInvariant() != _collectionSettings.Language1.FontName.ToLower())
				ChangeThatRequiresRestart();
		}

		private void _fontComboLanguage2_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_fontComboLanguage2.SelectedItem.ToString().ToLowerInvariant() != _collectionSettings.Language2.FontName.ToLower())
				ChangeThatRequiresRestart();
		}

		private void _fontComboLanguage3_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_fontComboLanguage3.SelectedItem.ToString().ToLowerInvariant() != _collectionSettings.Language3.FontName.ToLower())
				ChangeThatRequiresRestart();
		}

		private void _showExperimentalFeatures_CheckedChanged(object sender, EventArgs e)
		{
			Settings.Default.ShowExperimentalFeatures = _showExperimentalFeatures.Checked;
			ChangeThatRequiresRestart();
		}

		private void _xmatterList_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_xmatterList.SelectedItems.Count == 0 || _xmatterList.SelectedItems[0].Tag == null)
				_xmatterDescription.Text = "";
			else
				_xmatterDescription.Text = ((XMatterInfo)_xmatterList.SelectedItems[0].Tag).GetDescription();

			UpdateDisplay(); //may show restart required, if we have changed but not changed back to the orginal.
		}

		private void _tab_SelectedIndexChanged(object sender, EventArgs e)
		{
			if(_tab.SelectedIndex == 1)
				SetupXMatterList();
		}

		private void _automaticallyUpdate_CheckedChanged(object sender, EventArgs e)
		{
			Settings.Default.AutoUpdate = _automaticallyUpdate.Checked;
		}

		private void _fontSettings1Link_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			FontSettingsLinkClicked(_collectionSettings.Language1.Name, 1);
		}

		private void _fontSettings2Link_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			FontSettingsLinkClicked(_collectionSettings.Language2.Name, 2);
		}

		private void _fontSettings3Link_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			FontSettingsLinkClicked(_collectionSettings.Language3.Name, 3);
		}

		private void FontSettingsLinkClicked(string langName, int langNum1Based)
		{ 
			var langSpec = _collectionSettings.LanguagesZeroBased[langNum1Based - 1];
			using (var frm = new ScriptSettingsDialog())
			{
				frm.LanguageName = langName;
				frm.LanguageRightToLeft = langSpec.IsRightToLeft;
				frm.LanguageLineSpacing = langSpec.LineHeight;
				frm.UIFontSize = langSpec.BaseUIFontSizeInPoints;
				frm.BreakLinesOnlyAtSpaces = langSpec.BreaksLinesOnlyAtSpaces;
				frm.ShowDialog(this);

				// get the changes
				if (frm.LanguageRightToLeft != langSpec.IsRightToLeft) 
				{
					langSpec.IsRightToLeft = frm.LanguageRightToLeft;
					ChangeThatRequiresRestart();
				}

				// We don't need to restart, just gather the changes up. The caller
				// will save the .bloomCollection file. Later when a book
				// is edited, defaultLangStyles.css will be written out in the book's folder, which is all
				// that is needed for this setting to take effect.
				langSpec.LineHeight = frm.LanguageLineSpacing;
				langSpec.BreaksLinesOnlyAtSpaces = frm.BreakLinesOnlyAtSpaces;
				langSpec.BaseUIFontSizeInPoints = frm.UIFontSize;
			}
		}

		private CollectionSettingsApi.EnterpriseStatus GetEnterpriseStatus()
		{
			if (CollectionSettingsApi.FixEnterpriseSubscriptionCodeMode)
			{
				// We're being displayed to fix a branding code...select that option
				return CollectionSettingsApi.EnterpriseStatus.Subscription;
			}
			if (_brand == "Default")
				return CollectionSettingsApi.EnterpriseStatus.None;
			else if (_brand == "Local-Community")
				return CollectionSettingsApi.EnterpriseStatus.Community;
			return CollectionSettingsApi.EnterpriseStatus.Subscription; ;
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
			if (BrandingProject.HaveFilesForBranding(fullBrandingName))
			{
				if (_brand != fullBrandingName || DifferentSubscriptionCodes(subscriptionCode, _subscriptionCode))
				{
					Invoke((Action) ChangeThatRequiresRestart);
					_brand = fullBrandingName;
					_subscriptionCode = subscriptionCode;

					// if the branding.json specifies an xmatter, set the default for this collection to that.
					var correspondingXMatterPack = BrandingSettings.GetSettings(fullBrandingName).GetXmatterToUse();
					if (!string.IsNullOrEmpty(correspondingXMatterPack))
					{
						_collectionSettings.XMatterPackName = correspondingXMatterPack;
					}
				}

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

		private void showTroubleShooterCheckBox_CheckedChanged(object sender, EventArgs e)
		{
			if (showTroubleShooterCheckBox.Checked)
			{
				TroubleShooterDialog.ShowTroubleShooter();
			}
			else
			{
				TroubleShooterDialog.HideTroubleShooter();
			}
		}

		private void _numberStyleCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			ChangeThatRequiresRestart();
		}
	}
}
