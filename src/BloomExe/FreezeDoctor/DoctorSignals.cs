// Explicit usings and nullable context: copied into BloomDesktop. See DoctorChannel.cs.
#nullable enable
using System;
using System.Threading;

namespace Bloom.FreezeDoctor;

// =====================================================================================================
//  THIRD PART OF THE CONTRACT BETWEEN BLOOM AND THE FREEZE DOCTOR. COPIED INTO BOTH REPOS.
//
//  Source of truth: BloomBooks/bloom-freeze-doctor, src/BloomFreezeDoctor.Core/Contract/DoctorSignals.cs
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
            return EventWaitHandle.OpenExisting(name);
        }
        catch (Exception)
        {
            // WaitHandleCannotBeOpenedException in the ordinary case: the other side is not there.
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
}
