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

    /// <summary>
    /// True if this opened a **new** card; false if it added a comment to one that already existed. The
    /// daily cap counts only the former — see <see cref="ReportOutbox.MaxFilingsPerDay"/> — and the
    /// submitter is the only thing that knows which happened, since it depends on what the tracker
    /// already had.
    /// </summary>
    public bool CreatedNewCard { get; init; }

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
/// The tempting argument for exempting it — these writes are only diagnostics, so a transient failure costs
/// nothing much — is wrong, and this class is where it shows: a plain rename to publish a gathered report
/// into this queue fails about one run in three with "access is denied", because Windows has not finished
/// with the files written milliseconds earlier, and each failure discards a report at the exact moment a
/// user has just sat through a freeze. A tool whose entire purpose is to capture evidence that is otherwise
/// lost has *less* room to be careless with the disk than Bloom does, not more.
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
    /// Names the body of a report folded in as a later instalment about the same Bloom. See
    /// <see cref="FoldInAFollowOnProblem"/>.
    /// </summary>
    public const string FollowOnReportPrefix = "follow-on-report-";

    /// <summary>Guards a single bundle's metadata while it is read-modify-written. See <see cref="WhileHoldingTheMetadata"/>.</summary>
    private const string MetadataLockFileName = ".meta.lock";

    /// <summary>
    /// How long to wait for another thread or process to finish its own read-modify-write of one bundle's
    /// metadata. Deliberately tiny: the thing being waited for is a few file operations, never a network
    /// call, so anything longer than this means something is wrong rather than merely busy.
    /// </summary>
    private static readonly TimeSpan MetadataLockWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How long a bundle may sit marked as being uploaded before we conclude the send that marked it is
    /// never coming back. See <see cref="ReclaimAbandonedUploads"/>.
    /// </summary>
    public static readonly TimeSpan AbandonedUploadTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How recently the previous report about a Bloom must have been gathered for a new one to be treated
    /// as a further instalment of the same trouble rather than as separate news.
    ///
    /// A bound is needed in both directions. The instalments this exists for arrive seconds apart — the UI
    /// freezes, then the process dies — so it does not need to be long; and a process id is only unique
    /// while its process lives, so without a bound a queued report about a Bloom that died this morning
    /// could swallow this afternoon's report about whatever new process Windows handed that number to.
    /// Measured from the *last* thing that happened to the bundle, so a chain of instalments keeps
    /// extending rather than being cut off ten minutes after the first one.
    /// </summary>
    public static readonly TimeSpan FollowOnWindow = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a bundle may sit unfiled before we give up on it. A month of a bad connection is
    /// plausible; a year of it means the report has lost its value and its disk space matters more.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>Most bundles to keep, oldest evicted first. A cap on someone else's disk, so: modest.</summary>
    public const int MaxBundles = 20;

    /// <summary>
    /// Most **new cards** one machine may open per day on its own initiative, before we assume the fault is
    /// ours rather than Bloom's.
    ///
    /// Two things are deliberately outside it, because the cap is about unsolicited volume and neither of
    /// them is that:
    ///
    /// - **A report somebody asked for by pressing a button.** Refusing that would be absurd, and pressing
    ///   it is also how anyone checks that filing works at all.
    /// - **A comment on a card that already exists.** It is a few lines of text with no attachments, and
    ///   counting it meant three "it happened again" notes could silence a machine for the rest of the day
    ///   about problems nobody had heard of yet — the exact opposite of what the cap is for.
    ///
    /// So the number is small on purpose: it bounds the only thing that is actually expensive, which is
    /// opening cards nobody asked for.
    /// </summary>
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
    /// Adds a gathered report to the queue, or folds it into an existing pending bundle — for either of
    /// two reasons.
    ///
    /// **The same problem again** (matching fingerprints) is what stops an offline user with a recurring
    /// freeze producing twenty cards: the count goes up, the artifacts are not duplicated (they would be
    /// near-identical dumps of the same problem), and one good card gets filed when the network returns.
    ///
    /// **A different problem with the same Bloom** folds in too, and that is not a variation on the
    /// first — see <see cref="FoldInAFollowOnProblem"/> for why one process gets one card.
    /// </summary>
    public QueuedBundle Enqueue(
        GatheredReport report,
        string project,
        string? channel,
        string? reason,
        int? processId = null,
        bool userRequested = false
    )
    {
        Directory.CreateDirectory(_root);

        // Uploading counts as a candidate, not just Pending. It is the case that matters: the merge will
        // be refused (nothing may be written to a bundle mid-send) and we need to have identified the
        // bundle anyway, so that this report can be told whose card it belongs on and wait for it.
        var existing = List()
            .FirstOrDefault(b =>
                IsAwaitingOrDuringSend(b.Metadata.State)
                && b.Metadata.Fingerprint == report.Fingerprint
            );
        // Where this report belongs if it cannot be merged after all. Same fingerprint needs no note - the
        // submitter finds that card by searching for the fingerprint - but a follow-on's card can only be
        // found by way of the sibling that owns it.
        string? belongsOnTheCardFor = null;

        if (existing != null)
        {
            // Null means it stopped being mergeable while we were deciding - most likely it went out, or
            // is going out this second - so fall through and make a bundle of our own, which the submitter
            // will turn into a comment on the card once that card exists. See StillMergeable.
            var merged = RecordAnotherOccurrence(existing);
            if (merged != null)
                return merged;
            belongsOnTheCardFor = existing.Metadata.Fingerprint;
        }

        // A different problem, but the same Bloom, moments ago. See FoldInAFollowOnProblem.
        // List() is newest-first, so Last is the EARLIEST such bundle - the original report, and the one
        // whose card everything about this Bloom should end up on.
        if (processId != null)
        {
            var sameBloom = List()
                .LastOrDefault(b =>
                    IsAwaitingOrDuringSend(b.Metadata.State)
                    && b.Metadata.ProcessId == processId
                    && _now() - (b.Metadata.LastOccurrenceUtc ?? b.Metadata.GatheredAtUtc)
                        <= FollowOnWindow
                );
            if (sameBloom != null)
            {
                var folded = FoldInAFollowOnProblem(sameBloom, report, reason);
                if (folded != null)
                    return folded;
                // It was the right bundle to join and we could not join it, so remember whose card this
                // belongs on. Nothing else could work it out later: the fingerprints differ, which is what
                // made this a follow-on rather than a recurrence in the first place.
                belongsOnTheCardFor = sameBloom.Metadata.Fingerprint;
            }
        }

        var state = report.MayFile ? BundleState.Pending : BundleState.NotForFiling;
        var now = _now();
        var name = $"{now:yyyyMMdd-HHmmss}-{report.Fingerprint}";

        // Two bundles really can want this name: the timestamp is only to the second, and reports of the
        // same problem that do NOT merge are a normal case - a developer run, where nothing merges at all,
        // or one arriving just after the bundle it would have joined was filed. The name is only a name,
        // so make it unique rather than letting the publish rename below fail, which throws out of
        // Enqueue and discards the report.
        var final = Path.Combine(_root, name);
        for (var attempt = 2; Directory.Exists(final); attempt++)
        {
            name = $"{now:yyyyMMdd-HHmmss}-{report.Fingerprint}-{attempt}";
            final = Path.Combine(_root, name);
        }

        // Build in a staging folder and rename into place, so a reader never sees a half-written
        // bundle. A rename within one volume is atomic; a copy is not.
        var staging = Path.Combine(_root, ".staging-" + name);
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
            ProcessId = processId,
            UserRequested = userRequested,
            CommentOnFingerprint = belongsOnTheCardFor,
            RecurrenceNote = report.RecurrenceNote,
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
        ReclaimAbandonedUploads();
        var filed = 0;
        var filedToday = CountFiledToday();

        foreach (var bundle in Pending().OrderBy(b => b.Metadata.GatheredAtUtc))
        {
            cancellation.ThrowIfCancellationRequested();

            if (filedToday >= MaxFilingsPerDay && SubjectToTheDailyCap(bundle))
            {
                // Not an error, and not a reason to drop anything: it stays pending and goes out tomorrow.
                // A machine opening more cards than this on its own is telling us something we will hear
                // from the first three anyway.
                //
                // `continue`, not `break`: the cap now applies per bundle rather than to the whole pass,
                // so a capped report must not stop the queue behind it — the next one may be a comment or
                // something a person actually asked for, and those are exempt.
                continue;
            }

            // A bundle waiting on a sibling's card is skipped until that card exists. See
            // BundleMetadata.CommentOnFingerprint.
            if (!ReadyToSend(bundle))
                continue;

            // Claim it before the network call, under the brief metadata lock, so a gather arriving
            // mid-upload can see that merging into this bundle is no longer safe and take itself
            // elsewhere. Marking it is the point; the upload below happens with no lock held at all.
            var belongsOn = CardThisBundleBelongsOn(bundle);
            var claimed = WhileHoldingTheMetadata(
                bundle.Directory,
                current =>
                    current.State != BundleState.Pending
                        ? null // somebody else got to it first
                        : current with
                        {
                            State = BundleState.Uploading,
                            AttemptCount = current.AttemptCount + 1,
                            LastAttemptUtc = _now(),
                            CommentOnIssueId = belongsOn ?? current.CommentOnIssueId,
                        }
            );
            if (claimed == null)
                continue;
            var claimedBundle = bundle with { Metadata = claimed };

            var result = await submitter
                .SubmitAsync(claimedBundle, cancellation)
                .ConfigureAwait(false);
            var metadata = claimed;

            switch (result.Outcome)
            {
                case SubmitOutcome.Filed:
                    WriteMetadata(
                        bundle.Directory,
                        metadata with
                        {
                            State = BundleState.Filed,
                            IssueId = result.IssueId,
                            CreatedNewCard = result.CreatedNewCard,
                            LastError = null,
                        }
                    );
                    filed++;
                    // Only an unsolicited new card spends any of the daily allowance.
                    if (result.CreatedNewCard && !metadata.UserRequested)
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
                    // Still offline. Put it back to pending - it is no longer being uploaded - and stop
                    // trying the rest: if this one could not reach the tracker, neither will the next, and
                    // each attempt costs a timeout.
                    WriteMetadata(
                        bundle.Directory,
                        metadata with
                        {
                            State = BundleState.Pending,
                            LastError = result.Error,
                        }
                    );
                    return filed;
            }
        }
        return filed;
    }

    /// <summary>
    /// Puts back to Pending any bundle left marked <see cref="BundleState.Uploading"/> by a send that
    /// never finished — the Doctor was killed mid-upload, or the machine went down.
    ///
    /// **Without this, marking a bundle before the network call would be a way to lose reports rather than
    /// a way to protect them.** Uploading is not Pending, and the drain only ever looks at Pending, so an
    /// abandoned mark would make that bundle invisible to every future drain: never retried, never filed,
    /// and not even reported as failed. It would sit there until age eviction quietly deleted it.
    ///
    /// The timeout is generous because a real upload can be slow — a dump of a dozen megabytes over the
    /// sort of connection that accompanies a freeze — and reclaiming one that is genuinely still in flight
    /// would file it twice.
    /// </summary>
    private void ReclaimAbandonedUploads()
    {
        foreach (var bundle in List().Where(b => b.Metadata.State == BundleState.Uploading))
        {
            var startedAt = bundle.Metadata.LastAttemptUtc ?? bundle.Metadata.GatheredAtUtc;
            if (_now() - startedAt < AbandonedUploadTimeout)
                continue;
            WhileHoldingTheMetadata(
                bundle.Directory,
                current =>
                    current.State != BundleState.Uploading
                        ? null // it finished while we were looking at it
                        : current with
                        {
                            State = BundleState.Pending,
                            LastError =
                                "a previous send was interrupted before it finished; queued again",
                        }
            );
        }
    }

    /// <summary>
    /// Whether a bundle should be sent on this pass.
    ///
    /// Nearly always yes. The exception is a bundle that belongs on a sibling's card — it exists because
    /// the sibling was being uploaded at the moment this report wanted to join it (see
    /// <see cref="BundleMetadata.CommentOnFingerprint"/>) — and the sibling's card may not exist yet.
    ///
    /// Three outcomes, and the middle one is the reason this is not just a search:
    /// - the sibling is filed, so its card id is known locally: send, and the submitter comments there;
    /// - the sibling is still waiting or still uploading: skip this pass, and try again on the next one.
    ///   Creating a card now would produce exactly the second card this whole mechanism exists to avoid;
    /// - the sibling is gone from the queue entirely (evicted, or given up on): stop waiting for a card
    ///   that is never coming and let this one be filed on its own merits.
    /// </summary>
    /// <summary>
    /// True for a bundle that is on its way to the tracker but has not got there: waiting in the queue, or
    /// being sent this moment. Both are candidates for a new report to belong with; only the first can
    /// actually be written to.
    /// </summary>
    private static bool IsAwaitingOrDuringSend(BundleState state) =>
        state == BundleState.Pending || state == BundleState.Uploading;

    private bool ReadyToSend(QueuedBundle bundle)
    {
        var waitingFor = bundle.Metadata.CommentOnFingerprint;
        if (string.IsNullOrEmpty(waitingFor))
            return true;
        var sibling = List()
            .FirstOrDefault(b =>
                b.Metadata.Fingerprint == waitingFor && b.Directory != bundle.Directory
            );
        if (sibling == null)
            return true;
        return sibling.Metadata.State != BundleState.Pending
            && sibling.Metadata.State != BundleState.Uploading;
    }

    /// <summary>
    /// The card a bundle should comment on rather than opening a new one, when we already know it locally.
    /// Null when there is nothing to say — which is the normal case.
    /// </summary>
    public string? CardThisBundleBelongsOn(QueuedBundle bundle)
    {
        var waitingFor = bundle.Metadata.CommentOnFingerprint;
        if (string.IsNullOrEmpty(waitingFor))
            return null;
        return List()
            .FirstOrDefault(b =>
                b.Metadata.Fingerprint == waitingFor
                && b.Directory != bundle.Directory
                && !string.IsNullOrEmpty(b.Metadata.IssueId)
            )
            ?.Metadata.IssueId;
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
                // Only unsolicited NEW CARDS count. See MaxFilingsPerDay for why a comment and a
                // deliberately requested report are both outside the cap rather than merely cheap.
                && b.Metadata.CreatedNewCard
                && !b.Metadata.UserRequested
            );
    }

    /// <summary>
    /// Whether the daily cap has anything to say about this particular bundle.
    ///
    /// It applies only to a bundle that might open a new card nobody asked for. A report somebody
    /// requested is exempt outright; so is one we can already see will only add a comment, because a card
    /// for it exists locally.
    ///
    /// The remaining case is a bundle whose card may exist only on the tracker — filed from another machine,
    /// or by an install since replaced — which we cannot know without asking. Those are treated as
    /// potential new cards and capped. That errs towards the cap rather than towards a machine that talks
    /// too much, and the cost of being wrong is a comment that waits until tomorrow.
    /// </summary>
    private bool SubjectToTheDailyCap(QueuedBundle bundle)
    {
        if (bundle.Metadata.UserRequested)
            return false;
        return !WillOnlyAddAComment(bundle);
    }

    /// <summary>
    /// True when we can see locally that sending this bundle will add a comment to an existing card rather
    /// than open a new one: either it is waiting on a named sibling's card, or another bundle for the same
    /// problem has already been filed and its card is the one this will land on.
    /// </summary>
    private bool WillOnlyAddAComment(QueuedBundle bundle)
    {
        if (CardThisBundleBelongsOn(bundle) != null)
            return true;
        return List()
            .Any(b =>
                b.Directory != bundle.Directory
                && b.Metadata.Fingerprint == bundle.Metadata.Fingerprint
                && b.Metadata.State == BundleState.Filed
                && !string.IsNullOrEmpty(b.Metadata.IssueId)
            );
    }

    /// <summary>
    /// Folds a report about a **different** problem into the bundle for a report about the same Bloom.
    ///
    /// One Bloom's collapse is one story, and the Doctor tends to see it in instalments: the UI stops
    /// responding, so we report a freeze; then the process dies, and the exit examination finds no proof
    /// of an orderly shutdown and reports that. Two reports, different reasons, therefore different
    /// fingerprints - so nothing recognised them as related and two cards were filed about one event.
    /// That is exactly what happened during the first live test (AUT-20929 and AUT-20930), and it will
    /// happen again for a crashing Bloom now that the dump handshake works, which gathers once while
    /// Bloom is alive and once after it has gone.
    ///
    /// So the tie is the process, not the fingerprint. The fingerprint's job is recognising this problem
    /// on *other* machines and is unchanged; a process id means nothing anywhere else, which is precisely
    /// why it is the right key for "these two happened to the same Bloom, minutes apart".
    ///
    /// Unlike a plain recurrence, the follow-on report is **kept**: its body goes in as an attachment
    /// and its own artifacts come with it. A recurrence's evidence is a near-duplicate not worth
    /// re-attaching, but "and then it died" is new information, and for a crash it is the instalment
    /// carrying the minidump.
    /// </summary>
    private QueuedBundle? FoldInAFollowOnProblem(
        QueuedBundle bundle,
        GatheredReport report,
        string? reason
    )
    {
        // Checked here, before writing anything, and again before the metadata write below. See
        // StillMergeable.
        var fresh = StillMergeable(bundle);
        if (fresh == null)
            return null;

        var added = new List<string>();
        var artifacts = fresh.Metadata.Artifacts.ToList();
        var followOnNumber = artifacts.Count(n =>
            n.StartsWith(FollowOnReportPrefix, StringComparison.Ordinal)
        );
        var bodyName = $"{FollowOnReportPrefix}{followOnNumber + 1}.md";
        try
        {
            RobustFile.WriteAllText(Path.Combine(bundle.Directory, bodyName), report.Body);
            added.Add(bodyName);
            artifacts.Add(bodyName);
        }
        catch (Exception)
        {
            // The note below still records that it happened, which is the part that must not be lost.
        }

        foreach (var artifact in report.Artifacts)
        {
            try
            {
                var fileName = Path.GetFileName(artifact);
                // A follow-on can easily carry a file named like one already here (both collectors name
                // the log after the process). Renaming keeps both rather than overwriting the first.
                if (artifacts.Contains(fileName))
                    fileName =
                        Path.GetFileNameWithoutExtension(fileName)
                        + $"-followon{followOnNumber + 1}"
                        + Path.GetExtension(fileName);
                RobustFile.Move(artifact, Path.Combine(bundle.Directory, fileName));
                added.Add(fileName);
                artifacts.Add(fileName);
            }
            catch (Exception)
            {
                // As in Enqueue: an artifact we cannot move is one the report does without.
            }
        }

        // Writing the report body and moving a minidump takes long enough for a drain to start in the
        // meantime, so the decision is re-taken against the disk here, under the lock, immediately before
        // the only write that could undo one. If the bundle has gone out or is going out, the files just
        // written stay in its folder as harmless spare copies and the caller makes a bundle of its own.
        var note =
            $"{_now():yyyy-MM-dd HH:mm}Z — the same Bloom then had a further problem: "
            + $"{reason ?? "reason not recorded"}. {report.Summary}";
        var written = WhileHoldingTheMetadata(
            bundle.Directory,
            current =>
            {
                if (current.State != BundleState.Pending)
                    return null;
                var notes = current.FollowOnNotes.ToList();
                notes.Add(note);
                return current with
                {
                    // NOT Occurrences: see FollowOnNotes. LastOccurrenceUtc is still right, though - it is
                    // the last time we saw anything wrong with this Bloom, which is what a reader wants.
                    LastOccurrenceUtc = _now(),
                    // Union of what the disk says and what we added, so an artifact another thread
                    // recorded while we were writing is not dropped.
                    Artifacts = current.Artifacts.Concat(added).Distinct().ToList(),
                    FollowOnNotes = notes,
                };
            }
        );
        return written == null ? null : bundle with { Metadata = written };
    }

    private QueuedBundle? RecordAnotherOccurrence(QueuedBundle bundle)
    {
        var written = WhileHoldingTheMetadata(
            bundle.Directory,
            current =>
                current.State != BundleState.Pending
                    ? null // gone out, or going out right now; see WhileHoldingTheMetadata
                    : current with
                    {
                        Occurrences = current.Occurrences + 1,
                        LastOccurrenceUtc = _now(),
                    }
        );
        return written == null ? null : bundle with { Metadata = written };
    }

    /// <summary>
    /// Re-reads a bundle's metadata at the moment of merging into it, and refuses the merge if it has
    /// left the queue in the meantime.
    ///
    /// Merging is a read-then-write, and a drain can run between the two: the supervisor drains on a
    /// timer as well as after each gather, while an enqueue happens on whichever worker thread gathered.
    /// The gap is not small either — a follow-on merge writes a report body and moves a minidump before
    /// it writes the metadata. Writing our stale copy back over a bundle that had just been filed would
    /// restore `Pending` and drop its `IssueId`, and the queue would then file the same report a second
    /// time: the exact duplicate card that merging exists to prevent.
    ///
    /// Refusing here sends the caller back to making a bundle of its own, which is the right answer — the
    /// card it would have joined has already gone out.
    /// </summary>
    private QueuedBundle? StillMergeable(QueuedBundle bundle)
    {
        var current = TryReadMetadata(bundle.Directory);
        if (current == null || current.State != BundleState.Pending)
            return null;
        // The disk's copy, not ours: it carries any attempt count or occurrence another thread recorded
        // while we were deciding, and dropping those is a lost update in its own right.
        return bundle with { Metadata = current };
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
    /// <summary>
    /// Runs one read-modify-write of a bundle's metadata with nobody else doing the same thing.
    ///
    /// The lock is held for a handful of file operations and **never across the upload**, which is the
    /// whole design: the drain marks a bundle <see cref="BundleState.Uploading"/> under this lock, lets it
    /// go, does the network round trip, then takes it again to record the answer. A gather that arrives in
    /// between therefore waits microseconds, not the length of an upload on a bad connection — and what it
    /// then reads is the truth, because the marking and its own read cannot interleave.
    ///
    /// <paramref name="change"/> is given the metadata as it is on disk and returns what to write, or null
    /// to write nothing. Returns what was written, or null if it declined or the lock could not be had.
    /// A lock we cannot take means somebody else is mid-change, so declining is right: whatever we decided
    /// from a stale read is exactly what must not be written.
    /// </summary>
    private BundleMetadata? WhileHoldingTheMetadata(
        string directory,
        Func<BundleMetadata, BundleMetadata?> change
    )
    {
        var lockPath = Path.Combine(directory, MetadataLockFileName);
        // Real elapsed time, deliberately not the injectable clock, which tests freeze.
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                // robustfile-hook: allow FileStream - the exclusive share mode IS the lock, which is
                // precisely what RobustFile's retrying wrapper cannot express.
                using var held = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
                var current = TryReadMetadata(directory);
                if (current == null)
                    return null;
                var updated = change(current);
                if (updated == null)
                    return null;
                WriteMetadata(directory, updated);
                return updated;
            }
            catch (IOException)
            {
                // Somebody else holds it. They are doing a few file operations, so this is brief.
                if (waited.Elapsed >= MetadataLockWait)
                    return null;
                Thread.Sleep(15);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

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
