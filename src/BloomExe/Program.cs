using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Configuration;
using Bloom.Collection;
using Bloom.Collection.BloomPack;
using Bloom.Properties;
using Bloom.Registration;
using Bloom.ToPalaso;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.Miscellaneous;
using SIL.Windows.Forms.Registration;
using SIL.Windows.Forms.Reporting;
using SIL.Windows.Forms.UniqueToken;
using System.Linq;
using System.Threading.Tasks;
using Bloom.CLI;
using Bloom.CollectionChoosing;
using Bloom.ErrorReporter;
using Bloom.TeamCollection;
using Bloom.MiscUI;
using Bloom.web;
using CommandLine;
using Sentry;
using SIL.WritingSystems;
using System.Text;
using Bloom.Utils;
using Bloom.web.controllers;
using Bloom.SafeXml;

namespace Bloom
{
    static class Win32Imports
    {
        // Use DllImport to import the Win32 MessageBox function.  This is used for a fatal crash message on Windows.
        [System.Runtime.InteropServices.DllImport(
            "user32.dll",
            CharSet = System.Runtime.InteropServices.CharSet.Unicode
        )]
        public static extern int MessageBox(IntPtr hWnd, String text, String caption, uint type);
    }

    static class Program
    {
        private const string _mutexId = "bloom";

        //static HttpListener listener = new HttpListener();

        /// <summary>
        /// We have one project open at a time, and this helps us bootstrap the project and
        /// properly dispose of various things when the project is closed.
        /// </summary>
        private static ProjectContext _projectContext;
        private static int _uiThreadId;
        private static ApplicationContainer _applicationContainer;
        public static bool StartUpWithFirstOrNewVersionBehavior;

        static string _originalPreload; // saves LD_PRELOAD environment variable for restarting Bloom
#if PerProjectMutex
        private static Mutex _oneInstancePerProjectMutex;
#else
        // Some splash screen management variables were moved from here to StartupScreenManager.
        // Not sure what should be done about them if we ever turn on PerProjectMutex.
#endif
        internal static string PathToBookDownloadedAtStartup { get; set; }

        private static bool _supressRegistrationDialog = false;

#if !__MonoCS__
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int pid);
#endif

        /// <summary>
        /// The UI language of the system when the program starts
        /// </summary>
        internal static CultureInfo UserInterfaceCulture = CultureInfo.CurrentUICulture;

        public static bool RunningOnUiThread => Thread.CurrentThread.ManagedThreadId == _uiThreadId;

        // Background threads creating thumbnails need to be canceled if the project window
        // closes down, which shuts down the BloomServer.
        // See https://silbloom.myjetbrains.com/youtrack/issue/BL-6214.
        public static CancellationTokenSource BloomThreadCancelService;

        public static SynchronizationContext MainContext { get; private set; }

        public static bool RunningSecondInstance { get; private set; }

