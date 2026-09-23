using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Reading "which crash was this" out of the .NET Runtime event.
///
/// The sample below is a REAL event, copied verbatim, rather than one written to suit the parser. A
/// fixture that invents its input tests only the author's idea of the format.
/// </summary>
[TestFixture]
public class CrashSignatureTests
{
    private const string RealEvent =
        "Application: Bloom.exe\r\n"
        + "CoreCLR Version: 8.0.3026.36720\r\n"
        + ".NET Version: 8.0.30\r\n"
        + "Description: The process was terminated due to an unhandled exception.\r\n"
        + "Exception Info: System.ApplicationException: FreezeSimulator was asked to crash a background thread\r\n"
        + "   at Bloom.FreezeDoctor.FreezeSimulator.<>c.<Simulate>b__8_0() in C:\\github\\BloomDesktop\\src\\BloomExe\\FreezeDoctor\\FreezeSimulator.cs:line 243\r\n";

    [Test]
    public void FromEventLogMessage_RealEvent_ContainsExceptionTypeAndFaultingFrame()
    {
        var signature = CrashSignature.FromEventLogMessage(RealEvent);

        Assert.That(signature, Does.StartWith("System.ApplicationException"));
        Assert.That(
            signature,
            Does.Contain("Bloom.FreezeDoctor.FreezeSimulator"),
            "the faulting method is the whole point: it is what distinguishes one crash from another"
        );
    }

    [Test]
    public void FromEventLogMessage_RealEvent_OmitsFileAndLine()
    {
        // A path and line number change whenever anyone edits the file above the fault, so keeping them
        // would give the same crash a new identity after any rebuild - and a new card with it.
        var signature = CrashSignature.FromEventLogMessage(RealEvent);

        Assert.That(signature, Does.Not.Contain("line 243"));
        Assert.That(signature, Does.Not.Contain("FreezeSimulator.cs"));
        Assert.That(signature, Does.Not.Contain("C:\\"));
    }

    [Test]
    public void FromEventLogMessage_RealEvent_OmitsExceptionMessage()
    {
        // Messages carry file paths, book names and ids. Including them would give one fault a different
        // identity on every machine, which is the opposite of what a fingerprint is for.
        var signature = CrashSignature.FromEventLogMessage(RealEvent);

        Assert.That(signature, Does.Not.Contain("asked to crash a background thread"));
    }

    [Test]
    public void FromEventLogMessage_DifferentFaults_DifferentSignatures()
    {
        // The property that matters: unrelated crashes must not collapse onto one card.
        var other = RealEvent
            .Replace("System.ApplicationException", "System.NullReferenceException")
            .Replace(
                "Bloom.FreezeDoctor.FreezeSimulator.<>c.<Simulate>b__8_0()",
                "Bloom.Book.Book.Save()"
            );

        var a = CrashSignature.FromEventLogMessage(RealEvent);
        var b = CrashSignature.FromEventLogMessage(other);

        Assert.That(a, Is.Not.Null, "setup: the real event must parse");
        Assert.That(b, Is.Not.Null, "setup: the altered event must parse too");
        Assert.That(a, Is.Not.EqualTo(b));
    }

    [Test]
    public void FromEventLogMessage_SameFaultDifferentLine_SameSignature()
    {
        // And the other half: the same crash on two runs must still be one card. Only the line number
        // differs here, standing in for somebody having edited the file in between.
        var afterAnEdit = RealEvent.Replace(":line 243", ":line 251");

        Assert.That(
            CrashSignature.FromEventLogMessage(afterAnEdit),
            Is.EqualTo(CrashSignature.FromEventLogMessage(RealEvent))
        );
    }

    [Test]
    public void FromEventLogMessage_DeepStack_KeepsTopFramesOnly()
    {
        var deep = "Exception Info: System.InvalidOperationException: something\r\n";
        for (var i = 0; i < 20; i++)
            deep += $"   at Bloom.Layer{i}.Method()\r\n";

        var signature = CrashSignature.FromEventLogMessage(deep);

        Assert.That(signature, Does.Contain("Bloom.Layer0.Method()"));
        Assert.That(
            signature,
            Does.Not.Contain("Bloom.Layer9.Method()"),
            "a whole stack is too much: deep frames are shared by unrelated faults and shift on refactors"
        );
    }

    [Test]
    public void FromEventLogMessage_NotAManagedCrash_ReturnsNull()
    {
        // Not every entry naming Bloom is a managed crash - an Application Error 1000 has a quite
        // different shape - and the caller must be able to tell, so it can keep the identity it had.
        Assert.That(CrashSignature.FromEventLogMessage(null), Is.Null);
        Assert.That(CrashSignature.FromEventLogMessage(""), Is.Null);
        Assert.That(
            CrashSignature.FromEventLogMessage(
                "Faulting application name: Bloom.exe, version: 6.5.0.0\r\n"
                    + "Faulting module name: ntdll.dll\r\nException code: 0xc0000005\r\n"
            ),
            Is.Null
        );
    }
}
