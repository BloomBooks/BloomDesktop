using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.Properties;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;
using Newtonsoft.Json;
using SIL.Extensions;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Miscellaneous;

namespace Bloom
{
    public partial class WebView2Browser : Browser
    {
        public static int? RemoteDebuggingPort =>
            BloomServer.portForHttp > 0 ? BloomServer.RemoteDebuggingPort : (int?)null;

        public static string AlternativeWebView2Path;
        private bool _readyToNavigate;

        // Exposes whether the (async) CoreWebView2 environment initialization has finished. Lets callers
        // measure how long that init takes separately from the subsequent navigation (see BookProcessor).
        public override bool IsReadyToNavigate => _readyToNavigate;
        private PasteCommand _pasteCommand;
        private CopyCommand _copyCommand;
        private UndoCommand _undoCommand;
        private CutCommand _cutCommand;
        private bool _inDisposeMethod;
        private bool _isBuiltInBrowserZoomEnabled = true;

        // All our existing code assumes we can just construct a browser. And it seems to work.
        // But in some newer code involving awaits and multiple browsers in unit tests, we
        // really need to properly await InitWebView. The dummy argument is just so we can force this
        // constructor to be called in those cases, while still allowing the default constructor
        // to work the old way.
        private WebView2Browser(string dummy) { }

        // When set (via CreateWithInjectedEnvironment), InitWebView uses this already-created environment
        // instead of making its own. Lets OffScreenBrowser share one environment — one browser process,
        // user-data folder, and HTTP cache — across the fresh browser it makes per page, thread-safely and
        // without the global shared-environment batch statics.
        private CoreWebView2Environment _injectedEnvironment;

        /// <summary>
        /// Create a browser that reuses the given already-created CoreWebView2Environment instead of making its
        /// own. As with the default constructor, initialization is kicked off unawaited; callers wait on
        /// <see cref="IsReadyToNavigate"/> before navigating.
        /// </summary>
        internal static WebView2Browser CreateWithInjectedEnvironment(
            CoreWebView2Environment environment
        )
        {
            // The string argument selects the do-nothing private constructor so we can set the injected
            // environment before InitWebView runs, then initialize exactly as the normal constructor does.
            var browser = new WebView2Browser("dummy");
            browser._injectedEnvironment = environment;
            browser.InitializeComponent();
            // Kicked off unawaited (like the default constructor); callers wait on IsReadyToNavigate.
            _ = browser.InitWebView();
            return browser;
        }

        /// <summary>
        /// The CoreWebView2Environment this browser was initialized with, or null if it is not yet ready.
        /// A caller can capture this from one browser and pass it to CreateWithInjectedEnvironment to make
        /// further browsers that share the same environment.
        /// </summary>
        internal CoreWebView2Environment CoreEnvironment => _webview?.CoreWebView2?.Environment;

        /// <summary>
        /// This gives us a way to create a WebView2Browser with the async init properly awaited.
        /// It would be good to use this everywhere, but I think a lot of stuff would have to become async.
        /// </summary>
        /// <returns></returns>
        public static async Task<WebView2Browser> CreateAsync()
        {
            // the argument forces it to use the special private constructor that does nothing.
            var browser = new WebView2Browser("dummy");
            // The rest of this should match the other constructor, except we can await InitWebView
            // (or anything else we need to).
            browser.InitializeComponent();
            await browser.InitWebView();
            return browser;
        }

        public WebView2Browser()
        {
            InitializeComponent();

            // This should be awaited, but we can't use await in a constructor. If possible, use CreateAsync.
            // Otherwise, this method will return a new browser, and eventually it will get initialized, but
            // EnsureCoreWebView2Async may not have completed, so the CoreWebView2 property may not be set yet,
            // and the CoreWebView2InitializationCompleted event may not have been raised yet, so all kinds of
            // stuff is not set up.
            // We work around this by not setting _readyToNavigate to true until the event IS raised,
            // and having various things call EnsureBrowserReadyToNavigate which waits until that is true.
            // This is ugly, but so is using async code all over the place. (Not only does every caller of an
            // async method have to change its signature and be awaited, but also we can't use ref or out
            // parameters, and we can't hold a lock while awaiting, because the continuation may take place
            // in a different thread. This is a problem for most of our API handlers.
            // Moreover, async code is reentrant: user events like handling a mouse click could
            // happen between awaiting something and continuing the method, and even a lock won't prevent it;
            // the reentrancy happens on the main thread.
            // Moreover anyc code depends on pumping events in the main message loop, and if it needs to invoke
            // something on that thread, it's really easy to deadlock.
            // A lot of this is because of the way that WinForms works, and when we finally get away from WinForms,
            // we may be able to get to an environment where we don't need to try so hard to avoid async.)
            _ = InitWebView();
        }

