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
    /// **They must be created by US.** Every accessor in DoctorSignals but TryCreate opens an existing
    /// event, so with no creator neither side could ever see the other: Bloom's request would find no event
    /// to set, and our check for one would always read false. With the handshake silently unable to fire,
    /// the whole crash-dump path would be dead code - in exactly the case the feature exists for.
    ///
    /// We are the right owner rather than Bloom: our lifetime spans the crash and Bloom's does not, and
    /// Bloom already asks whether we are watching before it pauses for anything.
    /// </summary>
    private EventWaitHandle? _dumpRequest;
    private EventWaitHandle? _dumpComplete;

    /// <summary>
    /// Told to Bloom the moment we take up its dump request, so that its wait can be generous once the work
    /// is genuinely underway and impatient when nobody has picked it up. See
    /// <see cref="Protocol.DoctorSignals.DumpStartedName"/>.
    /// </summary>
    private EventWaitHandle? _dumpStarted;

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
        _dumpStarted ??= Protocol.DoctorSignals.TryCreate(
            Protocol.DoctorSignals.DumpStartedName(Target.ProcessId)
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
            // The handle we created and hold, not a fresh TryOpen: nothing else creates this event.
            if (_dumpRequest == null || !_dumpRequest.WaitOne(TimeSpan.Zero))
                return false;
            // Manual-reset, so clear it now we have taken it, or every later tick would read the same
            // request as a fresh one.
            _dumpRequest.Reset();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Tells Bloom we have taken up its request and are working on it. Called before the work, which is the
    /// whole point: it is what turns Bloom's wait from a flat guess into "wait while this is really being
    /// done". Bloom gives up quickly if this never arrives.
    /// </summary>
    public void SignalDumpStarted()
    {
        try
        {
            _dumpStarted?.Set();
        }
        catch (Exception)
        {
            // Bloom then falls back to its short patience, which loses the dump on a slow machine but
            // cannot hang it. Failing safe in the right direction.
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

            var mayFile = MayFileAReport();
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
    /// Whether a report about this Bloom may actually be filed, as opposed to gathered to disk and kept.
    ///
    /// Four independent reasons never to file, and they live here — one definition, called by every path
    /// that files — rather than being restated at each one. They were restated at each one, and two paths
    /// got a shorter version: the crash-dump path and the exit examination each checked only the debugger
    /// and the channel, so a **deliberately simulated** crash on a channel where the simulator is allowed
    /// filed a real tracker card. Rehearsals reaching the tracker is the one outcome the simulated-failure
    /// guard exists to prevent, and the paths that ran when Bloom actually died were the ones without it.
    ///
    /// The four: this target has been under a debugger at some point; it is a developer or automation run;
    /// Bloom's own reporting has already told us about this problem, in which case a second card is noise
    /// about the same trouble; or the failure was deliberately simulated.
    ///
    /// Any of these can still be overridden by the Doctor's `--force`, which exists to test the filing
    /// path itself; see DoctorSupervisor, where that override is applied.
    /// </summary>
    public bool MayFileAReport() => ReasonsFilingWouldNormallyBeBlocked().Count == 0;

    /// <summary>
    /// The same four conditions as <see cref="MayFileAReport"/>, but named, in the words someone would
    /// want to read before overriding them.
    ///
    /// "Report now" deliberately files whatever these say — being able to force a real filing without
    /// restarting the Doctor with `--force` is useful twice over: it is how the filing path itself gets
    /// tested, and a developer build can have a real freeze genuinely worth reporting. So the person is
    /// shown what they are overriding rather than being stopped.
    /// </summary>
    public IReadOnlyList<string> ReasonsFilingWouldNormallyBeBlocked()
    {
        var reasons = new List<string>();
        if (_detector.IsPoisonedByDebugger)
            reasons.Add(
                "a debugger has been attached to this Bloom, so its stacks may show a breakpoint "
                    + "rather than a freeze"
            );
        if (BloomChannel.IsDeveloperChannel(Target.Channel))
            reasons.Add("this is a developer build, which never files on its own");
        else if (BloomChannel.IsHeadlessOrAutomationRun(Target.CommandLine))
            reasons.Add(
                "this Bloom is an automation or headless run, which never files on its own"
            );
        var simulated = SimulatedFailureKind();
        if (!string.IsNullOrEmpty(simulated))
            reasons.Add($"this Bloom was told to break itself on purpose (`{simulated}`)");
        if (BloomAlreadyReported())
            reasons.Add("Bloom has already reported a problem itself during this run");
        return reasons;
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
    /// What this Bloom was told to break itself with, or null if nothing — which is what makes any freeze
    /// we see a rehearsal rather than news. Returns the kind rather than a bare true so that the person
    /// overriding it in "Report now" can be told which rehearsal they are filing.
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
    private string? SimulatedFailureKind()
    {
        try
        {
            return Protocol.DoctorSessionStore.TryRead(Target.ProcessId)?.SimulatedFailure;
        }
        catch (Exception)
        {
            // Err towards reporting, as with BloomAlreadyReported: silence about a real freeze is the
            // costlier mistake, and `--force` is there for anyone who needs to override this either way.
            return null;
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
        _dumpStarted?.Dispose();
        _dumpStarted = null;
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
