using System.Text.Json.Serialization;

namespace BloomFreezeDoctor.Outbox;

/// <summary>Where a queued report has got to.</summary>
public enum BundleState
{
    /// <summary>Waiting to be filed. The normal state, possibly for weeks on a bad connection.</summary>
    Pending,

    /// <summary>
    /// Handed to the tracker, with the answer still to come. A visible state rather than an internal
    /// detail, because it is what lets a gather happening at the same moment tell "waiting to be sent"
    /// from "being sent right now" — and those need opposite treatment. Merging into a bundle that is
    /// already going out would either be lost or overwrite the card it came back with; a bundle that is
    /// merely waiting can be merged into freely.
    ///
    /// Not a terminal state: it becomes <see cref="Filed"/>, or falls back to <see cref="Pending"/> if
    /// the upload failed, or <see cref="FailedPermanently"/> if the tracker refused it outright.
    /// </summary>
    Uploading,

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

    /// <summary>
    /// True when a person asked for this report by pressing a button, rather than the Doctor deciding on
    /// its own that something was wrong.
    ///
    /// It exempts the report from the daily cap. The cap exists to stop a misbehaving machine filing
    /// dozens of cards nobody asked for; somebody deliberately pressing "Report now" is the opposite of
    /// that, and being told "not today, you have had your three" would be absurd — particularly since
    /// pressing it is also how anyone checks that filing works at all.
    /// </summary>
    public bool UserRequested { get; init; }

    /// <summary>
    /// True once this bundle has been sent and it opened a **new card**, as opposed to adding a comment to
    /// one that already existed.
    ///
    /// Only new cards count against the daily cap. A comment is one small POST with no attachments - the
    /// recurrence note is deliberately a few lines - so counting them would let three "it happened again"
    /// notes silence a machine for the rest of the day about problems nobody had heard of yet, which is
    /// exactly backwards.
    /// </summary>
    public bool CreatedNewCard { get; init; }

    /// <summary>
    /// Which Bloom this was about. Not for identifying the *problem* — a process id means nothing on
    /// anyone else's machine, which is why the fingerprint exists — but for recognising that two reports
    /// are about one Bloom's last few seconds and belong on one card.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// One line per *further, different* problem the same Bloom had after this report was gathered — the
    /// freeze that then became a death, the crash whose exit was examined afterwards.
    ///
    /// Deliberately not counted in <see cref="Occurrences"/>, which means "this same problem, again":
    /// adding a follow-on there made the card say "this problem happened 2 times", which is a plain
    /// misreading of one Bloom failing once and then dying of it.
    /// </summary>
    public IReadOnlyList<string> FollowOnNotes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The fingerprint of a sibling bundle whose card this one belongs on, set when this report *would*
    /// have been folded into that sibling but could not be — because the sibling was being uploaded at
    /// that exact moment.
    ///
    /// This is how "wait and comment" is expressed without holding anything up. The sibling's upload is a
    /// network round trip and nothing should block on it, so the report goes into a bundle of its own with
    /// a note of where it belongs; when it is drained, the sibling has a card and this becomes a comment
    /// on it. Null for a bundle that is nobody's follow-on, which is nearly all of them.
    /// </summary>
    public string? CommentOnFingerprint { get; init; }

    /// <summary>
    /// The card <see cref="CommentOnFingerprint"/> resolved to, filled in at the moment of sending. Known
    /// locally, so it needs no search — and a search could not find it anyway, since this report's own
    /// fingerprint differs from the one the card was opened under.
    /// </summary>
    public string? CommentOnIssueId { get; init; }

    /// <summary>
    /// The few facts that differ between occurrences of this problem, used as the comment when a card for
    /// this fingerprint already exists rather than posting the whole report again.
    /// </summary>
    public string? RecurrenceNote { get; init; }

    /// <summary>Options used for reading and writing these files. Indented so a human can read a queue.</summary>
    [JsonIgnore]
    public static System.Text.Json.JsonSerializerOptions JsonOptions { get; } =
        new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
}
