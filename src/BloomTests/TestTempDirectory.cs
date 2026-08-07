using System;
using System.IO;
using BloomTemp;
using NUnit.Framework;
using NUnit.Framework.Interfaces;

/// <summary>
/// Gives this test process a temporary directory of its own, so that two test runs on the same
/// machine cannot tread on each other's scratch folders.
///
/// Our tests name their temp folders after themselves — there are around 180 calls of the form
/// `new TemporaryFolder("SomeFixtureName")` — and those names resolve to machine-global paths.
/// That was harmless when one person ran the suite at a time. It is not harmless now that agents
/// work in several worktrees at once, because TemporaryFolder's constructor *deletes* any
/// existing folder of the name before creating it (see TemporaryFolder in
/// src/BloomExe/TempFiles.cs). So one run's setup would quietly delete another run's in-flight
/// folder, and the victim would fail somewhere unrelated, naming a folder it had never heard of.
/// See BL-16664, and BL-16661 for the same shape of failure from a different cause.
///
/// Rather than rename 180 call sites — which would still leave Bloom's own production code
/// writing to the shared temp directory while under test — we move the whole process's idea of
/// where "temp" is. Every existing call site then keeps its familiar name, but the name is scoped
/// to this run.
/// </summary>
/// <remarks>
/// This deliberately sits in the global namespace. An NUnit [SetUpFixture] applies to the
/// namespace it is declared in and the namespaces beneath it; nearly all our tests are under
/// BloomTests, but not quite all (there is a fixture in the Bloom.Api namespace), and the
/// redirect has to be in place before *any* fixture runs. The global namespace covers the whole
/// assembly and is ordered ahead of the BloomTests one.
/// </remarks>
[SetUpFixture]
public class TestTempDirectory
{
    /// <summary>Everything this assembly writes to temp goes under here, a folder per run.</summary>
    private const string kContainerName = "BloomTests";

    /// <summary>How long a leftover run folder must sit untouched before another run clears it.</summary>
    private static readonly TimeSpan kStaleAfter = TimeSpan.FromDays(1);

    private static string _runFolder;

    /// <summary>
    /// The machine-wide temp directory, as it was before we redirected. Kept so the tests for
    /// this class can check that we really did move somewhere else.
    /// </summary>
    internal static string MachineTempFolder { get; private set; }

    /// <summary>The folder this run's temporary files live in.</summary>
    internal static string RunFolder => _runFolder;

    /// <summary>
    /// Points this process's temp directory at a folder of our own, before any fixture runs, and
    /// takes the opportunity to clear out folders left by runs that died. NUnit calls this once.
    /// </summary>
    [OneTimeSetUp]
    public void RedirectTempToAFolderOfOurOwn()
    {
        MachineTempFolder = Path.GetTempPath();
        var container = Path.Combine(MachineTempFolder, kContainerName);
        _runFolder = Path.Combine(container, KeyForThisRun());
        Directory.CreateDirectory(_runFolder);

        // Path.GetTempPath() is defined in terms of these, so from this point on every temp path
        // the process computes — ours and Bloom's own — lands inside _runFolder.
        Environment.SetEnvironmentVariable("TMP", _runFolder);
        Environment.SetEnvironmentVariable("TEMP", _runFolder);

        RemoveFoldersLeftByRunsThatDiedBeforeCleaningUp(container);
    }

