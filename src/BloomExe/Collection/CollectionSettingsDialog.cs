using System;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Properties;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.WritingSystems;

namespace Bloom.Collection
{
	public partial class CollectionSettingsDialog : Form
	{
		public delegate CollectionSettingsDialog Factory();//autofac uses this

		private readonly CollectionSettings _collectionSettings;
		private XMatterPackFinder _xmatterPackFinder;
		private bool _restartRequired;

		public CollectionSettingsDialog(CollectionSettings collectionSettings, XMatterPackFinder xmatterPackFinder)
		{
			_collectionSettings = collectionSettings;
			_xmatterPackFinder = xmatterPackFinder;
			InitializeComponent();
			if(_collectionSettings.IsSourceCollection)
			{
				_language1Label.Text = "Language 1";
				_language2Label.Text = "Language 2";
				_language3Label.Text = "Language 3";
			}

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
			_language1Name.Text = string.Format("{0} ({1})", _collectionSettings.GetVernacularName("en"), _collectionSettings.Language1Iso639Code);
			_language2Name.Text = string.Format("{0} ({1})",  _collectionSettings.GetLanguage2Name("en"), _collectionSettings.Language2Iso639Code);

			if (string.IsNullOrEmpty(_collectionSettings.Language3Iso639Code))
			{
				_language3Name.Text = "--";
				_removeLanguage3Link.Visible = false;
			}
			else
			{
				_language3Name.Text = string.Format("{0} ({1})", _collectionSettings.GetLanguage3Name("en"), _collectionSettings.Language3Iso639Code);
				_removeLanguage3Link.Visible = true;
			}

			_countryText.Text = _collectionSettings.Country;
			_provinceText.Text = _collectionSettings.Province;
			_districtText.Text = _collectionSettings.District;
			_restartReminder.Visible = _restartRequired;

			_xmatterPackCombo.Items.Clear();
			_xmatterPackCombo.Items.AddRange(_xmatterPackFinder.All.ToArray());
			_xmatterPackCombo.SelectedItem = _xmatterPackFinder.FindByKey(_collectionSettings.XMatterPackName);
			if (_xmatterPackCombo.SelectedItem == null) //if something goes wrong
				_xmatterPackCombo.SelectedItem = _xmatterPackFinder.FactoryDefault;
		}

		private void _vernacularChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.Language1Iso639Code = ChangeLanguage(_collectionSettings.Language1Iso639Code);

			_restartRequired = true;
			UpdateDisplay();
		}
		private void _national1ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.Language2Iso639Code = ChangeLanguage( _collectionSettings.Language2Iso639Code);
			_restartRequired = true;
			UpdateDisplay();
		}

		private void _national2ChangeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.Language3Iso639Code = ChangeLanguage(_collectionSettings.Language3Iso639Code);
			_restartRequired = true;
			UpdateDisplay();
		}
		private void _removeSecondNationalLanguageButton_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			_collectionSettings.Language3Iso639Code = null;
			_restartRequired = true;
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
			Logger.WriteMinorEvent("Settings Dialog OK Clicked");

			_collectionSettings.XMatterPackName = ((XMatterInfo) _xmatterPackCombo.SelectedItem).Key;
			_collectionSettings.Country = _countryText.Text.Trim();
			_collectionSettings.Province = _provinceText.Text.Trim();
			_collectionSettings.District = _districtText.Text.Trim();

			//no point in letting them have the Nat lang 2 be the same as 1
			if (_collectionSettings.Language2Iso639Code == _collectionSettings.Language3Iso639Code)
				_collectionSettings.Language3Iso639Code = null;

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
			_restartRequired = true;
			UpdateDisplay();
		}

		private void CollectionSettingsDialog_Load(object sender, EventArgs e)
		{
			Logger.WriteEvent("Entered Settings Dialog");
			UsageReporter.SendNavigationNotice("Entered Settings Dialog");
		}
	}
}
