using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Palaso.Xml;

namespace Bloom.Edit
{
	public partial class ConfigurationDialog : Form
	{
		private readonly string _filePath;

		public ConfigurationDialog(string filePath)
		{
			_filePath = filePath;
			InitializeComponent();
			//TODO: we need some way to make the configuration page visible, and all the pages invisible
			_browser.Navigate(filePath,false);
		}
		private void ConfigurationDialog_Load(object sender, EventArgs e)
		{
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			_browser.Save(_filePath);

			DialogResult = DialogResult.OK;
			Close();
		}

		private void _cancelButton_Click(object sender, EventArgs e)
		{
			DialogResult = DialogResult.Cancel;
			Close();
		}
	}
}
