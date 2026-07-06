using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.CollectionTab;
using Bloom.Publish.BloomPub;
using Bloom.ToPalaso;
using Bloom.web;
using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Publish.Rab
{
    /// <summary>
    /// Coordinates Bloom's Reading App Builder workflow, including preparing the project, building an APK, and installing it on a device.
    /// </summary>
    public class RabProjectService
    {
        private const string kDefaultAlias = "bloomkey";
        private const string kWebSocketContext = RabPublishApi.kWebSocketContext;
        private const string kBloomOwnedRabToolchainFolderName = "ReadingAppBuilder";
        private const string kRabInstallFolderParentName = "SIL";
        private const string kBloomRabInstallFolderName = "Reading App Builder for Bloom";
        private const string kRabRegistrySubKey = @"Software\SIL\Reading App Builder";
        private const string kBloomRabRegistrySubKey =
            @"Software\SIL\Reading App Builder for Bloom";
        private const int kUserCanceledShellLaunchErrorCode = 1223;
        private const string kRabSetupInstallerPrefix = "Reading-App-Builder-For-Bloom-";
        private const string kRabInstallerVersion = "14-0";
        private const string kRabSetupInstallerSuffix = "-Setup.exe";
        internal const string kRabSetupInstallerFileName =
            kRabSetupInstallerPrefix + kRabInstallerVersion + kRabSetupInstallerSuffix;
        private const string kDefaultBundledIconId = "bloom-app-icon-52";

        // Keep this URL in sync with rabInstallerDownloadUrl in BloomBrowserUI/publish/Apps/AppPublisherScreen.tsx.
        private const string kRabSetupDownloadUrl =
            "https://bloomlibrary.org/RAB/installers/" + kRabSetupInstallerFileName;
        private const string kRabSetupLanguage = "en";
        private const int kRabLaunchPollIntervalMs = 250;
        private const int kRabLaunchTimeoutMs = 60000;
        private static readonly TimeSpan kRabInstallerDownloadLogInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan kRabInstallerDownloadTimeout = TimeSpan.FromMinutes(30);
        private static readonly (int Size, string RelativePath)[] kLauncherIconFiles =
        {
            (36, @"drawable-ldpi\ic_launcher.png"),
            (48, @"drawable-mdpi\ic_launcher.png"),
            (72, @"drawable-hdpi\ic_launcher.png"),
            (96, @"drawable-xhdpi\ic_launcher.png"),
            (144, @"drawable-xxhdpi\ic_launcher.png"),
            (192, @"drawable-xxxhdpi\ic_launcher.png"),
            (512, @"drawable-web\ic_launcher.png"),
        };
        internal const long kEstimatedAppOverheadBytes = 12L * 1000 * 1000;
        public const long kMaxAppSizeBytes = 100L * 1000 * 1000;
        private static readonly (
            string organizationName,
            string packageSegment
        )[] kOrganizationPackageSegments = { ("SIL International", "sil"), ("SIL", "sil") };
        private readonly CollectionModel _collectionModel;
        private readonly BookSelection _bookSelection;
        private readonly BookServer _bookServer;
        private readonly CollectionSettings _collectionSettings;
        private readonly BloomWebSocketServer _webSocketServer;
        private readonly IWebSocketProgress _progress;
        private volatile string _activeProgressAction;
        private int _lastBuildProgressPercent;
        private volatile string _lastLoggedProgressStage;
        private volatile int _lastLoggedProgressPercent = -1;

        // When non-null, RAB process output lines are collected here (in addition to being sent to
        // the progress log) so a build that produces no APK can surface RAB's own diagnostics.
        // Writes happen on threadpool threads from both the stdout and stderr process callbacks, so
        // access is guarded by _rabOutputCaptureLock to keep the List<string> from corrupting.
        private List<string> _rabOutputCapture;
        private readonly object _rabOutputCaptureLock = new object();

        public RabProjectService(
            CollectionModel collectionModel,
            BookSelection bookSelection,
            BookServer bookServer,
            CollectionSettings collectionSettings,
            BloomWebSocketServer webSocketServer
        )
            : this(
                collectionModel,
                bookSelection,
                bookServer,
                collectionSettings,
                webSocketServer,
                null
            ) { }

        internal RabProjectService(
            CollectionModel collectionModel,
            BookSelection bookSelection,
            BookServer bookServer,
            CollectionSettings collectionSettings,
            BloomWebSocketServer webSocketServer,
            IWebSocketProgress progress
        )
        {
            _collectionModel = collectionModel;
            _bookSelection = bookSelection;
            _bookServer = bookServer;
            _collectionSettings = collectionSettings;
            _webSocketServer = webSocketServer;
            _progress = progress ?? new WebSocketProgress(_webSocketServer, kWebSocketContext);
        }

        /// <summary>
        /// Prepares the collection-owned Reading App Builder workspace so the project can be customized or built.
        /// </summary>
        public Task PrepareAsync()
        {
            Prepare();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Builds a fresh APK from the current prepared project state.
        /// </summary>
        public Task BuildAsync()
        {
            Build();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Clears cached BloomPUB exports that are only intended to live for the current Apps screen session.
        /// </summary>
        public void ResetBloomPubCacheForScreenSession()
        {
            var paths = GetPaths();
            EnsureWorkspaceFolders(paths);
            DeleteStaleBloomPubExports(
                paths.BloomPubRoot,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            );
        }

        /// <summary>
        /// Installs the latest APK on a connected Android device and launches it.
        /// </summary>
        public Task InstallAsync()
        {
            Install();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Loads the effective app settings by merging the saved RAB project with Bloom-derived defaults.
        /// </summary>
        public RabAppSettings GetAppSettings()
        {
            var paths = GetPaths();
            return GetEffectiveAppSettings(paths);
        }

        /// <summary>
        /// Returns the default app settings Bloom would use before a project has been prepared.
        /// </summary>
        public RabAppSettings GetDefaultSettings()
        {
            return GetDefaultAppSettings();
        }

        /// <summary>
        /// Returns all app icon choices available from Bloom and the installed Reading App Builder toolchain.
        /// </summary>
        public IReadOnlyCollection<RabIconChoice> GetAvailableIconChoices()
        {
            return new[] { GetBundledIconRoot(), GetRabInstalledIconRoot() }
                .SelectMany(GetAvailableIconChoicesFromRoot)
                .GroupBy(choice => choice.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Where(choice => choice != null)
                .OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private IEnumerable<RabIconChoice> GetAvailableIconChoicesFromRoot(string iconRoot)
        {
            if (string.IsNullOrWhiteSpace(iconRoot) || !Directory.Exists(iconRoot))
                return Array.Empty<RabIconChoice>();

            var folderChoices = Directory
                .GetDirectories(iconRoot)
                .Select(directory =>
                {
                    var directoryName = Path.GetFileName(directory);
                    return CreateIconChoice(
                        directoryName,
                        Path.Combine(directory, directoryName + ".png")
                    );
                });

            var flatFileChoices = Directory
                .GetFiles(iconRoot, "*.png")
                .Select(iconPath =>
                    CreateIconChoice(Path.GetFileNameWithoutExtension(iconPath), iconPath)
                );

            return folderChoices.Concat(flatFileChoices).Where(choice => choice != null);
        }

        private RabIconChoice CreateIconChoice(string iconId, string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconId) || !RobustFile.Exists(iconPath))
                return null;

            return new RabIconChoice()
            {
                Id = iconId,
                Label = iconId,
                IconPath = iconPath,
            };
        }

        /// <summary>
        /// Persists app settings back to the prepared Reading App Builder project.
        /// </summary>
        public void SaveAppSettings(RabAppSettings settings)
        {
            var paths = GetPaths();
            EnsureWorkspaceFolders(paths);

            var state = EnsureStateHasProjectAndSigningInfo(
                paths,
                LoadState(paths) ?? new RabPrepareState()
            );
            if (string.IsNullOrWhiteSpace(state.AppDefPath) || !RobustFile.Exists(state.AppDefPath))
                throw new ApplicationException(
                    "Run Prepare before customizing the Reading App Builder project."
                );

            var normalizedSettings = NormalizeSettingsForPersistence(paths, settings);

            var project = RabAppProject.Load(state.AppDefPath);
            project.SetAppSettings(normalizedSettings);
            EnsureAboutText(paths, normalizedSettings);
            SynchronizeProjectIconFiles(paths, project, normalizedSettings);
            project.Save();
            SaveState(paths, state);
        }

        /// <summary>
        /// Persists the ordered list of books Bloom should export into the prepared Reading App Builder project.
        /// </summary>
        public void SaveTrackedBooks(IReadOnlyCollection<RabTrackedBookInfo> trackedBooks)
        {
            if (trackedBooks == null || trackedBooks.Count == 0)
                throw new ApplicationException("Choose at least one book for the app.");

            var paths = GetPaths();
            EnsureWorkspaceFolders(paths);

            var state = EnsureStateHasProjectAndSigningInfo(
                paths,
                LoadState(paths) ?? new RabPrepareState()
            );
            var existingByFolder = (state.Books ?? new List<RabBookPublishInfo>())
                .Where(book => !string.IsNullOrWhiteSpace(book.FolderPath))
                .ToDictionary(book => book.FolderPath, StringComparer.OrdinalIgnoreCase);

            state.Books = trackedBooks
                .Select(trackedBook => CreateTrackedBookPublishInfo(trackedBook, existingByFolder))
                .ToList();
            SaveState(paths, state);
        }

        /// <summary>
        /// Opens the prepared Reading App Builder project in the external RAB application.
        /// </summary>
        public void OpenInRab()
        {
            OpenInRabInternal();
        }

        /// <summary>
        /// Opens the prepared Reading App Builder project and waits until the RAB window is visible.
        /// </summary>
        public async Task OpenInRabAndWaitForWindowAsync()
        {
            OpenInRabInternal();
            await WaitForRabWindowAsync();
        }

        private void OpenInRabInternal()
        {
            var paths = GetPaths();
            var appDefPath = FindAppDefPath(paths);
            if (string.IsNullOrWhiteSpace(appDefPath) || !RobustFile.Exists(appDefPath))
                throw new ApplicationException(
                    "Run Prepare before opening the app in Reading App Builder."
                );

            var rabLauncher = FindRabLauncherPath();
            if (string.IsNullOrWhiteSpace(rabLauncher))
                throw new ApplicationException(GetRabNotFoundMessage());

            if (TryBringRunningRabToFront())
                return;

            SeedRabSettingsToOpenProject(appDefPath);

            StartDetachedProcess(
                "cmd.exe",
                $"/d /c call {QuoteArgument(rabLauncher)}",
                Path.GetDirectoryName(rabLauncher),
                GetRabProcessEnvironmentVariables()
            );
        }

        private async Task WaitForRabWindowAsync()
        {
            var elapsedMilliseconds = 0;
            while (elapsedMilliseconds < kRabLaunchTimeoutMs)
            {
                if (TryBringRunningRabToFront())
                    return;

                await Task.Delay(kRabLaunchPollIntervalMs);
                elapsedMilliseconds += kRabLaunchPollIntervalMs;
            }
        }

        /// <summary>
        /// Returns the current prepare/build/install status that drives the Apps screen.
        /// </summary>
        public RabProjectStatus GetStatus()
        {
            var paths = GetPaths();
            var appDefPath = FindAppDefPath(paths);
            var latestApk = FindLatestApkPath(paths);
            var apkExists = !string.IsNullOrEmpty(latestApk) && RobustFile.Exists(latestApk);
            var rabInstalled = IsRabInstalledForPrepare();
            var state = EnsureStateHasProjectAndSigningInfo(paths, LoadState(paths), appDefPath);
            var prepareSteps = GetPrepareSteps(paths, state, appDefPath, rabInstalled);
            var trackedBooks = GetConfiguredTrackedBooks(paths).ToArray();
            var currentInputSignature = ComputeBuildInputSignature(
                GetEffectiveAppSettings(paths),
                trackedBooks
            );
            var buildNeeded =
                !string.IsNullOrEmpty(appDefPath)
                && RobustFile.Exists(appDefPath)
                && (
                    string.IsNullOrWhiteSpace(latestApk)
                    || !apkExists
                    || !string.Equals(
                        state?.LastBuiltInputSignature,
                        currentInputSignature,
                        StringComparison.Ordinal
                    )
                );

            // read this once early in the method to avoid returning inconsistent progress info if
            // another thread changes it.
            var activeAction = _activeProgressAction;
            var status = new RabProjectStatus()
            {
                RabInstalled = rabInstalled,
                ProjectExists = !string.IsNullOrEmpty(appDefPath) && RobustFile.Exists(appDefPath),
                ApkExists = apkExists,
                BuildNeeded = buildNeeded,
                UserDownloadsDirectory = GetUserDownloadsDirectory(),
                AppDefPath = appDefPath,
                ApkPath = latestApk,
                ApkSizeBytes = apkExists ? new FileInfo(latestApk).Length : 0,
                RabRoot = paths.RabRoot,
                TrackedBooks = trackedBooks,
                TrackedBookTitles = trackedBooks.Select(book => book.Title).ToArray(),
                PrepareSteps = prepareSteps,
                ActiveAction = activeAction,
                ActiveActionProgressStage = activeAction != null ? _lastLoggedProgressStage : null,
                ActiveActionProgressPercent =
                    activeAction != null
                        ? (_lastLoggedProgressPercent < 0 ? 0 : _lastLoggedProgressPercent)
                        : 0,
            };

            if (status.ProjectExists)
            {
                var project = RabAppProject.Load(appDefPath);
                status.AppName = project.AppName;
                if (status.TrackedBookTitles.Length == 0)
                    status.TrackedBookTitles = project.BookTitles;
            }
            else
            {
                status.AppName = GetEffectiveAppSettings(paths).AppName;
            }

            return status;
        }

        /// <summary>
        /// Estimates per-book and overall app size using available BloomPUB exports when possible.
        /// </summary>
        public RabAppSizeEstimates GetSizeEstimates()
        {
            var paths = GetPaths();
            return new RabAppSizeEstimates()
            {
                Books = GetCollectionBooksForSizeEstimates()
                    .Select(book =>
                    {
                        var bloomPubPath = Path.Combine(
                            paths.BloomPubRoot,
                            Path.GetFileName(book.FolderPath)
                                + BloomPubMaker.BloomPubExtensionWithDot
                        );
                        var hasBloomPub =
                            !string.IsNullOrWhiteSpace(bloomPubPath)
                            && RobustFile.Exists(bloomPubPath);
                        return new RabBookSizeEstimate()
                        {
                            BookId = book.BookId,
                            FolderPath = book.FolderPath,
                            Title = book.Title,
                            SizeBytes = hasBloomPub
                                ? new FileInfo(bloomPubPath).Length
                                : GetFolderSizeBytes(book.FolderPath),
                            IsActual = hasBloomPub,
                        };
                    })
                    .ToArray(),
                EstimatedAppOverheadBytes = kEstimatedAppOverheadBytes,
                MaxAppSizeBytes = kMaxAppSizeBytes,
            };
        }

        /// <summary>
        /// True while a prepare/build/install action is running on a background thread.
        /// </summary>
        internal bool IsActionInProgress => _activeProgressAction != null;

        /// <summary>
        /// Atomically claims the action slot, setting <see cref="_activeProgressAction"/> to
        /// <paramref name="action"/>. Returns true if this call won the slot, false if another
        /// action is already running.
        /// </summary>
        internal bool TryBeginAction(string action)
        {
            if (Interlocked.CompareExchange(ref _activeProgressAction, action, null) != null)
                return false;
            // Reset stale progress from the previous action so a status poll that lands
            // before the first ReportProgressStage call doesn't serve stale values.
            _lastLoggedProgressStage = null;
            _lastLoggedProgressPercent = -1;
            return true;
        }

        /// <summary>
        /// Releases the action slot. Called by <see cref="RabPublishApi"/> just before
        /// <see cref="SendActionCompleteEvent"/> so the slot remains claimed until the
        /// action is complete. (We clear it before actually sending the notification
        /// so that the notification handlers correctly see that no task is running).
        /// </summary>
        internal void ClearAction()
        {
            _activeProgressAction = null;
        }

        /// <summary>
        /// Sends an "actionComplete" websocket event so the Apps screen knows when a background
        /// prepare/build/install has finished.  Called by RabPublishApi after ReportFailure (if
        /// any) so the error message is logged before the UI tears down the progress subscriber.
        /// </summary>
        internal void SendActionCompleteEvent(string action, bool succeeded)
        {
            _webSocketServer.SendString(
                kWebSocketContext,
                RabPublishApi.kWebSocketEventId_ActionComplete,
                $"{action}:{(succeeded ? "success" : "failure")}"
            );
        }

        /// <summary>
        /// Reports an App Builder failure to Bloom's log and the progress channel used by the Apps screen.
        /// </summary>
        public void ReportFailure(string action, Exception error)
        {
            Logger.WriteError($"Reading App Builder {action} failed.", error);
            _progress.MessageWithoutLocalizing(
                $"{action} failed: {error.GetBaseException().Message}",
                ProgressKind.Error
            );
            if (!string.IsNullOrWhiteSpace(Logger.LogPath))
                _progress.MessageWithoutLocalizing($"Bloom log: {Logger.LogPath}");
        }

        private void Prepare()
        {
            var paths = GetPaths();
            ReportProgressStage("checking-installer", 0);
            if (!EnsureRabInstalledForPrepare())
                return;

            // Prepare makes the on-disk RAB workspace match Bloom's current settings and tracked-book list,
            // creating the project only on the first run and refreshing inputs after that.
            EnsureWorkspaceFolders(paths);
            EnsureRabBuildPrerequisites(paths);

            ReportProgressStage("preparing-workspace", 0);
            _progress.MessageWithoutLocalizing(
                "Preparing the Reading App Builder workspace...",
                ProgressKind.Heading
            );

            ReportProgressStage("exporting-bloompubs", 10);
            var trackedBooks = ExportPrepareBooks(paths);
            var effectiveSettings = GetEffectiveAppSettings(paths);
            var supportFiles = EnsureProjectSupportFiles(paths);
            var existingProjectPath = FindAppDefPath(paths);
            RabPrepareState state;
            var createdNewProject = string.IsNullOrEmpty(existingProjectPath);

            if (createdNewProject)
            {
                ReportProgressStage("generating-signing-key", 55);
                _progress.MessageWithoutLocalizing("Preparing Bloom's Android signing key...");
                state = CreatePrepareState(EnsureBloomOwnedSigningState(paths));

                ReportProgressStage("creating-project", 70);
                _progress.MessageWithoutLocalizing(
                    "Creating the initial Reading App Builder project..."
                );
                RunRabCommand(
                    BuildRabArgsForNewProject(
                        paths,
                        effectiveSettings,
                        trackedBooks,
                        state,
                        supportFiles
                    ),
                    paths.RabRoot
                );

                existingProjectPath = FindAppDefPath(paths);
                if (string.IsNullOrEmpty(existingProjectPath))
                    throw new ApplicationException(
                        "Reading App Builder finished, but Bloom could not find the generated .appDef file."
                    );
            }
            else
            {
                ReportProgressStage("updating-project", 70);
                _progress.MessageWithoutLocalizing(
                    "The Reading App Builder project already exists. Refreshing BloomPUB inputs instead."
                );
                state = EnsureStateHasProjectAndSigningInfo(
                    paths,
                    LoadState(paths) ?? new RabPrepareState(),
                    existingProjectPath
                );
                state = ApplyBloomOwnedSigningState(state, EnsureBloomOwnedSigningState(paths));
            }

            state = EnsureStateHasProjectAndSigningInfo(paths, state, existingProjectPath);
            EnsureKeystore(state.KeystorePath, state.KeystorePassword);
            state.Books = trackedBooks;

            if (!createdNewProject)
            {
                PrepareProjectForTrackedBookImport(existingProjectPath, trackedBooks);
                ReportProgressStage("updating-project", 85);
                RunRabCommand(
                    BuildRabArgsForProjectUpdate(paths, state, trackedBooks, supportFiles, false),
                    paths.RabRoot
                );
            }

            ReconcileProjectWithImportedBooks(existingProjectPath, trackedBooks);
            SynchronizeProjectFonts(existingProjectPath, trackedBooks);
            ReportProgressStage("updating-project", 85);
            SaveState(paths, state);

            ReportProgressStage("complete", 100);
            _progress.MessageWithoutLocalizing("Prepare complete.", ProgressKind.Heading);
            _progress.MessageWithoutLocalizing($"Project file: {existingProjectPath}");
        }

        private bool EnsureRabInstalledForPrepare()
        {
            ReportProgressStage("checking-installer", 0);

            if (IsRabInstalledForPrepare())
                return true;

            var installerPath = GetRabSetupInstallerPath();
            if (!string.IsNullOrWhiteSpace(installerPath))
            {
                ReportProgressStage("running-installer", 0);
                _progress.MessageWithoutLocalizing(
                    "Reading App Builder is not installed at the registry install path. Installing it now...",
                    ProgressKind.Heading
                );
                try
                {
                    InstallRabFromSetup(installerPath);
                }
                catch (Win32Exception error) when (IsUserCanceledShellLaunch(error))
                {
                    _progress.MessageWithoutLocalizing(
                        $"Reading App Builder installer did not start. Windows canceled the shell launch before the installer process started (error {error.NativeErrorCode}: {error.Message}). Bloom did not cancel it.",
                        ProgressKind.Warning
                    );
                    _progress.MessageWithoutLocalizing($"Installer: {installerPath}");
                    return false;
                }
                if (!IsRabInstalledForPrepare())
                    throw new ApplicationException(
                        "Reading App Builder installer finished, but Bloom still could not find the installed program."
                    );

                _progress.MessageWithoutLocalizing(
                    "Reading App Builder installation complete.",
                    ProgressKind.Heading
                );
                _progress.MessageWithoutLocalizing($"Installer: {installerPath}");
                return true;
            }

            ReportProgressStage("downloading-installer", 0);
            _progress.MessageWithoutLocalizing(
                "Reading App Builder is not installed at the registry install path. Bloom could not download the installer.",
                ProgressKind.Heading
            );
            _progress.MessageWithoutLocalizing($"Download: {kRabSetupDownloadUrl}");
            return false;
        }

        internal virtual string GetRabSetupInstallerPath()
        {
            var existingInstallerPath = FindRabSetupInstallerPath();
            if (!string.IsNullOrWhiteSpace(existingInstallerPath))
                return existingInstallerPath;

            ReportProgressStage("downloading-installer", 0);
            _progress.MessageWithoutLocalizing(
                "Reading App Builder is not installed at the registry install path. Downloading it now...",
                ProgressKind.Heading
            );

            var downloadedInstallerPath = DownloadRabSetupInstaller();
            _progress.MessageWithoutLocalizing($"Download: {kRabSetupDownloadUrl}");
            return downloadedInstallerPath;
        }

        private void EnsureRabBuildPrerequisites(RabWorkspacePaths paths)
        {
            ReportProgressStage("installing-build-tools", 5);
            _progress.MessageWithoutLocalizing(
                "Making sure Reading App Builder's JDK and Android SDK are installed...",
                ProgressKind.Heading
            );
            ResetIncompleteRabBuildToolFolders();
            RunRabCommand(BuildRabArgsForInstallingSdks(), paths.RabRoot);
        }

        private void ResetIncompleteRabBuildToolFolders()
        {
            if (Directory.Exists(GetRabJdkInstallFolder()) && !IsRabJdkInstalled())
            {
                _progress.MessageWithoutLocalizing(
                    $"Deleting incomplete Reading App Builder JDK folder: {GetRabJdkInstallFolder()}"
                );
                RobustIO.DeleteDirectoryAndContents(GetRabJdkInstallFolder());
            }

            if (Directory.Exists(GetRabAndroidSdkInstallFolder()) && !IsRabAndroidSdkInstalled())
            {
                _progress.MessageWithoutLocalizing(
                    $"Deleting incomplete Reading App Builder Android SDK folder: {GetRabAndroidSdkInstallFolder()}"
                );
                RobustIO.DeleteDirectoryAndContents(GetRabAndroidSdkInstallFolder());
            }
        }

        private void Build()
        {
            // Build reuses BloomPUBs created during the current Apps-screen session and regenerates only missing ones.
            var paths = GetPaths();
            EnsureWorkspaceFolders(paths);
            _lastBuildProgressPercent = 0;
            ReportProgressStage("preparing-build", 0);
            var state = LoadStateOrThrow(paths);
            EnsureKeystore(state.KeystorePath, state.KeystorePassword);
            EnsureRabBuildPrerequisites(paths);
            ReportProgressStage("exporting-bloompubs", 10);
            var trackedBooks = ExportTrackedBooks(paths, state);
            var supportFiles = EnsureProjectSupportFiles(paths);

            ReportProgressStage("updating-project", 35);
            _progress.MessageWithoutLocalizing(
                "Updating the Reading App Builder project with fresh BloomPUB files..."
            );
            PrepareProjectForTrackedBookImport(state.AppDefPath, trackedBooks);
            RunRabCommand(
                BuildRabArgsForProjectUpdate(paths, state, trackedBooks, supportFiles, false),
                paths.RabRoot
            );
            ReconcileProjectWithImportedBooks(state.AppDefPath, trackedBooks);
            SynchronizeProjectFonts(state.AppDefPath, trackedBooks);
            state.Books = trackedBooks;
            SaveState(paths, state);

            ReportProgressStage("building-android-app", 45);
            _progress.MessageWithoutLocalizing(
                "Building the Android app with Reading App Builder...",
                ProgressKind.Heading
            );
            Directory.CreateDirectory(paths.SafeApkRoot);

            // Capture this run's RAB output so that, if no APK is produced, we can surface
            // Reading App Builder's own diagnostics (e.g. a missing font) instead of a generic
            // "no APK was found" message that gives the user nothing to act on (BL-16467).
            var buildOutput = new List<string>();
            _rabOutputCapture = buildOutput;
            try
            {
                RunRabCommand(
                    BuildRabArgsForProjectUpdate(
                        paths,
                        state,
                        Array.Empty<RabBookPublishInfo>(),
                        supportFiles,
                        true
                    ),
                    paths.RabRoot
                );
            }
            catch (ApplicationException e)
            {
                // RAB exited with an error (RunProcess throws here on a non-zero exit code).
                // Its own stdout/stderr (collected in buildOutput) usually explains why — e.g. a
                // missing font — so surface those diagnostics alongside the exit-code summary
                // instead of leaving the user with a bare "cmd.exe exited with code N" (BL-16467).
                // The original exception is preserved as InnerException for the log.
                throw new ApplicationException(DescribeFailedRabBuild(e.Message, buildOutput), e);
            }
            finally
            {
                _rabOutputCapture = null;
            }

            ReportProgressStage("finalizing-apk", 98);

            var apkPath = FindLatestApkPath(paths);
            if (string.IsNullOrEmpty(apkPath))
                throw new ApplicationException(DescribeMissingApkFailure(buildOutput));

            ReportProgressStage("complete", 100);
            _progress.MessageWithoutLocalizing("Build complete.", ProgressKind.Heading);
            _progress.MessageWithoutLocalizing($"APK: {apkPath}");

            state.LastBuiltInputSignature = ComputeBuildInputSignature(
                GetEffectiveAppSettings(paths),
                trackedBooks
            );
            state.LastBuiltApkPath = apkPath;
            SaveState(paths, state);
        }

        // Markers that flag a RAB output line as worth surfacing when a build produces no APK.
        // "fail" intentionally also matches "failed"/"failure"; "Message_" matches RAB's pre-build
        // requirement keys such as Message_Build_Add_Font.
        private static readonly string[] kRabProblemMarkers =
        {
            "not found",
            "missing",
            "error",
            "fail",
            "warning",
            "cannot",
            "could not",
            "unable",
            "not valid",
            "invalid",
            "Message_",
        };

        /// <summary>
        /// Builds the error message for the case where Reading App Builder ran but produced no APK.
        /// RAB normally explains the problem in its own output (for example a missing font, or an
        /// app that is not yet configured to build), so we surface those lines rather than a
        /// generic "no APK was found" message that leaves the user with nothing to act on
        /// (BL-16467).
        /// </summary>
        internal static string DescribeMissingApkFailure(IReadOnlyList<string> rabOutputLines)
        {
            const string header =
                "Reading App Builder finished without producing an Android app (APK).";

            var notableLines = ExtractNotableRabOutputLines(rabOutputLines);
            if (notableLines.Count == 0)
                return header
                    + " It did not report a reason; please check the Reading App Builder messages above for details.";

            return header
                + " Reading App Builder reported:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, notableLines.Select(line => "    " + line));
        }

        /// <summary>
        /// Builds the error message for the case where Reading App Builder exits with an error
        /// (a non-zero exit code) rather than running cleanly to a missing-APK state. The
        /// exit-code summary (<paramref name="failureSummary"/>) is kept so the user still sees
        /// what failed, and RAB's own diagnostics are appended when it reported any, so a build
        /// that fails part-way still surfaces something actionable instead of a bare exit-code
        /// message (BL-16467).
        /// </summary>
        internal static string DescribeFailedRabBuild(
            string failureSummary,
            IReadOnlyList<string> rabOutputLines
        )
        {
            var notableLines = ExtractNotableRabOutputLines(rabOutputLines);
            if (notableLines.Count == 0)
                return failureSummary;

            return failureSummary
                + Environment.NewLine
                + "Reading App Builder reported:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, notableLines.Select(line => "    " + line));
        }

        /// <summary>
        /// Picks the RAB output lines most likely to explain a failed build. Falls back to the tail
        /// of the output when nothing matches a known problem marker, so we never hide RAB's last
        /// words.
        /// </summary>
        private static List<string> ExtractNotableRabOutputLines(
            IReadOnlyList<string> rabOutputLines
        )
        {
            if (rabOutputLines == null)
                return new List<string>();

            var cleaned = rabOutputLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();

            const int kMaxLines = 25;
            var notable = cleaned
                .Where(line =>
                    kRabProblemMarkers.Any(marker =>
                        line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0
                    )
                )
                .Distinct()
                .ToList();

            if (notable.Count > 0)
                return notable.Take(kMaxLines).ToList();

            // Nothing matched a known marker; show the last few lines so the user still has
            // something concrete to look at.
            const int kTailLineCount = 15;
            return cleaned.Skip(Math.Max(0, cleaned.Count - kTailLineCount)).ToList();
        }

        private void Install()
        {
            // Install re-reads package/app metadata from the project so launching uses the same identity that was built.
            var paths = GetPaths();
            var apkPath = FindLatestApkPath(paths);
            if (string.IsNullOrEmpty(apkPath))
                throw new ApplicationException(
                    "Build the app before trying to install it on a phone."
                );

            var adbPath = FindAdbPath();
            if (string.IsNullOrEmpty(adbPath))
                throw new ApplicationException(
                    "Bloom could not find adb. Install Android platform-tools or make adb available on PATH."
                );

            var project = LoadProjectOrNull(paths);
            var packageName = project?.PackageName;
            if (string.IsNullOrWhiteSpace(packageName))
                packageName = GetPackageName();

            var appName = project?.AppName;
            if (string.IsNullOrWhiteSpace(appName))
                appName = GetAppName();

            ReportProgressStage("checking-device", 0);
            _progress.MessageWithoutLocalizing("Checking for a connected Android device...");
            var device = GetSingleConnectedDevice(adbPath);

            ReportProgressStage("installing-on-phone", 45);
            _progress.MessageWithoutLocalizing(
                $"Installing {Path.GetFileName(apkPath)} on {device.DisplayName}...",
                ProgressKind.Heading
            );
            var installResult = InstallApkOnDevice(adbPath, device.Serial, apkPath, paths.RabRoot);
            if (installResult.ExitCode != 0)
            {
                if (IsUpdateIncompatibleInstallFailure(installResult.Output))
                {
                    _progress.MessageWithoutLocalizing(
                        $"A different signed copy of {appName} is already installed on {device.DisplayName}. Removing it and retrying...",
                        ProgressKind.Warning
                    );
                    UninstallAppFromDevice(adbPath, device.Serial, packageName, paths.RabRoot);
                    installResult = InstallApkOnDevice(
                        adbPath,
                        device.Serial,
                        apkPath,
                        paths.RabRoot
                    );
                }

                if (installResult.ExitCode != 0)
                {
                    throw new ApplicationException(
                        $"{Path.GetFileName(adbPath)} exited with code {installResult.ExitCode}."
                    );
                }
            }

            ReportProgressStage("launching-on-phone", 85);
            _progress.MessageWithoutLocalizing(
                $"Launching {appName} on {device.DisplayName}...",
                ProgressKind.Heading
            );
            RunProcess(adbPath, BuildLaunchAppArguments(device.Serial, packageName), paths.RabRoot);
            ReportProgressStage("complete", 100);
            _progress.MessageWithoutLocalizing(
                $"Install complete. Opened {appName} on {device.DisplayName}.",
                ProgressKind.Heading
            );
        }

        internal virtual RabWorkspacePaths GetPaths()
        {
            var collectionRoot = _collectionModel.TheOneEditableCollection.PathToDirectory;
            return new RabWorkspacePaths(collectionRoot, GetBloomOwnedRabToolchainRoot());
        }

        internal virtual string GetAppName()
        {
            return string.IsNullOrWhiteSpace(_collectionSettings.CollectionName)
                ? Path.GetFileName(_collectionModel.TheOneEditableCollection.PathToDirectory)
                : _collectionSettings.CollectionName;
        }

        internal virtual string GetPackageName()
        {
            return MakeDefaultPackageName("stories", _collectionSettings?.Language1Tag);
        }

        internal static string MakeDefaultPackageName(string projectSlug, string language1Tag)
        {
            var segments = GetPackageNameSegments(projectSlug);
            var languageSegment = GetLanguagePackageSegment(language1Tag);
            var packagePrefix = string.IsNullOrWhiteSpace(languageSegment)
                ? "org.sil.bloom"
                : $"org.sil.{languageSegment}";
            return packagePrefix + "." + string.Join(".", segments);
        }

        internal static string MakeProjectSlug(string baseName)
        {
            var chars = (baseName ?? string.Empty)
                .ToLowerInvariant()
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray();
            var slug = new string(chars);
            while (slug.Contains("--"))
                slug = slug.Replace("--", "-");

            slug = slug.Trim('-');
            if (string.IsNullOrWhiteSpace(slug))
                slug = "bloom-app";

            return slug;
        }

        internal static string MakePackageName(string projectSlug)
        {
            return MakePackageName(projectSlug, null, null);
        }

        internal static string MakePackageName(
            string projectSlug,
            string copyrightNotice,
            IEnumerable<(string organizationName, string packageSegment)> organizationPairs
        )
        {
            var segments = GetPackageNameSegments(projectSlug);
            if (segments.Length == 0)
                return "org.sil.bloom.app";

            var organizationSegment = GetOrganizationPackageSegment(
                copyrightNotice,
                organizationPairs
            );
            var packagePrefix = string.IsNullOrWhiteSpace(organizationSegment)
                ? "org.sil.bloom"
                : $"org.{organizationSegment}.bloom";
            return packagePrefix + "." + string.Join(".", segments);
        }

        private static string[] GetPackageNameSegments(string projectSlug)
        {
            return MakeProjectSlug(projectSlug)
                .Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePackageSegment)
                .ToArray();
        }

        private static string GetLanguagePackageSegment(string language1Tag)
        {
            var segment = MakeProjectSlug(language1Tag).Replace("-", string.Empty);
            return string.IsNullOrWhiteSpace(segment) ? null : NormalizePackageSegment(segment);
        }

        private static string NormalizePackageSegment(string segment)
        {
            return char.IsDigit(segment[0]) ? "a" + segment : segment;
        }

        private static string GetOrganizationPackageSegment(
            string copyrightNotice,
            IEnumerable<(string organizationName, string packageSegment)> organizationPairs
        )
        {
            var rightsHolder = ExtractRightsHolder(copyrightNotice);
            if (string.IsNullOrWhiteSpace(rightsHolder))
                return null;

            if (organizationPairs != null)
            {
                foreach (var (organizationName, packageSegment) in organizationPairs)
                {
                    if (
                        !string.IsNullOrWhiteSpace(packageSegment)
                        && OrganizationNamesMatch(rightsHolder, organizationName)
                    )
                        return packageSegment.Trim().ToLowerInvariant();
                }
            }

            var segment = MakeProjectSlug(rightsHolder).Replace("-", string.Empty);
            if (string.IsNullOrWhiteSpace(segment))
                return null;

            return char.IsDigit(segment[0]) ? "org" + segment : segment;
        }

        private static bool OrganizationNamesMatch(string rightsHolder, string organizationName)
        {
            return string.Equals(
                NormalizeOrganizationName(rightsHolder),
                NormalizeOrganizationName(organizationName),
                StringComparison.Ordinal
            );
        }

        private static string NormalizeOrganizationName(string value)
        {
            return Regex
                .Replace(value ?? string.Empty, "[^a-z0-9]+", string.Empty, RegexOptions.IgnoreCase)
                .ToLowerInvariant();
        }

        private static string ExtractRightsHolder(string copyrightNotice)
        {
            if (string.IsNullOrWhiteSpace(copyrightNotice))
                return null;

            var value = copyrightNotice.Trim();
            value = Regex.Replace(value, @"^copyright\s*", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"^(?:\(c\)|©)\s*", string.Empty, RegexOptions.IgnoreCase);

            var commaIndex = value.IndexOf(',');
            if (commaIndex >= 0)
            {
                var prefix = value.Substring(0, commaIndex);
                if (Regex.IsMatch(prefix, @"^[\d\s\-/]+$"))
                    value = value.Substring(commaIndex + 1).Trim();
            }

            value = Regex.Replace(
                value,
                @"^\d{4}(?:\s*[-/]\s*\d{4})*\s*",
                string.Empty,
                RegexOptions.IgnoreCase
            );
            value = Regex.Replace(value, @"^(?:by\s+)", string.Empty, RegexOptions.IgnoreCase);
            value = value.Trim(' ', ',', '.', ':', ';', '-', '/');

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private void EnsureWorkspaceFolders(RabWorkspacePaths paths)
        {
            Directory.CreateDirectory(paths.RabRoot);
            Directory.CreateDirectory(paths.BloomPubRoot);
            Directory.CreateDirectory(paths.BuildRoot);
            Directory.CreateDirectory(paths.ApkRoot);
            Directory.CreateDirectory(paths.KeystoreRoot);
            Directory.CreateDirectory(paths.ProjectAssetsRoot);
            Directory.CreateDirectory(paths.LauncherIconRoot);
        }

        internal virtual List<RabBookPublishInfo> ExportPrepareBooks(RabWorkspacePaths paths)
        {
            var state = LoadState(paths);
            if (state?.Books?.Count > 0)
                return ExportTrackedBooks(paths, state);

            var currentBook = _bookSelection.CurrentSelection;
            if (currentBook == null)
                throw new ApplicationException("Select a book before using Publish > Apps.");

            return ExportBookInfos(paths, new[] { currentBook.BookInfo });
        }

        internal virtual List<RabBookPublishInfo> ExportTrackedBooks(
            RabWorkspacePaths paths,
            RabPrepareState state
        )
        {
            if (state.Books == null || state.Books.Count == 0)
                return ExportPrepareBooks(paths);

            var bookInfos = new List<BookInfo>();
            var normalizedTrackedBooks = new List<RabBookPublishInfo>();
            foreach (var trackedBook in state.Books)
            {
                var matchingBook = FindTrackedBookInfo(
                    new RabTrackedBookInfo
                    {
                        BookId = trackedBook.BookId,
                        FolderPath = trackedBook.FolderPath,
                        Title = trackedBook.Title,
                    }
                );

                bookInfos.Add(matchingBook);
                normalizedTrackedBooks.Add(
                    new RabBookPublishInfo
                    {
                        BookId = string.IsNullOrWhiteSpace(trackedBook.BookId)
                            ? matchingBook.Id
                            : trackedBook.BookId,
                        FolderPath = matchingBook.FolderPath,
                        Title = matchingBook.Title,
                        BloomPubPath = trackedBook.BloomPubPath,
                        ThumbnailFileName = trackedBook.ThumbnailFileName,
                    }
                );
            }

            return ExportBookInfos(
                paths,
                bookInfos,
                normalizedTrackedBooks
                    .Where(book => !string.IsNullOrWhiteSpace(book.FolderPath))
                    .ToDictionary(book => book.FolderPath, StringComparer.OrdinalIgnoreCase)
            );
        }

        private List<RabBookPublishInfo> ExportBookInfos(
            RabWorkspacePaths paths,
            IEnumerable<BookInfo> bookInfos,
            IDictionary<string, RabBookPublishInfo> existingByFolder = null
        )
        {
            var exportedBooks = new List<RabBookPublishInfo>();
            var booksToExport = bookInfos.ToList();
            var bloomPubPathsToKeep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var bookInfo in booksToExport)
            {
                var existing =
                    existingByFolder != null
                    && existingByFolder.TryGetValue(bookInfo.FolderPath, out var found)
                        ? found
                        : null;
                var bloomPubPath = ResolveBloomPubPath(
                    paths.BloomPubRoot,
                    bookInfo.FolderName + BloomPubMaker.BloomPubExtensionWithDot,
                    existing?.BloomPubPath
                );

                if (
                    !string.IsNullOrWhiteSpace(bloomPubPath)
                    && string.Equals(
                        Path.GetDirectoryName(bloomPubPath),
                        paths.BloomPubRoot,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    bloomPubPathsToKeep.Add(bloomPubPath);
            }

            DeleteStaleBloomPubExports(paths.BloomPubRoot, bloomPubPathsToKeep);

            for (var index = 0; index < booksToExport.Count; index++)
            {
                var bookInfo = booksToExport[index];
                var book = _collectionModel.GetBookFromBookInfo(bookInfo);
                var existing =
                    existingByFolder != null
                    && existingByFolder.TryGetValue(bookInfo.FolderPath, out var found)
                        ? found
                        : null;
                var bookTitle = GetBookTitleForRab(book, bookInfo);
                var bloomPubPath = ResolveBloomPubPath(
                    paths.BloomPubRoot,
                    bookInfo.FolderName + BloomPubMaker.BloomPubExtensionWithDot,
                    existing?.BloomPubPath
                );

                if (RobustFile.Exists(bloomPubPath))
                {
                    _progress.MessageWithoutLocalizing(
                        $"Reusing existing BloomPUB for {bookTitle}..."
                    );
                }
                else
                {
                    _progress.MessageWithoutLocalizing($"Creating BloomPUB for {bookTitle}...");
                    var settings = BloomPubPublishSettings.GetPublishSettingsForBook(
                        _bookServer,
                        bookInfo
                    );
                    BloomPubMaker.CreateBloomPub(
                        settings,
                        bloomPubPath,
                        book,
                        _bookServer,
                        _progress
                    );
                }
                ReportBloomPubStageProgress(index + 1, booksToExport.Count);

                var bloomPubContent = ReadBloomPubContentInfo(bloomPubPath);

                exportedBooks.Add(
                    new RabBookPublishInfo()
                    {
                        BookId = bookInfo.Id,
                        FolderPath = bookInfo.FolderPath,
                        Title = bookTitle,
                        BloomPubPath = bloomPubPath,
                        ThumbnailFileName = bloomPubContent.ThumbnailFileName,
                        EmbeddedFonts = bloomPubContent.FontDefinitions,
                    }
                );
            }

            return exportedBooks;
        }

        internal static string ResolveBloomPubPath(
            string bloomPubRoot,
            string defaultFileName,
            string existingBloomPubPath
        )
        {
            var fileName = defaultFileName;
            if (!string.IsNullOrWhiteSpace(existingBloomPubPath))
            {
                var existingFileName = Path.GetFileName(existingBloomPubPath);
                if (!string.IsNullOrWhiteSpace(existingFileName))
                    fileName = existingFileName;

                var existingDirectory = Path.GetDirectoryName(existingBloomPubPath);
                if (
                    string.Equals(
                        existingDirectory,
                        bloomPubRoot,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                    return existingBloomPubPath;
            }

            return Path.Combine(bloomPubRoot, fileName);
        }

        private static void DeleteStaleBloomPubExports(
            string bloomPubRoot,
            ISet<string> bloomPubPathsToKeep
        )
        {
            if (!Directory.Exists(bloomPubRoot))
                return;

            foreach (
                var existingBloomPubPath in Directory.GetFiles(
                    bloomPubRoot,
                    "*" + BloomPubMaker.BloomPubExtensionWithDot,
                    SearchOption.TopDirectoryOnly
                )
            )
            {
                if (bloomPubPathsToKeep.Contains(existingBloomPubPath))
                    continue;

                RobustFile.Delete(existingBloomPubPath);
            }
        }

        private static void SynchronizeProjectFonts(
            string appDefPath,
            IEnumerable<RabBookPublishInfo> trackedBooks
        )
        {
            var project = RabAppProject.Load(appDefPath);
            project.SynchronizeFonts(ReadFontDefinitionsFromBloomPubs(trackedBooks));
            project.Save();
        }

        internal static List<RabAppFontDefinition> ReadFontDefinitionsFromBloomPub(
            string bloomPubPath
        )
        {
            return ReadBloomPubContentInfo(bloomPubPath, true).FontDefinitions;
        }

        internal static List<RabAppFontDefinition> ReadFontDefinitionsFromBloomPubs(
            IEnumerable<RabBookPublishInfo> trackedBooks
        )
        {
            return (trackedBooks ?? Array.Empty<RabBookPublishInfo>())
                .SelectMany(book =>
                    book?.EmbeddedFonts ?? ReadFontDefinitionsFromBloomPub(book?.BloomPubPath)
                )
                .GroupBy(
                    font =>
                        string.Join(
                            "\u001f",
                            font.DisplayName ?? string.Empty,
                            font.Weight ?? string.Empty,
                            font.Style ?? string.Empty,
                            font.FileName ?? string.Empty,
                            font.Format ?? string.Empty
                        ),
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(group => group.First())
                .ToList();
        }

        private static BloomPubContentInfo ReadBloomPubContentInfo(
            string bloomPubPath,
            bool includeFonts = true
        )
        {
            if (string.IsNullOrWhiteSpace(bloomPubPath) || !RobustFile.Exists(bloomPubPath))
                return new BloomPubContentInfo();

            using var archive = ZipFile.OpenRead(bloomPubPath);
            var fontsCss = string.Empty;

            if (includeFonts)
            {
                var fontsCssEntry = archive.GetEntry("fonts.css");
                if (fontsCssEntry != null)
                {
                    using var reader = new StreamReader(fontsCssEntry.Open());
                    fontsCss = reader.ReadToEnd();
                }
            }

            var thumbnailFileName = "thumbnail.jpg";
            if (
                archive.GetEntry("thumbnail.jpg") == null
                && archive.GetEntry("thumbnail.png") != null
            )
                thumbnailFileName = "thumbnail.png";

            return new BloomPubContentInfo
            {
                ThumbnailFileName = thumbnailFileName,
                FontDefinitions = string.IsNullOrWhiteSpace(fontsCss)
                    ? new List<RabAppFontDefinition>()
                    : Regex
                        .Matches(
                            fontsCss,
                            "@font-face\\s*\\{(?<body>[^}]*)\\}",
                            RegexOptions.IgnoreCase
                        )
                        .Cast<Match>()
                        .Select(match => CreateFontDefinitionFromCss(match.Groups["body"].Value))
                        .Where(font => font != null)
                        .ToList(),
            };
        }

        /// <summary>
        /// Reads the thumbnail entry directly from the BloomPUB so reused exports do not trust stale tracked-book metadata.
        /// Export paths share this lookup with embedded font parsing so each BloomPUB archive is opened only once per pass.
        /// </summary>
        internal static string GetBloomPubThumbnailFileName(string bloomPubPath)
        {
            return ReadBloomPubContentInfo(bloomPubPath, false).ThumbnailFileName;
        }

        private static RabAppFontDefinition CreateFontDefinitionFromCss(string cssBody)
        {
            var familyName = ReadCssProperty(cssBody, "font-family");
            var fileName = ReadCssUrlFileName(cssBody);
            if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(fileName))
                return null;

            var weight = ReadCssProperty(cssBody, "font-weight") ?? "normal";
            var style = ReadCssProperty(cssBody, "font-style") ?? "normal";
            var format = ReadCssFormat(cssBody) ?? Path.GetExtension(fileName).TrimStart('.');

            return new RabAppFontDefinition()
            {
                FamilyName = familyName,
                FontName = BuildFontName(familyName, weight, style),
                DisplayName = familyName,
                FileName = fileName,
                Format = format,
                Weight = weight,
                Style = style,
            };
        }

        private static string ReadCssProperty(string cssBody, string propertyName)
        {
            var match = Regex.Match(
                cssBody ?? string.Empty,
                $@"{Regex.Escape(propertyName)}\s*:\s*(?<value>[^;]+)",
                RegexOptions.IgnoreCase
            );
            return match.Success ? match.Groups["value"].Value.Trim().Trim('\'', '"') : null;
        }

        private static string ReadCssUrlFileName(string cssBody)
        {
            var match = Regex.Match(
                cssBody ?? string.Empty,
                @"url\((['""]?)(?<file>[^)'""]+)\1\)",
                RegexOptions.IgnoreCase
            );
            return match.Success ? Path.GetFileName(match.Groups["file"].Value.Trim()) : null;
        }

        private static string ReadCssFormat(string cssBody)
        {
            var match = Regex.Match(
                cssBody ?? string.Empty,
                @"format\((['""]?)(?<format>[^)'""]+)\1\)",
                RegexOptions.IgnoreCase
            );
            return match.Success ? match.Groups["format"].Value.Trim() : null;
        }

        private static string BuildFontName(string familyName, string weight, string style)
        {
            var parts = new List<string> { familyName };
            if (!string.Equals(weight, "normal", StringComparison.OrdinalIgnoreCase))
                parts.Add(
                    char.ToUpperInvariant(weight[0]) + weight.Substring(1).ToLowerInvariant()
                );

            if (!string.Equals(style, "normal", StringComparison.OrdinalIgnoreCase))
                parts.Add(char.ToUpperInvariant(style[0]) + style.Substring(1).ToLowerInvariant());

            return string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private class BloomPubContentInfo
        {
            public string ThumbnailFileName { get; set; } = "thumbnail.jpg";
            public List<RabAppFontDefinition> FontDefinitions { get; set; } =
                new List<RabAppFontDefinition>();
        }

        private void ReportProgressStage(string stage, int percent)
        {
            var clampedPercent = Math.Max(0, Math.Min(100, percent));
            _progress.SendStage(stage);
            _progress.SendPercent(clampedPercent);

            if (
                !string.Equals(stage, _lastLoggedProgressStage, StringComparison.Ordinal)
                || _lastLoggedProgressPercent != clampedPercent
            )
            {
                _progress.MessageWithoutLocalizing($"Progress: {stage} ({clampedPercent}%)");
                _lastLoggedProgressStage = stage;
                _lastLoggedProgressPercent = clampedPercent;
            }
        }

        private void ReportBloomPubStageProgress(int completedBooks, int totalBooks)
        {
            if (totalBooks <= 0 || string.IsNullOrWhiteSpace(_activeProgressAction))
                return;

            // Keep the prepare/build progress bar moving during per-book export work instead of jumping straight to the next stage.
            var startPercent = _activeProgressAction == "build" ? 10 : 10;
            var endPercent = _activeProgressAction == "build" ? 30 : 45;
            var percent =
                startPercent
                + (int)
                    Math.Round((endPercent - startPercent) * completedBooks / (double)totalBooks);

            ReportProgressStage("exporting-bloompubs", percent);
        }

        private static readonly (string Marker, int Percent)[] kBuildOutputMilestones =
        {
            ("*** Building Android app ***", 45),
            ("*** Setting paths ***", 47),
            ("*** JDK ***", 49),
            ("*** Android SDK ***", 51),
            ("*** Compiling Android APK ***", 55),
            ("BUILD SUCCESSFUL", 97),
        };

        private static readonly (string TaskName, int Percent)[] kGradleTaskMilestones =
        {
            ("mergeReleaseNativeLibs", 58),
            ("generateReleaseResources", 60),
            ("mergeReleaseResources", 63),
            ("compressReleaseAssets", 66),
            ("processReleaseResources", 68),
            ("compileReleaseJavaWithJavac", 70),
            ("minifyReleaseWithR8", 85),
            ("compileReleaseArtProfile", 94),
            ("packageRelease", 95),
            ("assembleRelease", 97),
        };

        internal void ReportProcessOutputLine(
            string line,
            ProgressKind kind = ProgressKind.Progress
        )
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            TryAdvanceBuildProgressFromOutput(line);
            lock (_rabOutputCaptureLock)
                _rabOutputCapture?.Add(line);
            _progress.MessageWithoutLocalizing(FormatTimestampedLogLine(line), kind);
        }

        private static string FormatTimestampedLogLine(string line)
        {
            return $"[{DateTime.Now:HH:mm:ss.fff}] {line}";
        }

        private void TryAdvanceBuildProgressFromOutput(string line)
        {
            if (_activeProgressAction != "build")
                return;

            var progressPercent = GetBuildProgressPercentFromOutput(line);
            if (!progressPercent.HasValue || progressPercent.Value <= _lastBuildProgressPercent)
                return;

            _lastBuildProgressPercent = progressPercent.Value;
            ReportProgressStage("building-android-app", progressPercent.Value);
        }

        internal static int? GetBuildProgressPercentFromOutput(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            if (TryGetGradleTaskName(line, out var taskName))
            {
                foreach (var milestone in kGradleTaskMilestones)
                {
                    if (string.Equals(taskName, milestone.TaskName, StringComparison.Ordinal))
                        return milestone.Percent;
                }
            }

            foreach (var milestone in kBuildOutputMilestones)
            {
                if (!line.Contains(milestone.Marker, StringComparison.Ordinal))
                    continue;

                return milestone.Percent;
            }

            return null;
        }

        private static bool TryGetGradleTaskName(string line, out string taskName)
        {
            taskName = null;
            var prefix = "> Task :";
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            var suffixStart = line.IndexOf(' ', prefix.Length);
            taskName =
                suffixStart >= 0
                    ? line.Substring(prefix.Length, suffixStart - prefix.Length)
                    : line.Substring(prefix.Length);
            return !string.IsNullOrWhiteSpace(taskName);
        }

        private string GetBookTitleForRab(global::Bloom.Book.Book book, BookInfo bookInfo)
        {
            var preferredTitle = book?.TitleBestForUserDisplay;
            if (!string.IsNullOrWhiteSpace(preferredTitle))
                return preferredTitle;

            preferredTitle = book?.NameBestForUserDisplay;
            if (!string.IsNullOrWhiteSpace(preferredTitle))
                return preferredTitle;

            if (!string.IsNullOrWhiteSpace(bookInfo.Title))
                return bookInfo.Title;

            return bookInfo.FolderName;
        }

        internal virtual RabProjectSupportFiles EnsureProjectSupportFiles(RabWorkspacePaths paths)
        {
            var settings = GetEffectiveAppSettings(paths);
            return new RabProjectSupportFiles()
            {
                AboutTextPath = EnsureAboutText(paths, settings),
                LauncherIconPaths = EnsureLauncherIcons(paths, settings),
            };
        }

        private string EnsureAboutText(RabWorkspacePaths paths, RabAppSettings settings)
        {
            var aboutText = settings?.About;
            if (string.IsNullOrWhiteSpace(aboutText))
                aboutText = BuildDefaultAboutText(settings);

            RobustFile.WriteAllText(paths.AboutTextPath, aboutText ?? string.Empty);
            return paths.AboutTextPath;
        }

        private string BuildDefaultAboutText(RabAppSettings settings)
        {
            var currentBook = _bookSelection?.CurrentSelection;

            var metadata = currentBook?.GetLicenseMetadata();
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(settings?.AppName))
                lines.Add(settings.AppName);

            var url = "https://bloomlibrary.org/language:" + _collectionSettings?.Language1Tag;

            lines.Add(
                $"Created with Bloom. Get more books in this language at [BloomLibrary.org]({url})"
            );

            return string.Join(
                Environment.NewLine + Environment.NewLine,
                lines.Where(line => !string.IsNullOrWhiteSpace(line))
            );
        }

        internal string[] EnsureLauncherIcons(RabWorkspacePaths paths, RabAppSettings settings)
        {
            var iconSourcePath = settings?.IconPath;

            if (!RobustFile.Exists(iconSourcePath))
                throw new ApplicationException(
                    $"Bloom could not find the Reading App Builder icon source: {iconSourcePath}"
                );

            var iconSizes = new[] { 36, 48, 72, 96, 144, 192, 512 };
            using (var iconImage = LoadIconImage(iconSourcePath))
            {
                return iconSizes
                    .Select(size =>
                    {
                        var outputPath = Path.Combine(
                            paths.LauncherIconRoot,
                            $"bloom-icon-{size}.png"
                        );
                        SaveResizedPng(iconImage, outputPath, size);
                        return outputPath;
                    })
                    .ToArray();
            }
        }

        /// <summary>
        /// Loads the chosen launcher-icon image, converting GDI+'s misleading failure for an image
        /// it cannot decode into a clear error that names the offending file (BL-16467).
        /// System.Drawing's Image.FromFile reports an undecodable image — a corrupt file, or one in
        /// an unsupported encoding such as a CMYK JPEG — by throwing OutOfMemoryException, which
        /// previously aborted the RAB build with a baffling "Out of memory" message that gave no
        /// hint of which file was at fault.
        /// </summary>
        private static Image LoadIconImage(string iconSourcePath)
        {
            try
            {
                // RobustImageIO rides out transient file-sharing hiccups while reading the file.
                return RobustImageIO.GetImageFromFile(iconSourcePath);
            }
            catch (Exception e) when (e is OutOfMemoryException || e is ArgumentException)
            {
                // GDI+ uses these two exception types to signal "I can't decode this image",
                // regardless of the actual reason. Re-throw with the path and size so the user and
                // our logs can see exactly which file failed. Any other exception (e.g. a genuine
                // I/O failure) is left to propagate unchanged.
                var sizeInBytes = RobustFile.Exists(iconSourcePath)
                    ? new FileInfo(iconSourcePath).Length
                    : 0L;
                throw new ApplicationException(
                    $"Bloom could not read the app icon image \"{iconSourcePath}\" ({sizeInBytes:N0} bytes). "
                        + "The file appears to be corrupt or in an image format Bloom cannot read.",
                    e
                );
            }
        }

        private void SaveResizedPng(Image sourceImage, string outputPath, int size)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(sourceImage, 0, 0, size, size);
                bitmap.Save(outputPath, ImageFormat.Png);
            }
        }

        internal virtual void EnsureKeystore(string keystorePath, string password)
        {
            if (string.IsNullOrWhiteSpace(keystorePath) || string.IsNullOrWhiteSpace(password))
                throw new ApplicationException(
                    "Bloom could not determine the Reading App Builder keystore path and password."
                );

            if (RobustFile.Exists(keystorePath))
            {
                if (IsKeystoreValid(keystorePath, password))
                {
                    _progress.MessageWithoutLocalizing(
                        $"Reusing existing keystore: {keystorePath}"
                    );
                    return;
                }

                _progress.MessageWithoutLocalizing(
                    $"Existing keystore does not match the saved password. Regenerating: {keystorePath}",
                    ProgressKind.Warning
                );
                RobustFile.Delete(keystorePath);
            }

            var keytoolPath = FindKeytoolPath();
            if (string.IsNullOrEmpty(keytoolPath))
                throw new ApplicationException(
                    "Bloom could not find the Reading App Builder runtime keytool."
                );

            var args = string.Join(
                " ",
                new[]
                {
                    "-genkeypair",
                    "-alias",
                    QuoteArgument(kDefaultAlias),
                    "-keyalg RSA",
                    "-keysize 2048",
                    "-validity 10000",
                    "-storetype PKCS12",
                    "-keystore",
                    QuoteArgument(keystorePath),
                    "-storepass",
                    QuoteArgument(password),
                    "-keypass",
                    QuoteArgument(password),
                    "-dname",
                    QuoteArgument("CN=Bloom RAB, OU=Bloom, O=SIL, L=Unknown, S=Unknown, C=US"),
                    "-noprompt",
                }
            );

            RunProcess(keytoolPath, args, Path.GetDirectoryName(keystorePath));
        }

        private bool IsKeystoreValid(string keystorePath, string password)
        {
            var keytoolPath = FindKeytoolPath();
            if (string.IsNullOrEmpty(keytoolPath))
                throw new ApplicationException(
                    "Bloom could not find the Reading App Builder runtime keytool."
                );

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo()
                {
                    FileName = keytoolPath,
                    Arguments = string.Join(
                        " ",
                        new[]
                        {
                            "-list",
                            "-alias",
                            QuoteArgument(kDefaultAlias),
                            "-keystore",
                            QuoteArgument(keystorePath),
                            "-storepass",
                            QuoteArgument(password),
                        }
                    ),
                    WorkingDirectory = Path.GetDirectoryName(keystorePath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                if (!process.Start())
                    throw new ApplicationException("Bloom could not start keytool.");

                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }

        private IReadOnlyList<string> BuildRabArgsForNewProject(
            RabWorkspacePaths paths,
            RabAppSettings settings,
            IEnumerable<RabBookPublishInfo> books,
            RabPrepareState state,
            RabProjectSupportFiles supportFiles
        )
        {
            var arguments = new List<string>
            {
                "-new",
                "-n",
                settings?.AppName ?? GetAppName(),
                "-p",
                settings?.PackageName ?? GetPackageName(),
                "-fp",
                $"app.def={paths.RabRoot}",
                "-fp",
                $"build={paths.BuildRoot}",
                "-fp",
                $"apk.output={paths.SafeApkRoot}",
            };

            AddProjectConfigurationArguments(arguments, state, supportFiles);

            foreach (var book in books)
            {
                arguments.Add("-b");
                arguments.Add(book.BloomPubPath);
            }

            return arguments;
        }

        private IReadOnlyList<string> BuildRabArgsForProjectUpdate(
            RabWorkspacePaths paths,
            RabPrepareState state,
            IEnumerable<RabBookPublishInfo> books,
            RabProjectSupportFiles supportFiles,
            bool buildProject
        )
        {
            var arguments = new List<string> { "-load", state.AppDefPath };
            arguments.Add("-fp");
            arguments.Add($"build={paths.BuildRoot}");
            arguments.Add("-fp");
            arguments.Add($"apk.output={paths.SafeApkRoot}");
            AddProjectConfigurationArguments(arguments, state, supportFiles);

            foreach (var book in books)
            {
                arguments.Add("-b");
                arguments.Add(book.BloomPubPath);
            }

            if (buildProject)
                arguments.Add("-build");

            return arguments;
        }

        private IReadOnlyList<string> BuildRabArgsForInstallingSdks()
        {
            return new[]
            {
                "-install-sdks-if-needed",
                "-jdk-install-folder",
                GetRabJdkInstallFolder(),
                "-android-sdk-install-folder",
                GetRabAndroidSdkInstallFolder(),
            };
        }

        private string RedactSensitiveProcessArguments(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
                return arguments;

            // Keep the process log readable without exposing signing credentials in progress output.
            var redactedArguments = arguments;

            foreach (var flag in new[] { "-ksp", "-kap", "-storepass", "-keypass" })
            {
                redactedArguments = Regex.Replace(
                    redactedArguments,
                    $@"{Regex.Escape(flag)}\s+(""(?:\\""|[^""])*""|\S+)",
                    $"{flag} \"***\""
                );
            }

            return redactedArguments;
        }

        private void AddProjectConfigurationArguments(
            List<string> arguments,
            RabPrepareState state,
            RabProjectSupportFiles supportFiles
        )
        {
            if (!string.IsNullOrWhiteSpace(supportFiles?.AboutTextPath))
            {
                arguments.Add("-a");
                arguments.Add(supportFiles.AboutTextPath);
            }

            foreach (var iconPath in supportFiles?.LauncherIconPaths ?? Array.Empty<string>())
            {
                arguments.Add("-ic");
                arguments.Add(iconPath);
            }

            if (!string.IsNullOrWhiteSpace(state?.KeystorePath))
            {
                arguments.Add("-ks");
                arguments.Add(state.KeystorePath);
            }

            if (!string.IsNullOrWhiteSpace(state?.KeystorePassword))
            {
                arguments.Add("-ksp");
                arguments.Add(state.KeystorePassword);
            }

            if (!string.IsNullOrWhiteSpace(state?.KeyAlias))
            {
                arguments.Add("-ka");
                arguments.Add(state.KeyAlias);
            }

            if (!string.IsNullOrWhiteSpace(state?.AliasPassword))
            {
                arguments.Add("-kap");
                arguments.Add(state.AliasPassword);
            }
        }

        internal virtual void RunRabCommand(
            IReadOnlyList<string> rabArguments,
            string workingDirectory
        )
        {
            var rabLauncher = FindRabLauncherPath();
            if (string.IsNullOrEmpty(rabLauncher))
                throw new ApplicationException(GetRabNotFoundMessage());

            var argumentFilePath = CreateRabArgumentFile(rabArguments);
            try
            {
                var command = $"\"{rabLauncher}\" -i {QuoteArgument(argumentFilePath)}";
                RunProcess(
                    "cmd.exe",
                    $"/d /c \"{command}\"",
                    workingDirectory,
                    GetRabProcessEnvironmentVariables()
                );
            }
            finally
            {
                try
                {
                    RobustFile.Delete(argumentFilePath);
                }
                catch (Exception) { }
            }
        }

        private string CreateRabArgumentFile(IReadOnlyList<string> rabArguments)
        {
            var argumentDirectory = GetRabArgumentFileDirectory();
            Directory.CreateDirectory(argumentDirectory);

            var argumentFilePath = Path.Combine(
                argumentDirectory,
                $"rab-args-{Guid.NewGuid():N}.txt"
            );

            var argumentFileContents = string.Join(
                Environment.NewLine,
                (rabArguments ?? Array.Empty<string>()).Select(QuoteArgument)
            );
            RobustFile.WriteAllText(argumentFilePath, argumentFileContents, new UTF8Encoding(true));
            return argumentFilePath;
        }

        internal virtual string GetRabArgumentFileDirectory()
        {
            return Path.Combine(GetPaths().SafeWorkRoot, "RabArgs");
        }

        internal virtual IReadOnlyDictionary<string, string> GetRabProcessEnvironmentVariables()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["APPDATA"] = GetRabAppDataFolder(),
                ["ANDROID_HOME"] = string.Empty,
                ["ANDROID_SDK_ROOT"] = string.Empty,
                ["JAVA_HOME"] = string.Empty,
                ["JDK_HOME"] = string.Empty,
                // Disable the Gradle daemon so no background JVM process lingers after the build.
                // A persistent Gradle daemon would hold file handles inside the collection's
                // "Bloom App Data/build" folder, preventing the collection from being renamed.
                ["GRADLE_OPTS"] = "-Dorg.gradle.daemon=false",
            };
        }

        internal virtual void RunProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables = null
        )
        {
            ReportProcessOutputLine(
                $"> {Path.GetFileName(fileName)} {RedactSensitiveProcessArguments(arguments)}"
            );

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo()
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                if (environmentVariables != null)
                {
                    foreach (var pair in environmentVariables)
                        process.StartInfo.EnvironmentVariables[pair.Key] =
                            pair.Value ?? string.Empty;
                }

                process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                        ReportProcessOutputLine(args.Data, ProgressKind.Progress);
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                        ReportProcessOutputLine(args.Data, ProgressKind.Warning);
                };

                if (!process.Start())
                    throw new ApplicationException($"Bloom could not start {fileName}.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new ApplicationException(
                        $"{Path.GetFileName(fileName)} exited with code {process.ExitCode}."
                    );
            }
        }

        internal virtual (int ExitCode, string Output) InstallApkOnDevice(
            string adbPath,
            string deviceSerial,
            string apkPath,
            string workingDirectory
        )
        {
            return RunProcessCapturingOutput(
                adbPath,
                $"-s {QuoteArgument(deviceSerial)} install -r {QuoteArgument(apkPath)}",
                workingDirectory
            );
        }

        internal virtual void UninstallAppFromDevice(
            string adbPath,
            string deviceSerial,
            string packageName,
            string workingDirectory
        )
        {
            RunProcess(
                adbPath,
                $"-s {QuoteArgument(deviceSerial)} uninstall {QuoteArgument(packageName)}",
                workingDirectory
            );
        }

        private (int ExitCode, string Output) RunProcessCapturingOutput(
            string fileName,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables = null
        )
        {
            ReportProcessOutputLine(
                $"> {Path.GetFileName(fileName)} {RedactSensitiveProcessArguments(arguments)}"
            );

            var outputLines = new List<string>();
            var syncRoot = new object();

            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo()
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                if (environmentVariables != null)
                {
                    foreach (var pair in environmentVariables)
                        process.StartInfo.EnvironmentVariables[pair.Key] =
                            pair.Value ?? string.Empty;
                }

                process.OutputDataReceived += (sender, args) =>
                {
                    if (string.IsNullOrWhiteSpace(args.Data))
                        return;

                    lock (syncRoot)
                        outputLines.Add(args.Data);

                    ReportProcessOutputLine(args.Data, ProgressKind.Progress);
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (string.IsNullOrWhiteSpace(args.Data))
                        return;

                    lock (syncRoot)
                        outputLines.Add(args.Data);

                    ReportProcessOutputLine(args.Data, ProgressKind.Warning);
                };

                if (!process.Start())
                    throw new ApplicationException($"Bloom could not start {fileName}.");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                return (process.ExitCode, string.Join(Environment.NewLine, outputLines));
            }
        }

        internal virtual void StartDetachedProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables = null
        )
        {
            _progress.MessageWithoutLocalizing($"> {Path.GetFileName(fileName)} {arguments}");

            using var process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = fileName,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };

            if (environmentVariables != null)
            {
                foreach (var pair in environmentVariables)
                    process.StartInfo.EnvironmentVariables[pair.Key] = pair.Value ?? string.Empty;
            }

            if (!process.Start())
                throw new ApplicationException($"Bloom could not start {fileName}.");
        }

        internal virtual bool IsRabInstalledForPrepare()
        {
            var installDir = GetRabRegistryValue("InstallDir");
            if (string.IsNullOrWhiteSpace(installDir))
                return false;

            return RobustFile.Exists(Path.Combine(installDir, "rab.bat"))
                && RobustFile.Exists(Path.Combine(installDir, "runtime", "bin", "keytool.exe"));
        }

        internal virtual string GetRabInstalledIconRoot()
        {
            var installDir = GetRabRegistryValue("InstallDir");
            if (string.IsNullOrWhiteSpace(installDir))
                return null;

            return Path.Combine(installDir, "images", "icons", "rab");
        }

        internal virtual string GetBundledIconRoot()
        {
            var applicationRoot = FileLocationUtilities.DirectoryOfApplicationOrSolution;
            var packagedRoot = Path.Combine(applicationRoot, "appbuilder-icons");
            if (Directory.Exists(packagedRoot))
                return packagedRoot;

            var sourceRoot = Path.Combine(applicationRoot, "DistFiles", "appbuilder-icons");
            return Directory.Exists(sourceRoot) ? sourceRoot : null;
        }

        private string GetDefaultBundledIconPath()
        {
            var bundledIconRoot = GetBundledIconRoot();
            if (string.IsNullOrWhiteSpace(bundledIconRoot))
                return string.Empty;

            var iconPath = Path.Combine(bundledIconRoot, kDefaultBundledIconId + ".png");
            return RobustFile.Exists(iconPath) ? iconPath : string.Empty;
        }

        internal virtual string FindRabSetupInstallerPath()
        {
            var searchDirectories = new[]
            {
                GetUserDownloadsDirectory(),
                GetRabInstallerDownloadDirectory(),
                Path.GetTempPath(),
            }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in searchDirectories)
            {
                if (!Directory.Exists(directory))
                    continue;

                var installerPath = Path.Combine(directory, kRabSetupInstallerFileName);
                if (RobustFile.Exists(installerPath))
                    return installerPath;
            }

            return null;
        }

        /// <summary>
        /// Downloads the Reading App Builder installer from the public BloomLibrary URL.
        /// Direct anonymous S3 access to the guessed bucket/key returns AccessDenied in production,
        /// so we must download from the real published URL instead of talking to S3 directly.
        /// </summary>
        internal virtual string DownloadRabSetupInstaller()
        {
            var installerPath = Path.Combine(
                GetRabInstallerDownloadDirectory(),
                kRabSetupInstallerFileName
            );
            DateTime? lastProgressLogUtc = null;

            Logger.WriteEvent(
                $"Downloading Reading App Builder installer from {kRabSetupDownloadUrl} to {installerPath}"
            );

            DownloadRabSetupInstallerFromUrl(
                installerPath,
                (transferredBytes, totalBytes) =>
                {
                    lastProgressLogUtc = MaybeLogRabInstallerDownloadProgress(
                        transferredBytes,
                        totalBytes,
                        lastProgressLogUtc
                    );
                }
            );

            Logger.WriteEvent(
                $"Finished downloading Reading App Builder installer to {installerPath}"
            );

            return installerPath;
        }

        internal virtual HttpClient CreateRabInstallerHttpClient()
        {
            return new HttpClient() { Timeout = kRabInstallerDownloadTimeout };
        }

        internal virtual void DownloadRabSetupInstallerFromUrl(
            string installerPath,
            Action<long, long> reportProgress
        )
        {
            using var httpClient = CreateRabInstallerHttpClient();
            using var response = httpClient
                .GetAsync(kRabSetupDownloadUrl, HttpCompletionOption.ResponseHeadersRead)
                .GetAwaiter()
                .GetResult();
            response.EnsureSuccessStatusCode();

            Directory.CreateDirectory(Path.GetDirectoryName(installerPath));

            using var responseStream = response
                .Content.ReadAsStreamAsync()
                .GetAwaiter()
                .GetResult();
            using var fileStream = RobustFile.Create(installerPath);

            CopyRabInstallerDownloadStream(
                responseStream,
                fileStream,
                response.Content.Headers.ContentLength ?? -1,
                reportProgress
            );
        }

        internal virtual void CopyRabInstallerDownloadStream(
            Stream responseStream,
            Stream fileStream,
            long totalBytes,
            Action<long, long> reportProgress
        )
        {
            var buffer = new byte[81920];
            long transferredBytes = 0;

            while (true)
            {
                var bytesRead = responseStream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                    break;

                fileStream.Write(buffer, 0, bytesRead);
                transferredBytes += bytesRead;
                reportProgress?.Invoke(transferredBytes, totalBytes);
            }
        }

        internal virtual DateTime GetUtcNow()
        {
            return DateTime.UtcNow;
        }

        internal DateTime? MaybeLogRabInstallerDownloadProgress(
            long transferredBytes,
            long totalBytes,
            DateTime? lastLoggedUtc
        )
        {
            var now = GetUtcNow();
            if (!ShouldLogRabInstallerDownloadProgress(now, lastLoggedUtc))
                return lastLoggedUtc;

            Logger.WriteEvent(
                FormatRabInstallerDownloadProgressMessage(transferredBytes, totalBytes)
            );
            return now;
        }

        internal static bool ShouldLogRabInstallerDownloadProgress(
            DateTime now,
            DateTime? lastLoggedUtc
        )
        {
            return !lastLoggedUtc.HasValue
                || now - lastLoggedUtc.Value >= kRabInstallerDownloadLogInterval;
        }

        internal static string FormatRabInstallerDownloadProgressMessage(
            long transferredBytes,
            long totalBytes
        )
        {
            var transferredText = FormatRabInstallerDownloadBytes(transferredBytes);
            if (totalBytes > 0)
            {
                return $"Downloading Reading App Builder installer: {transferredText} / {FormatRabInstallerDownloadBytes(totalBytes)}";
            }

            return $"Downloading Reading App Builder installer: {transferredText} received";
        }

        internal static string FormatRabInstallerDownloadBytes(long byteCount)
        {
            const double kilobyte = 1024d;
            const double megabyte = kilobyte * 1024d;

            if (byteCount >= megabyte)
                return $"{byteCount / megabyte:0.0} MB";

            if (byteCount >= kilobyte)
                return $"{byteCount / kilobyte:0.0} KB";

            return $"{byteCount} B";
        }

        internal virtual IReadOnlyList<string> GetRabRegistrySubKeys()
        {
            return new[] { kBloomRabRegistrySubKey, kRabRegistrySubKey };
        }

        internal static bool IsUserCanceledShellLaunch(Win32Exception error)
        {
            return error != null && error.NativeErrorCode == kUserCanceledShellLaunchErrorCode;
        }

        internal virtual void LaunchExternalTarget(string pathOrUrl)
        {
            if (IsExternalExecutablePath(pathOrUrl))
            {
                StartExternalExecutable(pathOrUrl);
                return;
            }

            ProcessExtra.SafeStartInFront(pathOrUrl);
        }

        internal static bool IsExternalExecutablePath(string pathOrUrl)
        {
            return !string.IsNullOrWhiteSpace(pathOrUrl)
                && !pathOrUrl.Contains("://")
                && string.Equals(
                    Path.GetExtension(pathOrUrl),
                    ".exe",
                    StringComparison.OrdinalIgnoreCase
                );
        }

        internal virtual void StartExternalExecutable(string executablePath)
        {
            using var process = StartShellProcess(
                new ProcessStartInfo()
                {
                    FileName = executablePath,
                    WorkingDirectory = Path.GetDirectoryName(executablePath),
                    UseShellExecute = true,
                    ErrorDialog = false,
                }
            );
        }

        internal virtual void InstallRabFromSetup(string installerPath)
        {
            var stagedInstallerPath = PrepareRabInstallerForLaunch(installerPath);
            var logPath = Path.Combine(Path.GetTempPath(), "rab-install.log");
            var arguments = BuildRabInstallerArguments(logPath);

            _progress.MessageWithoutLocalizing($"> {Path.GetFileName(installerPath)} {arguments}");

            using var process = StartShellProcess(
                new ProcessStartInfo()
                {
                    FileName = stagedInstallerPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(stagedInstallerPath),
                    UseShellExecute = true,
                    ErrorDialog = false,
                }
            );

            if (process == null)
                throw new ApplicationException(
                    $"Bloom could not start the installer at {installerPath}."
                );

            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new ApplicationException(
                    $"Reading App Builder installer exited with code {process.ExitCode}."
                );
        }

        /// <summary>
        /// Stages the downloaded installer into a Bloom-owned temp folder and removes Mark-of-the-Web
        /// metadata before shell launch, so Windows handles it like a local file instead of a browser download.
        /// </summary>
        internal virtual string PrepareRabInstallerForLaunch(string installerPath)
        {
            if (string.IsNullOrWhiteSpace(installerPath))
                throw new ArgumentException("Installer path is required.", nameof(installerPath));

            if (!RobustFile.Exists(installerPath))
                throw new FileNotFoundException(
                    $"Reading App Builder installer was not found at {installerPath}.",
                    installerPath
                );

            var stagingDirectory = GetRabInstallerStagingDirectory();
            Directory.CreateDirectory(stagingDirectory);

            var stagedInstallerPath = Path.Combine(
                stagingDirectory,
                Path.GetFileName(installerPath)
            );
            RobustFile.Copy(installerPath, stagedInstallerPath, true);
            RemoveZoneIdentifierFromFile(stagedInstallerPath);
            return stagedInstallerPath;
        }

        /// <summary>
        /// Returns the Bloom-owned temp folder used for staging the Reading App Builder installer before launch.
        /// </summary>
        internal virtual string GetRabInstallerStagingDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "Bloom", "ReadingAppBuilderInstaller");
        }

        internal virtual string GetRabInstallerDownloadDirectory()
        {
            return Path.Combine(Path.GetTempPath(), "Bloom", "ReadingAppBuilderDownloads");
        }

        internal virtual string GetUserDownloadsDirectory()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrWhiteSpace(userProfile)
                ? string.Empty
                : Path.Combine(userProfile, "Downloads");
        }

        /// <summary>
        /// Removes the browser-added Zone.Identifier alternate data stream when present.
        /// </summary>
        internal virtual void RemoveZoneIdentifierFromFile(string filePath)
        {
            // Browser downloads can carry a Zone.Identifier stream that makes Windows treat the
            // installer as internet-downloaded when we shell launch it.
            var zoneIdentifierPath = filePath + ":Zone.Identifier";
            try
            {
                RobustFile.Delete(zoneIdentifierPath);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            catch (NotSupportedException) { }
        }

        internal virtual Process StartShellProcess(ProcessStartInfo startInfo)
        {
            Process process = null;
            Exception launchError = null;

            if (Program.MainContext != null)
            {
                Program.MainContext.Send(
                    _ =>
                    {
                        try
                        {
                            process = Process.Start(startInfo);
                        }
                        catch (Exception error)
                        {
                            launchError = error;
                        }
                    },
                    null
                );
            }
            else
            {
                process = Process.Start(startInfo);
            }

            if (launchError != null)
                throw launchError;

            return process;
        }

        internal virtual string BuildRabInstallerArguments(string logPath)
        {
            var arguments = new List<string>
            {
                "/VERYSILENT",
                "/SUPPRESSMSGBOXES",
                "/NORESTART",
                "/SP-",
                $"/LANG={kRabSetupLanguage}",
                $"/LOG={QuoteArgument(logPath)}",
            };

            var installDir = GetDefaultRabInstallDir();
            if (!string.IsNullOrWhiteSpace(installDir))
                arguments.Add($"/DIR={QuoteArgument(installDir)}");

            return string.Join(" ", arguments);
        }

        internal virtual string GetRabJdkInstallFolder()
        {
            return Path.Combine(GetBloomOwnedRabToolchainRoot(), "jdk");
        }

        internal virtual string GetRabAndroidSdkInstallFolder()
        {
            return Path.Combine(GetBloomOwnedRabToolchainRoot(), "android-sdk");
        }

        internal virtual string GetBloomOwnedRabToolchainRoot()
        {
            return Path.Combine(
                Bloom.ProjectContext.GetBloomAppDataFolder(),
                kBloomOwnedRabToolchainFolderName
            );
        }

        internal virtual string GetRabAppDataFolder()
        {
            return Path.Combine(GetBloomOwnedRabToolchainRoot(), "appdata");
        }

        internal virtual bool TryBringRunningRabToFront()
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    if (process.ProcessName != "java")
                        continue; // Don't match browser tabs, for instance.

                    if (
                        process.MainWindowTitle.IndexOf(
                            "Reading App Builder",
                            StringComparison.OrdinalIgnoreCase
                        ) < 0
                    )
                        continue;

                    return ProcessExtra.SetForegroundWindow(process.MainWindowHandle);
                }
                catch
                {
                    // Ignore processes that cannot be inspected.
                }
                finally
                {
                    // close the process handle to avoid leaking resources, but don't kill the process
                    process.Dispose();
                }
            }

            return false;
        }

        internal virtual string GetRabSettingsFilePath()
        {
            return Path.Combine(
                GetRabAppDataFolder(),
                "SIL",
                "App Builder for Bloom",
                "rab-settings.xml"
            );
        }

        internal virtual void SeedRabSettingsToOpenProject(string appDefPath)
        {
            var settingsPath = GetRabSettingsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));

            var document = LoadRabSettingsDocument(settingsPath);

            var root = document.Root;
            var appsElement = root.Element("apps");
            if (appsElement == null)
            {
                appsElement = new XElement("apps");
                root.Add(appsElement);
            }

            appsElement.RemoveNodes();
            appsElement.Add(
                new XElement(
                    "app",
                    new XElement("name", Path.GetFileNameWithoutExtension(appDefPath)),
                    new XElement("filename", appDefPath)
                )
            );

            SaveRabSettingsDocument(settingsPath, document);
        }

        /// <summary>
        /// Loads Reading App Builder's shared settings file, recreating it if the existing XML is malformed.
        /// </summary>
        private XDocument LoadRabSettingsDocument(string settingsPath)
        {
            if (!RobustFile.Exists(settingsPath))
                return new XDocument(new XElement("settings"));

            try
            {
                var document = XDocument.Load(settingsPath, LoadOptions.PreserveWhitespace);
                if (document.Root == null)
                    document.Add(new XElement("settings"));

                return document;
            }
            catch (System.Xml.XmlException error)
            {
                Logger.WriteEvent(
                    $"Recreating corrupt Reading App Builder settings file at {settingsPath}: {error.Message}"
                );
                return new XDocument(new XElement("settings"));
            }
        }

        /// <summary>
        /// Writes Reading App Builder's shared settings file through a temp file so interrupted saves do not leave partial XML behind.
        /// </summary>
        private void SaveRabSettingsDocument(string settingsPath, XDocument document)
        {
            string tempFilePath;
            using (var temp = TempFile.InFolderOf(settingsPath))
            {
                tempFilePath = temp.Path;
            }

            document.Save(tempFilePath);
            if (RobustFile.Exists(settingsPath))
                RobustFile.Replace(tempFilePath, settingsPath, null);
            else
                RobustFile.Move(tempFilePath, settingsPath);
        }

        internal virtual RabPrepareStepStatus[] GetPrepareSteps(
            RabWorkspacePaths paths,
            RabPrepareState state,
            string appDefPath,
            bool rabInstalled
        )
        {
            var installerPath = FindRabSetupInstallerPath();
            var installerIsAvailable = !string.IsNullOrWhiteSpace(installerPath);
            var hasSigningKey = HasSigningKey(state);
            var hasProject =
                !string.IsNullOrWhiteSpace(appDefPath) && RobustFile.Exists(appDefPath);
            string installerCompleteTooltip = null;

            if (rabInstalled)
                installerCompleteTooltip =
                    "Reading App Builder is already installed, so you can skip downloading the installer."; // Reading App Builder is already installed, so this step is complete without needing an installer file.
            else if (installerIsAvailable)
                installerCompleteTooltip = $"Installer found at {installerPath}"; // Reading App Builder is not installed yet, but Bloom found an installer file it can run.

            return new[]
            {
                new RabPrepareStepStatus()
                {
                    Id = "installer-available",
                    Complete = rabInstalled || installerIsAvailable,
                    IncompleteTooltip =
                        "Download the Reading App Builder installer so Bloom can run it for you.",
                    CompleteTooltip = installerCompleteTooltip,
                },
                new RabPrepareStepStatus()
                {
                    Id = "rab-installed",
                    Complete = rabInstalled,
                    IncompleteTooltip =
                        "Run the Reading App Builder installer to install the app on this computer.",
                    CompleteTooltip = "Reading App Builder is installed and ready to use.",
                },
                new RabPrepareStepStatus()
                {
                    Id = "build-tools-installed",
                    Complete = AreRabBuildToolsInstalled(),
                    IncompleteTooltip =
                        "Install the Android SDK and JDK build tools that Reading App Builder needs.",
                    CompleteTooltip = "The Android SDK and JDK build tools are installed.",
                },
                new RabPrepareStepStatus()
                {
                    Id = "publisher-identity-created",
                    Complete = hasSigningKey,
                    IncompleteTooltip =
                        $"Create a keystore that is used to sign any app you create, from any collection on this computer. It is stored at {paths.SharedKeystorePath}",
                    CompleteTooltip = hasSigningKey
                        ? $"Your keystore is used to sign apps, and you'll need it to publish new versions of your app. It is stored at {paths.SharedKeystorePath}"
                        : $"Your keystore is used to sign apps, and you'll need it to publish new versions of your app. It is stored at {paths.SharedKeystorePath}",
                },
                new RabPrepareStepStatus()
                {
                    Id = "bloom-app-data-created",
                    Complete = hasProject,
                    IncompleteTooltip =
                        "Create a Reading App Builder project for this Bloom collection.",
                    CompleteTooltip =
                        "A Reading App Builder project already exists for this Bloom collection.",
                },
            };
        }

        internal virtual bool AreRabBuildToolsInstalled()
        {
            return IsRabJdkInstalled() && IsRabAndroidSdkInstalled();
        }

        internal virtual bool IsRabJdkInstalled()
        {
            return RobustFile.Exists(GetRabJavaExecutablePath())
                && RobustFile.Exists(Path.Combine(GetRabJdkRootPath(), "lib", "tzdb.dat"));
        }

        internal virtual bool IsRabAndroidSdkInstalled()
        {
            return RobustFile.Exists(
                Path.Combine(GetRabAndroidSdkInstallFolder(), "platform-tools", "adb.exe")
            );
        }

        internal virtual string GetRabJdkRootPath()
        {
            return Path.GetDirectoryName(Path.GetDirectoryName(GetRabJavaExecutablePath()));
        }

        internal virtual string GetRabJavaExecutablePath()
        {
            var directPath = Path.Combine(GetRabJdkInstallFolder(), "bin", "java.exe");
            if (RobustFile.Exists(directPath) || !Directory.Exists(GetRabJdkInstallFolder()))
                return directPath;

            foreach (var nestedDirectory in Directory.GetDirectories(GetRabJdkInstallFolder()))
            {
                var nestedPath = Path.Combine(nestedDirectory, "bin", "java.exe");
                if (RobustFile.Exists(nestedPath))
                    return nestedPath;
            }

            return directPath;
        }

        internal virtual bool HasSigningKey(RabPrepareState state)
        {
            return state != null
                && !string.IsNullOrWhiteSpace(state.KeystorePath)
                && RobustFile.Exists(state.KeystorePath)
                && !string.IsNullOrWhiteSpace(state.KeystorePassword)
                && !string.IsNullOrWhiteSpace(state.KeyAlias)
                && !string.IsNullOrWhiteSpace(state.AliasPassword);
        }

        private void PrepareProjectForTrackedBookImport(
            string appDefPath,
            IEnumerable<RabBookPublishInfo> books
        )
        {
            var project = RabAppProject.Load(appDefPath);
            project.ClearBookEntries();
            project.Save();
            project.DeleteGeneratedBookData();
        }

        private void ReconcileProjectWithImportedBooks(
            string appDefPath,
            IReadOnlyList<RabBookPublishInfo> trackedBooks
        )
        {
            var project = RabAppProject.Load(appDefPath);
            var generatedBookIds = GetGeneratedBookIds(
                Path.Combine(
                    Path.GetDirectoryName(appDefPath) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(appDefPath) + "_data",
                    "books"
                )
            );

            if (generatedBookIds.Count != trackedBooks.Count)
                throw new ApplicationException(
                    $"Reading App Builder imported {generatedBookIds.Count} books, but Bloom expected {trackedBooks.Count}."
                );

            project.SetBookEntries(
                generatedBookIds.Zip(trackedBooks, (bookId, book) => (bookId, book))
            );
            project.Save();
        }

        private static List<string> GetGeneratedBookIds(string booksRootPath)
        {
            if (!Directory.Exists(booksRootPath))
                return new List<string>();

            return Directory
                .GetDirectories(booksRootPath)
                .SelectMany(collectionPath =>
                    Directory.GetDirectories(collectionPath, "B*", SearchOption.TopDirectoryOnly)
                )
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private RabBookPublishInfo CreateTrackedBookPublishInfo(
            RabTrackedBookInfo trackedBook,
            IReadOnlyDictionary<string, RabBookPublishInfo> existingByFolder
        )
        {
            if (_collectionModel == null)
            {
                existingByFolder.TryGetValue(trackedBook.FolderPath, out var existingTrackedBook);
                return new RabBookPublishInfo()
                {
                    BookId = trackedBook.BookId,
                    FolderPath = trackedBook.FolderPath,
                    Title = trackedBook.Title,
                    BloomPubPath = existingTrackedBook?.BloomPubPath,
                    ThumbnailFileName = GetExistingThumbnailFileName(existingTrackedBook),
                };
            }

            var matchingBookInfo = FindTrackedBookInfo(trackedBook);
            existingByFolder.TryGetValue(matchingBookInfo.FolderPath, out var existing);

            return new RabBookPublishInfo()
            {
                BookId = matchingBookInfo.Id,
                FolderPath = matchingBookInfo.FolderPath,
                Title = matchingBookInfo.Title,
                BloomPubPath = existing?.BloomPubPath,
                ThumbnailFileName = GetExistingThumbnailFileName(existing),
            };
        }

        private static string GetExistingThumbnailFileName(RabBookPublishInfo book)
        {
            if (
                !string.IsNullOrWhiteSpace(book?.BloomPubPath)
                && RobustFile.Exists(book.BloomPubPath)
            )
                return GetBloomPubThumbnailFileName(book.BloomPubPath);

            return book?.ThumbnailFileName;
        }

        private BookInfo FindTrackedBookInfo(RabTrackedBookInfo trackedBook)
        {
            var bookInfos = _collectionModel.TheOneEditableCollection.GetBookInfos();
            var matchingBookInfo = !string.IsNullOrWhiteSpace(trackedBook.BookId)
                ? bookInfos.FirstOrDefault(info =>
                    string.Equals(info.Id, trackedBook.BookId, StringComparison.OrdinalIgnoreCase)
                )
                : null;

            if (matchingBookInfo == null && !string.IsNullOrWhiteSpace(trackedBook.FolderPath))
            {
                matchingBookInfo = bookInfos.FirstOrDefault(info =>
                    string.Equals(
                        info.FolderPath,
                        trackedBook.FolderPath,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                // These fallbacks may be helpful where things have changed since the last build,
                // especially when the collection has been renamed.
                if (matchingBookInfo == null)
                {
                    var trackedFolderName = Path.GetFileName(
                        trackedBook.FolderPath.TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar
                        )
                    );
                    if (!string.IsNullOrWhiteSpace(trackedFolderName))
                    {
                        var folderNameMatches = bookInfos
                            .Where(info =>
                                string.Equals(
                                    info.FolderName,
                                    trackedFolderName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            .ToList();
                        if (folderNameMatches.Count == 1)
                            matchingBookInfo = folderNameMatches[0];
                    }
                }
            }

            if (matchingBookInfo == null && !string.IsNullOrWhiteSpace(trackedBook.Title))
            {
                var titleMatches = bookInfos
                    .Where(info =>
                        string.Equals(
                            info.Title,
                            trackedBook.Title,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();
                if (titleMatches.Count == 1)
                    matchingBookInfo = titleMatches[0];
            }

            if (matchingBookInfo == null)
            {
                throw new ApplicationException(
                    $"Bloom could not find the selected book '{trackedBook.Title}' in this collection anymore."
                );
            }

            return matchingBookInfo;
        }

        private RabPrepareState LoadStateOrThrow(RabWorkspacePaths paths)
        {
            var state = EnsureStateHasProjectAndSigningInfo(paths, LoadState(paths));
            if (
                state == null
                || string.IsNullOrEmpty(state.AppDefPath)
                || !RobustFile.Exists(state.AppDefPath)
            )
                throw new ApplicationException(
                    "Use Prepare to create the Reading App Builder project before building or installing."
                );

            return state;
        }

        private RabPrepareState EnsureStateHasProjectAndSigningInfo(
            RabWorkspacePaths paths,
            RabPrepareState state,
            string appDefPath = null
        )
        {
            appDefPath = string.IsNullOrWhiteSpace(appDefPath) ? state?.AppDefPath : appDefPath;
            appDefPath = string.IsNullOrWhiteSpace(appDefPath) ? FindAppDefPath(paths) : appDefPath;
            // The stored path may be stale (e.g. after the collection folder was renamed).
            // Fall back to searching the current workspace so the project is still found.
            if (!string.IsNullOrWhiteSpace(appDefPath) && !RobustFile.Exists(appDefPath))
                appDefPath = FindAppDefPath(paths);
            if (string.IsNullOrWhiteSpace(appDefPath) || !RobustFile.Exists(appDefPath))
                return state;

            var project = RabAppProject.Load(appDefPath);
            state = state ?? new RabPrepareState();
            state.AppDefPath = appDefPath;
            state.ProjectName = string.IsNullOrWhiteSpace(state.ProjectName)
                ? Path.GetFileNameWithoutExtension(appDefPath)
                : state.ProjectName;
            state.KeystorePath = string.IsNullOrWhiteSpace(state.KeystorePath)
                ? project.KeystorePath
                : state.KeystorePath;
            state.KeystorePassword = string.IsNullOrWhiteSpace(state.KeystorePassword)
                ? project.KeystorePassword
                : state.KeystorePassword;
            state.KeyAlias = string.IsNullOrWhiteSpace(state.KeyAlias)
                ? (string.IsNullOrWhiteSpace(project.KeyAlias) ? kDefaultAlias : project.KeyAlias)
                : state.KeyAlias;
            state.AliasPassword = string.IsNullOrWhiteSpace(state.AliasPassword)
                ? project.AliasPassword
                : state.AliasPassword;
            return state;
        }

        private RabAppSettings GetEffectiveAppSettings(RabWorkspacePaths paths)
        {
            EnsureStateHasProjectAndSigningInfo(paths, LoadState(paths));
            var project = LoadProjectOrNull(paths);

            if (project != null)
            {
                var defaultSettings = GetDefaultAppSettings();
                var effectiveSettings = MergeSettings(
                    project.GetAppSettings(),
                    null,
                    defaultSettings
                );
                effectiveSettings.About = FirstNonEmpty(
                    ReadAboutText(paths),
                    effectiveSettings.About,
                    defaultSettings.About
                );
                return effectiveSettings;
            }

            return MergeSettings(null, null, GetDefaultAppSettings());
        }

        private void SynchronizeProjectIconFiles(
            RabWorkspacePaths paths,
            RabAppProject project,
            RabAppSettings settings
        )
        {
            var launcherIconPaths = EnsureLauncherIcons(paths, settings);
            var launcherImagesRoot = Path.Combine(
                Path.GetDirectoryName(project.FilePath) ?? string.Empty,
                Path.GetFileNameWithoutExtension(project.FilePath) + "_data",
                "images"
            );

            var launcherEntries = kLauncherIconFiles
                .Zip(
                    launcherIconPaths,
                    (launcherIcon, sourcePath) =>
                    {
                        var destinationPath = Path.Combine(
                            launcherImagesRoot,
                            launcherIcon.RelativePath.Replace('\\', Path.DirectorySeparatorChar)
                        );
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        RobustFile.Copy(sourcePath, destinationPath, true);
                        return (launcherIcon.RelativePath, launcherIcon.Size, launcherIcon.Size);
                    }
                )
                .ToArray();

            project.SetLauncherIcons(launcherEntries);

            var adaptiveForegroundSourcePath = settings?.IconPath;

            var adaptiveForegroundDestinationPath = Path.Combine(
                launcherImagesRoot,
                "mipmap-xxxhdpi",
                "ic_launcher_foreground.png"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(adaptiveForegroundDestinationPath));
            RobustFile.Copy(adaptiveForegroundSourcePath, adaptiveForegroundDestinationPath, true);
            project.SetAdaptiveIconForegroundImage("ic_launcher_foreground.png");
        }

        private RabAppSettings NormalizeSettingsForPersistence(
            RabWorkspacePaths paths,
            RabAppSettings settings
        )
        {
            var mergedSettings = NormalizeSettingsForComparison(settings);
            mergedSettings.AppName = mergedSettings.AppName?.Trim();
            mergedSettings.ColorScheme = mergedSettings.ColorScheme?.Trim();
            mergedSettings.PackageName = mergedSettings.PackageName?.Trim();
            mergedSettings.Copyright = mergedSettings.Copyright?.Trim();
            mergedSettings.About = mergedSettings.About?.Trim();
            mergedSettings.IconPath = NormalizeIconPath(paths, mergedSettings.IconPath?.Trim());
            return mergedSettings;
        }

        private RabAppSettings NormalizeSettingsForComparison(RabAppSettings settings)
        {
            var mergedSettings = MergeSettings(settings, null, GetDefaultAppSettings());
            mergedSettings.AppName = mergedSettings.AppName?.Trim();
            mergedSettings.ColorScheme = mergedSettings.ColorScheme?.Trim();
            mergedSettings.PackageName = mergedSettings.PackageName?.Trim();
            mergedSettings.Copyright = mergedSettings.Copyright?.Trim();
            mergedSettings.About = mergedSettings.About?.Trim();
            mergedSettings.IconPath = mergedSettings.IconPath?.Trim();
            return mergedSettings;
        }

        private RabAppSettings GetDefaultAppSettings()
        {
            var defaultSettings = new RabAppSettings()
            {
                AppName = GetAppName(),
                ColorScheme = RabAppProject.DefaultColorScheme,
                PackageName = GetPackageName(),
                IconPath = GetDefaultBundledIconPath(),
                Copyright = GetDefaultAppCopyright(),
            };

            defaultSettings.About = BuildDefaultAboutText(defaultSettings);
            return defaultSettings;
        }

        private string GetDefaultAppCopyright()
        {
            var currentBook = _bookSelection?.CurrentSelection;
            if (currentBook == null)
                return string.Empty;

            var metadata = currentBook.GetLicenseMetadata();
            if (!string.IsNullOrWhiteSpace(metadata?.CopyrightNotice))
                return metadata.CopyrightNotice;

            return currentBook.BookInfo?.Copyright ?? string.Empty;
        }

        private RabAppSettings MergeSettings(
            RabAppSettings preferredSettings,
            RabAppSettings fallbackSettings,
            RabAppSettings defaultSettings
        )
        {
            return new RabAppSettings()
            {
                AppName = FirstNonEmpty(
                    preferredSettings?.AppName,
                    fallbackSettings?.AppName,
                    defaultSettings?.AppName
                ),
                ColorScheme = FirstNonEmpty(
                    preferredSettings?.ColorScheme,
                    fallbackSettings?.ColorScheme,
                    defaultSettings?.ColorScheme
                ),
                PackageName = FirstNonEmpty(
                    preferredSettings?.PackageName,
                    fallbackSettings?.PackageName,
                    defaultSettings?.PackageName
                ),
                IconPath = FirstNonEmpty(
                    preferredSettings?.IconPath,
                    fallbackSettings?.IconPath,
                    defaultSettings?.IconPath
                ),
                Copyright = FirstNonEmpty(
                    preferredSettings?.Copyright,
                    fallbackSettings?.Copyright,
                    defaultSettings?.Copyright
                ),
                About = FirstNonEmpty(
                    preferredSettings?.About,
                    fallbackSettings?.About,
                    defaultSettings?.About
                ),
            };
        }

        private string ReadAboutText(RabWorkspacePaths paths)
        {
            if (!RobustFile.Exists(paths.AboutTextPath))
                return null;

            return RobustFile.ReadAllText(paths.AboutTextPath);
        }

        private string NormalizeIconPath(RabWorkspacePaths paths, string iconPath)
        {
            if (string.IsNullOrWhiteSpace(iconPath))
                return string.Empty;

            if (!RobustFile.Exists(iconPath))
                throw new ApplicationException(
                    $"Bloom could not find the selected app icon: {iconPath}"
                );

            var extension = Path.GetExtension(iconPath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".png";

            var copiedIconPath = Path.Combine(paths.ProjectAssetsRoot, "selected-icon" + extension);
            if (!string.Equals(iconPath, copiedIconPath, StringComparison.OrdinalIgnoreCase))
                RobustFile.Copy(iconPath, copiedIconPath, true);

            return copiedIconPath;
        }

        private IEnumerable<RabTrackedBookInfo> GetConfiguredTrackedBooks(RabWorkspacePaths paths)
        {
            var books = LoadState(paths)?.Books;
            if (books != null && books.Count > 0)
            {
                return books.Select(book => new RabTrackedBookInfo()
                {
                    BookId = book.BookId,
                    FolderPath = book.FolderPath,
                    Title = book.Title,
                });
            }

            var currentBook = _bookSelection?.CurrentSelection;
            if (currentBook == null)
                return Array.Empty<RabTrackedBookInfo>();

            return new[]
            {
                new RabTrackedBookInfo()
                {
                    BookId = currentBook.BookInfo.Id,
                    FolderPath = currentBook.BookInfo.FolderPath,
                    Title = GetBookTitleForRab(currentBook, currentBook.BookInfo),
                },
            };
        }

        internal virtual IEnumerable<RabTrackedBookInfo> GetCollectionBooksForSizeEstimates()
        {
            if (_collectionModel?.TheOneEditableCollection == null)
                return Array.Empty<RabTrackedBookInfo>();

            return _collectionModel
                .TheOneEditableCollection.GetBookInfos()
                .Select(book => new RabTrackedBookInfo()
                {
                    BookId = book.Id,
                    FolderPath = book.FolderPath,
                    Title = string.IsNullOrWhiteSpace(book.Title) ? book.FolderName : book.Title,
                });
        }

        private static long GetFolderSizeBytes(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return 0;

            long totalBytes = 0;

            try
            {
                foreach (
                    var filePath in Directory.EnumerateFiles(
                        folderPath,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                {
                    try
                    {
                        totalBytes += new FileInfo(filePath).Length;
                    }
                    catch (Exception error)
                        when (error is IOException
                            || error is UnauthorizedAccessException
                            || error is NotSupportedException
                        ) { }
                }
            }
            catch (Exception error)
                when (error is IOException
                    || error is UnauthorizedAccessException
                    || error is NotSupportedException
                ) { }

            return totalBytes;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? string.Empty;
        }

        private string ComputeBuildInputSignature(
            RabAppSettings settings,
            IEnumerable<RabTrackedBookInfo> trackedBooks
        )
        {
            // Compare normalized logical inputs rather than timestamps so Build Needed reflects real user-visible changes.
            var normalizedSettings = NormalizeSettingsForComparison(settings);
            var orderedBooks = (trackedBooks ?? Enumerable.Empty<RabTrackedBookInfo>())
                .Select(book => new JObject
                {
                    ["bookId"] = book.BookId ?? string.Empty,
                    ["folderPath"] = book.FolderPath ?? string.Empty,
                    ["title"] = book.Title ?? string.Empty,
                })
                .ToArray();

            return new JObject
            {
                ["settings"] = JObject.FromObject(normalizedSettings),
                ["books"] = new JArray(orderedBooks),
            }.ToString(Formatting.None);
        }

        private string ComputeBuildInputSignature(
            RabAppSettings settings,
            IEnumerable<RabBookPublishInfo> trackedBooks
        )
        {
            // Build Needed intentionally tracks only explicit settings and tracked-book selection/order/title.
            // Thumbnail file names can also go stale, but we avoid special-casing that for now because other
            // book-content changes inside the BloomPUB are already outside this signature.
            return ComputeBuildInputSignature(
                settings,
                (trackedBooks ?? Enumerable.Empty<RabBookPublishInfo>()).Select(
                    book => new RabTrackedBookInfo
                    {
                        BookId = book.BookId,
                        FolderPath = book.FolderPath,
                        Title = book.Title,
                    }
                )
            );
        }

        private RabPrepareState LoadState(RabWorkspacePaths paths)
        {
            if (!RobustFile.Exists(paths.PrepareStatePath))
                return null;

            return JsonConvert.DeserializeObject<RabPrepareState>(
                RobustFile.ReadAllText(paths.PrepareStatePath)
            );
        }

        private void SaveState(RabWorkspacePaths paths, RabPrepareState state)
        {
            RobustFile.WriteAllText(
                paths.PrepareStatePath,
                JsonConvert.SerializeObject(state, Formatting.Indented)
            );
        }

        private RabSharedSigningState LoadBloomOwnedSigningState(RabWorkspacePaths paths)
        {
            if (!RobustFile.Exists(paths.SharedSigningStatePath))
                return null;

            return JsonConvert.DeserializeObject<RabSharedSigningState>(
                RobustFile.ReadAllText(paths.SharedSigningStatePath)
            );
        }

        private void SaveBloomOwnedSigningState(
            RabWorkspacePaths paths,
            RabSharedSigningState state
        )
        {
            Directory.CreateDirectory(paths.KeystoreRoot);
            RobustFile.WriteAllText(
                paths.SharedSigningStatePath,
                JsonConvert.SerializeObject(state, Formatting.Indented)
            );
        }

        private RabPrepareState CreatePrepareState(RabSharedSigningState signingState)
        {
            return new RabPrepareState
            {
                KeystorePath = signingState.KeystorePath,
                KeystorePassword = signingState.KeystorePassword,
                KeyAlias = signingState.KeyAlias,
                AliasPassword = signingState.AliasPassword,
            };
        }

        private RabSharedSigningState CreateBloomOwnedSigningState(RabWorkspacePaths paths)
        {
            var keystorePassword = GeneratePassword();
            EnsureKeystore(paths.SharedKeystorePath, keystorePassword);

            var signingState = new RabSharedSigningState
            {
                KeystorePath = paths.SharedKeystorePath,
                KeystorePassword = keystorePassword,
                KeyAlias = kDefaultAlias,
                AliasPassword = keystorePassword,
            };

            SaveBloomOwnedSigningState(paths, signingState);
            return signingState;
        }

        private RabSharedSigningState EnsureBloomOwnedSigningState(RabWorkspacePaths paths)
        {
            var signingState = LoadBloomOwnedSigningState(paths);
            if (signingState != null)
            {
                if (!RobustFile.Exists(signingState.KeystorePath))
                    throw new ApplicationException(
                        "Bloom could not find the shared App Builder signing key. Delete the prepared project and prepare again."
                    );

                return signingState;
            }

            return CreateBloomOwnedSigningState(paths);
        }

        private RabPrepareState ApplyBloomOwnedSigningState(
            RabPrepareState state,
            RabSharedSigningState signingState
        )
        {
            state = state ?? new RabPrepareState();
            state.KeystorePath = signingState.KeystorePath;
            state.KeystorePassword = signingState.KeystorePassword;
            state.KeyAlias = signingState.KeyAlias;
            state.AliasPassword = signingState.AliasPassword;
            return state;
        }

        internal virtual string FindAppDefPath(RabWorkspacePaths paths)
        {
            if (!Directory.Exists(paths.RabRoot))
                return null;

            return Directory
                .GetFiles(paths.RabRoot, "*.appDef", SearchOption.AllDirectories)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
        }

        internal virtual string FindLatestApkPath(RabWorkspacePaths paths)
        {
            var searchRoots = new[]
            {
                paths.ApkRoot,
                paths.SafeApkRoot,
                paths.BuildRoot,
                paths.RabRoot,
            }
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (!searchRoots.Any())
                return null;

            return searchRoots
                .SelectMany(root => Directory.GetFiles(root, "*.apk", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(RobustFile.GetLastWriteTime)
                .FirstOrDefault();
        }

        internal virtual string FindRabLauncherPath()
        {
            return FindRabRuntimePath("rab.bat");
        }

        private string FindKeytoolPath()
        {
            return FindRabRuntimePath(Path.Combine("runtime", "bin", "keytool.exe"));
        }

        private string FindRabRuntimePath(string relativePath)
        {
            var installDirCandidates = new[]
            {
                GetRabRegistryValue("InstallDir"),
                GetDefaultRabInstallDir(),
            }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var installDir in installDirCandidates)
            {
                var candidate = Path.Combine(installDir, relativePath);
                if (RobustFile.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        internal virtual string GetDefaultRabInstallDir()
        {
            var nativeProgramFiles = GetNativeProgramFilesDirectory();
            if (string.IsNullOrWhiteSpace(nativeProgramFiles))
                return null;

            return Path.Combine(
                nativeProgramFiles,
                kRabInstallFolderParentName,
                kBloomRabInstallFolderName
            );
        }

        internal virtual string GetNativeProgramFilesDirectory()
        {
            var nativeProgramFiles = Environment.GetEnvironmentVariable("ProgramW6432");
            if (!string.IsNullOrWhiteSpace(nativeProgramFiles))
                return nativeProgramFiles;

            return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        }

        internal virtual string GetRabRegistryValue(string valueName)
        {
            try
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    foreach (var subKeyPath in GetRabRegistrySubKeys())
                    {
                        using var rabKey = baseKey.OpenSubKey(subKeyPath);
                        var value = rabKey?.GetValue(valueName) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }
            catch (Exception error)
                when (error is IOException
                    || error is SecurityException
                    || error is UnauthorizedAccessException
                    || error is PlatformNotSupportedException
                ) { }

            return null;
        }

        private string GetRabNotFoundMessage()
        {
            var version = GetRabRegistryValue("Version");
            var defaultInstallDir = GetDefaultRabInstallDir();
            var installLocation = string.IsNullOrWhiteSpace(defaultInstallDir)
                ? "the expected install location"
                : defaultInstallDir;
            if (string.IsNullOrWhiteSpace(version))
                return $"Bloom could not find Reading App Builder at {installLocation}.";

            return $"Bloom could not find Reading App Builder at {installLocation} (registry reports version {version}).";
        }

        internal virtual string FindAdbPath()
        {
            return RabAdbHelper.ResolveAdbPath();
        }

        internal static string ResolveAdbPath(
            IReadOnlyDictionary<string, string> environmentVariables,
            Func<string, bool> fileExists = null
        )
        {
            // Keep these wrappers for existing tests and callers that historically targeted RabProjectService.
            return RabAdbHelper.ResolveAdbPath(environmentVariables, fileExists);
        }

        private RabAppProject LoadProjectOrNull(RabWorkspacePaths paths)
        {
            var appDefPath = FindAppDefPath(paths);
            if (string.IsNullOrWhiteSpace(appDefPath) || !RobustFile.Exists(appDefPath))
                return null;

            return RabAppProject.Load(appDefPath);
        }

        internal virtual RabAdbConnectedDevice GetSingleConnectedDevice(string adbPath)
        {
            return RabAdbHelper.GetSingleConnectedDevice(adbPath, GetPaths().RabRoot, _progress);
        }

        private static bool IsUpdateIncompatibleInstallFailure(string output)
        {
            return !string.IsNullOrWhiteSpace(output)
                && output.IndexOf(
                    "INSTALL_FAILED_UPDATE_INCOMPATIBLE",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        internal static IReadOnlyList<string> ParseConnectedDeviceSerials(string adbDevicesOutput)
        {
            return RabAdbHelper.ParseConnectedDeviceSerials(adbDevicesOutput);
        }

        internal static IReadOnlyList<string> ParseConnectedDeviceDisplayNames(
            string adbDevicesOutput
        )
        {
            return RabAdbHelper.ParseConnectedDeviceDisplayNames(adbDevicesOutput);
        }

        internal static string BuildLaunchAppArguments(string deviceSerial, string packageName)
        {
            return RabAdbHelper.BuildLaunchAppArguments(deviceSerial, packageName);
        }

        private string GeneratePassword()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 20);
        }

        private static string QuoteArgument(string value)
        {
            if (value == null)
                return "\"\"";

            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
