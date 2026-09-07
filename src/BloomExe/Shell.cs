using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Bloom.Api;
using Bloom.Collection;
using Bloom.Edit;
using Bloom.Properties;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using SIL.Extensions;
using SIL.Reporting;
using SIL.Windows.Forms.PortableSettingsProvider;

namespace Bloom
{
    public partial class Shell : SIL.Windows.Forms.Miscellaneous.FormForUsingPortableClipboard
    {
        public static Shell GetShellOrNull()
        {
            return Application.OpenForms.OfType<Shell>().FirstOrDefault();
        }

        public static Form GetShellOrOtherOpenForm()
        {
            Form form = GetShellOrNull();
            if (form == null)
                form = Application.OpenForms.Cast<Form>().LastOrDefault();
            return form;
        }

        private readonly CollectionSettings _collectionSettings;
        private readonly CollectionClosing _collectionClosingEvent;
        private readonly ControlKeyEvent _controlKeyEvent;
        private readonly WorkspaceView _workspaceView;
        private AudioRecording _audioRecording;

        // This is needed because on Linux the ResizeEnd event is firing before the Load event handler is
        // finished, overwriting the saved RestoreBounds before they are applied.
        private bool _finishedLoading;

        // During an automation run (--automation, e.g. the Playwright suites) the window must
        // not steal the user's keyboard focus when it is shown. That holds wherever the window
        // is: on the developer's desktop, on a monitor of its own, or off every monitor.
        protected override bool ShowWithoutActivation => Program.StartupAutomation;

        public Shell(
            Func<WorkspaceView> projectViewFactory,
            CollectionSettings collectionSettings,
            BookDownloadStartingEvent bookDownloadStartingEvent,
            CollectionClosing collectionClosingEvent,
            QueueRenameOfCollection queueRenameOfCollection,
            ControlKeyEvent controlKeyEvent,
            SignLanguageApi signLanguageApi,
            AudioRecording audioRecording
        )
        {
            queueRenameOfCollection.Subscribe(newName =>
                _nameToChangeCollectionUponClosing = newName.Trim().SanitizeFilename('-')
            );
            _collectionSettings = collectionSettings;
            _collectionClosingEvent = collectionClosingEvent;
            _controlKeyEvent = controlKeyEvent;
            _audioRecording = audioRecording;
            InitializeComponent();
            if (AutomationWindowPlacement.IsOffEveryMonitor)
            {
                // Keep the off-screen window out of the task bar, so such a run leaves no trace
                // on the developer's desktop.
                //
                // This has to happen before the window handle exists, which is why it is here
                // and not in Shell_Load with the rest of the headless placement. Assigning
                // ShowInTaskbar on a form that is already showing makes Windows Forms recreate
                // the form's handle, and every child handle with it, including the WebView2
                // host. The Edit tab survived that with a browser that no longer answered a
                // jump to another page, so every e2e test that moves between pages hung.
                ShowInTaskbar = false;
            }
            Activated += (sender, args) =>
            {
                // In at least one case (BL-15060) we seem to have gotten activated
                // while Bloom was shutting down to switch to an updated version. It's dangerous
                // to do the usual activation process in such a state, because we may be in
                // the process of disposing the ProjectContext, which can lead to ObjectDisposed
                // exceptions, and if we're really unlucky, trying to report that can lock things up.
                // So if we know we're in the process of shutting down, ignore being activated.
                if (AppIsShuttingDown)
                    return;
                // Some of the stuff we do to update things depends on a current editing view and model.
                // So just don't try if the user is for some reason editing the videos while not editing
                // the book. Hopefuly in that case he hasn't opened the book and none of its old state
                // is cached.
                if (_workspaceView.InEditMode)
                    signLanguageApi.CheckForChangedVideoOnActivate(sender, args);
                if (_workspaceView.InCollectionTab)
                    _workspaceView.CheckForCollectionUpdates();
            };
            Deactivate += (sender, args) => signLanguageApi.DeactivateTime = DateTime.Now;

            //bring the application to the front (will normally be behind the user's web browser)
            bookDownloadStartingEvent.Subscribe(
                (x) =>
                {
                    try
                    {
                        this.Invoke((Action)this.Activate);
                    }
                    catch (Exception e)
                    {
                        Debug.Fail(
                            "(Debug Only) Can't bring to front in the current state: " + e.Message
                        );
                        //swallow... so we were in some state that we couldn't come to the front... that's ok.
                    }
                }
            );

            WindowState = FormWindowState.Normal;
            Size = new Size(1024, 720);

            _workspaceView = projectViewFactory();

            _workspaceView.ReopenCurrentProject += (
                (x, y) =>
                {
                    UserWantsToOpeReopenProject = true;
                    Close();
                }
            );

            _workspaceView.BackColor = Bloom.Palette.GeneralBackground;
            _workspaceView.Dock = System.Windows.Forms.DockStyle.Fill;

            this.Controls.Add(this._workspaceView);

            SetWindowText(null);
        }

