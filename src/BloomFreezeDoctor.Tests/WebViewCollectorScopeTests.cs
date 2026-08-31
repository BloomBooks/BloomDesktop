using BloomFreezeDoctor;
using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// When the WebView2 collector is allowed to run at all.
///
/// A port number outlives the process that owned it, so this is the one collector where running too
/// eagerly does not merely waste time - it produces confident, wrong evidence about a process that had
/// already exited, sourced from whatever holds that port now.
/// </summary>
[TestFixture]
public class WebViewCollectorScopeTests
{
    private static GatherContext Context(bool processWasAlive, int? cdpPort) =>
        new()
        {
            Target = new BloomTargetFacts
            {
                ProcessId = 4242,
                ExePath = @"C:\Users\jt\AppData\Local\Bloom\current\Bloom.exe",
                Channel = "Release",
                CommandLine = "\"Bloom.exe\"",
                StartTime = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Local),
            },
            Verdict = new DetectorVerdict
            {
                State = TargetState.Frozen,
                Report = ReportReason.Frozen,
                Explanation = "for the test",
            },
            ProcessWasAlive = processWasAlive,
            ArtifactDirectory = Path.GetTempPath(),
            CdpPort = cdpPort,
        };

    [Test]
    public void It_runs_for_a_live_process_with_a_known_port()
    {
        Assert.That(
            new WebViewCollector().AppliesTo(Context(processWasAlive: true, cdpPort: 8091)),
            Is.True,
            "sanity check: this is the case the collector exists for"
        );
    }

    [Test]
    public void It_does_not_run_for_a_process_that_had_already_exited()
    {
        // The finding this pins. A dead Bloom still has a port recorded in its session file, and connecting
        // to it reaches whoever owns that port NOW - on a developer's machine, plausibly their own browser,
        // whose page titles would then appear on a Bloom card. The reply also reads as evidence: "WebView2
        // answers normally, so the block is in Bloom's .NET UI thread", said of a process that had gone.
        //
        // Its three sibling collectors - managed stacks, process evidence, wait chains - all had this guard
        // already. This one was the odd one out.
        Assert.That(
            new WebViewCollector().AppliesTo(Context(processWasAlive: false, cdpPort: 8091)),
            Is.False,
            "a port outlives its process; the evidence does not"
        );
    }

    [Test]
    public void It_does_not_run_when_no_port_was_found()
    {
        Assert.That(
            new WebViewCollector().AppliesTo(Context(processWasAlive: true, cdpPort: null)),
            Is.False
        );
    }
}
