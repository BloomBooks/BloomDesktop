using System;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using Bloom.Edit;
using ICSharpCode.SharpZipLib.Zip;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Collection.BloomPack
{
	/// <summary>
	/// A Bloom Pack is just a zipped collection folder (a folder full of book folders).
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

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			// BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
			this.Icon = global::Bloom.Properties.Resources.BloomIcon;
		}

		private void _okButton_Click(object sender, EventArgs e)
		{
			Close();
		}

		// This gets set if Bloom is already running so this process should exit when done rather than continuing to start up.
		public bool ExitWithoutRunningBloom { get; set; }

		private void BeginInstall()
		{
			if (!RobustFile.Exists(_path))
			{
				string msg = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.DoesNotExist", "{0} does not exist");
				ErrorReport.NotifyUserOfProblem(msg, _path);
				return;
			}

			//For BL-3061 at the moment, I'm just trying to log more information.
			Logger.WriteEvent("BloomPackInstallDialog.BeginInstall. _path is " + _path);

			_folderName = GetRootFolderName();

			Logger.WriteEvent("BloomPackInstallDialog.BeginInstall. _folderName is " + _folderName);
			if (_folderName == null)
				return;
			string destinationFolder = Path.Combine(ProjectContext.GetInstalledCollectionsDirectory(), _folderName);
			if (Directory.Exists(destinationFolder))
			{
				Logger.WriteEvent("Bloom Pack already exists, asking...");
				string title = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.BloomPackInstaller",
						"Bloom Pack Installer", "Displayed as the message box title");
				string msg = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.Replace",
					"This computer already has a Bloom collection named '{0}'. Do you want to replace it with the one from this Bloom Pack?");
				msg = string.Format(msg, _folderName);
				if (DialogResult.OK != MessageBox.Show(msg, title, MessageBoxButtons.OKCancel))
				{
					_message.Text = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.NotInstalled", "The Bloom collection will not be installed.");
					_okButton.Text = L10NSharp.LocalizationManager.GetString("Common.CancelButton", "&Cancel");
					return;
				}
				try
				{
					Logger.WriteEvent("Deleting existing Bloom Pack at " + destinationFolder);
					DeleteExistingDirectory(destinationFolder);
				}
				catch (Exception error)
				{
					string text = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.UnableToReplace", "Bloom was not able to remove the existing copy of '{0}'. Quit Bloom if it is running & try again. Otherwise, try again after restarting your computer.");
					throw new ApplicationException(string.Format(text, destinationFolder), error);
				}
			}
			Logger.WriteEvent("Installing Bloom Pack " + _path);
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
				var attributes = RobustFile.GetAttributes(f);
				if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
				{
					RobustFile.SetAttributes(f, attributes & ~FileAttributes.ReadOnly);
				}
			}
			SIL.IO.RobustIO.DeleteDirectory(destinationFolder, true);
		}

		private delegate void ReportBadBloomPack();

		private string GetRootFolderName()
		{
			string rootDirectory = null;
			ZipFile zip = null;
			try
			{
				zip = new ZipFile(_path);
				foreach (ZipEntry zipEntry in zip)
				{
					var parts = zipEntry.Name.Split(new[] { '/', '\\' });
					if (rootDirectory != null && rootDirectory != parts[0])
					{
						if (InvokeRequired)
							Invoke(new ReportBadBloomPack(ReportInvalidBloomPack));
						else
							ReportInvalidBloomPack();
						return null;
					}
					rootDirectory = parts[0];
				}
			}
			catch (Exception ex)
			{
				// Report a corrupt file instead of crashing.  See http://issues.bloomlibrary.org/youtrack/issue/BL-2485.
				if (InvokeRequired)
					Invoke(new ReportBadBloomPack(ReportErrorUnzippingBloomPack));
				else
					ReportErrorUnzippingBloomPack();
				return null;
			}
			finally
			{
				if (zip != null)
					zip.Close();
			}
			return rootDirectory;
		}

		/// <summary>
		/// Report an invalid Bloom Pack file.
		/// </summary>
		private void ReportInvalidBloomPack()
		{
			string msg = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.SingleCollectionFolder",
				"Bloom Packs should have only a single collection folder at the top level of the zip file.");
			_message.Text = msg;
			pictureBox1.Image = _errorImage.Image;
			_okButton.Text = L10NSharp.LocalizationManager.GetString("Common.CancelButton", "&Cancel");
			_okButton.Enabled = true;
		}

		/// <summary>
		/// Report a corrupt Bloom Pack file.
		/// </summary>
		private void ReportErrorUnzippingBloomPack()
		{
			string msg = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.CorruptBloomPack",
				"This BloomPack appears to be incomplete or corrupt.");
			_message.Text = msg;
			pictureBox1.Image = _errorImage.Image;
			_okButton.Text = L10NSharp.LocalizationManager.GetString("Common.CancelButton", "&Cancel");
			_okButton.Enabled = true;
		}

		private void _backgroundWorker_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
		{
			//nb: we want exceptions to be uncaught, to be transferred up to the worker completed event
			_folderName = GetRootFolderName();
			if (_folderName == null)
				return;
			// ZipFile internally converts all \ separators to / (at least on Linux). So using
			// ZipFile instead of FastZip fixes https://jira.sil.org/browse/BL-1213.
			ZipFile zip = null;
			try
			{
				zip = new ZipFile(_path);
				byte[] buffer = new byte[4096];     // 4K is optimum
				foreach (ZipEntry entry in zip)
				{
					var fullOutputPath = Path.Combine(ProjectContext.GetInstalledCollectionsDirectory(), entry.Name);
					if (entry.IsDirectory)
					{
						Directory.CreateDirectory(fullOutputPath);
						// In the SharpZipLib code, IsFile and IsDirectory are not defined exactly as inverse: a third
						// (or fourth) type of entry might be possible.  In practice in BloomPacks, this should not be
						// an issue.
						continue;
					}
					var directoryName = Path.GetDirectoryName(fullOutputPath);
					if (!String.IsNullOrEmpty(directoryName))
						Directory.CreateDirectory(directoryName);
					using (var instream = zip.GetInputStream(entry))
					using (var writer = RobustFile.Create(fullOutputPath))
					{
						ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
					}
				}
			}
			catch (Exception ex)
			{
				// Report a corrupt file instead of crashing.  See http://issues.bloomlibrary.org/youtrack/issue/BL-2485.
				if (InvokeRequired)
					Invoke(new ReportBadBloomPack(ReportErrorUnzippingBloomPack));
				else
					ReportErrorUnzippingBloomPack();
				return;
			}
			finally
			{
				if (zip != null)
					zip.Close();
			}

			var newlyAddedFolderOfThePack = Path.Combine(ProjectContext.GetInstalledCollectionsDirectory(), _folderName);
			CopyXMatterFoldersToWhereTheyBelong(newlyAddedFolderOfThePack);
			ToolboxView.CopyToolSettingsForBloomPack(newlyAddedFolderOfThePack);
		}

		//xmatter in bloompacks was an afterthought... at the moment we unpack everything to programdata/../Collections,
		//but now we need to move xmatter over to programdata/../XMatter
		private static void CopyXMatterFoldersToWhereTheyBelong(string newlyAddedFolderOfThePack)
		{
			foreach (var dir in Directory.GetDirectories(newlyAddedFolderOfThePack, "*-XMatter"))
			{
				var destDirName = Path.Combine(ProjectContext.XMatterAppDataFolder, Path.GetFileName(dir));
				try
				{
					if (Directory.Exists(destDirName))
					{
						SIL.IO.RobustIO.DeleteDirectory(destDirName, true);
					}
				}
				catch (Exception error)
				{
					throw new ApplicationException("Could not delete the existing xmatter pack in order to update it", error);
				}
				try
				{
					SIL.IO.RobustIO.MoveDirectory(dir, destDirName);
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
				if (e.Error is ArgumentException && e.Error.StackTrace.Contains("CheckIllegalCharacters"))
				{
					_message.Text += Environment.NewLine + Environment.NewLine
						+ L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.BadCharsInFileName",
						"Possibly this is an old BloomPack created before BloomPacks could handle special characters in file names. You may be able to get the author to re-create it using a current version. If that's not possible a technical expert may be able to repair things.");
				}
				_errorImage.Visible = true;
				_okButton.Text = L10NSharp.LocalizationManager.GetString("Common.CancelButton","&Cancel");
				DesktopAnalytics.Analytics.ReportException(e.Error);
				ErrorReport.NotifyUserOfProblem(e.Error, _message.Text);
				return;
			}
			var allDone = L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.BloomPackInstalled",
				"The {0} Collection is now ready to use on this computer.");
			_message.Text = string.Format(allDone, _folderName);
			if (Program.GetRunningBloomProcessCount() > 1)
			{
				_message.Text += Environment.NewLine + Environment.NewLine +
					L10NSharp.LocalizationManager.GetString("BloomPackInstallDialog.MustRestartToSee",
					"Bloom is already running, but the contents will not show up until the next time you run Bloom");
				ExitWithoutRunningBloom = true;
			}
			//Analytics.Track("Install Bloom Pack");
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
