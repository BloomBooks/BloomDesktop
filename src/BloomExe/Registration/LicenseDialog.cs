using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using MarkdownSharp;
using SIL.IO;

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
			_licenseBrowser.Location = new Point(12, 12);
			_licenseBrowser.Name = "licenseBrowser";
			_licenseBrowser.Size = new Size(_acceptButton.Right - 12, _acceptButton.Top - 24);
			Controls.Add(_licenseBrowser);
			var options = new MarkdownOptions() { LinkEmails = true, AutoHyperlink = true };
			var m = new Markdown(options);
			var locale = CultureInfo.CurrentUICulture.Name;
			string licenseFilePath = BloomFileLocator.GetFileDistributedWithApplication("license.md");
			var localizedLicenseFilePath = licenseFilePath.Substring(0, licenseFilePath.Length - 3) + "-" + locale + ".md";
			if (RobustFile.Exists(localizedLicenseFilePath))
				licenseFilePath = localizedLicenseFilePath;
			else
			{
				var index = locale.IndexOf('-');
				if (index > 0)
				{
					locale = locale.Substring(0, index);
					localizedLicenseFilePath = licenseFilePath.Substring(0, licenseFilePath.Length - 3) + "-" + locale + ".md";
					if (RobustFile.Exists(localizedLicenseFilePath))
						licenseFilePath = localizedLicenseFilePath;
				}
			}
			var contents = m.Transform(RobustFile.ReadAllText(licenseFilePath, Encoding.UTF8));
			var html = string.Format("<html><head><head/><body>{0}</body></html>", contents);
			_licenseBrowser.NavigateRawHtml(html);
			_licenseBrowser.Visible = true;
		}
	}
}
