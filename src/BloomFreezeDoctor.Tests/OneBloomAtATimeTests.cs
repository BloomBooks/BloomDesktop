using System.Diagnostics;
using BloomFreezeDoctor;
using BloomFreezeDoctor.Outbox;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The Doctor watches one Bloom at a time.
///
/// Worth a test rather than a comment because the failure is invisible: watching a second Bloom looks
/// exactly like watching the first, right up to the point where "Restart Bloom" offers to kill somebody
/// else's work, or a stale note about one process attaches itself to another.
///
/// These use REAL CHILD PROCESSES as stands-in for Bloom. They used to adopt the test host itself, which
/// was convenient and is now forbidden: a Doctor refuses to watch its own process, because one was seen
/// doing exactly that. A separate process is also the more faithful stand-in - the test host is the one
/// process whose lifetime cannot tell us anything about watching another.
/// </summary>
[TestFixture]
public class OneBloomAtATimeTests
{
    private string _outbox = null!;
    private readonly List<Process> _children = new();

    /// <summary>
    /// A process that will sit there for a minute without a window, so the Doctor has something real to
    /// adopt. Registered for teardown, so a failing test cannot leave one behind.
    /// </summary>
    private Process StartAStandIn()
    {
        var child = Process.Start(
            // Ten minutes, not one. A minute was not enough: the sweep asks WMI about each candidate, which
            // costs seconds, and one run took 76 seconds to reach its assertion - by which time the
            // stand-in had exited and the test failed for a reason it is not about. It is killed in
            // teardown, so the long life costs nothing.
            new ProcessStartInfo("ping.exe", "-n 600 127.0.0.1")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            }
        )!;
        _children.Add(child);
        Assert.That(
            child.HasExited,
            Is.False,
            "setup: the stand-in must actually be running, or the test proves nothing"
        );
        return child;
    }

    private DoctorSupervisor WatchingOnly(string processName) =>
        new(
            project: "AUT",
            targetProcessName: processName,
            outbox: new ReportOutbox(_outbox),
            neverEndZombies: true,
            targetNameWasGiven: true
        );

    [SetUp]
    public void SetUp()
    {
        _outbox = Path.Combine(
            Path.GetTempPath(),
            "FreezeDoctorTests",
            "one-bloom-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(_outbox);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var child in _children)
        {
            try
            {
                if (!child.HasExited)
                    child.Kill();
                child.Dispose();
            }
            catch (Exception) { }
        }
        _children.Clear();
        try
        {
            if (Directory.Exists(_outbox))
                Directory.Delete(_outbox, recursive: true);
        }
        catch (Exception) { }
    }

    [Test]
    public void The_Doctor_refuses_to_watch_itself()
    {
        // Seen in a real doctor.log: "[25736] watching Bloom 25736 (Release)". The log prefix is the writing
        // process's own id, so that Doctor had adopted itself. How it got there was never established,
        // which is the point of a guard rather than a fix: watching our own process is nonsense whatever
        // route reaches it, and it would report on that process's "death" as we exited.
        using var supervisor = WatchingOnly("no-such-process-at-all");

        supervisor.Adopt(Process.GetCurrentProcess().Id);

        Assert.That(supervisor.LiveWatchedBlooms(), Is.Empty);
    }

    [Test]
    public void Adopting_the_same_process_twice_leaves_one_watcher()
    {
        var standIn = StartAStandIn();
        using var supervisor = WatchingOnly(standIn.ProcessName);

        supervisor.Adopt(standIn.Id);
        Assert.That(
            supervisor.LiveWatchedBlooms(),
            Has.Count.EqualTo(1),
            "setup: the first adoption must take, or this test proves nothing"
        );

        supervisor.Adopt(standIn.Id);

        Assert.That(supervisor.LiveWatchedBlooms(), Has.Count.EqualTo(1));
    }

    [Test]
    public void A_discovery_tick_does_not_drop_the_Bloom_it_just_adopted()
    {
        // This is the test that was missing, and its absence cost a whole manual run.
        //
        // The one-Bloom rewrite decided whether the watched Bloom had gone from a flag that was only set
        // in the "we already have a target" branch. On a tick that ADOPTED, that branch never ran, so the
        // flag was still false and the freshly adopted Bloom was treated as departed - every adopting tick
        // un-adopted. Two symptoms, both seen on a real run: the log filled with "watching Bloom NNNN"
        // every five seconds, and the death of a real crashing Bloom was never examined at all, because
        // the spurious departure had already claimed the one examination.
        //
        // Calling Discover directly is why it is internal. Waiting on its five-second timer would make
        // this test slow and flaky for no gain; what needs asserting is what one tick does. The supervisor
        // must do the ADOPTING itself, which means letting its sweep find the stand-in by name:
        // pre-adopting by hand takes the other branch of Discover entirely, and that is why the first
        // version of this test passed against the bug it was written for.
        var standIn = StartAStandIn();
        using var supervisor = WatchingOnly(standIn.ProcessName);

        // One tick, which both adopts and then decides whether the adopted Bloom has gone. With the bug
        // those happened in the same pass and the second undid the first, so this comes back empty.
        supervisor.Discover();

        var afterFirstTick = supervisor.LiveWatchedBlooms();
        Assert.That(
            afterFirstTick,
            Has.Count.EqualTo(1),
            "the tick that adopted must not also have decided the process had gone"
        );
        // Deliberately NOT asserting WHICH process. The sweep looks up by name, and another of these can be
        // running - a second test host, or somebody's own ping - so pinning the id made this fail for a
        // reason the test is not about.
        Assert.That(afterFirstTick[0].ProcessId, Is.GreaterThan(0));

        // A second tick, which takes the other branch - we now have a target, so it checks rather than
        // adopts. It must leave a live process alone.
        supervisor.Discover();
        Assert.That(
            supervisor.LiveWatchedBlooms(),
            Has.Count.EqualTo(1),
            "and a later tick must not drop a Bloom that is still running"
        );
    }

    [Test]
    public void A_second_process_is_not_adopted()
    {
        // The case that matters: a developer with two worktrees open, or an alpha tester running two
        // channels. Before this, both were watched and both appeared in the list "Restart Bloom" offers to
        // end - so clearing the way for one Bloom meant being asked to kill the other.
        var first = StartAStandIn();
        var second = StartAStandIn();
        using var supervisor = WatchingOnly(first.ProcessName);

        supervisor.Adopt(first.Id);
        Assert.That(
            supervisor.LiveWatchedBlooms(),
            Has.Count.EqualTo(1),
            "setup: watching the first"
        );
        Assert.That(
            second.HasExited,
            Is.False,
            "setup: the second process must still be alive, or there is nothing to refuse"
        );

        supervisor.Adopt(second.Id);

        var watched = supervisor.LiveWatchedBlooms();
        Assert.That(watched, Has.Count.EqualTo(1), "a second Bloom must not be adopted");
        Assert.That(
            watched[0].ProcessId,
            Is.EqualTo(first.Id),
            "and the one already being watched is the one we keep"
        );
    }
}
