using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.MiscUI;
using Bloom.Properties;
using L10NSharp;
using SIL.PlatformUtilities;
#if !__MonoCS__
using Squirrel;
#endif

namespace Bloom
{
	/// <summary>
	/// This class contains code to work with the Squirrel package to handle automatic updating of
	/// Bloom to new versions. The key methods are called from WorkspaceView when Bloom is first idle or
	/// when the user requests an update.
	/// </summary>
	static class ApplicationUpdateSupport
	{
#if !__MonoCS__
		internal static UpdateManager _bloomUpdateManager;
#endif

		internal enum BloomUpdateMessageVerbosity { Quiet, Verbose }

		internal static bool BloomUpdateInProgress
		{
#if __MonoCS__
			get { return false; }
#else
			get { return _bloomUpdateManager != null; }
#endif
		}

		/// <summary>
		/// See if any updates are available and if so do them. Once they are done a notification
		/// pops up and the user can restart Bloom to run the new version.
		/// The restartBloom argument is an action that is executed if the user clicks the toast that suggests
		/// a restart. This is the responsibility of the caller (typically the workspace view). It is passed the new
		/// install directory.
		/// </summary>
		internal static async void CheckForASquirrelUpdate(BloomUpdateMessageVerbosity verbosity, Action<string> restartBloom, bool autoUpdate)
		{
#if !__MonoCS__
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
					// Tester should ensure that the version we want to pretend to upgrade is installed there (under Bloom)...the critical thing
					// seems to be the version of Bloom/packages/RELEASES in this folder which indicates what is already installed.
					rootDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
				}
				else
				{
					// Mostly, we're willing to use a cached value for this URL. But if verbosity is Verbose,
					// which means the user has asked us to check for updates and we're going to report even
					// if there is nothing to update, we need to know whether we are online NOW. If not,
					// the failure report we get when Squirrel fails would be misleading.
					var result = InstallerSupport.LookupUrlOfSquirrelUpdate(verbosity == BloomUpdateMessageVerbosity.Verbose);

					if (result.Error != null || string.IsNullOrEmpty(result.URL))
					{
						// no need to tell them we can't connect, if they didn't explicitly ask us to look for an update
						if (verbosity != BloomUpdateMessageVerbosity.Verbose) return;

						// but if they did, try and give them a hint about what went wrong
						if (result.IsConnectivityError)
						{
							var failMsg = LocalizationManager.GetString("CollectionTab.UnableToCheckForUpdate",
								"Could not connect to the server to check for an update. Are you connected to the internet?",
								"Shown when Bloom tries to check for an update but can't, for example because it can't connect to the internet, or a problems with our server, etc.");
							ShowFailureNotification(failMsg);
						}
						else if (result.Error == null || string.IsNullOrWhiteSpace(result.Error.Message))
						{
							SIL.Reporting.ErrorReport.NotifyUserOfProblem(
								"Bloom failed to find if there is an update available, for some unknown reason.");
						}
						else
						{
							ShowFailureNotification(result.Error.Message);
						}
						return;
					}
					updateUrl = result.URL;
				}
				if (!autoUpdate)
				{
					ApplicationUpdateSupport.InitiateSquirrelNotifyUpdatesAvailable(verbosity, updateUrl, restartBloom);
					return;
				}

				string newInstallDir;
				UpdateOutcome outcome;
				ArrangeToDisposeSquirrelManagerOnExit();
				using (_bloomUpdateManager = new UpdateManager(updateUrl, Application.ProductName, rootDirectory))
				{
					// At this point the method returns(!) and no longer blocks anything.
					var result = await UpdateApp(_bloomUpdateManager);
					newInstallDir = result.NewInstallDirectory;
					outcome = result.Outcome;
				}
				// Since this is in the async method _after_ the await we know the UpdateApp has finished.
				_bloomUpdateManager = null;

				if (outcome != UpdateOutcome.GotNewVersion)
				{
					if (verbosity == BloomUpdateMessageVerbosity.Verbose)
					{
						// Enhance: bring this in quiet mode, but only show it after an update.
						var noneNotifier = new ToastNotifier();
						noneNotifier.Image.Image = Resources.BloomIcon.ToBitmap();
						string message;
						if (outcome == UpdateOutcome.AlreadyUpToDate)
							message = LocalizationManager.GetString("CollectionTab.UpToDate", "Your Bloom is up to date.");
						else
							message = LocalizationManager.GetString("CollectionTab.UpdateFailed", "A new version appears to be available, but Bloom could not install it.");
						noneNotifier.Show(message, "", 5);
					}
					return;
				}

