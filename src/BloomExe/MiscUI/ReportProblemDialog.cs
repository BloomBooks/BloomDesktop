using System;
using System.Windows.Forms;

namespace Bloom.MiscUI
{
	public partial class ReportProblemDialog : Form
	{
		public ReportProblemDialog()
		{
			InitializeComponent();
		}

		private void ReportProblemDialog_Load(object sender, EventArgs e)
		{
			var labelText = _linkLabel.Text;
			const string emailAddress = "issues@bloomlibrary.org";

			_linkLabel.Links.Clear();

			var linkPosition = labelText.IndexOf(emailAddress, StringComparison.Ordinal);

			if (linkPosition == -1) return;

			_linkLabel.Links.Add(linkPosition, emailAddress.Length, "mailto:" + emailAddress);
		}

		private void _linkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
		{
			System.Diagnostics.Process.Start(e.Link.LinkData.ToString());
		}
	}
}
