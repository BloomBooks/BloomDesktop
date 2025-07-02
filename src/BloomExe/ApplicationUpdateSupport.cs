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
using Velopack;
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

        internal enum BloomUpdateMessageVerbosity
        {
            Quiet,
            Verbose
        }

        internal static bool BloomUpdateInProgress
        {
#if __MonoCS__
            get { return false; }
#else
            get { return _bloomUpdateManager != null; }
#endif
        }

        /// <summary>
        /// See if any updates are available and if so download them. Once they are ready a notification
        /// pops up and the user can restart Bloom to run the new version.
        /// The restartBloom argument is an action that is executed if the user clicks the toast that suggests
        /// a restart. This is the responsibility of the caller (typically the workspace view). Typically it
        /// just shuts down Bloom; the update and restart are managed automatically by Velopack.
        /// </summary>
        internal static async void CheckForAVelopackUpdate(
            BloomUpdateMessageVerbosity verbosity,
            Action restartBloom,
            bool autoUpdate
        )
        {
            // In early 2023, MS stopped updating WebView2 for Windows 7, 8, and 8.1. So for 5.4, we would like to "just get the latest 5.4".
            // But at the moment, we aren't investing in that. We're just stranding these users at whatever 5.4 version they have.
            if (Environment.OSVersion.Version.Major < 10)
            {
                return;
            }

#if !__MonoCS__
            if (!OkToInitiateUpdateManager)
                return;
            if (GetUpdateUrl(verbosity, out var updateUrl))
                return;

            _bloomUpdateManager = new UpdateManager(updateUrl);
            var newVersion = await _bloomUpdateManager.CheckForUpdatesAsync();
            if (newVersion == null)
            {
                if (verbosity == BloomUpdateMessageVerbosity.Verbose)
                {
                    // Only show this if the user manually initiated the check.
                    var message = LocalizationManager.GetString(
                        "CollectionTab.UpToDate",
                        "Your Bloom is up to date."
                    );
                    var noneNotifier = new ToastNotifier();
                    noneNotifier.Image.Image = Resources.BloomIcon.ToBitmap();
                    noneNotifier.Show(message, "", 5);
                }
                _bloomUpdateManager = null; // no updates, so no need to keep this object around, and allows user to try again
                return;
            }
            // Velopack may use a more sophisticated algorithm to decide which to download,
            // but this should be good enough to give the user an idea.
            var fullSize = newVersion.TargetFullRelease.Size;
            var deltasSize = newVersion.DeltasToTarget.Sum(d => d.Size);
            var downloadSize = deltasSize;
            if (newVersion.DeltasToTarget.Length > 0 && fullSize < deltasSize)
                downloadSize = fullSize;
            var updatingMsg = String.Format(
                LocalizationManager.GetString(
                    "CollectionTab.Updating",
                    "Downloading update to {0} ({1}K)"
                ),
                newVersion.TargetFullRelease.Version.ToString(),
                downloadSize / 1024
            );

            var updatingNotifier = new ToastNotifier();
            updatingNotifier.Image.Image = Resources.Bloom;
            updatingNotifier.Show(updatingMsg, "", -1);
            updatingNotifier.Show(updatingMsg, "", 5);

            await _bloomUpdateManager.DownloadUpdatesAsync(newVersion);

            var msg = String.Format(
                LocalizationManager.GetString(
                    "CollectionTab.UpdateInstalled",
                    "Update for {0} is ready",
                    "Appears after Bloom has downloaded a program update in the background and is ready to switch the user to it the next time they run Bloom."
                ),
                newVersion.TargetFullRelease.Version
            );
            var action = String.Format(
                LocalizationManager.GetString(
                    "CollectionTab.RestartToUpdate",
                    "Restart Bloom to Update",
                    "Restart the Bloom program, not Windows"
                )
            );
            // Unfortunately, there's no good time to dispose of this object...according to its own comments
            // it's not even safe to close it. It moves itself out of sight eventually if ignored.
            var notifier = new ToastNotifier();
            notifier.Image.Image = Resources.Bloom;
            notifier.ToastClicked += (sender, args) =>
            {
                _bloomUpdateManager.WaitExitThenApplyUpdates(null);
                restartBloom();
            };
            notifier.Show(msg, action, -1); //stay up until clicked
            if (autoUpdate)
            {
                Application.ApplicationExit += (sender, args) =>
                {
                    // When we exit, apply the updates.
                    // Don't show any UI or automatically restart.
                    _bloomUpdateManager.WaitExitThenApplyUpdates(null, true, false);
                };
            }
#endif
        }

        // returns true if we should not proceed with the update check.
        private static bool GetUpdateUrl(
            BloomUpdateMessageVerbosity verbosity,
            out string updateUrl
        )
        {
            // For local testing, uncomment and adjust the following line.
            // Note that you need an absolute path; when testing an installed Bloom, even installed by a
            // locally-built installer, we are not running in output/debug or output/release, so there
            // is no automatic way to find output/installer/result.
            //updateUrl = "C:\\github\\BloomDesktop\\output\\installer\\result";
            //return false;
            updateUrl = null; // default for when we return true
            if (Debugger.IsAttached)
            {
                // update'Url' can actually also just be a path to where the deltas and RELEASES file are found.
                // When debugging this function we want this to be the directory where we build installers.
                var location = Assembly.GetExecutingAssembly().Location; // typically in output\debug
                var output = Path.GetDirectoryName(Path.GetDirectoryName(location));
                updateUrl = Path.Combine(output, "installer\\result");
            }
            else
            {
                // Mostly, we're willing to use a cached value for this URL. But if verbosity is Verbose,
                // which means the user has asked us to check for updates and we're going to report even
                // if there is nothing to update, we need to know whether we are online NOW. If not,
                // the failure report we get when Squirrel fails would be misleading.
                var result = InstallerSupport.LookupUrlOfVelopackUpdate(
                    verbosity == BloomUpdateMessageVerbosity.Verbose
                );

                if (result.Error != null || string.IsNullOrEmpty(result.URL))
                {
                    // no need to tell them we can't connect, if they didn't explicitly ask us to look for an update
                    if (verbosity != BloomUpdateMessageVerbosity.Verbose)
                        return true;

                    // but if they did, try and give them a hint about what went wrong
                    if (result.IsConnectivityError)
                    {
                        var failMsg = LocalizationManager.GetString(
                            "CollectionTab.UnableToCheckForUpdate",
                            "Could not connect to the server to check for an update. Are you connected to the internet?",
                            "Shown when Bloom tries to check for an update but can't, for example because it can't connect to the internet, or a problems with our server, etc."
                        );
                        ShowFailureNotification(failMsg);
                    }
                    else if (
                        result.Error == null || string.IsNullOrWhiteSpace(result.Error.Message)
                    )
                    {
                        SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                            "Bloom failed to find if there is an update available, for some unknown reason."
                        );
                    }
                    else
                    {
                        ShowFailureNotification(result.Error.Message);
                    }

                    return true;
                }

                updateUrl = result.URL;
            }
            return false;
        }

        private static void ShowFailureNotification(string failMsg)
        {
            var failNotifier = new ToastNotifier();
            failNotifier.Image.Image = Resources.Bloom;
            failNotifier.Show(failMsg, "", 5);
        }

        internal const string kChannelNameForUnitTests = "TestChannel";

        public static bool IsDevOrAlpha
        {
            get
            {
                var channel = ApplicationUpdateSupport.ChannelName.ToLowerInvariant();
                return channel.Contains("developer")
                    || channel.Contains("alpha")
                    || channel.Contains("unstable");
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
                if (Program.RunningUnitTests)
                    return kChannelNameForUnitTests;

                var path = Assembly.GetEntryAssembly().ManifestModule.FullyQualifiedName;
                // Use a very specific channel name on developer machines based on build configuration.
                if (path.Replace('\\', '/').EndsWith("/output/Debug/Bloom.exe"))
                    return "Developer/Debug"; // verifies this code is running on a developer machine.
                if (path.Replace('\\', '/').EndsWith("/output/Release/Bloom.exe"))
                    return "Developer/Release"; // verifies this code is running on a developer machine.
                if (Platform.IsUnix)
                {
                    // The specific directories where the program is installed reflect
                    // the status ("channel") of the program on Linux.
                    if (path.Contains("/bloom-desktop-alpha/"))
                        return "Alpha";
                    if (path.Contains("/bloom-desktop-beta/"))
                        return "Beta";
                    // The next two have never existed yet, but maybe someday we'll want to use them.
                    if (path.Contains("/bloom-desktop-betainternal/"))
                        return "BetaInternal";
                    if (path.Contains("/bloom-desktop-internal/"))
                        return "ReleaseInternal";
                    return "Release";
                }
                var s = Assembly
                    .GetEntryAssembly()
                    .ManifestModule.Name.Replace("bloom", "")
                    .Replace("Bloom", "")
                    .Replace(".exe", "")
                    .Trim();
                return (s == "") ? "Release" : s;
            }
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
            get
            {
                return Platform.IsWindows
                    && _bloomUpdateManager == null
                    && !InstallerSupport.SharedByAllUsers()
                    && !ApplicationUpdateSupport.IsDev;
            }
#endif
        }

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

        private static void UpdateProgress(
            ToastNotifier updatingNotifier,
            string updatingMsg,
            string progressMsg,
            int x
        )
        {
            if (updatingNotifier.IsHandleCreated) // Must have a handle to Invoke anything!
            {
                try
                {
                    updatingNotifier.Invoke(
                        (Action)(
                            () =>
                            {
                                updatingNotifier.UpdateMessage(
                                    updatingMsg + " " + string.Format(progressMsg, x)
                                );
                            }
                        )
                    );
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