				string version = Path.GetFileName(newInstallDir).Substring("app-".Length); // version folders always start with this
				var msg = String.Format(LocalizationManager.GetString("CollectionTab.UpdateInstalled", "Update for {0} is ready", "Appears after Bloom has downloaded a program update in the background and is ready to switch the user to it the next time they run Bloom."), version);
				var action = String.Format(LocalizationManager.GetString("CollectionTab.RestartToUpdate", "Restart Bloom to Update", "Restart the Bloom program, not Windows"));
				// Unfortunately, there's no good time to dispose of this object...according to its own comments
				// it's not even safe to close it. It moves itself out of sight eventually if ignored.
				var notifier = new ToastNotifier();
				notifier.Image.Image = Resources.Bloom;
				notifier.ToastClicked += (sender, args) => restartBloom(newInstallDir);
				notifier.Show(msg, action, -1);//Len wants it to stay up until he clicks on it
			}
#endif
		}

		private static void ShowFailureNotification(string failMsg)
		{
			var failNotifier = new ToastNotifier();
			failNotifier.Image.Image = Resources.Bloom;
			failNotifier.Show(failMsg, "", 5);
		}

		internal static void ArrangeToDisposeSquirrelManagerOnExit()
		{
#if !__MonoCS__
			Application.ApplicationExit += (sender, args) =>
			{
				if (_bloomUpdateManager != null)
				{
					var temp = _bloomUpdateManager;
					_bloomUpdateManager = null; // in case more than one notification comes
					temp.Dispose(); // otherwise squirrel throws a nasty exception.
				}
			};
#endif
		}

		internal const string kChannelNameForUnitTests ="TestChannel";

		public static bool IsDevOrAlpha
		{
			get
			{
				var channel = ApplicationUpdateSupport.ChannelName.ToLowerInvariant();
				return channel.Contains("developer") || channel.Contains("alpha") || channel.Contains("unstable");
			}
		}
		public static bool IsDev
		{
			get
			{
				var channel = ApplicationUpdateSupport.ChannelName.ToLowerInvariant();
				return channel.Contains("developer");
			}
		}
		public static string ChannelName
		{
			get
			{
				if(Program.RunningUnitTests)
					return kChannelNameForUnitTests;

				var path = Assembly.GetEntryAssembly().ManifestModule.FullyQualifiedName;
				// Use a very specific channel name on developer machines based on build configuration.
				if (path.Replace('\\','/').EndsWith("/output/Debug/Bloom.exe"))
					return "Developer/Debug";       // verifies this code is running on a developer machine.
				if (path.Replace('\\', '/').EndsWith("/output/Release/Bloom.exe"))
					return "Developer/Release";       // verifies this code is running on a developer machine.
				if (Platform.IsUnix)
				{
					// The specific directories where the program is installed reflect
					// the status ("channel") of the program on Linux.
					if (path.Contains("/bloom-desktop-alpha/"))
						return "Alpha";
					if (path.Contains ("/bloom-desktop-beta/"))
						return "Beta";
					// The next two have never existed yet, but maybe someday we'll want to use them.
					if (path.Contains ("/bloom-desktop-betainternal/"))
						return "BetaInternal";
					if (path.Contains("/bloom-desktop-internal/"))
						return "ReleaseInternal";
					return "Release";
				}
				var s = Assembly.GetEntryAssembly().ManifestModule.Name.Replace("bloom", "").Replace("Bloom", "").Replace(".exe", "").Trim();
				return (s == "") ? "Release" : s;
			}
		}

		private static async void InitiateSquirrelNotifyUpdatesAvailable(BloomUpdateMessageVerbosity verbosity, string updateUrl, Action<string> restartBloom)
		{
#if !__MonoCS__
			try
			{
				if (OkToInitiateUpdateManager)
				{
					UpdateInfo info;
					ArrangeToDisposeSquirrelManagerOnExit();
					using (_bloomUpdateManager = new UpdateManager(updateUrl, Application.ProductName))
					{
						// At this point the method returns(!) and no longer blocks anything.
						info = await _bloomUpdateManager.CheckForUpdate();
					}
					// Since this is in the async method _after_ the await we know the CheckForUpdate has finished.
					_bloomUpdateManager = null;
					if (NoUpdatesAvailable(info))
					{
						SIL.Reporting.Logger.WriteEvent("Squirrel: No update available.");
						if (verbosity == BloomUpdateMessageVerbosity.Verbose)
						{
							// Only show this if the user manually initiated the check.
							var message = LocalizationManager.GetString("CollectionTab.UpToDate", "Your Bloom is up to date.");
							var noneNotifier = new ToastNotifier();
							noneNotifier.Image.Image = Resources.BloomIcon.ToBitmap();
							noneNotifier.Show(message, "", 5);
						}
						return;
					}
					var msg = LocalizationManager.GetString("CollectionTab.UpdatesAvailable", "A new version of Bloom is available.");
					var action = LocalizationManager.GetString("CollectionTab.UpdateNow", "Update Now");
					SIL.Reporting.Logger.WriteEvent("Squirrel: Notifying that an update is available");
					// Unfortunately, there's no good time to dispose of this object...according to its own comments
					// it's not even safe to close it. It moves itself out of sight eventually if ignored.
					var notifier = new ToastNotifier();
					notifier.Image.Image = Resources.Bloom;
					notifier.ToastClicked += (sender, args) => CheckForASquirrelUpdate(BloomUpdateMessageVerbosity.Verbose, restartBloom, true);
					notifier.Show(msg, action, 10);
				}
			}
			catch (System.Net.WebException e)
			{
				SIL.Reporting.Logger.WriteEvent("Squirrel: Network unreliable - " + e.Message);
				return;
			}
#endif
		}

		/// <summary>
		/// True if it is currently possible to start checking for or getting updates.
		/// This approach is only relevant for Windows.
		/// If some bloom update activity is already in progress we must not start another one...that crashes.
		/// If we were installed in Program Files (using the --allUsers installer command-line argument
		/// in administrator mode), we don't attempt updates.
		/// </summary>
		internal static bool OkToInitiateUpdateManager
		{
#if __MonoCS__
			get { return false; }
#else
			get { return Platform.IsWindows && _bloomUpdateManager == null && !InstallerSupport.SharedByAllUsers(); }
#endif
		}

