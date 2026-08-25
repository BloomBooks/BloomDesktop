using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
// Aliased rather than imported plainly, so uses below still read as Protocol.DoctorSignals - it is worth
// saying at each use that this is the shared wire format, not something local to the Doctor.
using Protocol = BloomFreezeDoctor.Protocol;

namespace BloomFreezeDoctor;

/// <summary>
/// Reads one observation of a real process. Split behind an interface so the watcher and detector can
/// be tested without a Bloom, and so the awkward Win32 details live in exactly one place.
/// </summary>
public interface ITargetProbe
{
    /// <summary>Takes a reading. Must never throw, and must never block for long.</summary>
    TargetObservation Observe(TimeSpan uptime);
}

/// <summary>
/// The real probe: asks Windows about a Bloom process without perturbing it. Everything here is
/// read-only, needs no special privilege for a process of our own user, and cannot leave the target in
/// a worse state than it found it — a property worth preserving deliberately, since the spike proved
/// how badly a suspending diagnostic can end (see docs/SPIKE-FINDINGS.md §6).
/// </summary>
public sealed class WindowsTargetProbe : ITargetProbe
{
    private readonly Process _process;

    /// <summary>
    /// Sticky, per the spike: a dead process cannot be asked whether it was debugged, and stopping the
    /// debugger is the most common thing a developer does all day. Once true, always true.
    /// </summary>
    private bool _everDebugged;

    /// <summary>
    /// The last debugger state Bloom published, remembered because a process that has died can no longer be
    /// asked and its shared page may already have gone with it.
    ///
    /// It is what covers a debugger *terminating* Bloom: that is a TerminateProcess, so Bloom never gets to
    /// record a detach, and the last thing it published still says a debugger was attached.
    /// </summary>
    private bool _debuggerAttachedWhenLastSeen;
    private TimeSpan? _debuggerLastDetachedAge;

    /// <summary>Creates a probe for an already-opened process handle.</summary>
    public WindowsTargetProbe(Process process)
    {
        _process = process;
    }

    /// <summary>True if a debugger has ever been seen attached to this process.</summary>
    public bool EverDebugged => _everDebugged;