        private void SetupEventHandling()
        {
            _webview.CoreWebView2InitializationCompleted += (
                object sender,
                CoreWebView2InitializationCompletedEventArgs args
            ) =>
            {
                if (Disposing || _inDisposeMethod)
                    return; // disposed before initialization completed.  See BL-13593 and BL-11384.
                if (args.IsSuccess == false)
                {
                    // One way to get this to fail is to have a zombie Bloom running that has different "accept-lang" arguments.
                    // enhance: how to show using the winforms error dialog?
                    MessageBox.Show(
                        $"Bloom was unable to initialize the WebView2 browser. Please see https://docs.bloomlibrary.org/wv2trouble. \r\n\r\n{args.InitializationException.Message}\r\n{args.InitializationException.ToString()}\r\n{args.InitializationException.StackTrace}",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    // hard exit
                    Environment.Exit(1);
                }
                try
                {
                    Logger.WriteEvent(
                        $"Initialized a WebView2 (version {_webview.CoreWebView2.Environment.BrowserVersionString}) with UserDataFolder=\"{_webview.CoreWebView2.Environment.UserDataFolder}\""
                    );

                    // prevent the browser from opening external links, by intercepting NavigationStarting
                    _webview.CoreWebView2.NavigationStarting += (
                        object sender1,
                        CoreWebView2NavigationStartingEventArgs args1
                    ) =>
                    {
                        if (
                            args1.Uri.StartsWith("http")
                            && !args1.Uri.StartsWith("http://localhost")
                        )
                        {
                            args1.Cancel = true;
                            ToPalaso.ProcessExtra.SafeStartInFront(args1.Uri);
                        }
                    };
                    _webview.CoreWebView2.NavigationCompleted += (
                        object sender2,
                        CoreWebView2NavigationCompletedEventArgs args2
                    ) =>
                    {
                        RaiseDocumentCompleted(sender2, args2);
                    };
                }
                catch (Exception)
                {
                    // enhance: how to show using the winforms error dialog?
                    MessageBox.Show(
                        "Bloom was unable to initialize the WebView2 browser. Please see https://docs.bloomlibrary.org/wv2trouble",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    // hard exit
                    Environment.Exit(1);
                }
                // Prevent the browser from opening external links in iframes by intercepting FrameNavigationStarting events.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-13316.  Old versions of Bloom would open a new browser
                // window for external links the same as this does.
                _webview.CoreWebView2.FrameNavigationStarting += (
                    object sender,
                    CoreWebView2NavigationStartingEventArgs args
                ) =>
                {
                    if (args.Uri.StartsWith("http") && !args.Uri.StartsWith("http://localhost"))
                    {
                        args.Cancel = true;
                        ToPalaso.ProcessExtra.SafeStartInFront(args.Uri);
                    }
                };
                _webview.CoreWebView2.FrameNavigationCompleted += (o, eventArgs) =>
                {
                    RaiseDocumentCompleted(o, eventArgs);
                };
                // We thought we might need something like this to tell WebView2 to open pages in the system browser
                // rather than a new WebView2 window. But ExternalLinkController.HandleLink() does what we want if we
                // hook things up correctly on the typescript side (see hookupLinkHandler in linkHandler.ts).
                //_webview.CoreWebView2.NewWindowRequested += (object sender3, CoreWebView2NewWindowRequestedEventArgs eventArgs) =>
                //{
                //	if (eventArgs.Uri.StartsWith("https://"))
                //	{
                //		eventArgs.Handled = true;
                //		ProcessExtra.SafeStartInFront(eventArgs.Uri);
                //	}
                //};
                _webview.CoreWebView2.ContextMenuRequested += ContextMenuRequested;

                // This is only really needed for the print tab. But it is harmless elsewhere.
                // It removes some unwanted controls from the toolbar that WebView2 inserts when
                // previewing a PDF file.
                _webview.CoreWebView2.Settings.HiddenPdfToolbarItems =
                    CoreWebView2PdfToolbarItems.Print // we prefer our big print button, and it may show a dialog first
                    | CoreWebView2PdfToolbarItems.Rotate // shouldn't be needed, just clutter
                    | CoreWebView2PdfToolbarItems.Save // would always be disabled, there's no known place to save
                    | CoreWebView2PdfToolbarItems.SaveAs // We want our Save code, which checks things like not saving in the book folder
                    | CoreWebView2PdfToolbarItems.FullScreen // doesn't work right and is hard to recover from
                    | CoreWebView2PdfToolbarItems.MoreSettings; // none of its functions seem useful

                _webview.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _webview.CoreWebView2.Settings.IsWebMessageEnabled = true;
                _webview.CoreWebView2.Settings.IsZoomControlEnabled = _isBuiltInBrowserZoomEnabled;
                // Disable swipe navigation, which is a problem on trackpads (and touch screens). See BL-12405.
                _webview.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;

                // Based on https://github.com/MicrosoftEdge/WebView2Feedback/issues/308,
                // this attempts to prevent Bloom asking permission to read the clipboard
                // the first time the user does a paste. I can't test it, because I don't know
                // how to revoke that permission.
                _webview.CoreWebView2.PermissionRequested += (o, e) =>
                {
                    if (e.PermissionKind == CoreWebView2PermissionKind.ClipboardRead)
                        e.State = CoreWebView2PermissionState.Allow;
                };
                _readyToNavigate = true;
            };
        }

        public override void SetBuiltInBrowserZoomEnabled(bool enabled)
        {
            _isBuiltInBrowserZoomEnabled = enabled;
            if (_webview?.CoreWebView2 != null)
            {
                _webview.CoreWebView2.Settings.IsZoomControlEnabled = enabled;
            }
        }

        private void ContextMenuRequested(
            object sender,
            CoreWebView2ContextMenuRequestedEventArgs e
        )
        {
            if (ReplaceContextMenu != null)
            {
                // If the user is holding down ctrl, we want to show the developer menu, so don't override.
                if (!ModifierKeys.HasFlag(Keys.Control))
                {
                    e.Handled = true;
                    ReplaceContextMenu();
                    return;
                }
            }

            var wantDebug = WantDebugMenuItems;
            // Remove built-in items (except "Inspect" and "Refresh", if we're in a debugging context)
            var menuList = e.MenuItems;
            for (int index = 0; index < menuList.Count; )
            {
                if (
                    wantDebug
                    && new string[] { "inspectElement", "reload" }.Contains(menuList[index].Name)
                )
                {
                    index++;
                    continue;
                }
                menuList.RemoveAt(index);
            }
            AdjustContextMenu(new WebViewItemAdder(_webview, menuList));
        }

        private static bool _clearedCache;
        private static string _uiLanguageOfThisRun;
        private static bool _alreadyOpenedAWebView2Instance;

        static int dataFolderCounter = 0;

        // When set (via BeginSharedEnvironmentBatch), all WebView2 browsers created during the batch
        // share ONE CoreWebView2Environment — i.e. one browser process, one user-data folder, and one
        // HTTP cache — instead of each creating its own. The first browser of the batch creates the
        // environment; the rest reuse it. Each browser still gets its own fresh CoreWebView2 control
        // (fresh renderer), so this does NOT reintroduce the single-control reuse-wedge. Used by
        // BookProcessor's off-screen per-page fix-up to avoid paying environment creation per page.
        //
        // These statics assume the batch is UI-thread-only (which it is: all browser construction is
        // marshalled to the UI thread, and BookProcessor drives the batch there). If another browser
        // happens to be created during the batch (e.g. a thumbnail), it harmlessly joins the shared
        // environment. They are NOT a mechanism for concurrent batches.
        private static bool _useSharedEnvironment;
        private static CoreWebView2Environment _sharedEnvironment;

        public static void BeginSharedEnvironmentBatch()
        {
            AssertSharedEnvironmentStaticsAreUiThreadOnly();
            _useSharedEnvironment = true;
            _sharedEnvironment = null;
        }

        public static void EndSharedEnvironmentBatch()
        {
            AssertSharedEnvironmentStaticsAreUiThreadOnly();
            _useSharedEnvironment = false;
            // Drop our reference; the underlying browser process/profile is released once the last
            // CoreWebView2 using this environment is disposed. (CoreWebView2Environment is not IDisposable.)
            _sharedEnvironment = null;
        }

        // The shared-environment statics above have no synchronization; they are safe only because the
        // batch is UI-thread-only (see the comment on _useSharedEnvironment). Assert that assumption so
        // any future code that drives a batch off the UI thread trips here instead of silently corrupting
        // state. Unit-test/console modes are exempt, as elsewhere in this file.
        private static void AssertSharedEnvironmentStaticsAreUiThreadOnly()
        {
            Debug.Assert(
                Program.RunningOnUiThread
                    || Program.RunningUnitTests
                    || Program.RunningInConsoleMode,
                "Shared WebView2 environment batch must be driven on the UI thread (these statics are unsynchronized)"
            );
        }

        private async Task InitWebView()
        {
            // based on https://stackoverflow.com/questions/63404822/how-to-disable-cors-in-wpf-webview2
            // this should disable CORS, but it doesn't seem to work, at least for fixing communication from
            // an iframe in one domain to a parent in another. Keeping in case I need to try further.
            // However, the reason I thought I needed to disable it was a problem that sourced the root
            // HTML document in edit mode from the wrong domain; we may not need this at all.
            //var op = new CoreWebView2EnvironmentOptions("--allow-insecure-localhost --disable-web-security");
            //var env = await CoreWebView2Environment.CreateAsync(null, null, op);
            //await _webview.EnsureCoreWebView2Async(env);
            // We played with this also when it seemed that the only way to record a video might be to
            // disable the gpu. It didn't work; not sure whether because using the GPU wasn't the
            // problem, or because I still haven't figured out how to make this API actually work,
            // or because that specific option is not supported in WebView2.
            //var op = new CoreWebView2EnvironmentOptions("--disable-gpu");
            //var env = await CoreWebView2Environment.CreateAsync(null, null, op);
            //await _webview.EnsureCoreWebView2Async(env);
            // Setting the UI language in the second parameter ought to work, but it doesn't.
            // In the meantime, setting the "accept-lang" additional browser switch does work.
            // Unfortunately, this setting cannot be changed "on the fly", so Bloom will need to be restarted before
            // a change in UI language will take effect at this level.
            // See https://github.com/MicrosoftEdge/WebView2Feedback/issues/3635 (which was just opened last week!)
            var additionalBrowserArgs = "--autoplay-policy=no-user-gesture-required";
            // Disables sleeping tabs and keeps rendering active.
            // These were suggested by AI to prevent problems when the computer sleeps or is idle for a long
            // time while Bloom is open. Apparently by default WebView2 will put tabs to sleep after a while,
            // and become suspicious of some URLs when restarting. The hope is that this will help with BL-16363
            // and similar problems. The AI also suggested --allow-insecure-localhost, but this is not relevant
            // since we are not using https: to access our server.
            // Not sleeping potentially allows computation to continue and drain battery, but we don't
            // expect this to be an important factor for Bloom, as we don't have long-running
            // animations except for things like playing motion books, which probably want to continue to the end.
            additionalBrowserArgs += " --disable-renderer-backgrounding";
            // Chromium only respects the last --disable-features argument, so we collect all features
            // here and emit a single comma-separated value at the end.
            var featuresToDisable = new List<string>
            {
                "msSleepingTabs", // prevent WebView2 from putting tabs to sleep after idle (BL-16363)
            };

            // WebView2 can fail to initialize if we try to open a new one with different `--accept-lang` arguments.
            // This even happens if it is a different copy of Bloom running. Our hypothesis is that this is because they are sharing
            // the same user data folder. That is super rare, but expensive when it happens and the dev or user doesn't know why.
            // Therefore,
            // 1) we will only set the UI language of the browser once per run of Bloom. As a result, we don't get to pass on the UI language to the
            // browser until the next run, ah well. So things like full-stop vs. comma in numbers will be wrong until then.
            // 2) we will use a different folder name for each language (to prevent collisions between running Blooms).

            if (!_alreadyOpenedAWebView2Instance)
            {
                _alreadyOpenedAWebView2Instance = true;
                _uiLanguageOfThisRun = Settings.Default.UserInterfaceLanguage;
            }
            if (!string.IsNullOrEmpty(_uiLanguageOfThisRun))
            {
                additionalBrowserArgs += " --accept-lang=" + _uiLanguageOfThisRun;
            }
            if (RemoteDebuggingPort.HasValue && !Program.RunningUnitTests)
            {
                // Expose a CDP endpoint so Playwright and other automation can attach to the real Bloom WebView2 surface.
                // NOT in unit tests: nothing attaches to a test-run WebView2, and on a busy CI machine the port
                // (BloomServer's http port + 2) can already be held by a real Bloom instance running beside the
                // tests, in which case Chromium's fight over the port breaks WebView2 startup and fails the test
                // (seen as flaky BookUploadAndDownloadTests upload failures on agents also running Bloom automation).
                additionalBrowserArgs += $" --remote-debugging-port={RemoteDebuggingPort.Value} "; // allow external inspector connect
            }
            if (featuresToDisable.Count > 0)
                additionalBrowserArgs +=
                    " --disable-features=" + string.Join(",", featuresToDisable);

            var op = new CoreWebView2EnvironmentOptions(additionalBrowserArgs);

            // In 5.5 time period, John Hatton kept getting into a situation where no version of Bloom would run,
            // and even a "Hello World" of WV2 would not run.
            // One hypothesis was that this was caused by an update to WV2, as it seemed to coincide, and also the
            // problem going away seemed to coincide. This could happen to a user too. A workaround
            // is to point to the WV2 in edge using an environment variable.
            // THIS IS DESCRIBED in the troubleshooting documentation at https://docs.bloomlibrary.org/wv2trouble,
            // so if you change it here, change the instructions there.
            AlternativeWebView2Path = Environment.GetEnvironmentVariable("BloomWV2Path");

            if (!string.IsNullOrEmpty(AlternativeWebView2Path))
            {
                if (AlternativeWebView2Path.ToLower() == "edge")
                {
                    AlternativeWebView2Path = GetEdgeInstallationPath();
                }

                if (!Directory.Exists(AlternativeWebView2Path))
                {
                    MessageBox.Show(
                        AlternativeWebView2Path
                            + " does not exist anymore. Please remove or update the environment variable 'BloomWV2Path' to point to a valid folder.",
                        "BloomWV2Path is invalid",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    MessageBox.Show(
                        "Bloom will now attempt with the default path (the WebView2 Evergreen Runtime"
                    );
                    AlternativeWebView2Path = null;
                }
                Bloom.ErrorReporter.BloomErrorReport.NotifyUserUnobtrusively(
                    "Using alternate WebView2 path: " + AlternativeWebView2Path,
                    ""
                );
            }

            // I suspect that some situations may require the user deleting this folder to get things working again.
            // Normally, it seems to get deleted automatically when we exit. But if we crash, it may not.
            // Apparently if more than one instance of Bloom is running, they must use different folders, or WV2 will fail to initialize
            // We tried adding the language code like this, but it died in tests
            // var dataFolder = Path.Combine(Path.GetTempPath(), "Bloom WebView2 "+_uiLanguageOfThisRun).Trim();
            // Now using the port number. This should be unique to each running instance of Bloom, and should be the
            // same for all browsers in a given instance. I also shortened the name, in case that was the cause of the previous problem.
            // This is a better strategy than language name, as it should give each instance its own folder, even if the language is the same.
            // And it should always be a short name using simple ASCII characters, which might help.
            // I don't think this is ever called before the server chooses a port, but just in case, I'm providing a default
            // that won't match any port we actually use.
            // We had some problems that seemed to possibly related to the folder being left in a bad state, or
            // different instances of WebView2 using the same one, so we decided to make sure it is uniqiue.
            // Enhance: it might be a good thing to try to delete this folder if we find it already exists (on a background thread).
            // For now we'll just keep incrementing until we find an available folder.
            // This sets up a handler for the CoreWebView2InitializationCompleted event, which will run before
            // EnsureCoreWebView2Async returns if we're awaiting properly, so we need to set up that handler
            // before calling EnsureCoreWebView2Async. (It must run even when we reuse a shared environment,
            // because EnsureCoreWebView2Async still fires this control's own InitializationCompleted, which
            // is what sets _readyToNavigate.)
            SetupEventHandling();
            // Reuse an existing environment (and its browser process + user-data folder + HTTP cache) when we
            // can, so we don't pay environment creation per browser:
            //  - _injectedEnvironment: an environment handed to this instance (OffScreenBrowser shares one
            //    environment across the fresh browser it creates per page — see CreateWithInjectedEnvironment);
            //  - _sharedEnvironment: the legacy on-UI-thread shared-environment batch (BookProcessor's old path).
            // Otherwise we fall through and create a fresh one.
            var env = _injectedEnvironment ?? (_useSharedEnvironment ? _sharedEnvironment : null);
            if (env == null)
            {
                string dataFolder;
                do
                {
                    dataFolder = Path.Combine(
                        Path.GetTempPath(),
                        "Bloom WV2-"
                            + (BloomServer.portForHttp == 0 ? 8085 : BloomServer.portForHttp)
                            + dataFolderCounter++
                    );
                } while (Directory.Exists(dataFolder));
                env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: AlternativeWebView2Path,
                    userDataFolder: dataFolder,
                    options: op
                );
                if (_useSharedEnvironment)
                    _sharedEnvironment = env;
            }
            await _webview.EnsureCoreWebView2Async(env);
            // Added as a footnote to BL-15466 to prevent popups generated from title
            // attributes being white on black, presumably because of some setting the
            // user has made for Chrome/Edge generally.
            _webview.CoreWebView2.Profile.PreferredColorScheme =
                CoreWebView2PreferredColorScheme.Light;
            // If we were changing this after possibly loading a document we would now
            // want to reload it, but we are just initializing this WebView2, so no need.
            // _webview.Reload();

            // It is kinda hard to get a click event from webview2. This needs to be explicitly sent from the browser code,
            // e.g. (window as any).chrome.webview.postMessage("browser-clicked");
            _webview.WebMessageReceived += (o, e) =>
            {
                // for now the only thing we're using this for is to close the page thumbnail list context menu when the user clicks outside it
                if (e.TryGetWebMessageAsString() == "browser-clicked")
                {
                    RaiseBrowserClick(null, null);
                }
            };

            // Now do the same thing for any iframes. When an iframe is created...
            _webview.CoreWebView2.FrameCreated += (o, e) =>
            {
                // ... register for a message that our javascript will send us.
                // We are using this in the Edit View
                // to know when to cancel a page context menu until we rewrite that in React.
                // Note that _webview.GotFocus() is easier, but I was not able to get the
                // winforms popup menu to receive focus such that the webview would lose it
                // and thus tell us when it regained it.
                e.Frame.WebMessageReceived += (a, b) =>
                {
                    if (b.TryGetWebMessageAsString() == "browser-clicked")
                    {
                        RaiseBrowserClick(null, null);
                    }
                };
            };

            if (!_clearedCache)
            {
                _clearedCache = true;
                // The intent here is that none of Bloom's assets should be cached from one run of the program to another
                // (in case a new version of Bloom has been installed).
                // OTOH, I don't want to clear things so drastically as to preclude using local storage or cookies.
                // The doc is unclear as to the distinction between CacheStorage and DiskCache, but I _think_
                // this should clear what we need and nothing else.
                await _webview.CoreWebView2.Profile.ClearBrowsingDataAsync(
                    CoreWebView2BrowsingDataKinds.CacheStorage
                        | CoreWebView2BrowsingDataKinds.DiskCache
                );
            }
        }

        // used when the WebView2 installation is broken
        public static string GetEdgeInstallationPath()
        {
            string path = null;

            // Check registry for Edge installation path
            RegistryKey regKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients"
            );
            if (regKey != null)
            {
                string edgeAppId = "{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}";
                RegistryKey edgeKey = regKey.OpenSubKey(edgeAppId);
                if (edgeKey != null)
                {
                    string version = edgeKey.GetValue("pv") as string;
                    if (!string.IsNullOrEmpty(version))
                    {
                        string programFiles = Environment.GetFolderPath(
                            Environment.SpecialFolder.ProgramFilesX86
                        );
                        path = Path.Combine(programFiles, "Microsoft", "EdgeCore", version);
                    }
                }
            }

            return path;
        }

        // needed by geckofx but not webview2
        public override void EnsureHandleCreated() { }

        public override void CopySelection()
        {
            // Fire-and-forget is fine here as long as this is only used for user actions and not
            // by code that would immediately try to do something with the result.
            RunJavascriptFireAndForget("document.execCommand(\"Copy\")");
        }

        public override void SelectAll()
        {
            // Fire-and-forget is fine here as long as this is only used for user actions and not
            // by code that would immediately try to do something with the result.
            RunJavascriptFireAndForget("document.execCommand(\"SelectAll\")");
        }

        public override void SelectBrowser()
        {
            // Enhance: investigate reasons why we do this. Possibly it is not necessary after we
            // settle on WebView2; at least one client was just using it to work around a
            // peculiar behavior of GeckoFx.
            _webview.Select();
        }

        public override void ActivateFocussed()
        {
            // I can't find any place where this does anything useful in GeckoFx that would allow me to
            // test a WebView2 implementation. For example, from the comment in the ReactControl_Load
            // method which is currently the only caller, I would expect that using it would cause
            // something useful, possibly the OK button or the number, to be selected in the Duplicate Many
            // dialog, which is one thing that actually executes this method as it launches. But
            // in fact nothing helpful is focused in either Gecko mode or WV2 mode, and in both modes,
            // it takes the same number of tab presses to get focus to the desired control. I think we
            // can leave implementing this until someone identifies a difference in Gecko vs WV2 behavior
            // that we think is due to not implementing it.
        }

        protected override void UpdateDisplay(string newUrl)
        {
            EnsureBrowserReadyToNavigate();

            // If we are disposed, this will certainly fail. Likely we are shutting down and just
            // trying to navigate to a blank page.
            if (!_webview.IsDisposed && !_webview.Disposing)
                _webview.CoreWebView2.Navigate(newUrl);
        }

        protected override void EnsureBrowserReadyToNavigate()
        {
            // Don't really know if this is enough. Arguably, we should also
            // wait until we are sure all the awaits in InitWebView complete.
            // But that is very hard to do without making half Bloom's code async.
            // This seems to be enough for the one case (making epubs) where I
            // experienced a problem from navigating too soon.
            // True confessions: I'm not sure why this works, nor even absolutely
            // sure that it could not loop forever. But in every case I've tried,
            // it did terminate, and in the one case where Navigation previously
            // threw an Exception indicating it was not ready, waiting like this fixed it.
            while (!_readyToNavigate)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        // This variation should be used by clients that use a stopwatch
        // to enforce a time limit. At least once (in a bulk upload),
        // the previous version of NavigateAndWaitTillDone waited forever,
        // instead of at most 10s, waiting for the function above to terminate.
        // Callers should check that _readyToNavigate is true on return.
        private void EnsureBrowserReadyToNavigate(Stopwatch navTimer, int timeLimit)
        {
            while (!_readyToNavigate && navTimer.ElapsedMilliseconds < timeLimit)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }

        public override bool NavigateAndWaitTillDone(
            HtmlDom htmlDom,
            int timeLimit,
            InMemoryHtmlFileSource source,
            Func<bool> cancelCheck,
            bool throwOnTimeout
        )
        {
            // Should be called on UI thread. Since it is quite typical for this method to create the
            // window handle and browser, it can't do its own Invoke, which depends on already having a handle.
            // OTOH, Unit tests are often not run on the UI thread (and would therefore just pop up annoying asserts).
            // For future reference, if we are navigating to produce a preview, make sure that the api call that
            // requests the call is syncing on the correct thumbnail/preview sync object, otherwise we can get a
            // deadlock here while trying to navigate (See BL-11513).
            Debug.Assert(
                Program.RunningOnUiThread
                    || Program.RunningUnitTests
                    || Program.RunningInConsoleMode,
                "Should be running on UI Thread or Unit Tests or Console mode"
            );
            var done = false;
            var navTimer = new Stopwatch();
            navTimer.Start();
            EnsureBrowserReadyToNavigate(navTimer, timeLimit);

            EventHandler<CoreWebView2NavigationCompletedEventArgs> navigationCompletedHandler =
                null;
            if (_readyToNavigate) // should always be true, unless we timed out
            {
                navigationCompletedHandler = (sender, args) => done = true;
                _webview.CoreWebView2.NavigationCompleted += navigationCompletedHandler;
                // The Gecko implementation also had _browser.NavigationError += (sender, e) => done = true;
                // I can't find any equivalent for WebView2 and I think the doc says it will raise NavigationCompleted
                // even if there was an error, but consider this if implementing for yet another browser.
                Navigate(htmlDom, source: source);
            } // else we've timed out and will immediately exit the next loop and report it

            // If done is set (by NavigationError?) prematurely, we still need to wait while IsBusy
            // is true to give the loaded document time to become available for the checks later.
            // See https://issues.bloomlibrary.org/youtrack/issue/BL-8741.
            try
            {
                while ((!done) && navTimer.ElapsedMilliseconds < timeLimit)
                {
                    Application.DoEvents(); // NOTE: this has bad consequences all down the line. See BL-6122.
                    Thread.Sleep(10);
                    // Remember this might be needed if we reimplement with a Linux-compatible control.
                    // OTOH, it doesn't help on Windows, and may lead to unwanted reentrancy if multiple
                    // navigation-involving tasks as waiting on Idle.
                    // I haven't made it conditional-compilation because this whole WebView2-based class is Windows-only.
                    // Application.RaiseIdle(new EventArgs()); // needed on Linux to avoid deadlock starving browser navigation
                    if (cancelCheck != null && cancelCheck())
                    {
                        navTimer.Stop();
                        return false;
                    }
                }
                //Debug.WriteLine(
                //    $"DEBUG: Navigation wait loop ended after {navTimer.Elapsed}: done={done}"
                //);

                navTimer.Stop();
                if (!done)
                {
                    if (throwOnTimeout)
                        throw new ApplicationException(
                            _readyToNavigate
                                ? "Browser unexpectedly took too long to load a page"
                                : "Browser unexpectedly took too long to initialize"
                        );
                    else
                        return false;
                }

                return true;
            }
            finally
            {
                if (
                    navigationCompletedHandler != null
                    && !_webview.IsDisposed
                    && !_webview.Disposing
                    && _webview.CoreWebView2 != null
                )
                {
                    _webview.CoreWebView2.NavigationCompleted -= navigationCompletedHandler;
                }
            }
        }

        // This should be used as little as possible, since it breaks the goal of being able to
        // just drop in another implementation of the base class. However, some code outside this
        // class (currently the PDF preview code in Publish tab) already has different behaviors
        // depending on which browser we're using, and it seems simpler to me to just let it get
        // at the underlying object. If we do introduce another browser, it may become clearer
        // how we might want to encapsulate the things we use this for.
        public WebView2 InternalBrowser => _webview;

        public override string Url => _webview.Source.ToString();

        public override async Task<Bitmap> CapturePreview()
        {
            var stream = new MemoryStream();
            await _webview.CoreWebView2.CapturePreviewAsync(
                CoreWebView2CapturePreviewImageFormat.Png,
                stream
            );

            stream.Position = 0;
            return new Bitmap(stream);
        }

        public override async Task SaveDocumentAsync(string path)
        {
            var html = await GetStringFromJavascriptAsync("document.documentElement.outerHTML");
            RobustFile.WriteAllText(path, html, Encoding.UTF8);
        }

        public override async Task RunJavascriptAsync(string script)
        {
            await _webview.ExecuteScriptAsync(script);
        }

        /// <summary>
        /// Run a javascript script asynchronously.
        /// This version of the method simply makes it explicit that we are purposefully not awaiting the result.
        /// Therefore the script likely has not finished executing by the time the method returns.
        /// </summary>
        public override void RunJavascriptFireAndForget(string script)
        {
            // Guard against running when the browser isn't in a usable state. During app startup
            // (before CoreWebView2 is initialized) or shutdown (while the control is disposing),
            // ExecuteScriptAsync throws from inside its async state machine. Because nothing here
            // awaits or otherwise observes the returned Task, that fault used to reach the GC
            // finalizer thread and get rethrown as an UnobservedTaskException (Sentry
            // BLOOM-DESKTOP-D07). This mirrors the readiness check in UpdateDisplay().
            if (
                _webview == null
                || _webview.IsDisposed
                || _webview.Disposing
                || _webview.CoreWebView2 == null
            )
                return;

            // Even with the guard above there is a tiny race window in which the control can start
            // disposing between the check and the call, so we still observe the returned Task. On a
            // fault we just log it: this is an expected startup/shutdown race, not a user-facing
            // failure, so no MessageBox and no rethrow. Observing the Task is what structurally
            // eliminates the UnobservedTaskException (BLOOM-DESKTOP-D07).
            _webview
                .ExecuteScriptAsync(script)
                .ContinueWith(
                    t =>
                    {
                        // Read the exception first so it is observed even if logging fails.
                        var message = t.Exception?.GetBaseException().Message;
                        try
                        {
                            Logger.WriteEvent(
                                "WebView2Browser.RunJavascriptFireAndForget: ExecuteScriptAsync faulted (expected during startup/shutdown): "
                                    + message
                            );
                        }
                        catch
                        {
                            // Swallow any logging failure: letting it escape would fault this
                            // continuation's own Task and reintroduce the very
                            // UnobservedTaskException we are preventing (BLOOM-DESKTOP-D07).
                        }
                    },
                    TaskContinuationOptions.OnlyOnFaulted
                );
        }

        public override async Task<string> GetStringFromJavascriptAsync(string script)
        {
            var result = await _webview.ExecuteScriptAsync(script);
            // Whatever the javascript produces gets JSON encoded automatically by ExecuteScriptAsync.
            // All the methods Bloom calls this way return strings (or null), so we just need to do this to recover them.
            var result2 = JsonConvert.DeserializeObject(result);
            var result3 = result2?.ToString();
            return result3;
        }

        /// <summary>
        /// Executes a JavaScript script asynchronously and retrieves the result as a JSON-encoded string.
        /// </summary>
        public override async Task<string> GetObjectFromJavascriptAsync(string script)
        {
            // Whatever the javascript produces gets JSON encoded automatically by ExecuteScriptAsync.
            return await _webview.ExecuteScriptAsync(script);
        }

        public override void SaveHTML(string path)
        {
            throw new NotImplementedException();
        }

        public override void SetEditingCommands(
            CutCommand cutCommand,
            CopyCommand copyCommand,
            PasteCommand pasteCommand,
            UndoCommand undoCommand
        )
        {
            _cutCommand = cutCommand;
            _copyCommand = copyCommand;
            _pasteCommand = pasteCommand;
            _undoCommand = undoCommand;

            // Once these buttons are in the same browser as the page and we don't have to go through C#,
            // we can get rid of the commands completely. Until then, if we don't set an Implementer,
            // Enabled will always be false.
            _cutCommand.Implementer = () => { };
            _copyCommand.Implementer = () => { };
            _undoCommand.Implementer = () => { };

            // This implementation is specific to our Edit tab. This is currently the only place
            // we show the paste button that uses this command, but we will have to generalize somehow if
            // that changes. I'm not sure whether the checks for existence of workspaceBundle etc are needed.
            // I deliberately use the fire-and-forget variant here, because nothing requires the
            // result (we only care about the side effects on the document).
            _pasteCommand.Implementer = () =>
            {
                PalasoImage clipboardImage = null;
                try
                {
                    clipboardImage = PortableClipboard.GetImageFromClipboardWithExceptions();
                }
                catch (Exception) // anything goes wrong, just assume it's not an image
                { }
                var haveClipboardImage = clipboardImage == null ? "false" : "true";

                clipboardImage?.Dispose();

                RunJavascriptFireAndForget(
                    $"workspaceBundle?.getEditablePageBundleExports()?.pasteClipboard({haveClipboardImage})"
                );
            };
        }

        public override void ShowHtml(string html)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// We configure something in Javascript to keep track of this, since WebView2 doesn't provide an API for it
        /// (This means this method is only reliable in EditingView, but that's also the only context where we
        /// currently use it).
        /// </summary>
        /// <returns></returns>
        private bool IsThereACurrentTextSelection()
        {
            return EditingModel.IsTextSelected;
        }

        bool _currentlyInUpdateButtons = false;

        // This is 'async void' rather than 'async Task' because it overrides the void
        // IBrowser.UpdateEditButtonsAsync() and is invoked as a fire-and-forget UI update (e.g. from
        // the edit-buttons timer). That is acceptable here only because the body wraps its awaited
        // work in a try/catch, so an exception can't escape into the void and crash the process.
        public override async void UpdateEditButtonsAsync()
        {
            if (_currentlyInUpdateButtons)
                return;
            _currentlyInUpdateButtons = true;

            try
            {
                if (_copyCommand == null)
                    return;

                if (InvokeRequired)
                {
                    Invoke(new Action(UpdateEditButtonsAsync));
                    return;
                }

                try
                {
                    var isTextSelection = IsThereACurrentTextSelection();
                    _cutCommand.Enabled = isTextSelection;
                    _copyCommand.Enabled = isTextSelection;
                    _pasteCommand.Enabled =
                        PortableClipboard.ContainsText() || PortableClipboard.CanGetImage();

                    _undoCommand.Enabled = await CanUndoAsync();
                }
                catch (Exception)
                {
                    _pasteCommand.Enabled = false;
                    Logger.WriteMinorEvent("UpdateEditButtons(): Swallowed exception.");
                    //REf jira.palaso.org/issues/browse/BL-197
                    //I saw this happen when Bloom was in the background, with just normal stuff on the clipboard.
                    //so it's probably just not ok to check if you're not front-most.
                }
            }
            finally
            {
                _currentlyInUpdateButtons = false;
            }
        }

        bool _currentlyRunningCanUndo = false;

        private async Task<bool> CanUndoAsync()
        {
            // once we got a stackoverflow exception here, when, apparently, JS took longer to complete this than the timer interval
            if (_currentlyRunningCanUndo)
                return true;
            try
            {
                _currentlyRunningCanUndo = true;
                return "yes" == await GetStringFromJavascriptAsync("workspaceBundle?.canUndo?.()");
            }
            finally
            {
                _currentlyRunningCanUndo = false;
            }
        }

        public static string kWebView2NotInstalled = "not installed";

        // If we change this minimum WebView2 version, consider updating the rule near the end of
        // "BloomBrowserUI/webpack.common.js".
        public static string kMinimumWebView2Version = "112.0.0.0";

        public static bool GetIsWebView2NewEnough(out string version)
        {
            version = GetWebView2Version();
            if (kWebView2NotInstalled == version)
                return false;
            return (
                CoreWebView2Environment.CompareBrowserVersions(version, kMinimumWebView2Version)
                >= 0
            );
        }

        public static string GetWebView2Version()
        {
            try
            {
                return CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return kWebView2NotInstalled;
            }
        }

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            // Have seen bizarre cases involving Dispose in the tail end of an async method where
            // something goes wrong as the async method is resumed which somehow has the result that
            // while we are disposing the _webview, the CoreWebView2InitializationCompleted event handler
            // fires with success false, but Disposing returns false!! Hopefully this variable will be
            // more reliable.
            _inDisposeMethod = true;
            if (disposing)
            {
                int procId = 0;
                string userFolder = null;
                try
                {
                    var uprocId = _webview?.CoreWebView2?.BrowserProcessId;
                    procId = uprocId.HasValue ? (int)uprocId.Value : 0;
                    userFolder = _webview?.CoreWebView2?.Environment?.UserDataFolder;
                }
                catch
                {
                    // If we can't get the process id or user folder, just ignore it.
                    // This can happen if the WebView2 control is not initialized.
                }
                if (components != null)
                {
                    components.Dispose();
                }
                else if (_webview != null)
                {
                    _webview.Dispose();
                }
                if (procId > 0 && userFolder != null && Directory.Exists(userFolder))
                {
                    // We need to wait until the process finishes to reliably delete the folder.
                    // Unit tests can produce WebView2 processes that reuse the same id.  This
                    // could conceivably happen in Bloom Desktop, so I've added code to handle it.
                    // If needed, the prior user folder is stored so that we can delete it after
                    // all of the processes have finished.
                    if (WebView2ProcessToUserFolder.ContainsKey(procId))
                    {
                        var priorUserFolder = WebView2ProcessToUserFolder[procId];
                        if (priorUserFolder != userFolder)
                        {
                            // Since we're not absolutely sure that folder names are unique, we
                            // save the prior user folder so that we can safely delete it later
                            // if needed.
                            if (Directory.Exists(priorUserFolder))
                                ObsoleteWebView2UserFolders.Add(priorUserFolder);
                            WebView2ProcessToUserFolder[procId] = userFolder;
                        }
                    }
                    else
                    {
                        WebView2ProcessToUserFolder.Add(procId, userFolder);
                    }
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Mapping of WebView2 process IDs to their user folder paths.  We want to delete these user folders
        /// because they don't contain anything we need to persist, and they can take up a lot of space.  New ones
        /// are usually created, so they accumulate over time and can hold gigabytes of useless data.
        /// </summary>
        /// <remarks>
        /// The WebView2 browser engines run in separate processes, and we have to wait until each process has
        /// exited before we can reliably delete its user folder.
        /// https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder?tabs=win32#deleting-user-data-folders
        /// The WebView2Browser.Dispose method fills in this dictionary with the process ID and user folder path.
        /// All of the browsers will have been disposed by the the application shutting down before the following
        /// method is called.
        /// </remarks>
        private static readonly Dictionary<int, string> WebView2ProcessToUserFolder =
            new Dictionary<int, string>();

        /// <summary>
        /// Unit tests in particular can create WebView2 processes that reuse the same process IDs.  This set of
        /// files are those that we had already recorded in the dictionary with the same process ID but which have
        /// different names.  We want to delete these as well, since they are no longer needed.
        /// </summary>
        private static HashSet<string> ObsoleteWebView2UserFolders = new HashSet<string>();

        public static void CleanupWebView2UserFolders()
        {
            try
            {
                int loopCount = 0;
                while (WebView2ProcessToUserFolder.Count > 0)
                {
                    foreach (var key in WebView2ProcessToUserFolder.Keys.ToArray())
                    {
                        try
                        {
                            // The most efficient way to detect that a process with a given id is not
                            // running is to try to access it and get an exception thrown.
                            var process = Process.GetProcessById((int)key);
                        }
                        catch (ArgumentException)
                        {
                            // The process is no longer running, so we can delete the folder.
                            var userFolder = WebView2ProcessToUserFolder[key];
                            if (Directory.Exists(userFolder))
                            {
                                try
                                {
                                    RobustIO.DeleteDirectory(userFolder, true);
                                }
                                catch (Exception) { }
                            }
                            WebView2ProcessToUserFolder.Remove(key);
                        }
                    }
                    if (WebView2ProcessToUserFolder.Count > 0)
                        Thread.Sleep(100); // Wait a bit before checking again
                    if (++loopCount > 30)
                    {
                        // If we can't clean up the folders after 3 seconds, give up.
                        // The developer machine took no more than 4 iterations in this loop,
                        // so 30 should be plenty.
                        Logger.WriteEvent("Timeout cleaning up WebView2 user folders.");
                        break;
                    }
                }
                foreach (var userFolder in ObsoleteWebView2UserFolders)
                {
                    if (Directory.Exists(userFolder))
                    {
                        try
                        {
                            RobustIO.DeleteDirectory(userFolder, true);
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception e)
            {
                // If we can't delete the folders, we don't want to crash Bloom.
                // This is not a critical operation, so just log the error and continue.
                Logger.WriteError("Error cleaning up WebView2 user folders: ", e);
            }
        }
    }

    class WebViewItemAdder : IMenuItemAdder
    {
        private readonly IList<CoreWebView2ContextMenuItem> _menuList;
        private Microsoft.Web.WebView2.WinForms.WebView2 _webview;

        public WebViewItemAdder(
            Microsoft.Web.WebView2.WinForms.WebView2 webview,
            IList<CoreWebView2ContextMenuItem> menuList
        )
        {
            _webview = webview;
            _menuList = menuList;
        }

        public void Add(string caption, EventHandler handler, bool enabled = true)
        {
            CoreWebView2ContextMenuItem newItem =
                _webview.CoreWebView2.Environment.CreateContextMenuItem(
                    caption,
                    null,
                    CoreWebView2ContextMenuItemKind.Command
                );
            newItem.CustomItemSelected += (sender, args) => handler(sender, new EventArgs());
            newItem.IsEnabled = enabled;
            _menuList.Insert(_menuList.Count, newItem);
        }
    }
}