    /// <summary>
    /// Deletes this run's temp folder — and with it everything the run put in temp, since every
    /// temp path the process computed descends from it. Kept instead of deleted when tests
    /// failed, so their files can be examined. NUnit calls this once, at the end of the run.
    /// </summary>
    [OneTimeTearDown]
    public void RemoveOurTempFolderUnlessSomethingFailed()
    {
        // Point temp back at the machine's own folder before we go. Anything that runs after
        // this -- NUnit's own shutdown, a background thread that outlives the tests -- would
        // otherwise compute temp paths inside a directory we are about to delete, and fail
        // confusingly at the very end of an otherwise good run.
        Environment.SetEnvironmentVariable("TMP", MachineTempFolder);
        Environment.SetEnvironmentVariable("TEMP", MachineTempFolder);

        // When tests failed, leave the folder alone. What a failing test wrote is often the
        // evidence you need, and this suite's nastiest bugs have been about temp folders
        // appearing and disappearing — deleting the scene of the crime would be perverse.
        // Whatever we leave behind is cleared by a later run once it goes stale.
        if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
        {
            // Standard error, because it is the only channel `dotnet test` shows at its default
            // verbosity; Console.Out, TestContext.Out and TestContext.Progress are all swallowed.
            Console.Error.WriteLine(
                $"Tests failed, so their temporary files have been left in {_runFolder}"
            );
            return;
        }

        // Failing silently is deliberate: a file some test left open must not turn a green run
        // red at the very last moment.
        TemporaryFolder.DeleteFolderThatMayBeInUseAndIfNotFailSilently(_runFolder);

        // But it should not be *silent* silent. If something is still holding a file, that is
        // worth knowing: it usually means a test finished without disposing something, which is
        // a small bug of its own and can make later runs behave oddly.
        var whatIsLeft = DescribeWhyFolderCouldNotBeDeleted(_runFolder);
        if (whatIsLeft != null)
        {
            Console.Error.WriteLine(
                $"WARNING: could not delete this test run's temp folder, {_runFolder}. "
                    + "Something in the run probably did not release a file it opened. "
                    + whatIsLeft
            );
        }
    }

    /// <summary>
    /// Describes what is still sitting in a folder we tried and failed to delete, naming one item
    /// that will not open and the reason the operating system gave for it. Returns null when the
    /// folder did in fact go, so the caller can use it as "is there anything to complain about?".
    /// </summary>
    internal static string DescribeWhyFolderCouldNotBeDeleted(string folder)
    {
        if (!Directory.Exists(folder))
            return null;

        string[] files;
        try
        {
            files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
        }
        catch (Exception e)
        {
            return $"Its contents could not even be listed: {e.Message}";
        }

        // Whatever refuses to open exclusively is almost always the thing holding the folder, so
        // report the first one of those along with what the OS said about it.
        foreach (var file in files)
        {
            try
            {
                using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            catch (Exception e)
            {
                return $"{files.Length} file(s) remain; at least this one is still in use: {file} -- {e.Message}";
            }
        }

        if (files.Length > 0)
        {
            return $"{files.Length} file(s) remain, though each can be opened now, so whatever held them may have just let go. First of them: {files[0]}";
        }

        return "It contains no files, so something may be holding the folder itself -- a process "
            + "using it as its current directory, for instance.";
    }

    /// <summary>
    /// A name for this run's folder: unique, so no two runs can ever share one, but still
    /// recognizable, so you can tell which terminal a leftover folder came from.
    /// </summary>
    private static string KeyForThisRun()
    {
        // build/agent-dotnet.sh gives each terminal its own build tree at output/agent/<key> and
        // passes the path down in this variable, which the test host inherits. Naming ourselves
        // after the same key means a temp folder can be matched to the build tree beside it.
        // It is truncated because every temp path in the run grows by whatever we choose here,
        // and the key is normally a 36-character session id.
        var key = "";
        var buildDir = Environment.GetEnvironmentVariable("BLOOM_AGENT_BUILD_DIR");
        if (!string.IsNullOrEmpty(buildDir))
        {
            var name = new DirectoryInfo(buildDir.TrimEnd('/', '\\')).Name;
            key = name.Length > 8 ? name.Substring(0, 8) : name;
        }

        // The process id is what actually guarantees uniqueness. The key above is only a label,
        // and two terminals could in principle share its first eight characters.
        return string.IsNullOrEmpty(key)
            ? $"p{Environment.ProcessId}"
            : $"{key}-p{Environment.ProcessId}";
    }

    /// <summary>
    /// Clear out run folders old enough that nothing can still be using them. Runs that crash,
    /// or that fail and so deliberately keep their files, would otherwise accumulate forever.
    /// </summary>
    private static void RemoveFoldersLeftByRunsThatDiedBeforeCleaningUp(string container)
    {
        try
        {
            foreach (var folder in Directory.GetDirectories(container))
            {
                if (folder == _runFolder)
                    continue;
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) < kStaleAfter)
                    continue;
                TemporaryFolder.DeleteFolderThatMayBeInUseAndIfNotFailSilently(folder);
            }
        }
        catch (Exception)
        {
            // Housekeeping only. If we cannot read the container — another run is busy in it,
            // a permission oddity — that is no reason to stop the test run before it starts.
        }
    }
}
