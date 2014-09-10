using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using L10NSharp;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.WritingSystems;
using Palaso.Extensions;
using Palaso.WritingSystems;

namespace Bloom.Collection
{
	public partial class CollectionSettingsDialog : Form
	{
		public delegate CollectionSettingsDialog Factory();//autofac uses this

		private readonly CollectionSettings _collectionSettings;
		private XMatterPackFinder _xmatterPackFinder;
	    private readonly QueueRenameOfCollection _queueRenameOfCollection;
	    private bool _restartRequired;
		private bool _loaded;

		public CollectionSettingsDialog(CollectionSettings collectionSettings, XMatterPackFinder xmatterPackFinder, QueueRenameOfCollection queueRenameOfCollection)
		{
			_collectionSettings = collectionSettings;
			_xmatterPackFinder = xmatterPackFinder;
		    _queueRenameOfCollection = queueRenameOfCollection;
		    InitializeComponent();
			if(_collectionSettings.IsSourceCollection)	
			{
                _language1Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language1InSourceCollection", "Language 1", "In a vernacular collection, we say 'Vernacular Language', but in a source collection, Vernacular has no relevance, so we use this different label");
                _language2Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language2InSourceCollection", "Language 2", "In a vernacular collection, we say 'Language 2 (e.g. National Language)', but in a source collection, National Language has no relevance, so we use this different label");
                _language3Label.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.Language3InSourceCollection", "Language 3", "In a vernacular collection, we say 'Language 3 (e.g. Regional Language)', but in a source collection, National Language has no relevance, so we use this different label"); 
			}

		    _showSendReceive.Checked = Settings.Default.ShowSendReceive;
		    _showExperimentalTemplates.Checked = Settings.Default.ShowExperimentalBooks;
			_showExperimentCommands.Checked = Settings.Default.ShowExperimentalCommands;
			_rtlLanguagesCombo.Text = LocalizationManager.GetString("CollectionSettingsDialog.BookMakingTab.RightToLeft", "Right To Left");
			_rtlLanguagesCombo.ToolTipText =
				LocalizationManager.GetString("CollectionSettingsDialog.BookMakingTab.RightToLeftTip", "Select languages that are written from right to left");

//		    _showSendReceive.CheckStateChanged += (sender, args) =>
//		                                              {
//		                                                  Settings.Default.ShowSendReceive = _showSendReceive.CheckState ==
//		                                                                                     CheckState.Checked;
//
//                                                          _restartRequired = true;
//		                                                  UpdateDisplay();
//		                                              };
		    
			switch(Settings.Default.ImageHandler)
			{
				case "":
					_useImageServer.CheckState = CheckState.Checked;
					break;
				case "off":
					_useImageServer.CheckState = CheckState.Unchecked;
					break;
				case "http":
					_useImageServer.CheckState = CheckState.Checked;
					break;
			}
//			this._useImageServer.CheckedChanged += new System.EventHandler(this._useImageServer_CheckedChanged);
			_useImageServer.CheckStateChanged += new EventHandler(_useImageServer_CheckedChanged);

			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			var lang1UiName = _collectionSettings.GetLanguage1Name(LocalizationManager.UILanguageId);
			var lang2UiName = _collectionSettings.GetLanguage2Name(LocalizationManager.UILanguageId);
			_language1Name.Text = string.Format("{0} ({1})", lang1UiName, _collectionSettings.Language1Iso639Code);
			_language2Name.Text = string.Format("{0} ({1})", lang2UiName, _collectionSettings.Language2Iso639Code);
			_language1FontLabel.Text = string.Format("Default Font for {0}", lang1UiName);
			_language2FontLabel.Text = string.Format("Default Font for {0}", lang2UiName);
			_rtlLanguagesCombo.DropDownItems.Clear();
			_rtlLanguagesCombo.DropDownItems.Add(lang1UiName);
			_rtlLanguagesCombo.DropDownItems.Add(lang2UiName);

			if (string.IsNullOrEmpty(_collectionSettings.Language3Iso639Code))
			{
				_language3Name.Text = "--";
				_removeLanguage3Link.Visible = false;
				_language3FontLabel.Visible = false;
				_fontComboLanguage3.Visible = false;
				_changeLanguage3Link.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.SetThirdLanguageLink", "Set...", "If there is no third language specified, the link changes to this.");
			}
			else
			{
				var lang3UiName = _collectionSettings.GetLanguage3Name(LocalizationManager.UILanguageId);
				_language3Name.Text = string.Format("{0} ({1})", lang3UiName, _collectionSettings.Language3Iso639Code);
				_language3FontLabel.Text = string.Format("Default Font for {0}", lang3UiName);
				_removeLanguage3Link.Visible = true;
				_language3FontLabel.Visible = true;
				_fontComboLanguage3.Visible = true;
				_changeLanguage3Link.Text = LocalizationManager.GetString("CollectionSettingsDialog.LanguageTab.ChangeLanguageLink", "Change...");
				_rtlLanguagesCombo.DropDownItems.Add(lang3UiName);
			}

			_restartReminder.Visible = _restartRequired;
            _okButton.Text = _restartRequired ? LocalizationManager.GetString("CollectionSettingsDialog.Restart", "Restart", "If you make certain changes in the settings dialog, the OK button changes to this.") : LocalizationManager.GetString("Common.OKButton", "&OK");
			
			_xmatterPackCombo.Items.Clear();
			_xmatterPackCombo.Items.AddRange(_xmatterPackFinder.All.ToArray());
			_xmatterPackCombo.SelectedItem = _xmatterPackFinder.FindByKey(_collectionSettings.XMatterPackName); 
			if (_xmatterPackCombo.SelectedItem == null) //if something goes wrong
				_xmatterPackCombo.SelectedItem = _xmatterPackFinder.FactoryDefault;
		}

