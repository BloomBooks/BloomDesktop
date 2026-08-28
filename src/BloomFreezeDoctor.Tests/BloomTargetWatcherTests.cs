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
            ProcessId = TestPid,
            ExePath = exePath,
            Channel = BloomChannel.DeriveFromExePath(exePath),
            CommandLine = commandLine,
            StartTime = DateTime.Parse("2026-08-18 11:00:00"),
        };

    private const string InstalledExe = @"C:\Users\jt\AppData\Local\Bloom\current\Bloom.exe";
    private const string DeveloperExe = @"C:\github\BloomDesktop\output\Debug\x64\Bloom.exe";

    /// <summary>
    /// The pid every test here uses. It matters that the session file for it is absent unless a test puts
    /// one there: the watcher reads the session from the machine's default directory and takes no override,
    /// so a file left behind by an earlier run would silently change what these tests measure - a stray
    /// SimulatedFailure would make the fileable-report test fail for a reason nothing would explain.
    /// </summary>
    private const int TestPid = 1234;

    [SetUp]
    public void ClearAnyLeftoverSession() => RemoveTestSession();

    [TearDown]
    public void RemoveTestSessionAfterwards() => RemoveTestSession();

    private static void RemoveTestSession()
    {
        try
        {
            var path = Protocol.DoctorSessionStore.PathFor(TestPid);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception) { }
    }

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

    /// <summary>
    /// The invariant that keeps simulated freezes worth running at all: the marker may change whether a
    /// report is FILED, and nothing else. If it were ever allowed to short-circuit detection or gathering,
    /// a simulated run would stop exercising the code it exists to exercise, and would quietly become
    /// theatre.
    ///
    /// Deliberately an INSTALLED exe: on a developer build the report is unfilable anyway, so the test
    /// could pass with the simulated marker doing nothing at all. Its counterpart above -
    /// An_installed_Bloom_that_freezes_produces_a_fileable_report - is what proves the contrast.
    /// </summary>
    [Test]
    public void A_deliberately_simulated_freeze_is_still_detected_and_gathered_but_never_filed()
    {
        var session = new Protocol.DoctorSession
        {
            ProcessId = TestPid,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Channel = "Alpha",
            SimulatedFailure = "stawait",
        };
        Assert.That(
            Protocol.DoctorSessionStore.TryWrite(session),
            Is.True,
            "setup: without the session file on disk this test would prove nothing, since the watcher has "
                + "no other way to learn the freeze was simulated"
        );

        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(Facts(InstalledExe), probe);
        var reports = RunToFreeze(watcher, probe);

        Assert.That(
            reports,
            Is.Not.Empty,
            "a simulated freeze must still be detected and gathered - only the decision to file may differ"
        );
        Assert.That(
            reports[0].MayFile,
            Is.False,
            "nobody wants a tracker card about a freeze we asked for"
        );
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
    public void An_automation_flag_does_not_by_itself_stop_an_installed_Bloom_filing()
    {
        // The reversal of what this test used to assert, and deliberate. `--automation` is not a
        // "this is a test run" flag: in Bloom it means take the multi-instance path, print the ports,
        // and show them in the title. An installed Bloom carrying it is somebody's real Bloom, and a
        // freeze in it is worth a card.
        //
        // What protects our own work is the CHANNEL, one test above: a `go.sh` Bloom carries this same
        // flag but builds into output/Debug, so it is blocked as a developer build regardless.
        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(
            Facts(InstalledExe, "\"Bloom.exe\" --automation --label /x/"),
            probe
        );

        var reports = RunToFreeze(watcher, probe);

        Assert.That(reports, Has.Count.EqualTo(1));
        Assert.That(
            reports[0].MayFile,
            Is.True,
            "an installed Bloom is a real Bloom, whatever flags it was launched with"
        );
    }

    [Test]
    public void A_headless_job_run_is_gathered_but_never_filed()
    {
        // The command-line verbs are the genuine never-file case on the command line: they serve no
        // user, so a card about one would be a card about a script.
        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(
            Facts(InstalledExe, "\"Bloom.exe\" hydrate --bookpath foo"),
            probe
        );

        var reports = RunToFreeze(watcher, probe);

        Assert.That(reports, Has.Count.EqualTo(1));
        Assert.That(reports[0].MayFile, Is.False, "a headless job must never reach the tracker");
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

    /// <summary>
    /// The same question the freeze path answers above, asked the way the crash and exit paths ask it.
    ///
    /// Those two paths do not go through Tick's ReportWanted at all — they gather directly when the
    /// process dies — and they each used to work out for themselves whether filing was allowed, with a
    /// shorter list of conditions than this one: the debugger and the channel, but not the simulated
    /// marker. So a deliberately simulated CRASH on a channel where the simulator is allowed filed a real
    /// tracker card, while a simulated FREEZE on the same machine correctly did not.
    /// </summary>
    [Test]
    public void A_simulated_run_may_not_file_however_the_question_is_asked()
    {
        var session = new Protocol.DoctorSession
        {
            ProcessId = TestPid,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Channel = "Alpha",
            SimulatedFailure = "crashthread",
        };
        Assert.That(
            Protocol.DoctorSessionStore.TryWrite(session),
            Is.True,
            "setup: the marker on disk is the watcher's only way to know this was a rehearsal"
        );

        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(Facts(InstalledExe), probe);

        Assert.That(
            watcher.MayFileAReport(),
            Is.False,
            "an installed Bloom on a simulator-enabled channel, told to break itself - filing this would "
                + "put a rehearsal on the tracker"
        );
    }

    /// <summary>The sanity check on the test above: the same call says yes when nothing forbids it.</summary>
    [Test]
    public void An_ordinary_installed_Bloom_may_file()
    {
        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(Facts(InstalledExe), probe);

        Assert.That(
            watcher.MayFileAReport(),
            Is.True,
            "with no debugger, no simulation and a real channel, a report is exactly what we want"
        );
    }

    /// <summary>
    /// `--force` exists to exercise filing on a machine that would otherwise decline, and the obvious way
    /// to exercise the crash path is a deliberately simulated crash — so the one switch for testing filing
    /// has to work on the paths that run when Bloom dies. It was applied at exactly one of the three
    /// deciding sites, the freeze and zombie one, so the crash dump and the exit examination ignored it
    /// entirely and a forced run of a simulated crash filed nothing at all.
    ///
    /// This test guards the shape that prevents that: one method answers the question, so a caller cannot
    /// have its own shorter version. The supervisor's `MayFile` is `MayFileAReport() || force`, and what
    /// this checks is the half that lives here — that a simulated run really does say no on its own, so
    /// that the override is doing something rather than papering over an answer of yes.
    /// </summary>
    [Test]
    public void A_simulated_run_says_no_so_that_force_has_something_to_override()
    {
        var session = new Protocol.DoctorSession
        {
            ProcessId = TestPid,
            StartedAtUtc = DateTimeOffset.UtcNow,
            Channel = "Alpha",
            SimulatedFailure = "crashthread",
        };
        Assert.That(Protocol.DoctorSessionStore.TryWrite(session), Is.True, "setup");

        var probe = new ScriptedProbe();
        using var watcher = new BloomTargetWatcher(Facts(InstalledExe), probe);

        Assert.That(watcher.MayFileAReport(), Is.False, "the guard must refuse");
        Assert.That(
            watcher.ReasonsFilingWouldNormallyBeBlocked(),
            Has.Some.Contains("crashthread"),
            "and it must say which rehearsal, since that is what the person overriding it is shown"
        );
    }
}
