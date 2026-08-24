using BloomFreezeDoctor;
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
    private const ExitReportPolicy Phase1 = ExitReportPolicy.RequiresCorroboratingEvidence;
    private const ExitReportPolicy Phase3 = ExitReportPolicy.RequiresProofOfCleanExit;

    [Test]
    public void Phase1_stays_quiet_about_a_bare_exit()
    {
        // The whole point of the Phase 1 rule: a Bloom that simply vanished is indistinguishable from
        // the user closing it in a way we could not see.
        var conclusion = ExitClassifier.Classify(new ExitEvidence { ExitCode = 1 }, Phase1);

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
    public void A_crash_exit_code_is_reportable_even_in_Phase1(int exitCode, string expectedInText)
    {
        var conclusion = ExitClassifier.Classify(new ExitEvidence { ExitCode = exitCode }, Phase1);

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Crashed));
        Assert.That(conclusion.ShouldReport, Is.True);
        Assert.That(conclusion.Explanation, Does.Contain(expectedInText));
    }

    [Test]
    public void Event_log_or_WER_evidence_is_enough_on_its_own()
    {
        var fromEventLog = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = 1, HasEventLogCrashEntry = true },
            Phase1
        );
        var fromWer = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = 1, HasWerReport = true },
            Phase1
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
            new ExitEvidence { ExitCode = 1, LogShowsForcedShutdown = true },
            Phase1
        );

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.ForcedAfterStalledShutdown));
        Assert.That(conclusion.ShouldReport, Is.True, "reportable in either regime");
        Assert.That(conclusion.Explanation, Does.Contain("stalled"));
    }

    [Test]
    public void Phase3_reports_a_missing_proof_but_labels_it_honestly()
    {
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = -1, CleanExitProofPresent = false },
            Phase3
        );

        Assert.That(conclusion.ShouldReport, Is.True);
        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.NoOrderlyShutdown));
        Assert.That(
            conclusion.Explanation,
            Does.Contain("may be a user-initiated kill"),
            "it must not dress a kill up as a crash"
        );
    }

    [Test]
    public void Phase3_says_nothing_when_the_proof_is_present()
    {
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence
            {
                ExitCode = 0,
                CleanExitProofPresent = true,
                ShutdownPhaseReached = 1,
            },
            Phase3
        );

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Clean));
        Assert.That(conclusion.ShouldReport, Is.False);
        Assert.That(
            conclusion.Explanation,
            Does.Contain("phase 1"),
            "the phase reached is worth saying"
        );
    }

    [Test]
    public void A_missing_proof_in_Phase1_is_not_treated_as_evidence()
    {
        // The same evidence, judged under the two regimes, must reach opposite conclusions. This is
        // the distinction the phasing rests on: in Phase 1 no proof mechanism existed, so its absence
        // means nothing at all.
        var evidence = new ExitEvidence { ExitCode = -1, CleanExitProofPresent = false };

        var phase1 = ExitClassifier.Classify(evidence, Phase1);
        var phase3 = ExitClassifier.Classify(evidence, Phase3);

        Assert.That(
            phase1.ShouldReport,
            Is.False,
            "Phase 1 cannot read anything into a missing proof"
        );
        Assert.That(phase3.ShouldReport, Is.True, "Phase 3 can, because Bloom would have left one");
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
            },
            Phase3
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
            },
            Phase3
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
        // These are two separate questions, and conflating them was a real bug: this classifier used to
        // answer "do not report" for a developer run, so the supervisor gathered nothing at all — while
        // logging that it had gathered and merely declined to file. The freeze path had always got this
        // right, gathering to disk and refusing to file, and now both paths agree. Whether a report may be
        // FILED is settled by the caller; this only answers whether the exit is worth reporting on.
        var conclusion = ExitClassifier.Classify(
            new ExitEvidence { ExitCode = ExitClassifier.ExitCodeFailFast, NeverFile = true },
            Phase1
        );

        Assert.That(conclusion.Verdict, Is.EqualTo(ExitVerdict.Crashed));
        Assert.That(
            conclusion.ShouldReport,
            Is.True,
            "a crash is worth gathering evidence about even when we will not file it"
        );
    }

    [Test]
    public void A_clean_exit_code_zero_is_quiet_in_Phase1()
    {
        var conclusion = ExitClassifier.Classify(new ExitEvidence { ExitCode = 0 }, Phase1);

        Assert.That(conclusion.ShouldReport, Is.False);
        Assert.That(conclusion.Explanation, Does.Contain("code 0"));
    }
}
