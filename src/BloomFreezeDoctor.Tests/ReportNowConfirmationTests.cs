using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// What "Report now" says before it creates a real tracker card.
///
/// Worth testing on its own because the failure is quiet and outward-facing: a click on a button that is
/// only on screen because CTRL is held filed a real card, with no dialog, whenever the watched Bloom was
/// healthy on a build allowed to file (BL-16719). The window itself cannot be unit tested, so the words
/// are decided here.
/// </summary>
[TestFixture]
public class ReportNowConfirmationTests
{
    private static readonly IReadOnlyList<string> NoBlockers = Array.Empty<string>();

    [Test]
    public void Reasons_HealthyBloomNothingBlocking_SaysNothingIsWrong()
    {
        var reasons = ReportNowConfirmation.Reasons(TargetState.Healthy, NoBlockers);

        Assert.That(reasons, Has.Count.EqualTo(1));
        Assert.That(reasons[0], Does.Contain("has not found anything wrong"));
    }

    [Test]
    public void Reasons_SuspectBloom_SaysNotYetDecidedFrozen()
    {
        var reasons = ReportNowConfirmation.Reasons(TargetState.Suspect, NoBlockers);

        Assert.That(reasons, Has.Count.EqualTo(1));
        Assert.That(reasons[0], Does.Contain("not yet decided"));
    }

    [Test]
    public void Reasons_FrozenBloomNothingBlocking_Empty()
    {
        // A frozen Bloom on a build that may file is the one case where the Doctor itself would have filed,
        // so there is nothing to list as being overridden.
        Assert.That(ReportNowConfirmation.Reasons(TargetState.Frozen, NoBlockers), Is.Empty);
        Assert.That(ReportNowConfirmation.Reasons(TargetState.Zombie, NoBlockers), Is.Empty);
    }

    [Test]
    public void Reasons_HealthyBloomWithBlockers_ListsNothingWrongFirstThenBlockers()
    {
        var blockers = new[] { "this is a developer build", "simulated on purpose" };

        var reasons = ReportNowConfirmation.Reasons(TargetState.Healthy, blockers);

        Assert.That(reasons, Has.Count.EqualTo(3));
        Assert.That(reasons[0], Does.Contain("has not found anything wrong"));
        Assert.That(reasons[1], Is.EqualTo("this is a developer build"));
        Assert.That(reasons[2], Is.EqualTo("simulated on purpose"));
    }

    [Test]
    public void Message_HealthyBloomNothingBlocking_WarnsAndNamesProject()
    {
        // The bug: this exact situation (a tester's healthy BetaInternal Bloom) used to produce no dialog
        // at all, so the message here must both exist and say what is being overridden.
        var message = ReportNowConfirmation.Message(TargetState.Healthy, NoBlockers, "BL");

        Assert.That(message, Does.Contain("would not normally file"));
        Assert.That(message, Does.Contain("has not found anything wrong"));
        Assert.That(message, Does.Contain("create a real card in BL?"));
    }

    [Test]
    public void Message_FrozenBloomNothingBlocking_StillAsksAndNamesProject()
    {
        var message = ReportNowConfirmation.Message(TargetState.Frozen, NoBlockers, "BLTEST");

        Assert.That(message, Is.Not.Empty);
        Assert.That(message, Does.Not.Contain("would not normally file"));
        Assert.That(message, Does.Contain("gathers a fresh report"));
        Assert.That(message, Does.Contain("create a real card in BLTEST?"));
    }

    [Test]
    public void Message_WithBlockers_ListsEachOnItsOwnBulletedLine()
    {
        var blockers = new[] { "this is a developer build", "a debugger has been attached" };

        var message = ReportNowConfirmation.Message(TargetState.Frozen, blockers, "BL");

        Assert.That(message, Does.Contain("• this is a developer build" + Environment.NewLine));
        Assert.That(message, Does.Contain("• a debugger has been attached"));
        Assert.That(message, Does.Contain("\"Report now\" files anyway."));
    }
}
