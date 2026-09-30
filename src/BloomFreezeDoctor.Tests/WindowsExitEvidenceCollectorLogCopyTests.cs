using NUnit.Framework;
using SIL.IO;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Pins the one thing about attaching Bloom's log that is easy to get wrong and fails silently: the
/// process being diagnosed is holding it open for writing the whole time.
///
/// **This is the main case, not an edge case.** For a freeze, Bloom is by definition still alive and still
/// holding its log, so a copy that cannot cope with that could only ever work for a Bloom that had already
/// exited. And the failure is silent: the copy throws, the report simply has no log attached, and the card
/// claims both to show "the whole log" and to have failed to attach it.
/// </summary>
[TestFixture]
public class WindowsExitEvidenceCollectorLogCopyTests
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
    public void CopyWhileInUse_LogHeldOpenForWriting_Copies(FileShare heldWith)
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
    /// The reason the copy above cannot simply be <c>RobustFile.Copy</c>, recorded as a test because it is
    /// easy to assume otherwise: <c>File.Copy</c> tolerates a writer, and the retrying wrapper looks as if
    /// it would too.
    ///
    /// If this ever starts failing - libpalaso loosening its sharing, say - then the production code can go
    /// back to being one call, and this test is how you find out.
    /// </summary>
    [Test]
    public void RobustFileCopy_LogHeldOpenForWriting_Throws()
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
