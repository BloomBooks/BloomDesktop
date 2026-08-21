using System.Text.Json;
using BloomFreezeDoctor.Gathering;

namespace BloomFreezeDoctor.Outbox;

/// <summary>One queued report on disk.</summary>
public sealed record QueuedBundle
{
    /// <summary>The bundle's own folder.</summary>
    public required string Directory { get; init; }

    /// <summary>Its metadata, as read from <c>meta.json</c>.</summary>
    public required BundleMetadata Metadata { get; init; }

    /// <summary>Full path to the report body.</summary>
    public string ReportPath => Path.Combine(Directory, ReportOutbox.ReportFileName);

    /// <summary>Full paths to the artifacts to attach.</summary>
    public IEnumerable<string> ArtifactPaths =>
        Metadata.Artifacts.Select(name => Path.Combine(Directory, name));
}

/// <summary>What happened when we tried to file a bundle.</summary>
public enum SubmitOutcome
{
    /// <summary>Filed. The bundle can be marked done.</summary>
    Filed,

    /// <summary>
    /// Could not reach the tracker. Entirely expected — for many of our users the network is down more
    /// than it is up — so this is a wait, not a failure.
    /// </summary>
    NetworkUnavailable,

    /// <summary>
    /// The tracker refused it in a way retrying will not fix. Stop, and say so loudly: an expired token
    /// would otherwise become an infinite retry loop.
    /// </summary>
    RejectedPermanently,
}

/// <summary>The result of one filing attempt.</summary>
public readonly record struct SubmitResult
{
    /// <summary>What happened.</summary>
    public required SubmitOutcome Outcome { get; init; }

    /// <summary>The card created or commented on, when we got that far.</summary>
    public string? IssueId { get; init; }

    /// <summary>What went wrong, for the metadata and the log.</summary>
    public string? Error { get; init; }
}

/// <summary>Files a report somewhere. Implemented by the YouTrack submitter, and faked in tests.</summary>
public interface IReportSubmitter
{
    /// <summary>Attempts to file one bundle.</summary>
    Task<SubmitResult> SubmitAsync(QueuedBundle bundle, CancellationToken cancellation);
}

/// <summary>
/// The queue of reports waiting to be filed, and the rules about what goes in it.
///
/// This exists because gathering and filing must be separate steps (plan §5.1). A freeze frequently
/// arrives *with* a dead network — BL-16697's own log shows DNS failing moments before the freeze — so a
/// design that files inline loses precisely the reports we most want. Everything here therefore assumes
/// the network is absent and the machine may be restarted at any moment.
/// </summary>
public sealed class ReportOutbox
{
    /// <summary>Name of the report body inside a bundle.</summary>
    public const string ReportFileName = "report.md";

    /// <summary>Name of the metadata file inside a bundle.</summary>
    public const string MetadataFileName = "meta.json";

    /// <summary>
    /// How long a bundle may sit unfiled before we give up on it. A month of a bad connection is
    /// plausible; a year of it means the report has lost its value and its disk space matters more.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>Most bundles to keep, oldest evicted first. A cap on someone else's disk, so: modest.</summary>
    public const int MaxBundles = 20;

    /// <summary>Most reports to file from one machine per day, before we assume something is wrong with us.</summary>
    public const int MaxFilingsPerDay = 3;

    private readonly string _root;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>
    /// Creates an outbox rooted at a directory (defaulting to
    /// <c>%LOCALAPPDATA%\SIL\BloomFreezeDoctor\outbox</c>). The clock is injectable so retention and
    /// rate-limiting can be tested without waiting a day.
    /// </summary>
    public ReportOutbox(string? root = null, Func<DateTimeOffset>? clock = null)
    {
        _root =
            root
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SIL",
                "BloomFreezeDoctor",
                "outbox"
            );
        _now = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The outbox's folder, which is also what support asks a user to send.</summary>
    public string Root => _root;

    /// <summary>
    /// Adds a gathered report to the queue, or folds it into an existing pending bundle with the same
    /// fingerprint.
    ///
    /// Folding rather than adding is what stops an offline user with a recurring freeze producing
    /// twenty cards: the count goes up, the artifacts are not duplicated (they would be near-identical
    /// dumps of the same problem), and one good card gets filed when the network returns.
    /// </summary>
    public QueuedBundle Enqueue(
        GatheredReport report,
        string project,
        string? channel,
        string? reason
    )
    {
        Directory.CreateDirectory(_root);

        var existing = List()
            .FirstOrDefault(b =>
                b.Metadata.State == BundleState.Pending
                && b.Metadata.Fingerprint == report.Fingerprint
            );
        if (existing != null)
            return RecordAnotherOccurrence(existing);

        var state = report.MayFile ? BundleState.Pending : BundleState.NotForFiling;
        var now = _now();
        var name = $"{now:yyyyMMdd-HHmmss}-{report.Fingerprint}";

        // Build in a staging folder and rename into place, so a reader never sees a half-written
        // bundle. A rename within one volume is atomic; a copy is not.
        var staging = Path.Combine(_root, ".staging-" + name);
        var final = Path.Combine(_root, name);
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        File.WriteAllText(Path.Combine(staging, ReportFileName), report.Body);

        var artifactNames = new List<string>();
        foreach (var artifact in report.Artifacts)
        {
            try
            {
                var fileName = Path.GetFileName(artifact);
                File.Move(artifact, Path.Combine(staging, fileName));
                artifactNames.Add(fileName);
            }
            catch (Exception)
            {
                // An artifact we cannot move is one the report does without; the text matters more.
            }
        }

        var metadata = new BundleMetadata
        {
            Summary = report.Summary,
            Fingerprint = report.Fingerprint,
            Project = project,
            State = state,
            GatheredAtUtc = now,
            Artifacts = artifactNames,
            BloomChannel = channel,
            Reason = reason,
        };
        WriteMetadata(staging, metadata);

        Directory.Move(staging, final);
        Prune();
        return new QueuedBundle { Directory = final, Metadata = metadata };
    }

