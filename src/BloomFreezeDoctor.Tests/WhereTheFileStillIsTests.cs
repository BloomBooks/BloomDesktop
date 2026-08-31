using BloomFreezeDoctor;
using BloomFreezeDoctor.Outbox;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// What a card says about a file the Doctor could not attach.
///
/// Pinned because the obvious wording is subtly false, and was. "Still on the user's machine at …" reads
/// as a permanent fact; the folder is not permanent, so the sentence could be true when the card was filed
/// and quietly wrong by the time somebody acted on it - which is worse than saying nothing, because it
/// sends them looking for a file that has gone.
/// </summary>
[TestFixture]
public class WhereTheFileStillIsTests
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
    public void It_says_where_the_file_is()
    {
        var text = YouTrackSubmitter.StillOnTheUsersMachine(
            Bundle(new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero)),
            "bloom-minidump.dmp"
        );

        Assert.That(text, Does.Contain("bloom-minidump.dmp"));
        Assert.That(text, Does.Contain(@"outbox\20260831-090000-abc"));
    }

    [Test]
    public void It_says_how_long_that_will_stay_true()
    {
        // The correction John asked for. Retention runs from when the report was gathered, so a card filed
        // today about a report gathered today promises roughly a month - and says so, rather than implying
        // for ever.
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
    public void It_warns_that_volume_can_shorten_that()
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
}
