using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using SIL.Extensions;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;

namespace Bloom.MiscUI
{
	public partial class BloomIntegrityDialog : Form
	{
		public BloomIntegrityDialog()
		{
			InitializeComponent();
		}
		/// <summary>
		/// We've had a number of user reports that suggest that files were either missing or inaccessible.
		/// The idea here is to check a set of files and folders at the start of each launch, and generate
		/// a useful report if anything is missing.
		/// </summary>
		/// <returns>true if all is well. Application should exit if this returns false.</returns>
		public static bool CheckIntegrity()
		{
			var errors = new StringBuilder();
			var files = new[] { "Bloom.chm", "PdfDroplet.exe",
#if Chorus
				"Chorus.exe",
#endif
				"BloomPdfMaker.exe", "optipng.exe" };

				string[] dirs;
			if (Platform.IsWindows)
				dirs = new[] { "AndikaNewBasic", "localization", "xslts", "icons" };
			else
				dirs = new[] { "localization", "xslts", "icons" };

			foreach(var fileName in files)
			{
				if(!Platform.IsWindows && fileName == "optipng.exe")
				{
					// optipng is provided by a package dependency, will be found as /usr/bin/optipng (no .exe)
					continue;
				}
				if(FileLocationUtilities.GetFileDistributedWithApplication(true, fileName) == null)
				{
					//In a code directory, the FileLocator considers the solution the root, so it can't find files in output\debug
					if(!RobustFile.Exists(Path.Combine(FileLocationUtilities.DirectoryOfTheApplicationExecutable, fileName)))
					{
						//maybe it's an exe in distfiles?
						if(fileName.EndsWith(".exe") && RobustFile.Exists(Path.Combine(FileLocationUtilities.DirectoryOfApplicationOrSolution, "DistFiles")))
						{
							continue;
						}
						errors.AppendFormat("<p>Missing File: {0}</p>{1}", fileName, Environment.NewLine);
					}
				}
			}
			foreach(var directory in dirs)
			{
				if(FileLocationUtilities.GetDirectoryDistributedWithApplication(true, directory) == null)
				{
					errors.AppendFormat("<p>Missing Directory: {0}</p>{1}", directory, Environment.NewLine);
				}
			}
			if(errors.Length == 0)
				return true;

			using(var dlg = new BloomIntegrityDialog())
			{
				const string nonHtmlMessage = "Bloom cannot find some of its own files, and cannot continue. After you submit this report, we will contact you and help you work this out. In the meantime, you can run the Bloom installer again.";
				var messagePath = BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(false, "help", "IntegrityFailureAdvice-en.htm");
				string message;
				if(messagePath == null) // maybe we can't even get at this file we need for a good description of the problem
				{
					message = nonHtmlMessage;
				}
				else
				{
					var installFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
							.CombineForPath(Application.ProductName);
					message = RobustFile.ReadAllText(messagePath).Replace("{{installFolder}}", installFolder);//new
					message = message.Replace("{installFolder}", installFolder);  //old
				}

				message = message + Environment.NewLine + Environment.NewLine + errors;
				dlg.htmlTextBox1.HtmlText = message;
				dlg.ShowDialog();
				Logger.WriteEvent("Bloom Integrity Check Failed: " + message);
				// We would like to do this:
				// ProblemReportApi.ShowProblemDialog(null, "fatal");
				// But that can't work because BloomServer isn't running yet.
			}

			return false; //Force termination of the current process.
		}

		private void _reportButton_Click(object sender, EventArgs e)
		{
			Close();
		}
	}
}
