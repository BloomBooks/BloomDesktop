using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Tests for the detection rules. Several of these exist because the Phase 0 spike showed the
/// obvious implementation would be wrong; those name the trap they are guarding.
/// </summary>
[TestFixture]
public class FreezeDetectorTests
{
    private FreezeDetector _detector = null!;

    [SetUp]
    public void SetUp()
    {
        _detector = new FreezeDetector();
    }

    /// <summary>A healthy reading: alive, answering, with a visible window.</summary>
    private static TargetObservation Healthy(double atSeconds) =>
        new()
        {
            Uptime = TimeSpan.FromSeconds(atSeconds),
            IsAlive = true,
            WindowResponds = true,
            HasVisibleWindow = true,
        };

    /// <summary>Alive with a window, but the window will not answer.</summary>
    private static TargetObservation NotResponding(double atSeconds) =>
        Healthy(atSeconds) with
        {
            WindowResponds = false,
        };

    /// <summary>
    /// Feeds a steady one-per-second stream between two times, so elapsed-time rules are exercised
    /// the way they will be in production rather than by a single jump.
    ///
    /// Returns the verdict that asked for a report if one did, otherwise the last verdict. Returning
    /// only the last would make these tests depend on a report landing exactly on the final second,
    /// which is a property of the arithmetic rather than of the behaviour we mean to pin down — and
    /// it is deceptive, because a report is fired once and then suppressed, so a test looking only at
    /// the end sees "None" for a freeze that was reported perfectly well a second earlier.
    /// </summary>
    private DetectorVerdict RunSeconds(
        double from,
        double to,
        Func<double, TargetObservation> shape
    )
    {
        var last = default(DetectorVerdict);
        DetectorVerdict? reported = null;
        for (var t = from; t <= to; t += 1)
        {
            last = _detector.Observe(shape(t));
            if (last.ShouldReport)
                reported ??= last;
        }
        return reported ?? last;
    }

    [Test]
    public void Healthy_target_is_healthy_and_silent()
    {
        var verdict = RunSeconds(0, 30, Healthy);

        Assert.That(verdict.State, Is.EqualTo(TargetState.Healthy));
        Assert.That(verdict.ShouldReport, Is.False, "a healthy Bloom must never produce a report");
    }

    [Test]
    public void Becomes_suspect_at_twenty_seconds_but_does_not_report_yet()
    {
        _detector.Observe(Healthy(0));
        var justBefore = RunSeconds(1, 19, NotResponding);
        Assert.That(
            justBefore.State,
            Is.EqualTo(TargetState.Healthy),
            "19s unresponsive is below the suspect threshold"
        );

        var atThreshold = _detector.Observe(NotResponding(20));

        Assert.That(atThreshold.State, Is.EqualTo(TargetState.Suspect));
        Assert.That(atThreshold.ShouldReport, Is.False, "suspect is not yet worth a card");
    }

    [Test]
    public void Reports_once_at_sixty_seconds_and_not_again()
    {
        _detector.Observe(Healthy(0));

        var atThreshold = RunSeconds(1, 60, NotResponding);
        Assert.That(atThreshold.State, Is.EqualTo(TargetState.Frozen));
        Assert.That(atThreshold.Report, Is.EqualTo(ReportReason.Frozen));
        Assert.That(atThreshold.Explanation, Does.Contain("has not answered"));

        var laterStill = RunSeconds(61, 300, NotResponding);
        Assert.That(laterStill.State, Is.EqualTo(TargetState.Frozen), "still frozen");
        Assert.That(
            laterStill.ShouldReport,
            Is.False,
            "one freeze must produce one card, however long it lasts"
        );
    }

    [Test]
    public void Recovery_after_a_reported_freeze_is_itself_reported()
    {
        _detector.Observe(Healthy(0));
        var frozen = RunSeconds(1, 60, NotResponding);
        Assert.That(
            frozen.Report,
            Is.EqualTo(ReportReason.Frozen),
            "setup: should be frozen first"
        );

        var recovered = _detector.Observe(Healthy(61));

        Assert.That(recovered.State, Is.EqualTo(TargetState.Healthy));
        Assert.That(
            recovered.Report,
            Is.EqualTo(ReportReason.RecoveredFromFreeze),
            "a freeze the user waited out is at least as informative as one they killed"
        );
    }

    [Test]
    public void Dying_while_frozen_produces_one_report_not_two()
    {
        _detector.Observe(Healthy(0));
        var frozen = RunSeconds(1, 60, NotResponding);
        Assert.That(
            frozen.Report,
            Is.EqualTo(ReportReason.Frozen),
            "setup: should be frozen first"
        );

        var died = _detector.Observe(NotResponding(61) with { IsAlive = false });

        Assert.That(died.State, Is.EqualTo(TargetState.Exited));
        Assert.That(died.Report, Is.EqualTo(ReportReason.DiedWhileFrozen));
    }