        [STAThread]
        [HandleProcessCorruptedStateExceptions]
        static int Main(string[] args1)
        {
            // AttachConsole(-1);	// Enable this to allow Console.Out.WriteLine to be viewable (must run Bloom from terminal, AFAIK)
            bool gotUniqueToken = false;
            _uiThreadId = Thread.CurrentThread.ManagedThreadId;
            Logger.Init();
            // Configure TempFile to create temp files with a "bloom" prefix so we can
            // catch stuff we make that doesn't get cleaned up properly, including in our
            // final call to CleanupTempFolder. Also prevents our temp files competing with
            // other programs for 64K available default temp file names.
            TempFile.NamePrefix = "bloom";
            CheckForCorruptUserConfig();
            // We want to do this as early as possible, definitely before we create
            // the TeamCollectionManager, which needs the registered user information.
            // But since it's difficult to predict what else might want it, it's best
            // to do it before we create the Autofac context objects that create all
            // our singletons.
            RegistrationDialog.UpgradeRegistrationIfNeeded();
            // We use crowdin for localizing, and they require a directory per language setup.
            LocalizationManager.UseLanguageCodeFolders = true;
            // We want only good localizations in Bloom.
            // REVIEW: should the setting be used only for alpha and beta?
            LocalizationManager.ReturnOnlyApprovedStrings = !Settings
                .Default
                .ShowUnapprovedLocalizations;
            // We want to do this very early. It needs to happen before anything tries to get localized strings.
            // For example, it should be before we try to get the unique token (BL-13268).
            // Another goal is for it to happen before this method breaks off into various paths, so that
            // every startup path calls it.
            SetUpLocalization();

            // Old comment: Firefox60 uses Gtk3, so we need to as well.  (BL-10469)
            // Aug 2023, we've moved away from GeckoFx/Firefox to wv2, but I don't know if this is still needed or not...
            // Steve says he thinks Gtk3 is still better but that it likely only matters for Linux,
            // which for now is not supported in 5.5+.
            GraphicsManager.GtkVersionInUse = GraphicsManager.GtkVersion.Gtk3;

#if DEBUG
            //MessageBox.Show("Attach debugger now");
#endif
            // Bloom has several command line scenarios, without a coherent system for them.
            // The following is how we will do things from now on, and things can be moved
            // into this as time allows. See CommandLineOptions.cs.
            if (
                args1.Length > 0
                && new[]
                {
                    "--help",
                    "hydrate",
                    "upload",
                    "download",
                    "getfonts",
                    "changeLayout",
                    "createArtifacts",
                    "spreadsheetExport",
                    "spreadsheetImport",
                    "sendFontAnalytics"
                }.Contains(args1[0])
            ) //restrict using the commandline parser to cases were it should work
            {
#if !__MonoCS__
                AttachConsole(-1);
#endif

                RunningInConsoleMode = true;

                var mainTask = CommandLine.Parser.Default
                    .ParseArguments(
                        args1,
                        new[]
                        {
                            typeof(HydrateParameters),
                            typeof(UploadParameters),
                            typeof(DownloadBookOptions),
                            typeof(GetUsedFontsParameters),
                            typeof(ChangeLayoutParameters),
                            typeof(CreateArtifactsParameters),
                            typeof(SendFontAnalyticsParameters),
                            typeof(SpreadsheetExportParameters),
                            typeof(SpreadsheetImportParameters),
                        }
                    )
                    .MapResult(
                        (HydrateParameters opts) => HydrateBookCommand.Handle(opts),
                        (UploadParameters opts) => HandleUpload(opts),
                        (DownloadBookOptions opts) =>
                            DownloadBookCommand.HandleSilentDownload(opts),
                        (GetUsedFontsParameters opts) => GetUsedFontsCommand.Handle(opts),
                        (ChangeLayoutParameters opts) => ChangeLayoutCommand.Handle(opts),
                        (CreateArtifactsParameters opts) => CreateArtifactsCommand.Handle(opts),
                        (SendFontAnalyticsParameters opts) => SendFontAnalyticsCommand.Handle(opts),
                        (SpreadsheetExportParameters opts) => SpreadsheetExportCommand.Handle(opts),
                        // We don't have a way to get the CollectionSettings object for the Import process.
                        // This means that if we use this CLI version, care should be taken to update the book,
                        // so the pages get the correct "side" classes (side-left, side-right). (BL-10884)
                        (SpreadsheetImportParameters opts) => SpreadsheetImportCommand.Handle(opts),
                        async errors =>
                        {
                            var code = 0;
                            foreach (var error in errors)
                            {
                                if (
                                    !(error is HelpVerbRequestedError)
                                    && !(error is HelpRequestedError)
                                )
                                {
                                    // All of the errors have already been reported in English text. This would just add
                                    // a cryptic class name to the output, possibly in the middle of a line in the usage
                                    // message displayed as a result of the errors.
                                    // Console.WriteLine(error.ToString());
                                    code = 1;
                                }
                            }

                            return code;
                        }
                    );
                // What we want to do here is await mainTask. But to do that we have to make
                // Main async. That is allowed since C# 7.1, but if we do it, we don't get
                // a Windows.Forms synchronization context, which means async tasks started on
                // the UI thread don't have to complete on the UI thread. That will mess up
                // WebView2, and it is so that we can use WebView2's ExecuteJavascriptAsync
                // that we made all these commands async to begin with.
                // As soon as we DO have a windows.forms sync context...even if we made it ourselves,
                // which is tricky because await won't work on a windows.forms sync context
                // until we enter Run() and start pumping messages...we run into the problem
                // that we need the result of the main task to return as the result of Main().
                // We can't just call Result, because that blocks the main thread, which stops
                // it pumping messages, which means anything that awaits on the main thread
                // will deadlock.
                // So, the best I can find to do is to sit here pumping messages until the
                // main task completes.
                // (Many of the main tasks don't actually do any awaiting and will immediately
                // show as completed.)
                while (!mainTask.IsCompleted)
                {
                    Application.DoEvents();
                }
                return mainTask.Result; // we're done; this is safe once there is nothing being awaited.
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                var args = args1;

                if (SIL.PlatformUtilities.Platform.IsWindows)
                {
                    OldVersionCheck();
                }

                // In early 2023, MS stopped updating WebView2 for Windows 7, 8, and 8.1.
                if (Environment.OSVersion.Version.Major < 10)
                {
                    using (_applicationContainer = new ApplicationContainer())
                    {
                        var msg = LocalizationManager.GetString(
                            "Errors.Pre10WindowsNotSupported",
                            "We are sorry, but your version of Windows is no longer supported by Microsoft. This version of Bloom requires Windows 10 or greater. As a result, you will need to install Bloom version 5.4. Bloom will now open a web page where you can download Bloom 5.4."
                        );
                        MessageBox.Show(msg, "Bloom");
                        SIL.Program.Process.SafeStart(
                            UrlLookup.LookupUrl(UrlType.LastVersionForPreWindows10, null)
                        );
                        return 1;
                    }
                }

                if (IsWebviewMissingOrTooOld())
                    return 1;

                //bring in settings from any previous version
                if (Settings.Default.NeedUpgrade)
                {
                    //see http://stackoverflow.com/questions/3498561/net-applicationsettingsbase-should-i-call-upgrade-every-time-i-load
                    Settings.Default.Upgrade();
                    Settings.Default.Reload();
                    Settings.Default.NeedUpgrade = false;
                    Settings.Default.MaximizeWindow = true; // this is needed to force this to be written to the file, where a user can find it to modify it by hand (our video maker)
                    Settings.Default.Save();

                    StartUpWithFirstOrNewVersionBehavior = true;
                }
                // Migrate from old monolithic experimental features setting.
                ExperimentalFeatures.MigrateFromOldSettings();

                if (IsInstallerLaunch(args))
                {
                    InstallerSupport.HandleSquirrelInstallEvent(args); // may exit program
                }

                // Needs to be AFTER HandleSquirrelInstallEvent, because that can happen when the program is launched by Update rather than
                // by the user.
                if (!Settings.Default.LicenseAccepted)
                {
                    using (var dlg = new LicenseDialog("license.htm"))
                        if (dlg.ShowDialog() != DialogResult.OK)
                            return 1;
                    Settings.Default.LicenseAccepted = true;
                    Settings.Default.Save();
                }

#if DEBUG
                if (args.Length > 0)
                {
                    if (IsLocalizationHarvestingLaunch(args))
                        LocalizationManager.IgnoreExistingEnglishTranslationFiles = true;
                    else
                        // This allows us to debug things like  interpreting a URL.
                        MessageBox.Show("Attach debugger now");
                }
                var harvest = Environment.GetEnvironmentVariable("HARVEST_FOR_LOCALIZATION");
                if (
                    !String.IsNullOrWhiteSpace(harvest)
                    && (harvest.ToLowerInvariant() == "on" || harvest.ToLowerInvariant() == "yes")
                )
                    LocalizationManager.IgnoreExistingEnglishTranslationFiles = true;
#endif

                using (InitializeAnalytics())
                {
                    // do not show the registration dialog if bloom was started for a special purpose
                    if (args.Length > 0)
                        _supressRegistrationDialog = true;

                    if (args.Length == 1 && args[0].ToLowerInvariant().EndsWith(".bloompack"))
                    {
                        SetUpErrorHandling();
                        using (_applicationContainer = new ApplicationContainer())
                        {
                            var path = args[0];
                            // This allows local links to bloom packs.
                            if (path.ToLowerInvariant().StartsWith("bloom://"))
                            {
                                path = path.Substring("bloom://".Length);
                                if (!RobustFile.Exists(path))
                                {
                                    path = FileLocationUtilities.GetFileDistributedWithApplication(
                                        true,
                                        path
                                    );
                                    if (!RobustFile.Exists(path))
                                        return 1;
                                }
                            }
                            using (var dlg = new BloomPackInstallDialog(path))
                            {
                                dlg.ShowDialog();
                                if (dlg.ExitWithoutRunningBloom)
                                    return 1;
                            }
                        }
                        // Continue with normal startup now we unpacked the bloompack.
                        // Various other things will misinterpret the .bloompack argument if we leave it in args.
                        args = new string[] { };
                    }

                    if (FolderTeamCollection.IsJoinTeamCollectionFile(args))
                    {
                        SetUpErrorHandling();
                        string newCollection;
                        // When we're spinning up a fake local collection in "projectName", we don't want
                        // any chance of copying FROM there to the repo.
                        TeamCollectionManager.ForceNextSyncToLocal = true;
                        // Since the New Team Collection dialog is an HTML/Typescript one, we need to spin up
                        // quite a lot of stuff so it can use a BloomServer to get localization information
                        // and otherwise communicate with the C# side of things. But we can't wait to do this
                        // until all this stuff is spun up normally, because it gets spun up for a specific
                        // collection, and we're in the process of creating the collection. In fact, we need
                        // to make a fake version of the collection in a temp folder that we can pretend to
                        // be editing in order to make a ProjectContext. We do manage to avoid building at
                        // least some of the unnecessary objects by passing justEnoughForHtmlDialog true.
                        using (_applicationContainer = new ApplicationContainer())
                        {
                            using (var fakeProjectFolder = new TemporaryFolder("projectName"))
                            {
                                var fakeCollectionPath =
                                    FolderTeamCollection.SetupMinimumLocalCollectionFilesForRepo(
                                        Path.GetDirectoryName(args[0]),
                                        fakeProjectFolder.FolderPath
                                    );
                                using (
                                    var projectContext = _applicationContainer.CreateProjectContext(
                                        fakeCollectionPath,
                                        true
                                    )
                                )
                                {
                                    if (!UniqueToken.AcquireTokenQuietly(_mutexId))
                                    {
                                        var msg = LocalizationManager.GetString(
                                            "TeamCollection.QuitOtherBloom",
                                            "Please close Bloom before joining a Team Collection"
                                        );
                                        BloomMessageBox.ShowInfo(msg);
                                        return 1;
                                    }
                                    gotUniqueToken = true;

                                    if (
                                        projectContext.TeamCollectionManager.CurrentCollection
                                        == null
                                    )
                                    {
                                        if (
                                            projectContext
                                                .TeamCollectionManager
                                                .CurrentCollectionEvenIfDisconnected != null
                                        )
                                        {
                                            var msg = LocalizationManager.GetString(
                                                "TeamCollection.ConnectToJoin",
                                                "Bloom cannot currently join this collection."
                                            );
                                            // This is a bit of a kludge, but when we make a disconnected collection it's pretty
                                            // consistently true that the last message just says "you'll be disconnected till you fix this",
                                            // but the previous one actually says what's wrong. So we'll reuse this in this (hopefully)
                                            // rare case.
                                            var messages =
                                                projectContext.TeamCollectionManager.CurrentCollectionEvenIfDisconnected.MessageLog.GetProgressMessages();
                                            if (
                                                messages.Length > 1
                                                && messages[messages.Length - 2].progressKind
                                                    == "Error"
                                            )
                                            {
                                                msg +=
                                                    Environment.NewLine
                                                    + messages[messages.Length - 2].message;
                                            }
                                            ErrorReport.NotifyUserOfProblem(msg);
                                        }
                                        return 1; // something went wrong processing it, hopefully already reported.
                                    }
                                    newCollection =
                                        FolderTeamCollection.ShowJoinCollectionTeamDialog(
                                            args[0],
                                            projectContext.TeamCollectionManager
                                        );
                                }
                            }
                        }

                        if (newCollection == null) // user canceled
                            return 1;

                        args = new string[] { }; // continue to open, but without args.

                        // Put the new TC's local collection into our MRU list so it will be the one we open.

                        // Usually guaranteed to exist by ApplicationContainer, but we haven't created that yet.
                        // (This might be redundant now we create one above for showing the dialog.)
                        if (Settings.Default.MruProjects == null)
                        {
                            Settings.Default.MruProjects = new MostRecentPathsList();
                        }

                        Settings.Default.MruProjects.AddNewPath(newCollection);
                        Settings.Default.Save();

                        // and now we need to get the lock as usual before going on to load the new collection.
                        if (!UniqueToken.AcquireToken(_mutexId, "Bloom"))
                            return 1;
                        gotUniqueToken = true;
                    }
                    else if (IsBloomBookOrder(args))
                    {
                        bool forEdit = HandleDownload(args[0]);

                        if (UniqueToken.AcquireTokenQuietly(_mutexId))
                        {
                            // No other instance isrunning. Start up normally (and show the book just downloaded).
                            // See https://silbloom.myjetbrains.com/youtrack/issue/BL-3822
                            gotUniqueToken = true;
                        }
                        else if (forEdit)
                        {
                            RunningSecondInstance = true;
                        }
                        else
                        {
                            // Another instance is running. For a normal download to "books from bloom library", this
                            // instance has served its purpose and can exit right away. If we've created a new collection
                            // for editing the book we downloaded, we will open it now even though we don't have the token.
                            return 0;
                        }
                    }
                    else
                    {
                        if ((Control.ModifierKeys & Keys.Control) == Keys.Control)
                        {
                            // control key is held down so allow second instance to run; note that we're deliberately in this state
                            if (UniqueToken.AcquireTokenQuietly(_mutexId))
                            {
                                gotUniqueToken = true;
                            }
                            else
                            {
                                RunningSecondInstance = true;
                            }
                        }
                        else if (UniqueToken.AcquireToken(_mutexId, "Bloom"))
                        {
                            // No other instance is running. We own the token and should release it on quitting.
                            gotUniqueToken = true;
                        }
                        else
                        {
                            // We're trying to run a second instance. This is not allowed except for a few special cases,
                            // such as (temporarily) when downloading a book from BloomLibrary, or when ctrl is held down. We'll just quit now.
                            // (UniqueToken.AcquireToken will have already notified the user of this situation.)
                            return 1;
                        }
                    }
                    if (IsInstallerLaunch(args))
                    {
                        // Start the splash screen early for installer launches to reassure
                        // user while localization and other one-time setup is happening.  For
                        // installer launches, no more dialogs should be popping up after this.
                        // See https://issues.bloomlibrary.org/youtrack/issue/BL-12085.
                        StartupScreenManager.StartManaging();
                    }
                    OldVersionCheck();

                    SetUpErrorHandling();

                    using (_applicationContainer = new ApplicationContainer())
                    {
                        InstallerSupport.MakeBloomRegistryEntries(args);
                        BookDownloadSupport.EnsureDownloadFolderExists();

                        if (
                            args.Length == 1
                            && !IsInstallerLaunch(args)
                            && !IsLocalizationHarvestingLaunch(args)
                            && !IsBloomBookOrder(args)
                        )
                        {
                            // Might be a double-click on a .bloomCollection file.  We'll open it after running some checks.
                            var argPath = args[0];

                            // See BL-10012. Windows File explorer will give us an 8.3 path with ".BLO" at the end, so we must convert
                            // before looking for .bloomCollection.
                            var path = Utils.LongPathAware.GetLongPath(argPath);

                            if (path.ToLowerInvariant().EndsWith(@".bloomproblembook"))
                            {
                                Settings.Default.MruProjects.AddNewPath(
                                    ProblemReportApi.UnpackProblemBook(path)
                                );
                            }
                            else if (path.ToLowerInvariant().EndsWith(@".bloomcollection"))
                            {
                                // See BL-10012. We'll die eventually, might as well nip this in the bud.
                                if (Utils.LongPathAware.GetExceedsMaxPath(path))
                                {
                                    var pathForWantOfAClosure = path;
                                    StartupScreenManager.AddStartupAction(
                                        () =>
                                            Utils.LongPathAware.ReportLongPath(
                                                pathForWantOfAClosure
                                            )
                                    );
                                    // go forwards as if we weren't given an explicit collection to open (except we're showing a report about what happened)
                                    // (That is, if the collection path is too long to open successfully, we don't add it to MruProjects, so will start up
                                    // as if the user just double-clicked Bloom itself.)
                                }
                                else
                                {
                                    if (Utils.MiscUtils.ReportIfInvalidCollection(path))
                                        return 1;
                                    Settings.Default.MruProjects.AddNewPath(path);
                                }
                            }
                        }

                        if (args.Length > 0 && args[0] == "--rename")
                        {
                            try
                            {
                                // Using Windows File Explorer to open a file causes the Current Directory to be set to the directory we want to
                                // rename, and that prevents the rename. Since we don't even use Current Directory in Bloom, just change it to temp.
                                // See BL-11004
                                Directory.SetCurrentDirectory(Path.GetTempPath());
                                var pathToNewCollection = CollectionSettings.RenameCollection(
                                    args[1],
                                    args[2]
                                );
                                //MessageBox.Show("Your collection has been renamed.");
                                Settings.Default.MruProjects.AddNewPath(pathToNewCollection);
                            }
                            catch (ApplicationException error)
                            {
                                SIL.Reporting.ErrorReport.NotifyUserOfProblem(error, error.Message);
                                Environment.Exit(-1);
                            }
                            catch (Exception error)
                            {
                                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                                    error,
                                    "Bloom could not finish renaming your collection folder. Restart your computer and try again."
                                );
                                Environment.Exit(-1);
                            }
                        }

                        if (!BloomIntegrityChecker.CheckIntegrity())
                        {
                            Environment.Exit(-1);
                        }

                        if (!DefenderFolderProtectionCheck.CanWriteToDirectory())
                        {
                            Environment.Exit(-1);
                        }

                        LocalizationManager.SetUILanguage(
                            Settings.Default.UserInterfaceLanguage,
                            false
                        );
                        // TODO-WV2: Can we set the browser language for WV2?  Do we need to?

                        // Kick off getting all the font metadata for fonts currently installed in the system.
                        // This can take several seconds on slow machines with lots of fonts installed, so we
                        // run it in the background once at startup.  (The results are cached automatically.)
                        System.Threading.Tasks.Task.Run(
                            () => FontProcessing.FontsApi.GetAllFontMetadata()
                        );

                        // This has served its purpose on Linux, and with Geckofx60 it interferes with CommandLineRunner.
                        _originalPreload = Environment.GetEnvironmentVariable("LD_PRELOAD");
                        Environment.SetEnvironmentVariable("LD_PRELOAD", null);

                        Run(args);
                    }
                }
            }
            finally
            {
                // Check memory one final time for the benefit of developers.  The user won't see anything.
                Bloom.Utils.MemoryManagement.CheckMemory(true, "Bloom finished and exiting", false);
                if (gotUniqueToken)
                    UniqueToken.ReleaseToken();

                _sentry?.Dispose();
                // In a debug build we want to be able to see if we're leaving garbage around. (Note: this doesn't seem to be working.)
#if !Debug
                // We should not clean up garbage if we didn't get the token.
                // - we might delete something in use by the instance that has the token
                // - we would delete the token itself (since _mutexid and NamePrefix happen to be the same),
                // allowing a later duplicate process to start normally.
                if (gotUniqueToken)
                    TempFile.CleanupTempFolder();
#endif
            }
            Settings.Default.FirstTimeRun = false;
            Settings.Default.Save();
            return 0;
        }

        private static bool IsWebviewMissingOrTooOld()
        {
            string version;
            bool missingOrAntique = !WebView2Browser.GetIsWebView2NewEnough(out version);
            if (missingOrAntique)
            {
                using (_applicationContainer = new ApplicationContainer())
                {
                    var msgBldr = new StringBuilder();
                    var msgFmt1 = LocalizationManager.GetString(
                        "Webview.MissingOrTooOld",
                        "Bloom depends on Microsoft WebView2 Evergreen, at least version {0}. We will now send you to a webpage that will help you add this to your computer."
                    );
                    msgBldr.AppendFormat(msgFmt1, WebView2Browser.kMinimumWebView2Version);
                    msgBldr.AppendLine();
                    if (version == WebView2Browser.kWebView2NotInstalled)
                    {
                        msgBldr.Append(
                            LocalizationManager.GetString(
                                "Webview.NotInstalled",
                                "(Currently not installed)"
                            )
                        );
                    }
                    else
                    {
                        var msgFmt2 = LocalizationManager.GetString(
                            "Webview.CurrentVersion",
                            "(Currently {0})"
                        );
                        msgBldr.AppendFormat(msgFmt2, version);
                    }
                    MessageBox.Show(msgBldr.ToString());
                    // The new process showing a website should use the current culture, so we don't need to worry about that.
                    // We don't wait for this to finish, so we don't use the CommandLineRunner methods.
                    ProcessExtra.SafeStartInFront("https://docs.bloomlibrary.org/webview2");
                }
            }
            return missingOrAntique;
        }

        static async Task<int> HandleUpload(UploadParameters opts)
        {
            using (InitializeAnalytics())
            {
                return await UploadCommand.Handle(opts);
            }
        }

        /// <summary>
        /// Sets up different analytics channels depending on Debug or not.
        /// Also determines whether or not Registration should occur.
        /// </summary>
        /// <returns></returns>
        private static DesktopAnalytics.Analytics InitializeAnalytics()
        {
            // Ensures that registration settings for all channels of Bloom are stored in a common place,
            // so the user is not asked to register each independently.
            RegistrationSettingsProvider.SetProductName("Bloom");

            Dictionary<string, string> propertiesThatGoWithEveryEvent =
                ErrorReport.GetStandardProperties();
            propertiesThatGoWithEveryEvent.Remove("MachineName");
            propertiesThatGoWithEveryEvent.Remove("UserName");
            propertiesThatGoWithEveryEvent.Remove("UserDomainName");
            propertiesThatGoWithEveryEvent.Add("channel", ApplicationUpdateSupport.ChannelName);

            DesktopAnalytics.Analytics.UrlThatReturnsExternalIpAddress = "http://icanhazip.com";

#if DEBUG
            _supressRegistrationDialog = true;
            return new DesktopAnalytics.Analytics(
                "rw21mh2piu",
                RegistrationDialog.GetAnalyticsUserInfo(),
                propertiesThatGoWithEveryEvent,
                allowTracking: false, // change to true if you want to test sending
                retainPii: true,
                clientType: DesktopAnalytics.ClientType.Segment,
                host: "https://analytics.bloomlibrary.org"
            );
#else
            var feedbackSetting = System.Environment.GetEnvironmentVariable("FEEDBACK");

            //default is to allow tracking
            var allowTracking = IsFeedbackOn();
            _supressRegistrationDialog = _supressRegistrationDialog || !allowTracking;

            return new DesktopAnalytics.Analytics(
                "c8ndqrrl7f0twbf2s6cv",
                RegistrationDialog.GetAnalyticsUserInfo(),
                propertiesThatGoWithEveryEvent,
                allowTracking,
                retainPii: true,
                clientType: DesktopAnalytics.ClientType.Segment,
                host: "https://analytics.bloomlibrary.org"
            );
#endif
        }

        private static bool IsFeedbackOn()
        {
            var feedbackSetting = System.Environment.GetEnvironmentVariable("FEEDBACK");

            //default is to allow feedback/tracking
            var isFeedbackOn =
                string.IsNullOrEmpty(feedbackSetting)
                || feedbackSetting.ToLowerInvariant() == "yes"
                || feedbackSetting.ToLowerInvariant() == "true"
                || feedbackSetting.ToLowerInvariant() == "on";

            return isFeedbackOn;
        }

