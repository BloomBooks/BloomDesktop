using System.Threading;
using BloomBooks.FreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The named events Bloom and the Doctor use to reach each other: "please exit under your own power", and
/// "dump me before I die".
///
/// These had no tests at all until the day `TryOpen` was rewritten to stop using an exception for its most
/// ordinary answer. They are worth having for a reason beyond that change: **the interesting case is the
/// absent one.** Almost every Bloom in the world runs with no Doctor installed, so "nobody is listening"
/// is the normal answer to every question here, and it has to be cheap, quiet, and above all reliable —
/// `Exists` is what a crashing Bloom calls to decide whether to wait for a dump, and getting that wrong
/// means either a lost dump or a user watching a dead Bloom sit there.
/// </summary>
[TestFixture]
public class DoctorSignalsTests
{
    /// <summary>A pid no real process will have, so these cannot collide with a live Bloom or Doctor.</summary>
    private const int TestProcessId = 999_003;

    [Test]
    public void Nobody_listening_is_answered_without_an_exception_and_without_a_handle()
    {
        var name = DoctorSignals.QuitRequestName(TestProcessId);

        // Sanity check that nothing is left over from another test or an earlier run, or this proves
        // nothing at all.
        Assert.That(DoctorSignals.Exists(name), Is.False, "setup: nothing should be listening yet");

        Assert.That(DoctorSignals.TryOpen(name), Is.Null, "no handle, rather than a throw");
        Assert.That(
            DoctorSignals.TrySignal(name),
            Is.False,
            "signalling nobody reports that it did not"
        );
    }

    [Test]
    public void An_event_that_exists_can_be_found_signalled_and_waited_on()
    {
        var name = DoctorSignals.DumpRequestName(TestProcessId);
        Assert.That(DoctorSignals.Exists(name), Is.False, "setup: should start absent");

        using (var created = DoctorSignals.TryCreate(name))
        {
            Assert.That(created, Is.Not.Null, "setup: we should be able to create it");
            Assert.That(DoctorSignals.Exists(name), Is.True, "now somebody is listening");

            using var opened = DoctorSignals.TryOpen(name);
            Assert.That(opened, Is.Not.Null, "the other side should be able to open it");

            // Not yet set, so a wait must time out rather than sail through. This is the zero-timeout
            // question Bloom asks while crashing: "is anyone there?" must not become "wait for someone".
            Assert.That(
                created.WaitOne(TimeSpan.Zero),
                Is.False,
                "an unset event must not read as signalled"
            );

            Assert.That(DoctorSignals.TrySignal(name), Is.True, "somebody was listening");
            Assert.That(
                created.WaitOne(TimeSpan.FromSeconds(5)),
                Is.True,
                "the waiting side should be released"
            );
        }
    }

    [Test]
    public void The_event_goes_away_again_when_the_last_handle_is_closed()
    {
        // Worth pinning because the whole design leans on it: a named event exists only while somebody
        // holds a handle, so a Doctor that has exited leaves nothing behind for Bloom to find and wait on.
        var name = DoctorSignals.DumpCompleteName(TestProcessId);

        using (var created = DoctorSignals.TryCreate(name))
        {
            Assert.That(created, Is.Not.Null, "setup");
            Assert.That(DoctorSignals.Exists(name), Is.True, "setup: present while held");
        }

        Assert.That(
            DoctorSignals.Exists(name),
            Is.False,
            "once the last handle is closed there is nothing left to open"
        );
    }

    [Test]
    public void An_impossible_name_is_refused_rather_than_thrown()
    {
        // The genuinely unexpected case the try/catch is still there for, as opposed to the merely absent
        // case above. Callers must get the same quiet "no" either way: publishing diagnostics is never
        // worth failing over, and the callers here are Bloom's shutdown and crash paths.
        //
        // An empty name is the reliable way to reach that branch for the three OPENING calls. Two things
        // that look like they ought to work as triggers and do not, both established by watching this test
        // fail:
        //
        //   * A very long name is accepted. Windows creates a 5000-character event quite happily, so there
        //     is no length guard here to lean on.
        //   * TryCreate("") SUCCEEDS, and is therefore not asserted below. An empty or null name means an
        //     ANONYMOUS event in .NET rather than an invalid one, so it hands back a perfectly good handle
        //     that simply nobody else can find. Worth knowing before anyone reaches for `TryCreate(name)`
        //     with a name they have not checked.
        Assert.DoesNotThrow(() =>
        {
            Assert.That(DoctorSignals.TryOpen(""), Is.Null, "open");
            Assert.That(DoctorSignals.Exists(""), Is.False, "exists");
            Assert.That(DoctorSignals.TrySignal(""), Is.False, "signal");
        });
    }
}
