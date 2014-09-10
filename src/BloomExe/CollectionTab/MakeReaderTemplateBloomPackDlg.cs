using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using L10NSharp;

namespace Bloom.CollectionTab
{
	public partial class MakeReaderTemplateBloomPackDlg : Form
	{
		private string _willCarrySettingsOriginal;
		public MakeReaderTemplateBloomPackDlg()
		{
			InitializeComponent();
			_willCarrySettingsOriginal = _willCarrySettingsLabel.Text;


			//another work around for the problem described in https://jira.sil.org/browse/BL-316
			_willCarrySettingsLabel.Text = LocalizationManager.GetString("ReaderTemplateBloomPackDialog.ExplanationParagraph",
				"In addition, this BloomPack will carry your latest decodable and levelled reader settings for the \"{0}\" language. Anyone opening this BloomPack , who then opens a \"{0}\" collection, will have their current decodable and leveled reader settings replaced by the settings in this BloomPack. They will also get the current set of words for use in decodable readers.");
		}

		public void SetLanguage(string name)
		{
			_willCarrySettingsLabel.Text = string.Format(_willCarrySettingsOriginal, name);
		}

		public void SetTitles(IEnumerable<string> files)
		{
			_bookList.SuspendLayout();
			_bookList.Items.Clear();
			_bookList.Items.AddRange(files.ToArray());
			_bookList.ResumeLayout();
		}
	}
}
