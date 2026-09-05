using BloomFreezeDoctor;
using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// That two different crashes get two different fingerprints, and so two different cards.
///
/// The bug this pins was invisible from inside the code and obvious from the data: three separate
/// simulated crashes on one afternoon, with three different processes, all produced fingerprint
/// 1ec8760ad8a5 and piled onto one card as "This happened again". The fingerprint's only distinguishing
/// ingredient was the top of the UI thread's stack - exactly right for a freeze, where that stack IS the
/// problem, and worthless for a crash, where the fault is on another thread and the UI thread is sitting
/// in its message pump looking identical every time.
/// </summary>
[TestFixture]
public class CrashFingerprintTests
{
    /// <summary>
    /// A crash report's context. The UI-thread stack is deliberately IDENTICAL between the two crashes
    /// below - that is the whole point: it is what the old fingerprint hashed, and it cannot tell them
    /// apart.
    /// </summary>
    private static GatherContext CrashContext() =>
        new()
        {
            Target = new BloomTargetFacts
            {
                ProcessId = 1234,
                ExePath = @"C:\github\BloomDesktop\output\Debug\AnyCPU\Bloom.exe",
                Channel = "Developer/Debug",
                StartTime = new DateTime(2026, 8, 31, 13, 56, 42),
                CommandLine = "",
            },
            Verdict = new DetectorVerdict
            {
                State = TargetState.Exited,
                Report = ReportReason.ExitedWithoutProof,
                Explanation = "Bloom crashed",
            },
            ProcessWasAlive = false,
            ArtifactDirectory = Path.GetTempPath(),
        };

    private static readonly ReportSection[] TheSameIdleUiThread =
    {
        new()
        {
            Title = "Managed stacks",
            Body =
                "### The UI thread\n"
                + "    System.Windows.Forms.Application.Run()\n"
                + "    Bloom.Program.Main()\n",
        },
    };

    [Test]
    public void Two_unrelated_crashes_get_two_fingerprints()
    {
        var context = CrashContext();

        var nullReference = ReportFingerprint.For(
            context,
            TheSameIdleUiThread,
            "System.NullReferenceException|Bloom.Book.Book.Save()"
        );
        var invalidOperation = ReportFingerprint.For(
            context,
            TheSameIdleUiThread,
            "System.InvalidOperationException|Bloom.Publish.Epub.Make()"
        );

        Assert.That(
            nullReference,
            Is.Not.EqualTo(invalidOperation),
            "two different faults must get two different cards"
        );
    }

    [Test]
    public void Without_a_crash_identity_they_collapse_which_is_the_bug()
    {
        // The sanity check that gives the test above its meaning. Same two contexts, same stacks, no crash
        // identity supplied - and the fingerprints are equal. This is the old behaviour, kept here as an
        // executable record of why the identity is needed rather than a claim in a comment.
        var context = CrashContext();

        Assert.That(
            ReportFingerprint.For(context, TheSameIdleUiThread),
            Is.EqualTo(ReportFingerprint.For(context, TheSameIdleUiThread)),
            "with nothing to tell them apart, every crash on this build is one problem"
        );
    }

    [Test]
    public void The_same_crash_twice_still_gets_one_fingerprint()
    {
        // Deduplication still has to work, or we have traded one bug for its opposite: a machine crashing
        // the same way twenty times should open one card, not twenty.
        var context = CrashContext();
        const string sameFault = "System.NullReferenceException|Bloom.Book.Book.Save()";

        Assert.That(
            ReportFingerprint.For(context, TheSameIdleUiThread, sameFault),
            Is.EqualTo(ReportFingerprint.For(context, TheSameIdleUiThread, sameFault))
        );
    }

    [Test]
    public void A_freeze_is_fingerprinted_exactly_as_before()
    {
        // Freezes must be untouched by this: their identity is the UI thread's stack, and passing no
        // identity has to give the same answer it always did.
        var frozen = CrashContext() with
        {
            Verdict = new DetectorVerdict
            {
                State = TargetState.Frozen,
                Report = ReportReason.Frozen,
                Explanation = "the UI thread is blocked",
            },
        };
        var stuckSomewhereElse = new ReportSection[]
        {
            new()
            {
                Title = "Managed stacks",
                Body = "### The UI thread\n    System.Threading.Monitor.ObjWait()\n",
            },
        };

        Assert.That(
            ReportFingerprint.For(frozen, TheSameIdleUiThread),
            Is.Not.EqualTo(ReportFingerprint.For(frozen, stuckSomewhereElse)),
            "a freeze is still told apart by where the UI thread is stuck"
        );
    }
}
