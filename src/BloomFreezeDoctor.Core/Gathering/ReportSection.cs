// Aliased rather than imported plainly, so uses below still read as Protocol.DoctorSession - it is worth
// saying at each use that this is the shared wire format, not something local to the Doctor.
using Protocol = BloomBooks.FreezeDoctor.Protocol;

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