        public void CheckForInvalidBranding()
        {
            _workspaceView.CheckForInvalidBranding();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
            this.Icon = global::Bloom.Properties.Resources.BloomIcon;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            _audioRecording.ResumeMonitoringAudio();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            _audioRecording.PauseMonitoringAudio(true);
        }

        /// <summary>
        /// Keep the main workspace layout in sync when the window moves to a monitor with
        /// a different DPI, or when monitor scaling changes while Bloom is running.
        /// </summary>
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (AppIsShuttingDown || Disposing || IsDisposed)
                return;

            Logger.WriteMinorEvent($"Shell DPI changed from {e.DeviceDpiOld} to {e.DeviceDpiNew}");
            NotifyDpiChanged();
        }

        /// <summary>
        /// Refreshes layout and notifies browser UI listeners that DPI-related state changed.
        /// </summary>
        private void NotifyDpiChanged()
        {
            if (_workspaceView == null || _workspaceView.Disposing || _workspaceView.IsDisposed)
                return;

            BloomWebSocketServer.Instance?.SendEvent("recordVideo", "dpiChanged");
            _workspaceView.PerformLayout();
            _workspaceView.Invalidate(true);
        }

        public bool AppIsShuttingDown => _startedClosingEvent || _finishedClosingEvent;

        private bool _startedClosingEvent;
        private bool _finishedClosingEvent;

        protected override void OnClosing(CancelEventArgs e)
        {
            // We want to get everything saved (under the old collection name, if we are changing the name and restarting).
            // This is tricky because we may need to save current changes to a book we are editing, and this
            // involves an inherently asynchronous process (thanks to WebView2). We tried endless ways to
            // wait for the data we need from the page we're editing, and nothing worked reliably.
            // If we go ahead and close the Shell, the message we eventually get on our API with the data to save
            // tries to use Invoke on the Shell to get on the UI thread, but the Shell is already disposed.
            // So, the first time OnClosing is called, we raise an event that will do the saving, and cancel
            // the close. In case the user manages to click the Close button again before the saving is done,
            // we set a flag to say it is in progress, so that we can ignore any subsequent OnClosing events
            // until we are done saving. When we ARE done saving, we set a flag to say so, and then call Close()
            // to actually get the window closed.
            if (_finishedClosingEvent)
            {
                base.OnClosing(e);
                return;
            }

            if (_startedClosingEvent)
            {
                e.Cancel = true;
                return;
            }

            Logger.WriteMinorEvent("starting to shut Bloom down");

            _startedClosingEvent = true;

            _collectionClosingEvent.Raise(
                new CollectionClosingArgs()
                {
                    PostponedWork = () =>
                    {
                        if (
                            !string.IsNullOrEmpty(_nameToChangeCollectionUponClosing)
                            && _nameToChangeCollectionUponClosing
                                != _collectionSettings.CollectionName
                            && UserWantsToOpeReopenProject
                        )
                        {
                            // Without checking and resetting this flag, Linux endlessly spawns new instances. Apparently the Mono runtime
                            // calls OnClosing again as a result of calling Program.RestartBloom() which calls Application..Exit().
                            UserWantsToOpeReopenProject = false;
                            //Actually restart Bloom with a parameter requesting this name change. It's way more likely to succeed
                            //when this run isn't holding onto anything.
                            try
                            {
                                var existingDirectoryPath = Path.GetDirectoryName(
                                    _collectionSettings.SettingsFilePath
                                );
                                var parentDirectory = Path.GetDirectoryName(existingDirectoryPath);
                                var newDirectoryPath = Path.Combine(
                                    parentDirectory,
                                    _nameToChangeCollectionUponClosing
                                );

                                Program.RestartBloom(
                                    true,
                                    string.Format(
                                        "--rename \"{0}\" \"{1}\" ",
                                        existingDirectoryPath,
                                        newDirectoryPath
                                    )
                                );
                            }
                            catch (Exception error)
                            {
                                SIL.Reporting.ErrorReport.NotifyUserOfProblem(
                                    error,
                                    "Sorry, Bloom failed to even prepare for the rename of the project to '{0}'",
                                    _nameToChangeCollectionUponClosing
                                );
                            }
                        }

                        _finishedClosingEvent = true;
                        Logger.WriteMinorEvent("closing the Shell");
                        Close();
                    },
                    FailureAction = () =>
                    {
                        // We didn't want a second attempt at saving if the user clicks the close box while we are
                        // still trying to save after the first click on Close. But if the first attempt fails,
                        // we don't want to stay in a state where all attempts to close the program are ignored.
                        _startedClosingEvent = false;
                    },
                }
            );
            e.Cancel = true;
            base.OnClosing(e);
        }

