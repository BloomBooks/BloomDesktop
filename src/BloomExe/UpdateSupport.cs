using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.MiscUI;
using Bloom.Properties;
using Bloom.Workspace;
using L10NSharp;
using Palaso.PlatformUtilities;
using Squirrel;

namespace Bloom
{
	/// <summary>
	/// This class conctains code to work with the Squirrel package to handle automatic updating of
	/// Bloom to new versions. The key methods are called from WorkspaceView when Bloom is first idle or
	/// when the user requests an update.
	/// </summary>
	static class UpdateSupport
	{
		internal static UpdateManager _bloomUpdateManager;

		internal enum BloomUpdateMessageVerbosity { Quiet, Verbose }

		internal static bool BloomUpdateInProgress
		{
			get { return _bloomUpdateManager != null; }
		}

		/// <summary>
		/// See if any updates are available and if so do them. Once they are done a notification
		/// pops up and the user can restart Bloom to run the new version.
		/// The restartBloom argument is an action that is executed if the user clicks the toast that suggests
		/// a restart. This is the responsibility of the caller (typically the workspace view). It is passed the new
		/// install directory.
		/// </summary>
		internal static async void InitiateSquirrelUpdate(BloomUpdateMessageVerbosity verbosity, Action<string> restartBloom)
		{
			if (OkToInitiateUpdateManager)
			{
				string updateUrl;
				string rootDirectory = null; // null default causes squirrel to figure out the version actually running.
				if (Debugger.IsAttached)
				{
					// update'Url' can actually also just be a path to where the deltas and RELEASES file are found.
					// When debugging this function we want this to be the directory where we build installers.
					var location = Assembly.GetExecutingAssembly().Location; // typically in output\debug
					var output = Path.GetDirectoryName(Path.GetDirectoryName(location));
					updateUrl = Path.Combine(output, "installer");

					// For testing we will force it to look in the standard local data folder, even though we are not running there.
					// Tester should ensure that the version we want to pretent to upgrade is installed there (under Bloom)...the critical thing
					// seems to be the version of Bloom/packages/RELEASES in this folder which indicates what is already installed.
					rootDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				}
				else
				{
					updateUrl = InstallerSupport.SquirrelUpdateUrl;
				}
				if (updateUrl == null)
				{
					// For some reason we couldn't get one...possibly not online so can't get to UpdateVersionTable
					var failNotifier = new ToastNotifier();
					failNotifier.Image.Image = Resources.Bloom.ToBitmap();
					var failMsg = LocalizationManager.GetString("CollectionTab.UnableToCheck", "Unable to check for update.", "Shown when Bloom tries to check for an update but can't, for example becuase it can't connect to the internet, or a problems with our server, etc.");
					failNotifier.Show(failMsg, "", 5);
					return;
				}

				string newInstallDir;
				ArrangeToDisposeSquirrelManagerOnExit();
				using (_bloomUpdateManager = new UpdateManager(updateUrl, Application.ProductName, FrameworkVersion.Net45, rootDirectory))
				{
					// At this point the method returns(!) and no longer blocks anything.
					newInstallDir = await UpdateApp(_bloomUpdateManager, null);
				}
				// Since this is in the async method _after_ the await we know the UpdateApp has finished.
				_bloomUpdateManager = null;

				if (newInstallDir == null)
				{
					// No updates to install
					if (verbosity == BloomUpdateMessageVerbosity.Verbose)
					{
						// Enhance: bring this in quiet mode, but only show it after an update.
						var noneNotifier = new ToastNotifier();
						noneNotifier.Image.Image = Resources.Bloom.ToBitmap();
						var failMsg = LocalizationManager.GetString("CollectionTab.UpToDate", "Your Bloom is up to date.");
						noneNotifier.Show(failMsg, "", 5);
					}
					return;
				}

				string version = Path.GetFileName(newInstallDir).Substring("app-".Length); // version folders always start with this
				var msg = String.Format(LocalizationManager.GetString("CollectionTab.UpdateInstalled", "Update for {0} is ready", "Appears after Bloom has downloaded a program update in the background and is ready to switch the user to it the next time they run Bloom."), version);
				var action = String.Format(LocalizationManager.GetString("CollectionTab.RestartToUpdate", "Restart to Update"));
				// Unfortunately, there's no good time to dispose of this object...according to its own comments
				// it's not even safe to close it. It moves itself out of sight eventually if ignored.
				var notifier = new ToastNotifier();
				notifier.Image.Image = Resources.Bloom.ToBitmap();
				notifier.ToastClicked += (sender, args) => restartBloom(newInstallDir);
				notifier.Show(msg, action, 8);
			}
		}

