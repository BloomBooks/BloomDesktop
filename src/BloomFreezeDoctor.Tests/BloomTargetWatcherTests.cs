using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The watcher's job is to join a probe to a detector and decide whether a report may actually be
/// filed. These tests drive it by hand rather than by timer, so they are deterministic.
/// </summary>
[TestFixture]
public class BloomTargetWatcherTests
{
    /// <summary>A probe that returns whatever the test tells it to, at whatever time the test says.</summary>
    private sealed class ScriptedProbe : ITargetProbe
    {
        public bool Alive { get; set; } = true;
        public bool Responds { get; set; } = true;
        public bool HasVisibleWindow { get; set; } = true;
        public bool DebuggerAttached { get; set; }

        /// <summary>Uptime handed to the detector; the test advances it explicitly.</summary>
        public TimeSpan Uptime { get; set; }

        public TargetObservation Observe(TimeSpan uptime) =>
            new()
            {
                // Ignore the watcher's own clock so the test controls elapsed time exactly.
                Uptime = Uptime,
                IsAlive = Alive,
                WindowResponds = Responds,
                HasVisibleWindow = HasVisibleWindow,
                // No departure time, so this stands for the conservative case: a debugger was seen and we
                // cannot tell when it left, which poisons the target exactly as it always did.
                DebuggerAttachedNow = DebuggerAttached,
                DebuggerEverAttached = DebuggerAttached,
            };
    }

    private static BloomTargetFacts Facts(string exePath, string commandLine = "\"Bloom.exe\" ") =>
        new()
        {
            ProcessId = 1234,
            ExePath = exePath,
            Channel = BloomChannel.DeriveFromExePath(exePath),
            CommandLine = commandLine,
            StartTime = DateTime.Parse("2026-08-18 11:00:00"),
        };

    private const string InstalledExe = @"C:\Users\jt\AppData\Local\Bloom\current\Bloom.exe";
    private const string DeveloperExe = @"C:\github\BloomDesktop\output\Debug\x64\Bloom.exe";

    /// <summary>Runs the watcher forward a second at a time, collecting any reports raised.</summary>
    private static List<ReportWantedEventArgs> RunToFreeze(
        BloomTargetWatcher watcher,
        ScriptedProbe probe,
        int seconds = 61
    )
    {
        var reports = new List<ReportWantedEventArgs>();
        watcher.ReportWanted += (_, e) => reports.Add(e);

        probe.Uptime = TimeSpan.Zero;
        watcher.Tick();
        probe.Responds = false;
        for (var t = 1; t <= seconds; t++)
        {
            probe.Uptime = TimeSpan.FromSeconds(t);
            watcher.Tick();
        }
        return reports;
    }

    [Test]
    public void An_installed_Bloom_that_freezes_produces_a_fileable_report()
    {
        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(Facts(InstalledExe), probe);

        var reports = RunToFreeze(watcher, probe);

        Assert.That(reports, Has.Count.EqualTo(1), "one freeze, one report");
        Assert.That(reports[0].Verdict.Report, Is.EqualTo(ReportReason.Frozen));
        Assert.That(
            reports[0].MayFile,
            Is.True,
            "a real installed Bloom is exactly what we file for"
        );
        Assert.That(watcher.State, Is.EqualTo(TargetState.Frozen));
    }

    [Test]
    public void A_developer_build_is_gathered_but_never_filed()
    {
        // The primary defence from plan §3.3, and the one that covers `pnpm go` whether or not a
        // debugger is attached.
        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(Facts(DeveloperExe), probe);

        var reports = RunToFreeze(watcher, probe);

        Assert.That(reports, Has.Count.EqualTo(1), "we still want the report gathered to disk");
        Assert.That(reports[0].MayFile, Is.False, "a developer build must never reach the tracker");
    }

    [Test]
    public void An_automation_run_is_never_filed_either()
    {
        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(
            Facts(InstalledExe, "\"Bloom.exe\" --automation --label /x/"),
            probe
        );

        var reports = RunToFreeze(watcher, probe);

        Assert.That(reports[0].MayFile, Is.False);
    }

    [Test]
    public void A_target_seen_under_a_debugger_is_never_filed_even_if_it_later_looks_clean()
    {
        var probe = new ScriptedProbe { DebuggerAttached = true };
        using var watcher = new BloomTargetWatcher(Facts(InstalledExe), probe);
        var reports = new List<ReportWantedEventArgs>();
        watcher.ReportWanted += (_, e) => reports.Add(e);

        probe.Uptime = TimeSpan.Zero;
        watcher.Tick();
        Assert.That(watcher.IsPoisonedByDebugger, Is.True, "setup: should be flagged at once");

        // The debugger detaches, then the process freezes.
        probe.DebuggerAttached = false;
        probe.Responds = false;
        for (var t = 1; t <= 61; t++)
        {
            probe.Uptime = TimeSpan.FromSeconds(t);
            watcher.Tick();
        }

        Assert.That(reports, Has.Count.EqualTo(1), "still gathered");
        Assert.That(
            reports[0].MayFile,
            Is.False,
            "stopping the debugger must never file a card, and detaching first must not launder it"
        );
    }

    [Test]
    public void A_probe_that_throws_does_not_stop_the_watcher()
    {
        // The watcher is the thing that must not die: a watcher that throws stops watching, and then
        // we learn nothing at all.
        var probe = new ThrowingProbe();
        using var watcher = new BloomTargetWatcher(Facts(InstalledExe), probe);

        Assert.DoesNotThrow(() => watcher.Tick());
        Assert.DoesNotThrow(() => watcher.Tick());
        Assert.That(probe.Calls, Is.EqualTo(2), "it kept asking");
    }

    private sealed class ThrowingProbe : ITargetProbe
    {
        public int Calls { get; private set; }

        public TargetObservation Observe(TimeSpan uptime)
        {
            Calls++;
            throw new InvalidOperationException("probe failure");
        }
    }
}
