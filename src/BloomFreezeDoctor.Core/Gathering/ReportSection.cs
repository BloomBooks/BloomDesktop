// Aliased rather than imported plainly, so uses below still read as Protocol.DoctorSession - it is worth
// saying at each use that this is the shared wire format, not something local to the Doctor.
using Protocol = BloomFreezeDoctor.Protocol;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// One titled chunk of a report. A section either has a body or has a reason it could not be
/// gathered; the plan's rule (§4) is that a section we cannot collect says so in one line and the
/// report still goes out, so a failure here is content, never an exception.
/// </summary>
public sealed record ReportSection
{
    /// <summary>Heading the section appears under.</summary>
    public required string Title { get; init; }

    /// <summary>The gathered text, or empty if <see cref="Failure"/> explains why not.</summary>
    public string Body { get; init; } = "";

    /// <summary>One line saying why this section is missing, or null if it was gathered.</summary>
    public string? Failure { get; init; }

    /// <summary>How long collection took, which is worth knowing when a report was slow to produce.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Files this section produced that belong in the bundle's attachments (a dump, say). Paths are
    /// absolute; the gatherer decides what to do with them.
    /// </summary>
    public IReadOnlyList<string> Artifacts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// A one-line summary promoted to the top of the report, where the verdict lives. Only the few
    /// collectors that find something decisive set this — "the UI thread is blocked in X" belongs at
    /// the top of the card, not eighty lines down.
    /// </summary>
    public string? Headline { get; init; }

    /// <summary>Builds a section that failed, with the reason a human needs.</summary>
    public static ReportSection Failed(string title, string why, TimeSpan duration = default) =>
        new()
        {
            Title = title,
            Failure = why,
            Duration = duration,
        };
}

/// <summary>
/// What a collector is told about the Bloom it is gathering evidence from.
/// </summary>
public sealed record GatherContext
{
    /// <summary>The Bloom in question. May already have exited.</summary>
    public required BloomTargetFacts Target { get; init; }

    /// <summary>Why we are gathering, which shapes what matters.</summary>
    public required DetectorVerdict Verdict { get; init; }

    /// <summary>
    /// Called the moment nothing further needs the target process alive — which, for a Bloom that is dying
    /// and waiting for us, is the moment it may finish dying.
    ///
    /// **This is not a nicety; without it a crashing Bloom waits for the whole pipeline.** The crash path
    /// released Bloom only when the entire gather had finished, and a gather is bounded by two minutes and
    /// includes every collector, the log copy, queueing the report, and uploading a 17 MB dump to S3 over
    /// whatever connection the user has. None of that needs Bloom. Once the runtime has written the dump,
    /// everything else reads the *file*.
    ///
    /// It went unnoticed because the old three-second wait capped the damage: Bloom gave up long before the
    /// gather ended, so the misplaced signal cost nothing except the dump. Making the wait generous — which
    /// it had to become, since a real dump takes longer than three seconds — turned a harmless mistake into
    /// a crashing Bloom held open while a card is filed.
    /// </summary>
    public Action? TargetNoLongerNeeded { get; init; }

    /// <summary>
    /// True when this report is about a Bloom that stopped responding, rather than one that crashed or
    /// simply went away.
    ///
    /// It exists because several collectors draw a *conclusion* from what they see, and the same
    /// observation supports opposite conclusions in the two cases. "No thread is burning CPU, so this is a
    /// wait rather than a spin" is a genuinely useful deduction about a freeze; on a report headed "Bloom
    /// was crashing and asked to be dumped before it died" it reads as a flat contradiction, because of
    /// course nothing is spinning — the process is on its way out. Likewise "the UI thread is in its
    /// message loop", which is reassuring nonsense next to a crash.
    ///
    /// So collectors state the observation either way and reserve the freeze reasoning for a freeze.
    /// </summary>
    public bool IsAboutAFreeze =>
        Verdict.Report
            is ReportReason.Frozen
                or ReportReason.RecoveredFromFreeze
                or ReportReason.DiedWhileFrozen
                or ReportReason.Zombie;

    /// <summary>
    /// Narrower than <see cref="IsAboutAFreeze"/>: is the UI thread suspected of being STUCK.
    ///
    /// A zombie is not. Bloom is alive, its message loop is running perfectly, and its window has gone -
    /// which is why the deductions that suit a freeze read as nonsense on a zombie report. A real one said:
    ///
    ///     Verdict: alive with no visible window for 31s
    ///     No thread is burning CPU, so this is a wait rather than a spin.
    ///     WebView2 answers normally, so the block is in Bloom's .NET UI thread, not the browser.
    ///
    /// There is no block. The second line asserts one, and a reader could easily come away hunting a
    /// deadlock instead of asking where the window went. So the conclusions that presuppose a stuck UI
    /// thread use this, while the phrasing that merely distinguishes a live process from a dying one -
    /// "the UI thread is in its message loop", which for a zombie is the KEY finding rather than a
    /// reassurance - still uses IsAboutAFreeze.
    /// </summary>
    public bool IsAboutTheUiBeingStuck =>
        Verdict.Report
            is ReportReason.Frozen
                or ReportReason.RecoveredFromFreeze
                or ReportReason.DiedWhileFrozen;

    /// <summary>True if the process was still running when gathering began.</summary>
    public required bool ProcessWasAlive { get; init; }

    /// <summary>Where to put files too big or too binary for the report text.</summary>
    public required string ArtifactDirectory { get; init; }

    /// <summary>The log file we identified as this Bloom's, if we found one.</summary>
    public string? BloomLogPath { get; init; }

    /// <summary>WebView2's debugging port, if we discovered one for this process.</summary>
    public int? CdpPort { get; init; }

    /// <summary>
    /// What Bloom recorded about itself at startup, or null for a Bloom that publishes nothing — which is
    /// every Bloom in the field today, so nothing may depend on this being present.
    /// </summary>
    public Protocol.DoctorSession? Session { get; init; }

    /// <summary>
    /// The live state Bloom published at the moment we decided to gather: heartbeats, what it thought it was
    /// doing, its server's worker counts. Null for a Bloom that publishes nothing.
    /// </summary>
    public Protocol.DoctorChannelSnapshot? PublishedState { get; init; }
}

/// <summary>
/// Collects one section of evidence. Implementations must be read-only with respect to Bloom, must
/// not throw (the gatherer catches anyway, but a collector that relies on that is badly written), and
/// must respect the deadline they are given.
/// </summary>
public interface IEvidenceCollector
{
    /// <summary>Heading this collector's section appears under.</summary>
    string Title { get; }

    /// <summary>
    /// How long this collector may reasonably take. The gatherer abandons it after this, because a
    /// report that never finishes is worth nothing and the user may be waiting to restart Bloom.
    /// </summary>
    TimeSpan Budget { get; }

    /// <summary>True if this collector can say anything useful in the current situation.</summary>
    bool AppliesTo(GatherContext context);

    /// <summary>Gathers the section.</summary>
    Task<ReportSection> CollectAsync(GatherContext context, CancellationToken cancellation);
}
