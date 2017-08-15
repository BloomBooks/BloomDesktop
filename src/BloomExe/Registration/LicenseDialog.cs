using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Markdig;
using SIL.IO;

namespace Bloom.Registration
{
	public partial class LicenseDialog : Form
	{
		private Browser _licenseBrowser;

		/// <summary>
		/// Initialize a new instance of the <see cref="Bloom.Registration.LicenseDialog"/> class.
		/// </summary>
		/// <param name="licenseMdFile">filename of the license in Markdown format</param>
		/// <param name="prolog">prolog to the license (optional, already localized)</param>
		public LicenseDialog(string licenseMdFile, string prolog = null)
		{
			InitializeComponent();

			Text = string.Format(Text, Assembly.GetExecutingAssembly().GetName().Version);

			// If there's no prolog, normalize the variable to an empty string.
			// If there is a prolog, add a horizontal rule separating the prolog from the license.  (The double
			// newlines are significant to the markdown.)
			if (String.IsNullOrWhiteSpace(prolog))
				prolog = String.Empty;
			else
				prolog = String.Format("{0}{1}{1}---{1}{1}", prolog, Environment.NewLine);

			_licenseBrowser = new Browser();
			_licenseBrowser.Isolator = new NavigationIsolator(); // never used while other browsers are around
			_licenseBrowser.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			_licenseBrowser.Location = new Point(12, 12);
			_licenseBrowser.Name = "licenseBrowser";
			_licenseBrowser.Size = new Size(_acceptButton.Right - 12, _acceptButton.Top - 24);
			Controls.Add(_licenseBrowser);
			var locale = CultureInfo.CurrentUICulture.Name;
			string licenseFilePath = BloomFileLocator.GetFileDistributedWithApplication(licenseMdFile);
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
			var markdown = prolog + RobustFile.ReadAllText(licenseFilePath, Encoding.UTF8);
			// enable autolinks from text `http://`, `https://`, `ftp://`, `mailto:`, `www.xxx.yyy`
			var pipeline = new MarkdownPipelineBuilder().UseAutoLinks().UseCustomContainers().UseGenericAttributes().Build();
			var contents = Markdown.ToHtml(markdown, pipeline);
			var html = string.Format("<html><head><head/><body style=\"font-family:sans-serif\">{0}</body></html>", contents);
			_licenseBrowser.NavigateRawHtml(html);
			_licenseBrowser.Visible = true;
		}
	}
}