    [Test]
    public void A_long_operation_buys_patience_but_not_immunity()
    {
        _detector.Observe(Healthy(0));

        var atOneMinute = RunSeconds(
            1,
            60,
            t => NotResponding(t) with { LongOperationInProgress = true }
        );
        Assert.That(
            atOneMinute.ShouldReport,
            Is.False,
            "Bloom said it was busy on purpose, so 60s is not yet suspicious"
        );

        var atFiveMinutes = RunSeconds(
            61,
            300,
            t => NotResponding(t) with { LongOperationInProgress = true }
        );
        Assert.That(atFiveMinutes.Report, Is.EqualTo(ReportReason.Frozen));
        Assert.That(
            atFiveMinutes.Explanation,
            Does.Contain("long operation"),
            "the card should say we overrode Bloom's claim to be busy"
        );
    }

    [Test]
    public void A_stale_heartbeat_with_corroboration_catches_the_freeze_Tier_A_cannot_see()
    {
        // The spike's headline finding: with the UI thread stuck in an STA managed wait, the window
        // still answers messages. Only the heartbeat notices.
        _detector.Observe(Healthy(0));

        var verdict = RunSeconds(
            1,
            60,
            t => Healthy(t) with { HeartbeatIsStale = true, UiBlockCorroborated = true }
        );

        Assert.That(verdict.Report, Is.EqualTo(ReportReason.Frozen));
        Assert.That(
            verdict.Explanation,
            Does.Contain("heartbeat"),
            "the card must say which signal caught it, since the window looked fine"
        );
    }

    [Test]
    public void A_stale_heartbeat_alone_is_not_enough()
    {
        // WM_TIMER is the lowest-priority message, so a busy-but-live UI can starve the heartbeat.
        // Without corroboration that must not become a report.
        _detector.Observe(Healthy(0));

        var verdict = RunSeconds(
            1,
            300,
            t => Healthy(t) with { HeartbeatIsStale = true, UiBlockCorroborated = false }
        );

        Assert.That(verdict.State, Is.EqualTo(TargetState.Healthy));
        Assert.That(verdict.ShouldReport, Is.False);
    }

    [Test]
    public void A_debugged_process_stays_poisoned_when_we_cannot_tell_when_the_debugger_left()
    {
        // Stopping the debugger is a hard kill leaving no proof of shutdown, and it is the most
        // common thing a developer does all day. Asking a dead process about its debugger is
        // impossible, so with no departure time to work from the flag has to stay sticky.
        //
        // This is what a Bloom too old to publish a channel gets, and what our own outside sampling gets:
        // "one was here, no idea when it went" has to mean "assume it is still relevant".
        _detector.Observe(
            Healthy(0) with
            {
                DebuggerAttachedNow = true,
                DebuggerEverAttached = true,
            }
        );
        Assert.That(
            _detector.IsPoisonedByDebugger,
            Is.True,
            "setup: should be flagged immediately"
        );

        RunSeconds(1, 60, NotResponding); // debugger no longer reported attached

        Assert.That(
            _detector.IsPoisonedByDebugger,
            Is.True,
            "with no departure time, a target seen under a debugger stays untrustworthy"
        );
    }

    [Test]
    public void A_debugger_that_left_long_before_the_freeze_does_not_excuse_it()
    {
        // The case the old permanent poison got wrong, and the reason Bloom now records WHEN a debugger
        // left. Attach a debugger in the morning, detach it, and hours later Bloom genuinely freezes: that
        // freeze is real, and it is happening on the machine of somebody well placed to help diagnose it.
        // Writing off the rest of the run threw exactly that report away.
        _detector.Observe(
            Healthy(0) with
            {
                DebuggerAttachedNow = true,
                DebuggerEverAttached = true,
            }
        );
        Assert.That(_detector.IsPoisonedByDebugger, Is.True, "setup: attached, so poisoned");

        // It leaves. Note that immediately after a detach it is still poisoned, and rightly so — the margin
        // exists because handing Bloom back is not instant. Once the departure is older than the margin and
        // nothing is wrong, there is nothing left for it to explain.
        _detector.Observe(
            Healthy(1) with
            {
                DebuggerEverAttached = true,
                DebuggerLastDetachedAge = TimeSpan.FromMinutes(5),
            }
        );
        Assert.That(
            _detector.IsPoisonedByDebugger,
            Is.False,
            "gone five minutes, and Bloom is healthy: nothing for it to account for"
        );

        // Now a freeze, long after the debugger's departure. The departure is much older than the freeze.
        RunSeconds(
            2,
            90,
            t =>
                NotResponding(t) with
                {
                    DebuggerEverAttached = true,
                    DebuggerLastDetachedAge = TimeSpan.FromHours(3),
                }
        );

        Assert.That(
            _detector.IsPoisonedByDebugger,
            Is.False,
            "a debugger that left three hours before this freeze began cannot account for it"
        );
        Assert.That(
            _detector.State,
            Is.EqualTo(TargetState.Frozen),
            "sanity: the freeze itself should still have been detected"
        );
    }

