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
using System.IO;

namespace Bloom.Collection
{
	public partial class CollectionSettingsDialog : Form
	{
		public delegate CollectionSettingsDialog Factory();//autofac uses this

		private readonly CollectionSettings _collectionSettings;
		private XMatterPackFinder _xmatterPackFinder;
		private readonly QueueRenameOfCollection _queueRenameOfCollection;
		private readonly PageRefreshEvent _pageRefreshEvent;
		private bool _restartRequired;
		private bool _loaded;

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
				_language1Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language1InSourceCollection", "Language 1", "In a vernacular collection, we say 'Vernacular Language', but in a source collection, Vernacular has no relevance, so we use this different label");
				_language2Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language2InSourceCollection", "Language 2", "In a vernacular collection, we say 'Language 2 (e.g. National Language)', but in a source collection, National Language has no relevance, so we use this different label");
				_language3Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language3InSourceCollection", "Language 3", "In a vernacular collection, we say 'Language 3 (e.g. Regional Language)', but in a source collection, National Language has no relevance, so we use this different label");
			}

			_showExperimentalTemplates.Checked = Settings.Default.ShowExperimentalBooks;
			_showExperimentCommands.Checked = Settings.Default.ShowExperimentalCommands;
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


			UpdateDisplay();
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			// BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
			this.Icon = global::Bloom.Properties.Resources.BloomIcon;
		}

		private void UpdateDisplay()
		{
			string defaultFontText =
				LocalizationManager.GetString("CollectionSettingsDialog.BookMakingTab.DefaultFontFor", "Default Font for {0}", "{0} is a language name.");
			var lang1UiName = _collectionSettings.GetLanguage1Name(LocalizationManager.UILanguageId);
			var lang2UiName = _collectionSettings.GetLanguage2Name(LocalizationManager.UILanguageId);
			_language1Name.Text = string.Format("{0} ({1})", lang1UiName, _collectionSettings.Language1Iso639Code);
			_language2Name.Text = string.Format("{0} ({1})", lang2UiName, _collectionSettings.Language2Iso639Code);
			_language1FontLabel.Text = string.Format(defaultFontText, lang1UiName);
			_language2FontLabel.Text = string.Format(defaultFontText, lang2UiName);

			var lang3UiName = string.Empty;
			if (string.IsNullOrEmpty(_collectionSettings.Language3Iso639Code))
			{
				_language3Name.Text = "--";
				_removeLanguage3Link.Visible = false;
				_language3FontLabel.Visible = false;
				_fontComboLanguage3.Visible = false;
				_fontSettings3Link.Visible = false;
				_changeLanguage3Link.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink", "Set...", "If there is no third language specified, the link changes to this.");
			}
			else
			{
				lang3UiName = _collectionSettings.GetLanguage3Name(LocalizationManager.UILanguageId);
				_language3Name.Text = string.Format("{0} ({1})", lang3UiName, _collectionSettings.Language3Iso639Code);
				_language3FontLabel.Text = string.Format(defaultFontText, lang3UiName);
				_removeLanguage3Link.Visible = true;
				_language3FontLabel.Visible = true;
				_fontComboLanguage3.Visible = true;
				_fontSettings3Link.Visible = true;
				_changeLanguage3Link.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.ChangeLanguageLink", "Change...");
			}

			_restartReminder.Visible = AnyReasonToRestart();
			_okButton.Text = AnyReasonToRestart() ? LocalizationManager.GetString("CollectionSettingsDialog.Restart", "Restart", "If you make certain changes in the settings dialog, the OK button changes to this.") : LocalizationManager.GetString("Common.OKButton", "&OK");
		}

		private void _language1ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			//at this point, we don't let them customize the national languages

			var potentiallyCustomName = _collectionSettings.IsSourceCollection ? null: _collectionSettings.Language1Name;

			var l = ChangeLanguage(_collectionSettings.Language1Iso639Code, potentiallyCustomName);

			if (l != null)
			{
				_collectionSettings.Language1Iso639Code = l.LanguageTag;
				_collectionSettings.Language1Name = l.DesiredName;
				ChangeThatRequiresRestart();
			}
		}
		private void _language2ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var l = ChangeLanguage(_collectionSettings.Language2Iso639Code);
			if (l != null)
			{
				_collectionSettings.Language2Iso639Code = l.LanguageTag;
				ChangeThatRequiresRestart();
			}
		}

		private void _language3ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var l = ChangeLanguage(_collectionSettings.Language3Iso639Code);
			if (l != null)
			{
				_collectionSettings.Language3Iso639Code = l.LanguageTag;
				ChangeThatRequiresRestart();
			}
		}
		private void _removeSecondNationalLanguageButton_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.Language3Iso639Code = null;
			ChangeThatRequiresRestart();
		}

		private LanguageInfo ChangeLanguage(string iso639Code, string potentiallyCustomName=null)
		{
			using (var dlg = new LanguageLookupDialog())
			{
				//at this point, we don't let them customize the national languages
				dlg.IsDesiredLanguageNameFieldVisible = potentiallyCustomName != null;

				var language = new LanguageInfo() { LanguageTag = iso639Code};
				if (!string.IsNullOrEmpty(potentiallyCustomName))
				{
					language.DesiredName = potentiallyCustomName; // to be noticed, must set before dlg.SelectedLanguage
				}
				dlg.SelectedLanguage = language;
				dlg.SearchText = iso639Code;

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
				_collectionSettings.DefaultLanguage1FontName = _fontComboLanguage1.SelectedItem.ToString();
			}
			if (_fontComboLanguage2.SelectedItem != null)
			{
				_collectionSettings.DefaultLanguage2FontName = _fontComboLanguage2.SelectedItem.ToString();
			}
			if (_fontComboLanguage3.SelectedItem != null)
			{
				_collectionSettings.DefaultLanguage3FontName = _fontComboLanguage3.SelectedItem.ToString();
			}
			if (_numberStyleCombo.SelectedItem != null)
			{
				_collectionSettings.PageNumberStyle = CollectionSettings.PageNumberStyleKeys[_numberStyleCombo.SelectedIndex]; // this must be the non-localized version
			}
			if (_brandingCombo.SelectedItem != null)
			{
				_collectionSettings.BrandingProjectName = _brandingCombo.SelectedItem.ToString();
			}

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
			
			_loaded = true;
			Logger.WriteEvent("Entered Settings Dialog");
		}

		/// <summary>
		/// NB The selection stuff is flaky if we attempt to select things before the control is all created, settled down, bored.
		/// </summary>
		private void SetupXMatterList()
		{
			var packsToSkip = new string[] {"null", "bigbook", "SHRP", "SHARP", "ForUnitTest"};
			_xmatterList.Items.Clear();
			ListViewItem itemForFactoryDefault = null;
			foreach(var pack in _xmatterPackFinder.All)
			{
				if (packsToSkip.Any(s => pack.Key.ToLowerInvariant().Contains(s.ToLower())))
					continue;

				var labelToShow = LocalizationManager.GetDynamicString("Bloom","CollectionSettingsDialog.BookMakingTab.Front/BackMatterPack."+pack.Key, pack.EnglishLabel, "Name of a Front/Back Matter Pack");
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
				if (font == _collectionSettings.DefaultLanguage1FontName)
					_fontComboLanguage1.SelectedIndex = _fontComboLanguage1.Items.Count-1;
				if (font == _collectionSettings.DefaultLanguage2FontName)
					_fontComboLanguage2.SelectedIndex = _fontComboLanguage2.Items.Count - 1;
				if (font == _collectionSettings.DefaultLanguage3FontName)
					_fontComboLanguage3.SelectedIndex = _fontComboLanguage3.Items.Count - 1;
			}
		}


		private void LoadPageNumberStyleCombo()
		{
			foreach (var styleKey in CollectionSettings.PageNumberStyleKeys)
			{
				var localizedStyle =
					LocalizationManager.GetString("CollectionSettingsDialog.BookMakingTab.PageNumberingStyle." + styleKey, styleKey);
				_numberStyleCombo.Items.Add(localizedStyle);
				if (styleKey == _collectionSettings.PageNumberStyle)
					_numberStyleCombo.SelectedIndex = _numberStyleCombo.Items.Count - 1;
			}
		}

		private void LoadBrandingCombo()
		{
			var brandingDirectory = BloomFileLocator.GetDirectoryDistributedWithApplication("branding");
			foreach(var brandDirectory in Directory.GetDirectories(brandingDirectory))
			{
				var brand = Path.GetFileName(brandDirectory);
				_brandingCombo.Items.Add(brand);
				if (brand == _collectionSettings.BrandingProjectName)
					_brandingCombo.SelectedIndex = _brandingCombo.Items.Count - 1;
			}
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

				width = (from string s in _fontComboLanguage1.Items select (int)g.MeasureString(s, font).Width + vertScrollBarWidth).Concat(new[] { width }).Max();
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
			if (_fontComboLanguage1.SelectedItem.ToString().ToLowerInvariant() != _collectionSettings.DefaultLanguage1FontName.ToLower())
				ChangeThatRequiresRestart();
		}

		private void _fontComboLanguage2_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_fontComboLanguage2.SelectedItem.ToString().ToLowerInvariant() != _collectionSettings.DefaultLanguage2FontName.ToLower())
				ChangeThatRequiresRestart();
		}

		private void _fontComboLanguage3_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_fontComboLanguage3.SelectedItem.ToString().ToLowerInvariant() != _collectionSettings.DefaultLanguage3FontName.ToLower())
				ChangeThatRequiresRestart();
		}

		private void _showExperimentalTemplates_CheckedChanged(object sender, EventArgs e)
		{
			Settings.Default.ShowExperimentalBooks = _showExperimentalTemplates.Checked;
			ChangeThatRequiresRestart();
		}

		private void checkBox1_CheckedChanged(object sender, EventArgs e)
		{
			Settings.Default.ShowExperimentalCommands = _showExperimentCommands.Checked;
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
			FontSettingsLinkClicked(_collectionSettings.GetLanguage1Name(LocalizationManager.UILanguageId), 1);
		}

		private void _fontSettings2Link_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			FontSettingsLinkClicked(_collectionSettings.GetLanguage2Name(LocalizationManager.UILanguageId), 2);
		}

		private void _fontSettings3Link_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			FontSettingsLinkClicked(_collectionSettings.GetLanguage3Name(LocalizationManager.UILanguageId), 3);
		}

		private void FontSettingsLinkClicked(string langName, int langNum)
		{
			using (var frm = new ScriptSettingsDialog())
			{
				frm.LanguageName = langName;
				frm.LanguageRightToLeft = _collectionSettings.GetLanguageRtl(langNum);
				frm.LanguageLineSpacing = _collectionSettings.GetLanguageLineHeight(langNum);
				frm.ShowDialog(this);

				// get the changes
				var newRtl = frm.LanguageRightToLeft;
				var newLs = frm.LanguageLineSpacing;

				if (newRtl != _collectionSettings.GetLanguageRtl(langNum))
				{
					_collectionSettings.SetLanguageRtl(langNum, newRtl);
					ChangeThatRequiresRestart();
				}

				if (newLs != _collectionSettings.GetLanguageLineHeight(langNum))
				{
					_collectionSettings.SetLanguageLineHeight(langNum, newLs);
					ChangeThatRequiresRestart();
				}
			}
		}

		private void _brandingCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_brandingCombo.SelectedItem.ToString() != _collectionSettings.BrandingProjectName)
				ChangeThatRequiresRestart();
		}
	}
}
