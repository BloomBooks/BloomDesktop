using System.Diagnostics;
using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The guards on the only destructive thing the Doctor does.
///
/// Almost every test here is about **refusing**, which is the right balance: ending a stuck Bloom is worth
/// doing, but ending the wrong one — or the right one at the wrong moment — is the single way this tool
/// could do a user real harm.
/// </summary>
[TestFixture]
public class ZombieEnderTests
{
    /// <summary>The case where ending it is right: UI gone, evidence gathered, nothing in flight, waited.</summary>
    private static ZombieDecisionFacts AGenuineZombie() =>
        new()
        {
            State = TargetState.Zombie,
            ReportGathered = true,
            SinceDetected = ZombieEnder.GracePeriod + TimeSpan.FromSeconds(5),
            DebuggerCouldExplainIt = false,
            WorkInProgress = false,
            DisabledBySetting = false,
        };

    [Test]
    public void A_genuine_zombie_is_ended()
    {
        var decision = ZombieEnder.Decide(AGenuineZombie());

        Assert.That(decision.ShouldEnd, Is.True);
        Assert.That(
            decision.Explanation,
            Does.Contain("single-instance token"),
            "the reason should name the user's actual symptom: Bloom will not start again"
        );
    }

    [Test]
    public void A_frozen_Bloom_is_never_killed()
    {
        // The most important refusal. A frozen Bloom may hold edits that live in the WebView2 DOM and have
        // not yet reached C#; killing it would throw away the user's work. A zombie has no UI, so there is
        // nothing left to lose — that difference is the whole basis for doing this at all.
        var decision = ZombieEnder.Decide(AGenuineZombie() with { State = TargetState.Frozen });

        Assert.That(decision.ShouldEnd, Is.False);
        Assert.That(decision.Explanation, Does.Contain("not yet reached C#"));
    }

    [TestCase(TargetState.Healthy)]
    [TestCase(TargetState.Suspect)]
    [TestCase(TargetState.Exited)]
    public void Nothing_but_a_zombie_is_ended(TargetState state)
    {
        Assert.That(
            ZombieEnder.Decide(AGenuineZombie() with { State = state }).ShouldEnd,
            Is.False
        );
    }

    [Test]
    public void The_evidence_is_gathered_before_anything_is_killed()
    {
        // Killing first would destroy the only copy of what we came for.
        var decision = ZombieEnder.Decide(AGenuineZombie() with { ReportGathered = false });

        Assert.That(decision.ShouldEnd, Is.False);
        Assert.That(decision.Explanation, Does.Contain("destroy"));
    }

    [Test]
    public void A_slow_shutdown_is_given_time_to_finish_by_itself()
    {
        var decision = ZombieEnder.Decide(
            AGenuineZombie() with
            {
                SinceDetected = TimeSpan.FromSeconds(10),
            }
        );

        Assert.That(decision.ShouldEnd, Is.False);
        Assert.That(decision.Explanation, Does.Contain("slow shutdown"));
    }

    [Test]
    public void A_Bloom_that_says_it_is_still_saving_is_left_alone()
    {
        // Waiting costs nothing; interrupting a save can cost a book.
        var decision = ZombieEnder.Decide(AGenuineZombie() with { WorkInProgress = true });

        Assert.That(decision.ShouldEnd, Is.False);
        Assert.That(decision.Explanation, Does.Contain("saving or publishing"));
    }

    [Test]
    public void A_debugged_Bloom_is_not_ours_to_end()
    {
        // A developer's paused Bloom looks a great deal like a zombie.
        var decision = ZombieEnder.Decide(AGenuineZombie() with { DebuggerCouldExplainIt = true });

        Assert.That(decision.ShouldEnd, Is.False);
        Assert.That(decision.Explanation, Does.Contain("debugger"));
    }

    [Test]
    public void The_setting_switches_it_off_entirely()
    {
        var decision = ZombieEnder.Decide(AGenuineZombie() with { DisabledBySetting = true });

        Assert.That(decision.ShouldEnd, Is.False);
        Assert.That(decision.Explanation, Does.Contain("switched off"));
    }

    [Test]
    public void Ending_a_process_that_has_already_gone_is_not_a_failure()
    {
        // A pid that no longer exists is the outcome we wanted, however it came about.
        var outcome = ZombieEnder.End(999_999_9, DateTime.Now);

        Assert.That(
            outcome,
            Is.EqualTo(ZombieEndOutcome.AlreadyGone),
            "a Bloom that ended itself while we deliberated is a success, not an error"
        );
    }

    [Test]
    public void A_reused_process_id_is_left_alone_rather_than_killed()
    {
        // The dangerous case, and the reason End takes a start time at all. Windows hands process ids out
        // of a pool, so an id we are holding for a dead Bloom can belong to somebody else by the time we
        // act on it. Here the id is real and running - it is THIS process - but the start time says it is
        // not the process we meant.
        //
        // If this ever regresses, the test run dies: End would signal, and then kill, the test host. That
        // is uncomfortable but honest - it is exactly what the bug does to a user's machine.
        var thisProcess = Process.GetCurrentProcess();
        var notWhenItStarted = thisProcess.StartTime.AddMinutes(-5);

        // Sanity check first: the id really is live, so a passing result cannot come from it being absent.
        Assert.That(
            ProcessIdentity.IsStillTheSameProcess(thisProcess.Id, thisProcess.StartTime),
            Is.True,
            "sanity check: this process must be recognised as itself"
        );

        var outcome = ZombieEnder.End(thisProcess.Id, notWhenItStarted);

        Assert.That(
            outcome,
            Is.EqualTo(ZombieEndOutcome.AlreadyGone),
            "an id that has been handed on is treated as gone, never as ours to end"
        );
        Assert.That(
            ProcessIdentity.IsStillTheSameProcess(thisProcess.Id, thisProcess.StartTime),
            Is.True,
            "and the process that actually owns the id is untouched"
        );
    }
}