        public void SetWindowText(string bookName)
        {
            string formattedText;
            if (!string.IsNullOrWhiteSpace(Program.StartupLabel))
            {
                formattedText = string.Format("Bloom {0}", Program.StartupLabel);
            }
            else
            {
                // Let's only mark the window text for Alpha and Beta releases. It looks odd to have that in
                // release builds, and doesn't add much since we can treat Release builds as the unmarked case.
                // Note that developer builds now have a special "channel" marking as well to differentiate them
                // from true Release builds in screen shots.
                formattedText = string.Format(
                    "{0} - Bloom {1}",
                    _workspaceView.Text,
                    GetShortVersionInfo()
                );
                var channel = ApplicationUpdateSupport.ChannelName;
                if (channel.ToLowerInvariant() != "release")
                    formattedText = string.Format("{0} {1}", formattedText, channel);
                if (bookName != null)
                {
                    formattedText = string.Format("{0} - {1}", bookName, formattedText);
                }
            }

            if (ShouldShowPortSummaryInWindowTitle())
            {
                var portSummary = new[]
                {
                    GetHttpPortTitlePart(),
                    GetAutomationPortTitlePart(),
                    GetVitePortTitlePart(),
                }.Where(part => !string.IsNullOrEmpty(part));
                var portSummaryText = string.Join(" ", portSummary);
                if (!string.IsNullOrEmpty(portSummaryText))
                {
                    formattedText = string.Format("{0} - {1}", formattedText, portSummaryText);
                }
            }

            Text = formattedText;
        }

        internal static bool ShouldShowPortSummaryInWindowTitle()
        {
            return Program.StartupAutomation;
        }

        private static string GetHttpPortTitlePart()
        {
            return BloomServer.portForHttp > 0 ? $"http:{BloomServer.portForHttp}" : null;
        }

        private static string GetAutomationPortTitlePart()
        {
            // Surface the CDP port in the window title so humans and automation can identify the right Bloom instance.
            var cdpPort = WebView2Browser.RemoteDebuggingPort;
            return cdpPort.HasValue ? $"automation:{cdpPort.Value}" : null;
        }

        private static string GetVitePortTitlePart()
        {
            return ReactControl.TryGetActiveViteDevPort(out var vitePort)
                ? $"vite:{vitePort}"
                : null;
        }

        public static string GetShortVersionInfo()
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version;

