using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using SIL.Extensions;
using SIL.IO;
using SIL.PlatformUtilities;

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
			var files = new[] { "Bloom.chm", "PdfDroplet.exe", "Chorus.exe", "BloomPdfMaker.exe", "optipng.exe" };

			string[] dirs;
			if (SIL.PlatformUtilities.Platform.IsWindows)
				dirs = new[] { "AndikaNewBasic", "localization", "xslts" };
			else
				dirs = new[] { "localization", "xslts" };

			foreach(var fileName in files)
			{
				if(!Platform.IsWindows && fileName == "optipng.exe")
				{
					// optipng is provided by a package dependency, will be found as /usr/bin/optipng (no .exe)
					continue;
				}
				if(FileLocator.GetFileDistributedWithApplication(true, fileName) == null)
				{
					//In a code directory, the FileLocator considers the solution the root, so it can't find files in output\debug
					if(!File.Exists(Path.Combine(FileLocator.DirectoryOfTheApplicationExecutable, fileName)))
					{
						//maybe it's an exe in distfiles?
						if(fileName.EndsWith(".exe") && File.Exists(Path.Combine(FileLocator.DirectoryOfApplicationOrSolution, "DistFiles")))
						{
							continue;
						}
						errors.AppendFormat("Missing File: {0}{1}{1}", fileName, Environment.NewLine);
					}
				}
			}
			foreach(var directory in dirs)
			{
				if(FileLocator.GetDirectoryDistributedWithApplication(true, directory) == null)
				{
					errors.AppendFormat("Missing Directory: {0}{1}{1}", directory, Environment.NewLine);
				}
			}
			if(errors.Length == 0)
				return true;

			using(var dlg = new BloomIntegrityDialog())
			{
				var messagePath = FileLocator.GetFileDistributedWithApplication("IntegrityFailureAdvice-en.md");
				string message;
				if(messagePath == null) // maybe we can't even get at this file we need for a good description of the problem
				{
					message = "Bloom cannot find some of its own files, and cannot continue. After you submit this report, we will contact you and help you work this out. In the meantime, you can run the Bloom installer again.";
				}
				else
				{
					var installFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
							.CombineForPath(Application.ProductName);
					message = File.ReadAllText(messagePath).Replace("{installFolder}", installFolder);
				}

				message = message + Environment.NewLine + Environment.NewLine + errors.ToString();
				dlg.markDownTextBox1.MarkDownText = message;
				dlg.ShowDialog();
			}
			using(var dlg = new ProblemReporterDialog())
			{
				dlg.Summary = "Bloom Integrity Check Failed: {0}";
				dlg.Description = "Please answer any of these questions that you understand:"
								  + Environment.NewLine + Environment.NewLine
								  + "Did you install Bloom just now, or maybe allow it to update?"
								  + Environment.NewLine + Environment.NewLine
								  + "Is your computer locked down against installing new software?"
								  + Environment.NewLine + Environment.NewLine
								  + "What antivirus program do you use?"
								  + Environment.NewLine + Environment.NewLine
								  + "--------------------------------------------"
								  + Environment.NewLine + Environment.NewLine
								  + "The following information is for Bloom developers to see just what is and isn't missing:"
								  + Environment.NewLine + Environment.NewLine
								  + errors.ToString()
								  + GetDirectoryListing(FileLocator.DirectoryOfTheApplicationExecutable)
								  + Environment.NewLine + Environment.NewLine
								  + "Detected Antivirus Program(s): " + InstalledAntivirusPrograms();

#if !__MonoCS__

				try
				{
					var logPath =
						Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
							.CombineForPath(Application.ProductName, "SquirrelSetup.log");
					dlg.Description += "=Squirrel Log=" + Environment.NewLine;
					dlg.Description += logPath + Environment.NewLine;
					if(File.Exists(logPath))
					{
						dlg.Description += File.ReadAllText(logPath);
					}
					else
					{
						dlg.Description += logPath + "not found";
					}
				}
				catch(Exception error)
				{
					dlg.Description += error.Message;
				}
#endif
				dlg.ShowDialog();
			}

			return false; //Force termination of the current process.
		}

		private void _reportButton_Click(object sender, EventArgs e)
		{
			Close();
		}

		static string GetDirectoryListing(string directory)
		{
			var builder = new StringBuilder();
			builder.AppendLine("The following are under " + directory);
			GetDirectoryListing(directory.Length, directory, builder);
			return builder.ToString();
		}

		static void GetDirectoryListing(int rootDirectoryLength, string directory, StringBuilder builder)
		{
			try
			{
				foreach(var f in Directory.GetFiles(directory))
				{
					builder.AppendLine(Path.GetFileName(f));
				}
			}
			catch(Exception error)
			{
				builder.AppendLine("**** " + error.Message);
			}
			try
			{
				//If we let this box get too full, the user can't type into it (BL-2575). So we clip the tree on some big directories:
				// The problem reappeared on Linux (BL-2895), so we avoid redundant printing of subdirectory paths in the filenames.
				string[] bigDirectoriesToSkip = new string[] { "pdf", "Mercurial", BloomFileLocator.BrowserRoot };
				foreach(var d in Directory.GetDirectories(directory))
				{
					if(bigDirectoriesToSkip.Contains(Path.GetFileName(d)))
					{
						builder.AppendLine(d + " (will not list contents)");
					}
					else
					{
						builder.AppendLine(d + " contains these files:");
						GetDirectoryListing(rootDirectoryLength, d, builder);
					}
				}
			}
			catch(Exception error)
			{
				builder.AppendLine("**** " + error.Message);
			}
		}

		private static string InstalledAntivirusPrograms()
		{
			string result = "";
#if !__MonoCS__
			string wmipathstr = @"\\" + Environment.MachineName + @"\root\SecurityCenter2";
			try
			{
				var searcher = new System.Management.ManagementObjectSearcher(wmipathstr, "SELECT * FROM AntivirusProduct");
				var instances = searcher.Get();

				foreach(var instance in instances)
				{
					result += instance.GetText(System.Management.TextFormat.Mof).ToString() + Environment.NewLine;
				}
			}
			catch(Exception error)
			{
				return error.Message;
			}
#endif
			return result;
		}
	}
}
