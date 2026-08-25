using System.Diagnostics;
using SIL.IO;

namespace BloomFreezeDoctor;

/// <summary>
/// The small status window the card asked for: what Bloom is doing, whether anything is waiting to be
/// sent, and a way to restart Bloom after a report.
///
/// Deliberately plain. It is English-only (decision D1), it minimises to the tray rather than to the task
/// bar so it can sit out of the way all day, and when Bloom launches it, it starts minimised — a window
/// that appeared every time you started Bloom would get the Doctor uninstalled inside a week.
///
/// Its one real requirement is that it stay responsive while the Doctor works, so it does nothing here
/// but render what the supervisor publishes.
/// </summary>
public sealed class StatusForm : Form
{
    private readonly DoctorSupervisor _supervisor;
    private readonly Label _status = new();
    private readonly Label _lastEvent = new();
    private readonly Button _restartBloom = new();
    private readonly Button _showReport = new();
    private readonly Button _reportNow = new();
    private readonly NotifyIcon _tray = new();
    private readonly System.Windows.Forms.Timer _ctrlWatcher = new();

    private string? _bloomExeToRestart;

    /// <summary>Creates the window and wires it to the supervisor.</summary>
    public StatusForm(DoctorSupervisor supervisor, bool startMinimised)
    {
        _supervisor = supervisor;

        Text = "Bloom Freeze Doctor";
        // Small, and small enough to stay out of the way; the user can resize it if they want.
        ClientSize = new Size(460, 190);
        MinimumSize = new Size(360, 170);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        // Never steal focus: this window can appear while the user is working in Bloom.
        // (Setting it here rather than fighting Activated events later.)
        TopMost = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(12),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.Dock = DockStyle.Fill;
        _status.Text = "Bloom Status: looking…";
        _status.Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 10f, FontStyle.Regular);
        layout.Controls.Add(_status, 0, 0);
        layout.SetColumnSpan(_status, 2);

        _lastEvent.Dock = DockStyle.Fill;
        _lastEvent.AutoEllipsis = true;
        _lastEvent.ForeColor = SystemColors.GrayText;
        layout.Controls.Add(_lastEvent, 0, 1);
        layout.SetColumnSpan(_lastEvent, 2);

        _restartBloom.Text = "Restart Bloom";
        _restartBloom.AutoSize = true;
        _restartBloom.Visible = false;
        _restartBloom.Click += (_, _) => RestartBloom();

        // A real button, because the two things that used to lead to a gathered report were both
        // ephemeral: a balloon tip that goes away after ten seconds (or the moment you click anywhere
        // else), and a line of text that the next status update overwrites. Between them, a report could
        // be gathered perfectly and then be unreachable from the window a minute later - which is what
        // happened. A button survives status updates, and closing and reopening the window from the tray.
        _showReport.Text = "Show report";
        _showReport.AutoSize = true;
        _showReport.Visible = false;
        _showReport.Click += (_, _) => OpenSavedReportFolder();

