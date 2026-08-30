using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The exe name the supervisor hands to the exit-evidence collector.
///
/// A tiny function, tested because its two "nothing here" answers are not interchangeable and the
/// difference is invisible at the call site. The Event Log reader treats null as "I do not know which
/// executable, so accept any of the channel names the installer produces". An empty string would go into
/// the same comparison and match *every* message ever written - so a crash report could be assembled from
/// somebody else's crash, and nothing about it would look wrong.
///
/// Reachable at all only because DoctorSupervisor now lives in Core; while it was in the WinExe this could
/// not be tested without running the Doctor.
/// </summary>
[TestFixture]
public class ExeFileNameTests
{
    [Test]
    public void A_normal_path_gives_the_file_name()
    {
        Assert.That(
            DoctorSupervisor.SafeFileName(
                @"C:\Users\jt\AppData\Local\BloomBeta\current\BloomBeta.exe"
            ),
            Is.EqualTo("BloomBeta.exe")
        );
    }

    [TestCase(null, Description = "we never learned the path")]
    [TestCase("", Description = "an empty path")]
    [TestCase("   ", Description = "whitespace, which Path.GetFileName happily returns as-is")]
    [TestCase(@"C:\some\folder\", Description = "a directory, whose file name is empty")]
    public void Anything_without_a_usable_name_comes_back_null_never_empty(string? path)
    {
        // Null and "" travel the same route into the Event Log query, where they mean opposite things.
        // Returning "" from any of these would silently widen the search to every logged crash.
        Assert.That(
            DoctorSupervisor.SafeFileName(path),
            Is.Null,
            "an empty answer must be expressed as null, never as an empty string"
        );
    }

    [Test]
    public void A_malformed_path_does_not_throw()
    {
        // Gathering evidence about a dead Bloom must not fall over because its path was odd.
        //
        // Note what this does NOT assert. The obvious guess - that a path containing a null character
        // comes back null, via the catch - is wrong: .NET Core dropped the invalid-character checks that
        // made Path.GetFileName throw on desktop .NET, so it simply returns the last segment. The catch is
        // therefore all but unreachable, and is left as a belt rather than removed, since this runs while
        // examining a process that has just died and there is no report to be had if it throws.
        //
        // Returning a name here is harmless anyway: a name that matches nothing narrows the Event Log
        // search to nothing, which is the safe direction. Widening it is what null-versus-empty guards.
        Assert.That(
            () => DoctorSupervisor.SafeFileName("C:\\bad\0path\\x.exe"),
            Throws.Nothing,
            "an odd path must not cost us the whole report"
        );
    }
}
