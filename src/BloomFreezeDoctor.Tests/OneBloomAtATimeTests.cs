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
/// These adopt the TEST PROCESS as a stand-in for Bloom, which is safe here and deliberately so: the
/// supervisor is constructed with zombie-ending switched off, and ending a Bloom needs gathered evidence
/// first in any case, so nothing in this fixture can decide to end the test host.
/// </summary>
[TestFixture]
public class OneBloomAtATimeTests
{
    private static DoctorSupervisor Inert(string outboxRoot) =>
        new(
            project: "AUT",
            targetProcessName: "no-such-process-at-all",
            outbox: new ReportOutbox(outboxRoot),
            neverEndZombies: true,
            targetNameWasGiven: true
        );

    private string _outbox = null!;

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
        try
        {
            if (Directory.Exists(_outbox))
                Directory.Delete(_outbox, recursive: true);
        }
        catch (Exception) { }
    }

    [Test]
    public void Adopting_the_same_process_twice_leaves_one_watcher()
    {
        var pid = Process.GetCurrentProcess().Id;
        using var supervisor = Inert(_outbox);

        supervisor.Adopt(pid);
        Assert.That(
            supervisor.LiveWatchedBlooms(),
            Has.Count.EqualTo(1),
            "setup: the first adoption must take, or this test proves nothing"
        );

        supervisor.Adopt(pid);

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
        // this test slow and flaky for no gain; what needs asserting is what one tick does.
        // The supervisor must do the ADOPTING itself, which means letting its sweep find us: pre-adopting
        // by hand takes the other branch of Discover entirely, and that is why the first version of this
        // test passed against the bug it was written for.
        var self = Process.GetCurrentProcess();
        var pid = self.Id;
        using var supervisor = new DoctorSupervisor(
            project: "AUT",
            targetProcessName: self.ProcessName,
            outbox: new ReportOutbox(_outbox),
            neverEndZombies: true,
            targetNameWasGiven: true
        );

        // One tick, which both adopts and then decides whether the adopted Bloom has gone. With the bug
        // those happened in the same pass and the second undid the first, so this comes back empty.
        supervisor.Discover();

        var afterFirstTick = supervisor.LiveWatchedBlooms();
        Assert.That(
            afterFirstTick,
            Has.Count.EqualTo(1),
            "the tick that adopted this process must not also have decided it had gone"
        );
        // Deliberately NOT asserting WHICH process it adopted. The sweep looks up by process name, and on
        // a machine running more than one test host (or two of these runs at once) there is more than one
        // process of this name - so pinning the id made this fail for a reason the test is not about. What
        // the test is about is that the tick which adopted did not also decide the adopted Bloom had gone.
        Assert.That(
            afterFirstTick[0].ProcessId,
            Is.GreaterThan(0),
            "and it should be watching a real process"
        );

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
        var self = Process.GetCurrentProcess().Id;
        // `ping -n 60` rather than `pause`: pause needs a console to wait on, and with none attached it
        // exits at once - which made an earlier version of this test pass for the wrong reason, because
        // there was no second process left to adopt by the time we tried.
        using var other = Process.Start(
            new ProcessStartInfo("cmd.exe", "/c ping -n 60 127.0.0.1")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            }
        )!;
        try
        {
            using var supervisor = Inert(_outbox);
            supervisor.Adopt(self);
            Assert.That(
                supervisor.LiveWatchedBlooms(),
                Has.Count.EqualTo(1),
                "setup: watching the first"
            );
            Assert.That(
                other.HasExited,
                Is.False,
                "setup: the second process must still be alive, or there is nothing to refuse"
            );

            supervisor.Adopt(other.Id);

            var watched = supervisor.LiveWatchedBlooms();
            Assert.That(watched, Has.Count.EqualTo(1), "a second Bloom must not be adopted");
            Assert.That(
                watched[0].ProcessId,
                Is.EqualTo(self),
                "and the one already being watched is the one we keep"
            );
        }
        finally
        {
            try
            {
                other.Kill();
            }
            catch (Exception) { }
        }
    }
}
