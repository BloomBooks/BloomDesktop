using BloomFreezeDoctor;
using BloomFreezeDoctor.Outbox;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// What a card says about a file the Doctor could not attach.
///
/// Pinned because the obvious wording is subtly false. "Still on the user's machine at …" reads
/// as a permanent fact; the folder is not permanent, so the sentence could be true when the card was filed
/// and quietly wrong by the time somebody acted on it - which is worse than saying nothing, because it
/// sends them looking for a file that has gone.
/// </summary>
[TestFixture]
public class YouTrackSubmitterTests
{
    private static QueuedBundle Bundle(DateTimeOffset gatheredAt) =>
        new()
        {
            Directory =
                @"C:\Users\jt\AppData\Local\SIL\BloomFreezeDoctor\outbox\20260831-090000-abc",
            Metadata = new BundleMetadata
            {
                Fingerprint = "abc",
                Summary = "for the test",
                Project = "AUT",
                GatheredAtUtc = gatheredAt,
                State = BundleState.Pending,
            },
        };

    [Test]
    public void StillOnTheUsersMachine_NamesFileAndFolder()
    {
        var text = YouTrackSubmitter.StillOnTheUsersMachine(
            Bundle(new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero)),
            "bloom-minidump.dmp"
        );

        Assert.That(text, Does.Contain("bloom-minidump.dmp"));
        Assert.That(text, Does.Contain(@"outbox\20260831-090000-abc"));
    }

    [Test]
    public void StillOnTheUsersMachine_NamesRetentionDate()
    {
        // Retention runs from when the report was gathered, so a card filed today about a report gathered
        // today promises roughly a month - and says so, rather than implying for ever.
        var text = YouTrackSubmitter.StillOnTheUsersMachine(
            Bundle(new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero)),
            "Log.txt"
        );

        var expected = (
            new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero) + ReportOutbox.MaxAge
        ).ToString("yyyy-MM-dd");
        Assert.That(
            text,
            Does.Contain(expected),
            "the card must name the date the folder is kept until, not just the path"
        );
    }

    [Test]
    public void StillOnTheUsersMachine_WarnsThatBundleCapCanShortenRetention()
    {
        // Retention has two limits and only one of them is a date: a bundle also goes when it stops being
        // among the newest MaxBundles. Promising the date alone would be the same kind of wrong, on a
        // machine with twenty queued reports - which is exactly the machine somebody is reading about.
        var text = YouTrackSubmitter.StillOnTheUsersMachine(
            Bundle(DateTimeOffset.UtcNow),
            "Log.txt"
        );

        Assert.That(text, Does.Contain(ReportOutbox.MaxBundles.ToString()));
        Assert.That(
            text,
            Does.Contain("sooner"),
            "and it should tell the reader to ask sooner rather than later"
        );
    }

    private static QueuedBundle CommentBundle(
        string[] artifacts,
        string? commentOnFingerprint = null
    )
    {
        var bundle = Bundle(new DateTimeOffset(2026, 11, 8, 21, 9, 34, TimeSpan.Zero));
        return bundle with
        {
            Metadata = bundle.Metadata with
            {
                Summary = "UI froze, then the process died",
                Artifacts = artifacts,
                CommentOnFingerprint = commentOnFingerprint,
            },
        };
    }

    [Test]
    public void BuildRecurrenceComment_BundleHasNoDump_SaysSo()
    {
        // BL-17092: the comment said "The full report, with the crash dump and Bloom's log, is on that
        // machine" about a report that had no dump, and sent people looking for one.
        var bundle = CommentBundle(new[] { "bloom-log.txt" });
        Assert.That(bundle.Metadata.Artifacts, Has.None.EndsWith(".dmp"), "sanity check");

        var text = YouTrackSubmitter.BuildRecurrenceComment(bundle, contributingADump: false);

        Assert.That(text, Does.Contain("No crash dump was taken for this report"));
        Assert.That(text, Does.Not.Contain("with the crash dump"));
        Assert.That(text, Does.Not.Contain("with its crash dump"));
    }

    [Test]
    public void BuildRecurrenceComment_BundleHasADumpNotAttached_SaysItIsInTheFolder()
    {
        var bundle = CommentBundle(new[] { "bloom-log.txt", "bloom-26400.dmp" });

        var text = YouTrackSubmitter.BuildRecurrenceComment(bundle, contributingADump: false);

        Assert.That(text, Does.Contain("with its crash dump"));
        Assert.That(text, Does.Contain(bundle.Directory));
        Assert.That(text, Does.Not.Contain("No crash dump"));
    }

    [Test]
    public void BuildRecurrenceComment_ContributingADump_SaysItIsAttached()
    {
        var bundle = CommentBundle(new[] { "bloom-26400.dmp" });

        var text = YouTrackSubmitter.BuildRecurrenceComment(bundle, contributingADump: true);

        Assert.That(text, Does.Contain("The crash dump from this report is attached"));
        Assert.That(text, Does.Not.Contain("No crash dump"));
    }

    [Test]
    public void BuildRecurrenceComment_TrueRecurrence_SaysItHappenedAgain()
    {
        // The comment-on field is also set, to the bundle's own fingerprint, for a recurrence that missed
        // being merged; that is still a recurrence.
        var bundle = CommentBundle(new[] { "bloom-log.txt" }, commentOnFingerprint: "abc");
        Assert.That(bundle.Metadata.Fingerprint, Is.EqualTo("abc"), "sanity check");

        var text = YouTrackSubmitter.BuildRecurrenceComment(bundle, contributingADump: false);

        Assert.That(text, Does.Contain("This happened again"));
        Assert.That(text, Does.Contain("near enough a copy of the report already on this card"));
    }

    [Test]
    public void BuildRecurrenceComment_SameBloomsFollowOn_DoesNotSayItHappenedAgain()
    {
        // BL-17092: the second half of one freeze (it froze, then it died) could not be folded into the
        // first report, and was posted as "This happened again" about the very same process.
        var bundle = CommentBundle(new[] { "bloom-log.txt" }, commentOnFingerprint: "def");
        Assert.That(bundle.Metadata.Fingerprint, Is.Not.EqualTo("def"), "sanity check");

        var text = YouTrackSubmitter.BuildRecurrenceComment(bundle, contributingADump: false);

        Assert.That(text, Does.Not.Contain("happened again"));
        Assert.That(text, Does.Contain("The same Bloom then had a further problem"));
        Assert.That(text, Does.Contain("UI froze, then the process died"));
        Assert.That(
            text,
            Does.Not.Contain("near enough a copy"),
            "a follow-on is new information, not a copy of what the card says"
        );
    }
}
