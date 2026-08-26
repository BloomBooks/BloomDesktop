using System.Diagnostics;
using BloomFreezeDoctor.Gathering;
using BloomFreezeDoctor.Outbox;
using SIL.IO;
// Aliased rather than imported plainly, so uses below still read as Protocol.DoctorSignals - it is worth
// saying at each use that this is the shared wire format, not something local to the Doctor.
using Protocol = BloomFreezeDoctor.Protocol;

namespace BloomFreezeDoctor;

/// <summary>
/// What came of a person pressing "Report now".
///
/// Three distinct answers, and the middle one is the reason this is not just a string: a report can be
/// safely gathered and queued without being filed yet — offline, over the daily cap, or another process
/// is draining the queue — and telling somebody that FAILED, when their report is sitting on disk about
/// to go, is a good way to have them try again and again.
/// </summary>
/// <param name="IssueId">The card, if the tracker accepted it there and then.</param>
/// <param name="Queued">True if it is safely on disk and will be sent later.</param>
public readonly record struct ReportNowResult(string? IssueId, bool Queued);

/// <summary>One still-running Bloom the Doctor is watching, and the state it is in.</summary>
public readonly record struct LiveBloom(int ProcessId, TargetState State);

/// <summary>What the window needs to render, published by the supervisor as things change.</summary>
public sealed record DoctorStatus
{
    /// <summary>One line per watched Bloom, in the card's own vocabulary: Running, Frozen, and so on.</summary>
    public required IReadOnlyList<string> BloomLines { get; init; }

    /// <summary>A line about the outbox when it is not empty, or null.</summary>
    public string? OutboxLine { get; init; }

    /// <summary>The most recent thing that happened, for the bottom of the window.</summary>
    public string? LastEvent { get; init; }
}

/// <summary>
/// The Doctor's brain: finds Blooms, watches each one, and when a watcher asks for a report, gathers it,
/// queues it, and tries to file it.
///
/// Everything here runs off the UI thread. That is a hard requirement rather than a preference: the
/// Doctor has a visible window (decision D1), and a Freeze Doctor whose own window goes white while it
/// diagnoses a freeze would be its own worst advertisement.
/// </summary>
public sealed class DoctorSupervisor : IDisposable
{
    /// <summary>The process name we look for unless told otherwise. See <see cref="_targetProcessNames"/>.</summary>
    public const string DefaultTargetProcessName = "Bloom";

    /// <summary>How often to look for Blooms that have started or gone away.</summary>
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromSeconds(5);

    /// <summary>How often to try the outbox again while we are running and it is not empty.</summary>
    private static readonly TimeSpan DrainInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to stay alive after the last Bloom has gone, if reports are still waiting to be sent.
    /// The Doctor is never *pinned* by this (see plan §3.6) — it is a courtesy window in case the network
    /// comes back, not a dependency.
    /// </summary>
    private static readonly TimeSpan LingerForOutbox = TimeSpan.FromMinutes(10);

    private readonly ReportOutbox _outbox;
    private readonly string _project;
    private readonly string _targetProcessName;

    /// <summary>
    /// Every process name that might BE Bloom, because "Bloom" alone is wrong for most of our users.
    ///
    /// **This was a shipping-stopper.** Bloom's installer renames the executable per channel
    /// (`Bloom$(channel).exe` in build/Bloom.proj), so an Alpha install runs as `BloomAlpha` and a Beta
    /// one as `BloomBeta`. Bloom launches us with only `--adopt &lt;pid&gt;`, so the name stayed at its
    /// default of "Bloom", `Process.GetProcessesByName("Bloom")` matched nothing, discovery therefore
    /// treated the Bloom we had just adopted as gone, dropped its watcher, and the Doctor concluded it had
    /// nothing left to do and exited - within a second of starting, silently. It worked only on Release and
    /// on developer builds, where the exe happens to be `Bloom.exe`. Alpha and Beta are precisely where we
    /// most want it, and are the only channels where the freeze simulator is allowed.
    ///
    /// So the set starts from what we were told and LEARNS: adopting a process adds that process's own
    /// name, which needs no list to be kept up to date and cannot go stale. The seeded channel names below
    /// only matter for a Doctor started by hand, with no Bloom to adopt; `--target-name` covers anything
    /// unusual.
    /// </summary>
    private readonly HashSet<string> _targetProcessNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _forceFiling;
    private readonly Dictionary<int, BloomTargetWatcher> _watchers = new();

    /// <summary>
    /// Each watched Bloom's probe, kept beside its watcher for two reasons: so the discovery sweep can
    /// examine an exit itself rather than only hoping the watcher gets one more tick first (see
    /// <see cref="Discover"/>), and so the handle each probe holds has a definite owner.
    ///
    /// **An entry here means nobody has claimed that Bloom's death yet.** Claiming it removes the entry
    /// and takes over releasing the handle, because the handle is what supplies a dead process's exit code
    /// and must outlive the death by as long as the examination takes.
    /// </summary>
    private readonly Dictionary<int, WindowsTargetProbe> _probes = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _stopping = new();

    // Explicitly System.Threading timers, not System.Windows.Forms ones. The distinction is the whole
    // point: these must tick on the thread pool, because a Doctor that did its work on the UI thread
    // would freeze its own window while diagnosing a freeze.
    private System.Threading.Timer? _discovery;
    private System.Threading.Timer? _drain;

    /// <summary>Processes whose crash dump we have already started, so one crash produces one dump.</summary>
    private readonly HashSet<int> _dumpsRequested = new();

    /// <summary>Zombies whose evidence has been gathered, and which may therefore be ended.</summary>
    private readonly HashSet<int> _zombiesReported = new();

