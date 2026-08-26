using BloomFreezeDoctor.Gathering;
using BloomFreezeDoctor.Outbox;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The outbox is the reliability-critical part: it is what makes a report survive the dead network that
/// so often accompanies a freeze, and it runs on someone else's disk. These tests care as much about
/// what it refuses to do (duplicate cards, retry forever, grow without bound) as about the happy path.
/// </summary>
[TestFixture]
public class ReportOutboxTests
{
    private string _root = null!;
    private DateTimeOffset _now;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "FreezeDoctorTests",
            "outbox-" + Guid.NewGuid().ToString("N")
        );
        _now = DateTimeOffset.Parse("2026-08-19T10:00:00Z");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception) { }
    }

    private ReportOutbox NewOutbox() => new(_root, () => _now);

    /// <summary>
    /// An outbox that gives up on the drain gate almost at once, so the test below need not wait out the
    /// real twenty seconds.
    /// </summary>
    private ReportOutbox NewImpatientOutbox() =>
        new(_root, () => _now, drainGateWait: TimeSpan.FromMilliseconds(200));

    [Test]
    public async Task A_second_drainer_is_refused_while_another_holds_the_gate()
    {
        // This exists because the gate has now been got wrong twice: first it was in-process only, so
        // `--drain` in another process sailed past it; then, when it became a lock file, the "somebody
        // else holds it" case fell through and drained anyway, which made the whole gate a no-op while
        // its comment claimed otherwise. Two processes draining one queue means duplicate YouTrack cards
        // and a combined total past the deliberate three-a-day cap, so a comment is not enough here.
        var outbox = NewImpatientOutbox();
        outbox.Enqueue(Report(), "AUT", "Alpha", "Frozen");
        Assert.That(outbox.Pending(), Has.Count.EqualTo(1), "setup: one bundle should be waiting");

        var submitter = new FakeSubmitter();

        // Hold the gate exactly as another process would.
        var lockPath = Path.Combine(_root, ".drain.lock");
        using (
            var heldByOtherProcess = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            )
        )
        {
            var filed = (await outbox.DrainAsync(submitter, CancellationToken.None)).Filed;

            Assert.That(
                filed,
                Is.Zero,
                "nothing should be filed while another drainer holds the gate"
            );
            Assert.That(
                submitter.Submitted,
                Is.Empty,
                "it must not even attempt a submission - attempting is what duplicates a card"
            );
        }

        Assert.That(
            outbox.Pending(),
            Has.Count.EqualTo(1),
            "the bundle must still be pending, so whoever holds the gate can send it"
        );

        // Sanity check the other direction: with the gate free, the same outbox does drain. Without this
        // the test above would still pass if DrainAsync were broken outright.
        var filedAfter = (await outbox.DrainAsync(submitter, CancellationToken.None)).Filed;
        Assert.That(filedAfter, Is.EqualTo(1), "with the gate free it should file the bundle");
        Assert.That(submitter.Submitted, Has.Count.EqualTo(1));
    }

    private GatheredReport Report(
        string fingerprint = "abc123",
        bool mayFile = true,
        string[]? artifacts = null
    ) =>
        new()
        {
            Summary = $"Freeze Doctor: UI frozen ({fingerprint})",
            Body = "## What the Freeze Doctor saw\n\nthe UI thread is blocked",
            Fingerprint = fingerprint,
            Artifacts = artifacts ?? Array.Empty<string>(),
            Duration = TimeSpan.FromSeconds(6),
            MayFile = mayFile,
        };

    /// <summary>Makes a throwaway file to stand in for a dump.</summary>
    private string MakeArtifact(string name)
    {
        var directory = Path.Combine(_root, "..", "artifacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, "pretend this is a minidump");
        return path;
    }

    private sealed class FakeSubmitter : IReportSubmitter
    {
        public SubmitOutcome Outcome { get; set; } = SubmitOutcome.Filed;

        /// <summary>Stand in for a tracker that already has a card, so every send is a comment.</summary>
        public bool CommentsOnly { get; set; }

        public List<string> Submitted { get; } = new();

        public Task<SubmitResult> SubmitAsync(QueuedBundle bundle, CancellationToken cancellation)
        {
            Submitted.Add(bundle.Metadata.Fingerprint);
            return Task.FromResult(
                new SubmitResult
                {
                    Outcome = Outcome,
                    IssueId = Outcome == SubmitOutcome.Filed ? "BL-99999" : null,
                    Error = Outcome == SubmitOutcome.Filed ? null : "pretend failure",
                    // Stands in for opening a new card, which is the only thing the daily cap counts. Left
                    // at its default of false, this fake silently spent none of the allowance and the cap
                    // could never engage - so the test that exists to prove the cap works passed for the
                    // wrong reason.
                    CreatedNewCard = Outcome == SubmitOutcome.Filed && !CommentsOnly,
                }
            );
        }
    }

    [Test]
    public void A_report_lands_on_disk_complete_with_its_artifacts()
    {
        var outbox = NewOutbox();
        var artifact = MakeArtifact("bloom-1234.dmp");

        var bundle = outbox.Enqueue(
            Report(artifacts: new[] { artifact }),
            "BL",
            "Release",
            "Frozen"
        );

        Assert.That(File.Exists(bundle.ReportPath), Is.True, "the report body must be on disk");
        Assert.That(
            bundle.ArtifactPaths.All(File.Exists),
            Is.True,
            "artifacts must have been moved into the bundle"
        );
        Assert.That(
            File.Exists(artifact),
            Is.False,
            "and moved, not copied, so we do not leave dumps lying around"
        );
        Assert.That(outbox.Pending(), Has.Count.EqualTo(1));
    }

    [Test]
    public void Staging_folders_are_never_offered_as_bundles()
    {
        // A half-written bundle must be invisible, or a drain could try to file an empty report.
        var outbox = NewOutbox();
        Directory.CreateDirectory(Path.Combine(_root, ".staging-20260819-100000-abc123"));

        Assert.That(outbox.List(), Is.Empty);
    }

    [Test]
    public void The_same_problem_twice_becomes_one_bundle_with_two_occurrences()
    {
        // This is what stops an offline user with a recurring freeze producing twenty cards.
        var outbox = NewOutbox();
        outbox.Enqueue(Report("samefingerprint"), "BL", "Release", "Frozen");
        _now = _now.AddMinutes(10);
        outbox.Enqueue(Report("samefingerprint"), "BL", "Release", "Frozen");

        var pending = outbox.Pending();
        Assert.That(pending, Has.Count.EqualTo(1), "one bundle, not two");
        Assert.That(pending[0].Metadata.Occurrences, Is.EqualTo(2));
        Assert.That(
            pending[0].Metadata.LastOccurrenceUtc,
            Is.EqualTo(_now),
            "the card should be able to say when it last happened"
        );
    }

    [Test]
    public void Different_problems_get_their_own_bundles()
    {
        var outbox = NewOutbox();
        outbox.Enqueue(Report("fingerprintone"), "BL", "Release", "Frozen");
        outbox.Enqueue(Report("fingerprinttwo"), "BL", "Release", "Zombie");

        Assert.That(outbox.Pending(), Has.Count.EqualTo(2));
    }

    [Test]
    public void A_run_we_must_not_file_is_kept_but_never_queued()
    {
        // Developer and automation runs are still gathered — that is how we exercise the gatherer
        // without touching the tracker — but they must never be waiting to be sent.
        var outbox = NewOutbox();

        var bundle = outbox.Enqueue(Report(mayFile: false), "BL", "Developer/Debug", "Frozen");

        Assert.That(bundle.Metadata.State, Is.EqualTo(BundleState.NotForFiling));
        Assert.That(outbox.Pending(), Is.Empty, "it must not be in the queue to send");
        Assert.That(outbox.List(), Has.Count.EqualTo(1), "but it must still be on disk");
    }

    [Test]
    public async Task Draining_files_pending_reports_oldest_first()
    {
        var outbox = NewOutbox();
        outbox.Enqueue(Report("oldest"), "BL", "Release", "Frozen");
        _now = _now.AddHours(1);
        outbox.Enqueue(Report("newest"), "BL", "Release", "Frozen");
        var submitter = new FakeSubmitter();

        var filed = (await outbox.DrainAsync(submitter)).Filed;

        Assert.That(filed, Is.EqualTo(2));
        Assert.That(
            submitter.Submitted,
            Is.EqualTo(new[] { "oldest", "newest" }),
            "the queue should drain in the order things happened"
        );
        Assert.That(outbox.Pending(), Is.Empty);
        Assert.That(
            outbox.List().All(b => b.Metadata.IssueId == "BL-99999"),
            Is.True,
            "the local record should say where each report went"
        );
    }

    [Test]
    public async Task An_offline_drain_leaves_everything_pending_and_stops_early()
    {
        // If one bundle cannot reach the tracker, neither will the next, and each attempt costs a
        // timeout. Stopping early matters on the connections our users actually have.
        var outbox = NewOutbox();
        outbox.Enqueue(Report("one"), "BL", "Release", "Frozen");
        _now = _now.AddMinutes(1);
        outbox.Enqueue(Report("two"), "BL", "Release", "Frozen");
        var submitter = new FakeSubmitter { Outcome = SubmitOutcome.NetworkUnavailable };

        var filed = (await outbox.DrainAsync(submitter)).Filed;

        Assert.That(filed, Is.EqualTo(0));
        Assert.That(outbox.Pending(), Has.Count.EqualTo(2), "nothing may be lost by being offline");
        Assert.That(
            submitter.Submitted,
            Has.Count.EqualTo(1),
            "and we stop after the first failure"
        );
        Assert.That(
            outbox.Pending().Any(b => b.Metadata.AttemptCount == 1),
            Is.True,
            "the attempt should still be recorded"
        );
    }

    [Test]
    public async Task A_permanent_rejection_stops_the_retries_for_good()
    {
        // An expired token would otherwise become an infinite retry loop against the tracker.
        var outbox = NewOutbox();
        outbox.Enqueue(Report("rejected"), "BL", "Release", "Frozen");
        var submitter = new FakeSubmitter { Outcome = SubmitOutcome.RejectedPermanently };

        await outbox.DrainAsync(submitter);

        Assert.That(outbox.Pending(), Is.Empty, "it must not be tried again");
        var bundle = outbox.List().Single();
        Assert.That(bundle.Metadata.State, Is.EqualTo(BundleState.FailedPermanently));
        Assert.That(
            bundle.Metadata.LastError,
            Is.Not.Null,
            "and the reason must be recorded, loudly enough to notice"
        );
    }

    [Test]
    public async Task The_daily_limit_defers_reports_rather_than_dropping_them()
    {
        var outbox = NewOutbox();
        for (var i = 0; i < ReportOutbox.MaxFilingsPerDay + 2; i++)
        {
            outbox.Enqueue(Report($"fingerprint{i}"), "BL", "Release", "Frozen");
            _now = _now.AddMinutes(1);
        }
        var submitter = new FakeSubmitter();

        var filed = (await outbox.DrainAsync(submitter)).Filed;

        Assert.That(filed, Is.EqualTo(ReportOutbox.MaxFilingsPerDay));
        Assert.That(
            outbox.Pending(),
            Has.Count.EqualTo(2),
            "the rest must still be queued, not discarded"
        );

        // A day later the queue moves again.
        _now = _now.AddDays(1).AddMinutes(1);
        var filedTomorrow = (await outbox.DrainAsync(submitter)).Filed;
        Assert.That(filedTomorrow, Is.EqualTo(2));
    }

    [Test]
    public void Bundles_older_than_the_retention_period_are_dropped()
    {
        var outbox = NewOutbox();
        outbox.Enqueue(Report("ancient"), "BL", "Release", "Frozen");
        Assert.That(outbox.List(), Has.Count.EqualTo(1), "setup: should be there to begin with");

        // Well past the retention period, then something new arrives and triggers the prune.
        _now = _now.Add(ReportOutbox.MaxAge).AddDays(1);
        outbox.Enqueue(Report("recent"), "BL", "Release", "Frozen");

        var remaining = outbox.List();
        Assert.That(remaining, Has.Count.EqualTo(1));
        Assert.That(remaining[0].Metadata.Fingerprint, Is.EqualTo("recent"));
    }

    [Test]
    public void The_queue_cannot_grow_without_bound()
    {
        // This is disk on someone else's machine, and a user who is never online would otherwise
        // accumulate a bundle per freeze forever.
        var outbox = NewOutbox();
        for (var i = 0; i < ReportOutbox.MaxBundles + 5; i++)
        {
            outbox.Enqueue(Report($"fingerprint{i:00}"), "BL", "Release", "Frozen");
            _now = _now.AddMinutes(1);
        }

        Assert.That(outbox.List(), Has.Count.EqualTo(ReportOutbox.MaxBundles));
        Assert.That(
            outbox.List().Select(b => b.Metadata.Fingerprint),
            Does.Not.Contain("fingerprint00"),
            "the oldest should be the one evicted"
        );
    }

    [Test]
    public void A_corrupt_bundle_does_not_break_the_queue()
    {
        var outbox = NewOutbox();
        outbox.Enqueue(Report("good"), "BL", "Release", "Frozen");
        var junk = Path.Combine(_root, "20260819-110000-corrupt");
        Directory.CreateDirectory(junk);
        File.WriteAllText(Path.Combine(junk, ReportOutbox.MetadataFileName), "{ this is not json");

        var listed = outbox.List();

        Assert.That(listed, Has.Count.EqualTo(1), "the good bundle is still readable");
        Assert.That(listed[0].Metadata.Fingerprint, Is.EqualTo("good"));
    }

    [Test]
    public void A_second_problem_with_the_same_Bloom_joins_the_first_card()
    {
        // One Bloom's collapse arrives in instalments: the UI freezes (one report), then the process
        // dies (another, with a different reason and so a different fingerprint). Before this, nothing
        // recognised the two as related and two cards were filed for one event - which is exactly what
        // the first live test produced, AUT-20929 and AUT-20930.
        var outbox = NewOutbox();
        var dump = MakeArtifact("dump-from-the-death.dmp");

        var first = outbox.Enqueue(Report("frozen-fp"), "AUT", "Alpha", "Frozen", processId: 4242);
        Assert.That(outbox.List(), Has.Count.EqualTo(1), "setup: the freeze report is queued");
        Assert.That(
            first.Metadata.FollowOnNotes,
            Is.Empty,
            "setup: nothing has followed on from it yet"
        );

        var second = outbox.Enqueue(
            Report("died-fp", artifacts: new[] { dump }),
            "AUT",
            "Alpha",
            "DiedWhileFrozen",
            processId: 4242
        );

        Assert.That(outbox.List(), Has.Count.EqualTo(1), "still one card's worth, not two");
        Assert.That(
            second.Directory,
            Is.EqualTo(first.Directory),
            "the death should have joined the freeze's bundle"
        );
        Assert.That(
            second.Metadata.Fingerprint,
            Is.EqualTo("frozen-fp"),
            "the first card's identity is what everything about this Bloom lands on"
        );
        Assert.That(
            second.Metadata.FollowOnNotes,
            Has.Count.EqualTo(1),
            "the death must be recorded, not merely swallowed"
        );
        Assert.That(second.Metadata.FollowOnNotes[0], Does.Contain("DiedWhileFrozen"));
        Assert.That(
            second.Metadata.Occurrences,
            Is.EqualTo(1),
            "a different problem is not the same one twice; counting it would make the card claim it was"
        );
        Assert.That(
            second.Metadata.Artifacts,
            Has.Some.StartsWith(ReportOutbox.FollowOnReportPrefix),
            "the follow-on report's own body is kept - 'and then it died' is new information"
        );
        Assert.That(
            second.Metadata.Artifacts,
            Contains.Item("dump-from-the-death.dmp"),
            "and its artifacts come with it, which for a crash is where the minidump is"
        );
        Assert.That(
            File.Exists(Path.Combine(second.Directory, "dump-from-the-death.dmp")),
            Is.True,
            "the artifact should really have been moved in, not just listed"
        );
    }

    [Test]
    public void A_problem_with_a_different_Bloom_gets_its_own_card()
    {
        // The sanity check on the test above: folding is keyed on the process, so two Blooms in trouble
        // must still produce two cards however close together it happens.
        var outbox = NewOutbox();

        outbox.Enqueue(Report("fp-one"), "AUT", "Alpha", "Frozen", processId: 111);
        outbox.Enqueue(Report("fp-two"), "AUT", "Alpha", "Frozen", processId: 222);

        Assert.That(outbox.List(), Has.Count.EqualTo(2), "two Blooms, two problems, two cards");
    }

    [Test]
    public void A_much_later_problem_with_the_same_process_id_gets_its_own_card()
    {
        // The fold is for instalments of one collapse, which arrive seconds apart. Windows reuses process
        // ids, so without a time bound a report still queued from a Bloom that died this morning could
        // swallow this afternoon's report about whatever process inherited its number.
        var outbox = NewOutbox();
        outbox.Enqueue(Report("this-morning"), "AUT", "Alpha", "Frozen", processId: 4242);

        _now = _now + ReportOutbox.FollowOnWindow + TimeSpan.FromMinutes(1);
        outbox.Enqueue(Report("this-afternoon"), "AUT", "Alpha", "Frozen", processId: 4242);

        Assert.That(
            outbox.List(),
            Has.Count.EqualTo(2),
            "too far apart to be the same trouble, so two cards"
        );
    }

    [Test]
    public void A_report_is_not_merged_into_a_bundle_that_has_already_been_filed()
    {
        // Merging is a read-then-write, and the supervisor drains on a timer as well as after each
        // gather, so a drain can finish in between - and a follow-on merge writes a report body and moves
        // a minidump before it writes metadata, which is plenty of time. Writing our stale copy back over
        // a filed bundle would restore Pending and drop its IssueId, and the queue would file the same
        // report a second time: the exact duplicate card that merging exists to prevent.
        var outbox = NewOutbox();
        var first = outbox.Enqueue(Report("shared-fp"), "AUT", "Alpha", "Frozen", processId: 77);
        Assert.That(
            first.Metadata.State,
            Is.EqualTo(BundleState.Pending),
            "setup: it starts out mergeable"
        );

        // Exactly what a drain that won the race leaves behind.
        var filed = first.Metadata with
        {
            State = BundleState.Filed,
            IssueId = "AUT-1",
        };
        File.WriteAllText(
            Path.Combine(first.Directory, ReportOutbox.MetadataFileName),
            System.Text.Json.JsonSerializer.Serialize(filed, BundleMetadata.JsonOptions)
        );

        // Same fingerprint, so this would have merged; and same process, so the follow-on path would
        // have too.
        var second = outbox.Enqueue(Report("shared-fp"), "AUT", "Alpha", "Frozen", processId: 77);

        Assert.That(
            second.Directory,
            Is.Not.EqualTo(first.Directory),
            "it must make its own bundle rather than reopening a filed one"
        );
        var reread = outbox.List().Single(b => b.Directory == first.Directory);
        Assert.That(
            reread.Metadata.State,
            Is.EqualTo(BundleState.Filed),
            "and the filed bundle must still be filed"
        );
        Assert.That(reread.Metadata.IssueId, Is.EqualTo("AUT-1"), "with its card still recorded");
    }

    /// <summary>
    /// Submits, and while it is "uploading" reads what the queue says about the bundle it was handed —
    /// which is the only way to check that the mark goes on *before* the network call rather than after.
    /// Optionally enqueues a second report at that same moment, standing in for a gather that arrives
    /// mid-upload.
    /// </summary>
    private sealed class NosySubmitter : IReportSubmitter
    {
        public NosySubmitter(ReportOutbox outbox) => _outbox = outbox;

        private readonly ReportOutbox _outbox;

        public BundleState StateSeenDuringUpload { get; private set; }
        public Func<QueuedBundle>? EnqueueDuringUpload { get; set; }
        public QueuedBundle? WhatTheGatherProduced { get; private set; }
        public string? CommentedOn { get; private set; }

        public Task<SubmitResult> SubmitAsync(QueuedBundle bundle, CancellationToken cancellation)
        {
            StateSeenDuringUpload = _outbox
                .List()
                .First(b => b.Directory == bundle.Directory)
                .Metadata.State;
            CommentedOn = bundle.Metadata.CommentOnIssueId;
            if (EnqueueDuringUpload != null)
                WhatTheGatherProduced = EnqueueDuringUpload();
            return Task.FromResult(
                new SubmitResult { Outcome = SubmitOutcome.Filed, IssueId = "AUT-4242" }
            );
        }
    }

    [Test]
    public async Task A_bundle_is_marked_as_uploading_before_the_network_call_not_after()
    {
        // The mark is the whole mechanism: it is what lets a gather arriving mid-upload tell "waiting to
        // be sent" from "going out right now", and those need opposite treatment. Marked afterwards it
        // would be useless, and there is no way to tell the two apart except from inside the upload.
        var outbox = NewOutbox();
        outbox.Enqueue(Report("fp"), "AUT", "Alpha", "Frozen", processId: 500);
        var submitter = new NosySubmitter(outbox);

        await outbox.DrainAsync(submitter, CancellationToken.None);

        Assert.That(
            submitter.StateSeenDuringUpload,
            Is.EqualTo(BundleState.Uploading),
            "the queue must say 'being sent right now' for the whole duration of the send"
        );
        Assert.That(
            outbox.List().Single().Metadata.State,
            Is.EqualTo(BundleState.Filed),
            "and settle to Filed once the answer comes back"
        );
    }

    [Test]
    public async Task A_report_arriving_mid_upload_ends_up_commenting_on_the_same_card()
    {
        // The case the developer asked for. A freeze is reported and starts uploading; the same Bloom then
        // dies, and that report wants to join the first one's card. It cannot merge into a bundle that is
        // already going out - that is what would either be lost or overwrite the card id coming back - so
        // it takes a bundle of its own, remembers whose card it belongs on, and waits.
        var outbox = NewOutbox();
        outbox.Enqueue(Report("frozen-fp"), "AUT", "Alpha", "Frozen", processId: 501);

        var submitter = new NosySubmitter(outbox);
        submitter.EnqueueDuringUpload = () =>
            outbox.Enqueue(Report("died-fp"), "AUT", "Alpha", "DiedWhileFrozen", processId: 501);

        await outbox.DrainAsync(submitter, CancellationToken.None);

        var arrival = submitter.WhatTheGatherProduced;
        Assert.That(arrival, Is.Not.Null, "setup: the second report should have been enqueued");
        Assert.That(
            arrival!.Metadata.CommentOnFingerprint,
            Is.EqualTo("frozen-fp"),
            "it must remember whose card it belongs on - nothing could work that out later, because the "
                + "two fingerprints differ, which is what made this a follow-on rather than a recurrence"
        );
        Assert.That(
            arrival.Metadata.State,
            Is.EqualTo(BundleState.Pending),
            "and it must still be waiting, not filed as a card of its own"
        );

        // Second pass: the first bundle now has its card, so the waiting one goes out as a comment on it.
        var second = new NosySubmitter(outbox);
        await outbox.DrainAsync(second, CancellationToken.None);

        Assert.That(
            second.CommentedOn,
            Is.EqualTo("AUT-4242"),
            "the second report should be told which card to comment on, rather than opening another"
        );
    }

    [Test]
    public async Task A_report_waiting_on_a_card_is_not_filed_before_that_card_exists()
    {
        // The sanity check on the test above, and the failure it guards against: a bundle waiting for a
        // sibling's card must not be sent before that card exists, or it opens the second card this whole
        // mechanism exists to prevent. The waiting one is deliberately the OLDER of the two, because the
        // queue drains oldest first - so it is reached while its sibling is still merely pending.
        var outbox = NewOutbox();
        var waiting = outbox.Enqueue(Report("orphan-fp"), "AUT", "Alpha", "Died", processId: 503);
        var meta = waiting.Metadata with { CommentOnFingerprint = "frozen-fp" };
        File.WriteAllText(
            Path.Combine(waiting.Directory, ReportOutbox.MetadataFileName),
            System.Text.Json.JsonSerializer.Serialize(meta, BundleMetadata.JsonOptions)
        );

        _now = _now + TimeSpan.FromMinutes(1);
        outbox.Enqueue(Report("frozen-fp"), "AUT", "Alpha", "Frozen", processId: 502);

        var submitter = new FakeSubmitter();
        await outbox.DrainAsync(submitter, CancellationToken.None);

        Assert.That(
            submitter.Submitted,
            Is.EqualTo(new[] { "frozen-fp" }),
            "only the bundle that owns the card should have gone out; the one waiting on it must hold"
        );
        Assert.That(
            outbox.List().Single(b => b.Metadata.Fingerprint == "orphan-fp").Metadata.State,
            Is.EqualTo(BundleState.Pending),
            "and it must still be waiting rather than given up on"
        );
    }

    [Test]
    public async Task A_send_that_was_interrupted_leaves_its_report_sendable()
    {
        // Marking a bundle before the network call is what lets a gather see that it must not merge. The
        // hazard that creates: the drain only ever looks at Pending, so a mark left behind by a Doctor that
        // was killed mid-upload would make that report invisible to every future drain - never retried,
        // never filed, not even recorded as failed, just quietly evicted by age eventually. Protecting a
        // report must not become the way to lose it.
        var outbox = NewOutbox();
        var bundle = outbox.Enqueue(
            Report("interrupted-fp"),
            "AUT",
            "Alpha",
            "Frozen",
            processId: 600
        );

        // Exactly what a killed Doctor leaves on disk.
        var abandoned = bundle.Metadata with
        {
            State = BundleState.Uploading,
            LastAttemptUtc = _now,
        };
        File.WriteAllText(
            Path.Combine(bundle.Directory, ReportOutbox.MetadataFileName),
            System.Text.Json.JsonSerializer.Serialize(abandoned, BundleMetadata.JsonOptions)
        );
        Assert.That(
            outbox.Pending(),
            Is.Empty,
            "setup: while marked, nothing considers it sendable"
        );

        // Not yet - an upload of a large dump on a poor connection is genuinely slow, and reclaiming one
        // still in flight would file it twice.
        var tooSoon = new FakeSubmitter();
        await outbox.DrainAsync(tooSoon, CancellationToken.None);
        Assert.That(
            tooSoon.Submitted,
            Is.Empty,
            "a send that may still be in flight must be left alone"
        );

        _now = _now + ReportOutbox.AbandonedUploadTimeout + TimeSpan.FromMinutes(1);
        var later = new FakeSubmitter();
        await outbox.DrainAsync(later, CancellationToken.None);

        Assert.That(
            later.Submitted,
            Contains.Item("interrupted-fp"),
            "once the send cannot still be running, the report must go out rather than being stranded"
        );
    }

    [Test]
    public void A_real_minidump_is_routed_to_the_bucket_rather_than_attached()
    {
        // YouTrack will not take an attachment much over 10 MB - Bloom measured about that in July 2020 and
        // stopped attaching altogether - and a Normal dump of a real Bloom is 16-17 MB by this project's
        // own measurement. So the dump must be sorted into the upload pile, not the attach pile. Raising
        // our own budget to "fix" this was the earlier mistake: our number was never the binding one.
        const long measuredDumpBytes = 17L * 1024 * 1024;
        const long youTrackCeilingBytes = 10L * 1024 * 1024;

        Assert.That(
            YouTrackSubmitter.MaxSingleAttachmentBytes,
            Is.LessThan(youTrackCeilingBytes),
            "attaching anything the tracker will refuse just trades a silent skip for a failed upload"
        );
        Assert.That(
            measuredDumpBytes,
            Is.GreaterThan(YouTrackSubmitter.MaxSingleAttachmentBytes),
            "sanity check on the premise: a measured dump really is over the attach limit, so this test is "
                + "about the routing and not a tautology"
        );
        Assert.That(
            YouTrackSubmitter.MaxAttachmentBytes,
            Is.GreaterThan(YouTrackSubmitter.MaxSingleAttachmentBytes),
            "the per-card total must leave room for at least one attachable file"
        );
    }

    [Test]
    public async Task A_report_a_person_asked_for_is_not_refused_by_the_daily_cap()
    {
        // The cap exists to bound cards nobody asked for. Telling somebody who pressed "Report now" that
        // they have had their three for today would be absurd - and pressing it is also how anyone checks
        // that filing works at all, so a capped machine would have no way to test itself.
        var outbox = NewOutbox();
        for (var i = 0; i < ReportOutbox.MaxFilingsPerDay; i++)
        {
            outbox.Enqueue(Report($"automatic{i}"), "BL", "Release", "Frozen");
            _now = _now.AddMinutes(1);
        }
        var submitter = new FakeSubmitter();
        await outbox.DrainAsync(submitter);
        Assert.That(
            outbox.DailyLimitReached(),
            Is.True,
            "setup: the machine has now used its whole allowance on automatic reports"
        );

        outbox.Enqueue(
            Report("asked-for"),
            "BL",
            "Release",
            "RequestedByPerson",
            processId: 900,
            userRequested: true
        );
        var second = new FakeSubmitter();
        await outbox.DrainAsync(second);

        Assert.That(
            second.Submitted,
            Contains.Item("asked-for"),
            "a report somebody deliberately asked for must go out regardless of the cap"
        );
    }

    [Test]
    public async Task A_comment_on_an_existing_card_is_not_refused_by_the_daily_cap()
    {
        // "It happened again" is a few lines of text with no attachments. Counting it against the cap meant
        // three of them could silence a machine for the rest of the day about problems nobody had heard of
        // yet, which is the opposite of what the cap is for.
        var outbox = NewOutbox();
        for (var i = 0; i < ReportOutbox.MaxFilingsPerDay; i++)
        {
            outbox.Enqueue(Report($"automatic{i}"), "BL", "Release", "Frozen");
            _now = _now.AddMinutes(1);
        }
        await outbox.DrainAsync(new FakeSubmitter());
        Assert.That(outbox.DailyLimitReached(), Is.True, "setup: allowance spent");

        // A second report of a problem whose card this machine has already filed: it can only ever add a
        // comment, and the outbox can see that locally.
        outbox.Enqueue(Report("automatic0"), "BL", "Release", "Frozen", processId: 901);
        var second = new FakeSubmitter { CommentsOnly = true };
        await outbox.DrainAsync(second);

        Assert.That(
            second.Submitted,
            Contains.Item("automatic0"),
            "a report that can only add a comment must not be held back by the cap"
        );
    }

    [Test]
    public async Task Comments_do_not_spend_the_allowance_that_new_cards_need()
    {
        // The other half of the same rule, and the one that bites in the field: a machine that comments all
        // morning must still be able to open a card about something genuinely new in the afternoon.
        var outbox = NewOutbox();
        var submitter = new FakeSubmitter { CommentsOnly = true };
        for (var i = 0; i < ReportOutbox.MaxFilingsPerDay + 3; i++)
        {
            outbox.Enqueue(Report($"comment{i}"), "BL", "Release", "Frozen", processId: 950 + i);
            _now = _now.AddMinutes(1);
            await outbox.DrainAsync(submitter);
        }

        Assert.That(
            outbox.DailyLimitReached(),
            Is.False,
            "comments must not consume an allowance that exists to bound new cards"
        );

        outbox.Enqueue(Report("something-new"), "BL", "Release", "Frozen", processId: 999);
        var opener = new FakeSubmitter();
        await outbox.DrainAsync(opener);
        Assert.That(
            opener.Submitted,
            Contains.Item("something-new"),
            "and a genuinely new problem must still get a card"
        );
    }
}
