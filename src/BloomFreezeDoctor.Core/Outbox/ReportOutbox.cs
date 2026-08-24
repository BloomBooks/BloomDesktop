using System.Diagnostics;
using System.Text.Json;
using BloomFreezeDoctor.Gathering;
using SIL.IO;

namespace BloomFreezeDoctor.Outbox;

/// <summary>
/// What a drain did. **The two fields are not interchangeable, and conflating them was a real bug:**
/// `Filed: 0` on its own is ambiguous between "there was nothing to send" and "somebody else is sending
/// it", and the caller that asks a user-facing question - "Report now" - needs to tell those apart, or it
/// announces failure for a report that is about to be filed perfectly well.
/// </summary>
/// <param name="Filed">How many bundles were actually accepted by the tracker.</param>
/// <param name="GatedOut">
/// True if another drain held the gate and we left the queue to it, having sent nothing.
/// </param>
public readonly record struct DrainOutcome(int Filed, bool GatedOut);

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
///
/// **Every file operation in the Doctor goes through SIL.IO's RobustFile / RobustIO, with no exemption.**
/// That was decided deliberately when the Doctor moved into this repository (BL-16719): the tempting
/// argument was that its writes are only diagnostics, so a transient failure costs nothing much. It is
/// wrong, and this class is the proof — the rename that publishes a gathered report into this queue was
/// failing about one run in three with "access is denied", because Windows had not finished with the files
/// we had written milliseconds earlier, and each failure discarded a report at the exact moment a user had
/// just sat through a freeze. A tool whose entire purpose is to capture evidence that is otherwise lost
/// has *less* room to be careless with the disk than Bloom does, not more.
///
/// The only carve-outs are the three documented `robustfile-hook: allow FileStream` sites, where the
/// sharing flags are the requirement rather than an accident — reading a log another process holds open,
/// and the exclusive lock that serialises draining.
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
    /// <param name="drainGateWait">
    /// How long to wait for another drainer's gate before leaving the queue to it. Injectable so the test
    /// that proves a second drain is refused does not have to wait out the real timeout.
    /// </param>
    public ReportOutbox(
        string? root = null,
        Func<DateTimeOffset>? clock = null,
        TimeSpan? drainGateWait = null
    )
    {
        _drainGateWait = drainGateWait ?? DefaultDrainGateWait;
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
            RobustIO.DeleteDirectoryAndContents(staging);
        Directory.CreateDirectory(staging);

        RobustFile.WriteAllText(Path.Combine(staging, ReportFileName), report.Body);

        var artifactNames = new List<string>();
        foreach (var artifact in report.Artifacts)
        {
            try
            {
                var fileName = Path.GetFileName(artifact);
                RobustFile.Move(artifact, Path.Combine(staging, fileName));
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

        // RobustIO rather than the plain framework rename, because this exact line failed about one run in three:
        //
        //     IOException: Access to the path '...\.staging-20260919-100000-recent' is denied.
        //
        // Nothing is wrong with the code's logic. On Windows a directory whose files were written
        // milliseconds ago is quite likely to still be held by something else - a virus scanner or the
        // search indexer following our own writes - and the rename simply loses that race. RobustIO
        // retries for a short while, which is all this needs.
        //
        // Worth being clear about what was at stake, since this is the one place in the Doctor where a
        // transient failure destroys evidence rather than merely inconveniencing somebody: this rename IS
        // the publish step. Before it, the bundle is a hidden staging directory nothing will ever look in;
        // after it, the bundle is queued and will be filed. Losing the race meant throwing away a gathered
        // report at the exact moment a user had just sat through a freeze - and a real machine, with real
        // antivirus, is more exposed to it than a temp folder on a developer's box, not less.
        RobustIO.MoveDirectory(staging, final);
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
    /// <summary>
    /// The file whose exclusive lock serves as the cross-process gate for draining. It lives inside the
    /// outbox, which is the entire point: **the gate has to be scoped exactly like the thing it protects.**
    ///
    /// This was a named `Local\` semaphore, and that was wrong in a way worth recording. `Local\` names are
    /// per Windows LOGON SESSION, while the outbox lives under LOCALAPPDATA, which is per USER - so the
    /// same user in two sessions (fast user switching, or a second desktop over RDP) shared one queue while
    /// holding two different gates, and could drain it twice at once. A `Global\` name would have fixed the
    /// scope and broken something else: creating a `Global\` object needs SeCreateGlobalPrivilege, which a
    /// standard user does not necessarily have, and the failure path here is "carry on ungated".
    ///
    /// A lock file in the outbox directory has neither problem. Same outbox means the same file and
    /// therefore the same gate; a different outbox - another user, or a test with its own root - gets its
    /// own gate and no spurious contention. It needs no privileges, and Windows releases the lock when the
    /// handle closes, including when the process dies without closing it.
    /// </summary>
    private const string DrainLockFileName = ".drain.lock";

    /// <summary>
    /// How long to keep trying for the gate before giving up and leaving the queue to whoever holds it.
    /// The bundles are not lost either way: the holder is already sending them.
    /// </summary>
    private static readonly TimeSpan DefaultDrainGateWait = TimeSpan.FromSeconds(20);

    private readonly TimeSpan _drainGateWait;

    public async Task<DrainOutcome> DrainAsync(
        IReportSubmitter submitter,
        CancellationToken cancellation = default
    )
    {
        // A CROSS-PROCESS gate, because in-process serialisation is not enough: `--drain` is handled
        // before the singleton mutex - deliberately, so support can drain the queue whether or not a
        // Doctor is running - and it calls straight in here. Two processes draining one queue would each
        // list the same pending bundles and each walk the non-atomic search-then-create flow: duplicate
        // cards, or a combined total past the three-a-day cap that exists to stop a machine in a bad
        // state spamming the tracker.
        // NOTE the two different reasons `gate` can end up null, because conflating them made this whole
        // gate a no-op once already: a null RETURN means somebody else holds it and we must not drain,
        // while a THROW means the gating mechanism itself is unavailable and we drain anyway.
        FileStream? gate;
        try
        {
            gate = await AcquireDrainGateAsync(cancellation).ConfigureAwait(false);
            if (gate == null)
            {
                // Somebody else is draining, so those bundles are theirs to send. Saying so, rather than
                // just reporting zero filed, is what lets "Report now" tell the user their report is
                // QUEUED instead of implying it failed.
                return new DrainOutcome(Filed: 0, GatedOut: true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Could not even try for the gate (an unwritable directory, say). Carry on ungated rather than
            // never draining: a report that never reaches the tracker is worse than a rare duplicate, and
            // this is the path by which reports actually get sent.
            gate = null;
        }

        try
        {
            var filed = await DrainInnerAsync(submitter, cancellation).ConfigureAwait(false);
            return new DrainOutcome(filed, GatedOut: false);
        }
        finally
        {
            gate?.Dispose();
        }
    }

    /// <summary>
    /// Takes the drain gate, or returns null if somebody else is holding it after
    /// <see cref="DrainGateWait"/>. Null means "not ours to drain", not "nothing to drain".
    /// </summary>
    private async Task<FileStream?> AcquireDrainGateAsync(CancellationToken cancellation)
    {
        // Only directories are ever treated as bundles (see Pending), so a lock FILE sitting here cannot
        // be mistaken for one.
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, DrainLockFileName);
        // Real elapsed time, deliberately NOT the injectable _now clock. That clock is frozen in tests so
        // that retention and the daily cap can be exercised without waiting a day - and a frozen clock
        // would make this deadline unreachable and spin here for ever.
        var waited = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                // robustfile-hook: allow FileStream
                // FileShare.None is not incidental here — it IS the lock, and the exclusivity is the
                // entire mechanism. A robust wrapper that retried past a sharing violation would defeat
                // the gate rather than harden it, since "somebody else has it" is the answer we want.
                return new FileStream(
                    path,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
            }
            catch (IOException)
            {
                // Held by somebody else. Keep trying until the deadline.
                if (waited.Elapsed >= _drainGateWait)
                    return null;
                await Task.Delay(250, cancellation).ConfigureAwait(false);
            }
        }
    }

    private async Task<int> DrainInnerAsync(
        IReportSubmitter submitter,
        CancellationToken cancellation
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
            if (!RobustFile.Exists(path))
                return null;
            return JsonSerializer.Deserialize<BundleMetadata>(
                RobustFile.ReadAllText(path),
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
        RobustFile.WriteAllText(
            temp,
            JsonSerializer.Serialize(metadata, BundleMetadata.JsonOptions)
        );
        // Replace-or-Move, not an overwriting Move: RobustFile.Move has no overwrite overload, and Replace
        // is the better primitive when the target exists anyway, since it swaps the file in one step
        // rather than leaving a moment where the metadata file is absent.
        if (RobustFile.Exists(path))
            RobustFile.Replace(temp, path, null);
        else
            RobustFile.Move(temp, path);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            RobustIO.DeleteDirectoryAndContents(directory);
        }
        catch (Exception)
        {
            // Locked by an antivirus scan, most likely; it will go on the next pass.
        }
    }
}
