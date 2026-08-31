using BloomFreezeDoctor.Outbox;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// What a repeat occurrence adds to a card that already exists.
///
/// The rule is a compromise between two real failures: a card carrying a 16 MB dump for every crash on a
/// machine in a bad state, and a card carrying none at all because each occurrence assumed the first had
/// supplied it. Both happened on real runs, in that order.
/// </summary>
[TestFixture]
public class RecurrenceArtifactsTests
{
    private static readonly string[] ADumpAndALog =
    {
        @"C:\outbox\20260831-191534-abc\bloom-103828.dmp",
        @"C:\outbox\20260831-191534-abc\bloom-log.txt",
    };

    [Test]
    public void A_dump_goes_to_a_card_that_has_none()
    {
        var worth = RecurrenceArtifacts.WorthAttaching(ADumpAndALog, cardAlreadyHasADump: false);

        Assert.That(worth, Has.Count.EqualTo(1), "the dump is the one thing worth adding");
        Assert.That(worth[0], Does.EndWith("bloom-103828.dmp"));
    }

    [Test]
    public void A_card_that_already_has_a_dump_gets_nothing()
    {
        // The reason a blanket "attach nothing" rule existed, and it still holds: a second dump for the
        // same fingerprint is a near-duplicate at some 16 MB, and a machine crashing repeatedly would bury
        // the card.
        var worth = RecurrenceArtifacts.WorthAttaching(ADumpAndALog, cardAlreadyHasADump: true);

        Assert.That(
            worth,
            Is.Empty,
            "one dump per problem is what a developer needs; the rest is weight"
        );
    }

    [Test]
    public void A_log_is_never_added_on_its_own()
    {
        // Logs are not attachments in the first place - the report body inlines the tail of Bloom's log in
        // a collapsed section - so an "it is missing" test would be true for every card and would attach a
        // log to all of them.
        var worth = RecurrenceArtifacts.WorthAttaching(
            new[] { @"C:\outbox\20260831-191534-abc\bloom-log.txt" },
            cardAlreadyHasADump: false
        );

        Assert.That(worth, Is.Empty);
    }

    [Test]
    public void An_attached_dump_counts()
    {
        Assert.That(
            RecurrenceArtifacts.ShowsADump(
                new[] { "bloom-99999.dmp", "bloom-log.txt" },
                Array.Empty<string>()
            ),
            Is.True
        );
    }

    [Test]
    public void A_dump_attached_by_hand_under_any_name_still_counts()
    {
        // Somebody has already put a dump on the card themselves. The Doctor's naming convention says
        // nothing about theirs, so the test is the extension - and case must not defeat it.
        Assert.That(
            RecurrenceArtifacts.ShowsADump(new[] { "from-the-user.DMP" }, Array.Empty<string>()),
            Is.True
        );
    }

    [Test]
    public void A_dump_in_the_support_bucket_counts_too()
    {
        // The bug a real run produced, and the reason this question is no longer "does it have an
        // ATTACHMENT". A dump 422 bytes over the attachment ceiling is uploaded to the bucket and linked
        // from a comment: the card plainly has the dump, and has no attachment at all. Asking only about
        // attachments meant the next occurrence uploaded a second copy and told the reader the card had
        // none - a confident false statement, which is worse than silence.
        var comment =
            "**Files too large for a tracker attachment**, uploaded to Bloom's support bucket:\r\n\r\n"
            + "- [bloom-7928.dmp](https://s3.amazonaws.com/bloom-problem-books/freeze-doctor/"
            + "7502be8f254f4a1e96cf51851beaf47a/bloom-7928.dmp) (8.0 MB)\r\n";

        Assert.That(
            RecurrenceArtifacts.ShowsADump(Array.Empty<string>(), new[] { comment }),
            Is.True
        );
    }

    [Test]
    public void Prose_about_a_dump_that_could_not_be_sent_does_not_count()
    {
        // The trap in reading the card's text: the Doctor's own comments TALK about dumps. A comment saying
        // a dump could not be attached and names the folder it is still in must not be read as the card
        // carrying one, or the first failure would suppress every later attempt to supply it.
        var comment =
            "- `bloom-7928.dmp` (8.0 MB) - the tracker refused the upload. Still on the user's machine "
            + @"at `C:\Users\jt\AppData\Local\SIL\BloomFreezeDoctor\outbox\20260831-204053-abc`.";

        Assert.That(
            RecurrenceArtifacts.ShowsADump(Array.Empty<string>(), new[] { comment }),
            Is.False,
            "naming a dump is not the same as carrying one"
        );
    }

    [Test]
    public void A_book_someone_uploaded_to_the_same_bucket_does_not_count()
    {
        // The other half of that: the support bucket is shared with Bloom's problem-book uploads, so the
        // bucket name alone proves nothing.
        var comment =
            "The book is at https://s3.amazonaws.com/bloom-problem-books/somebody/book.bloomSource";

        Assert.That(
            RecurrenceArtifacts.ShowsADump(Array.Empty<string>(), new[] { comment }),
            Is.False
        );
    }

    [Test]
    public void A_card_with_nothing_shows_no_dump()
    {
        Assert.That(
            RecurrenceArtifacts.ShowsADump(Array.Empty<string>(), Array.Empty<string>()),
            Is.False
        );
    }
}
