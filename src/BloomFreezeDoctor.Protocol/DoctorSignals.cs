using System;
using System.Threading;

namespace BloomFreezeDoctor.Protocol;

// =====================================================================================================
//  THIRD PART OF THE CONTRACT BETWEEN BLOOM AND THE FREEZE DOCTOR.
//
//  Shared memory lets the Doctor watch. These named events let the two actually ask each other for
//  something, in the two cases where waiting is worth it:
//
//   * ENDING A ZOMBIE. When Bloom's UI is gone but the process lives on, the Doctor can ask Bloom to exit
//     under its own power. That is much better than killing it from outside: Bloom's ProcessExit runs, so
//     its single-instance token is released properly and its own clean-exit record is written. Killing is
//     the fallback for when nobody is listening.
//
//   * DUMPING A DYING BLOOM. A crash gives us one short window in which the process still exists. Bloom
//     signals, the Doctor dumps it from outside, and Bloom waits briefly. Dumping from outside beats
//     self-dumping a process whose state is already suspect.
//
//  Everything here is built so that the ABSENCE of the other side costs nothing. Bloom never waits unless
//  it has first confirmed, with a zero timeout, that a Doctor is actually watching — because an
//  unconditional pause would make every crash worse for the majority of users, who have no Doctor
//  installed.
// =====================================================================================================

/// <summary>
/// The named events Bloom and the Doctor use to ask each other for something. All in the `Local\`
/// namespace, since neither can act on the other across Windows sessions anyway.
/// </summary>
public static class DoctorSignals
{
    /// <summary>
    /// Created by the Doctor while it is watching a particular Bloom. Bloom tests for it with a zero
    /// timeout before it ever agrees to wait for anything: no Doctor, no waiting.
    /// </summary>
    public static string WatchingName(int processId) =>
        $@"Local\BloomFreezeDoctor.watching.{processId}";

    /// <summary>
    /// Set by the Doctor to ask Bloom to exit under its own power. Bloom's watchdog thread waits on this,
    /// which is the point: that thread is still running even when the UI thread is long gone.
    /// </summary>
    public static string QuitRequestName(int processId) =>
        $@"Local\BloomFreezeDoctor.quit.{processId}";

    /// <summary>
    /// Set by Bloom when it starts, to tell a Doctor that is ALREADY RUNNING to come and look now rather
    /// than at its next sweep.
    ///
    /// The only signal here with no process id in its name, and it cannot have one: the Doctor is waiting
    /// before it knows which Bloom will appear. That is also why it is the only one Bloom sets without
    /// first checking whether anyone is listening - there is nobody to check for.
    ///
    /// Why it exists: adoption otherwise waits for the next poll, and that poll interval is a window in
    /// which Bloom's own startup cannot be doctored at all. It is not merely a tidiness matter - on one
    /// measured run Bloom crashed and asked to be dumped twenty seconds before the Doctor had noticed it
    /// existed, and because Bloom only asks when a Doctor is already watching, the dump was never taken.
    ///
    /// Polling stays as the backstop, and must: a Bloom too old to know about any of this cannot announce
    /// itself, and those are the Blooms most worth watching.
    /// </summary>
    public static string BloomStartedName() => @"LocalBloomFreezeDoctor.bloomstarted";

    /// <summary>Set by Bloom as it dies, to ask for a dump while the process still exists.</summary>
    public static string DumpRequestName(int processId) =>
        $@"Local\BloomFreezeDoctor.dumpme.{processId}";

    /// <summary>
    /// Set by the Doctor the moment it takes up a dump request, before it begins the work, so that Bloom can
    /// tell "nobody picked this up" from "it is underway" — and be impatient about the first while being
    /// patient about the second.
    ///
    /// Both halves matter. A dump of a real Bloom takes seconds, more on a slow or loaded machine, and
    /// giving up early does not merely delay it but loses it: the dying Bloom is what writes the dump over
    /// the diagnostics pipe, so exiting mid-write aborts it and the report ends up with no managed stacks at
    /// all. Yet a flat, generous timeout would hold a crashing Bloom open for its whole length whenever no
    /// Doctor answers — which includes one that died between Bloom's "is anyone watching" check and its wait.
    /// </summary>
    public static string DumpStartedName(int processId) =>
        $@"Local\BloomFreezeDoctor.dumping.{processId}";

    /// <summary>Set by the Doctor when the dump is written, so Bloom can stop waiting.</summary>
    public static string DumpCompleteName(int processId) =>
        $@"Local\BloomFreezeDoctor.dumped.{processId}";

