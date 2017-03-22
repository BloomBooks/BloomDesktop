using System.Diagnostics;
using System.Windows.Forms;
using Bloom.Properties;

namespace Bloom.MiscUI
{
	public partial class InstallationFailedDialog : Form
	{
		public InstallationFailedDialog()
		{
			InitializeComponent();

			Icon = Resources.BloomIcon;
		}

		private void _linkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			Process.Start("https://community.software.sil.org/t/how-to-fix-installation-problems");
		}
	}
}
