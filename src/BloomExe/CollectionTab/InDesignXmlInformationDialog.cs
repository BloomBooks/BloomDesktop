using System;
using System.Windows.Forms;
using Bloom.Properties;

namespace Bloom.CollectionTab
{
	public partial class InDesignXmlInformationDialog : Form
	{
		public InDesignXmlInformationDialog()
		{
			InitializeComponent();
			dontShowThisAgainButton1.CloseIfShouldNotShow(Settings.Default, "InDesignXmlInformationDialog");
		}

		private void button1_Click(object sender, EventArgs e)
		{
			Close();
		}
	}
}
