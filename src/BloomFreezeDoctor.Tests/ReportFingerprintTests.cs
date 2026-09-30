using BloomFreezeDoctor;
using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// That two different crashes get two different fingerprints, and so two different cards.
///
/// A fingerprint whose only distinguishing ingredient is the top of the UI thread's stack is exactly right
/// for a freeze, where that stack IS the problem, and worthless for a crash, where the fault is on another
/// thread and the UI thread is sitting in its message pump looking identical every time: every crash on a
/// build would pile onto one card as "This happened again".
/// </summary>
[TestFixture]
public class ReportFingerprintTests
{
    /// <summary>
    /// A crash report's context. The UI-thread stack is deliberately IDENTICAL between the two crashes
    /// below - that is the whole point: a fingerprint built from it alone cannot tell them apart.
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
    public void For_TwoCrashesWithDifferentIdentities_DifferentFingerprints()
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
    public void For_TwoCrashesWithoutIdentity_SameFingerprint()
    {
        // The sanity check that gives the test above its meaning. Same two contexts, same stacks, no crash
        // identity supplied - and the fingerprints are equal. An executable record of why the identity is
        // needed rather than a claim in a comment.
        var context = CrashContext();

        Assert.That(
            ReportFingerprint.For(context, TheSameIdleUiThread),
            Is.EqualTo(ReportFingerprint.For(context, TheSameIdleUiThread)),
            "with nothing to tell them apart, every crash on this build is one problem"
        );
    }

    [Test]
    public void For_SameCrashTwice_SameFingerprint()
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
    public void For_FreezesWithDifferentUiStacks_DifferentFingerprints()
    {
        // Freezes must be untouched by this: their identity is the UI thread's stack, and passing no
        // identity must still tell them apart by it.
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
