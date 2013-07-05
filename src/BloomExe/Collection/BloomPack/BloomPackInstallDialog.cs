using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using Ionic.Zip;
using Palaso.Reporting;

namespace Bloom.Collection.BloomPack
{
	/// <summary>
	/// A BloomPack is just a zipped collection folder (a folder full of book folders).
	/// </summary>
	public partial class BloomPackInstallDialog : Form
	{
		private readonly string _path;

		public BloomPackInstallDialog(string path)
		{
			_path = path;
			InitializeComponent();
			_message.Text = string.Format("Opening {0}...", Path.GetFileName(path));
			_okButton.Enabled = false;
		}

		private void _startupTimer_Tick(object sender, EventArgs e)
		{
			_startupTimer.Enabled = false;
			BeginInstall();
			_okButton.Enabled = true;
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			Close();
		}

		private void BeginInstall()
		{
			if (!File.Exists(_path))
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem("{0} does not exist", _path);
				return;
			}
			try
			{
				Logger.WriteEvent("Installing BloomPack "+_path);

				using (var zip = new Ionic.Zip.ZipFile(_path))
				{
					var folderName = GetRootFolderName(zip);
					if (folderName == null)
						return;
					string destinationFolder = Path.Combine(ProjectContext.InstalledCollectionsDirectory, folderName);
					if (Directory.Exists(destinationFolder))
					{
						Logger.WriteEvent("BloomPack already exists, asking...");
						var msg =
							string.Format(
								"This computer already has a Bloom collection named '{0}'. Do you want to replace it with the one from this BloomPack?",
								folderName);
						if (DialogResult.OK != MessageBox.Show(msg, "BloomPack Installer", MessageBoxButtons.OKCancel))
						{
							_message.Text = "The Bloom collection will not be installed.";
							_okButton.Text = "&Cancel";
							return;
						}
						try
						{
							Logger.WriteEvent("Deleting existing BloomPack at "+destinationFolder);
							Directory.Delete(destinationFolder, true);
						}
						catch (Exception error)
						{
							_message.Text =
								string.Format(
									"Bloom was not able to remove the existing copy of '{0}'. Quit Bloom if it is running & try again. Otherwise, try again after restarting your computer.",
									destinationFolder);
							pictureBox1.Image = _errorImage.Image;
							_okButton.Text = "&Cancel";
							return;
						}
					}
					zip.ExtractAll(ProjectContext.InstalledCollectionsDirectory);

					var allDone = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.BloomPackInstalled",
																	  "The {0} Collection is now ready to use on this computer.");
					_message.Text = string.Format(allDone, folderName);
					if (Process.GetProcesses().Count(p => p.ProcessName.Contains("Bloom")) > 1)
					{
						_message.Text += System.Environment.NewLine + System.Environment.NewLine
							+ L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.MustRestartToSee",
																				 "Bloom is already running, but the contents will not show up until the next time you run Bloom");
					}
					UsageReporter.SendNavigationNotice("Installed BloomPack");
					UsageReporter.SendEvent("BloomPack", "BloomPack", "Install", folderName,0);
				}
			}
			catch (Exception error)
			{
				_message.Text = "Bloom was not able to install that Bloom Pack";
				_errorImage.Visible = true;
				_okButton.Text = "&Cancel";
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom was not able to install that Bloom Pack");
			}
		}

		private string GetRootFolderName(ZipFile zip)
		{
			string fileName = null;
			foreach (var f in zip.Entries)
			{
				var parts = f.FileName.Split(new[] {'/', '\\'});
				if (fileName != null && fileName != parts[0])
				{
					string msg = "Bloom Packs should have only a single collection folder at the top level of the zip file.";
					_message.Text = msg;
					pictureBox1.Image = _errorImage.Image;
					_okButton.Text = "&Cancel";
					return null;
				}
				fileName = parts[0];
			}
			return fileName;
		}
	}
}