		internal static void ArrangeToDisposeSquirrelManagerOnExit()
		{
			Application.ApplicationExit += (sender, args) =>
			{
				if (_bloomUpdateManager != null)
				{
					var temp = _bloomUpdateManager;
					_bloomUpdateManager = null; // in case more than one notification comes
					temp.Dispose(); // otherwise squirrel throws a nasty exception.
				}
			};
		}

		internal static async void InitiateSquirrelNotifyUpdatesAvailable(Action<string> restartBloom)
		{
			if (OkToInitiateUpdateManager)
			{
				var updateUrl = InstallerSupport.SquirrelUpdateUrl;
				if (updateUrl == null)
					return;
				UpdateInfo info;
				ArrangeToDisposeSquirrelManagerOnExit();
				using (_bloomUpdateManager = new UpdateManager(updateUrl, Application.ProductName, FrameworkVersion.Net45))
				{
					// At this point the method returns(!) and no longer blocks anything.
					info = await _bloomUpdateManager.CheckForUpdate();
				}
				// Since this is in the async method _after_ the await we know the CheckForUpdate has finished.
				_bloomUpdateManager = null;
				if (NoUpdatesAvailable(info))
					return; // none available.
				var msg = LocalizationManager.GetString("CollectionTab.UpdatesAvailable", "A new version of Bloom is available.");
				var action = LocalizationManager.GetString("CollectionTab.UpdateNow", "Update Now");
				// Unfortunately, there's no good time to dispose of this object...according to its own comments
				// it's not even safe to close it. It moves itself out of sight eventually if ignored.
				var notifier = new ToastNotifier();
				notifier.Image.Image = Resources.Bloom.ToBitmap();
				notifier.ToastClicked += (sender, args) => InitiateSquirrelUpdate(BloomUpdateMessageVerbosity.Quiet, restartBloom);
				notifier.Show(msg, action, 10);
			}
		}

		/// <summary>
		/// True if it is currently possible to start checking for or getting updates.
		/// This approach is only relevant for Windows.
		/// If some bloom update activity is already in progress we must not start another one...that crashes.
		/// </summary>
		internal static bool OkToInitiateUpdateManager
		{
			get { return Platform.IsWindows && _bloomUpdateManager == null; }
		}

		internal static bool NoUpdatesAvailable(UpdateInfo info)
		{
			return info == null || info.ReleasesToApply.Count == 0;
		}

		// Adapted from Squirrel's EasyModeMixin.UpdateApp, but this version yields the new directory.
		internal static async Task<string> UpdateApp(IUpdateManager manager, Action<int> progress = null)
		{
			progress = progress ?? (_ => { });

			bool ignoreDeltaUpdates = false;

			retry:
			var updateInfo = default(UpdateInfo);
			string newInstallDirectory = null;

			try
			{
				updateInfo = await manager.CheckForUpdate(ignoreDeltaUpdates, x => progress(x / 3));
				if (NoUpdatesAvailable(updateInfo))
					return null; // none available.

				var updatingNotifier = new ToastNotifier();
				updatingNotifier.Image.Image = Resources.Bloom.ToBitmap();
				var version = updateInfo.FutureReleaseEntry.Version;
				var size = updateInfo.ReleasesToApply.Sum(x => x.Filesize)/1024;
				var updatingMsg = String.Format(LocalizationManager.GetString("CollectionTab.Updating", "Downloading update to {0} ({1}K)"), version, size);
				updatingNotifier.Show(updatingMsg, "", 5);

				await manager.DownloadReleases(updateInfo.ReleasesToApply, x => progress(x / 3 + 33));

				newInstallDirectory = await manager.ApplyReleases(updateInfo, x => progress(x / 3 + 66));

				await manager.CreateUninstallerRegistryEntry();
			}
			catch (Exception ex)
			{
				if (ignoreDeltaUpdates == false)
				{
					// I think the idea here is that if something goes wrong applying deltas we
					// just download and install whatever the update url says is the latest version,
					// as a complete package.
					// Thus we can even recover if the executing program and the package that created
					// it are not part of the sequence on the web site at all, or even if there's
					// some sort of discontinuity in the sequence of deltas.
					ignoreDeltaUpdates = true;
					goto retry;
				}

				throw;
			}

			return newInstallDirectory;
		}
	}
}
