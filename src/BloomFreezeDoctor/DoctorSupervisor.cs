using System.Diagnostics;
using BloomFreezeDoctor.Gathering;
using BloomFreezeDoctor.Outbox;
// Aliased rather than imported plainly, so uses below still read as Protocol.DoctorSignals - it is worth
// saying at each use that this is the shared wire format, not something local to the Doctor.
using Protocol = BloomBooks.FreezeDoctor.Protocol;

namespace BloomFreezeDoctor;

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
    private readonly bool _forceFiling;
    private readonly Dictionary<int, BloomTargetWatcher> _watchers = new();
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
        string targetProcessName = "Bloom",
        bool forceFiling = false,
        ReportOutbox? outbox = null,
        bool neverEndZombies = false
    )
    {
        _project = project;
        _targetProcessName = targetProcessName;
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

    /// <summary>Raised when the Doctor has nothing left to do and should exit.</summary>
    public event EventHandler? NothingLeftToDo;

    /// <summary>The queue of reports waiting to be sent.</summary>
    public ReportOutbox Outbox => _outbox;

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
        AdoptFacts(facts);
    }

    /// <summary>
    /// Gathers and files a report for a process right now, whatever state it is in. This is the CTRL-key
    /// "Report now" of the card, and it is also how support gets a snapshot of a Bloom that is merely
    /// slow rather than frozen.
    /// </summary>
    public async Task<string?> ReportNowAsync(int processId, CancellationToken cancellation)
    {
        var facts = GatherContextBuilder.DescribeRunningProcess(processId);
        if (facts == null)
            return null;
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
        return await GatherFileAndRecordAsync(facts, verdict, mayFile: true, cancellation)
            .ConfigureAwait(false);
    }

    /// <summary>Looks for Blooms we are not yet watching, and forgets ones that have gone.</summary>
    private void Discover()
    {
        try
        {
            var running = Process.GetProcessesByName(_targetProcessName);
            foreach (var process in running)
            {
                var facts = GatherContextBuilder.DescribeRunningProcess(process.Id);
                if (facts != null)
                    AdoptFacts(facts);
            }

            lock (_lock)
            {
                if (_watchers.Count > 0)
                    _lastBloomSeenAt = DateTimeOffset.UtcNow;

                // Drop watchers whose process has exited. The watcher itself reports the exit first, so
                // by the time we get here its story has been told.
                foreach (var id in _watchers.Keys.ToList())
                {
                    if (running.Any(p => p.Id == id))
                        continue;
                    _watchers[id].Dispose();
                    _watchers.Remove(id);
                }
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
            watcher.Start();
            Note($"watching Bloom {facts.ProcessId} ({facts.Channel})");
        }
    }

    /// <summary>
    /// A watcher has decided something is wrong. Gather, queue and try to send — on a worker thread, and
    /// without letting one failure take down the Doctor.
    /// </summary>
    private void OnReportWanted(object? sender, ReportWantedEventArgs e)
    {
        Interlocked.Increment(ref _workInFlight);
        _ = Task.Run(async () =>
        {
            try
            {
                Note(
                    $"gathering evidence about Bloom {e.Target.ProcessId}: {e.Verdict.Explanation}"
                );
                var issue = await GatherFileAndRecordAsync(
                        e.Target,
                        e.Verdict,
                        e.MayFile || _forceFiling,
                        _stopping.Token
                    )
                    .ConfigureAwait(false);
                if (issue != null)
                    ReportFiled?.Invoke(this, issue);
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

    private async Task<string?> GatherFileAndRecordAsync(
        BloomTargetFacts facts,
        DetectorVerdict verdict,
        bool mayFile,
        CancellationToken cancellation
    )
    {
        var alive = IsAlive(facts.ProcessId);
        var artifacts = Path.Combine(
            Path.GetTempPath(),
            "BloomFreezeDoctor",
            $"gather-{facts.ProcessId}-{Guid.NewGuid():N}"
        );
        var context = GatherContextBuilder.Build(facts, verdict, alive, artifacts);

        var report = await new EvidenceGatherer()
            .GatherAsync(context, mayFile, cancellation)
            .ConfigureAwait(false);

        var bundle = _outbox.Enqueue(report, _project, facts.Channel, verdict.Report.ToString());
        // Ending a zombie is only allowed once its evidence is safely on disk, so this is the moment that
        // unlocks it.
        if (verdict.Report == ReportReason.Zombie)
            lock (_lock)
                _zombiesReported.Add(facts.ProcessId);
        Note(
            report.MayFile
                ? $"report queued ({report.Summary})"
                : "report gathered to disk only (developer or automation run, or a debugged process)"
        );
        TryDeleteDirectory(artifacts);
        PublishStatus();

        if (!report.MayFile)
            return null;

        await DrainAsync(cancellation).ConfigureAwait(false);
        return _outbox
            .List()
            .FirstOrDefault(b => b.Directory == bundle.Directory)
            ?.Metadata.IssueId;
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
                if (!_exitsExamined.Add(watcher.Target.ProcessId))
                    return; // one examination per process
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
                        cleanExitProofPresent: session == null ? null : session.Exit != null,
                        shutdownPhaseReached: session?.Exit?.ShutdownPhase
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
                            mayFile: !watcher.IsPoisonedByDebugger && !watcher.Target.NeverFile,
                            _stopping.Token
                        )
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Note($"could not examine an exit: {e.GetType().Name}: {e.Message}");
                }
                finally
                {
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
                            mayFile: !watcher.IsPoisonedByDebugger && !watcher.Target.NeverFile,
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
                    watcher.SignalDumpComplete();
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

    private async Task DrainAsync(CancellationToken cancellation)
    {
        try
        {
            if (_outbox.Pending().Count == 0)
                return;
            var filed = await _outbox
                .DrainAsync(new YouTrackSubmitter(), cancellation)
                .ConfigureAwait(false);
            if (filed > 0)
                Note($"filed {filed} report(s)");
            PublishStatus();
            ConsiderExiting();
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Note($"could not send reports: {e.GetType().Name}");
        }
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

        lock (_lock)
        {
            if (_watchers.Count > 0)
                return;
        }
        if (_lastBloomSeenAt == null)
            return; // we have not seen a Bloom yet; wait for one rather than exiting immediately

        var waited = DateTimeOffset.UtcNow - _lastBloomSeenAt.Value;
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
                Directory.Delete(path, recursive: true);
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
        }
        _stopping.Dispose();
    }
}
