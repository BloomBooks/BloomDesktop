using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MarkdownSharp;

namespace Bloom.Registration
{
	public partial class LicenseDialog : Form
	{
		private Browser _licenseBrowser;
		public LicenseDialog()
		{
			InitializeComponent();

			Text = string.Format(Text, Assembly.GetExecutingAssembly().GetName().Version);

			_licenseBrowser = new Browser();
			_licenseBrowser.Isolator = new NavigationIsolator(); // never used while other browsers are around
			_licenseBrowser.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			_licenseBrowser.Location = new Point(176, 12);
			_licenseBrowser.Name = "licenseBrowser";
			_licenseBrowser.Size = new Size(_acceptButton.Right - 176, _acceptButton.Top - 24);
			Controls.Add(_licenseBrowser);
			var options = new MarkdownOptions() { LinkEmails = true, AutoHyperlink = true };
			var m = new Markdown(options);
			var contents = m.Transform(File.ReadAllText(BloomFileLocator.GetFileDistributedWithApplication("license.md"), Encoding.UTF8));
			var html = string.Format("<html><head><head/><body>{0}</body></html>", contents);
			_licenseBrowser.NavigateRawHtml(html);
			_licenseBrowser.Visible = true;
		}
	}
}