        // Both in a strip, so adding one did not have to disturb the grid or move "Report now" off the
        // right-hand side.
        var leftButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
        };
        leftButtons.Controls.Add(_restartBloom);
        leftButtons.Controls.Add(_showReport);
        layout.Controls.Add(leftButtons, 0, 2);

        // The card asks for this to be revealed by holding CTRL, so it is available for testing and for
        // the "Bloom is merely slow" support case without inviting everyday use.
        _reportNow.Text = "Report now";
        _reportNow.AutoSize = true;
        _reportNow.Visible = false;
        _reportNow.Click += (_, _) => ReportNow();
        layout.Controls.Add(_reportNow, 1, 2);

        Controls.Add(layout);

        // Bloom's own icon rather than a system one. This icon lives in the notification area, where it
        // is the ONLY thing telling somebody the Doctor is running - and a generic blue "i" says nothing
        // about Bloom, so it gets overlooked among every other tray icon (it was, first time out).
        // Reading it back off our own exe, which carries it via <ApplicationIcon>, means the tray icon
        // and the exe's icon cannot drift apart. Extracted twice rather than shared, so that whichever
        // of the form and the tray is torn down first cannot leave the other holding a disposed handle.
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _tray.Text = "Bloom Freeze Doctor";
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Quit", null, (_, _) => Close());
        _tray.ContextMenuStrip = menu;

        // CTRL is polled rather than key-hooked, because the window is usually not focused when someone
        // wants this — they have been staring at a frozen Bloom.
        _ctrlWatcher.Interval = 200;
        _ctrlWatcher.Tick += (_, _) =>
            _reportNow.Visible = (ModifierKeys & Keys.Control) == Keys.Control;
        _ctrlWatcher.Start();

        _supervisor.StatusChanged += OnStatusChanged;
        _supervisor.ReportFiled += OnReportFiled;
        _supervisor.ReportSavedWithoutFiling += OnReportSavedWithoutFiling;
        _supervisor.ZombieEnded += OnZombieEnded;
        // Clicking the balloon is the obvious thing to do when it tells you a report was saved, so make
        // that open the folder. No new button, and therefore no change to the window's layout.
        _tray.BalloonTipClicked += (_, _) => OpenSavedReportFolder();

        if (startMinimised)
        {
            // Not merely minimised: not shown at all. See SetVisibleCore.
            _stayHidden = true;
            ShowInTaskbar = false;
        }
    }

    /// <summary>
    /// True while the window must not appear, however anyone asks. Cleared by <see cref="RevealYourself"/>.
    /// </summary>
    private bool _stayHidden;

    /// <summary>
    /// Keeps the window genuinely invisible until the Doctor has something to say.
    ///
    /// This is the whole of "no UI until it does something". Starting minimised was not enough: a
    /// minimised window still exists, still owns a taskbar entry for a moment as it is created, and can
    /// flash on screen before it minimises. <c>Application.Run(form)</c> shows its form unconditionally,
    /// so refusing here is the only reliable way to say no - WinForms routes every path that would make a
    /// form visible, including that one, through SetVisibleCore.
    ///
    /// The tray icon stays, deliberately. It is the one affordance by which somebody can tell the Doctor
    /// is running at all, quit it, or hold CTRL to file a report on a Bloom that is merely being slow -
    /// and a tray icon is about as unobtrusive as a running program can be while remaining reachable.
    /// </summary>
    protected override void SetVisibleCore(bool value)
    {
        // Force the window handle into existence on the first call, even though we are about to refuse
        // to become visible. This is not ceremony: Application.Run(form) sets Visible = true, and if that
        // is simply denied the form never gets a handle - so the message loop has no window, exits
        // immediately, and the Doctor dies a few milliseconds after Bloom starts it, tray icon and all.
        // No test would catch that, because nothing unit-tests a message loop.
        if (!IsHandleCreated)
        {
            CreateHandle();
            // Only the hidden-start case refuses here. A Doctor launched by a person, rather than by
            // Bloom, has _stayHidden false and must appear as usual - they went looking for it.
            if (_stayHidden)
            {
                base.SetVisibleCore(false);
                return;
            }
        }
        var showing = value && !_stayHidden;
        // Every route that makes this form visible or invisible comes through here, which makes it the one
        // place worth recording it - the supervisor's thread cannot safely read Control.Visible.
        _windowIsShowing = showing;
        base.SetVisibleCore(showing);
    }

    /// <summary>
    /// Lets the window be seen, and shows it. Called when the Doctor actually has something to report, so
    /// the first time a user sees this window is a time when it has something to tell them.
    /// </summary>
    private void RevealYourself()
    {
        if (!_stayHidden)
            return;
        _stayHidden = false;
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Show();
    }

    /// <inheritdoc />
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Bottom-right of the working area, out of the way of whatever the user is doing.
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
    }

    /// <summary>Minimising hides the window into the tray, which is what "shrink it away" means here.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
            ShowInTaskbar = false;
    }

    private void RestoreFromTray()
    {
        // Asking for the window from the tray is an explicit request, so it overrides the
        // stay-hidden rule rather than being silently refused by SetVisibleCore.
        RevealYourself();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void OnStatusChanged(object? sender, DoctorStatus status)
    {
        // The supervisor publishes from worker threads; marshal before touching controls.
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(() => OnStatusChanged(sender, status));
            }
            catch (Exception)
            {
                // The window may be closing; nothing to do.
            }
            return;
        }

        var lines = status.BloomLines.ToList();
        if (status.OutboxLine != null)
            lines.Add("");
        if (status.OutboxLine != null)
            lines.Add(status.OutboxLine);
        _status.Text = string.Join(Environment.NewLine, lines);
        _lastEvent.Text = status.LastEvent ?? "";
        _tray.Text = Truncate(
            "Bloom Freeze Doctor — " + (status.BloomLines.FirstOrDefault() ?? ""),
            63
        );
    }

    private void OnReportFiled(object? sender, string issueId)
    {
        if (InvokeRequired)
        {
            OnUiThread(() => OnReportFiled(sender, issueId));
            return;
        }

        // A filed report supersedes any saved-but-unfiled one, so neither the balloon nor the button
        // should still be offering to open that older folder.
        _savedReportFolder = null;
        _showReport.Visible = false;
        _restartBloom.Visible = true;
        _lastEvent.Text = $"Reported as {issueId}.";
        // Something has happened, so now the window may be seen. Until this moment the Doctor has been
        // nothing but a tray icon.
        RevealYourself();
        // A balloon tip is native to WinForms and needs neither an AppUserModelID nor a registered COM
        // activator, which is what made a toast with a button expensive before we had a real window.
        _tray.BalloonTipTitle = "Bloom problem reported";
        _tray.BalloonTipText =
            $"The Freeze Doctor sent a report about Bloom ({issueId}). You can restart Bloom from the "
            + "Freeze Doctor window.";
        _tray.ShowBalloonTip(10_000);
    }

    /// <summary>Where the last unfiled report was saved, for the balloon to open. Null if there isn't one.</summary>
    private string? _savedReportFolder;

    /// <summary>Greater than zero while the user is part-way through something with this window.</summary>
    private int _busyWithTheUser;

    /// <summary>
    /// Runs something on the UI thread, holding off the Doctor's own exit until it has actually run.
    ///
    /// The hold is taken HERE, on the calling thread, and that is the whole point. The supervisor raises
    /// these events from a worker, and for a CRASHED Bloom the process is already gone by the time there
    /// is anything to say - so "nothing left to watch" becomes true in the same breath as the report. Take
    /// the hold only inside the queued action and the exit has already been decided by the time it runs;
    /// what the user sees then is a window that appears and vanishes, or never paints at all.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (!InvokeRequired)
        {
            action();
            return;
        }
        Interlocked.Increment(ref _busyWithTheUser);
        try
        {
            BeginInvoke(() =>
            {
                try
                {
                    action();
                }
                finally
                {
                    Interlocked.Decrement(ref _busyWithTheUser);
                }
            });
        }
        catch (Exception)
        {
            // The window has gone; there is nothing to show and nothing to hold open for.
            Interlocked.Decrement(ref _busyWithTheUser);
        }
    }

    /// <summary>True while the window is actually on screen. Maintained in <see cref="SetVisibleCore"/>.</summary>
    private volatile bool _windowIsShowing;

    /// <summary>
    /// True when the Doctor must not quit yet, whatever the supervisor thinks.
    ///
    /// Two cases, and the first is the one that bit us: a restart is in flight, where ending the old Bloom
    /// is precisely what makes the Doctor think its work is done. The second is broader - the window is on
    /// screen, so somebody is looking at it, and a program that vanishes mid-sentence while you are reading
    /// it is indistinguishable from one that crashed. The Doctor only ever shows the window when it has
    /// something to say, so this cannot keep it alive during ordinary running.
    ///
    /// Read from the supervisor's thread, so both parts are written accordingly.
    /// </summary>
    internal bool MustNotQuitYet => Volatile.Read(ref _busyWithTheUser) > 0 || _windowIsShowing;

    /// <summary>
    /// A report was gathered and deliberately not filed. Says so, and says where it went.
    ///
    /// The window is revealed for exactly the reason a filed report reveals it: the Doctor has done its
    /// job, and this is the case where somebody is most likely to be watching for that - a developer
    /// testing the thing. Before this, such a run ended in silence and looked like a failure.
    /// </summary>
    private void OnReportSavedWithoutFiling(object? sender, string folder)
    {
        if (InvokeRequired)
        {
            OnUiThread(() => OnReportSavedWithoutFiling(sender, folder));
            return;
        }

        _savedReportFolder = folder;
        _showReport.Visible = true;
        _restartBloom.Visible = true;
        // Worth setting even though the next status update will overwrite it: it names the folder, which
        // the button cannot. The button is what makes the report reachable afterwards.
        _lastEvent.Text = $"Report saved, not sent: {folder}";
        RevealYourself();
        _tray.BalloonTipTitle = "Bloom problem found — report saved, not sent";
        _tray.BalloonTipText =
            "The Freeze Doctor gathered a full report about Bloom but did not send it, because this is a "
            + "developer or automation build. Click here to open the folder it was saved in.";
        _tray.ShowBalloonTip(10_000);
    }

    /// <summary>
    /// Opens the folder holding the last unfiled report. Does nothing if there is none, or if it has since
    /// been sent or removed - the balloon that offers this is not necessarily the one still on screen.
    /// </summary>
    private void OpenSavedReportFolder()
    {
        var folder = _savedReportFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Failing to open a file manager is not worth interrupting anyone over; the path is on screen
            // in the window as well.
        }
    }

    /// <summary>
    /// Restarts Bloom, using the executable path of a Bloom we were watching. We remember it because by
    /// the time the user presses this, the process it came from is usually gone.
    /// </summary>
    private void RestartBloom()
    {
        _bloomExeToRestart ??= FindBloomExecutable();
        if (_bloomExeToRestart == null)
        {
            MessageBox.Show(
                this,
                "The Freeze Doctor could not work out where Bloom is installed, so it cannot start it for "
                    + "you. Please start Bloom the way you normally do.",
                "Bloom Freeze Doctor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }
        // Hold off our own "nothing left to do" exit until Bloom has actually been started. This is not
        // belt-and-braces: ending the old Bloom is itself the thing that leaves the Doctor with nothing
        // to watch, so without this the Doctor exits in the middle of the restart it was asked for,
        // Application.Exit takes the confirmation dialog down with it unread, and Bloom never starts.
        // Which is exactly what happened the first time this was tried.
        Interlocked.Increment(ref _busyWithTheUser);
        var handedOff = false;
        try
        {
            // Bloom is single-instance, so a new one cannot start while an old one is still holding the
            // token: it starts, finds it held, and exits a few seconds later. Starting one regardless is
            // worse than doing nothing, because what the user sees is "Bloom will not start" - the very
            // complaint that brought them to the Doctor.
            var inTheWay = _supervisor.LiveWatchedBlooms();
            if (inTheWay.Count == 0)
            {
                StartBloomNow();
                return;
            }

            if (!AskPermissionToEnd(inTheWay))
                return;

            // Ending waits for the process to actually go, so it cannot happen on this thread: a Freeze
            // Doctor whose own window went white while it worked would be its own worst advertisement.
            _restartBloom.Enabled = false;
            _lastEvent.Text = "Ending the old Bloom…";
            var ids = inTheWay.Select(bloom => bloom.ProcessId).ToList();
            handedOff = true;
            Task.Run(() =>
                {
                    foreach (var id in ids)
                        ZombieEnder.End(id);
                })
                .ContinueWith(_ => FinishRestart());
        }
        finally
        {
            // The asynchronous path releases this itself, once Bloom is started.
            if (!handedOff)
                Interlocked.Decrement(ref _busyWithTheUser);
        }
    }

    /// <summary>
    /// Starts Bloom back on the UI thread once whatever was in the way has gone, and releases the hold on
    /// the Doctor's exit either way - including when the window has vanished from under us, where nothing
    /// is going to start Bloom and continuing to hold the Doctor open would achieve nothing.
    /// </summary>
    private void FinishRestart()
    {
        try
        {
            BeginInvoke(() =>
            {
                try
                {
                    _restartBloom.Enabled = true;
                    StartBloomNow();
                }
                finally
                {
                    Interlocked.Decrement(ref _busyWithTheUser);
                }
            });
        }
        catch (Exception)
        {
            Interlocked.Decrement(ref _busyWithTheUser);
        }
    }

    /// <summary>
    /// Asks before ending the Bloom that is in the way.
    ///
    /// **This button ends a FROZEN Bloom too, which was decided deliberately.** The reasoning: a frozen
    /// Bloom cannot save anything anyway, so refusing mostly leaves the user unable to start Bloom at
    /// all. The warning still names the one thing genuinely given up - a frozen Bloom does sometimes
    /// start responding again by itself, as one did during testing - so whoever clicks decides with that
    /// in front of them.
    ///
    /// Note this is the EXPLICIT path only. The Doctor's own automatic policy (<see cref="ZombieEnder"/>)
    /// is untouched: it still refuses, by itself, to end a frozen Bloom or one under a debugger.
    /// </summary>
    private bool AskPermissionToEnd(IReadOnlyList<LiveBloom> inTheWay)
    {
        var which = string.Join(", ", inTheWay.Select(bloom => bloom.ProcessId));
        var message =
            $"Bloom (process {which}) is still running, and Bloom will not start a second copy, so a new "
            + "Bloom cannot start until that one ends.\n\nEnd it and start a new Bloom?";
        if (inTheWay.Any(bloom => bloom.State != TargetState.Zombie))
        {
            message +=
                "\n\nThat Bloom is frozen rather than window-less. Anything it has not saved will be lost "
                + "- though a frozen Bloom cannot save anything anyway. Be aware that a frozen Bloom does "
                + "sometimes start responding again on its own.";
        }
        return MessageBox.Show(
                this,
                message,
                "Bloom Freeze Doctor",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            ) == DialogResult.Yes;
    }

    /// <summary>Starts Bloom, once anything in the way has gone.</summary>
    private void StartBloomNow()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_bloomExeToRestart!) { UseShellExecute = true });
            _restartBloom.Visible = false;
        }
        catch (Exception e)
        {
            MessageBox.Show(
                this,
                $"Bloom could not be started: {e.Message}",
                "Bloom Freeze Doctor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        }
    }

    /// <summary>
    /// Says what came of ending a stuck Bloom. This event had no listener at all, so the Doctor could end
    /// a process on the user's behalf and never mention it - the same silence that made a successful
    /// gather look like a failure.
    /// </summary>
    private void OnZombieEnded(object? sender, ZombieEndOutcome outcome)
    {
        if (InvokeRequired)
        {
            OnUiThread(() => OnZombieEnded(sender, outcome));
            return;
        }

        _lastEvent.Text = outcome switch
        {
            ZombieEndOutcome.ExitedOnRequest => "The stuck Bloom was asked to close, and did.",
            ZombieEndOutcome.Killed => "The stuck Bloom did not respond, so it was ended.",
            ZombieEndOutcome.AlreadyGone => "The stuck Bloom had already gone.",
            _ => "The stuck Bloom could not be ended; you may need to end it in Task Manager.",
        };
        _restartBloom.Visible = true;
        RevealYourself();
    }

    /// <summary>Remembers a Bloom's path while we can still see it, for the restart button later.</summary>
    public void RememberBloomPath(string exePath) => _bloomExeToRestart ??= exePath;

    private static string? FindBloomExecutable()
    {
        // The installed layout: %LOCALAPPDATA%\Bloom{Channel}\current\Bloom{Channel}.exe
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var folder in new[] { "Bloom", "BloomBeta", "BloomAlpha", "BloomBetaInternal" })
        {
            var candidate = Path.Combine(local, folder, "current", folder + ".exe");
            if (RobustFile.Exists(candidate))
                return candidate;
        }
        return null;
    }

    private void ReportNow()
    {
        var target = Process.GetProcessesByName("Bloom").FirstOrDefault();
        if (target == null)
        {
            MessageBox.Show(
                this,
                "Bloom does not appear to be running, so there is nothing to report on.",
                "Bloom Freeze Doctor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }
        _lastEvent.Text = "Gathering a report on request…";
        _ = Task.Run(async () =>
        {
            var result = await _supervisor
                .ReportNowAsync(target.Id, CancellationToken.None)
                .ConfigureAwait(false);
            if (result.IssueId != null)
            {
                OnReportFiled(this, result.IssueId);
            }
            else if (result.Queued)
            {
                // Gathered and safely on disk, just not sent yet: offline, over the daily limit, or
                // another Doctor is draining the queue. Saying so matters, because the alternative was
                // saying nothing at all and leaving "Gathering a report…" on screen, which reads as a
                // failure and invites the user to press it again.
                SayOnTheUiThread(
                    "Report saved. It will be sent when the Freeze Doctor can reach the tracker."
                );
            }
            else
            {
                SayOnTheUiThread("The report could not be gathered.");
            }
        });
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ctrlWatcher.Stop();
            _ctrlWatcher.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Puts a line in the window's status area from whatever thread we happen to be on. The report work
    /// runs on a worker thread, so this cannot touch the control directly.
    /// </summary>
    private void SayOnTheUiThread(string text)
    {
        try
        {
            if (InvokeRequired)
                BeginInvoke(() => _lastEvent.Text = text);
            else
                _lastEvent.Text = text;
        }
        catch (Exception)
        {
            // The window is going away. Losing a status line at that point costs nothing.
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);
}