#if !__MonoCS__
		internal static bool NoUpdatesAvailable(UpdateInfo info)
		{
			return info == null || info.ReleasesToApply.Count == 0;
		}
#endif

		internal enum UpdateOutcome
		{
			GotNewVersion,
			AlreadyUpToDate,
			InstallFailed
		}

		internal class UpdateResult
		{
			public string NewInstallDirectory;
			public UpdateOutcome Outcome;

		}

#if !__MonoCS__
		// Adapted from Squirrel's EasyModeMixin.UpdateApp, but this version yields the new directory.
		internal static async Task<UpdateResult> UpdateApp(IUpdateManager manager)
		{
			bool ignoreDeltaUpdates = false;

			retry:
			var updateInfo = default(UpdateInfo);
			string newInstallDirectory = null;

			try
			{
				updateInfo = await manager.CheckForUpdate(ignoreDeltaUpdates, x => { });
				if (NoUpdatesAvailable(updateInfo))
					return new UpdateResult() { NewInstallDirectory = null, Outcome = UpdateOutcome.AlreadyUpToDate }; // none available.

				var updatingNotifier = new ToastNotifier();
				updatingNotifier.Image.Image = Resources.Bloom;
				var version = updateInfo.FutureReleaseEntry.Version;
				var releasesToDownload = updateInfo.ReleasesToApply;
				var size = releasesToDownload.Sum(x => x.Filesize)/1024;
				var updatingMsg = String.Format(LocalizationManager.GetString("CollectionTab.Updating", "Downloading update to {0} ({1}K)"), version.ToString(), size);
				SIL.Reporting.Logger.WriteEvent("Squirrel: "+updatingMsg);
				updatingNotifier.Show(updatingMsg, "", -1);

				var sb = new StringBuilder("Squirrel update downloading " + releasesToDownload.Count + " release files starting at" + DateTime.Now + ":");
				foreach (var release in releasesToDownload)
				{
					sb.Append(" ");
					sb.Append(release.Filename);
					sb.Append("(");
					sb.Append(release.Filesize);
					sb.Append(")");
				}
				SIL.Reporting.Logger.WriteEvent(sb.ToString());

				var progressMsg = LocalizationManager.GetString("CollectionTab.Progress", "({0}% complete)");

				await manager.DownloadReleases(releasesToDownload, x => UpdateProgress(updatingNotifier, updatingMsg, progressMsg, x));

				SIL.Reporting.Logger.WriteEvent("Squirrel update download succeeded at " + DateTime.Now);

				// There's no telling what fraction of the total download and update will be. With a bad connection downloading can take a long time.
				// With a lot of updates applying can take a long time.  If it has to
				// download the whole package applying will be negligible and downloading all of it.
				// Rather than have it suddenly slow down or speed up half way I decided to actually describe the two stages.
				updatingMsg = LocalizationManager.GetString("CollectionTab.Applying", "Applying updates");
				UpdateProgress(updatingNotifier, updatingMsg, progressMsg, 0);
				newInstallDirectory = await manager.ApplyReleases(updateInfo, x => UpdateProgress(updatingNotifier, updatingMsg, progressMsg, x));

				SIL.Reporting.Logger.WriteEvent("Squirrel update finished applying updates at " + DateTime.Now);

				await manager.CreateUninstallerRegistryEntry();
				updatingNotifier.Hide();
			}
			catch (Exception ex)
			{
				if(Directory.Exists(newInstallDirectory))
				{
					try
					{
						SIL.IO.RobustIO.DeleteDirectoryAndContents(newInstallDirectory);
					}
					catch(Exception error)
					{
						SIL.Reporting.Logger.WriteError("The failed installation update directory "+newInstallDirectory+" could not be deleted, will interfere with running Bloom.", error);
					}
				}
				if (ignoreDeltaUpdates == false)
				{
					// I think the idea here is that if something goes wrong applying deltas we
					// just download and install whatever the update url says is the latest version,
					// as a complete package.
					// Thus we can even recover if the executing program and the package that created
					// it are not part of the sequence on the web site at all, or even if there's
					// some sort of discontinuity in the sequence of deltas.
					ignoreDeltaUpdates = true;
					SIL.Reporting.Logger.WriteEvent("Squirrel update incremental download failed; trying whole package. Exception: " + ex.Message);
					goto retry;
				}

				// OK, the update failed. We've had cases where somehow Squirrel thinks there should be
				// a new release available and it isn't there yet. Possibly somehow a new version of
				// RELEASES is getting uploaded before the delta and nupkg files (due to subtask overlap
				// in MsBuild? Due to 'eventual consistency' not yet being attained by S3?).
				// In any case we don't need to crash the program over a failed update. Just log it.
				SIL.Reporting.Logger.WriteEvent("Squirrel update failed: " + ex.Message + ex.StackTrace);
				return new UpdateResult() {NewInstallDirectory = null, Outcome = UpdateOutcome.InstallFailed};
			}

			return new UpdateResult()
			{
				NewInstallDirectory = newInstallDirectory,
				Outcome = newInstallDirectory == null ? UpdateOutcome.AlreadyUpToDate : UpdateOutcome.GotNewVersion
			};
		}
#endif

		private static void UpdateProgress(ToastNotifier updatingNotifier, string updatingMsg, string progressMsg, int x)
		{
			if (updatingNotifier.IsHandleCreated)	// Must have a handle to Invoke anything!
			{
				try
				{
					updatingNotifier.Invoke((Action)(() =>
					{
						updatingNotifier.UpdateMessage(updatingMsg + " " + string.Format(progressMsg, x));
					}));
				}
				catch (InvalidOperationException)
				{
					// This can be caused by someone clicking on the progress Toast display (BL-2465).
					// Ignore it.  (It's possible that the IsHandleCreated above removes the possibility
					// of this error, but I've always been a belt AND suspenders type guy when it comes
					// to bugfixing.)
				}
			}
		}
	}
}
