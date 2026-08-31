namespace BloomFreezeDoctor;

/// <summary>
/// What the Doctor believes about one watched Bloom.
/// </summary>
public enum TargetState
{
    /// <summary>Responding, with a visible window. Nothing to do.</summary>
    Healthy,

    /// <summary>Not responding, but not for long enough to report yet.</summary>
    Suspect,

    /// <summary>Not responding for long enough that we report it (plan §3.2).</summary>
    Frozen,

    /// <summary>
    /// Alive with no visible window for long enough to count as the zombie of plan §3.6. Note
    /// "visible": a healthy Bloom keeps an invisible window of its own (its splash screen is hidden
    /// rather than closed), so counting any window would mean never detecting this at all.
    /// </summary>
    Zombie,

    /// <summary>The process is gone.</summary>
    Exited,
}

/// <summary>
/// One reading of a watched process, taken by whatever can see the real world. Everything the
/// detector needs is in here, so the detector itself can be tested without a process.
/// </summary>
public readonly record struct TargetObservation
{
    /// <summary>
    /// Monotonic time since watching began. Deliberately NOT a wall clock: the machine can sleep,
    /// and a resumed laptop must not look like a six-hour freeze (plan §3.5).
    /// </summary>
    public required TimeSpan Uptime { get; init; }

    /// <summary>False once the process has gone.</summary>
    public required bool IsAlive { get; init; }

    /// <summary>
    /// Whether the window answered a message probe. The spike settled which probe: use
    /// SendMessageTimeout, because IsHungAppWindow needs about five seconds to make up its mind.
    /// Both are worthless against a UI thread blocked in an STA managed wait, which is what
    /// <see cref="HeartbeatIsStale"/> is for.
    /// </summary>
    public required bool WindowResponds { get; init; }

    /// <summary>Whether the process still has a VISIBLE top-level window. See <see cref="TargetState.Zombie"/>.</summary>
    public required bool HasVisibleWindow { get; init; }

    /// <summary>
    /// Tier B only: Bloom's UI-thread heartbeat has stopped advancing. This is the only signal that
    /// catches a freeze in an STA managed wait, where the window still answers messages. It is never
    /// trusted alone, because WM_TIMER is the lowest-priority message and can starve on a busy but
    /// live UI (plan §3.1).
    /// </summary>
    public bool HeartbeatIsStale { get; init; }

    /// <summary>
    /// Tier B only: independent evidence that a stale UI heartbeat means a blocked UI thread rather than a
    /// starved timer.
    ///
    /// Today this means **Bloom's background watchdog thread is still ticking while the UI thread is
    /// not** — so the process is alive and scheduling threads, and it is the UI thread specifically that
    /// is stuck. That is exactly the signature of a managed wait on the STA thread. When Bloom publishes
    /// breadcrumbs and in-flight API calls, those become additional corroboration of the same kind.
    /// </summary>
    public bool UiBlockCorroborated { get; init; }

    /// <summary>
    /// A debugger is attached at this moment. For a process that has already died this is the last thing
    /// Bloom published before it went, which is what covers the case of a debugger *terminating* Bloom:
    /// that is a TerminateProcess, so Bloom never gets to record a detach, and the page it leaves behind
    /// still says a debugger was there.
    /// </summary>
    public bool DebuggerAttachedNow { get; init; }

    /// <summary>
    /// A debugger has been attached at some point in this Bloom's life, whether or not one is now. Taken
    /// from Bloom's own sticky flag when Bloom publishes a channel — authoritative, and it covers the whole
    /// run rather than only the part the Doctor was watching — and otherwise from our own outside sampling.
    /// </summary>
    public bool DebuggerEverAttached { get; init; }

    /// <summary>
    /// How long ago a debugger was last detached, or **null when that cannot be known**: no published
    /// channel, or one is still attached, or none ever was.
    ///
    /// This is what stops <see cref="DebuggerEverAttached"/> writing off a whole run. Null is treated as
    /// "assume it overlaps", so a Bloom that publishes nothing behaves exactly as it did before.
    /// </summary>
    public TimeSpan? DebuggerLastDetachedAge { get; init; }

    /// <summary>
    /// Tier B only: Bloom says it is deliberately busy (publishing, uploading, making a PDF). Raises
    /// the patience threshold rather than suppressing detection.
    /// </summary>
    public bool LongOperationInProgress { get; init; }

    /// <summary>
    /// How long the target has *already* been unresponsive at the moment we first looked, when that can
    /// be known — which it can, from Bloom's published heartbeat, since the age of the last tick says
    /// exactly how long ago the UI thread stopped.
    ///
    /// This exists for the case the whole tool has to handle well: someone installs the Doctor **because**
    /// Bloom is already frozen. Without it, the Doctor would start its clock from the moment it happened
    /// to arrive and make that person wait another minute to be told what they already knew.
    /// </summary>
    public TimeSpan? AlreadyUnresponsiveFor { get; init; }
}

