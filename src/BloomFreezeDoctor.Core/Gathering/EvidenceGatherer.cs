using System.Diagnostics;
using System.Text;
using BloomFreezeDoctor.Protocol;

namespace BloomFreezeDoctor.Gathering;

/// <summary>A finished report, ready to be filed or to sit in the outbox until it can be.</summary>
public sealed record GatheredReport
{
    /// <summary>The one-line summary that becomes the tracker card's title.</summary>
    public required string Summary { get; init; }

    /// <summary>The full report body, in Markdown.</summary>
    public required string Body { get; init; }

    /// <summary>
    /// A stable hash of what makes this problem *this* problem, used to recognise the same freeze
    /// happening again (plan §5.2).
    /// </summary>
    public required string Fingerprint { get; init; }

    /// <summary>Files to attach: the dump, and anything else too big for the body.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>How long the whole gather took.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>False for a developer or automation run, or a target seen under a debugger.</summary>
    public required bool MayFile { get; init; }

    /// <summary>
    /// The handful of facts that differ between occurrences of the same problem, for the comment posted
    /// when a card for this fingerprint already exists. See EvidenceGatherer.BuildRecurrenceNote.
    /// </summary>
    public string? RecurrenceNote { get; init; }
}

/// <summary>
/// Runs the collectors and assembles their output into a report.
///
/// Two rules shape this class, both from the plan: **every section is optional** — one that cannot be
/// gathered says so in a line and the report still goes out — and **the whole thing is bounded**,
/// because the person whose Bloom just froze may be waiting to restart it.
/// </summary>
public sealed class EvidenceGatherer
{
    private readonly IReadOnlyList<IEvidenceCollector> _collectors;

    /// <summary>
    /// Total time allowed for a whole gather. Individual collectors have their own smaller budgets;
    /// this is the backstop for the case where several are slow at once.
    /// </summary>
    public static readonly TimeSpan TotalBudget = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Creates a gatherer with the default collectors, **in the order they appear in the report**. That
    /// order is deliberate: whoever opens the card should meet the managed stacks first, because they
    /// usually contain the answer, and the machine-and-network section last, because it usually only
    /// rules things out.
    /// </summary>
    public EvidenceGatherer()
        : this(
            new IEvidenceCollector[]
            {
                new ManagedStacksCollector(),
                new ProcessEvidenceCollector(),
                new WaitChainCollector(),
                new WebViewCollector(),
                new BloomLogCollector(),
                new SystemEvidenceCollector(),
            }
        ) { }

    /// <summary>Creates a gatherer with a specific set of collectors, for tests.</summary>
    public EvidenceGatherer(IReadOnlyList<IEvidenceCollector> collectors)
    {
        _collectors = collectors;
    }

    /// <summary>
    /// Gathers everything applicable and returns the finished report. Never throws for a collector's
    /// sake: a failed collector becomes a line of text.
    /// </summary>
    public async Task<GatheredReport> GatherAsync(
        GatherContext context,
        bool mayFile,
        CancellationToken cancellation = default
    )
    {
        var overall = Stopwatch.StartNew();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TotalBudget);

        var sections = new List<ReportSection>();
        foreach (var collector in _collectors)
        {
            if (!collector.AppliesTo(context))
                continue;
            sections.Add(await RunOneAsync(collector, context, budget.Token).ConfigureAwait(false));
        }

