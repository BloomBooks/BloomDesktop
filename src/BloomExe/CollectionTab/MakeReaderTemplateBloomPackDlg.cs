using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Bloom.CollectionTab
{
	public partial class MakeReaderTemplateBloomPackDlg : Form
	{
		private string _willCarrySettingsOriginal;
		public MakeReaderTemplateBloomPackDlg()
		{
			InitializeComponent();
			_willCarrySettingsOriginal = _willCarrySettingsLabel.Text;
			_btnSaveBloomPack.Enabled = false; // only enable if checkbox is checked
		}

		public void SetLanguage(string name)
		{
			_willCarrySettingsLabel.Text = string.Format(_willCarrySettingsOriginal, name);
		}

		public void SetTitles(IEnumerable<string> files)
		{
			_bookList.SuspendLayout();
			_bookList.Items.Clear();
			var titles = files.Where(f => !string.IsNullOrWhiteSpace(f));
			_bookList.Items.AddRange(titles.ToArray());
			_bookList.ResumeLayout();
		}

		private void _confirmationCheckBox_CheckedChanged(object sender, System.EventArgs e)
		{
			_btnSaveBloomPack.Enabled = _confirmationCheckBox.Checked;
		}

		private void _helpButton_Click(object sender, System.EventArgs e)
		{
			HelpLauncher.Show(this, "Concepts/Bloom_Pack.htm");
		}
	}
}