/// <summary>
/// The thresholds from decision D3, in one place so that dogfooding can move them without a code
/// change.
/// </summary>
public sealed record DetectorThresholds
{
    /// <summary>How long unresponsive before we start paying attention.</summary>
    public TimeSpan Suspect { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long unresponsive before we report.</summary>
    public TimeSpan Report { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long unresponsive before we report, when Bloom says it is busy on purpose.</summary>
    public TimeSpan ReportDuringLongOperation { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long alive-with-no-visible-window before we call it a zombie.</summary>
    public TimeSpan Zombie { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A gap between observations larger than this means something stopped the world — the machine
    /// slept, or the Doctor itself was starved — so elapsed "unresponsive" time is not trustworthy
    /// and gets restarted rather than accumulated.
    /// </summary>
    public TimeSpan ImplausibleGap { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How much slack to give a departed debugger when deciding whether it can account for the episode
    /// being judged.
    ///
    /// The comparison is "did the debugger leave before this episode began?", and it wants slack in one
    /// direction: detaching does not always hand Bloom back instantly, so a UI thread can still be
    /// catching up for a while afterwards, and our own once-a-second sampling puts a second of slop on
    /// every timestamp anyway. Generous on purpose — the cost of being too generous is one unreported
    /// freeze on a developer's machine, and the cost of being too mean is a bogus card.
    /// </summary>
    public TimeSpan DebuggerOverlapMargin { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Why the detector is asking for a report; becomes the shape of the card.</summary>
public enum ReportReason
{
    None,

    /// <summary>UI unresponsive past the threshold — plan state 1.</summary>
    Frozen,

    /// <summary>Was frozen and started responding again. Still worth reporting; often better evidence.</summary>
    RecoveredFromFreeze,

    /// <summary>Froze, then the process died or was killed. One card, not two.</summary>
    DiedWhileFrozen,

    /// <summary>Alive with no visible window — plan state 3.</summary>
    Zombie,

    /// <summary>
    /// Exited, and something says it went wrong. The detector does not decide that - it has none of the
    /// evidence - it only carries the reason so the report says what it is about; see ExitClassifier.
    /// </summary>
    ExitedWithoutProof,

    /// <summary>
    /// A person asked for this report — the CTRL-key "Report now" button, or `--report-now`. Bloom was
    /// not necessarily frozen, and the card must not claim it was: this exists because the first such
    /// report came out titled "UI frozen" about a perfectly healthy Bloom, which would have wasted
    /// somebody's afternoon.
    /// </summary>
    RequestedByPerson,
}

/// <summary>The detector's answer to one observation.</summary>
public readonly record struct DetectorVerdict
{
    /// <summary>What we now believe about the target.</summary>
    public required TargetState State { get; init; }

    /// <summary>Set when this observation is the moment to gather and file. Fires at most once per reason.</summary>
    public required ReportReason Report { get; init; }

    /// <summary>Human-readable justification, for the card and for the Doctor's own log.</summary>
    public required string Explanation { get; init; }

    /// <summary>True when a report is being asked for.</summary>
    public bool ShouldReport => Report != ReportReason.None;
}

/// <summary>
/// Turns a stream of observations of one Bloom into "report now, for this reason" decisions.
///
/// Everything here came out of the Phase 0 spike, so the reasons for the odd-looking rules are
/// recorded in the comments rather than left to be rediscovered:
/// a debugged process is poison forever, not just while the debugger is attached; a stale heartbeat
/// needs a second opinion; and a big gap between observations means the machine slept, not that
/// Bloom hung.
/// </summary>
public sealed class FreezeDetector
{
    private readonly DetectorThresholds _thresholds;

    /// <summary>When the target was last seen to be alive and answering. Null until the first look.</summary>
    private TimeSpan? _lastRespondedAt;

    /// <summary>When the target was last seen to have a visible window.</summary>
    private TimeSpan? _lastHadWindowAt;

    /// <summary>Uptime of the previous observation, to notice gaps that mean the world stopped.</summary>
    private TimeSpan? _previousUptime;

    /// <summary>
    /// Once true, never false again. A developer stopping the debugger is a hard kill that leaves no
    /// proof of shutdown, so without this the most common thing a developer does all day would look
    /// exactly like the crash we are hunting.
    /// </summary>
    private bool _everDebugged;
    private bool _debuggerAttachedNow;
    private TimeSpan? _debuggerLastDetachedAge;

    /// <summary>
    /// How long the episode we would currently report has been running — the unresponsive or windowless
    /// time, whichever is doing the reporting. Kept so <see cref="IsPoisonedByDebugger"/> can ask whether a
    /// departed debugger was still around when it began. Zero when nothing is wrong, which makes the
    /// comparison strict: only a debugger that left within the margin excuses a process that was healthy
    /// right up to the end.
    /// </summary>
    private TimeSpan _episodeLength;

    private readonly HashSet<ReportReason> _alreadyReported = new();

    /// <summary>Creates a detector, optionally with thresholds other than decision D3's defaults.</summary>
    public FreezeDetector(DetectorThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? new DetectorThresholds();
    }

    /// <summary>The state as of the last observation.</summary>
    public TargetState State { get; private set; } = TargetState.Healthy;

    /// <summary>
    /// True if a debugger can account for what we are looking at, and therefore nothing should be
    /// reported. Exposed so the gatherer can say why it declined, rather than silently doing nothing.
    ///
    /// **The question is deliberately narrow: was a debugger around while THIS episode was happening?**
    /// The broader reading — "a debugger has been attached at some point in this run" — writes off the whole
    /// run, so that attaching one in the morning would bury a genuine freeze that afternoon, on precisely
    /// the machines whose owners are best placed to diagnose it. Bloom records *when* a debugger last left
    /// so that the narrower question can be answered.
    ///
    /// The three answers, in order:
    ///
    ///   * A debugger is attached now — nothing to discuss. This also covers a debugger that terminated
    ///     Bloom, since the page Bloom leaves behind still says one was attached.
    ///   * One was attached but we cannot tell when it left — assume it overlaps. This is what a Bloom too
    ///     old to publish a channel gets, since it cannot tell us the departure time.
    ///   * One was attached and we know when it left — it only excuses an episode that had already
    ///     started by then.
    /// </summary>
    public bool IsPoisonedByDebugger
    {
        get
        {
            if (_debuggerAttachedNow)
                return true;
            if (!_everDebugged)
                return false;
            if (!_debuggerLastDetachedAge.HasValue)
                return true;

            // Both of these are ages, so the arithmetic is the other way round from how it reads: a
            // debugger that left LONGER ago than the episode has been running left BEFORE it started, and
            // therefore explains nothing.
            return _debuggerLastDetachedAge.Value
                <= _episodeLength + _thresholds.DebuggerOverlapMargin;
        }
    }

    /// <summary>
    /// Feeds one observation in and gets back what to do about it. Call this on a steady cadence
    /// (about once a second); the detector works entirely from the timestamps it is given, so a
    /// missed beat costs nothing.
    /// </summary>
    public DetectorVerdict Observe(TargetObservation observation)
    {
        // Latched rather than merely copied: Bloom's own flag is sticky and survives, but our outside
        // sampling of a Bloom that publishes nothing is a series of instants, and an attach we saw once
        // must not be forgotten a second later.
        _debuggerAttachedNow = observation.DebuggerAttachedNow;
        if (observation.DebuggerAttachedNow || observation.DebuggerEverAttached)
            _everDebugged = true;
        // Only ever moves forward to a *more recent* departure; a null here means "still attached" or
        // "unknowable", neither of which should erase a time we already learned.
        if (observation.DebuggerLastDetachedAge.HasValue)
            _debuggerLastDetachedAge = observation.DebuggerLastDetachedAge;

        // A gap far larger than our cadence means the world stopped: the machine slept, or something
        // starved the Doctor. Elapsed unresponsive time measured across such a gap is meaningless, so
        // treat the target as freshly seen rather than accumulating a freeze it never had.
        var slept =
            _previousUptime.HasValue
            && observation.Uptime - _previousUptime.Value > _thresholds.ImplausibleGap;
        if (slept)
        {
            _lastRespondedAt = observation.Uptime;
            _lastHadWindowAt = observation.Uptime;
        }
        _previousUptime = observation.Uptime;

        if (!observation.IsAlive)
            return ObserveDeadProcess(observation);

        if (observation.WindowResponds && !BelievesHeartbeatIsStale(observation))
            _lastRespondedAt = observation.Uptime;
        if (observation.HasVisibleWindow)
            _lastHadWindowAt = observation.Uptime;

        // First look. If the target can tell us how long it has already been unresponsive, believe it and
        // backdate accordingly, so a Doctor started BECAUSE Bloom is frozen reports at once instead of
        // making the user wait out a threshold that has in truth already passed. Without a published
        // heartbeat we have no way to know, and the clock has to start now.
        _lastRespondedAt ??=
            observation.Uptime - (observation.AlreadyUnresponsiveFor ?? TimeSpan.Zero);
        _lastHadWindowAt ??= observation.Uptime;

        var unresponsiveFor = observation.Uptime - _lastRespondedAt.Value;
        var windowlessFor = observation.Uptime - _lastHadWindowAt.Value;

        // How long something has been wrong, for the debugger-overlap question. The LONGER of the two,
        // deliberately: a longer episode is more easily excused by a departed debugger, so taking the
        // larger errs toward staying quiet. Note this is left alone on the dead-process path below, which
        // is what makes "died while frozen" keep the freeze's length rather than resetting to nothing.
        _episodeLength = unresponsiveFor > windowlessFor ? unresponsiveFor : windowlessFor;

        // Zombie is checked first, because a process with no window cannot meaningfully be called
        // unresponsive: there is nothing left to send a message to.
        if (!observation.HasVisibleWindow && windowlessFor >= _thresholds.Zombie)
            return Settle(
                TargetState.Zombie,
                ReportReason.Zombie,
                $"alive with no visible window for {Describe(windowlessFor)}"
            );

        var reportAfter = observation.LongOperationInProgress
            ? _thresholds.ReportDuringLongOperation
            : _thresholds.Report;

        if (unresponsiveFor >= reportAfter)
        {
            var why = BelievesHeartbeatIsStale(observation)
                ? $"UI-thread heartbeat stale for {Describe(unresponsiveFor)} with no forward progress"
                : $"window has not answered for {Describe(unresponsiveFor)}";
            if (observation.LongOperationInProgress)
                why += ", despite Bloom reporting a long operation";
            return Settle(TargetState.Frozen, ReportReason.Frozen, why);
        }

        if (unresponsiveFor >= _thresholds.Suspect)
            return Settle(
                TargetState.Suspect,
                ReportReason.None,
                $"unresponsive for {Describe(unresponsiveFor)}; watching"
            );

        // Responding again after we had decided it was frozen. Report it: a freeze the user waited
        // out is at least as informative as one they killed, and we caught this one live.
        if (State == TargetState.Frozen)
            return Settle(
                TargetState.Healthy,
                ReportReason.RecoveredFromFreeze,
                "started responding again after being reported frozen"
            );

        return Settle(TargetState.Healthy, ReportReason.None, "responding");
    }

    private DetectorVerdict ObserveDeadProcess(TargetObservation observation)
    {
        // Died while we already thought it was in trouble: that is one story, not two, and the
        // freeze is the interesting half.
        if (State is TargetState.Frozen or TargetState.Suspect)
            return Settle(
                TargetState.Exited,
                ReportReason.DiedWhileFrozen,
                $"exited while {State.ToString().ToLowerInvariant()}"
            );

        // Otherwise this is plan §3.4/§3.5 territory, and the detector is deliberately not the judge:
        // whether a bare exit is reportable depends on evidence it does not have (a clean-exit proof,
        // Event Log entries, WER files). The watcher asks the exit classifier about it.
        return Settle(TargetState.Exited, ReportReason.None, "exited while apparently healthy");
    }

    /// <summary>
    /// A stale heartbeat is only believed when something else agrees, because WM_TIMER is the
    /// lowest-priority message and a busy-but-live UI can starve it (plan §3.1).
    /// </summary>
    private static bool BelievesHeartbeatIsStale(TargetObservation observation) =>
        observation.HeartbeatIsStale
        && (observation.UiBlockCorroborated || !observation.WindowResponds);

    /// <summary>
    /// Records the new state and suppresses a repeat of a reason we have already reported, so one
    /// freeze produces one card however long it lasts.
    /// </summary>
    private DetectorVerdict Settle(TargetState state, ReportReason reason, string explanation)
    {
        State = state;
        if (reason != ReportReason.None && !_alreadyReported.Add(reason))
            reason = ReportReason.None;
        return new DetectorVerdict
        {
            State = state,
            Report = reason,
            Explanation = explanation,
        };
    }

    private static string Describe(TimeSpan span) =>
        span.TotalSeconds < 90 ? $"{span.TotalSeconds:F0}s" : $"{span.TotalMinutes:F1} minutes";
}