#if DEBUG
        private static bool _harvestFinalized;
        private static object _harvestLock = new object();
#endif

        /// <summary>
        /// When exiting, finish any processing needed for harvesting strings for localization.
        /// </summary>
        /// <remarks>
        /// The lock and boolean check are to prevent this from being executed more than once
        /// as the program is shutting down.  Somehow this was being called twice before these
        /// checks were added.
        /// </remarks>
        public static void FinishLocalizationHarvesting()
        {
#if DEBUG
            lock (_harvestLock)
            {
                if (LocalizationManager.IgnoreExistingEnglishTranslationFiles && !_harvestFinalized)
                {
                    var installedStringFileFolder =
                        FileLocationUtilities.GetDirectoryDistributedWithApplication(
                            true,
                            "localization"
                        );
                    LocalizationManager.MergeExistingEnglishTranslationFileIntoNew(
                        installedStringFileFolder,
                        "Bloom"
                    );
                    LocalizationManager.MergeExistingEnglishTranslationFileIntoNew(
                        installedStringFileFolder,
                        "Palaso"
                    );
                }
                _harvestFinalized = true;
            }
#endif
        }

        /// <summary>
        /// This routine handles the download requests which come as an order URL from BloomLibrary.
        /// Enhance: it's unfortunate that we have two command-line methods of downloading a book. However, they have
        /// rather different requirements:
        ///   - this one displays a progress UI, the other doesn't.
        ///   - this one must extract the URL from a bloom: url (which the library must produce with urlencoding),
        ///			for the other, it's more convenient to pass an unencoded url
        ///   - worse, this version typically goes on to fully launch Bloom; the other always shuts the program down
        ///			when done. Thus, this version is much more tightly connected to the normal startup code.
        /// Note that we can't easily change the exact command line that this version deals with, because
        /// Bloom library generates that command line, and if we change what it generates, everyone running an
        /// older Bloom will be in trouble.
        /// Most of the core implementation of the download process is common.
        /// </summary>
        private static bool HandleDownload(string bookOrderUrl)
        {
            // We will start up just enough to download the book. This avoids the code that normally tries to keep only a single instance running.
            // There is probably a pathological case here where we are overwriting an existing template just as the main instance is trying to
            // do something with it. The time interval would be very short, because download uses a temp folder until it has the whole thing
            // and then copies (or more commonly moves) it over in one step, and making a book from a template involves a similarly short
            // step of copying the template to the new book. Hopefully users have (or will soon learn) enough sense not to
            // try to use a template while in the middle of downloading a new version.
            SetUpErrorHandling();
            bool forEdit = false;
            using (_applicationContainer = new ApplicationContainer())
            {
                //JT please review: is this needed? InstallerSupport.MakeBloomRegistryEntries(args);
                BookDownloadSupport.EnsureDownloadFolderExists();
                LocalizationManager.SetUILanguage(Settings.Default.UserInterfaceLanguage, false);
                var downloader = new BookDownload(ProjectContext.CreateBloomS3Client());
                downloader.HandleBloomBookOrder(bookOrderUrl);
                PathToBookDownloadedAtStartup = downloader.LastBookDownloadedPath;
                if (downloader.CollectionCreatedForLastDownload != null)
                {
                    Settings.Default.MruProjects.AddNewPath(
                        downloader.CollectionCreatedForLastDownload
                    );
                    Settings.Default.Save();
                    forEdit = true;
                }
                // BL-2143: Don't show download complete message if download was not successful
                // Also, if we're about to open a new collection (BL-13036), I don't think we need a message...
                // it will be obvious we succeeded, and there is nothing to say about finding
                // the book because it's the only one. If we do want a message in that case,
                // it needs rewording.
                if (!string.IsNullOrEmpty(PathToBookDownloadedAtStartup) && !forEdit)
                {
                    var caption = LocalizationManager.GetString(
                        "Download.CompletedCaption",
                        "Download complete"
                    );
                    var message = LocalizationManager.GetString(
                        "Download.Completed",
                        @"Your download ({0}) is complete. You can see it in the 'Books from BloomLibrary.org' section of your Collections."
                    );
                    message = string.Format(
                        message,
                        Path.GetFileName(PathToBookDownloadedAtStartup)
                    );
                    MessageBox.Show(message, caption);
                }
            }

            return forEdit;
        }

        public static string BloomExePath => Application.ExecutablePath;

        public static void RestartBloom(bool hardExit, string args = null)
        {
            try
            {
                var program = BloomExePath;
                if (SIL.PlatformUtilities.Platform.IsLinux)
                {
                    // This is needed until the day comes (if it ever does) when we can use the
                    // system mono on Linux.
                    program = "/opt/mono5-sil/bin/mono";
                    if (args == null)
                        args = "\"" + BloomExePath + "\"";
                    else
                        args = "\"" + BloomExePath + "\" " + args;
                    if (_originalPreload != null)
                        Environment.SetEnvironmentVariable("LD_PRELOAD", _originalPreload);
                }
                if (args == null)
                    Process.Start(program);
                else
                    Process.Start(program, args);

                //give some time for that process.start to finish starting the new instance, which will see
                //we have a mutex and wait for us to die.

                Thread.Sleep(2000);
                if (hardExit || _projectContext?.ProjectWindow == null)
                {
                    Application.Exit();
                }
                else
                {
                    _projectContext.ProjectWindow.Close();
                }
            }
            catch (Exception e)
            {
                ErrorReport.NotifyUserOfProblem(e, "Bloom encountered a problem while restarting.");
            }
        }

        private static bool IsInstallerLaunch(string[] args)
        {
            return args.Length > 0 && args[0].ToLowerInvariant().StartsWith("--squirrel");
        }

        private static bool IsLocalizationHarvestingLaunch(string[] args)
        {
            return args.Length == 1
                && args[0].StartsWith("--ha")
                && "--harvest-for-localization".StartsWith(args[0]);
        }

        // I think this does something like the Wix element
        // <ProgId Id='Bloom.BloomCollectionFile' Description='BloomPack file' >
        //   <Extension Id='BloomCollectionFile' ContentType='application/bloom'>
        //	   <!-- I know application/bloom looks weird, but it copies MSword docs -->
        //	   <Verb Id='open' Command='Open' TargetFile ='Bloom.exe' Argument='"%1"' />
        //   </Extension>
        // </ProgId>
        // (But I'm not completely sure all these come from that)

        // The folder where we tell squirrel to look for upgrades.
        // As of 2-20-15 this is  = @"https://s3.amazonaws.com/bloomlibrary.org/squirrel";
        // Controlled by the file at "http://bloomlibrary.org/channels/SquirrelUpgradeTable.txt".
        // This allows us to have different sets of deltas and upgrade targets for betas and stable releases,
        // or indeed to do something special for any particular version(s) of Bloom,
        // or even to switch to a different upgrade path after releasing a version.

        internal static void SetProjectContext(ProjectContext projectContext)
        {
            _projectContext = projectContext;
        }

        [HandleProcessCorruptedStateExceptions]
        private static void Run(string[] args)
        {
            if (!IsInstallerLaunch(args))
                StartupScreenManager.StartManaging();

            Settings.Default.Save();

            // Note: MainContext needs to be set from WinForms land (Application.Run() from System.Windows.Forms), not from here.
            StartupScreenManager.AddStartupAction(
                () => MainContext = SynchronizationContext.Current
            );
            StartupScreenManager.AddStartupAction(
                () => StartUpShellBasedOnMostRecentUsedIfPossible()
            );
            StartupScreenManager.DoLastOfAllAfterClosingSplashScreen = () =>
            {
                if (_projectContext != null && _projectContext.ProjectWindow != null)
                {
                    var shell = _projectContext.ProjectWindow as Shell;
                    if (shell != null)
                    {
                        shell.Invoke((Action)(() => shell.ReallyComeToFront()));
                    }
                }
            };
            StartupScreenManager.AddStartupAction(
                () =>
                {
                    CheckRegistration();
                },
                shouldHideSplashScreen: RegistrationDialog.ShouldWeShowRegistrationDialog(),
                lowPriority: true
            );

            // Crashes if initialized twice, and there's at least once case when joining a TC
            // where we can come here twice.
            if (!Sldr.IsInitialized)
                Sldr.Initialize();
            try
            {
                Application.Run();
            }
            catch (System.Reflection.TargetInvocationException bad)
            {
                if (bad.InnerException is System.AccessViolationException)
                    Logger.WriteError(
                        "Exception caught at outermost level of Bloom: ",
                        bad.InnerException
                    );
                else
                    Logger.WriteError("Exception caught at outermost level of Bloom:", bad);
                var exceptMsg = "TargetInvocationException";
                try
                {
                    NonFatalProblem.ReportSentryOnly(bad, throwOnException: true);
                }
                catch (Exception e)
                {
                    exceptMsg += $" (Sentry report failed: {e})";
                }
                ShowUserEmergencyShutdownMessage(bad);
                System.Environment.FailFast(exceptMsg);
            }
            catch (System.AccessViolationException nasty)
            {
                Logger.WriteError("Exception caught at outermost level of Bloom: ", nasty);
                var exceptMsg = "AccessViolationException";
                try
                {
                    NonFatalProblem.ReportSentryOnly(nasty, throwOnException: true);
                }
                catch (Exception e)
                {
                    exceptMsg += $" (Sentry report failed: {e})";
                }
                ShowUserEmergencyShutdownMessage(nasty);
                System.Environment.FailFast(exceptMsg);
            }
            finally
            {
                if (FileMeddlerManager.IsMeddling)
                    FileMeddlerManager.Stop();
            }

            try
            {
                Settings.Default.Save();
            }
            catch (ArgumentException e)
            {
                if (
                    MiscUtils.ContainsSurrogatePairs(Settings.Default.CurrentBookPath)
                    && e.Message.Contains("surrogate")
                )
                {
                    Settings.Default.CurrentBookPath = "";
                    Settings.Default.Save();
                }
            }

            Sldr.Cleanup();
            Logger.ShutDown();

            if (_projectContext != null)
                _projectContext.Dispose();
        }

        /// <summary>
        /// Show the user an emergency shutdown message.  The application event loop is almost
        /// certainly dead when this method is called, so write out a text file and start another
        /// process to show it to the user using the default program for opening .txt files.
        /// </summary>
        private static void ShowUserEmergencyShutdownMessage(Exception nasty)
        {
            StartupScreenManager.CloseSplashScreen();
            if (SIL.PlatformUtilities.Platform.IsWindows)
            {
                try
                {
                    Win32Imports.MessageBox(
                        new IntPtr(0),
                        "Something unusual happened and Bloom needs to quit.  A report has been sent to the Bloom Team.\r\nIf this keeps happening to you, please write to issues@BloomLibrary.org.\r\n\r\n"
                            + nasty.Message,
                        "Bloom Crash",
                        0
                    );
                    return;
                }
                catch
                {
                    // nothing we can do if Win32Imports.MessageBox throws: try the fallback approach
                    // of writing a text file and opening it in a different process.
                }
            }
            string tempFileName = TempFile.WithExtension(".txt").Path;
            using (var writer = RobustFile.CreateText(tempFileName))
            {
                writer.WriteLine(
                    "Something unusual happened and Bloom needs to quit.  A report has been sent to the Bloom Team."
                );
                writer.WriteLine(
                    "If this keeps happening to you, please write to issues@BloomLibrary.org."
                );
                writer.WriteLine();
                writer.WriteLine(nasty.Message);
                writer.Flush();
                writer.Close();
            }
            ProcessExtra.SafeStartInFront(tempFileName);
        }

        private static bool IsBloomBookOrder(string[] args)
        {
            return args.Length == 1
                && !args[0].ToLowerInvariant().EndsWith(".bloomcollection")
                && !args[0].ToLowerInvariant().EndsWith(".bloomproblembook")
                && !IsInstallerLaunch(args);
        }

        private static void CheckRegistration()
        {
            if (RegistrationDialog.ShouldWeShowRegistrationDialog() && !_supressRegistrationDialog)
            {
                using (
                    var dlg = new RegistrationDialog(
                        false,
                        _projectContext.TeamCollectionManager.UserMayChangeEmail
                    )
                )
                {
                    if (_projectContext != null && _projectContext.ProjectWindow != null)
                        dlg.ShowDialog(_projectContext.ProjectWindow);
                    else
                    {
                        dlg.ShowDialog();
                    }
                }
            }
        }