    /// <summary>Zombies we have already tried to end, so we do not keep hammering at one.</summary>
    private readonly HashSet<int> _zombiesEnded = new();

    /// <summary>
    /// Blooms we asked to stop - whether by our own zombie policy or because somebody pressed "Restart
    /// Bloom". Their deaths are our doing, so they are not news and must not be reported.
    ///
    /// Without this the Doctor filed a second card about a death it had itself caused: a frozen Bloom was
    /// reported (AUT-20929), the user pressed Restart, the button ended that Bloom, and the Doctor then
    /// dutifully reported "UI froze, then the process died" as a fresh problem (AUT-20930). Two cards for
    /// one incident, the second of them describing our own action as a bug.
    /// </summary>
    private readonly HashSet<int> _weAskedItToStop = new();

    /// <summary>Exits we have already examined, so one death produces one examination.</summary>
    private readonly HashSet<int> _exitsExamined = new();

    /// <summary>
    /// How many gathers or examinations are in flight. The Doctor must not decide it has nothing left to do
    /// while it is still busy — which is exactly what happened before this existed: a Bloom would crash, the
    /// Doctor would notice the process was gone, conclude there was nothing left to watch, and exit,
    /// cancelling the examination of the very crash it had just seen.
    /// </summary>
    private int _workInFlight;

    /// <summary>Set by `--never-end-zombies`, for anyone who would rather we only ever observed.</summary>
    private readonly bool _neverEndZombies;
    private DateTimeOffset? _lastBloomSeenAt;
    private string? _lastEvent;

    /// <summary>
    /// Creates the supervisor.
    /// </summary>
    /// <param name="project">Tracker project to file into — `AUT` while testing, `BL` in earnest.</param>
    /// <param name="targetProcessName">
    /// Process name to watch, "Bloom" in production. Overridable so the freeze stub can stand in for
    /// Bloom during testing, which is the only way to exercise this without breaking a real Bloom.
    /// </param>
    /// <param name="forceFiling">
    /// Files reports even from developer builds. For deliberate end-to-end tests only; without it a
    /// developer machine gathers to disk and never files, which is what keeps our own work off the
    /// tracker.
    /// </param>
    public DoctorSupervisor(
        string project = "BL",
        string targetProcessName = DefaultTargetProcessName,
        bool forceFiling = false,
        ReportOutbox? outbox = null,
        bool neverEndZombies = false
    )
    {
        _project = project;
        _targetProcessName = targetProcessName;
        _targetProcessNames.Add(targetProcessName);
        // Only when nobody named a specific target: `--target-name` exists so the freeze stub can stand in
        // for Bloom, and widening that would have us adopt a real Bloom in the middle of a test.
        if (targetProcessName == DefaultTargetProcessName)
        {
            // The same channel folders StatusForm.FindBloomExecutable already looks in.
            _targetProcessNames.Add("BloomAlpha");
            _targetProcessNames.Add("BloomBeta");
            _targetProcessNames.Add("BloomBetaInternal");
        }
        _forceFiling = forceFiling;
        _outbox = outbox ?? new ReportOutbox();
        _neverEndZombies = neverEndZombies;
    }

    /// <summary>Raised after an attempt to end a stuck Bloom, so the window can say what happened.</summary>
    public event EventHandler<ZombieEndOutcome>? ZombieEnded;

    /// <summary>Raised whenever the status changes, so the window can redraw. Fires on a background thread.</summary>
    public event EventHandler<DoctorStatus>? StatusChanged;

    /// <summary>Raised when a report has been filed, so the window can say so and offer a restart.</summary>
    public event EventHandler<string>? ReportFiled;

    /// <summary>
    /// Raised when a report was gathered and deliberately NOT filed - a developer or automation build, or
    /// a Bloom under a debugger. Carries the folder it was saved in.
    ///
    /// A separate event from <see cref="ReportFiled"/> rather than a flag on it, because the two need
    /// quite different words: "the tracker has been told" versus "nothing was sent, and here is where to
    /// look". Without this the Doctor did all of its work and then showed absolutely nothing - on
    /// precisely the runs a developer uses to test it, which is where it most needs to be visible. That is
    /// the same failure as a "Report now" that said nothing: silence reads as failure.
    /// </summary>
    public event EventHandler<string>? ReportSavedWithoutFiling;

    /// <summary>
    /// Raised with the full path of each Bloom we start watching, so the window knows which Bloom to
    /// restart.
    ///
    /// It used to be told only on the `--adopt` path, in Program.Main - so a Doctor started by hand, or
    /// watching a second Bloom it found by discovery, never learned any path at all and fell back to
    /// scanning the installed layouts. Testing showed the consequence plainly: "Restart Bloom" relaunched
    /// an installed 6.3.2 Release build instead of the developer build that had just frozen. Raising it
    /// from here covers every route into a watcher, because they all pass through one place.
    /// </summary>
    public event EventHandler<string>? WatchingBloomAt;

    /// <summary>Raised when the Doctor has nothing left to do and should exit.</summary>
    public event EventHandler? NothingLeftToDo;

    /// <summary>The queue of reports waiting to be sent.</summary>
    public ReportOutbox Outbox => _outbox;

    /// <summary>
    /// Ends a Bloom because a person asked us to - the "Restart Bloom" button clearing the way for a new
    /// one. Goes through the supervisor rather than straight to <see cref="ZombieEnder"/> so that the death
    /// is recorded as our own doing and is not then reported as a problem; see
    /// <see cref="_weAskedItToStop"/>.
    /// </summary>
    public ZombieEndOutcome EndBloomAtSomebodysRequest(int processId)
    {
        lock (_lock)
            _weAskedItToStop.Add(processId);
        return ZombieEnder.End(processId);
    }

