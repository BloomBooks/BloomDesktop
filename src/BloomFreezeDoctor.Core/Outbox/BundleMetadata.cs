using System.Text.Json.Serialization;

namespace BloomFreezeDoctor.Outbox;

/// <summary>Where a queued report has got to.</summary>
public enum BundleState
{
    /// <summary>Waiting to be filed. The normal state, possibly for weeks on a bad connection.</summary>
    Pending,

    /// <summary>Filed. Kept briefly so the local record shows what happened and where it went.</summary>
    Filed,

    /// <summary>
    /// Gathered but deliberately never to be filed: a developer or automation run, or a target seen
    /// under a debugger. Kept on disk because it is still the evidence, and it is how we test the
    /// gatherer without touching the tracker.
    /// </summary>
    NotForFiling,

    /// <summary>
    /// The tracker refused it in a way retrying cannot fix — a bad token, a missing project, a
    /// malformed request. Retrying forever would hammer the server and hide the real problem, so we
    /// stop and say so loudly.
    /// </summary>
    FailedPermanently,
}

/// <summary>
/// The <c>meta.json</c> beside each queued report: everything the outbox needs to decide what to do
/// with a bundle without reading the report itself.
/// </summary>
public sealed record BundleMetadata
{
    /// <summary>Schema version, so a newer Doctor can read an older machine's queue.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>The tracker card's title.</summary>
    public required string Summary { get; init; }

    /// <summary>What makes this problem this problem; the key for dedupe (plan §5.2).</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Tracker project to file into — `BL` normally, `AUT` when testing.</summary>
    public required string Project { get; init; }

    /// <summary>Where this bundle has got to.</summary>
    public required BundleState State { get; init; }

    /// <summary>
    /// When the problem happened, in UTC. Not when it was filed: a report may sit here for weeks, and
    /// the card must say so rather than implying the freeze was recent.
    /// </summary>
    public required DateTimeOffset GatheredAtUtc { get; init; }

    /// <summary>
    /// How many times we have seen this same problem while this bundle was waiting. One card with
    /// `occurrences: 4` beats four cards, especially since an offline user cannot tell us anything in
    /// between (plan §5.1).
    /// </summary>
    public int Occurrences { get; init; } = 1;

    /// <summary>The most recent time we saw this problem, if it has recurred.</summary>
    public DateTimeOffset? LastOccurrenceUtc { get; init; }

    /// <summary>File names (not paths) of the artifacts sitting in this bundle's folder.</summary>
    public IReadOnlyList<string> Artifacts { get; init; } = Array.Empty<string>();

    /// <summary>How many times we have tried to file it.</summary>
    public int AttemptCount { get; init; }

    /// <summary>When we last tried, so backoff can be worked out after a restart.</summary>
    public DateTimeOffset? LastAttemptUtc { get; init; }

    /// <summary>Why the last attempt failed, for the Doctor's log and for support.</summary>
    public string? LastError { get; init; }

    /// <summary>The card we ended up creating or commenting on.</summary>
    public string? IssueId { get; init; }

    /// <summary>Bloom's version at the time, which may not be its version by the time this is filed.</summary>
    public string? BloomChannel { get; init; }

    /// <summary>Why we gathered: frozen, zombie, died-while-frozen, and so on.</summary>
    public string? Reason { get; init; }

    /// <summary>Options used for reading and writing these files. Indented so a human can read a queue.</summary>
    [JsonIgnore]
    public static System.Text.Json.JsonSerializerOptions JsonOptions { get; } =
        new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
}
