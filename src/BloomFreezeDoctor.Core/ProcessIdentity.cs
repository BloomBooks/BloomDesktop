using System.Diagnostics;

namespace BloomFreezeDoctor;

/// <summary>
/// Tells whether a process id still refers to the process we think it does.
///
/// **A process id is not an identity.** Windows hands ids out of a pool and reuses them, so
/// <c>Process.GetProcessById(id)</c> succeeding proves only that SOMETHING with that id is running. The
/// Doctor spends its life holding ids of Blooms that are frozen, dying, or already dead, and acting on one
/// of those after the id has been handed to somebody else means reading another process's evidence, or -
/// on the paths that end a stuck Bloom - killing a stranger.
///
/// **Why nothing has gone wrong so far, and why that is not a reason to leave it.** While the Doctor is
/// watching a Bloom it holds an open handle to that process (see <see cref="WindowsTargetProbe"/>), and
/// Windows will not reuse an id while any handle to the process object remains open. So every id the
/// supervisor currently acts on is pinned, and the reuse cannot happen. But that safety is a side effect
/// of a line in another class whose stated purpose is detecting a debugger, it ends the moment the probe
/// is disposed, and nothing would fail if somebody removed it. An identity check makes each call site
/// safe on its own terms rather than by an argument spanning three files.
///
/// The pair is the id plus the process's start time, which is the conventional Windows identity: an id
/// that has been reused necessarily belongs to a process that started later than the one we knew.
/// </summary>
public static class ProcessIdentity
{
    /// <summary>
    /// True when <paramref name="processId"/> is running AND is still the process that started at
    /// <paramref name="startedAt"/>.
    ///
    /// Compared exactly, not within a tolerance, and deliberately. Both values come from the same kernel
    /// field, so repeated reads agree to the tick; a tolerance would buy nothing and would blind us to the
    /// case this exists for, since a busy machine can genuinely reuse an id within a second. Where the
    /// comparison is wrong it is wrong in the safe direction - we treat a live process as gone, and so
    /// decline to act on it, rather than acting on the wrong one.
    /// </summary>
    public static bool IsStillTheSameProcess(int processId, DateTime startedAt)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited && process.StartTime == startedAt;
        }
        catch (Exception)
        {
            // No such id, or we may not ask. Either way we cannot confirm it is ours, and "not confirmed"
            // has to mean no: every caller is about to either believe its evidence or end it.
            return false;
        }
    }
}
