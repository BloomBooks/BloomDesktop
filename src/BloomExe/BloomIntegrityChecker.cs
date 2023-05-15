using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Bloom.Properties;
using L10NSharp;
using SIL.Extensions;
using SIL.IO;
using SIL.PlatformUtilities;
using SIL.Reporting;
using SIL.Windows.Forms.ReleaseNotes;

namespace Bloom.MiscUI
{
	public class BloomIntegrityChecker
	{
		/// <summary>
		/// We've had a number of user reports that suggest that files were either missing or inaccessible.
		/// The idea here is to check a set of files and folders at the start of each launch, and generate
		/// a useful report if anything is missing.
		/// </summary>
		/// <returns>true if all is well. Application should exit if this returns false.</returns>
		public static bool CheckIntegrity()
		{
			var errors = new StringBuilder();
			var files = new[] { "Bloom.chm", "PdfDroplet.exe", "BloomPdfMaker.exe" };

			string[] dirs;
			if (Platform.IsWindows)
				dirs = new[] { "fonts", "localization", "icons" };
			else
				dirs = new[] { "localization", "icons" };

			foreach (var fileName in files)
			{
				if (FileLocationUtilities.GetFileDistributedWithApplication(true, fileName) == null)
				{
					//In a code directory, the FileLocator considers the solution the root, so it can't find files in output\debug
					if (!RobustFile.Exists(Path.Combine(FileLocationUtilities.DirectoryOfTheApplicationExecutable, fileName)))
					{
						//maybe it's an exe in distfiles?
						if (fileName.EndsWith(".exe") && RobustFile.Exists(Path.Combine(FileLocationUtilities.DirectoryOfApplicationOrSolution, "DistFiles")))
						{
							continue;
						}
						errors.AppendFormat("<p>Missing File: {0}</p>{1}", fileName, Environment.NewLine);
					}
				}
			}
			foreach (var directory in dirs)
			{
				if (FileLocationUtilities.GetDirectoryDistributedWithApplication(true, directory) == null)
				{
					errors.AppendFormat("<p>Missing Directory: {0}</p>{1}", directory, Environment.NewLine);
				}
			}
			if (errors.Length == 0)
				return true;

			const string nonHtmlMessage = "Bloom cannot find some of its own files, and cannot continue. After you submit this report, we will contact you and help you work this out. In the meantime, you can run the Bloom installer again.";
			var messagePath = BloomFileLocator.GetBestLocalizableFileDistributedWithApplication(false, "help", "IntegrityFailureAdvice-en.htm");
			string message;
			if (messagePath == null) // maybe we can't even get at this file we need for a good description of the problem
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

			Logger.WriteEvent("Bloom Integrity Check Failed: " + message);

			// We would like to do this:
			// ProblemReportApi.ShowProblemDialog(null, "fatal");
			// But that can't work because BloomServer isn't running yet.

			// We could make it use the help.css file it currently refers to (even though it isn't actually used in 5.4) like this:
			//var cssUri = new Uri(FileLocationUtilities.GetFileDistributedWithApplication(BloomFileLocator.BrowserRoot, "help", "help.css"));
			//message = message.Replace("help.css", cssUri.ToString());
			// But instead, I'm making it look more like the BloomIntegrityDialog from versions <=5.4. We could also do both.
			message = message.Replace("</head>", "<style>body {font-family:Segoe UI, Arial, san-serif; font-size:10pt}</style></head>");

			using (var tempFile = new TempFile())
			{
				File.WriteAllText(tempFile.Path, message);

				// We're making ShowReleaseNotesDialog do double duty here. Actually, triple duty, since it also handles
				// the Bloom videos dialog currently. But currently this is the easiest way to display a bit of markdown in a dialog,
				// especially in a situation where we can't assume that the BloomServer is running.
				using (var dlg = new ShowReleaseNotesDialog(Resources.BloomIcon, tempFile.Path))
				{
					dlg.ApplyMarkdown = false;
					dlg.Text = LocalizationManager.GetString("BloomIntegrity.WindowTitle", "Bloom Problem");
					dlg.Height = 950;
					dlg.Width = 750;
					dlg.ShowDialog();
				}
			}
			return false; //Force termination of the current process.
		}
	}
}
