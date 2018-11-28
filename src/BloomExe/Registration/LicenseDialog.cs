using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using SIL.IO;

namespace Bloom.Registration
{
	public partial class LicenseDialog : Form
	{
		private Browser _licenseBrowser;

		/// <summary>
		/// Initialize a new instance of the <see cref="Bloom.Registration.LicenseDialog"/> class.
		/// </summary>
		/// <param name="licenseMdFile">filename of the license in HTML format</param>
		/// <param name="prolog">prolog to the license (optional, already localized)</param>
		/// <remarks>
		/// The HTML file content should be suitable for inserting inside a body element.
		/// (I.e., it should not contain html, head, or body elements.)
		/// </remarks>
		public LicenseDialog(string licenseHtmlFile, string prolog = null)
		{
			InitializeComponent();

			Text = string.Format(Text, Assembly.GetExecutingAssembly().GetName().Version);

			// If there's no prolog, normalize the variable to an empty string.
			// If there is a prolog, add a horizontal rule separating the prolog from the license.
			if (String.IsNullOrWhiteSpace(prolog))
				prolog = String.Empty;
			else
				prolog = String.Format("<p>{0}</p>{1}<hr>{1}", prolog, Environment.NewLine);

			_licenseBrowser = new Browser();
			_licenseBrowser.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			_licenseBrowser.Location = new Point(12, 12);
			_licenseBrowser.Name = "licenseBrowser";
			_licenseBrowser.Size = new Size(_acceptButton.Right - 12, _acceptButton.Top - 24);
			Controls.Add(_licenseBrowser);
			var locale = CultureInfo.CurrentUICulture.Name;
			string licenseFilePath = BloomFileLocator.GetBrowserFile(false, licenseHtmlFile);
			var localizedLicenseFilePath = licenseFilePath.Substring(0, licenseFilePath.Length - 3) + "-" + locale + ".htm";
			if (RobustFile.Exists(localizedLicenseFilePath))
				licenseFilePath = localizedLicenseFilePath;
			else
			{
				var index = locale.IndexOf('-');
				if (index > 0)
				{
					locale = locale.Substring(0, index);
					localizedLicenseFilePath = licenseFilePath.Substring(0, licenseFilePath.Length - 3) + "-" + locale + ".htm";
					if (RobustFile.Exists(localizedLicenseFilePath))
						licenseFilePath = localizedLicenseFilePath;
				}
			}
			var contents = prolog + RobustFile.ReadAllText(licenseFilePath, Encoding.UTF8);
			var html = string.Format("<html><head><head/><body style=\"font-family:sans-serif\">{0}</body></html>", contents);
			_licenseBrowser.NavigateRawHtml(html);
			_licenseBrowser.Visible = true;
		}
	}
}
