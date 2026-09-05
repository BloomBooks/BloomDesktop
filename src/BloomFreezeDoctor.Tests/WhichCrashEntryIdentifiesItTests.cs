using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Choosing, among the Event Log entries Windows writes for one crash, the one that says WHICH crash.
///
/// This is the bug that made the crash-fingerprint work do nothing at all, and it is worth stating why it
/// escaped: taking the first entry that names Bloom looks obviously correct, the code read as if it were,
/// and on a real machine it produced a null identity every single time. Only the data showed it - three
/// entries for one crash, and the useful one is not the first.
/// </summary>
[TestFixture]
public class WhichCrashEntryIdentifiesItTests
{
    /// <summary>The 1000 entry: names Bloom, so it matches - and carries no exception.</summary>
    private const string ApplicationError =
        "Faulting application name: Bloom.exe, version: 6.5.0.0, time stamp: 0x6a5a0000\r\n"
        + "Faulting module name: KERNELBASE.dll\r\nException code: 0xe0434352\r\n";

    /// <summary>The 1026 entry: the only one that says what actually went wrong.</summary>
    private const string DotNetRuntime =
        "Application: Bloom.exe\r\n"
        + "Description: The process was terminated due to an unhandled exception.\r\n"
        + "Exception Info: System.ApplicationException: something went wrong\r\n"
        + "   at Bloom.FreezeDoctor.FreezeSimulator.Crash() in C:\\x\\y.cs:line 243\r\n";

    [Test]
    public void The_real_entry_order_yields_the_identity()
    {
        // Newest first, exactly as measured on this machine at 15:18:55 - the Application Error entry comes
        // before the .NET Runtime one. Stopping at the first match is what was wrong.
        var picked = WindowsExitEvidenceCollector.PickTheCrashThatIdentifiesItself(
            new[] { ApplicationError, DotNetRuntime }
        );

        Assert.That(picked.Found, Is.True, "either entry proves Windows logged a crash");
        Assert.That(
            picked.Signature,
            Is.Not.Null,
            "and the walk must go past the entry that cannot identify the fault to the one that can"
        );
        Assert.That(picked.Signature, Does.Contain("System.ApplicationException"));
        Assert.That(picked.Signature, Does.Contain("FreezeSimulator.Crash()"));
    }

    [Test]
    public void An_unidentifiable_crash_is_still_a_crash()
    {
        // Environment.FailFast and an access violation produce a 1000 entry and no managed one. That must
        // still count as evidence of a crash - it is what the classifier reports on - and simply leave the
        // fingerprint to fall back on what it used before.
        var picked = WindowsExitEvidenceCollector.PickTheCrashThatIdentifiesItself(
            new[] { ApplicationError }
        );

        Assert.That(picked.Found, Is.True);
        Assert.That(picked.Signature, Is.Null);
    }

    [Test]
    public void No_entries_means_no_crash_and_no_identity()
    {
        var picked = WindowsExitEvidenceCollector.PickTheCrashThatIdentifiesItself(
            Array.Empty<string>()
        );

        Assert.That(picked.Found, Is.False);
        Assert.That(picked.Signature, Is.Null);
    }

    [Test]
    public void It_takes_the_newest_identifiable_crash_not_a_later_one()
    {
        // Two managed crashes inside the five-minute window - a machine crashing repeatedly. The newest is
        // the one this death is about; an older one would identify the wrong fault.
        var older = DotNetRuntime.Replace(
            "System.ApplicationException: something went wrong",
            "System.NullReferenceException: an earlier and different fault"
        );

        var picked = WindowsExitEvidenceCollector.PickTheCrashThatIdentifiesItself(
            new[] { ApplicationError, DotNetRuntime, older }
        );

        Assert.That(picked.Signature, Does.Contain("System.ApplicationException"));
        Assert.That(picked.Signature, Does.Not.Contain("System.NullReferenceException"));
    }
}
