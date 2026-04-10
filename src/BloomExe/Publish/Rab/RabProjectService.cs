using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security;
using System.Text.RegularExpressions;
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
    public class RabProjectService
    {
        private const string kDefaultAlias = "bloomkey";
        private const string kWebSocketContext = RabPublishApi.kWebSocketContext;
        private const string kBloomOwnedRabToolchainFolderName = "ReadingAppBuilder";
        private const string kDefaultRabInstallDir =
            @"C:\Program Files (x86)\SIL\Reading App Builder";
        private const string kRabRegistrySubKey = @"Software\SIL\Reading App Builder";
        private const string kBloomRabRegistrySubKey =
            @"Software\SIL\Reading App Builder for Bloom";
        private const int kUserCanceledShellLaunchErrorCode = 1223;
        private const string kRabSetupInstallerPrefix = "Reading-App-Builder-14.0";
        private const string kRabSetupInstallerSuffix = "-Setup.exe";
        private const string kRabSetupDownloadUrl =
            "https://drive.google.com/file/d/1LjWaGg1IMeB9Y8aK5It2RDmKmFNnrL3l/view?usp=drive_link";
        private const string kRabSetupLanguage = "en";
        private const int kRabLaunchPollIntervalMs = 250;
        private const int kRabLaunchTimeoutMs = 60000;
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
        private string _activeProgressAction;

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

        public Task SetupAsync()
        {
            Setup();
            return Task.CompletedTask;
        }

        public Task BuildAsync()
        {
            Build();
            return Task.CompletedTask;
        }

        public Task InstallAsync()
        {
            Install();
            return Task.CompletedTask;
        }

        public RabAppSettings GetAppSettings()
        {
            var paths = GetPaths();
            return GetEffectiveAppSettings(paths);
        }

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

        public void SaveAppSettings(RabAppSettings settings)
        {
            var paths = GetPaths();
            EnsureWorkspaceFolders(paths);

            var state = EnsureStateHasProjectAndSigningInfo(
                paths,
                LoadState(paths) ?? new RabSetupState()
            );
            if (string.IsNullOrWhiteSpace(state.AppDefPath) || !RobustFile.Exists(state.AppDefPath))
                throw new ApplicationException(
                    "Run Setup before customizing the Reading App Builder project."
                );

            var normalizedSettings = NormalizeSettingsForPersistence(paths, settings);

            var project = RabAppProject.Load(state.AppDefPath);
            project.SetAppSettings(normalizedSettings);
            EnsureAboutText(paths, normalizedSettings);
            SynchronizeProjectIconFiles(paths, project, normalizedSettings);
            project.Save();
            SaveState(paths, state);
        }

        public void SaveTrackedBooks(IReadOnlyCollection<RabTrackedBookInfo> trackedBooks)
        {
            if (trackedBooks == null || trackedBooks.Count == 0)
                throw new ApplicationException("Choose at least one book for the app.");

            var paths = GetPaths();
            EnsureWorkspaceFolders(paths);

            var state = EnsureStateHasProjectAndSigningInfo(
                paths,
                LoadState(paths) ?? new RabSetupState()
            );
            var existingByFolder = (state.Books ?? new List<RabBookPublishInfo>())
                .Where(book => !string.IsNullOrWhiteSpace(book.FolderPath))
                .ToDictionary(book => book.FolderPath, StringComparer.OrdinalIgnoreCase);

            state.Books = trackedBooks
                .Select(trackedBook => CreateTrackedBookPublishInfo(trackedBook, existingByFolder))
                .ToList();
            SaveState(paths, state);
        }

        public void OpenInRab()
        {
            OpenInRabInternal();
        }

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
                    "Run Setup before opening the app in Reading App Builder."
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

        public RabProjectStatus GetStatus()
        {
            var paths = GetPaths();
            var appDefPath = FindAppDefPath(paths);
            var latestApk = FindLatestApkPath(paths);
            var apkExists = !string.IsNullOrEmpty(latestApk) && RobustFile.Exists(latestApk);
            var rabInstalled = IsRabInstalledForSetup();
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

            var status = new RabProjectStatus()
            {
                RabInstalled = rabInstalled,
                ProjectExists = !string.IsNullOrEmpty(appDefPath) && RobustFile.Exists(appDefPath),
                ApkExists = apkExists,
                BuildNeeded = buildNeeded,
                AppDefPath = appDefPath,
                ApkPath = latestApk,
                ApkSizeBytes = apkExists ? new FileInfo(latestApk).Length : 0,
                RabRoot = paths.RabRoot,
                TrackedBooks = trackedBooks,
                TrackedBookTitles = trackedBooks.Select(book => book.Title).ToArray(),
                PrepareSteps = prepareSteps,
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

        private void Setup()
        {
            var paths = GetPaths();
            _activeProgressAction = "setup";
            try
            {
                ReportProgressStage("checking-installer", 0);
                if (!EnsureRabInstalledForSetup())
                    return;

                // Setup makes the on-disk RAB workspace match Bloom's current settings and tracked-book list,
                // creating the project only on the first run and refreshing inputs after that.
                EnsureWorkspaceFolders(paths);
                EnsureRabBuildPrerequisites(paths);

                ReportProgressStage("preparing-workspace", 0);
                _progress.MessageWithoutLocalizing(
                    "Preparing the Reading App Builder workspace...",
                    ProgressKind.Heading
                );

                ReportProgressStage("exporting-bloompubs", 10);
                var trackedBooks = ExportSetupBooks(paths);
                var effectiveSettings = GetEffectiveAppSettings(paths);
                var supportFiles = EnsureProjectSupportFiles(paths);
                var existingProjectPath = FindAppDefPath(paths);
                RabSetupState state;
                var createdNewProject = string.IsNullOrEmpty(existingProjectPath);

                if (createdNewProject)
                {
                    ReportProgressStage("generating-signing-key", 55);
                    _progress.MessageWithoutLocalizing(
                        "Generating a signing key for this collection..."
                    );
                    var keystorePassword = GeneratePassword();
                    var keystorePath = Path.Combine(
                        paths.KeystoreRoot,
                        GetProjectSlug() + ".keystore"
                    );
                    EnsureKeystore(keystorePath, keystorePassword);

                    state = new RabSetupState()
                    {
                        KeystorePath = keystorePath,
                        KeystorePassword = keystorePassword,
                        KeyAlias = kDefaultAlias,
                        AliasPassword = keystorePassword,
                    };

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
                        LoadState(paths) ?? new RabSetupState(),
                        existingProjectPath
                    );
                }

                state = EnsureStateHasProjectAndSigningInfo(paths, state, existingProjectPath);
                EnsureKeystore(state.KeystorePath, state.KeystorePassword);
                state.Books = trackedBooks;

                if (!createdNewProject)
                {
                    PrepareProjectForTrackedBookImport(existingProjectPath, trackedBooks);
                    ReportProgressStage("updating-project", 85);
                    RunRabCommand(
                        BuildRabArgsForProjectUpdate(
                            paths,
                            state,
                            trackedBooks,
                            supportFiles,
                            false
                        ),
                        paths.RabRoot
                    );
                }

                ReconcileProjectWithImportedBooks(existingProjectPath, trackedBooks);
                ReportProgressStage("updating-project", 85);
                SaveState(paths, state);

                ReportProgressStage("complete", 100);
                _progress.MessageWithoutLocalizing("Setup complete.", ProgressKind.Heading);
                _progress.MessageWithoutLocalizing($"Project file: {existingProjectPath}");
            }
            finally
            {
                _activeProgressAction = null;
            }
        }

        private bool EnsureRabInstalledForSetup()
        {
            ReportProgressStage("checking-installer", 0);

            if (IsRabInstalledForSetup())
                return true;

            var installerPath = FindRabSetupInstallerPath();
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
                        $"Reading App Builder installer did not start. Windows reported: {error.Message}",
                        ProgressKind.Warning
                    );
                    _progress.MessageWithoutLocalizing($"Installer: {installerPath}");
                    return false;
                }
                if (!IsRabInstalledForSetup())
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
                "Reading App Builder is not installed at the registry install path. Opening the download page...",
                ProgressKind.Heading
            );
            LaunchExternalTarget(kRabSetupDownloadUrl);
            _progress.MessageWithoutLocalizing($"Download: {kRabSetupDownloadUrl}");
            return false;
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
            // Build always refreshes BloomPUB inputs before invoking RAB so the APK matches the current collection state.
            var paths = GetPaths();
            EnsureWorkspaceFolders(paths);
            _activeProgressAction = "build";
            try
            {
                ReportProgressStage("preparing-build", 0);
                var state = LoadStateOrThrow(paths);
                EnsureKeystore(state.KeystorePath, state.KeystorePassword);
                EnsureRabBuildPrerequisites(paths);
                ReportProgressStage("exporting-bloompubs", 10);
                var trackedBooks = ExportTrackedBooks(paths, state);
                var supportFiles = EnsureProjectSupportFiles(paths);

                ReportProgressStage("updating-project", 65);
                _progress.MessageWithoutLocalizing(
                    "Updating the Reading App Builder project with fresh BloomPUB files..."
                );
                PrepareProjectForTrackedBookImport(state.AppDefPath, trackedBooks);
                RunRabCommand(
                    BuildRabArgsForProjectUpdate(paths, state, trackedBooks, supportFiles, false),
                    paths.RabRoot
                );
                ReconcileProjectWithImportedBooks(state.AppDefPath, trackedBooks);
                state.Books = trackedBooks;
                SaveState(paths, state);

                ReportProgressStage("building-android-app", 80);
                _progress.MessageWithoutLocalizing(
                    "Building the Android app with Reading App Builder...",
                    ProgressKind.Heading
                );
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

                ReportProgressStage("finalizing-apk", 95);
                var apkPath = FindLatestApkPath(paths);
                if (string.IsNullOrEmpty(apkPath))
                {
                    var searchRoots = new[] { paths.ApkRoot, paths.BuildRoot, paths.RabRoot }
                        .Where(Directory.Exists)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var apkCandidates = searchRoots
                        .SelectMany(root =>
                            Directory.GetFiles(root, "*.apk", SearchOption.AllDirectories)
                        )
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    throw new ApplicationException(
                        "Reading App Builder finished without producing an APK in the collection's app configuration folder. "
                            + $"Searched roots: {string.Join(", ", searchRoots)}. "
                            + $"APK candidates found: {string.Join(", ", apkCandidates)}"
                    );
                }

                ReportProgressStage("complete", 100);
                _progress.MessageWithoutLocalizing("Build complete.", ProgressKind.Heading);
                _progress.MessageWithoutLocalizing($"APK: {apkPath}");

                state.LastBuiltInputSignature = ComputeBuildInputSignature(
                    GetEffectiveAppSettings(paths),
                    trackedBooks
                );
                SaveState(paths, state);
            }
            finally
            {
                _activeProgressAction = null;
            }
        }

        private void Install()
        {
            // Install re-reads package/app metadata from the project so launching uses the same identity that was built.
            var paths = GetPaths();
            _activeProgressAction = "install";
            try
            {
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
                RunProcess(
                    adbPath,
                    $"-s {QuoteArgument(device.Serial)} install -r {QuoteArgument(apkPath)}",
                    paths.RabRoot
                );

                ReportProgressStage("launching-on-phone", 85);
                _progress.MessageWithoutLocalizing(
                    $"Launching {appName} on {device.DisplayName}...",
                    ProgressKind.Heading
                );
                RunProcess(
                    adbPath,
                    BuildLaunchAppArguments(device.Serial, packageName),
                    paths.RabRoot
                );
                ReportProgressStage("complete", 100);
                _progress.MessageWithoutLocalizing(
                    $"Install complete. Opened {appName} on {device.DisplayName}.",
                    ProgressKind.Heading
                );
            }
            finally
            {
                _activeProgressAction = null;
            }
        }

        internal virtual RabWorkspacePaths GetPaths()
        {
            var collectionRoot = _collectionModel.TheOneEditableCollection.PathToDirectory;
            var paths = new RabWorkspacePaths(collectionRoot);
            MigrateLegacyRabFolder(collectionRoot, paths.RabRoot);
            return paths;
        }

        private void MigrateLegacyRabFolder(string collectionRoot, string currentRabRoot)
        {
            var legacyRabRoot = Path.Combine(collectionRoot, "rab");
            if (!Directory.Exists(legacyRabRoot) || Directory.Exists(currentRabRoot))
                return;

            RobustIO.MoveDirectory(legacyRabRoot, currentRabRoot);
        }

        internal virtual string GetProjectSlug()
        {
            var baseName = string.IsNullOrWhiteSpace(_collectionSettings.CollectionName)
                ? Path.GetFileName(_collectionModel.TheOneEditableCollection.PathToDirectory)
                : _collectionSettings.CollectionName;
            return MakeProjectSlug(baseName);
        }

        internal virtual string GetAppName()
        {
            return string.IsNullOrWhiteSpace(_collectionSettings.CollectionName)
                ? Path.GetFileName(_collectionModel.TheOneEditableCollection.PathToDirectory)
                : _collectionSettings.CollectionName;
        }

        internal virtual string GetPackageName()
        {
            return MakeDefaultPackageName(GetProjectSlug(), _collectionSettings?.Language1Tag);
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

        internal virtual List<RabBookPublishInfo> ExportSetupBooks(RabWorkspacePaths paths)
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
            RabSetupState state
        )
        {
            if (state.Books == null || state.Books.Count == 0)
                return ExportSetupBooks(paths);

            var bookInfos = new List<BookInfo>();
            foreach (var trackedBook in state.Books)
            {
                var matchingBook = _collectionModel
                    .TheOneEditableCollection.GetBookInfos()
                    .FirstOrDefault(info =>
                        string.Equals(
                            info.FolderPath,
                            trackedBook.FolderPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
                if (matchingBook == null)
                    throw new ApplicationException(
                        $"Bloom could not find the tracked book '{trackedBook.Title}' in this collection anymore."
                    );

                bookInfos.Add(matchingBook);
            }

            return ExportBookInfos(
                paths,
                bookInfos,
                state.Books.ToDictionary(book => book.FolderPath, StringComparer.OrdinalIgnoreCase)
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
                var bloomPubPath =
                    existing?.BloomPubPath
                    ?? Path.Combine(
                        paths.BloomPubRoot,
                        bookInfo.FolderName + BloomPubMaker.BloomPubExtensionWithDot
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
                var bloomPubPath =
                    existing?.BloomPubPath
                    ?? Path.Combine(
                        paths.BloomPubRoot,
                        bookInfo.FolderName + BloomPubMaker.BloomPubExtensionWithDot
                    );

                _progress.MessageWithoutLocalizing($"Creating BloomPUB for {bookTitle}...");
                var settings = BloomPubPublishSettings.GetPublishSettingsForBook(
                    _bookServer,
                    bookInfo
                );
                BloomPubMaker.CreateBloomPub(settings, bloomPubPath, book, _bookServer, _progress);
                ReportBloomPubStageProgress(index + 1, booksToExport.Count);

                exportedBooks.Add(
                    new RabBookPublishInfo()
                    {
                        BookId = bookInfo.Id,
                        FolderPath = bookInfo.FolderPath,
                        Title = bookTitle,
                        BloomPubPath = bloomPubPath,
                    }
                );
            }

            return exportedBooks;
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

        private void ReportProgressStage(string stage, int percent)
        {
            _progress.SendStage(stage);
            _progress.SendPercent(Math.Max(0, Math.Min(100, percent)));
        }

        private void ReportBloomPubStageProgress(int completedBooks, int totalBooks)
        {
            if (totalBooks <= 0 || string.IsNullOrWhiteSpace(_activeProgressAction))
                return;

            // Keep the setup/build progress bar moving during per-book export work instead of jumping straight to the next stage.
            var startPercent = _activeProgressAction == "build" ? 10 : 10;
            var endPercent = _activeProgressAction == "build" ? 55 : 45;
            var percent =
                startPercent
                + (int)
                    Math.Round((endPercent - startPercent) * completedBooks / (double)totalBooks);

            ReportProgressStage("exporting-bloompubs", percent);
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
            var currentBook = _bookSelection.CurrentSelection;

            var metadata = currentBook?.GetLicenseMetadata();
            var lines = new List<string>();
            var copyrightNotice = settings?.Copyright;
            if (string.IsNullOrWhiteSpace(copyrightNotice))
                copyrightNotice = metadata?.CopyrightNotice;
            if (string.IsNullOrWhiteSpace(copyrightNotice))
                copyrightNotice = currentBook?.BookInfo?.Copyright;

            if (!string.IsNullOrWhiteSpace(settings?.AppName))
                lines.Add(settings.AppName);
            if (!string.IsNullOrWhiteSpace(copyrightNotice))
                lines.Add(copyrightNotice);
            if (!string.IsNullOrWhiteSpace(metadata?.License?.RightsStatement))
                lines.Add(metadata.License.RightsStatement);
            lines.Add("Created with Bloom.");

            return string.Join(
                Environment.NewLine + Environment.NewLine,
                lines.Where(line => !string.IsNullOrWhiteSpace(line))
            );
        }

        private string[] EnsureLauncherIcons(RabWorkspacePaths paths, RabAppSettings settings)
        {
            var iconSourcePath = settings?.IconPath;

            if (string.IsNullOrWhiteSpace(iconSourcePath))
            {
                iconSourcePath = Path.Combine(
                    BloomFileLocator.GetBrandingFolder("shared"),
                    "bloom-icon.png"
                );
            }

            if (!RobustFile.Exists(iconSourcePath))
                throw new ApplicationException(
                    $"Bloom could not find the Reading App Builder icon source: {iconSourcePath}"
                );

            var iconSizes = new[] { 36, 48, 72, 96, 144, 192, 512 };
            using (var iconBitmap = (Bitmap)Image.FromFile(iconSourcePath))
            {
                return iconSizes
                    .Select(size =>
                    {
                        var outputPath = Path.Combine(
                            paths.LauncherIconRoot,
                            $"bloom-icon-{size}.png"
                        );
                        SaveResizedPng(iconBitmap, outputPath, size);
                        return outputPath;
                    })
                    .ToArray();
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

        private string BuildRabArgsForNewProject(
            RabWorkspacePaths paths,
            RabAppSettings settings,
            IEnumerable<RabBookPublishInfo> books,
            RabSetupState state,
            RabProjectSupportFiles supportFiles
        )
        {
            var arguments = new List<string>
            {
                "-new",
                "-n",
                QuoteArgument(settings?.AppName ?? GetAppName()),
                "-p",
                QuoteArgument(settings?.PackageName ?? GetPackageName()),
                "-fp",
                QuoteArgument($"app.def={paths.RabRoot}"),
                "-fp",
                QuoteArgument($"build={paths.BuildRoot}"),
                "-fp",
                QuoteArgument($"apk.output={paths.ApkRoot}"),
            };

            AddProjectConfigurationArguments(arguments, state, supportFiles);

            foreach (var book in books)
            {
                arguments.Add("-b");
                arguments.Add(QuoteArgument(book.BloomPubPath));
            }

            return string.Join(" ", arguments);
        }

        private string BuildRabArgsForProjectUpdate(
            RabWorkspacePaths paths,
            RabSetupState state,
            IEnumerable<RabBookPublishInfo> books,
            RabProjectSupportFiles supportFiles,
            bool buildProject
        )
        {
            var arguments = new List<string> { "-load", QuoteArgument(state.AppDefPath) };
            arguments.Add("-fp");
            arguments.Add(QuoteArgument($"build={paths.BuildRoot}"));
            arguments.Add("-fp");
            arguments.Add(QuoteArgument($"apk.output={paths.ApkRoot}"));
            AddProjectConfigurationArguments(arguments, state, supportFiles);

            foreach (var book in books)
            {
                arguments.Add("-b");
                arguments.Add(QuoteArgument(book.BloomPubPath));
            }

            if (buildProject)
                arguments.Add("-build");

            return string.Join(" ", arguments);
        }

        private string BuildRabArgsForInstallingSdks()
        {
            return string.Join(
                " ",
                new[]
                {
                    "-install-sdks-if-needed",
                    "-jdk-install-folder",
                    QuoteArgument(GetRabJdkInstallFolder()),
                    "-android-sdk-install-folder",
                    QuoteArgument(GetRabAndroidSdkInstallFolder()),
                }
            );
        }

        private void AddProjectConfigurationArguments(
            List<string> arguments,
            RabSetupState state,
            RabProjectSupportFiles supportFiles
        )
        {
            if (!string.IsNullOrWhiteSpace(supportFiles?.AboutTextPath))
            {
                arguments.Add("-a");
                arguments.Add(QuoteArgument(supportFiles.AboutTextPath));
            }

            foreach (var iconPath in supportFiles?.LauncherIconPaths ?? Array.Empty<string>())
            {
                arguments.Add("-ic");
                arguments.Add(QuoteArgument(iconPath));
            }

            if (!string.IsNullOrWhiteSpace(state?.KeystorePath))
            {
                arguments.Add("-ks");
                arguments.Add(QuoteArgument(state.KeystorePath));
            }

            if (!string.IsNullOrWhiteSpace(state?.KeystorePassword))
            {
                arguments.Add("-ksp");
                arguments.Add(QuoteArgument(state.KeystorePassword));
            }

            if (!string.IsNullOrWhiteSpace(state?.KeyAlias))
            {
                arguments.Add("-ka");
                arguments.Add(QuoteArgument(state.KeyAlias));
            }

            if (!string.IsNullOrWhiteSpace(state?.AliasPassword))
            {
                arguments.Add("-kap");
                arguments.Add(QuoteArgument(state.AliasPassword));
            }
        }

        internal virtual void RunRabCommand(string rabArguments, string workingDirectory)
        {
            var rabLauncher = FindRabLauncherPath();
            if (string.IsNullOrEmpty(rabLauncher))
                throw new ApplicationException(GetRabNotFoundMessage());

            var command = $"\"{rabLauncher}\" {rabArguments}";
            RunProcess(
                "cmd.exe",
                $"/d /c \"{command}\"",
                workingDirectory,
                GetRabProcessEnvironmentVariables()
            );
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
            };
        }

        private void RunProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables = null
        )
        {
            _progress.MessageWithoutLocalizing($"> {Path.GetFileName(fileName)} {arguments}");

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
                        _progress.MessageWithoutLocalizing(args.Data);
                };
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                        _progress.MessageWithoutLocalizing(args.Data, ProgressKind.Warning);
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

        internal virtual void StartDetachedProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environmentVariables = null
        )
        {
            _progress.MessageWithoutLocalizing($"> {Path.GetFileName(fileName)} {arguments}");

            var process = new Process()
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

        internal virtual bool IsRabInstalledForSetup()
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

        internal virtual string FindRabSetupInstallerPath()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var searchDirectories = new[]
            {
                string.IsNullOrWhiteSpace(userProfile)
                    ? null
                    : Path.Combine(userProfile, "Downloads"),
                Path.GetTempPath(),
            }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var directory in searchDirectories)
            {
                if (!Directory.Exists(directory))
                    continue;

                var installerPath = Directory
                    .GetFiles(
                        directory,
                        $"{kRabSetupInstallerPrefix}*.exe",
                        SearchOption.TopDirectoryOnly
                    )
                    .Where(path => IsRabSetupInstallerFileName(Path.GetFileName(path)))
                    .OrderBy(path => path.Length)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(installerPath))
                    return installerPath;
            }

            return null;
        }

        internal virtual IReadOnlyList<string> GetRabRegistrySubKeys()
        {
            return new[] { kRabRegistrySubKey, kBloomRabRegistrySubKey };
        }

        internal static bool IsRabSetupInstallerFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            return fileName.StartsWith(kRabSetupInstallerPrefix, StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(kRabSetupInstallerSuffix, StringComparison.OrdinalIgnoreCase);
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
            var logPath = Path.Combine(Path.GetTempPath(), "rab-install.log");
            var arguments = BuildRabInstallerArguments(logPath);

            _progress.MessageWithoutLocalizing($"> {Path.GetFileName(installerPath)} {arguments}");

            using var process = StartShellProcess(
                new ProcessStartInfo()
                {
                    FileName = installerPath,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetDirectoryName(installerPath),
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
            return string.Join(
                " ",
                new[]
                {
                    "/VERYSILENT",
                    "/SUPPRESSMSGBOXES",
                    "/NORESTART",
                    "/SP-",
                    $"/LANG={kRabSetupLanguage}",
                    $"/LOG={QuoteArgument(logPath)}",
                }
            );
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
            }

            return false;
        }

        internal virtual string GetRabSettingsFilePath()
        {
            return Path.Combine(GetRabAppDataFolder(), "SIL", "App Builder", "rab-settings.xml");
        }

        internal virtual void SeedRabSettingsToOpenProject(string appDefPath)
        {
            var settingsPath = GetRabSettingsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));

            XDocument document;
            if (RobustFile.Exists(settingsPath))
            {
                document = XDocument.Load(settingsPath, LoadOptions.PreserveWhitespace);
                if (document.Root == null)
                    document.Add(new XElement("settings"));
            }
            else
            {
                document = new XDocument(new XElement("settings"));
            }

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

            document.Save(settingsPath);
        }

        internal virtual RabPrepareStepStatus[] GetPrepareSteps(
            RabWorkspacePaths paths,
            RabSetupState state,
            string appDefPath,
            bool rabInstalled
        )
        {
            var installerPath = FindRabSetupInstallerPath();

            return new[]
            {
                new RabPrepareStepStatus()
                {
                    Id = "installer-available",
                    Complete = rabInstalled || !string.IsNullOrWhiteSpace(installerPath),
                },
                new RabPrepareStepStatus() { Id = "rab-installed", Complete = rabInstalled },
                new RabPrepareStepStatus()
                {
                    Id = "build-tools-installed",
                    Complete = AreRabBuildToolsInstalled(),
                },
                new RabPrepareStepStatus()
                {
                    Id = "project-created",
                    Complete =
                        HasSigningKey(state)
                        && !string.IsNullOrWhiteSpace(appDefPath)
                        && RobustFile.Exists(appDefPath),
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

        internal virtual bool HasSigningKey(RabSetupState state)
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
            };
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
            }

            if (matchingBookInfo == null)
            {
                throw new ApplicationException(
                    $"Bloom could not find the selected book '{trackedBook.Title}' in this collection anymore."
                );
            }

            return matchingBookInfo;
        }

        private RabSetupState LoadStateOrThrow(RabWorkspacePaths paths)
        {
            var state = EnsureStateHasProjectAndSigningInfo(paths, LoadState(paths));
            if (
                state == null
                || string.IsNullOrEmpty(state.AppDefPath)
                || !RobustFile.Exists(state.AppDefPath)
            )
                throw new ApplicationException(
                    "Use Setup to create the Reading App Builder project before building or installing."
                );

            return state;
        }

        private RabSetupState EnsureStateHasProjectAndSigningInfo(
            RabWorkspacePaths paths,
            RabSetupState state,
            string appDefPath = null
        )
        {
            appDefPath = string.IsNullOrWhiteSpace(appDefPath) ? state?.AppDefPath : appDefPath;
            appDefPath = string.IsNullOrWhiteSpace(appDefPath) ? FindAppDefPath(paths) : appDefPath;
            if (string.IsNullOrWhiteSpace(appDefPath) || !RobustFile.Exists(appDefPath))
                return state;

            var project = RabAppProject.Load(appDefPath);
            state = state ?? new RabSetupState();
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
            var mergedSettings = MergeSettings(settings, null, GetDefaultAppSettings());
            mergedSettings.AppName = mergedSettings.AppName?.Trim();
            mergedSettings.ColorScheme = mergedSettings.ColorScheme?.Trim();
            mergedSettings.PackageName = mergedSettings.PackageName?.Trim();
            mergedSettings.Copyright = mergedSettings.Copyright?.Trim();
            mergedSettings.About = mergedSettings.About?.Trim();
            mergedSettings.IconPath = NormalizeIconPath(paths, mergedSettings.IconPath?.Trim());
            return mergedSettings;
        }

        private RabAppSettings GetDefaultAppSettings()
        {
            var defaultSettings = new RabAppSettings()
            {
                AppName = GetAppName(),
                ColorScheme = RabAppProject.DefaultColorScheme,
                PackageName = GetPackageName(),
                IconPath = string.Empty,
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
            var normalizedSettings = NormalizeSettingsForPersistence(GetPaths(), settings);
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

        private RabSetupState LoadState(RabWorkspacePaths paths)
        {
            if (!RobustFile.Exists(paths.SetupStatePath))
                return null;

            return JsonConvert.DeserializeObject<RabSetupState>(
                RobustFile.ReadAllText(paths.SetupStatePath)
            );
        }

        private void SaveState(RabWorkspacePaths paths, RabSetupState state)
        {
            RobustFile.WriteAllText(
                paths.SetupStatePath,
                JsonConvert.SerializeObject(state, Formatting.Indented)
            );
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
            var searchRoots = new[] { paths.ApkRoot, paths.BuildRoot, paths.RabRoot }
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
            return kDefaultRabInstallDir;
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
            if (string.IsNullOrWhiteSpace(version))
                return $"Bloom could not find Reading App Builder at {GetDefaultRabInstallDir()}.";

            return $"Bloom could not find Reading App Builder at {GetDefaultRabInstallDir()} (registry reports version {version}).";
        }

        private string FindAdbPath()
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

        private RabAdbConnectedDevice GetSingleConnectedDevice(string adbPath)
        {
            return RabAdbHelper.GetSingleConnectedDevice(adbPath, GetPaths().RabRoot, _progress);
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