    /// <summary>Every bundle currently on disk, newest first. Never throws for one unreadable bundle.</summary>
    public List<QueuedBundle> List()
    {
        var bundles = new List<QueuedBundle>();
        if (!Directory.Exists(_root))
            return bundles;

        foreach (var directory in Directory.GetDirectories(_root))
        {
            // Skip staging folders: they are by definition incomplete.
            if (Path.GetFileName(directory).StartsWith(".staging-", StringComparison.Ordinal))
                continue;
            var metadata = TryReadMetadata(directory);
            if (metadata == null)
                continue;
            bundles.Add(new QueuedBundle { Directory = directory, Metadata = metadata });
        }
        return bundles.OrderByDescending(b => b.Metadata.GatheredAtUtc).ToList();
    }

    /// <summary>Bundles that are waiting to be filed.</summary>
    public List<QueuedBundle> Pending() =>
        List().Where(b => b.Metadata.State == BundleState.Pending).ToList();

    /// <summary>
    /// Tries to file everything pending, oldest first so the queue drains in the order things happened.
    ///
    /// Called on Doctor startup as well as periodically, and startup is the guarantee that matters: the
    /// most likely next event after a freeze is the user restarting Bloom, which starts the Doctor,
    /// which drains this queue.
    /// </summary>
    public async Task<int> DrainAsync(
        IReportSubmitter submitter,
        CancellationToken cancellation = default
    )
    {
        var filed = 0;
        var filedToday = CountFiledToday();

        foreach (var bundle in Pending().OrderBy(b => b.Metadata.GatheredAtUtc))
        {
            cancellation.ThrowIfCancellationRequested();

            if (filedToday >= MaxFilingsPerDay)
            {
                // Not an error, and not a reason to drop anything: the bundles stay pending and go out
                // tomorrow. A machine producing more than this is telling us something we will hear
                // from the first three reports anyway.
                break;
            }

            var result = await submitter.SubmitAsync(bundle, cancellation).ConfigureAwait(false);
            var metadata = bundle.Metadata with
            {
                AttemptCount = bundle.Metadata.AttemptCount + 1,
                LastAttemptUtc = _now(),
            };

            switch (result.Outcome)
            {
                case SubmitOutcome.Filed:
                    WriteMetadata(
                        bundle.Directory,
                        metadata with
                        {
                            State = BundleState.Filed,
                            IssueId = result.IssueId,
                            LastError = null,
                        }
                    );
                    filed++;
                    filedToday++;
                    break;

                case SubmitOutcome.RejectedPermanently:
                    WriteMetadata(
                        bundle.Directory,
                        metadata with
                        {
                            State = BundleState.FailedPermanently,
                            LastError = result.Error,
                        }
                    );
                    break;

                default:
                    // Still offline. Leave it pending and stop trying the rest: if this one could not
                    // reach the tracker, neither will the next, and each attempt costs a timeout.
                    WriteMetadata(bundle.Directory, metadata with { LastError = result.Error });
                    return filed;
            }
        }
        return filed;
    }

    /// <summary>
    /// True if this machine has already filed its daily allowance. Exposed so the window can explain
    /// why a report is waiting when the network is plainly fine.
    /// </summary>
    public bool DailyLimitReached() => CountFiledToday() >= MaxFilingsPerDay;

    private int CountFiledToday()
    {
        var since = _now() - TimeSpan.FromDays(1);
        return List()
            .Count(b =>
                b.Metadata.State == BundleState.Filed
                && b.Metadata.LastAttemptUtc.HasValue
                && b.Metadata.LastAttemptUtc.Value > since
            );
    }

    private QueuedBundle RecordAnotherOccurrence(QueuedBundle bundle)
    {
        var metadata = bundle.Metadata with
        {
            Occurrences = bundle.Metadata.Occurrences + 1,
            LastOccurrenceUtc = _now(),
        };
        WriteMetadata(bundle.Directory, metadata);
        return bundle with { Metadata = metadata };
    }

    /// <summary>
    /// Drops bundles that are too old or too many. Runs on every enqueue, so the queue cannot grow
    /// without bound on a machine that never gets online.
    /// </summary>
    private void Prune()
    {
        var all = List();
        var cutoff = _now() - MaxAge;

        foreach (var bundle in all.Where(b => b.Metadata.GatheredAtUtc < cutoff))
            TryDelete(bundle.Directory);

        // Keep the newest MaxBundles of whatever survived the age check.
        var surviving = List();
        foreach (var bundle in surviving.Skip(MaxBundles))
            TryDelete(bundle.Directory);
    }

    private static BundleMetadata? TryReadMetadata(string directory)
    {
        try
        {
            var path = Path.Combine(directory, MetadataFileName);
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<BundleMetadata>(
                File.ReadAllText(path),
                BundleMetadata.JsonOptions
            );
        }
        catch (Exception)
        {
            // A corrupt bundle is not worth crashing over; it will be pruned by age eventually.
            return null;
        }
    }

    /// <summary>
    /// Writes metadata by temp-then-rename, because this file is rewritten on every attempt and a
    /// power cut mid-write would otherwise lose the whole bundle rather than one attempt's record.
    /// </summary>
    private static void WriteMetadata(string directory, BundleMetadata metadata)
    {
        var path = Path.Combine(directory, MetadataFileName);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(metadata, BundleMetadata.JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception)
        {
            // Locked by an antivirus scan, most likely; it will go on the next pass.
        }
    }
}
