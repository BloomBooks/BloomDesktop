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
using Bloom.web.controllers;
using Bloom.Workspace;
using SIL.Extensions;
using SIL.Windows.Forms.PortableSettingsProvider;

namespace Bloom
{
    public partial class Shell : SIL.Windows.Forms.Miscellaneous.FormForUsingPortableClipboard
    {
        public static Form GetShellOrOtherOpenForm()
        {
            var form = Application.OpenForms.Cast<Form>().Where(x => x is Shell).FirstOrDefault();
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
            queueRenameOfCollection.Subscribe(
                newName => _nameToChangeCollectionUponClosing = newName.Trim().SanitizeFilename('-')
            );
            _collectionSettings = collectionSettings;
            _collectionClosingEvent = collectionClosingEvent;
            _controlKeyEvent = controlKeyEvent;
            _audioRecording = audioRecording;
            InitializeComponent();
            Activated += (sender, args) =>
            {
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

            _contextMenu.Opening += _contextMenu_Opening;

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
                            // calls OnClosing again as a result of calling Program.RestartBloom() which calls Application.Exit().
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
                        Close();
                    },
                    FailureAction = () =>
                    {
                        // We didn't want a second attempt at saving if the user clicks the close box while we are
                        // still trying to save after the first click on Close. But if the first attempt fails,
                        // we don't want to stay in a state where all attempts to close the program are ignored.
                        _startedClosingEvent = false;
                    }
                }
            );
            e.Cancel = true;
            base.OnClosing(e);
        }

        public void SetWindowText(string bookName)
        {
            // Let's only mark the window text for Alpha and Beta releases. It looks odd to have that in
            // release builds, and doesn't add much since we can treat Release builds as the unmarked case.
            // Note that developer builds now have a special "channel" marking as well to differentiate them
            // from true Release builds in screen shots.
            var formattedText = string.Format(
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
            Text = formattedText;
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

        /// <summary>
        /// Prevent the window sizes text/debug context menu from displaying anywhere but along the
        /// top tab strip of the workspace window.
        /// </summary>
        /// <remarks>
        /// See https://issues.bloomlibrary.org/youtrack/issue/BL-8668.  I never figured out why this
        /// menu started appearing more and more aggressively in the Edit tab where it wasn't wanted,
        /// but not in the other tabs.  After close to a day spent on this issue, I decided that this
        /// fix is good enough.
        /// </remarks>
        private void _contextMenu_Opening(object sender, CancelEventArgs e)
        {
            // In some ways, it would make more sense to have this context menu be attached
            // to the TabStrip object, but since all of the menu actions affect the Shell
            // object, it's just as easy to check its location before actually displaying
            // the menu.
            if (!_workspaceView.IsInTabStrip(this.PointToClient(_contextMenu.Location)))
                e.Cancel = true;
        }

        private void On800x600Click(object sender, EventArgs e)
        {
            Size = new Size(800, 600);
        }

        private void On1024x600Click(object sender, EventArgs e)
        {
            Size = new Size(1024, 600);
        }

        private void On1024x768(object sender, EventArgs e)
        {
            Size = new Size(1024, 768);
        }

        private void On1024x586(object sender, EventArgs e)
        {
            Size = new Size(1024, 586);
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
            //try really hard to become top most. See http://stackoverflow.com/questions/5282588/how-can-i-bring-my-application-window-to-the-front
            TopMost = true;
            Focus();
            BringToFront();
            TopMost = false;

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

                if (Settings.Default.WindowSizeAndLocation == null)
                {
                    StartPosition = FormStartPosition.WindowsDefaultLocation;
                    WindowState = FormWindowState.Maximized;
                    Settings.Default.WindowSizeAndLocation = FormSettings.Create(this);
                    Settings.Default.Save();
                }

                // This feature is not yet a normal part of Bloom, since we think just maximizing is more rice-farmer-friendly.
                // However, we added the ability to remember this stuff at the request of the person making videos, who needs
                // Bloom to open in the same place / size each time.
                if (Settings.Default.MaximizeWindow == false)
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
                    this.meddleWithNewFilesToolStripMenuItem.Text = "Stop Meddling with New Files";
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

        private void startMeasuringPerformanceToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PerformanceMeasurement.Global.StartMeasuring();
            UpdatePerformanceMeasurementStatus();
            // open in a browser
            this.showPerformancePageToolStripMenuItem_Click(sender, e);
        }

        private void showPerformancePageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ProcessExtra.SafeStartInFront(
                BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                    + "performance/PerformanceLogPage.htm"
            );
        }

        private void alwaysMeasureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings.Default.AlwaysMeasurePerformance = !Settings.Default.AlwaysMeasurePerformance;
            UpdatePerformanceMeasurementStatus();
        }

        private void meddleWithNewFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (FileMeddlerManager.IsMeddling)
            {
                FileMeddlerManager.Stop();
                meddleWithNewFilesToolStripMenuItem.Text = "Meddle with New Files";
            }
            else
            {
                FileMeddlerManager.Start(_collectionSettings?.FolderPath);
                meddleWithNewFilesToolStripMenuItem.Text = "Stop Meddling with New Files";
            }
        }

        private void UpdatePerformanceMeasurementStatus()
        {
            alwaysMeasureToolStripMenuItem.Checked = Settings.Default.AlwaysMeasurePerformance;

            if (
                Settings.Default.AlwaysMeasurePerformance
                && !PerformanceMeasurement.Global.CurrentlyMeasuring
            )
            {
                PerformanceMeasurement.Global.StartMeasuring();
            }

            this.startMeasuringPerformanceToolStripMenuItem.Enabled = !PerformanceMeasurement
                .Global
                .CurrentlyMeasuring;
            this.showPerformancePageToolStripMenuItem.Enabled = PerformanceMeasurement
                .Global
                .CurrentlyMeasuring;

            if (PerformanceMeasurement.Global.CurrentlyMeasuring)
            {
                startMeasuringPerformanceToolStripMenuItem.Text = "Currently Measuring Performance";
            }

            // if we're always measuring, don't offer to start/stop
            //this.startMeasuringPerformanceToolStripMenuItem.Enabled = !Settings.Default.AlwaysMeasurePerformance;
        }
    }
}