    /// <summary>
    /// The exit code, once the process has gone. Available only because we have held a handle to it since
    /// before it died — which is the whole reason the Doctor wants to be running *before* the trouble
    /// starts. Returns false while it is still running, or if we never had the right to ask.
    /// </summary>
    public bool TryGetExitCode(out int exitCode)
    {
        exitCode = 0;
        try
        {
            if (!_process.HasExited)
                return false;
            exitCode = _process.ExitCode;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>When the process exited, if we saw it happen.</summary>
    public DateTime? ExitedAt
    {
        get
        {
            try
            {
                return _process.HasExited ? _process.ExitTime : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The window we consider Bloom's main window: visible, top-level, owned by this process, and the
    /// largest such — chosen explicitly rather than trusting <c>Process.MainWindowHandle</c>, which
    /// picks the first visible top-level window it happens to find. A healthy Bloom keeps a second,
    /// invisible top-level window all session (its splash screen is hidden rather than closed), so
    /// relying on .NET's choice is an ordering accident waiting to matter.
    /// </summary>
    public IntPtr FindMainWindow()
    {
        var best = IntPtr.Zero;
        var bestArea = -1L;
        foreach (var window in EnumerateTopLevelWindows(_process.Id))
        {
            if (!IsWindowVisible(window))
                continue;
            if (!GetWindowRect(window, out var rect))
                continue;
            var area =
                (long)Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            if (area <= bestArea)
                continue;
            bestArea = area;
            best = window;
        }
        return best;
    }

    /// <summary>
    /// Counts this process's visible top-level windows. "Visible" is the operative word: see
    /// <see cref="FindMainWindow"/>.
    /// </summary>
    public int CountVisibleWindows() =>
        EnumerateTopLevelWindows(_process.Id).Count(IsWindowVisible);

    /// <inheritdoc />
    public TargetObservation Observe(TimeSpan uptime)
    {
        var alive = IsStillAlive();
        if (!alive)
            return new TargetObservation
            {
                Uptime = uptime,
                IsAlive = false,
                WindowResponds = false,
                HasVisibleWindow = false,
                // The last thing Bloom managed to publish, not a fresh reading: there is nothing left to
                // read. A debugger still attached at that point is very likely what ended it.
                DebuggerAttachedNow = _debuggerAttachedWhenLastSeen,
                DebuggerEverAttached = _everDebugged,
                DebuggerLastDetachedAge = _debuggerLastDetachedAge,
            };

        SampleDebugger();

        var window = FindMainWindow();
        var responds = window != IntPtr.Zero && WindowAnswers(window);
        var published = ReadPublishedState();

        return new TargetObservation
        {
            Uptime = uptime,
            IsAlive = true,
            WindowResponds = responds,
            HasVisibleWindow = window != IntPtr.Zero,
            // Bloom's own answer wins over our guess when we have it: it is authoritative, it covers the
            // whole run rather than only the part we have been watching, and it sees a NATIVE debugger,
            // which `Debugger.IsAttached` alone does not.
            DebuggerAttachedNow = _debuggerAttachedWhenLastSeen,
            DebuggerEverAttached = _everDebugged,
            DebuggerLastDetachedAge = _debuggerLastDetachedAge,
            HeartbeatIsStale = published.HeartbeatIsStale,
            UiBlockCorroborated = published.UiBlockCorroborated,
            LongOperationInProgress = published.LongOperation,
            // Bloom's heartbeat age says how long the UI thread has ALREADY been stuck, which lets a
            // Doctor started because Bloom is frozen report at once rather than waiting out a threshold
            // that has in truth already passed.
            AlreadyUnresponsiveFor = published.UiBlockCorroborated ? published.StaleFor : null,
        };
    }

    /// <summary>
    /// What Bloom publishes about itself, if it publishes anything. Every Bloom in the field today
    /// publishes nothing, so this must be cheap and silent when the channel is absent — which it is: a
    /// failed `OpenExisting` and nothing more.
    /// </summary>
    private (
        bool HeartbeatIsStale,
        bool UiBlockCorroborated,
        bool LongOperation,
        TimeSpan StaleFor
    ) ReadPublishedState()
    {
        if (
            !Protocol.DoctorChannelReader.TryRead(_process.Id, out var snapshot)
            || snapshot == null
        )
        {
            // Note what is deliberately NOT done here: clearing PublishedSnapshot. The channel lives in
            // the process's own memory, so the read stops working the instant Bloom dies — which is
            // exactly when the last thing Bloom said about itself turns into evidence. Throwing it away
            // on that tick discarded it at the moment it became worth having.
            return (false, false, false, TimeSpan.Zero);
        }
        PublishedSnapshot = snapshot;

        // Bloom's sticky flag is better than our latch in two ways: it covers the whole run, including
        // before the Doctor started watching this Bloom, and it sees a native debugger as well as a managed
        // one. The departure time is what keeps "ever attached" from writing off the rest of the run.
        _debuggerAttachedWhenLastSeen = snapshot.DebuggerAttached;
        if (snapshot.DebuggerAttached || snapshot.DebuggerEverAttached)
            _everDebugged = true;
        if (snapshot.DebuggerLastDetachedAge != TimeSpan.MaxValue)
            _debuggerLastDetachedAge = snapshot.DebuggerLastDetachedAge;

        var uiStale = snapshot.UiHeartbeatAge > StaleHeartbeatThreshold;

        // The corroboration that makes a stale UI heartbeat believable: the watchdog thread is still
        // ticking, so the process is alive and scheduling threads and it is the UI thread *specifically*
        // that is stuck. Without this we could not tell a blocked UI thread from a starved timer, and
        // WM_TIMER — the lowest-priority message there is — really can be starved by a busy but live UI.
        var watchdogHealthy = snapshot.WatchdogHeartbeatAge <= StaleHeartbeatThreshold;
        var staleFor =
            snapshot.UiHeartbeatAge == TimeSpan.MaxValue ? TimeSpan.Zero : snapshot.UiHeartbeatAge;

        return (uiStale, uiStale && watchdogHealthy, snapshot.LongOperationInProgress, staleFor);
    }

    /// <summary>
    /// The most recent state Bloom published, or null if it never published any. Kept so the report can
    /// quote what Bloom said it was doing — and kept *after* the read stops working, because a report
    /// about a Bloom that has died can be gathered no other way: the channel is in the dead process's
    /// memory. See the note in <see cref="ReadPublishedState"/>.
    /// </summary>
    public Protocol.DoctorChannelSnapshot? PublishedSnapshot { get; private set; }

    /// <summary>
    /// How old a heartbeat has to be before we call it stale. Bloom ticks every 500 ms, so this is ten
    /// intervals — far beyond ordinary scheduling jitter, and it costs nothing to be generous because the
    /// detector needs a full minute of staleness before it reports anything.
    /// </summary>
    private static readonly TimeSpan StaleHeartbeatThreshold = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Asks the window to acknowledge a do-nothing message. This is the signal the spike settled on:
    /// <c>IsHungAppWindow</c> needs about five seconds to make up its mind, whereas this reacts at
    /// once. Neither can see a UI thread blocked in an STA managed wait — that needs Bloom's heartbeat.
    /// </summary>
    private static bool WindowAnswers(IntPtr window) =>
        SendMessageTimeout(
            window,
            WM_NULL,
            IntPtr.Zero,
            IntPtr.Zero,
            SMTO_ABORTIFHUNG,
            ProbeTimeoutMs,
            out _
        ) != IntPtr.Zero;

    /// <summary>
    /// Corroborating signal, kept for the report rather than for the decision: Windows' own opinion
    /// that the window has stopped pumping.
    /// </summary>
    public bool WindowsThinksItIsHung()
    {
        var window = FindMainWindow();
        return window != IntPtr.Zero && IsHungAppWindow(window);
    }

    private void SampleDebugger()
    {
        if (_everDebugged)
            return;
        try
        {
            var present = false;
            if (CheckRemoteDebuggerPresent(_process.Handle, ref present) && present)
                _everDebugged = true;
        }
        catch (Exception)
        {
            // Losing this reading is survivable; the channel check is the stronger guard anyway.
        }
    }

    private bool IsStillAlive()
    {
        try
        {
            return !_process.HasExited;
        }
        catch (Exception)
        {
            // We can lose the right to ask (a handle can be invalidated); treat that as gone rather
            // than crashing the watcher.
            return false;
        }
    }

    /// <summary>
    /// Every top-level window owned by a process. Public because the report's window inventory needs
    /// it too, and there should be exactly one implementation of this.
    /// </summary>
    public static IEnumerable<IntPtr> EnumerateTopLevel(int processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows(
            (window, _) =>
            {
                GetWindowThreadProcessId(window, out var owner);
                if (owner == processId)
                    windows.Add(window);
                return true;
            },
            IntPtr.Zero
        );
        return windows;
    }

    /// <summary>Whether a window is visible. See <see cref="FindMainWindow"/> for why this matters.</summary>
    public static bool IsVisible(IntPtr window) => IsWindowVisible(window);

    /// <summary>
    /// Whether a window accepts input. A disabled main window is how a modal dialog gives itself
    /// away — including one that is off-screen or behind the main window, which is a Bloom bug in its
    /// own right.
    /// </summary>
    public static bool IsEnabled(IntPtr window) => IsWindowEnabled(window);

    private static IEnumerable<IntPtr> EnumerateTopLevelWindows(int processId) =>
        EnumerateTopLevel(processId);

    /// <summary>Reads a window's title, for the report's window inventory.</summary>
    public static string TitleOf(IntPtr window)
    {
        var text = new StringBuilder(512);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    /// <summary>Reads a window's class name, which is how a modal dialog gives itself away.</summary>
    public static string ClassOf(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    #region interop

    /// <summary>
    /// Short on purpose. This runs on the watcher's cadence, and a probe that blocks for seconds
    /// would make the Doctor look as stuck as the thing it is watching.
    /// </summary>
    private const uint ProbeTimeoutMs = 1000;

    private const uint WM_NULL = 0x0000;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeoutMs,
        out IntPtr result
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CheckRemoteDebuggerPresent(IntPtr process, ref bool present);

    #endregion
}
