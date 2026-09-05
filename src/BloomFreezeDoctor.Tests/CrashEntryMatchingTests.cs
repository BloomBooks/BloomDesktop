using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Deciding whether a Windows Event Log entry is about *our* Bloom. This has no happy path worth
/// celebrating and one failure mode that matters: a match on somebody else's crash puts "Bloom crashed"
/// on a card, with Windows apparently agreeing, about an event Bloom had nothing to do with. The
/// messages below are shaped like the real ones.
/// </summary>
[TestFixture]
public class CrashEntryMatchingTests
{
    /// <summary>An "Application Error" entry as Windows actually writes it, for a chosen program.</summary>
    private static string ApplicationError(string exe, string pidHex) =>
        $"Faulting application name: {exe}, version: 6.3.2.0, time stamp: 0x64b1c2d3\r\n"
        + "Faulting module name: KERNELBASE.dll, version: 10.0.26200.1, time stamp: 0x9a0b1c2d\r\n"
        + "Exception code: 0xe0434352\r\n"
        + "Fault offset: 0x00007ff81000a4c0\r\n"
        + $"Faulting process id: 0x{pidHex}\r\n"
        + "Faulting application start time: 0x01dc0f1e2d3c4b5a";

    [Test]
    public void A_pid_that_merely_appears_inside_a_hex_address_does_not_match()
    {
        // The bug this test exists for. The old check looked for the bare hex pid anywhere in the
        // message, undelimited. Every Application Error entry is full of hex - exception codes, fault
        // offsets, module timestamps - so a short pid was near-certain to turn up inside one of them.
        // Pid 4096 is "1000", which is sitting in the fault offset 0x00007ff81000a4c0 below.
        var someoneElsesCrash = ApplicationError("Notepad.exe", "3b7f");
        Assert.That(
            someoneElsesCrash,
            Does.Contain("1000"),
            "setup: the message really does contain this pid's digits, which is the trap"
        );

        Assert.That(
            WindowsExitEvidenceCollector.EntryNamesThisBloom(someoneElsesCrash, 4096, "Bloom.exe"),
            Is.False,
            "Notepad crashing must not be reported as Bloom crashing"
        );
    }

    [Test]
    public void A_pid_quoted_as_a_process_id_does_match()
    {
        // The sanity check on the test above: the pid clause has to still work, because ".NET Runtime"
        // entries identify the process by id and do not always name the executable.
        var ours = ApplicationError("something-we-do-not-recognise.exe", "1a2c");

        Assert.That(
            WindowsExitEvidenceCollector.EntryNamesThisBloom(ours, 0x1a2c, "Bloom.exe"),
            Is.True,
            "'Faulting process id: 0x1a2c' is Windows naming our process"
        );
    }

    [Test]
    public void A_longer_pid_starting_with_our_digits_does_not_match()
    {
        var theirs = ApplicationError("Whatever.exe", "1a2cf");

        Assert.That(
            WindowsExitEvidenceCollector.EntryNamesThisBloom(theirs, 0x1a2c, "Bloom.exe"),
            Is.False,
            "0x1a2cf is a different process from 0x1a2c"
        );
    }

    [Test]
    public void Another_programs_WebView2_crash_does_not_match()
    {
        // Bloom is far from the only WebView2 host on a Windows machine, and the old check accepted
        // "msedgewebview2.exe" unqualified - so Teams or Outlook losing a renderer within five minutes
        // became evidence that Bloom had crashed.
        var teamsRenderer = ApplicationError("msedgewebview2.exe", "5ce1");

        Assert.That(
            WindowsExitEvidenceCollector.EntryNamesThisBloom(teamsRenderer, 0x1a2c, "Bloom.exe"),
            Is.False,
            "somebody else's renderer is not our crash - and the pid in such an entry is the "
                + "renderer's own, so it could never have distinguished ours from theirs"
        );
    }

    [Test]
    public void The_channel_named_executable_matches()
    {
        // The installer renames the exe per channel, so a release machine has Bloom.exe but an alpha has
        // BloomAlpha.exe. Matching the literal "Bloom.exe" found neither of the renamed ones - which
        // quietly disabled this evidence on every channel except release.
        var alpha = ApplicationError("BloomAlpha.exe", "9f01");

        Assert.That(
            WindowsExitEvidenceCollector.EntryNamesThisBloom(alpha, 0x1a2c, "BloomAlpha.exe"),
            Is.True,
            "this is the exe that died"
        );
        Assert.That(
            WindowsExitEvidenceCollector.EntryNamesThisBloom(alpha, 0x1a2c, null),
            Is.True,
            "and with no name to go on, any of the installer's channel names counts"
        );
        Assert.That(
            WindowsExitEvidenceCollector.EntryNamesThisBloom(
                ApplicationError("Bloom.exe", "9f01"),
                0x1a2c,
                "BloomAlpha.exe"
            ),
            Is.False,
            "two Blooms of different channels on one machine are still two different programs"
        );
    }

    [Test]
    public void An_empty_message_matches_nothing()
    {
        Assert.That(
            WindowsExitEvidenceCollector.EntryNamesThisBloom("", 0x1a2c, "Bloom.exe"),
            Is.False
        );
    }
}
