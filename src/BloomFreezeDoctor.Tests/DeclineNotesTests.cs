using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Saying once, not sixteen times, why a process is not being watched.
///
/// Worth testing rather than eyeballing because both failure modes are quiet. Say it every time and the
/// log fills at a line every five seconds for as long as the process lives, burying whatever else it was
/// meant to record; say it only once ever and the reason is lost the moment it changes, which is precisely
/// what a process part-way through starting up does.
/// </summary>
[TestFixture]
public class DeclineNotesTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-08-31T15:17:08Z");

    [Test]
    public void The_first_decline_is_worth_saying()
    {
        var notes = new DeclineNotes();

        Assert.That(notes.ShouldSay(53468, "Win32Exception: cannot read", Start), Is.True);
    }

    [Test]
    public void The_same_reason_again_is_not()
    {
        // The discovery sweep runs every five seconds. This is what keeps eighty-three seconds of it to one
        // line.
        var notes = new DeclineNotes();
        Assert.That(
            notes.ShouldSay(53468, "Win32Exception: cannot read", Start),
            Is.True,
            "setup: the first one is said"
        );

        for (var tick = 1; tick <= 16; tick++)
        {
            Assert.That(
                notes.ShouldSay(
                    53468,
                    "Win32Exception: cannot read",
                    Start + TimeSpan.FromSeconds(5 * tick)
                ),
                Is.False,
                $"tick {tick} must stay quiet"
            );
        }
    }

    [Test]
    public void A_changed_reason_is_worth_saying_again()
    {
        // The case that rules out "only ever say it once". A process that is starting fails in one way and
        // then another, and the second reason is usually the more informative.
        var notes = new DeclineNotes();
        notes.ShouldSay(53468, "Win32Exception: cannot read", Start);

        Assert.That(
            notes.ShouldSay(53468, "InvalidOperationException: process has exited", Start),
            Is.True
        );
    }

    [Test]
    public void A_different_process_is_worth_saying()
    {
        var notes = new DeclineNotes();
        notes.ShouldSay(53468, "Win32Exception: cannot read", Start);

        Assert.That(notes.ShouldSay(99999, "Win32Exception: cannot read", Start), Is.True);
    }

    [Test]
    public void Adopting_reports_how_long_we_had_been_declining()
    {
        // The number the run that prompted all this was missing: Bloom up at 15:17:08, watched at 15:18:31,
        // and nothing anywhere to say what happened in between.
        var notes = new DeclineNotes();
        notes.ShouldSay(53468, "Win32Exception: cannot read", Start);

        var waited = notes.HowLongWeWereDeclining(53468, Start + TimeSpan.FromSeconds(83));

        Assert.That(waited, Is.Not.Null);
        Assert.That(waited!.Value.TotalSeconds, Is.EqualTo(83).Within(0.5));
    }

    [Test]
    public void The_timer_runs_from_the_first_sighting_not_the_latest_reason()
    {
        // Otherwise a process whose reason changes half way through reports only the tail of the wait, and
        // under-reports exactly the case worth measuring.
        var notes = new DeclineNotes();
        notes.ShouldSay(53468, "first reason", Start);
        notes.ShouldSay(53468, "second reason", Start + TimeSpan.FromSeconds(60));

        var waited = notes.HowLongWeWereDeclining(53468, Start + TimeSpan.FromSeconds(83));

        Assert.That(
            waited!.Value.TotalSeconds,
            Is.EqualTo(83).Within(0.5),
            "the wait began when we first saw it, not when the reason last changed"
        );
    }

    [Test]
    public void A_process_we_never_declined_reports_nothing()
    {
        // The normal case, and it must say nothing rather than "0s" - almost every Bloom is adopted on the
        // first tick that sees it, and a note on each one would be noise.
        var notes = new DeclineNotes();

        Assert.That(notes.HowLongWeWereDeclining(53468, Start), Is.Null);
    }

    [Test]
    public void Adopting_forgets_it_so_a_later_decline_speaks_up()
    {
        // A pid can be declined, adopted, and - after the process goes and the id is reused - declined
        // again. The second decline is new information.
        var notes = new DeclineNotes();
        notes.ShouldSay(53468, "Win32Exception: cannot read", Start);
        notes.HowLongWeWereDeclining(53468, Start + TimeSpan.FromSeconds(83));

        Assert.That(
            notes.ShouldSay(53468, "Win32Exception: cannot read", Start),
            Is.True,
            "and the same reason is worth saying again, because the earlier note was resolved"
        );
    }
}