        var headlines = sections
            .Select(s => s.Headline)
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!)
            .ToList();

        return new GatheredReport
        {
            Summary = BuildSummary(context, headlines),
            Body = BuildBody(context, sections, headlines),
            Fingerprint = ReportFingerprint.For(context, sections),
            Artifacts = sections.SelectMany(s => s.Artifacts).ToList(),
            Duration = overall.Elapsed,
            MayFile = mayFile,
            RecurrenceNote = BuildRecurrenceNote(context),
        };
    }

    /// <summary>
    /// The few facts that differ between one occurrence of a problem and the next, for the comment added
    /// to a card that already exists.
    ///
    /// **Why not the whole report.** Two reports sharing a fingerprint share their reason, Bloom's version,
    /// the channel and the top five frames of the UI thread — that is what the fingerprint means — so the
    /// bulk of a second report is identical by construction. A card accumulating several 18 KB reports
    /// becomes unreadable at exactly the moment it is most useful, and the second report's dump and log are
    /// near-duplicates of the first at some 16 MB each.
    ///
    /// What is worth saying is what varied, because that is what tells you whether the recurrences are the
    /// same situation or the same stack arrived at from different directions. The full report stays on the
    /// machine; the submitter adds its folder name so it can be fetched if anyone wants it.
    /// </summary>
    private static string BuildRecurrenceNote(GatherContext context)
    {
        var lines = new List<string> { $"- Process {context.Target.ProcessId}" };

        if (!string.IsNullOrWhiteSpace(context.Verdict.Explanation))
            lines.Add($"- What we saw: {context.Verdict.Explanation}");

        var state = context.PublishedState;
        if (state != null)
        {
            lines.Add(
                "- What Bloom thought it was doing: "
                    + (
                        string.IsNullOrWhiteSpace(state.Activity)
                            ? "(nothing in particular)"
                            : state.Activity
                    )
            );
            if (state.LongOperationInProgress)
                lines.Add(
                    "- Bloom said it was deliberately busy on a long operation, so it was being given "
                        + "extra patience"
                );
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Runs one collector inside its own budget. A collector that overruns is abandoned rather than
    /// allowed to hold up the report — its section says so, which is itself a useful signal.
    /// </summary>
    private static async Task<ReportSection> RunOneAsync(
        IEvidenceCollector collector,
        GatherContext context,
        CancellationToken outer
    )
    {
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(outer);
        scope.CancelAfter(collector.Budget);
        var timer = Stopwatch.StartNew();
        try
        {
            return await collector.CollectAsync(context, scope.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return ReportSection.Failed(
                collector.Title,
                $"gave up after {timer.Elapsed.TotalSeconds:F0}s (its budget is "
                    + $"{collector.Budget.TotalSeconds:F0}s); that it was slow may itself be a clue",
                timer.Elapsed
            );
        }
        catch (Exception e)
        {
            return ReportSection.Failed(
                collector.Title,
                $"failed with {e.GetType().Name}: {e.Message}",
                timer.Elapsed
            );
        }
    }

    /// <summary>
    /// Builds the card title. It leads with the verdict and the most telling headline, because a
    /// tracker list shows titles and nothing else, and "Bloom froze" tells a reader nothing they did
    /// not already know.
    /// </summary>
    private static string BuildSummary(GatherContext context, List<string> headlines)
    {
        var what = context.Verdict.Report switch
        {
            ReportReason.Frozen => "UI frozen",
            ReportReason.RecoveredFromFreeze => "UI froze, then recovered",
            ReportReason.DiedWhileFrozen => "UI froze, then the process died",
            ReportReason.Zombie => "UI gone but process still running",
            ReportReason.ExitedWithoutProof => "exited without shutting down properly",
            ReportReason.RequestedByPerson => "snapshot requested by a person",
            _ => "problem",
        };

        // The first headline that names a blocked call is the most useful thing we can put in a title.
        var detail = headlines.FirstOrDefault(h =>
            h.Contains("blocked in", StringComparison.OrdinalIgnoreCase)
        );
        var version = string.IsNullOrEmpty(context.Target.Channel)
            ? ""
            : $" [{context.Target.Channel}]";

        return detail == null
            ? $"Freeze Doctor: {what}{version}"
            : $"Freeze Doctor: {what} — {Trim(detail, 90)}{version}";
    }

    private static string BuildBody(
        GatherContext context,
        List<ReportSection> sections,
        List<string> headlines
    )
    {
        var text = new StringBuilder();

        text.AppendLine("## What the Freeze Doctor saw");
        text.AppendLine();
        text.AppendLine($"- **Verdict:** {context.Verdict.Explanation}");
        foreach (var headline in headlines)
            text.AppendLine($"- {headline}");
        text.AppendLine();

        text.AppendLine("### The Bloom this happened to");
        text.AppendLine();
        text.AppendLine("| | |");
        text.AppendLine("| --- | --- |");
        text.AppendLine($"| Channel | {context.Target.Channel} |");
        text.AppendLine($"| Executable | `{context.Target.ExePath}` |");
        text.AppendLine($"| Process id | {context.Target.ProcessId} |");
        text.AppendLine(
            $"| Started | {context.Target.StartTime:yyyy-MM-dd HH:mm:ss} local "
                + $"({context.Target.StartTime.ToUniversalTime():HH:mm:ss}Z) |"
        );
        text.AppendLine($"| Command line | `{context.Target.CommandLine}` |");
        text.AppendLine(
            $"| Still running when gathered | {(context.ProcessWasAlive ? "yes" : "no")} |"
        );
        text.AppendLine($"| Bloom log | {context.BloomLogPath ?? "not identified"} |");
        text.AppendLine(
            $"| WebView2 debug port | {(context.CdpPort.HasValue ? context.CdpPort.ToString() : "not found")} |"
        );
        if (context.Session != null)
        {
            text.AppendLine($"| Bloom version | {context.Session.Version} |");
            if (!string.IsNullOrEmpty(context.Session.CollectionName))
                text.AppendLine($"| Collection | {context.Session.CollectionName} |");
        }
        text.AppendLine();

        // What Bloom itself said it was doing is often the most useful line in the whole report — a stack
        // says the UI thread is waiting, this says which request has been running for 47 seconds.
        AppendWhatBloomSaid(text, context);

        foreach (var section in sections)
        {
            text.AppendLine($"### {section.Title}");
            text.AppendLine();
            if (section.Failure != null)
            {
                text.AppendLine($"*Not gathered: {section.Failure}*");
            }
            else
            {
                text.AppendLine(section.Body.TrimEnd());
            }
            text.AppendLine();
        }

        text.AppendLine("---");
        text.AppendLine();
        text.AppendLine(
            "*Filed automatically by the Bloom Freeze Doctor. Sections marked as not gathered were "
                + "attempted and failed; nothing was withheld.*"
        );
        return text.ToString();
    }

    /// <summary>
    /// Quotes what Bloom published about itself through the shared channel: what it thought it was doing,
    /// how its heartbeats were faring, and how its server's workers were placed. Absent for every Bloom in
    /// the field that predates this, so it says so plainly rather than leaving a gap.
    /// </summary>
    private static void AppendWhatBloomSaid(StringBuilder text, GatherContext context)
    {
        text.AppendLine("### What Bloom said about itself");
        text.AppendLine();

        // First, and outside the channel check below, because it must appear even for a Bloom that
        // published no live state: somebody reading this needs to know it is a rehearsal before they spend
        // any time on the stacks.
        if (!string.IsNullOrEmpty(context.Session?.SimulatedFailure))
        {
            text.AppendLine(
                $"> **This freeze was deliberate.** Bloom was told to simulate `{context.Session!.SimulatedFailure}` "
                    + $"by the `BLOOM_SIMULATE_FREEZE` environment variable, so it broke itself on purpose. "
                    + "Everything below was gathered exactly as it would be for a real freeze — only the "
                    + "decision to file is different, and a report like this is normally kept on disk rather "
                    + "than sent."
            );
            text.AppendLine();
        }

        if (context.PublishedState == null)
        {
            // Two quite different silences, and saying the wrong one is worse than saying nothing. The
            // channel lives in Bloom's own memory, so for a Bloom that has already died there is nothing
            // left to read however new it is. Calling that "does not publish a health channel" would be
            // false in exactly the reports where what Bloom last thought it was doing matters most.
            text.AppendLine(
                context.ProcessWasAlive
                    ? "Nothing: this Bloom does not publish a health channel, so everything below was "
                        + "observed from outside. That is the normal case for a Bloom released before the "
                        + "Freeze Doctor existed."
                    : "Nothing readable: Bloom had already gone by the time this was gathered, and the "
                        + "health channel lives in the process's own memory, so it went too. Whether this "
                        + "Bloom published one cannot be told from here."
            );
            text.AppendLine();
            return;
        }

        // State we hold for a Bloom that has gone can only be the last reading taken before it went, so
        // date it rather than presenting it as the state at the moment of gathering.
        if (!context.ProcessWasAlive)
        {
            text.AppendLine(
                "*Bloom had gone by the time this was gathered, so this is the last thing it published "
                    + "about itself, taken within a second or so of its death.*"
            );
            text.AppendLine();
        }

        var state = context.PublishedState;
        text.AppendLine("| | |");
        text.AppendLine("| --- | --- |");
        text.AppendLine(
            $"| What Bloom thought it was doing | {(string.IsNullOrWhiteSpace(state.Activity) ? "(nothing in particular)" : state.Activity)} |"
        );
        text.AppendLine(
            $"| UI-thread heartbeat last beat | {Describe(state.UiHeartbeatAge)} ago |"
        );
        text.AppendLine(
            $"| Background heartbeat last beat | {Describe(state.WatchdogHeartbeatAge)} ago |"
        );
        text.AppendLine($"| Server workers | {DescribeServerWorkers(state)} |");
        if (state.ShutdownPhase != BloomShutdownPhase.None)
            text.AppendLine($"| Shutdown had begun | {state.ShutdownPhase.Describe()} |");
        if (state.LongOperationInProgress)
            text.AppendLine("| Bloom said it was | deliberately busy on a long operation |");
        text.AppendLine();

        // The comparison that identifies the freeze class, spelled out so nobody has to work it out.
        if (
            state.UiHeartbeatAge > TimeSpan.FromSeconds(5)
            && state.WatchdogHeartbeatAge <= TimeSpan.FromSeconds(5)
        )
            text.AppendLine(
                "> **The UI thread stopped while the rest of the process kept running.** That is the "
                    + "signature of a blocked UI thread rather than a wedged process — and if the window was "
                    + "still answering messages, of a managed wait on the STA thread, which nothing outside "
                    + "Bloom can detect."
            );
        else if (state.UiHeartbeatAge > TimeSpan.FromSeconds(5))
            text.AppendLine(
                "> **Both heartbeats stopped**, so the whole process is wedged rather than just its UI "
                    + "thread — a garbage collection that will not finish, or a suspended process."
            );
        text.AppendLine();

        AppendRequestsInFlight(text, context);
    }

    /// <summary>
    /// Every API request Bloom had in flight, from the session file rather than the shared page — the page
    /// has room for one line of activity, and in a freeze the requests it cannot name are most of the
    /// picture: which paths, how long each, and which of them are queued behind a lock another is holding.
    ///
    /// **How current this is.** Bloom's watchdog thread rewrites the session every ten seconds and keeps
    /// doing so while the UI is wedged, and a freeze is not reported until the UI has been unresponsive for
    /// a minute — five during a long operation — so by then the list has been rewritten several times over.
    /// The age is printed anyway, for the two cases where it matters: a whole-process wedge, where the
    /// watchdog has stopped too and the reading is as old as the wedge, and a request that began after the
    /// last write and so is missing entirely.
    /// </summary>
    private static void AppendRequestsInFlight(StringBuilder text, GatherContext context)
    {
        var requests = context.Session?.InFlightRequests;
        if (requests == null || requests.Count == 0)
            return;

        text.AppendLine("**API requests in flight**, longest-running first:");
        text.AppendLine();
        foreach (var request in requests)
            text.AppendLine($"- {request}");
        text.AppendLine();

        var capturedAt = context.Session!.InFlightRequestsAtUtc;
        if (capturedAt != null)
        {
            var age = DateTimeOffset.UtcNow - capturedAt.Value;
            text.AppendLine(
                $"*Read {Describe(age < TimeSpan.Zero ? TimeSpan.Zero : age)} before this report was "
                    + "gathered; Bloom records it every ten seconds.*"
            );
            text.AppendLine();
        }
    }

    private static string Describe(TimeSpan age) =>
        age == TimeSpan.MaxValue ? "never"
        : age.TotalSeconds < 90 ? $"{age.TotalSeconds:F0}s"
        : $"{age.TotalMinutes:F1} minutes";

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max - 1) + "…";

    /// <summary>
    /// The server pool in one phrase, with the judgement made rather than left to the reader. Public so the
    /// judgement itself can be tested: the comparison is easy to write backwards, and a diagnostic that
    /// says the opposite of the truth is worse than one that says nothing.
    ///
    /// The arithmetic is the whole point: BloomServer's own rule is that the pool is exhausted when every
    /// live worker is blocked, so "3 blocked" means nothing until it is set against the pool size, and
    /// whether anything is actually held up depends on the queue behind it.
    /// </summary>
    public static string DescribeServerWorkers(DoctorChannelSnapshot state)
    {
        var text =
            $"{state.ServerWorkers} threads; {state.ServerBusyWorkers} busy, "
            + $"{state.ServerBlockedWorkers} blocked; {state.ServerQueuedRequests} request(s) queued";
        // The condition BloomServer itself acts on, said out loud. A pool that has run out while requests
        // wait behind it is the shape of a server-side deadlock rather than a slow operation.
        if (state.ServerWorkers > 0 && state.ServerBlockedWorkers >= state.ServerWorkers)
            text +=
                state.ServerQueuedRequests > 0
                    ? " — **every worker is blocked and requests are waiting behind them**"
                    : " — **every worker is blocked**";
        return text;
    }
}