    /// <summary>
    /// The Blooms we are watching that are still running, and what state each is in.
    ///
    /// The window needs this because Bloom is single-instance: a new one cannot start while an old one
    /// still holds the token, so "Restart Bloom" without this check starts a process that exits a few
    /// seconds later. What the user then sees is "Bloom will not start" - which is the very complaint
    /// that brought them to the Doctor in the first place.
    /// </summary>
    public IReadOnlyList<LiveBloom> LiveWatchedBlooms()
    {
        lock (_lock)
        {
            return _watchers
                .Values.Where(watcher => IsAlive(watcher.Target.ProcessId))
                .Select(watcher => new LiveBloom(watcher.Target.ProcessId, watcher.State))
                .ToList();
        }
    }

    /// <summary>Starts watching. Drains the outbox first, which is the moment that matters most.</summary>
    public void Start()
    {
        // Drain on startup, because the most likely next event after a freeze is the user restarting
        // Bloom — which starts us — so this is the reliable route by which yesterday's report gets out.
        _ = Task.Run(() => DrainAsync(_stopping.Token));

        _discovery = new System.Threading.Timer(
            _ => Discover(),
            null,
            TimeSpan.Zero,
            DiscoveryInterval
        );
        _drain = new System.Threading.Timer(
            _ => _ = DrainAsync(_stopping.Token),
            null,
            DrainInterval,
            DrainInterval
        );
    }

    /// <summary>
    /// Adopts a specific process, as Bloom asks us to when it launches us. Also used by `--report-now`.
    /// </summary>
    public void Adopt(int processId)
    {
        var facts = GatherContextBuilder.DescribeRunningProcess(processId);
        if (facts == null)
            return;
        // Learn this Bloom's actual process name before the first discovery tick, or that tick will look
        // for the wrong name, conclude this Bloom is gone, and take the Doctor down with it. See
        // _targetProcessNames.
        RememberProcessName(facts.ExePath);
        AdoptFacts(facts);
    }

