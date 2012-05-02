using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Bloom.Book;
using Palaso.UI.WindowsForms.WritingSystems;

namespace Bloom.Library
{
	public partial class SettingsDialog : Form
	{
		public delegate SettingsDialog Factory();//autofac uses this

		private readonly LibrarySettings _librarySettings;
		private XMatterPackFinder _xmatterPackFinder;
		private bool _restartMightBeNeeded;

		public SettingsDialog(LibrarySettings librarySettings, XMatterPackFinder xmatterPackFinder)
		{
			_librarySettings = librarySettings;
			_xmatterPackFinder = xmatterPackFinder;
			InitializeComponent();
			if(_librarySettings.IsShellLibrary)
			{
				_language1Label.Text = "Language 1";
				_language2Label.Text = "Language 2";
				_language3Label.Text = "Language 3";
			}
			UpdateDisplay();
		}

		private void UpdateDisplay()
		{
			_vernacularLanguageName.Text = string.Format("{0} ({1})", _librarySettings.GetVernacularName("en"), _librarySettings.VernacularIso639Code);
			_nationalLanguage1Label.Text = string.Format("{0} ({1})",  _librarySettings.GetNationalLanguage1Name("en"), _librarySettings.NationalLanguage1Iso639Code);

			if (string.IsNullOrEmpty(_librarySettings.NationalLanguage2Iso639Code))
			{
				_nationalLanguage2Label.Text = "--";
				_removeSecondNationalLanguageButton.Visible = false;
			}
			else
			{
				_nationalLanguage2Label.Text = string.Format("{0} ({1})", _librarySettings.GetNationalLanguage2Name("en"), _librarySettings.NationalLanguage2Iso639Code);
				_removeSecondNationalLanguageButton.Visible = true;
			}

			_countryText.Text = _librarySettings.Country;
			_provinceText.Text = _librarySettings.Province;
			_districtText.Text = _librarySettings.District;
			_restartMessage.Visible = _restartMightBeNeeded;

			_xmatterPackCombo.Items.Clear();
			_xmatterPackCombo.Items.AddRange(_xmatterPackFinder.All.ToArray());
			_xmatterPackCombo.SelectedItem = _xmatterPackFinder.FindByKey(_librarySettings.XMatterPackName);
			if (_xmatterPackCombo.SelectedItem == null) //if something goes wrong
				_xmatterPackCombo.SelectedItem = _xmatterPackFinder.FactoryDefault;
		}

		private void _vernacularChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_librarySettings.VernacularIso639Code = ChangeLanguage(_librarySettings.VernacularIso639Code);

			_restartMightBeNeeded = true;
			UpdateDisplay();
		}
		private void _national1ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_librarySettings.NationalLanguage1Iso639Code = ChangeLanguage( _librarySettings.NationalLanguage1Iso639Code);
			_restartMightBeNeeded = true;
			UpdateDisplay();
		}

		private void _national2ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_librarySettings.NationalLanguage2Iso639Code = ChangeLanguage(_librarySettings.NationalLanguage2Iso639Code);
			_restartMightBeNeeded = true;
			UpdateDisplay();
		}
		private void _removeSecondNationalLanguageButton_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_librarySettings.NationalLanguage2Iso639Code = null;
			_restartMightBeNeeded = true;
			UpdateDisplay();
		}

		private string ChangeLanguage( string currentIso639Code)
		{
			using (var dlg = new LookupISOCodeDialog())
			{
				if (DialogResult.OK != dlg.ShowDialog())
				{
					return currentIso639Code;
				}
				return dlg.ISOCodeAndName.Code;
			}
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			_librarySettings.XMatterPackName = ((XMatterInfo) _xmatterPackCombo.SelectedItem).Key;
			_librarySettings.Country = _countryText.Text.Trim();
			_librarySettings.Province = _provinceText.Text.Trim();
			_librarySettings.District = _districtText.Text.Trim();

			//no point in letting them have the Nat lang 2 be the same as 1
			if (_librarySettings.NationalLanguage1Iso639Code == _librarySettings.NationalLanguage2Iso639Code)
				_librarySettings.NationalLanguage2Iso639Code = null;

			_librarySettings.Save();
			Close();
		}

		private void label4_Click(object sender, EventArgs e)
		{

		}

		private void OnAboutLanguageSettings(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, "/Tasks/ProjectLibraryLevel_Tasks/Change_languages.htm");
		}

		private void OnAboutBookMakingSettings(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, "/Tasks/ProjectLibraryLevel_Tasks/Select_front_matter_or_back_matter_from_a_pack.htm");
		}

		private void OnAboutProjectInformationSetingsButton_Click(object sender, EventArgs e)
		{
			HelpLauncher.Show(this, "/Tasks/ProjectLibraryLevel_Tasks/Enter_project_information.htm");
		}
	}
}
