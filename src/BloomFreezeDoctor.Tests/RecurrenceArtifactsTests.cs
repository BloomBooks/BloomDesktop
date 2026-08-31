using BloomFreezeDoctor.Outbox;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// What a repeat occurrence adds to a card that already exists.
///
/// The rule is a compromise between two real failures: a card carrying a 16 MB dump for every crash on a
/// machine in a bad state, and a card carrying none at all because each occurrence assumed the first one
/// had supplied it. The second is what actually happened on a real run.
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
        // The case a real run exposed. The card was opened by a report from the exit examination, which
        // runs after the process has gone and so never has a dump. The next occurrence DID have one -
        // Bloom noticed it was crashing and waited three seconds for us to take it - and under the old
        // blanket rule it was not attached, the comment saying the evidence was "near enough a copy" of
        // what was on the card. What was on the card was nothing.
        var worth = RecurrenceArtifacts.WorthAttaching(
            ADumpAndALog,
            namesAlreadyOnTheCard: new[] { "bloom-log.txt" }
        );

        Assert.That(worth, Has.Count.EqualTo(1), "the dump is the one thing worth adding");
        Assert.That(worth[0], Does.EndWith("bloom-103828.dmp"));
    }

    [Test]
    public void A_card_with_no_attachments_at_all_still_gets_the_dump()
    {
        var worth = RecurrenceArtifacts.WorthAttaching(
            ADumpAndALog,
            namesAlreadyOnTheCard: Array.Empty<string>()
        );

        Assert.That(worth.Select(Path.GetFileName), Is.EqualTo(new[] { "bloom-103828.dmp" }));
    }

    [Test]
    public void A_card_that_already_has_a_dump_gets_nothing()
    {
        // The reason the blanket rule existed, and it still holds: a second dump for the same fingerprint
        // is a near-duplicate at some 16 MB, and a machine crashing repeatedly would bury the card.
        var worth = RecurrenceArtifacts.WorthAttaching(
            ADumpAndALog,
            namesAlreadyOnTheCard: new[] { "bloom-99999.dmp", "bloom-log.txt" }
        );

        Assert.That(
            worth,
            Is.Empty,
            "one dump per problem is what a developer needs; the rest is weight"
        );
    }

    [Test]
    public void A_dump_attached_by_hand_under_any_name_still_counts()
    {
        // Somebody has already put a dump on the card themselves. The Doctor's naming convention says
        // nothing about theirs, so the test is the extension.
        var worth = RecurrenceArtifacts.WorthAttaching(
            ADumpAndALog,
            namesAlreadyOnTheCard: new[] { "from-the-user.DMP" }
        );

        Assert.That(worth, Is.Empty, "and case must not defeat it either");
    }

    [Test]
    public void A_log_is_never_added_on_its_own()
    {
        // Logs are not attachments in the first place - the report body inlines the tail of Bloom's log in
        // a collapsed section - so an "it is missing" test on the card's attachments would be true for
        // every card and would attach a log to all of them.
        var worth = RecurrenceArtifacts.WorthAttaching(
            new[] { @"C:\outbox\20260831-191534-abc\bloom-log.txt" },
            namesAlreadyOnTheCard: Array.Empty<string>()
        );

        Assert.That(worth, Is.Empty);
    }

    [Test]
    public void Nothing_to_give_means_nothing_added()
    {
        Assert.That(
            RecurrenceArtifacts.WorthAttaching(
                Array.Empty<string>(),
                namesAlreadyOnTheCard: Array.Empty<string>()
            ),
            Is.Empty
        );
    }
}
