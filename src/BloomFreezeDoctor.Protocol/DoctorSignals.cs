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

    /// <summary>Set by Bloom as it dies, to ask for a dump while the process still exists.</summary>
    public static string DumpRequestName(int processId) =>
        $@"Local\BloomFreezeDoctor.dumpme.{processId}";

    /// <summary>
    /// Set by the Doctor the moment it takes up a dump request, before it begins the work.
    ///
    /// It exists so that Bloom can be impatient about one thing and patient about another. Waiting for a
    /// dump used to be a single flat three seconds, and that number was set against a spike measurement of
    /// 1.4 seconds for "a real Bloom" — while a measured dump on a fast developer machine, of a Bloom that
    /// had been running two minutes, took 2.4 of the 3 seconds allowed. On a user's slower machine, with a
    /// Bloom that has been up for days and a system short of memory, it would plainly overrun.
    ///
    /// And overrunning does not merely delay the dump, it loses it: the dying Bloom is what actually writes
    /// the dump, over the diagnostics pipe, so when Bloom stops waiting and exits the write is aborted and
    /// the report falls back to having no managed stacks at all. The budget was smallest exactly where the
    /// need was greatest.
    ///
    /// A flat 60 seconds would trade that for the opposite failure — a Doctor that died between Bloom's
    /// "is anyone watching" check and its wait would hold a crashing Bloom for a minute, for nothing. With
    /// this signal Bloom can tell "nobody picked it up" from "it is underway": give up quickly on the
    /// first, and wait generously on the second.
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