            return string.Format("{0}.{1}.{2}", ver.Major, ver.Minor, ver.Build);
        }

        public bool UserWantsToOpenADifferentProject { get; set; }

        public bool UserWantsToOpeReopenProject;

        /// <summary>
        /// used when the user does an in-app installer download; after we close down, Program will read this and return control to Sparkle
        /// </summary>
        public bool QuitForVersionUpdate;

        public bool QuitForSystemShutdown;

        private string _nameToChangeCollectionUponClosing;

        private void Shell_Activated(object sender, EventArgs e) { }

        private void Shell_Deactivate(object sender, EventArgs e)
        {
            Debug.WriteLine("Shell Deactivated");
        }

        public void ResizeWindow(int width, int height)
        {
            Size = new Size(width, height);
        }

        public static void ComeToFront()
        {
            if (GetShellOrOtherOpenForm() is Shell shell)
            {
                shell.Invoke(
                    (Action)(
                        () =>
                        {
                            shell.ReallyComeToFront();
                        }
                    )
                );
            }
        }

        /// <summary>
        /// we let the Program call this after it closes the splash screen
        /// </summary>
        public void ReallyComeToFront()
        {
            // During an automation run, grabbing focus would yank the user's keyboard away
            // from whatever they are doing while tests run, and a window placed off every
            // monitor cannot come to the front at all: TopMost and BringToFront on it would
            // take the foreground away for nothing.
            if (!Program.StartupAutomation)
            {
                //try really hard to become top most. See http://stackoverflow.com/questions/5282588/how-can-i-bring-my-application-window-to-the-front
                TopMost = true;
                Focus();
                BringToFront();
                TopMost = false;
            }

            _finishedLoading = true;
        }

        private void Shell_Load(object sender, EventArgs e)
        {
            //Handle window sizing/location. Normally, we just Maximize the window.
            //The exceptions to this are if we are in a DEBUG build or the settings have a MaximizeWindow=='False", which at this time
            //must be done by hand (no user UI is provided).
            try
            {
                SuspendLayout();

                // Where an automation run puts its window is BLOOM_AUTOMATION_MONITOR's to
                // decide (see AutomationWindowPlacement). A run that it says nothing about falls
                // through to the ordinary cases below, window placement and all, so the
                // developer sees the Bloom they would see without the variable.
                var placement = AutomationWindowPlacement.GetChoice();
                if (Program.StartupAutomation)
                {
                    // Say in the log which monitor this run chose and what the alternatives were.
                    // The number in the variable is not the number Windows Settings shows; see
                    // AutomationWindowPlacement.DescribeChoice.
                    Logger.WriteEvent(AutomationWindowPlacement.DescribeChoice());
                }
                if (placement == AutomationWindowPlacement.Choice.OffEveryMonitor)
                {
                    // The window goes off every monitor and out of the task bar, so a test can
                    // run while the developer works. It stays Normal (not minimized) and full
                    // size, because WebView2 only paints a window that is neither minimized nor
                    // hidden. See AutomationWindowPlacement.GetBoundsOffEveryMonitor.
                    StartPosition = FormStartPosition.Manual;
                    WindowState = FormWindowState.Normal;
                    Bounds = AutomationWindowPlacement.GetBoundsOffEveryMonitor();
                    // ShowInTaskbar is set in the constructor, not here. See the comment there.
                }
                else if (placement == AutomationWindowPlacement.Choice.OnTheChosenMonitor)
                {
                    // Open on the monitor the variable named, not on whichever one the developer
                    // is working on, and leave the saved window placement alone.
                    StartPosition = FormStartPosition.Manual;
                    WindowState = FormWindowState.Normal;
                    Bounds = AutomationWindowPlacement.GetChosenMonitor().WorkingArea;
                    // Maximizing keeps the window on the screen that contains its bounds.
                    WindowState = FormWindowState.Maximized;
                }
                else if (Program.StartupAutomation)
                {
                    // An automation run the variable said nothing about: open exactly where a
                    // Bloom with no saved placement opens, and write nothing. The guard below
                    // keeps it from touching the developer's saved placement either way, which
                    // it must not do whatever the variable says: every Bloom of one build shares
                    // one user.config.
                    StartPosition = FormStartPosition.WindowsDefaultLocation;
                    WindowState = FormWindowState.Maximized;
                }
                else if (Settings.Default.WindowSizeAndLocation == null)
                {
                    StartPosition = FormStartPosition.WindowsDefaultLocation;
                    WindowState = FormWindowState.Maximized;
                    Settings.Default.WindowSizeAndLocation = FormSettings.Create(this);
                    Settings.Default.Save();
                }

                // This feature is not yet a normal part of Bloom, since we think just maximizing is more rice-farmer-friendly.
                // However, we added the ability to remember this stuff at the request of the person making videos, who needs
                // Bloom to open in the same place / size each time.
                if (Program.StartupAutomation)
                {
                    // Placement is settled above; leave the developer's saved placement alone.
                }
                else if (Settings.Default.MaximizeWindow == false)
                {
                    Settings.Default.WindowSizeAndLocation.InitializeForm(this);
                }
                else
                {
                    // BL-1036: save and restore un-maximized settings
                    var savedBounds = Settings.Default.RestoreBounds;
                    if (
                        (savedBounds.Width > 200)
                        && (savedBounds.Height > 200)
                        && (IsOnScreen(savedBounds))
                    )
                    {
                        StartPosition = FormStartPosition.Manual;
                        WindowState = FormWindowState.Normal;
                        Bounds = savedBounds;
                    }
                    else
                    {
                        StartPosition = FormStartPosition.CenterScreen;
                    }

                    WindowState = FormWindowState.Maximized;

                    UpdatePerformanceMeasurementStatus();
                }

                // We may be opening on a different collection.  Meddle with that collection if
                // file meddling is enabled.  (Don't stop meddling with the previous collection.)
                if (FileMeddlerManager.IsMeddling)
                {
                    FileMeddlerManager.Start(_collectionSettings?.FolderPath);
                }
            }
            catch (Exception error)
            {
                Debug.Fail(error.Message);

                // ReSharper disable HeuristicUnreachableCode
                //Not worth bothering the user. Just reset the values to something reasonable.
                StartPosition = FormStartPosition.WindowsDefaultLocation;
                WindowState = FormWindowState.Maximized;
                // ReSharper restore HeuristicUnreachableCode
            }
            finally
            {
                ResumeLayout();
            }
        }

        private void Shell_ResizeEnd(object sender, EventArgs e)
        {
            // BL-1036: save and restore un-maximized settings
            if (!_finishedLoading)
                return;
            if (WindowState != FormWindowState.Normal)
                return;
            // An automation run must never write the saved bounds. Where BLOOM_AUTOMATION_MONITOR
            // put its window is somewhere the developer is not looking, off every monitor or on a
            // monitor of the run's choosing, and saving that would move their next Bloom there.
            if (Program.StartupAutomation)
                return;

            Settings.Default.RestoreBounds = new Rectangle(Left, Top, Width, Height);
            Settings.Default.Save();
        }

        /// <summary>
        /// Is a significant (100 x 100) portion of the form on-screen?
        /// </summary>
        /// <returns></returns>
        private static bool IsOnScreen(Rectangle rect)
        {
            var screens = Screen.AllScreens;
            var formTopLeft = new Rectangle(rect.Left, rect.Top, 100, 100);

            return screens.Any(screen => screen.WorkingArea.Contains(formTopLeft));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Control.ModifierKeys == Keys.Control)
            {
                _controlKeyEvent.Raise(keyData);
                //this event system doesn't actually give us a return value,, so we don't know if it was handled or not
                //so we'll always just let it bubble. If that becomes a problem, we'll need a different design.
                //return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        public void StartMeasuringPerformance()
        {
            PerformanceMeasurement.Global.StartMeasuring();
            UpdatePerformanceMeasurementStatus();
            ShowPerformancePage();
        }

        public void ShowPerformancePage()
        {
            ProcessExtra.SafeStartInFront(
                BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                    + "performance/PerformanceLogPage.htm"
            );
        }

        public bool GetAlwaysMeasurePerformance() => Settings.Default.AlwaysMeasurePerformance;

        public void SetAlwaysMeasurePerformance(bool value)
        {
            Settings.Default.AlwaysMeasurePerformance = value;
            Settings.Default.Save();
            UpdatePerformanceMeasurementStatus();
        }

        public bool GetIsMeddlingWithNewFiles() => FileMeddlerManager.IsMeddling;

        public void SetIsMeddlingWithNewFiles(bool value)
        {
            if (value == FileMeddlerManager.IsMeddling)
            {
                return;
            }

            if (value)
            {
                FileMeddlerManager.Start(_collectionSettings?.FolderPath);
            }
            else
            {
                FileMeddlerManager.Stop();
            }
        }

        /// <summary>
        /// Records the user's choice between bloomlibrary.org and dev.bloomlibrary.org, then
        /// restarts Bloom if the choice differs from the web site of this run.  A restart is
        /// necessary because the upload destination and the login belong to one run only.
        /// </summary>
        /// <remarks>
        /// The restart waits for the idle loop, as the change of the user interface language
        /// does in WorkspaceView.SetUiLanguage.  We are on the user interface thread inside an
        /// API request that holds the server's lock, and a restart closes the collection, which
        /// makes more API requests.
        /// </remarks>
        public void SetUseDevBloomLibrary(bool useDevSite)
        {
            if (!BookUpload.SetUserChoiceOfDevWebSite(useDevSite))
                return;
            Application.Idle -= RestartForWebSiteChange;
            Application.Idle += RestartForWebSiteChange;
        }

        private void RestartForWebSiteChange(object sender, EventArgs e)
        {
            Application.Idle -= RestartForWebSiteChange;
            Program.RestartBloom(false);
        }

        private void UpdatePerformanceMeasurementStatus()
        {
            if (
                Settings.Default.AlwaysMeasurePerformance
                && !PerformanceMeasurement.Global.CurrentlyMeasuring
            )
            {
                PerformanceMeasurement.Global.StartMeasuring();
            }
        }
    }
}
