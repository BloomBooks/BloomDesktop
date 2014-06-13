using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using System.Linq;
using DesktopAnalytics;
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
		private string _folderName;

		public BloomPackInstallDialog(string path)
		{
			_path = path;
			InitializeComponent();
			var msg = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.Opening", "Opening {0}...");
			_message.Text = string.Format(msg, Path.GetFileName(path));
			_okButton.Enabled = false;
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
			using (var zip = ZipFile.Read(_path))
			{
				_folderName = GetRootFolderName(zip);
				if (_folderName == null)
					return;
				string destinationFolder = Path.Combine(ProjectContext.GetInstalledCollectionsDirectory(), _folderName);
				if (Directory.Exists(destinationFolder))
				{
					Logger.WriteEvent("BloomPack already exists, asking...");
					var msg =
						string.Format(
							"This computer already has a Bloom collection named '{0}'. Do you want to replace it with the one from this BloomPack?",
							_folderName);
					if (DialogResult.OK != MessageBox.Show(msg, "BloomPack Installer", MessageBoxButtons.OKCancel))
					{
						_message.Text = "The Bloom collection will not be installed.";
						_okButton.Text = "&Cancel";
						return;
					}
					try
					{
						Logger.WriteEvent("Deleting existing BloomPack at " + destinationFolder);
						DeleteExistingDirectory(destinationFolder);
					}
					catch (Exception error)
					{
						throw new ApplicationException(string.Format(
								"Bloom was not able to remove the existing copy of '{0}'. Quit Bloom if it is running & try again. Otherwise, try again after restarting your computer.",
								destinationFolder), error);
					}
				}
			}
			Logger.WriteEvent("Installing BloomPack " + _path);
			_okButton.Enabled = false;
			_message.Text = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.Extracting", "Extracting...", "Shown while BloomPacks are being installed");
			_backgroundWorker.RunWorkerAsync();
		}

		private static void DeleteExistingDirectory(string destinationFolder)
		{
			foreach (var dir in Directory.GetDirectories(destinationFolder))
			{
				DeleteExistingDirectory(dir);
			}

			//By Bloom convention, thumbnails that were created by hand are marked "read only" so that the
			//thumbnail generator never overwrites them. However now that we're trying to clear out this
			//folder, we need to remove that readonly flag so we can delete it.
			foreach (var f in Directory.GetFiles(destinationFolder))
			{
				var attributes = File.GetAttributes(f);
				if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				{
					File.SetAttributes(f, attributes & ~FileAttributes.ReadOnly);
				}
			}
			Directory.Delete(destinationFolder, true);
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

		private void _backgroundWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
		{
			//nb: we want exceptions to be uncaught, to be transferred up to the worker completed event

			using (var zip = ZipFile.Read(_path))
			{
				_folderName = GetRootFolderName(zip);
				if (_folderName == null)
					return;

				//NB: in the version i have at the moment, EntriesExtracted & EntriesTotal are always 0
				zip.ExtractProgress +=(o, extractProgress) =>
					_backgroundWorker.ReportProgress(extractProgress.EntriesExtracted/
													 (extractProgress.EntriesTotal > 0 ? extractProgress.EntriesTotal : 1));

				zip.ZipError += (o, args) => { throw args.Exception; };

				zip.ExtractAll(ProjectContext.GetInstalledCollectionsDirectory());

				var newlyAddedFolderOfThePack = Path.Combine(ProjectContext.GetInstalledCollectionsDirectory(), _folderName);
				CopyXMatterFoldersToWhereTheyBelong(newlyAddedFolderOfThePack);
			}
		}

		//xmatter in bloompacks was an afterthought... at the moment we unpack everything to programdata/../Collections,
		//but now we need to move xmatter over to programdata/../xmatter
		private static void CopyXMatterFoldersToWhereTheyBelong(string newlyAddedFolderOfThePack)
		{
			foreach (var dir in Directory.GetDirectories(newlyAddedFolderOfThePack, "*-xmatter"))
			{
				var destDirName = Path.Combine(ProjectContext.XMatterAppDataFolder, Path.GetFileName(dir));
				try
				{
					if (Directory.Exists(destDirName))
					{
						Directory.Delete(destDirName, true);
					}
				}
				catch (Exception error)
				{
					throw new ApplicationException("Could not delete the existing xmatter pack in order to update it", error);
				}
				try
				{
					Directory.Move(dir, destDirName);
				}
				catch (Exception error)
				{
					throw new ApplicationException("Could not move an xmatter pack from collections to xmatter", error);
				}
			}
		}


		private void _backgroundWorker_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
		{
			_okButton.Enabled = true;
			if(e.Error!=null)
			{
				_message.Text =  L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.ErrorInstallingBloomPack","Bloom was not able to install that Bloom Pack");
				_errorImage.Visible = true;
				_okButton.Text = L10NSharp.LocalizationManager.GetString("Common.CancelButton","&Cancel");
				DesktopAnalytics.Analytics.ReportException(e.Error);
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e.Error, _message.Text);
				return;
			}
			var allDone = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.BloomPackInstalled",
																			  "The {0} Collection is now ready to use on this computer.");
			_message.Text = string.Format(allDone, _folderName);
			if (Process.GetProcesses().Count(p => p.ProcessName.Contains("Bloom")) > 1)
			{
				_message.Text += System.Environment.NewLine + System.Environment.NewLine
								 + L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.MustRestartToSee",
																		   "Bloom is already running, but the contents will not show up until the next time you run Bloom");
			}
			//Analytics.Track("Install BloomPack");
		}

		private void _backgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			//always 0: _message.Text = e.ProgressPercentage.ToString()+"%";
		}

		/// <summary>
		/// This makes the dialog show before we go asking any questions.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void _startupTimer_Tick(object sender, EventArgs e)
		{
			_startupTimer.Enabled = false;
			BeginInstall();
		}
	}
}
