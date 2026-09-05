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
    public void A_process_terminated_from_outside_is_quiet_without_needing_to_be_excused()
    {
        // This replaces two explicit exemptions - "the machine went down" and "a debugger could account
        // for it" - which existed only to excuse an absence back when an absence was reportable. They are
        // unnecessary now, and this is the shape of the case they used to catch.
        //
        // Measured on 2026-08-31: a TerminateProcess kill, which is exactly what "Stop Debugging" and Task
        // Manager both do, gives exit code -1, no ProcessExit, no Application Error event and no WER
        // report. A machine losing power leaves even less. So none of them reaches the crash signals at
        // all, and nothing has to recognise them by name to stay quiet.
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = -1, CleanExitProofPresent = false }
        );

        Assert.That(
            conclusion.ShouldReport,
            Is.False,
            "no evidence of failure means no card, whatever the cause was"
        );
    }

    [Test]
    public void Real_crash_evidence_is_reported_even_on_a_machine_that_has_been_debugging()
    {
        // A deliberate change of behaviour, and worth being explicit about. The classifier used to refuse
        // outright if a debugger had been near the process, however bad the evidence looked. It no longer
        // knows about debuggers at all.
        //
        // Nothing reaches the tracker that should not: a debugged Bloom is blocked from FILING one level
        // up, by BloomTargetWatcher.ReasonsFilingWouldNormallyBeBlocked - see
        // BloomTargetWatcherTests.A_target_seen_under_a_debugger_is_never_filed_even_if_it_later_looks_clean.
        // What changes is that the evidence is now gathered to disk instead of being thrown away, which is
        // the right way round: a Bloom that genuinely called FailFast is worth looking at, and the person
        // debugging it is the one best placed to.
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence
            {
                ExitCode = ExitClassifier.ExitCodeFailFast,
                HasEventLogCrashEntry = true,
                HasWerReport = true,
                CleanExitProofPresent = false,
            }
        );

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Crashed));
        Assert.That(
            conclusion.ShouldReport,
            Is.True,
            "deciding WHETHER to file is a separate question, asked elsewhere"
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
