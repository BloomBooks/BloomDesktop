using BloomFreezeDoctor;
using BloomFreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Decision D4 in both of its regimes. The exit codes used here were measured in the Phase 0 spike
/// rather than recalled, and the "quiet" cases matter as much as the reportable ones: this classifier's
/// main job in Phase 1 is to keep the tracker clean.
/// </summary>
[TestFixture]
public class ExitClassifierTests
{
    [Test]
    public void A_bare_exit_stays_quiet()
    {
        // The whole point of the Phase 1 rule: a Bloom that simply vanished is indistinguishable from
        // the user closing it in a way we could not see.
        var conclusion = ExitClassifier.Classify(new ExitEvidence { ExitCode = 1 });

        Assert.That(conclusion.ShouldReport, Is.False);
        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.NoOrderlyShutdown));
        Assert.That(
            conclusion.Explanation,
            Does.Contain("code 1"),
            "the local record should explain why we stayed quiet"
        );
    }

    [TestCase(ExitClassifier.ExitCodeUnhandledManagedException, "0xE0434352")]
    [TestCase(ExitClassifier.ExitCodeFailFast, "0x80131623")]
    [TestCase(ExitClassifier.ExitCodeAccessViolation, "0xC0000005")]
    public void A_crash_exit_code_is_reportable(int exitCode, string expectedInText)
    {
        var conclusion = ExitClassifier.Classify(new ExitEvidence { ExitCode = exitCode });

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Crashed));
        Assert.That(conclusion.ShouldReport, Is.True);
        Assert.That(conclusion.Explanation, Does.Contain(expectedInText));
    }

    [Test]
    public void Event_log_or_WER_evidence_is_enough_on_its_own()
    {
        var fromEventLog = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = 1, HasEventLogCrashEntry = true }
        );
        var fromWer = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = 1, HasWerReport = true }
        );

        Assert.That(fromEventLog.ShouldReport, Is.True);
        Assert.That(fromWer.ShouldReport, Is.True);
        Assert.That(fromWer.Verdict, Is.EqualTo(ExitVerdict.Crashed));
    }

    [Test]
    public void A_stalled_shutdown_that_Bloom_forced_gets_its_own_verdict()
    {
        // Exit code 1, same as a Task Manager kill — but Bloom's log says which it was, and this is a
        // real bug we would otherwise never hear about.
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = 1, LogShowsForcedShutdown = true }
        );

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.ForcedAfterStalledShutdown));
        Assert.That(conclusion.ShouldReport, Is.True, "reportable in either regime");
        Assert.That(conclusion.Explanation, Does.Contain("stalled"));
    }

    [Test]
    public void A_missing_clean_exit_proof_is_not_by_itself_worth_a_card()
    {
        // The deliberate reversal. This case used to be reported, with the card's own text conceding it
        // "may be a user-initiated kill" - which is exactly what it usually was. Absence of proof has too
        // many innocent causes to act on: Task Manager, a Windows shutdown that force-closed Bloom, a
        // power cut a moment before the machine noticed.
        //
        // Nothing is lost by declining, because a real crash does not present this way: see
        // An_unhandled_exception_on_a_foreign_thread_is_reported below.
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = -1, CleanExitProofPresent = false }
        );

        Assert.That(
            conclusion.ShouldReport,
            Is.False,
            "an exit we cannot explain is not the same as an exit we can blame on Bloom"
        );
    }

    [Test]
    public void An_unhandled_exception_on_a_foreign_thread_is_reported()
    {
        // The case the whole exit path exists for: an exception thrown, and not caught, on a thread Bloom
        // does not control. Bloom's own reporting never sees it and the process simply vanishes.
        //
        // Measured on 2026-08-31 with a minimal .NET 8 program throwing on a thread-pool thread: the
        // ProcessExit handler did NOT run (so there is no clean-exit proof), and the process exit code was
        // 0xE0434352, alongside a .NET Runtime event, an Application Error event and a WER report. The exit
        // code comes from the CLR rather than from any Windows feature that can be switched off, which is
        // why reporting on evidence rather than on absence still catches this.
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence
            {
                ExitCode = ExitClassifier.ExitCodeUnhandledManagedException,
                CleanExitProofPresent = false,
            }
        );

        Assert.That(conclusion.ShouldReport, Is.True, "this is the case we must not miss");
        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Crashed));
        Assert.That(conclusion.Explanation, Does.Contain("unhandled managed exception"));
    }

    [Test]
    public void Nothing_is_said_when_the_proof_is_present()
    {
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence
            {
                ExitCode = 0,
                CleanExitProofPresent = true,
                ShutdownPhaseReached = BloomShutdownPhase.MessageLoopReturned,
            }
        );

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Clean));
        Assert.That(conclusion.ShouldReport, Is.False);
        Assert.That(
            conclusion.Explanation,
            Does.Contain("message loop had returned"),
            "the phase reached is worth saying, and worth saying in words"
        );
    }

    [Test]
    public void A_machine_that_went_down_is_never_blamed_on_Bloom()
    {
        // Power loss leaves no proof and kills the Doctor too, so this is found when reconciling an
        // orphaned session. Reporting it would be pure noise.
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence
            {
                ExitCode = null,
                CleanExitProofPresent = false,
                MachineWentDown = true,
                HasEventLogCrashEntry = true,
            }
        );

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.MachineWentDown));
        Assert.That(
            conclusion.ShouldReport,
            Is.False,
            "an unexpected shutdown outranks even crash evidence"
        );
    }

    [Test]
    public void A_debugged_target_is_never_reported_however_bad_it_looks()
    {
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence
            {
                ExitCode = ExitClassifier.ExitCodeFailFast,
                HasEventLogCrashEntry = true,
                HasWerReport = true,
                CleanExitProofPresent = false,
                DebuggerCouldExplainIt = true,
            }
        );

        Assert.That(
            conclusion.ShouldReport,
            Is.False,
            "a developer's debugging session must never reach the tracker, whatever the evidence says"
        );
    }

    [Test]
    public void A_developer_run_still_gets_a_report_gathered_even_though_it_is_never_filed()
    {
        // These are two separate questions, and conflating them is a real trap. Whether a report may be
        // FILED is settled by the caller; this only answers whether the exit is worth reporting ON. Answer
        // "do not report" for a developer run and the supervisor gathers nothing at all, while logging that
        // it gathered and merely declined to file — so the evidence we most want from our own machines is
        // the evidence we quietly throw away.
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = ExitClassifier.ExitCodeFailFast, NeverFile = true }
        );

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Crashed));
        Assert.That(
            conclusion.ShouldReport,
            Is.True,
            "a crash is worth gathering evidence about even when we will not file it"
        );
    }

    [Test]
    public void A_clean_exit_code_zero_is_quiet()
    {
        var conclusion = ExitClassifier.Classify(new ExitEvidence { ExitCode = 0 });

        Assert.That(conclusion.ShouldReport, Is.False);
        Assert.That(conclusion.Explanation, Does.Contain("code 0"));
    }

    [Test]
    public void An_exit_Bloom_itself_recorded_as_forced_is_reported_not_called_clean()
    {
        // Bloom writes an exit record on the way out of a hard failure too - Environment.Exit before the
        // orderly shutdown began - and marks it forced. The supervisor used to pass "there is a record"
        // as "there is proof of a clean exit", so the loudest thing Bloom can tell us became silence,
        // under the self-contradicting explanation "Bloom shut down properly (shutdown phase 0)".
        var evidence = new ExitEvidence
        {
            CleanExitProofPresent = false,
            ExitRecordedAsForced = true,
            ShutdownPhaseReached = BloomShutdownPhase.None,
        };

        var conclusion = ExitClassifier.Classify(evidence);

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.NoOrderlyShutdown));
        Assert.That(
            conclusion.ShouldReport,
            Is.True,
            "a hard failure is the whole point of the Doctor"
        );
        Assert.That(
            conclusion.Explanation,
            Does.Contain("forced").And.Contains("never began"),
            "and it should say what Bloom told us, in words, not guess at a user kill"
        );
    }

    [Test]
    public void An_orderly_exit_is_still_clean()
    {
        // The sanity check on the test above: the new branch must not swallow the ordinary case, which is
        // by far the commonest thing that happens to a watched Bloom.
        var evidence = new ExitEvidence
        {
            CleanExitProofPresent = true,
            ExitRecordedAsForced = false,
            ShutdownPhaseReached = BloomShutdownPhase.ProjectContextDisposed,
        };

        var conclusion = ExitClassifier.Classify(evidence);

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Clean));
        Assert.That(conclusion.ShouldReport, Is.False, "nobody wants a card about quitting Bloom");
    }

    [Test]
    public void A_shutdown_the_Doctor_asked_for_is_still_clean()
    {
        // The exit record's "forced" flag covers two quite different things - the Doctor asking Bloom to
        // quit, and a hard failure that never began the orderly path. Treating them alike would file a
        // card about our own request whenever the Doctor that asked and the Doctor that examined the exit
        // were different processes, which is the mistake _weAskedItToStop exists to prevent. So the test
        // is the shutdown phase: this Bloom was asked to go and shut down properly.
        var evidence = new ExitEvidence
        {
            CleanExitProofPresent = true,
            ExitRecordedAsForced = false,
            ShutdownPhaseReached = BloomShutdownPhase.ProjectContextDisposed,
        };

        var conclusion = ExitClassifier.Classify(evidence);

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Clean));
        Assert.That(
            conclusion.ShouldReport,
            Is.False,
            "Bloom doing exactly what it was asked is not a bug report"
        );
    }
}
