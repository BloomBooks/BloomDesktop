using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.MiscUI;
using Bloom.Publish;
using Palaso.IO;
using Palaso.Extensions;

namespace Bloom
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
			var files = new[] { "Bloom.chm","PdfDroplet.exe", "xChorus.exe", "GeckofxHtmlToPdf.exe", "BloomBookUploader.exe", "optipng.exe" };
			
			var dirs = new[] {"AndikaNewBasic","factoryCollections","localization","xMatter","xslts"};
			
			foreach(var fileName in files)
			{
				if(Palaso.IO.FileLocator.GetFileDistributedWithApplication(true, fileName) == null)
				{
					//In a code directory, the FileLocator considers the solution the root, so it can't find files in output\debug
					if(!File.Exists(Path.Combine(FileLocator.DirectoryOfTheApplicationExecutable,fileName)))
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
				if(Palaso.IO.FileLocator.GetDirectoryDistributedWithApplication(true, directory) == null)
				{
					errors.AppendFormat("Missing Directory: {0}{1}{1}", directory, Environment.NewLine);
				}
			}
			if (errors.Length == 0)
				return true;

			using (var dlg = new BloomIntegrityDialog())
			{
				var messagePath = FileLocator.GetFileDistributedWithApplication("IntegrityFailureAdvice-en.md");
				string message;
				if (messagePath == null) // maybe we can't even get at this file we need for a good description of the problem
				{
					message = "Bloom cannot find some of its own files, and cannot continue. After you submit this report, we will contact you and help you work this out. In the meantime, you can run the Bloom installer again.";
				}
				else
				{
					var installFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
							.CombineForPath(Application.ProductName);
					message = File.ReadAllText(messagePath).Replace("{installFolder}", installFolder);
				}
				
                message = message + Environment.NewLine+ Environment.NewLine + errors.ToString();
				dlg.markDownTextBox1.MarkDownText = message;
				dlg.ShowDialog();
			}
			using (var dlg = new ProblemReporterDialog(null, null))
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
				                  + Environment.NewLine + Environment.NewLine
				                  + GetDirectoryListing(Palaso.IO.FileLocator.DirectoryOfTheApplicationExecutable)
				                  + "Detected Antivirus Program(s): " + InstalledAntivirusPrograms();

#if !__MonoCS__
				
				try
				{
					var logPath =
						Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
							.CombineForPath(Application.ProductName, "SquirrelSetup.log");
					dlg.Description += "=Squirrel Log=" + Environment.NewLine;
					dlg.Description += logPath + Environment.NewLine;
                    if (File.Exists(logPath))
					{
						dlg.Description += File.ReadAllText(logPath);
					}
					else
					{
						dlg.Description += logPath+ "not found";
					}
				}
				catch (Exception error)
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
			var builder  = new StringBuilder();
			GetDirectoryListing(directory,builder);
			return builder.ToString();
		}

		static void GetDirectoryListing(string directory, StringBuilder builder )
		{
			try
			{
				foreach (var f in Directory.GetFiles(directory))
				{
					builder.AppendLine(f);
				}
			}
			catch(Exception error)
			{
				builder.AppendLine("**** " + error.Message);
			}
			try
			{
				foreach(var d in Directory.GetDirectories(directory))
				{
					GetDirectoryListing(d, builder);
				}
			}
			catch(Exception error)
			{
				builder.AppendLine("**** "+error.Message);
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

				foreach (var instance in instances)
				{
					result += instance.GetText(System.Management.TextFormat.Mof).ToString() + System.Environment.NewLine;
				}
			}
			catch (Exception error)
			{
				return error.Message;
			}
#endif
			return result;
		}
	}
}
