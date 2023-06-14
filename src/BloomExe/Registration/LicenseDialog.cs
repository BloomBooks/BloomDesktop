using System;
using System.Drawing;
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
		/// <param name="licenseHtmlFile">filename of the license in HTML format</param>
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

			_licenseBrowser = BrowserMaker.MakeBrowser();
			_licenseBrowser.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
			_licenseBrowser.Location = new Point(12, 12);
			_licenseBrowser.Name = "licenseBrowser";
			_licenseBrowser.Size = new Size(_acceptButton.Right - 12, _acceptButton.Top - 24);
			Controls.Add(_licenseBrowser);

			string licenseFilePath = BloomFileLocator.GetBrowserFile(false, licenseHtmlFile);

			// Jun 2023:
			// 1. There was a bug in this code which meant we never found a localized file, even if it existed. (I fixed that.)
			// 2. We don't distribute any localized versions of the Bloom license or Adobe color profile file,
			//    so this is overhead for now; I'm commenting it out.
			//    If we ever decide to distribute localized versions, we just need to reenable this code.
			//    (And perhaps add a couple unit tests.)
			//var locale = CultureInfo.CurrentUICulture.Name;
			//var localizedLicenseFilePath = GetLocalizedLicenseFilePathIfExists(licenseFilePath, locale);
			//if (localizedLicenseFilePath != null)
			//	licenseFilePath = localizedLicenseFilePath;

			var contents = prolog + RobustFile.ReadAllText(licenseFilePath, Encoding.UTF8);
			var html = string.Format("<html><head><head/><body style=\"font-family:sans-serif\">{0}</body></html>", contents);
			_licenseBrowser.NavigateRawHtml(html);
			_licenseBrowser.Visible = true;
		}

		#region (currently) unused methods (see above)
		//private string GetLocalizedLicenseFilePathIfExists(string defaultLicenseFilePath, string locale)
		//{
		//	var localizedLicenseFilePath = GetLocalizedLicenseFilePathIfExistsInner(defaultLicenseFilePath, locale);
		//	if (localizedLicenseFilePath != null)
		//		return localizedLicenseFilePath;
		//	else
		//	{
		//		var index = locale.IndexOf('-');
		//		if (index > 0)
		//		{
		//			localizedLicenseFilePath = GetLocalizedLicenseFilePathIfExistsInner(defaultLicenseFilePath, locale.Substring(0, index));
		//			return localizedLicenseFilePath;
		//		}
		//	}

		//	return null;
		//}

		//private string GetLocalizedLicenseFilePathIfExistsInner(string defaultLicenseFilePath, string locale)
		//{
		//	var localizedLicenseFileName = $"{Path.GetFileNameWithoutExtension(defaultLicenseFilePath)}-{locale}.htm";
		//	var localizedLicenseFilePath = Path.Combine(Path.GetDirectoryName(defaultLicenseFilePath), localizedLicenseFileName);
		//	if (RobustFile.Exists(localizedLicenseFilePath))
		//		return localizedLicenseFilePath;

		//	return null;
		//}
		#endregion
	}
}
