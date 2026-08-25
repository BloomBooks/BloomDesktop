using System.Diagnostics;
// Aliased rather than imported plainly, so uses below still read as Protocol.DoctorSignals - it is worth
// saying at each use that this is the shared wire format, not something local to the Doctor.
using Protocol = BloomFreezeDoctor.Protocol;

namespace BloomFreezeDoctor;

/// <summary>What the watcher knows about the Bloom it is watching, for the report's header.</summary>
public sealed record BloomTargetFacts
{
    /// <summary>The process id.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Full path to Bloom.exe.</summary>
    public required string ExePath { get; init; }

    /// <summary>Release channel, derived from <see cref="ExePath"/>.</summary>
    public required string Channel { get; init; }

    /// <summary>The command line, which says whether this is an automation or headless run.</summary>
    public required string CommandLine { get; init; }

    /// <summary>When the process started, used to identify its log file.</summary>
    public required DateTime StartTime { get; init; }

    /// <summary>
    /// True when this Bloom must never produce a filed report: a developer build, or an automation
    /// run. We still gather (and write to disk), because that is how we test the gatherer.
    /// </summary>
    public bool NeverFile =>
        BloomChannel.IsDeveloperChannel(Channel)
        || BloomChannel.IsHeadlessOrAutomationRun(CommandLine);
}

/// <summary>Raised when the detector decides this Bloom is worth reporting.</summary>
public sealed class ReportWantedEventArgs : EventArgs
{
    /// <summary>The Bloom in question.</summary>
    public required BloomTargetFacts Target { get; init; }

    /// <summary>What the detector concluded, and why.</summary>
    public required DetectorVerdict Verdict { get; init; }

    /// <summary>
    /// True if a report may actually be filed. False for developer and automation runs, which are
    /// gathered to disk and no further — the guard that keeps our own daily work off the tracker.
    /// </summary>
    public required bool MayFile { get; init; }
}

/// <summary>
/// Watches one Bloom process: takes a reading every second, feeds it to a <see cref="FreezeDetector"/>,
/// and raises <see cref="ReportWanted"/> when there is something to report.
///
/// Runs on a background timer, never on a UI thread. That is a requirement rather than a preference:
/// the Doctor has a window of its own (decision D1), and a Doctor whose window goes white while it
/// diagnoses a freeze would be its own worst advertisement.
/// </summary>
public sealed class BloomTargetWatcher : IDisposable
{
    private readonly ITargetProbe _probe;
    private readonly FreezeDetector _detector;
    private readonly Stopwatch _monotonic = Stopwatch.StartNew();
    private readonly TimeSpan _cadence;
    private Timer? _timer;

    /// <summary>
    /// Held open for as long as we watch, so Bloom can tell instantly whether anyone is listening before it
    /// pauses a crash to ask for a dump.
    /// </summary>
    private EventWaitHandle? _watchingSignal;

    /// <summary>
    /// The two halves of the crash-dump handshake, created and held here for as long as we watch.
    ///
    /// **They have to be created by US, and nothing used to create them at all.** Every other accessor in
    /// DoctorSignals goes through TryOpenExisting, so with no creator: Bloom's request to be dumped found
    /// no event and gave up, and our own check for a request could only ever read false. The handshake
    /// could not fire at either end, which made the crash-dump path dead code - silently, in exactly the
    /// case the feature exists for. The quit handshake works only because Bloom happens to create ITS
    /// event and hold it while waiting.
    ///
    /// We are the right owner: our lifetime spans the crash, Bloom's does not, and Bloom already asks
    /// whether we are watching before it pauses for anything.
    /// </summary>
    private EventWaitHandle? _dumpRequest;
    private EventWaitHandle? _dumpComplete;

    /// <summary>Guards against a slow reading overlapping the next tick.</summary>
    private int _observing;

