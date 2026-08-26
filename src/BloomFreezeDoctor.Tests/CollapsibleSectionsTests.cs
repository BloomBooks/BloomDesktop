using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Turning the marked regions of a report into collapsed blocks for a tracker card. The same report text
/// has two readers — a card and a text editor — and this is the seam between them, so the things worth
/// testing are that the card gets valid HTML and that nothing outside a marked region is disturbed.
/// </summary>
[TestFixture]
public class CollapsibleSectionsTests
{
    [Test]
    public void A_report_with_no_marked_regions_is_returned_untouched()
    {
        // Most reports have none, and the transform must be invisible to them - not merely harmless, but
        // byte-for-byte absent, since it runs on every card.
        const string report =
            "## What happened\n\nThe UI thread is blocked.\n\n```\nsome code\n```\n";

        Assert.That(CollapsibleSections.RenderForACard(report), Is.EqualTo(report));
    }

    [Test]
    public void A_marked_region_becomes_a_collapsed_block_with_its_label_outside()
    {
        var report =
            "Before.\n"
            + CollapsibleSections.BeginPrefix
            + "Bloom's log -->\n```\nline one\nline two\n```\n"
            + CollapsibleSections.End
            + "\nAfter.\n";

        var card = CollapsibleSections.RenderForACard(report);

        Assert.That(card, Does.Contain("#### Bloom's log"), "the label becomes a heading");
        Assert.That(card, Does.Contain("<details>").And.Contain("</details>"));
        Assert.That(card, Does.Contain("<pre>").And.Contain("</pre>"));
        Assert.That(card, Does.Contain("line one").And.Contain("line two"), "content survives");
        Assert.That(
            card,
            Does.Not.Contain("```"),
            "the fences were for the on-disk reader; inside <pre> they would show as backticks"
        );
        Assert.That(card, Does.Contain("Before.").And.Contain("After."), "and nothing else moves");
        Assert.That(
            card,
            Does.Not.Contain(CollapsibleSections.BeginPrefix),
            "no marker should survive into the card"
        );
    }

    [Test]
    public void Angle_brackets_inside_a_region_are_escaped()
    {
        // The reason this matters is specific: thread stacks are full of generic types, and inside <pre>
        // an unescaped `List<int>` is read as a tag and silently vanishes - taking the rest of the line
        // with it, in the one section a reader is most likely to be studying closely.
        var report =
            CollapsibleSections.BeginPrefix
            + "Threads -->\nSystem.Collections.Generic.List<int>.Add & more\n"
            + CollapsibleSections.End
            + "\n";

        var card = CollapsibleSections.RenderForACard(report);

        Assert.That(card, Does.Contain("List&lt;int&gt;"), "the type must still be readable");
        Assert.That(card, Does.Contain("&amp; more"), "and an ampersand must not start an entity");
    }

    [Test]
    public void An_unterminated_region_does_not_swallow_the_rest_of_the_card()
    {
        // A collector that returned early mid-region would otherwise leave <details> open, and everything
        // after it - including whatever the report concluded - would be inside a collapsed block.
        var report = "Before.\n" + CollapsibleSections.BeginPrefix + "Threads -->\nthread stuff\n";

        var card = CollapsibleSections.RenderForACard(report);

        Assert.That(
            card.Split("</details>"),
            Has.Length.EqualTo(2),
            "exactly one closing tag, supplied for the region the report left open"
        );
        Assert.That(card.TrimEnd(), Does.EndWith("</details>"));
    }
}