    [Test]
    public void A_debugger_that_left_during_the_freeze_does_excuse_it()
    {
        // The other direction, and the one that must not regress: a developer sitting at a breakpoint
        // produces a heartbeat gap indistinguishable from a freeze. If they detach while it is still
        // running, the gap is already there and the debugger is what caused it.
        RunSeconds(
            0,
            90,
            t =>
                NotResponding(t) with
                {
                    DebuggerEverAttached = true,
                    // It left ten seconds ago; the gap has been growing for ninety.
                    DebuggerLastDetachedAge = TimeSpan.FromSeconds(10),
                }
        );

        Assert.That(
            _detector.IsPoisonedByDebugger,
            Is.True,
            "the debugger was still attached when this episode began, so it explains it"
        );
    }

    [Test]
    public void A_debugger_still_attached_when_the_process_died_explains_the_death()
    {
        // Terminating from a debugger is a TerminateProcess: no clean-exit proof, so it looks exactly like
        // an unreported crash. Bloom cannot record a detach because Bloom is already gone — so what the
        // probe reports is the last state it saw, which still says "attached".
        _detector.Observe(
            Healthy(0) with
            {
                DebuggerAttachedNow = true,
                DebuggerEverAttached = true,
            }
        );

        var verdict = _detector.Observe(
            Healthy(1) with
            {
                IsAlive = false,
                DebuggerAttachedNow = true,
                DebuggerEverAttached = true,
            }
        );

        Assert.That(verdict.State, Is.EqualTo(TargetState.Exited), "sanity: it noticed the exit");
        Assert.That(
            _detector.IsPoisonedByDebugger,
            Is.True,
            "a debugger attached at the moment of death is the likeliest reason for it"
        );
    }

    [Test]
    public void A_sleeping_machine_does_not_manufacture_a_freeze()
    {
        // The gap between observations is what gives this away: the Doctor cannot run while the
        // machine is asleep, so a jump far beyond our cadence means the world stopped, not that
        // Bloom hung.
        _detector.Observe(Healthy(0));
        _detector.Observe(Healthy(1));

        // Machine sleeps for two hours, then the very next reading finds Bloom briefly unresponsive
        // as it thaws.
        var afterWake = _detector.Observe(NotResponding(7201));

        Assert.That(
            afterWake.State,
            Is.EqualTo(TargetState.Healthy),
            "two hours of sleep must not read as two hours of freeze"
        );
        Assert.That(afterWake.ShouldReport, Is.False);

        // And it must still detect a real freeze that begins after waking.
        var reallyFrozen = RunSeconds(7202, 7262, NotResponding);
        Assert.That(
            reallyFrozen.Report,
            Is.EqualTo(ReportReason.Frozen),
            "after the gap is discounted, the clock restarts rather than stopping"
        );
    }

    [Test]
    public void No_visible_window_for_thirty_seconds_is_a_zombie()
    {
        // "Visible" matters: a healthy Bloom keeps an invisible top-level window all session,
        // because its splash screen is hidden rather than closed. Counting any window would mean
        // never detecting state 3.
        _detector.Observe(Healthy(0));

        var verdict = RunSeconds(
            1,
            30,
            t => Healthy(t) with { HasVisibleWindow = false, WindowResponds = false }
        );

        Assert.That(verdict.State, Is.EqualTo(TargetState.Zombie));
        Assert.That(verdict.Report, Is.EqualTo(ReportReason.Zombie));
        Assert.That(verdict.Explanation, Does.Contain("no visible window"));
    }

    [Test]
    public void An_apparently_healthy_exit_is_not_the_detectors_call()
    {
        // Phase 1 reports an exit only with corroborating evidence, and Phase 3 on the absence of a
        // clean-exit proof. Neither is something the detector can see, so it must not guess.
        RunSeconds(0, 30, Healthy);

        var exited = _detector.Observe(Healthy(31) with { IsAlive = false });

        Assert.That(exited.State, Is.EqualTo(TargetState.Exited));
        Assert.That(
            exited.ShouldReport,
            Is.False,
            "the exit classifier decides this, using evidence the detector does not have"
        );
    }
}