#if PerProjectMutex

        //NB: initially, you could have multiple blooms, if they were different projects.
        //however, then we switched to the embedded http image server, which had a fixed
        //port. That has now been fixed, however in the meantime we switched to using a
        //differnt mutex approach (UniqueToken), so this code would need some updating.

        private static bool GrabTokenForThisProject(string pathToProject)
        {
            //ok, here's how this complex method works...
            //First, we try to get the mutex quickly and quitely.
            //If that fails, we put up a dialog and wait a number of seconds,
            //while we wait for the mutex to come free.


            string mutexId = "bloom";
            //			string mutexId = pathToProject;
            //			mutexId = mutexId.Replace(Path.DirectorySeparatorChar, '-');
            //			mutexId = mutexId.Replace(Path.VolumeSeparatorChar, '-');
            bool mutexAcquired = false;
            try
            {
                _oneInstancePerProjectMutex = Mutex.OpenExisting(mutexId);
                mutexAcquired = _oneInstancePerProjectMutex.WaitOne(
                    TimeSpan.FromMilliseconds(1 * 1000),
                    false
                );
            }
            catch (WaitHandleCannotBeOpenedException e) //doesn't exist, we're the first.
            {
                _oneInstancePerProjectMutex = new Mutex(true, mutexId, out mutexAcquired);
                mutexAcquired = true;
            }
            catch (AbandonedMutexException e)
            {
                //that's ok, we'll get it below
            }

            using (var dlg = new SimpleMessageDialog("Waiting for other Bloom to finish..."))
            {
                dlg.TopMost = true;
                dlg.Show();
                try
                {
                    _oneInstancePerProjectMutex = Mutex.OpenExisting(mutexId);
                    mutexAcquired = _oneInstancePerProjectMutex.WaitOne(
                        TimeSpan.FromMilliseconds(10 * 1000),
                        false
                    );
                }
                catch (AbandonedMutexException e)
                {
                    _oneInstancePerProjectMutex = new Mutex(true, mutexId, out mutexAcquired);
                    mutexAcquired = true;
                }
                catch (Exception e)
                {
                    ErrorReport.NotifyUserOfProblem(
                        e,
                        "There was a problem starting Bloom which might require that you restart your computer."
                    );
                }
            }

            if (!mutexAcquired) // cannot acquire?
            {
                _oneInstancePerProjectMutex = null;
                ErrorReport.NotifyUserOfProblem(
                    "Another copy of Bloom is already open with "
                        + pathToProject
                        + ". If you cannot find that Bloom, restart your computer."
                );
                return false;
            }
            return true;
        }

        public static void ReleaseMutexForThisProject()
        {
            if (_oneInstancePerProjectMutex != null)
            {
                _oneInstancePerProjectMutex.ReleaseMutex();
                _oneInstancePerProjectMutex = null;
            }
        }
