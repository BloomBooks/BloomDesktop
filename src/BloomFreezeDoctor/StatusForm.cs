using System.Diagnostics;

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
        layout.Controls.Add(_restartBloom, 0, 2);

        // The card asks for this to be revealed by holding CTRL, so it is available for testing and for
        // the "Bloom is merely slow" support case without inviting everyday use.
        _reportNow.Text = "Report now";
        _reportNow.AutoSize = true;
        _reportNow.Visible = false;
        _reportNow.Click += (_, _) => ReportNow();
        layout.Controls.Add(_reportNow, 1, 2);

        Controls.Add(layout);

        _tray.Icon = SystemIcons.Information;
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

        if (startMinimised)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
        }
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
            try
            {
                BeginInvoke(() => OnReportFiled(sender, issueId));
            }
            catch (Exception) { }
            return;
        }

        _restartBloom.Visible = true;
        _lastEvent.Text = $"Reported as {issueId}.";
        // A balloon tip is native to WinForms and needs neither an AppUserModelID nor a registered COM
        // activator, which is what made a toast with a button expensive before we had a real window.
        _tray.BalloonTipTitle = "Bloom problem reported";
        _tray.BalloonTipText =
            $"The Freeze Doctor sent a report about Bloom ({issueId}). You can restart Bloom from the "
            + "Freeze Doctor window.";
        _tray.ShowBalloonTip(10_000);
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
        try
        {
            Process.Start(new ProcessStartInfo(_bloomExeToRestart) { UseShellExecute = true });
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

    /// <summary>Remembers a Bloom's path while we can still see it, for the restart button later.</summary>
    public void RememberBloomPath(string exePath) => _bloomExeToRestart ??= exePath;

    private static string? FindBloomExecutable()
    {
        // The installed layout: %LOCALAPPDATA%\Bloom{Channel}\current\Bloom{Channel}.exe
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var folder in new[] { "Bloom", "BloomBeta", "BloomAlpha", "BloomBetaInternal" })
        {
            var candidate = Path.Combine(local, folder, "current", folder + ".exe");
            if (File.Exists(candidate))
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
            var issue = await _supervisor
                .ReportNowAsync(target.Id, CancellationToken.None)
                .ConfigureAwait(false);
            if (issue != null)
                OnReportFiled(this, issue);
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

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);
}
