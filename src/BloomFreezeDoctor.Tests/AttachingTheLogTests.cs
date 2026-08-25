using NUnit.Framework;
using SIL.IO;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Pins the one thing about attaching Bloom's log that is easy to get wrong and fails silently: the
/// process being diagnosed is holding it open for writing the whole time.
///
/// **This is the main case, not an edge case.** For a freeze, Bloom is by definition still alive and still
/// holding its log, so a copy that cannot cope with that could only ever have worked for a Bloom that had
/// already exited. It shipped that way and nothing noticed - the copy threw, the report simply had no log
/// attached, and a real filed card (AUT-20929) claimed both to show "the whole log" and to have failed to
/// attach it.
/// </summary>
[TestFixture]
public class AttachingTheLogTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(
            Path.GetTempPath(),
            "FreezeDoctorTests",
            "attach-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (Exception) { }
    }

    /// <summary>
    /// Holds a file the way a logger does: open for writing, letting others read.
    /// </summary>
    private static FileStream HoldForWriting(string path, FileShare share) =>
        new FileStream(path, FileMode.Open, FileAccess.Write, share);

    [TestCase(FileShare.Read)]
    [TestCase(FileShare.ReadWrite)]
    [TestCase(FileShare.ReadWrite | FileShare.Delete)]
    public void The_log_can_be_attached_while_the_frozen_bloom_holds_it_open(FileShare heldWith)
    {
        var source = Path.Combine(_root, "Log.txt");
        var destination = Path.Combine(_root, "bloom-log.txt");
        var contents = "line one" + Environment.NewLine + "line two" + Environment.NewLine;
        File.WriteAllText(source, contents);

        using var heldByBloom = HoldForWriting(source, heldWith);

        WindowsExitEvidenceCollector.CopyWhileInUse(source, destination);

        Assert.That(File.Exists(destination), Is.True, "the copy should have been made");
        Assert.That(
            File.ReadAllText(destination),
            Is.EqualTo(contents),
            "the copy should hold everything the log held"
        );
    }

    /// <summary>
    /// The reason the copy above cannot simply be <c>RobustFile.Copy</c>, recorded as a test because
    /// measuring the wrong function is how this was got wrong once already: <c>File.Copy</c> tolerates a
    /// writer, which made the retrying wrapper look like it would too.
    ///
    /// If this ever starts failing - libpalaso loosening its sharing, say - then the production code can go
    /// back to being one call, and this test is how you find out.
    /// </summary>
    [Test]
    public void RobustFileCopy_is_refused_by_a_log_its_owner_still_holds()
    {
        var source = Path.Combine(_root, "Log.txt");
        var destination = Path.Combine(_root, "robust-copy.txt");
        File.WriteAllText(source, "whatever");

        using var heldByBloom = HoldForWriting(source, FileShare.ReadWrite);

        Assert.Throws<IOException>(
            () => RobustFile.Copy(source, destination, overwrite: true),
            "if this no longer throws, CopyWhileInUse has become unnecessary"
        );
    }
}