#endif

        /// ------------------------------------------------------------------------------------
        private static void StartUpShellBasedOnMostRecentUsedIfPossible()
        {
            var path = Settings.Default.MruProjects.Latest;

            if (!string.IsNullOrEmpty(path))
            {
                // Catch case where the last collection was so long that windows gave us a 8.3 version which will eventually
                // fail. Just fail right now, don't bother to have a conversation with the user about it.
                // This might be impossible in real life, I'm not sure. Part of BL-10012.
                if (path.EndsWith(".BLO"))
                {
                    Settings.Default.MruProjects.RemovePath(path);
                    path = null;
                }
                else
                {
                    while (Utils.MiscUtils.IsInvalidCollectionToEdit(path))
                    {
                        // Somehow...from a previous version?...we have an invalid file in our MRU list.
                        Settings.Default.MruProjects.RemovePath(path);
                        path = Settings.Default.MruProjects.Latest;
                    }
                }
            }

            if (path == null || !OpenProjectWindow(path))
            {
                // Rather than just adding it to the idle queue, we make sure it doesn't overlap with any other startup idle tasks
                // and that the splash screen will be closed to make way for it.
                StartupScreenManager.AddStartupAction(
                    () => ChooseACollection(),
                    shouldHideSplashScreen: true
                );
            }
        }

        /// ------------------------------------------------------------------------------------
        private static bool OpenProjectWindow(string collectionPath)
        {
            Debug.Assert(_projectContext == null);

            try
            {
                //// See BL-10012.
                var path = Bloom.Utils.LongPathAware.GetLongPath(collectionPath);
                if (Utils.LongPathAware.GetExceedsMaxPath(path))
                {
                    Utils.LongPathAware.ReportLongPath(path);
                    return false;
                }

                //NB: initially, you could have multiple blooms, if they were different projects.
                //however, then we switched to the embedded http image server, which can't share
                //a port. So we could fix that (get different ports), but for now, I'm just going
                //to lock it down to a single bloom
                /*					if (!GrabTokenForThisProject(projectPath))
                                    {
                                        return false;
                                    }
                                */
                _projectContext = _applicationContainer.CreateProjectContext(path);
                _projectContext.ProjectWindow.Closed += HandleProjectWindowClosed;
                _projectContext.ProjectWindow.Activated += HandleProjectWindowActivated;
#if DEBUG
                CheckLinuxFileAssociations();
#endif
                _projectContext.ProjectWindow.Show();

                StartupScreenManager.PutSplashAbove(_projectContext.ProjectWindow);

                if (BloomThreadCancelService != null)
                    BloomThreadCancelService.Dispose();
                BloomThreadCancelService = new CancellationTokenSource();

                return true;
            }
            catch (Exception e)
            {
                HandleErrorOpeningProjectWindow(e, collectionPath);
            }

            return false;
        }

        private static void HandleProjectWindowActivated(object sender, EventArgs e)
        {
            _projectContext.ProjectWindow.Activated -= HandleProjectWindowActivated;

            // Sometimes after closing the splash screen the project window
            // looses focus, so do this.
            _projectContext.ProjectWindow.Activate();

            (_projectContext.ProjectWindow as Shell).CheckForInvalidBranding();
        }

        /// ------------------------------------------------------------------------------------
        private static void HandleErrorOpeningProjectWindow(Exception error, string projectPath)
        {
            if (_projectContext != null)
            {
                if (_projectContext.ProjectWindow != null)
                {
                    _projectContext.ProjectWindow.Closed -= HandleProjectWindowClosed;
                    _projectContext.ProjectWindow.Close();
                }

                _projectContext.Dispose();
                _projectContext = null;
            }

            // FileException is a Bloom exception to capture the filepath. We want to report the inner, original exception.
            Exception originalError = FileException.UnwrapIfFileException(error);
            string errorFilePath = FileException.GetFilePathIfPresent(error);
            Logger.WriteError(
                $"*** Error loading collection {Path.GetFileNameWithoutExtension(projectPath)}, on filepath: {errorFilePath}",
                originalError
            );

            // Normally, NotifyUserOfProblem would take an exception and do this special-exception processing for us.
            // But in this case, we don't pass the exception to NotifyUserOfProblem because we may subsequently end up
            // calling SendReportWithoutUI. Therefore, we must check for the special exception independently.
            if (OneDriveUtils.CheckForAndHandleOneDriveExceptions(error))
            {
                return;
            }

            ErrorResult reportPressedResult = ErrorResult.Yes;
            // NB: I added the email to this directly because, at least on my machine, the old error report dialog had become unworkable
            // because, presumably, I haven't set things up properly with gmail.
            string errorMessage =
                $"Bloom had a problem loading the {Path.GetFileNameWithoutExtension(projectPath)} collection. Click the Report button or just email us at issues@bloomlibrary.org, and we'll help you get things fixed up.";
            var result = SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                new SIL.Reporting.ShowAlwaysPolicy(),
                "Report",
                reportPressedResult,
                errorMessage
            );

            // User clicked the report button.
            if (result == reportPressedResult)
            {
                var userEmail = SIL.Windows.Forms.Registration.Registration.Default.Email;
                if (!String.IsNullOrWhiteSpace(userEmail))
                {
                    // Just send the report in for them.
                    // Include the .bloomCollection file (projectPath)
                    // as well as any other files at the same level, in case they're helpful for debugging
                    var dirName = Path.GetDirectoryName(projectPath);
                    var additionalPathsToInclude = Directory.GetFiles(dirName);
                    _applicationContainer.ProblemReportApi.SendReportWithoutUI(
                        ProblemLevel.kNonFatal,
                        originalError,
                        errorMessage,
                        "",
                        additionalPathsToInclude
                    );
                }
                else
                {
                    // No email... just fallback to the WinFormsErrorReporter, which will allow the user to email us.
                    // Unfortunately, we won't be able to automatically get the .bloomCollection file from that.
                    SIL.Reporting.ErrorReport.ReportNonFatalExceptionWithMessage(
                        originalError,
                        errorMessage
                    );
                }
            }
        }

        /// <summary>
        /// Launches the Collection chooser UI and opens the selected collection.
        /// </summary>
        /// <param name="formToClose">If provided, this form will be closed after choosing a
        /// collection and before opening it. Currently, this is used to close the Shell at the proper
        /// time when switching collectons.</param>
        public static void ChooseACollection(Shell formToClose = null)
        {
            while (true)
            {
                // We decided to stop doing this (BL-1229) since the wizard can feel like part
                // of installation that might be irrevocable.
                ////If it looks like the 1st time, put up the create collection with the welcome.
                ////The user can cancel that if they want to go looking for a collection on disk.
                //if(Settings.Default.MruProjects.Latest == null)
                //{
                //	var path = NewCollectionWizard.CreateNewCollection();
                //	if (!string.IsNullOrEmpty(path) && RobustFile.Exists(path))
                //	{
                //		OpenCollection(path);
                //		return;
                //	}
                //}

                using (var dlg = _applicationContainer.OpenAndCreateCollectionDialog())
                {
                    dlg.StartPosition = FormStartPosition.Manual; // try not to have it under the splash screen
                    dlg.SetDesktopLocation(50, 50);
                    if (dlg.ShowDialog() != DialogResult.OK)
                    {
                        // If there is a form to close, it means the collection chooser is not the only thing open,
                        // and we don't want to exit the application. Otherwise, we are in initial startup and
                        // closing the chooser should exit the application.
                        if (formToClose == null)
                            Application.Exit();
                        return;
                    }

                    if (formToClose != null)
                    {
                        formToClose.UserWantsToOpenADifferentProject = true;
                        formToClose.Close();
                    }

                    if (OpenCollection(dlg.SelectedPath))
                        return;
                }
            }
        }

        private static bool OpenCollection(string path)
        {
            if (OpenProjectWindow(path))
            {
                Settings.Default.MruProjects.AddNewPath(path);
                Settings.Default.Save();
                return true;
            }
            return false;
        }

        /// ------------------------------------------------------------------------------------
        static void HandleProjectWindowClosed(object sender, EventArgs e)
        {
            BloomThreadCancelService.Cancel();

            _projectContext.Dispose();
            _projectContext = null;

            if (((Shell)sender).UserWantsToOpenADifferentProject)
            {
                // On this path, we have already shown the collection chooser,
                // and we are closing the Shell just to open it again with the selected collection.
                // See ChooseACollection().
                return;
            }
            else if (((Shell)sender).UserWantsToOpeReopenProject)
            {
                Application.Idle += new EventHandler(ReopenProject);
            }
            else if (((Shell)sender).QuitForVersionUpdate)
            {
                Application.Exit();
            }
            else
            {
                Application.Exit();
            }
        }

        private static void ReopenProject(object sender, EventArgs e)
        {
            Application.Idle -= ReopenProject;
            OpenCollection(Settings.Default.MruProjects.Latest);
        }

        public static void SetUpLocalization()
        {
            ILocalizationManager lm;
            var installedStringFileFolder =
                FileLocationUtilities.GetDirectoryDistributedWithApplication(true, "localization");
            var productVersion = Application.ProductVersion;
            if (Program.RunningUnitTests)
                productVersion = "1.2.3"; // Prevent invalid product version from being used in unit tests.
            if (installedStringFileFolder == null)
            {
                // nb do NOT try to localize this...it's a shame, but the problem we're reporting is that the localization data is missing!
                var msg =
                    @"Bloom seems to be missing some of the files it needs to run. Please uninstall Bloom, then install it again. If that's doesn't fix things, please contact us by clicking the ""Details"" button below, and we'd be glad to help.";
                ErrorReport.NotifyUserOfProblem(
                    new ApplicationException("Missing localization directory"),
                    msg
                );
                // If the user insists on continuing after that, start up using the built-in English.
                // We need an LM, and it needs some folder of tmx files, though it can be empty. So make a fake one.
                // Ideally we would dispose this at some point, but I don't know when we safely can. Normally this should never happen,
                // so I'm not very worried.
                var fakeLocalDir = new TemporaryFolder("Bloom fake localization").FolderPath;
                lm = LocalizationManager.Create(
                    "en",
                    "Bloom",
                    "Bloom",
                    productVersion,
                    fakeLocalDir,
                    "SIL/Bloom",
                    Resources.BloomIcon,
                    "issues@bloomlibrary.org",
                    //the parameters that follow are namespace beginnings:
                    new string[] { "Bloom" }
                );
                return;
            }

            try
            {
                // If the user has not set the interface language, try to use the system language if we can.
                // (See http://issues.bloomlibrary.org/youtrack/issue/BL-4393.)
                var desiredLanguage = GetDesiredUiLanguage(installedStringFileFolder);
                lm = LocalizationManager.Create(
                    desiredLanguage,
                    "Bloom",
                    "Bloom",
                    productVersion,
                    installedStringFileFolder,
                    "SIL/Bloom",
                    Resources.BloomIcon,
                    "issues@bloomlibrary.org",
                    //the parameters that follow are namespace beginnings:
                    new string[] { "Bloom" }
                );

                //We had a case where someone translated stuff into another language, and sent in their tmx. But their tmx had soaked up a bunch of string
                //from their various templates, which were not Bloom standard templates. So then someone else sitting down to localize bloom would be
                //faced with a bunch of string that made no sense to them, because they don't have those templates.
                //So for now, we only soak up new strings if it's a developer, and hope that the Commit process will be enough for them to realize "oh no, I
                //don't want to check that stuff in".

#if DEBUG
                lm.CollectUpNewStringsDiscoveredDynamically = true;
#else
                lm.CollectUpNewStringsDiscoveredDynamically = false;
#endif

                var uiLanguage = LocalizationManager.UILanguageId; //just feeding this into subsequent creates prevents asking the user twice if the language of their os isn't one we have a tmx for
                if (uiLanguage != desiredLanguage)
                    Settings.Default.UserInterfaceLanguageSetExplicitly = true;

                LocalizationManager.Create(
                    uiLanguage,
                    "Palaso",
                    "Palaso", /*review: this is just bloom's version*/
                    productVersion,
                    installedStringFileFolder,
                    "SIL/Bloom",
                    Resources.BloomIcon,
                    "issues@bloomlibrary.org",
                    new string[] { "SIL" }
                );

                LocalizationManager.Create(
                    uiLanguage,
                    "BloomMediumPriority",
                    "BloomMediumPriority",
                    productVersion,
                    installedStringFileFolder,
                    "SIL/Bloom",
                    Resources.BloomIcon,
                    "issues@bloomlibrary.org",
                    new string[] { "Bloom" }
                );

                LocalizationManager.Create(
                    uiLanguage,
                    "BloomLowPriority",
                    "BloomLowPriority",
                    productVersion,
                    installedStringFileFolder,
                    "SIL/Bloom",
                    Resources.BloomIcon,
                    "issues@bloomlibrary.org",
                    new string[] { "Bloom" }
                );

                Settings.Default.UserInterfaceLanguage = LocalizationManager.UILanguageId;

                // Per BL-6449, these two languages should try Spanish before English if a localization is missing.
                // (If they ever get localized enough to show up in our list.)
                // The full names are Mam and K'iche'.
                if (
                    LocalizationManager.UILanguageId == "mam"
                    || LocalizationManager.UILanguageId == "quc"
                )
                {
                    LocalizationManager.FallbackLanguageIds = new[] { "es", "en" };
                }

                // If this is removed, change code in WorkspaceView.OnSettingsProtectionChanged
                LocalizationManager.EnableClickingOnControlToBringUpLocalizationDialog = false; // BL-5111

                // It's now safe to read the localized strings.  See BL-13245.
                HtmlErrorReporter.Instance.LocalizeDefaultReportLabel();
            }
            catch (Exception error)
            {
                //handle http://jira.palaso.org/issues/browse/BL-213
                if (GetRunningBloomProcessCount() > 1)
                {
                    ErrorReport.NotifyUserOfProblem(
                        "Whoops. There is another copy of Bloom already running while Bloom was trying to set up L10NSharp."
                    );
                    Environment.FailFast("Bloom couldn't set up localization");
                }

                if (error.Message.Contains("Bloom.en.tmx"))
                {
                    ErrorReport.NotifyUserOfProblem(
                        error,
                        "Sorry. Bloom is trying to set up your machine to use this new version, but something went wrong getting at the file it needs. If you restart your computer, all will be well."
                    );

                    Environment.FailFast("Bloom couldn't set up localization");
                }

                //otherwise, we don't know what caused it.
                throw;
            }
        }

        /// <summary>
        /// Derive the desired UI language from the stored value, or from matching the OS value against
        /// the available localizations if nothing has been explicitly stored yet.
        /// </summary>
        /// <remarks>
        /// See http://issues.bloomlibrary.org/youtrack/issue/BL-4393.
        /// </remarks>
        private static string GetDesiredUiLanguage(string installedStringFileFolder)
        {
            var desiredLanguage = Settings.Default.UserInterfaceLanguage;
            if (
                String.IsNullOrEmpty(desiredLanguage)
                || !Settings.Default.UserInterfaceLanguageSetExplicitly
            )
            {
                // Nothing has been explicitly selected by the user yet, so try to get a localization for the system language.
                // First try for an exact match, or failing that a localization with the same language.
                // (This is motivated by the Chinese localization which currently specifies zh-CN. LM might eventually
                // support multiple variants for a single language like zh-CN or zh-Hans, at which point both might be in the
                // list and we'd want the best match. In the meantime we want zh-Hans to find zh-CN. See BL-3691.)
                string localeMatchingLanguage = null;
                foreach (var subdir in Directory.EnumerateDirectories(installedStringFileFolder))
                {
                    // The LocalizationManager has not been initialized yet when this method is called.
                    // (Indeed, the output of this method is used to initialize the LocalizationManager!)
                    // So we need to scan the disk ourselves to see what is available.  Unfortunately, the
                    // subdirectory names in the given folder are not sufficient because some localizations
                    // are more specific than the directory name.  For example, Spanish uses "es" for the
                    // subdirectory name, but the actual language tags inside the files are "es-ES".
                    // (Perhaps matching against the subdirectory name would be sufficient, but maybe
                    // someday a case will arise where the country/script name is significant in some but
                    // not all cases.)
                    var dirTag = Path.GetFileName(subdir);
                    if (IsSameActualLanguage(dirTag, UserInterfaceCulture.IetfLanguageTag))
                    {
                        var xliffPath = Path.Combine(subdir, "Bloom.xlf");
                        if (RobustFile.Exists(xliffPath))
                        {
                            var doc = SafeXmlDocument.Create();
                            doc.Load(xliffPath);
                            var fileNode = doc.DocumentElement.SelectSingleNodeHonoringDefaultNS(
                                "/xliff/file"
                            );
                            if (fileNode != null)
                            {
                                var target = fileNode.GetOptionalStringAttribute(
                                    "target-language",
                                    null
                                );
                                if (!string.IsNullOrEmpty(target))
                                {
                                    if (target == UserInterfaceCulture.IetfLanguageTag)
                                        return target;
                                    // We don't have an exact match, but we do have a match that should work.  Remember
                                    // it, but hold out (fading) hope for an exact match and don't quit the loop.
                                    if (localeMatchingLanguage == null)
                                        localeMatchingLanguage = target;
                                }
                            }
                        }
                    }
                }
                // No exact match; did we find one that is at least the right language? If so use that.
                if (localeMatchingLanguage != null)
                    return localeMatchingLanguage;
                // If exact matches fail, return the base language tag.  If that doesn't match in the LM.Create method,
                // then L10NSharp will prompt the user to select one of the available localizations.
                return UserInterfaceCulture.TwoLetterISOLanguageName;
            }
            return desiredLanguage;
        }

        /// <summary>
        /// Test whether two language codes refer to the same language, ignoring any country, script, or variant tagging.
        /// </summary>
        private static bool IsSameActualLanguage(string code1, string code2)
        {
            if (code1 == code2)
                return true;
            if (string.IsNullOrEmpty(code1) || string.IsNullOrEmpty(code2))
                return false;
            var codeTags1 = code1.Split('-');
            var codeTags2 = code2.Split('-');
            return (codeTags1[0] == codeTags2[0]);
        }

        private static bool _errorHandlingHasBeenSetUp;
        private static IDisposable _sentry;

        /// ------------------------------------------------------------------------------------
        internal static void SetUpErrorHandling()
        {
            if (_errorHandlingHasBeenSetUp)
                return;

            if (!ApplicationUpdateSupport.IsDev && !Program.RunningUnitTests)
            {
                try
                {
                    _sentry = SentrySdk.Init(
                        "https://bba22972ad6b4c2ab03a056f549cc23d@o1009031.ingest.sentry.io/5983534"
                    );
                    SentrySdk.ConfigureScope(scope =>
                    {
                        scope.SetExtra("channel", ApplicationUpdateSupport.ChannelName);
                        scope.SetExtra("version", ErrorReport.VersionNumberString);
                        scope.User = new User
                        {
                            Email = SIL.Windows.Forms.Registration.Registration.Default.Email,
                            Username = SIL.Windows
                                .Forms
                                .Registration
                                .Registration
                                .Default
                                .FirstName,
                            //Other = {Organization:SIL.Windows.Forms.Registration.Registration.Default.Organization}
                        };
                        scope.User.Other.Add(
                            "Organization",
                            SIL.Windows.Forms.Registration.Registration.Default.Organization
                        );
                    });
                }
                catch (Exception err)
                {
                    Debug.Fail(err.Message);
                }
            }

            var orderedReporters = new IBloomErrorReporter[]
            {
                SentryErrorReporter.Instance,
                HtmlErrorReporter.Instance
            };
            var htmlAndSentryReporter = new CompositeErrorReporter(
                orderedReporters,
                primaryReporter: HtmlErrorReporter.Instance
            );
            ErrorReport.SetErrorReporter(htmlAndSentryReporter);

            var msgTemplate =
                @"If you don't care who reads your bug report, you can skip this notice.

When you submit a crash report or other issue, the contents of your email go in our issue tracking system, ""YouTrack"", which is available via the web at {0}. This is the normal way to handle issues in an open-source project.

Our issue-tracking system is searchable by anyone. Search engines (like Google) should not search it, so someone searching with Google should not see your report, but we can't promise this for all search engines.

Anyone looking specifically at our issue tracking system can read what you sent us. So if you have something private to say, please send it to one of the developers privately with a note that you don't want the issue in our issue tracking system. If need be, we'll make some kind of sanitized place-holder for your issue so that we don't lose it.
";
            // To save startup time, we'll initially set the privacy notice based on our fallback version of this URL,
            // then get the real one in the background (which will also make other URLs available faster when needed).
            string issueTrackingUrl = UrlLookup.LookupUrl(
                UrlType.IssueTrackingSystem,
                acceptFinalUrl: (realUrl) =>
                {
                    ExceptionReportingDialog.PrivacyNotice = string.Format(msgTemplate, realUrl);
                }
            );

            ExceptionReportingDialog.PrivacyNotice = string.Format(msgTemplate, issueTrackingUrl);
            SIL.Reporting.ErrorReport.EmailAddress = "issues@bloomlibrary.org";
            SIL.Reporting.ErrorReport.AddStandardProperties();
            // with squirrel, the file's dates only reflect when they were installed, so we override this version thing which
            // normally would include a bogus "Apparently Built On" date:
            var versionNumber = Program.RunningUnitTests
                ? "Current build" // for some reason VersionNumberString throws when running unit tests, so just use this.
                : ErrorReport.VersionNumberString;
            ErrorReport.Properties["Version"] =
                versionNumber + " " + ApplicationUpdateSupport.ChannelName;
            SIL.Reporting.ExceptionHandler.Init(new FatalExceptionHandler());

            ExceptionHandler.AddDelegate(
                (w, e) => DesktopAnalytics.Analytics.ReportException(e.Exception)
            );
            if (!ApplicationUpdateSupport.IsDev)
            {
                ExceptionHandler.AddDelegate(
                    (w, e) =>
                    {
                        NonFatalProblem.ReportSentryOnly(e.Exception);
                    }
                );
            }
            _errorHandlingHasBeenSetUp = true;
        }

        public static void OldVersionCheck()
        {
            return;
        }

        /// <summary>
        /// Creates mime types and file associations on developer machine
        /// </summary>
        private static void CheckLinuxFileAssociations()
        {
            if (!SIL.PlatformUtilities.Platform.IsLinux)
                return;

            // on Linux, Environment.SpecialFolder.LocalApplicationData defaults to ~/.local/share
            var shareDir = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );
            var mimeDir = Path.Combine(shareDir, "mime", "packages"); // check for mime-type files in ~/.local/share/mime/packages
            var imageDir = Path.Combine(shareDir, "icons", "hicolor", "48x48", "apps"); // check for mime-type icons in ~/.local/share/icons/hicolor/48x48/apps

            // make sure target directories exist
            if (!Directory.Exists(mimeDir))
                Directory.CreateDirectory(mimeDir);

            if (!Directory.Exists(imageDir))
                Directory.CreateDirectory(imageDir);

            // list of files to copy
            var updateNeeded = false;
            var filesToCheck = new System.Collections.Generic.Dictionary<string, string>
            {
                {
                    "bloom-collection.sharedmimeinfo",
                    Path.Combine(mimeDir, "application-bloom-collection.xml")
                },
                {
                    "bloom-collection.png",
                    Path.Combine(imageDir, "application-bloom-collection.png")
                }
            }; // Dictionary<sourceFileName, destinationFullFileName>

            // check each file now
            var sourceDir = FileLocationUtilities.DirectoryOfApplicationOrSolution;
            foreach (var entry in filesToCheck)
            {
                var destFile = entry.Value;
                if (!RobustFile.Exists(destFile))
                {
                    var sourceFile = Path.Combine(sourceDir, "debian", entry.Key);
                    if (RobustFile.Exists(sourceFile))
                    {
                        updateNeeded = true;
                        RobustFile.Copy(sourceFile, destFile);
                    }
                }
            }

            if (!updateNeeded)
                return;

            // if there were changes, notify the system
            // The new process isn't affected by the culture, and chains to additional processes
            // that aren't affected by the culture setting.  Since we use events to chain the
            // processes, we can't use the CommandLineRunner methods.
            var proc = new Process
            {
                StartInfo =
                {
                    FileName = "update-desktop-database",
                    Arguments = Path.Combine(shareDir, "applications"),
                    UseShellExecute = false
                },
                EnableRaisingEvents = true // so we can run another process when this one finishes
            };

            // after the desktop database is updated, update the mime database
            proc.Exited += (sender, eventArgs) =>
            {
                var proc2 = new Process
                {
                    StartInfo =
                    {
                        FileName = "update-mime-database",
                        Arguments = Path.Combine(shareDir, "mime"),
                        UseShellExecute = false
                    },
                    EnableRaisingEvents = true // so we can run another process when this one finishes
                };

                // after the mime database is updated, set the file association
                proc2.Exited += (sender2, eventArgs2) =>
                {
                    var proc3 = new Process
                    {
                        StartInfo =
                        {
                            FileName = "xdg-mime",
                            Arguments = "default bloom.desktop application/bloom-collection",
                            UseShellExecute = false
                        }
                    };

                    Debug.Print("Setting file association");
                    proc3.Start();
                };

                Debug.Print("Executing update-mime-database");
                proc2.Start();
            };

            Debug.Print("Executing update-desktop-database");
            proc.Start();
        }

        /// <summary>
        /// Getting the count of running Bloom instances takes extra steps on Linux.
        /// </summary>
        /// <returns>The number of running Bloom instances</returns>
        public static int GetRunningBloomProcessCount()
        {
            if (SIL.PlatformUtilities.Platform.IsWindows)
            {
                // This is your count on Windows.
                return Process.GetProcesses().Count(p => IsBloomProcess(p));
                ;
            }
            // On Linux, the process name is usually "mono-sgen" or something similar, but not all processes
            // with this name are instances of Bloom.
            var processes = Process.GetProcesses().Where(p => IsMonoProcess(p));
            // DO NOT change this foreach loop into a LINQ expression. It takes longer to complete if you do.
            var bloomProcessCount = 0;
            foreach (var p in processes)
            {
                try
                {
                    if (p.Modules.Cast<ProcessModule>().Any(m => m.ModuleName == "Bloom.exe"))
                        ++bloomProcessCount;
                }
                catch (System.InvalidOperationException) { }
            }
            return bloomProcessCount;
        }

        public static bool IsBloomProcess(Process process)
        {
            try
            {
                var name = process.ProcessName.ToLowerInvariant();
                // The second test prevents counting the Bloom.vshost.exe process which Visual Studio and similar tools
                // create to speed up launching the program in debug mode. It's only useful for developers.
                return name.Contains("bloom")
                    && !name.Contains("vshost")
                    && !name.Contains("bloomharvester");
            }
            catch (System.InvalidOperationException)
            {
                // process is already exited?
                return false;
            }
        }

        public static bool IsMonoProcess(Process process)
        {
            try
            {
                return process.ProcessName.ToLowerInvariant().StartsWith("mono");
            }
            catch (System.InvalidOperationException)
            {
                // process is already exited?
                return false;
            }
        }

        public static BloomFileLocator OptimizedFileLocator
        {
            get { return _projectContext.OptimizedFileLocator; }
        }

        private static void CheckForCorruptUserConfig()
        {
            //First check the user.config we get through using the palaso stuff.  This is the one in a folder with a name like Bloom/3.5.0.0
            var palasoSettings = new SIL.Settings.CrossPlatformSettingsProvider();
            palasoSettings.Initialize(null, null);
            var error = palasoSettings.CheckForErrorsInSettingsFile();
            if (error != null)
            {
                //Note: this is probably too early to do anything more complicated that writing to a log...
                //Enhance: we might be able to do a MessageBox.Show(), but it would be better to save this error
                //and inform the user later when the UI can interact with them.
                Logger.WriteEvent("error reading palaso user config: " + error.Message);
                Logger.WriteEvent("Should self-heal");
            }

            //Now check the plain .net user.config we also use (sigh). This is the one in a folder with a name like Bloom.exe_Url_avygitvf1lws5lpjrmoh5j0ggsx4tkj0

            //roughly from http://stackoverflow.com/questions/9572243/what-causes-user-config-to-empty-and-how-do-i-restore-without-restarting
            try
            {
                ConfigurationManager.OpenExeConfiguration(
                    ConfigurationUserLevel.PerUserRoamingAndLocal
                );
            }
            catch (ConfigurationErrorsException ex)
            {
                Logger.WriteEvent("Cannot open user config file " + ex.Filename);
                Logger.WriteEvent(ex.Message);

                if (RobustFile.Exists(ex.Filename))
                {
                    Logger.WriteEvent(
                        "Config file content:\n{0}",
                        RobustFile.ReadAllText(ex.Filename)
                    );
                    Logger.WriteEvent("Deleting " + ex.Filename);
                    RobustFile.Delete(ex.Filename);
                    Properties.Settings.Default.Upgrade();
                    // Properties.Settings.Default.Reload();
                    // you could optionally restart the app instead
                }
                else
                {
                    Logger.WriteEvent("Config file {0} does not exist", ex.Filename);
                }
            }
        }

        public static bool RunningUnitTests
        {
            get { return Assembly.GetEntryAssembly() == null; }
        }

        // Set to true when Bloom is running one of the command line verbs, e.g. hydrate or createArtifacts
        public static bool RunningInConsoleMode { get; set; }

        // Should be set to true if this is being called by Harvester, false otherwise.
        public static bool RunningHarvesterMode { get; set; }

        // Show UI for development and testing which isn't shown to the user.
        // e.g. the gfx/wv2 labels and the experimental feature checkbox for wv2.
        // We may end up switching this on based on an environment variable.
        public static bool ShowDevelopmentOnlyUI
        {
            get { return ApplicationUpdateSupport.IsDevOrAlpha; }
        }
    }
}
