using BloomFreezeDoctor;
using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// A report about a Bloom that had gone by the time it was gathered still carries the dump taken when the
/// freeze began (BL-17092). See <see cref="EarlyFreezeDump"/>.
/// </summary>
[TestFixture]
public class ManagedStacksCollectorEarlyDumpTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() =>
        _root = Path.Combine(
            Path.GetTempPath(),
            "ManagedStacksCollectorEarlyDumpTests-" + Guid.NewGuid().ToString("N")
        );

    [TearDown]
    public void TearDown()
    {
        // No GC first: if failing to read the fake dump left it open, this throws, as it should.
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private GatherContext Context(bool processWasAlive, Task<EarlyFreezeDump.Taken?>? earlyDump) =>
        new()
        {
            Target = new BloomTargetFacts
            {
                ProcessId = 4242,
                ExePath = @"C:\Users\jt\AppData\Local\Bloom\current\Bloom.exe",
                Channel = "Release",
                CommandLine = "\"Bloom.exe\"",
                StartTime = new DateTime(2026, 11, 8, 15, 34, 31, DateTimeKind.Local),
            },
            Verdict = new DetectorVerdict
            {
                State = TargetState.Exited,
                Report = ReportReason.DiedWhileFrozen,
                Explanation = "exited while frozen",
            },
            ProcessWasAlive = processWasAlive,
            ArtifactDirectory = Path.Combine(_root, "gather"),
            EarlyDump = earlyDump,
        };

    /// <summary>
    /// An early dump on disk. Not a real dump, so its stacks cannot be read - which also proves the dump is
    /// kept when they cannot.
    /// </summary>
    private EarlyFreezeDump.Taken AnEarlyDump()
    {
        var path = Path.Combine(_root, "early-4242-x", "bloom-4242.dmp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not really a dump");
        return new EarlyFreezeDump.Taken(path, DateTimeOffset.UtcNow.AddSeconds(-40));
    }

    [Test]
    public async Task CollectAsync_GoneAndTheEarlyDumpFailed_SaysSo()
    {
        var context = Context(
            processWasAlive: false,
            earlyDump: Task.FromResult<EarlyFreezeDump.Taken?>(null)
        );

        var section = await new ManagedStacksCollector().CollectAsync(
            context,
            CancellationToken.None
        );

        Assert.That(section.Artifacts, Is.Empty);
        Assert.That(section.Failure, Does.Contain("could not be written"));
    }

    [Test]
    public void AppliesTo_GoneWithNoEarlyDump_False()
    {
        Assert.That(
            new ManagedStacksCollector().AppliesTo(
                Context(processWasAlive: false, earlyDump: null)
            ),
            Is.False,
            "with neither a process nor a dump there is nothing to read stacks from"
        );
    }

    [Test]
    public void AppliesTo_GoneWithAnEarlyDump_True()
    {
        Assert.That(
            new ManagedStacksCollector().AppliesTo(
                Context(
                    processWasAlive: false,
                    earlyDump: Task.FromResult<EarlyFreezeDump.Taken?>(AnEarlyDump())
                )
            ),
            Is.True
        );
    }

    [Test]
    public async Task CollectAsync_GoneWithAnEarlyDump_AttachesItToTheReport()
    {
        var early = AnEarlyDump();
        var context = Context(
            processWasAlive: false,
            earlyDump: Task.FromResult<EarlyFreezeDump.Taken?>(early)
        );

        var section = await new ManagedStacksCollector().CollectAsync(
            context,
            CancellationToken.None
        );

        var expected = Path.Combine(context.ArtifactDirectory, "bloom-4242-early.dmp");
        Assert.That(section.Artifacts, Is.EqualTo(new[] { expected }));
        Assert.That(File.ReadAllText(expected), Is.EqualTo("not really a dump"));
        Assert.That(File.Exists(early.DumpPath), Is.False, "the dump should be moved, not copied");
        Assert.That(
            section.Body,
            Does.Contain("Bloom had gone by the time this report was gathered"),
            "the report must say these stacks are from earlier, not from the moment of gathering"
        );
        Assert.That(section.Body, Does.Contain("could not be read here"));
    }

    [Test]
    public async Task CollectAsync_FreshDumpMissesItsDeadline_UsesTheEarlyDumpAndCleansUpTheLateOne()
    {
        var context = Context(
            processWasAlive: true,
            earlyDump: Task.FromResult<EarlyFreezeDump.Taken?>(AnEarlyDump())
        );
        using var runtimeAnswers = new ManualResetEventSlim();
        var lateDump = new TaskCompletionSource<long>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        // A runtime that answers only after the deadline, and then writes a real dump (of this test
        // process), which the abandoned attempt reads successfully and would otherwise leave behind.
        var collector = new ManagedStacksCollector(
            (_, path) =>
            {
                runtimeAnswers.Wait();
                ManagedStacksCollector.WriteDump(Environment.ProcessId, path);
                lateDump.SetResult(new FileInfo(path).Length);
            },
            freshDumpDeadlineWithEarlyDump: TimeSpan.FromMilliseconds(200),
            freshDumpDeadline: TimeSpan.FromSeconds(1),
            earlyDumpWait: TimeSpan.FromSeconds(10)
        );

        var section = await collector.CollectAsync(context, CancellationToken.None);

        var earlyInReport = Path.Combine(context.ArtifactDirectory, "bloom-4242-early.dmp");
        Assert.That(section.Artifacts, Is.EqualTo(new[] { earlyInReport }));
        Assert.That(section.Body, Does.Contain("dump pipe did not answer"));

        runtimeAnswers.Set();
        var size = await lateDump.Task.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.That(size, Is.GreaterThan(0), "sanity check: the late dump was really written");

        var lateDumpPath = Path.Combine(context.ArtifactDirectory, "bloom-4242.dmp");
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (File.Exists(lateDumpPath) && waited.Elapsed.TotalSeconds < 60)
            await Task.Delay(100);
        Assert.That(
            File.Exists(lateDumpPath),
            Is.False,
            "a dump the report went on without should be deleted once it was written"
        );
        Assert.That(
            File.Exists(earlyInReport),
            Is.True,
            "cleaning up the late dump must leave the report's own files alone"
        );
    }

    [Test]
    public async Task CollectAsync_NeitherDumpComes_WaitsForTheEarlyDumpOnlyOnce()
    {
        var context = Context(
            processWasAlive: true,
            earlyDump: new TaskCompletionSource<EarlyFreezeDump.Taken?>().Task
        );
        // A process id Windows never hands out (ids are multiples of 4), so the live read that follows
        // fails at once instead of reading some unrelated process.
        context = context with
        {
            Target = context.Target with { ProcessId = 4243 },
        };
        using var runtimeAnswers = new ManualResetEventSlim();
        var earlyDumpWait = TimeSpan.FromSeconds(3);
        var collector = new ManagedStacksCollector(
            (_, _) => runtimeAnswers.Wait(),
            freshDumpDeadlineWithEarlyDump: TimeSpan.FromMilliseconds(200),
            freshDumpDeadline: TimeSpan.FromSeconds(1),
            earlyDumpWait
        );

        try
        {
            var section = await collector.CollectAsync(context, CancellationToken.None);

            Assert.That(
                section.Failure,
                Does.Contain("neither the runtime's dump pipe nor a live read"),
                "sanity check: the report fell back to the live read, and it failed"
            );
            Assert.That(
                section.Duration,
                Is.GreaterThanOrEqualTo(earlyDumpWait),
                "sanity check: the early dump was waited for"
            );
            Assert.That(
                section.Duration,
                Is.LessThan(earlyDumpWait * 1.5),
                "the early dump should be waited for once, leaving the rest of the budget for the live read"
            );
        }
        finally
        {
            runtimeAnswers.Set();
        }
    }
}