		private void _language1ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			//at this point, we don't let them customize the national languages
			
			var potentiallyCustomName = _collectionSettings.IsSourceCollection ? null: _collectionSettings.Language1Name;

			var l = ChangeLanguage(_collectionSettings.Language1Iso639Code, potentiallyCustomName);

			if (l != null)
			{
				_collectionSettings.Language1Iso639Code = l.Code;
				_collectionSettings.Language1Name = l.DesiredName;
				_restartRequired = true;
				UpdateDisplay();
			}
		}
		private void _language2ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var l = ChangeLanguage(_collectionSettings.Language2Iso639Code);
			if (l != null)
			{
				_collectionSettings.Language2Iso639Code = l.Code;
				_restartRequired = true;
				UpdateDisplay();
			}
		}

		private void _language3ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			var l = ChangeLanguage(_collectionSettings.Language3Iso639Code);
			if (l != null)
			{
				_collectionSettings.Language3Iso639Code = l.Code;
				_restartRequired = true;
				UpdateDisplay();
			}
		}
		private void _removeSecondNationalLanguageButton_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.Language3Iso639Code = null;
			_restartRequired = true; 
			UpdateDisplay();
		}

		private LanguageInfo ChangeLanguage(string iso639Code, string potentiallyCustomName=null)
		{
			using (var dlg = new LookupISOCodeDialog())
			{
				//at this point, we don't let them customize the national languages
				dlg.ShowDesiredLanguageNameField = potentiallyCustomName != null;

				dlg.SelectedLanguage = new LanguageInfo() { Code = iso639Code};
				if(!string.IsNullOrEmpty(potentiallyCustomName))
				{
					dlg.SelectedLanguage.DesiredName = potentiallyCustomName;
				}

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

			_collectionSettings.XMatterPackName = ((XMatterInfo) _xmatterPackCombo.SelectedItem).Key;
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

			//no point in letting them have the Nat lang 2 be the same as 1
			if (_collectionSettings.Language2Iso639Code == _collectionSettings.Language3Iso639Code)
				_collectionSettings.Language3Iso639Code = null;
            
            if(_bloomCollectionName.Text.Trim()!=_collectionSettings.CollectionName)
            {
                _queueRenameOfCollection.Raise(_bloomCollectionName.Text.SanitizeFilename('-'));
                //_collectionSettings.PrepareToRenameCollection(_bloomCollectionName.Text.SanitizeFilename('-'));
            }
			_collectionSettings.Save();

			Logger.WriteEvent("Closing Settings Dialog");
			Close();
			DialogResult = _restartRequired ? DialogResult.Yes : DialogResult.OK;
		}

		private void label4_Click(object sender, EventArgs e)
		{

		}

		private void OnAboutLanguageSettings(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, "Tasks/Basic_tasks/Change_languages.htm");
		}

		private void OnAboutBookMakingSettings(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, "Tasks/Basic_tasks/Select_front_matter_or_back_matter_from_a_pack.htm");
		}

		private void OnAboutProjectInformationSetingsButton_Click(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, "Tasks/Basic_tasks/Enter_project_information.htm");
		}

		private void _useImageServer_CheckedChanged(object sender, EventArgs e)
		{
			if (_useImageServer.CheckState == CheckState.Unchecked
				//&& (Settings.Default.ImageHandler == "http" || Settings.Default.ImageHandler == "")
	&& DialogResult.Yes != MessageBox.Show(
	"Don't turn the image server off unless you are trying to solve a problem... it will likely just cause other problems.\r\n\r\n Really turn it off?", "Really?", MessageBoxButtons.YesNo))
			{
				var oldRestartRequired = _restartRequired;//don't restart if they repented of clicking the button
				_useImageServer.CheckState = CheckState.Checked;
				_restartRequired = oldRestartRequired;
				UpdateDisplay();
				return;
			}

			switch(_useImageServer.CheckState)
			{
				case CheckState.Unchecked:
					Settings.Default.ImageHandler = "off";
					break;
				case CheckState.Checked:
					Settings.Default.ImageHandler = "http";
					break;
//				case CheckState.Indeterminate:
//					Settings.Default.ImageHandler = "";//leave at default
//					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
			RestartRequired();
		}

		private void RestartRequired()
		{
			if (!_loaded)//ignore false events that come while setting upt the dialog
				return;

			_restartRequired = true;
			UpdateDisplay();
		}

		private void OnLoad(object sender, EventArgs e)
		{
            _countryText.Text = _collectionSettings.Country;
            _provinceText.Text = _collectionSettings.Province;
            _districtText.Text = _collectionSettings.District;
            _bloomCollectionName.Text = _collectionSettings.CollectionName;
		    LoadFontCombo();

			_loaded = true;
			Logger.WriteEvent("Entered Settings Dialog");
		}

	    private void LoadFontCombo()
	    {
            foreach (FontFamily fontFamily in FontFamily.Families)
            {
                _fontComboLanguage1.Items.Add(fontFamily.Name);
				_fontComboLanguage2.Items.Add(fontFamily.Name);
				_fontComboLanguage3.Items.Add(fontFamily.Name);
				if (fontFamily.Name == _collectionSettings.DefaultLanguage1FontName)
                    _fontComboLanguage1.SelectedIndex = _fontComboLanguage1.Items.Count-1;
				if (fontFamily.Name == _collectionSettings.DefaultLanguage2FontName)
					_fontComboLanguage2.SelectedIndex = _fontComboLanguage2.Items.Count - 1;
				if (fontFamily.Name == _collectionSettings.DefaultLanguage3FontName)
					_fontComboLanguage3.SelectedIndex = _fontComboLanguage3.Items.Count - 1;
			}
	    }


	    private void _cancelButton_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            Close();
        }

        private void _bloomCollectionName_TextChanged(object sender, EventArgs e)
        {
            if (_bloomCollectionName.Text.Trim() == _collectionSettings.CollectionName)
                return;


            RestartRequired();
        }

        private void _fontComboLanguage1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if(_fontComboLanguage1.SelectedItem.ToString().ToLower() != _collectionSettings.DefaultLanguage1FontName.ToLower())
                RestartRequired();
        }

		private void _fontComboLanguage2_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_fontComboLanguage2.SelectedItem.ToString().ToLower() != _collectionSettings.DefaultLanguage2FontName.ToLower())
				RestartRequired();
		}

		private void _fontComboLanguage3_SelectedIndexChanged(object sender, EventArgs e)
		{
			if (_fontComboLanguage3.SelectedItem.ToString().ToLower() != _collectionSettings.DefaultLanguage3FontName.ToLower())
				RestartRequired();
		}

        private void _showSendReceive_CheckedChanged(object sender, EventArgs e)
        {
            Settings.Default.ShowSendReceive = _showSendReceive.Checked;
            RestartRequired();
        }

        private void _showExperimentalTemplates_CheckedChanged(object sender, EventArgs e)
        {
            Settings.Default.ShowExperimentalBooks = _showExperimentalTemplates.Checked;
            RestartRequired();
        }

		private void checkBox1_CheckedChanged(object sender, EventArgs e)
		{
			Settings.Default.ShowExperimentalCommands = _showExperimentCommands.Checked;
			RestartRequired();
		}

	}
}