    /// <summary>
    /// Adds the executable's own name to the set discovery searches, so a channel-renamed Bloom
    /// (`BloomAlpha`, `BloomBeta`, or anything future) is found without anyone maintaining a list.
    /// </summary>
    private void RememberProcessName(string exePath)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(exePath);
            if (string.IsNullOrWhiteSpace(name))
                return;
            lock (_lock)
                _targetProcessNames.Add(name);
        }
        catch (Exception)
        {
            // A malformed path is not worth failing an adoption over; the seeded names still apply.
        }
    }

    /// <summary>
    /// Gathers and files a report for a process right now, whatever state it is in. This is the CTRL-key
    /// "Report now" of the card, and it is also how support gets a snapshot of a Bloom that is merely
    /// slow rather than frozen.
    /// </summary>
    /// <summary>
    /// Why a report about this Bloom would not normally be filed, in words fit to show someone. Empty
    /// when nothing stands in the way.
    ///
    /// "Report now" files regardless — see <see cref="BloomTargetWatcher.ReasonsFilingWouldNormallyBeBlocked"/>
    /// for why that is deliberate — so this exists to let the window say what is being overridden before
    /// it happens, rather than to prevent it.
    /// </summary>
    public IReadOnlyList<string> WhyFilingWouldNormallyBeBlocked(int processId)
    {
        lock (_lock)
        {
            return _watchers.TryGetValue(processId, out var watcher)
                ? watcher.ReasonsFilingWouldNormallyBeBlocked()
                : Array.Empty<string>();
        }
    }

    public async Task<ReportNowResult> ReportNowAsync(int processId, CancellationToken cancellation)
    {
        var facts = GatherContextBuilder.DescribeRunningProcess(processId);
        if (facts == null)
            return new ReportNowResult(null, Queued: false);
        var verdict = new DetectorVerdict
        {
            State = TargetState.Healthy,
            // Deliberately NOT ReportReason.Frozen: this Bloom may be perfectly healthy, and a card
            // titled "UI frozen" about a healthy Bloom would send someone hunting a freeze that never
            // happened.
            Report = ReportReason.RequestedByPerson,
            Explanation =
                "a person asked for this report deliberately (the Report now button, or --report-now); "
                + "Bloom was not necessarily frozen",
        };
        var outcome = await GatherFileAndRecordAsync(facts, verdict, mayFile: true, cancellation)
            .ConfigureAwait(false);
        return new ReportNowResult(outcome.IssueId, outcome.StillQueued);
    }

    /// <summary>Looks for Blooms we are not yet watching, and forgets ones that have gone.</summary>
    private void Discover()
    {
        try
        {
            // Take the ids and let the Process objects go at once. Each one holds an OS handle, and this
            // runs every five seconds for as long as the Doctor lives - which can be hours - so keeping
            // them until finalization accumulates handles in the one process that must stay healthy
            // enough to diagnose everything else.
            string[] namesToSearch;
            lock (_lock)
                namesToSearch = _targetProcessNames.ToArray();

            var ids = new List<int>();
            foreach (var name in namesToSearch)
            {
                var running = Process.GetProcessesByName(name);
                try
                {
                    ids.AddRange(running.Select(p => p.Id));
                }
                finally
                {
                    foreach (var process in running)
                        process.Dispose();
                }
            }
            var runningIds = ids.Distinct().ToArray();

            foreach (var id in runningIds)
            {
                var facts = GatherContextBuilder.DescribeRunningProcess(id);
                if (facts != null)
                    AdoptFacts(facts);
            }

            var departed = new List<(BloomTargetWatcher Watcher, WindowsTargetProbe? Probe)>();
            lock (_lock)
            {
                if (_watchers.Count > 0)
                    _lastBloomSeenAt = DateTimeOffset.UtcNow;

                // Collect the watchers whose process has gone. They are examined and disposed below,
                // outside the lock.
                foreach (var id in _watchers.Keys.ToList())
                {
                    if (runningIds.Contains(id))
                        continue;
                    departed.Add((_watchers[id], _probes.GetValueOrDefault(id)));
                    _watchers.Remove(id);
                    _probes.Remove(id);
                    // Forget that we asked this one to stop, now that it has. Windows recycles process ids
                    // from a pool, and "Restart Bloom" is exactly the case that kills one and starts
                    // another moments later - so a stale entry here could silence a genuine report about a
                    // DIFFERENT Bloom that happened to be handed the dead one's id.
                    _weAskedItToStop.Remove(id);
                }
            }

            // Give each departed Bloom its exit examination before letting go of its watcher.
            //
            // This used to assume the watcher had already told its story - "the watcher itself reports the
            // exit first, so by the time we get here its story has been told" - and that was a hope, not a
            // guarantee. The examination runs on the watcher's own one-second tick; this sweep runs every
            // five seconds; and whichever fires first after the death wins. When the sweep won it disposed
            // the watcher, which stopped the timer, and the exit was never examined at all. For a death at
            // a random moment that is something like one in ten - so roughly one Bloom in ten that simply
            // vanished, which is one of the three states this whole tool exists to notice, produced no
            // report whatsoever.
            //
            // Calling it here needs no coordination with the tick: the examination claims each process id
            // once, under the lock, so whichever path arrives second does nothing. Outside the lock
            // because it starts background work.
            foreach (var (watcher, probe) in departed)
            {
                if (probe != null)
                    ConsiderReportingAnExit(
                        watcher,
                        probe,
                        new DetectorVerdict
                        {
                            State = TargetState.Exited,
                            // The examination works out its own reason from the evidence; all this
                            // verdict has to say is that the process has gone.
                            Report = ReportReason.None,
                            Explanation = "the process is no longer running",
                        }
                    );
                watcher.Dispose();
            }

            PublishStatus();
            ConsiderExiting();
        }
        catch (Exception)
        {
            // Discovery failing must never stop the Doctor; the next tick will try again.
        }
    }

    private void AdoptFacts(BloomTargetFacts facts)
    {
        lock (_lock)
        {
            if (_watchers.ContainsKey(facts.ProcessId))
                return;

            // A headless or automation run legitimately has no window, so watching it would only produce
            // false zombie reports (plan §3.3).
            if (BloomChannel.IsHeadlessOrAutomationRun(facts.CommandLine))
                return;

            Process process;
            try
            {
                process = Process.GetProcessById(facts.ProcessId);
            }
            catch (Exception)
            {
                return;
            }

            var probe = new WindowsTargetProbe(process);
            var watcher = new BloomTargetWatcher(facts, probe);
            watcher.ReportWanted += OnReportWanted;
            watcher.Observed += (_, verdict) =>
            {
                PublishStatus();
                RespondToACrashingBloom(watcher);
                ConsiderEndingAZombie(watcher, verdict);
                ConsiderReportingAnExit(watcher, probe, verdict);
            };
            _watchers[facts.ProcessId] = watcher;
            _probes[facts.ProcessId] = probe;
            watcher.Start();
            Note($"watching Bloom {facts.ProcessId} ({facts.Channel})");
        }

        // Outside the lock: the handler marshals to the UI thread, and holding a lock across that is how
        // deadlocks are made. Every route into a watcher passes here, which is the point - see
        // WatchingBloomAt.
        if (!string.IsNullOrWhiteSpace(facts.ExePath))
            WatchingBloomAt?.Invoke(this, facts.ExePath);
    }

    /// <summary>
    /// A watcher has decided something is wrong. Gather, queue and try to send — on a worker thread, and
    /// without letting one failure take down the Doctor.
    /// </summary>
    private void OnReportWanted(object? sender, ReportWantedEventArgs e)
    {
        // A death we asked for is not a bug. If we told this Bloom to stop - our zombie policy, or somebody
        // pressing "Restart Bloom" - then it dying is the request being honoured, and reporting it as a
        // problem describes our own action as a fault. The freeze that led to the request has already been
        // reported on its own merits.
        var itDied =
            e.Verdict.Report == ReportReason.DiedWhileFrozen
            || e.Verdict.State == TargetState.Exited;
        if (itDied)
        {
            lock (_lock)
            {
                if (_weAskedItToStop.Contains(e.Target.ProcessId))
                {
                    Note(
                        $"Bloom {e.Target.ProcessId} has gone, as we asked it to; not reporting that as a problem"
                    );
                    return;
                }
            }
        }

        Interlocked.Increment(ref _workInFlight);
        _ = Task.Run(async () =>
        {
            try
            {
                Note(
                    $"gathering evidence about Bloom {e.Target.ProcessId}: {e.Verdict.Explanation}"
                );
                var outcome = await GatherFileAndRecordAsync(
                        e.Target,
                        e.Verdict,
                        e.MayFile || _forceFiling,
                        _stopping.Token
                    )
                    .ConfigureAwait(false);
                if (outcome.IssueId != null)
                    ReportFiled?.Invoke(this, outcome.IssueId);
            }
            catch (Exception ex)
            {
                Note($"gathering failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _workInFlight);
                ConsiderExiting();
            }
        });
    }

    /// <summary>
    /// What one gather-and-file attempt achieved.
    /// </summary>
    /// <param name="IssueId">The card, if the tracker accepted it during this attempt.</param>
    /// <param name="StillQueued">
    /// True when the report is safely on disk but has not been filed yet. That is a perfectly good
    /// outcome - offline, over the daily cap, or another process is sending it - and it must not be
    /// reported to a user as a failure.
    /// </param>
    /// <param name="WasGatedOut">True when another process was already draining, so we sent nothing.</param>
    private readonly record struct GatherOutcome(
        string? IssueId,
        bool StillQueued,
        bool WasGatedOut
    );

    private async Task<GatherOutcome> GatherFileAndRecordAsync(
        BloomTargetFacts facts,
        DetectorVerdict verdict,
        bool mayFile,
        CancellationToken cancellation,
        Protocol.DoctorChannelSnapshot? lastSeenPublishedState = null
    )
    {
        var alive = IsAlive(facts.ProcessId);
        var artifacts = Path.Combine(
            Path.GetTempPath(),
            "BloomFreezeDoctor",
            $"gather-{facts.ProcessId}-{Guid.NewGuid():N}"
        );
        var context = GatherContextBuilder.Build(
            facts,
            verdict,
            alive,
            artifacts,
            lastSeenPublishedState: lastSeenPublishedState
        );

        var report = await new EvidenceGatherer()
            .GatherAsync(context, mayFile, cancellation)
            .ConfigureAwait(false);

        var bundle = _outbox.Enqueue(
            report,
            _project,
            facts.Channel,
            verdict.Report.ToString(),
            facts.ProcessId,
            // Taken from the verdict rather than from a flag threaded through every caller: this reason is
            // set in exactly one place, ReportNowAsync, and it is set precisely because a person asked.
            userRequested: verdict.Report == ReportReason.RequestedByPerson
        );
        // Ending a zombie is only allowed once its evidence is safely on disk, so this is the moment that
        // unlocks it.
        if (verdict.Report == ReportReason.Zombie)
            lock (_lock)
                _zombiesReported.Add(facts.ProcessId);
        // Say WHICH of the reasons applies. The old wording listed them all at once and left the reader to
        // guess - and they are acted on quite differently: a developer build is permanent, a debugger can
        // be detached, and a simulated failure means nothing was wrong in the first place.
        //
        // Simulation is checked FIRST because it is the most informative answer when more than one applies,
        // which on a developer machine is the normal case: "you asked for this crash" tells the reader far
        // more than "this is a developer build", and a real report was confusing for exactly that reason.
        // All FOUR reasons are covered. With only three, the fourth - Bloom having already reported the
        // problem itself - fell through to whichever arm came last, and the log then stated something
        // simply untrue about a Bloom no debugger had been near.
        var simulated = context.Session?.SimulatedFailure;
        string notFiledBecause;
        if (!string.IsNullOrEmpty(simulated))
            notFiledBecause = $"this failure was deliberately simulated ({simulated})";
        else if (facts.NeverFile)
            notFiledBecause = "this is a developer or automation build";
        else if (context.Session?.BloomAlreadyReported == true)
            notFiledBecause = "Bloom has already reported this problem itself";
        else
            notFiledBecause = "a debugger was attached";
        Note(
            report.MayFile
                ? $"report queued ({report.Summary})"
                : $"report gathered to disk only, not filed because {notFiledBecause}"
        );
        TryDeleteDirectory(artifacts);
        PublishStatus();

        if (!report.MayFile)
        {
            // Gathered to disk and never to be sent, which is the intended end for a developer or
            // automation run. Not queued, and not a failure either - but somebody has to be TOLD, or a
            // successful gather is indistinguishable from the Doctor having done nothing at all.
            ReportSavedWithoutFiling?.Invoke(this, bundle.Directory);
            return new GatherOutcome(null, StillQueued: false, WasGatedOut: false);
        }

        var gatedOut = await DrainAsync(cancellation).ConfigureAwait(false);
        var issueId = _outbox
            .List()
            .FirstOrDefault(b => b.Directory == bundle.Directory)
            ?.Metadata.IssueId;
        // Deliberately returned rather than stashed in a field: several gathers can be in flight at once
        // (one per watched Bloom), so a field here would be read by the wrong caller.
        return new GatherOutcome(issueId, StillQueued: issueId == null, WasGatedOut: gatedOut);
    }

    /// <summary>
    /// A Bloom has gone. Works out whether its going was worth reporting — plan state 2, the crash that
    /// tells nobody.
    ///
    /// The detector deliberately refuses to judge this, because the answer depends on evidence gathered
    /// after the fact: the exit code (which we only have because we held a handle since before it died),
    /// Windows' own crash records, and whether Bloom left proof of an orderly shutdown. All of that is
    /// <see cref="ExitClassifier"/>'s business; this supplies it and acts on the verdict.
    /// </summary>
    private void ConsiderReportingAnExit(
        BloomTargetWatcher watcher,
        WindowsTargetProbe probe,
        DetectorVerdict verdict
    )
    {
        try
        {
            if (verdict.State != TargetState.Exited)
                return;
            // Under _lock because every watcher raises Observed on its OWN timer thread, so with two
            // Blooms being watched these sets are touched concurrently. An unsynchronised HashSet can
            // corrupt itself, throw, or - worst here, because it is silent - lose the entry and let a
            // second examination through. Only the test-and-claim is inside the lock; the work is not.
            lock (_lock)
            {
                // The same "we caused this" guard as OnReportWanted, and it has to be here too because
                // this path files its own report without going through that one. It matters most exactly
                // when we had to KILL rather than ask: a killed process runs no ProcessExit handler, so it
                // leaves no proof of a clean exit, which is precisely what this examination reports as
                // "exited without shutting down properly" - a second card about our own doing, and with a
                // different reason from the first, so the outbox could not have merged them either.
                if (_weAskedItToStop.Contains(watcher.Target.ProcessId))
                {
                    _exitsExamined.Add(watcher.Target.ProcessId);
                    _probes.Remove(watcher.Target.ProcessId);
                    Note(
                        $"Bloom {watcher.Target.ProcessId} has gone, as we asked it to; not examining that as a problem"
                    );
                    // We have claimed this death and are not going to examine it, so nothing else will
                    // release the handle. See WindowsTargetProbe.Dispose for why that is ours to do here
                    // and not the watcher's.
                    probe.Dispose();
                    return;
                }
                if (!_exitsExamined.Add(watcher.Target.ProcessId))
                    return; // one examination per process, and the one that claimed it owns the probe
                // Claimed. From here the background task below owns the probe and releases it when it has
                // finished reading the dead process's exit code.
                _probes.Remove(watcher.Target.ProcessId);
            }
            Note($"Bloom {watcher.Target.ProcessId} has gone; examining why");

            Interlocked.Increment(ref _workInFlight);
            _ = Task.Run(async () =>
            {
                try
                {
                    var session = Protocol.DoctorSessionStore.TryRead(watcher.Target.ProcessId);
                    probe.TryGetExitCode(out var exitCode);
                    var diedAt = probe.ExitedAt ?? DateTime.Now;

                    var evidence = new WindowsExitEvidenceCollector().Collect(
                        watcher.Target.ProcessId,
                        diedAt,
                        watcher.Target.StartTime,
                        session?.LogPath,
                        probe.TryGetExitCode(out var code) ? code : (int?)null,
                        watcher.IsPoisonedByDebugger,
                        watcher.Target.NeverFile,
                        // A session file with no exit record is the absence of proof that section 3.5 treats
                        // as evidence — but only when Bloom was capable of leaving one. No session file at
                        // all means an older Bloom, where absence means nothing.
                        //
                        // An exit record only counts as proof of a CLEAN exit when it says Bloom actually
                        // walked the orderly path. Bloom writes a record on the way out of a hard failure
                        // too; counting that as proof classified the failure as "Bloom shut down properly"
                        // and reported nothing.
                        //
                        // The test is the shutdown PHASE, not the record's ForcedByDoctor flag, because
                        // that flag means two different things: `_endedAtDoctorsRequest || phase == 0`. A
                        // Doctor asking a healthy Bloom to quit gets a forced flag on a perfectly orderly
                        // shutdown, and reporting that would be a card about our own request - the mistake
                        // `_weAskedItToStop` exists to prevent, coming back in through a different door
                        // whenever the asking and the examining are different Doctor processes. Phase 0
                        // means the orderly path was never begun, which is the thing actually worth a card.
                        cleanExitProofPresent: session == null
                            ? null
                            : session.Exit != null && session.Exit.ShutdownPhase > 0,
                        shutdownPhaseReached: session?.Exit?.ShutdownPhase,
                        exitRecordedAsForced: session?.Exit is { ShutdownPhase: 0 },
                        exeFileName: SafeFileName(watcher.Target.ExePath)
                    );

                    // Bloom telling us it already reported the problem outranks everything: a second card
                    // about the same trouble is noise.
                    if (session?.BloomAlreadyReported == true)
                    {
                        Note(
                            $"Bloom {watcher.Target.ProcessId} exited having already reported the problem "
                                + $"itself ({session.ReportedId}); saying nothing"
                        );
                        return;
                    }

                    // Which regime to judge by depends on whether this Bloom could leave proof at all.
                    var policy =
                        session == null
                            ? ExitReportPolicy.RequiresCorroboratingEvidence
                            : ExitReportPolicy.RequiresProofOfCleanExit;
                    var conclusion = ExitClassifier.Classify(evidence, policy);

                    Note(
                        $"Bloom {watcher.Target.ProcessId} exited: {conclusion.Verdict} — {conclusion.Explanation}"
                    );
                    if (!conclusion.ShouldReport)
                        return;

                    await GatherFileAndRecordAsync(
                            watcher.Target,
                            new DetectorVerdict
                            {
                                State = TargetState.Exited,
                                Report = ReportReason.ExitedWithoutProof,
                                Explanation = conclusion.Explanation,
                            },
                            mayFile: watcher.MayFileAReport(),
                            _stopping.Token,
                            // The process has gone, so its health channel has gone with it. This is the
                            // last reading we took while it was alive, and the only way this report can
                            // say what Bloom thought it was doing.
                            probe.PublishedSnapshot
                        )
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Note($"could not examine an exit: {e.GetType().Name}: {e.Message}");
                }
                finally
                {
                    // The handle goes now, and not before: everything above reads the exit code of a
                    // process that has already died, which is the one thing the handle is still good for.
                    probe.Dispose();
                    Interlocked.Decrement(ref _workInFlight);
                    ConsiderExiting();
                }
            });
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Bloom is crashing and has asked to be dumped while it still exists. This is the one time the Doctor
    /// has to be quick: Bloom is holding its own death open for about three seconds waiting for us.
    /// </summary>
    /// <summary>
    /// The exe's file name, or null if we never learned the path. Null rather than empty: the Event Log
    /// reader reads "unknown" as "accept any of the channel names the installer produces", where an empty
    /// string would match every message ever written.
    /// </summary>
    private static string? SafeFileName(string? path)
    {
        try
        {
            var name = string.IsNullOrWhiteSpace(path) ? null : Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void RespondToACrashingBloom(BloomTargetWatcher watcher)
    {
        try
        {
            if (!watcher.DumpRequested())
                return;
            // See the note in ConsiderReportingAnExit: shared set, per-watcher threads. A lost dedup here
            // would mean dumping a crashing Bloom twice, while it is holding its own death open for us.
            lock (_lock)
            {
                if (!_dumpsRequested.Add(watcher.Target.ProcessId))
                    return;
            }

            Note($"Bloom {watcher.Target.ProcessId} is crashing and asked for a dump");
            // Counted as work in flight, like the other two background jobs. Without this the Doctor can
            // decide it has nothing left to do and exit WHILE the dump is being written - and this is the
            // likeliest path for that to happen, because the crashing Bloom is about to disappear, which
            // is precisely the event that makes the Doctor look around and find nothing left to watch.
            // That is the exact failure _workInFlight was introduced for; the dump path was the one that
            // never got the guard.
            Interlocked.Increment(ref _workInFlight);
            _ = Task.Run(async () =>
            {
                try
                {
                    // Gather with the crash verdict, then tell Bloom it may go. Signalling completion is not
                    // in a finally by accident: if the dump failed there is nothing to wait for either, so
                    // Bloom should be released regardless.
                    await GatherFileAndRecordAsync(
                            watcher.Target,
                            new DetectorVerdict
                            {
                                State = TargetState.Exited,
                                Report = ReportReason.ExitedWithoutProof,
                                Explanation =
                                    "Bloom was crashing and asked to be dumped before it died",
                            },
                            mayFile: watcher.MayFileAReport(),
                            _stopping.Token
                        )
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Note($"could not dump the crashing Bloom: {e.GetType().Name}");
                }
                finally
                {
                    // Release Bloom first, then stop counting: Bloom is blocked waiting on this signal, so
                    // it should never wait on our bookkeeping.
                    watcher.SignalDumpComplete();
                    Interlocked.Decrement(ref _workInFlight);
                }
            });
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Considers ending a Bloom whose UI is gone but whose process lingers, holding the single-instance
    /// token that stops the user starting Bloom again. All the judgement is in <see cref="ZombieEnder"/> so
    /// that it can be tested; this only supplies the facts and acts on the answer.
    /// </summary>
    private void ConsiderEndingAZombie(BloomTargetWatcher watcher, DetectorVerdict verdict)
    {
        try
        {
            if (verdict.State != TargetState.Zombie || watcher.ZombieSince == null)
                return;
            // Both under one lock, because together they are a single test-and-claim: "the evidence is
            // gathered AND nobody else has taken the one attempt". Splitting them would let two watcher
            // threads both decide they were the one to end this process.
            lock (_lock)
            {
                if (!_zombiesReported.Contains(watcher.Target.ProcessId))
                    return; // the evidence has to be safely gathered first
                if (!_zombiesEnded.Add(watcher.Target.ProcessId))
                    return; // one attempt per process; we do not keep hammering at it
            }

            var decision = ZombieEnder.Decide(
                new ZombieDecisionFacts
                {
                    State = verdict.State,
                    ReportGathered = true,
                    SinceDetected = DateTimeOffset.UtcNow - watcher.ZombieSince.Value,
                    DebuggerCouldExplainIt = watcher.IsPoisonedByDebugger,
                    WorkInProgress = LooksLikeWorkInProgress(watcher.Target.ProcessId),
                    DisabledBySetting = _neverEndZombies,
                }
            );

            if (!decision.ShouldEnd)
            {
                // Put the ticket back: the reason may be temporary (the grace period, or a save in
                // progress), and we should look again next tick rather than never.
                lock (_lock)
                {
                    _zombiesEnded.Remove(watcher.Target.ProcessId);
                }
                return;
            }

            Note($"ending stuck Bloom {watcher.Target.ProcessId}: {decision.Explanation}");
            // Recorded before we act, so that the death cannot be observed and reported as a problem in the
            // gap between asking and it happening. See _weAskedItToStop.
            lock (_lock)
                _weAskedItToStop.Add(watcher.Target.ProcessId);
            _ = Task.Run(() =>
            {
                var outcome = ZombieEnder.End(watcher.Target.ProcessId);
                Note($"stuck Bloom {watcher.Target.ProcessId}: {Describe(outcome)}");
                ZombieEnded?.Invoke(this, outcome);
            });
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Whether Bloom says it is in the middle of something we should not interrupt. Reads what Bloom
    /// published; a Bloom that publishes nothing gives no reason to wait, and its UI is gone anyway.
    /// </summary>
    private static bool LooksLikeWorkInProgress(int processId)
    {
        try
        {
            if (!Protocol.DoctorChannelReader.TryRead(processId, out var state) || state == null)
                return false;
            if (state.LongOperationInProgress)
                return true;
            var activity = state.Activity ?? "";
            return activity.Contains("sav", StringComparison.OrdinalIgnoreCase)
                || activity.Contains("publish", StringComparison.OrdinalIgnoreCase)
                || activity.Contains("upload", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Describe(ZombieEndOutcome outcome) =>
        outcome switch
        {
            ZombieEndOutcome.ExitedOnRequest =>
                "it exited when asked, releasing the token properly",
            ZombieEndOutcome.Killed => "it had to be killed; the token is released either way",
            ZombieEndOutcome.AlreadyGone => "it had already gone",
            _ => "it could not be ended; the user may need to restart the machine",
        };

    /// <summary>
    /// Lets only one drain run at a time.
    ///
    /// Three separate things ask for a drain - startup, the five-minute timer, and the end of a gather -
    /// and all three were fire-and-forget, so two could overlap. Both would then list the SAME pending
    /// bundles and both walk the search-then-create flow, which is not atomic: the result is duplicate
    /// cards, or duplicate comments on one card, and a combined total that can exceed the deliberate
    /// three-per-day cap. That cap exists so a machine in a bad state cannot spam the tracker, so
    /// quietly exceeding it is the worst version of this bug.
    ///
    /// It WAITS rather than skipping. Skipping would be cheaper, but ReportNowAsync awaits this and then
    /// looks for its own bundle in the queue: if its drain had been skipped because another was already
    /// running, it could report failure for a report that was about to be filed perfectly well.
    ///
    /// **This is not the same gate as the one inside ReportOutbox.DrainAsync, and neither replaces the
    /// other.** That one is a NAMED semaphore guarding against a different PROCESS - `--drain`, which
    /// support can run while a Doctor is already going - and it gives up after a short wait, since the
    /// bundles belong to whoever holds it. This one is in-process only and waits indefinitely, which is
    /// what keeps ReportNowAsync's "drain, then look for my bundle" honest for our own three callers.
    /// </summary>
    private readonly SemaphoreSlim _drainGate = new(1, 1);

    /// <summary>
    /// Drains, and says whether another process was already doing it. See the note on
    /// <see cref="DrainOutcome"/> for why "nothing filed" and "somebody else is filing it" must not be
    /// the same answer.
    /// </summary>
    private async Task<bool> DrainAsync(CancellationToken cancellation)
    {
        try
        {
            await _drainGate.WaitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        try
        {
            if (_outbox.Pending().Count == 0)
                return false;
            var outcome = await _outbox
                .DrainAsync(new YouTrackSubmitter(), cancellation)
                .ConfigureAwait(false);
            if (outcome.Filed > 0)
                Note($"filed {outcome.Filed} report(s)");
            else if (outcome.GatedOut)
                Note("another Freeze Doctor is sending the queued reports");
            PublishStatus();
            ConsiderExiting();
            return outcome.GatedOut;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Note($"could not send reports: {e.GetType().Name}");
        }
        finally
        {
            _drainGate.Release();
        }
        return false;
    }

    /// <summary>
    /// Decides whether there is anything left worth staying alive for: a Bloom to watch, or a report
    /// waiting that might yet get out. Deliberately does NOT wait indefinitely on the outbox — a zombie
    /// Bloom or a permanently offline machine must not pin the Doctor forever (plan §3.6).
    /// </summary>
    private void ConsiderExiting()
    {
        // Never quit mid-job. Gathering a report takes tens of seconds, and the moment we most want to be
        // patient is right after a Bloom has died — which is also the moment we have least left to watch.
        if (Volatile.Read(ref _workInFlight) > 0)
            return;

        // Both under the one lock, and the second is not fussiness: _lastBloomSeenAt is a nullable
        // DateTimeOffset, far too wide to be read atomically, and it is written under this lock from the
        // discovery thread while this runs on another. Reading it unlocked risks seeing half of one value
        // and half of another - and this is the code that decides whether the Doctor exits.
        DateTimeOffset? lastSeen;
        lock (_lock)
        {
            if (_watchers.Count > 0)
                return;
            lastSeen = _lastBloomSeenAt;
        }
        if (lastSeen == null)
            return; // we have not seen a Bloom yet; wait for one rather than exiting immediately

        var waited = DateTimeOffset.UtcNow - lastSeen.Value;
        var pending = _outbox.Pending().Count;
        if (pending > 0 && waited < LingerForOutbox)
            return;

        NothingLeftToDo?.Invoke(this, EventArgs.Empty);
    }

    private void PublishStatus()
    {
        List<string> lines;
        lock (_lock)
        {
            lines = _watchers
                .Values.Select(w =>
                    $"Bloom {w.Target.ProcessId} ({w.Target.Channel}): {Describe(w.State)}"
                )
                .ToList();
        }
        if (lines.Count == 0)
            lines.Add("Bloom Status: Not running");

        var pending = _outbox.Pending().Count;
        StatusChanged?.Invoke(
            this,
            new DoctorStatus
            {
                BloomLines = lines,
                OutboxLine = pending switch
                {
                    0 => null,
                    1 => "1 report waiting to send",
                    _ => $"{pending} reports waiting to send",
                },
                LastEvent = _lastEvent,
            }
        );
    }

    /// <summary>The card's own vocabulary, so the window says what the card promised it would say.</summary>
    private static string Describe(TargetState state) =>
        state switch
        {
            TargetState.Healthy => "Running",
            TargetState.Suspect => "Running (not answering just now)",
            TargetState.Frozen => "Frozen",
            TargetState.Zombie => "Stuck in the background with no window",
            TargetState.Exited => "Not running",
            _ => state.ToString(),
        };

    private void Note(string message)
    {
        _lastEvent = $"{DateTime.Now:HH:mm:ss}  {message}";
        DoctorLog.Write(message);
        PublishStatus();
    }

    private static bool IsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                RobustIO.DeleteDirectoryAndContents(path);
        }
        catch (Exception)
        {
            // Anything still in there has already been moved into the bundle; a leftover temp folder is
            // untidy, not harmful.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping.Cancel();
        _discovery?.Dispose();
        _drain?.Dispose();
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
                watcher.Dispose();
            _watchers.Clear();
            // Only the probes still in here, which by construction are the ones no examination has
            // claimed - a claim removes its probe from this dictionary and takes over releasing it.
            foreach (var probe in _probes.Values)
                probe.Dispose();
            _probes.Clear();
        }
        _stopping.Dispose();
        _drainGate.Dispose();
    }
}