    /// <summary>Creates a watcher for a Bloom already identified.</summary>
    public BloomTargetWatcher(
        BloomTargetFacts target,
        ITargetProbe probe,
        DetectorThresholds? thresholds = null,
        TimeSpan? cadence = null
    )
    {
        Target = target;
        _probe = probe;
        _detector = new FreezeDetector(thresholds);
        _cadence = cadence ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>The Bloom being watched.</summary>
    public BloomTargetFacts Target { get; }

    /// <summary>The detector's current opinion.</summary>
    public TargetState State => _detector.State;

    /// <summary>True once this target has been seen under a debugger and can never be reported.</summary>
    public bool IsPoisonedByDebugger => _detector.IsPoisonedByDebugger;

    /// <summary>Raised on the watcher's background thread when a report is wanted.</summary>
    public event EventHandler<ReportWantedEventArgs>? ReportWanted;

    /// <summary>Raised whenever an observation is taken, for the status window to render.</summary>
    public event EventHandler<DetectorVerdict>? Observed;

    /// <summary>Begins watching.</summary>
    public void Start()
    {
        // Announce that we are watching this Bloom, so that Bloom can find out with a zero timeout whether
        // it is worth pausing a crash to ask us for a dump. Held open for as long as we watch.
        _watchingSignal ??= Protocol.DoctorSignals.TryCreate(
            Protocol.DoctorSignals.WatchingName(Target.ProcessId)
        );
        // Both halves of the dump handshake, created before Bloom could possibly need them. See the fields.
        _dumpRequest ??= Protocol.DoctorSignals.TryCreate(
            Protocol.DoctorSignals.DumpRequestName(Target.ProcessId)
        );
        _dumpComplete ??= Protocol.DoctorSignals.TryCreate(
            Protocol.DoctorSignals.DumpCompleteName(Target.ProcessId)
        );
        _timer ??= new Timer(_ => Tick(), null, TimeSpan.Zero, _cadence);
    }

    /// <summary>
    /// True if Bloom has asked us to dump it because it is crashing. Checked on each tick rather than from a
    /// dedicated thread: Bloom waits about three seconds, so a once-a-second check is quick enough, and a
    /// thread per watched Bloom is a cost with no return.
    /// </summary>
    public bool DumpRequested()
    {
        try
        {
            // The handle we created and hold, not a fresh TryOpen - which is what made this permanently
            // false, since nothing had created the event for it to find.
            if (_dumpRequest == null || !_dumpRequest.WaitOne(TimeSpan.Zero))
                return false;
            // Manual-reset, so clear it now we have taken it. The supervisor separately allows one dump per
            // process, but leaving this set would have every later tick believe a fresh request had arrived.
            _dumpRequest.Reset();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Tells Bloom the dump is done, so it can stop waiting and get on with dying.</summary>
    public void SignalDumpComplete()
    {
        try
        {
            _dumpComplete?.Set();
        }
        catch (Exception)
        {
            // Bloom's wait times out in about three seconds either way; it must not be our failure that
            // holds a dying process open.
        }
    }

    /// <summary>When this target was first judged a zombie, for the grace period in <see cref="ZombieEnder"/>.</summary>
    public DateTimeOffset? ZombieSince { get; private set; }

    /// <summary>
    /// Takes one reading and acts on it. Public so a test can drive the watcher deterministically
    /// instead of waiting on a timer.
    /// </summary>
    public void Tick()
    {
        // A reading that overruns its slot must not stack up behind itself.
        if (Interlocked.Exchange(ref _observing, 1) == 1)
            return;
        try
        {
            var observation = _probe.Observe(_monotonic.Elapsed);
            var verdict = _detector.Observe(observation);
            Observed?.Invoke(this, verdict);

            // Remember when the zombie state began, so the grace period in ZombieEnder measures from the
            // right moment rather than from whenever someone got round to asking.
            if (verdict.State == TargetState.Zombie)
                ZombieSince ??= DateTimeOffset.UtcNow;
            else
                ZombieSince = null;

            if (!verdict.ShouldReport)
                return;

            // Four independent reasons never to file, checked here rather than left to the gatherer so
            // that the decision lives in one place: this target has been under a debugger at some point;
            // it is a developer or automation run; Bloom's own reporting has already told us about this
            // problem, in which case a second card is noise about the same trouble; or the freeze was
            // deliberately simulated, and nobody wants a card about a rehearsal.
            //
            // Any of these can still be overridden by the Doctor's `--force`, which exists to test the
            // filing path itself; see DoctorSupervisor, where that override is applied.
            var mayFile =
                !_detector.IsPoisonedByDebugger
                && !Target.NeverFile
                && !BloomAlreadyReported()
                && !WasDeliberatelySimulated();
            ReportWanted?.Invoke(
                this,
                new ReportWantedEventArgs
                {
                    Target = Target,
                    Verdict = verdict,
                    MayFile = mayFile,
                }
            );
        }
        catch (Exception)
        {
            // A watcher that throws stops watching, and then we learn nothing at all. Swallowing here
            // is deliberate, and is the reason ITargetProbe promises not to throw: this is the net
            // under that promise, not a substitute for it.
        }
        finally
        {
            Interlocked.Exchange(ref _observing, 0);
        }
    }

    /// <summary>
    /// True when Bloom has already reported a problem for this run. Bloom writes this into its session file
    /// the moment one of its own reports succeeds, so a user who filed a problem report by hand and a Doctor
    /// that noticed the same trouble do not produce two cards about it.
    ///
    /// Read fresh each time rather than cached: the interesting case is Bloom reporting *while* we are
    /// deciding, which is precisely when the two would otherwise collide.
    /// </summary>
    private bool BloomAlreadyReported()
    {
        try
        {
            var session = Protocol.DoctorSessionStore.TryRead(Target.ProcessId);
            return session?.BloomAlreadyReported == true;
        }
        catch (Exception)
        {
            // If we cannot tell, err towards reporting: a duplicate card is a smaller loss than silence
            // about a real freeze.
            return false;
        }
    }

    /// <summary>
    /// True when this Bloom was deliberately told to break itself, which makes any freeze we see a
    /// rehearsal rather than news.
    ///
    /// Read fresh each time, and NOT captured when we adopt the process, because the ordering makes
    /// caching wrong: Bloom writes its session file, launches us, and only then arms the simulator - so at
    /// the moment we adopt a Bloom the marker is reliably absent. Reading it here, at the moment of
    /// decision, is the only way to see it at all.
    ///
    /// Note what this deliberately does NOT touch: detection, gathering, and the zombie-ending policy all
    /// behave exactly as they would for a real freeze. A simulated run that took a shortcut anywhere else
    /// would stop being a test of the thing it exists to test.
    /// </summary>
    private bool WasDeliberatelySimulated()
    {
        try
        {
            var session = Protocol.DoctorSessionStore.TryRead(Target.ProcessId);
            return !string.IsNullOrEmpty(session?.SimulatedFailure);
        }
        catch (Exception)
        {
            // Err towards reporting, as with BloomAlreadyReported: silence about a real freeze is the
            // costlier mistake, and `--force` is there for anyone who needs to override this either way.
            return false;
        }
    }

    /// <summary>Stops watching and releases the timer.</summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        // Closing this is how Bloom learns nobody is watching any more, so it stops pausing its crashes for
        // an answer that is not coming.
        _watchingSignal?.Dispose();
        _watchingSignal = null;
        // And the dump handshake goes with it: we are the only holder, so these are ours to release.
        _dumpRequest?.Dispose();
        _dumpRequest = null;
        _dumpComplete?.Dispose();
        _dumpComplete = null;
    }

    /// <summary>
    /// Gathers the facts about a running Bloom process. Returns null if the process went away while we
    /// were asking, which is entirely possible and not an error.
    /// </summary>
    public static BloomTargetFacts? DescribeProcess(Process process, string commandLine)
    {
        try
        {
            var exe = process.MainModule?.FileName ?? "";
            return new BloomTargetFacts
            {
                ProcessId = process.Id,
                ExePath = exe,
                Channel = BloomChannel.DeriveFromExePath(exe),
                CommandLine = commandLine,
                StartTime = process.StartTime,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}