    /// <summary>
    /// Creates (or opens) a manual-reset event by name, or returns null if that is not possible. Never
    /// throws: a signal we cannot create simply means that capability is unavailable, and both sides are
    /// written to carry on without it.
    /// </summary>
    public static EventWaitHandle? TryCreate(string name)
    {
        try
        {
            return new EventWaitHandle(false, EventResetMode.ManualReset, name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates (or opens) an AUTO-reset event by name. Used for one signal only, and the difference is not
    /// cosmetic.
    ///
    /// Every other signal here is a latch: "a Doctor is watching this process", "the dump is complete". A
    /// latch wants manual reset, because whoever asks later must still get the answer. The Bloom-has-started
    /// announcement is the opposite - a pulse, meaningful once - and putting it on a manual-reset event was
    /// measurably wrong: the Doctor waits with ThreadPool.RegisterWaitForSingleObject, which re-arms the wait
    /// before the callback has run, so a set event fires the callback again and again until something resets
    /// it. A measured run produced 103 wake-ups and 103 needless sweeps from one announcement.
    ///
    /// Auto-reset makes the kernel do it: one set releases exactly one wait and the event goes back to
    /// unsignalled with no cooperation needed. Both sides must create it the same way, because the FIRST
    /// creator fixes the mode - which is why Announce uses this too, rather than the manual-reset TryCreate.
    /// </summary>
    public static EventWaitHandle? TryCreatePulse(string name)
    {
        try
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Opens an existing event, or null if nobody has created it.</summary>
    public static EventWaitHandle? TryOpen(string name)
    {
        try
        {
            // TryOpenExisting, not OpenExisting. "Nobody has created it" is the ORDINARY answer here —
            // most Blooms run on a machine with no Doctor installed — and OpenExisting reports that by
            // throwing WaitHandleCannotBeOpenedException. Using an exception for the expected case is
            // wrong on its own terms, and it is worse than usual given where this is called from:
            // Exists() and TrySignal() both come through here, and Exists() is what Bloom asks on its
            // CRASH path to find out whether a Doctor is listening before it waits for a dump. Throwing
            // and catching inside a process that is already dying is the last thing that code needs.
            //
            // The try/catch stays, for the genuinely unexpected: a name that is malformed or too long, or
            // an existing handle we are not permitted to open. Those are worth swallowing too — a signal
            // we cannot reach just means that capability is unavailable — but they are not the common
            // path, which is the distinction that matters.
            return EventWaitHandle.TryOpenExisting(name, out var handle) ? handle : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// True if the named event exists. Used for the question "is a Doctor watching me?", which must be
    /// answerable instantly and without waiting.
    /// </summary>
    public static bool Exists(string name)
    {
        using var handle = TryOpen(name);
        return handle != null;
    }

    /// <summary>
    /// Sets a named event whether or not anyone has created it yet, creating it if necessary.
    ///
    /// Distinct from <see cref="TrySignal"/>, which only sets an event somebody is already listening on and
    /// reports "nobody there" otherwise. That is the right shape for the per-process signals, where the
    /// listener creates the event and its absence is the answer to a real question; here the caller does not
    /// care who is listening and has nothing useful to do with the answer.
    ///
    /// **It does NOT keep an announcement for a Doctor that arrives later, and I first wrote that it did.**
    /// A Windows named event lives only while a handle to it is open, so creating one, setting it and
    /// disposing the handle - all of which this does - destroys it again on the way out. Announcing to an
    /// empty room is exactly that: a no-op. Observed, not reasoned: a Bloom announcing itself 9 seconds
    /// before a Doctor started was never heard, while the same Bloom's later announcement, made once the
    /// Doctor held the event, arrived at once.
    ///
    /// That is a limitation and not a hole, because "no Doctor is running" is the case Bloom handles by
    /// starting one, which then finds Bloom by its own sweep. What this is for is the other case: a Doctor
    /// already running, which would otherwise not learn of a new Bloom until its next poll.
    /// </summary>
    public static bool Announce(string name)
    {
        try
        {
            using var handle = TryCreatePulse(name);
            if (handle == null)
                return false;
            handle.Set();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Sets an existing event, if there is one. Returns whether anyone was listening.</summary>
    public static bool TrySignal(string name)
    {
        try
        {
            using var handle = TryOpen(name);
            if (handle == null)
                return false;
            handle.Set();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for an event to be set, up to a limit. Returns false if it was not set in time, or could not
    /// be opened at all — the caller treats both the same way, by carrying on.
    /// </summary>
    public static bool WaitFor(string name, TimeSpan timeout)
    {
        try
        {
            using var handle = TryOpen(name);
            return handle != null && handle.WaitOne(timeout);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Waits for an event, but gives up the moment the other side stops existing — checked by testing
    /// whether <paramref name="whileThisExists"/> is still there.
    ///
    /// This is what lets a wait be generous without being a gamble. A flat timeout has to be short enough
    /// to survive the other side vanishing, which then makes it too short for the other side doing real
    /// work. Bounding it by the other side's *presence* instead separates the two: it can be long, because
    /// the case a short timeout was protecting against now ends the wait immediately rather than after the
    /// full period.
    ///
    /// The presence test is exact rather than a heuristic. A named event lives while a handle to it is
    /// open, and the Doctor holds its "watching" event open for precisely as long as it watches — so if
    /// that Doctor dies, however abruptly, Windows closes the handle, the event ceases to exist, and the
    /// next slice here sees it gone. Nothing has to be cleaned up for this to be true.
    ///
    /// <paramref name="ceiling"/> remains as a backstop, for a Doctor that is alive but not answering.
    /// </summary>
    public static bool WaitWhileTheOtherSideLives(
        string name,
        string whileThisExists,
        TimeSpan ceiling,
        TimeSpan slice
    )
    {
        try
        {
            using var handle = TryOpen(name);
            if (handle == null)
                return false;
            var waited = TimeSpan.Zero;
            while (waited < ceiling)
            {
                var thisSlice = slice < ceiling - waited ? slice : ceiling - waited;
                if (handle.WaitOne(thisSlice))
                    return true;
                waited += thisSlice;
                // Checked AFTER waiting rather than before, so that an event already signalled is honoured
                // even if the other side has since gone. It did the work; we should not discard it.
                if (!Exists(whileThisExists))
                    return handle.WaitOne(TimeSpan.Zero);
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
