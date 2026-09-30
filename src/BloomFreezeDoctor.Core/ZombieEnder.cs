using System.Diagnostics;
using BloomFreezeDoctor.Protocol;

namespace BloomFreezeDoctor;

/// <summary>What the Doctor knows when deciding whether to end a stuck Bloom.</summary>
public readonly record struct ZombieDecisionFacts
{
    /// <summary>What the detector concluded. Only a zombie qualifies.</summary>
    public required TargetState State { get; init; }

    /// <summary>Whether a report has already been gathered. We never kill before recording the evidence.</summary>
    public required bool ReportGathered { get; init; }

    /// <summary>How long since we decided it was a zombie.</summary>
    public required TimeSpan SinceDetected { get; init; }

    /// <summary>
    /// True if a debugger can account for the state this process is in — one is attached, or left too
    /// recently to rule out. Killing a process somebody may be debugging is not ours to do.
    /// </summary>
    public required bool DebuggerCouldExplainIt { get; init; }

    /// <summary>
    /// True if Bloom says it is in the middle of a long operation, or its activity mentions saving or
    /// publishing. Killing a Bloom mid-save is the one way this feature could do real harm.
    /// </summary>
    public required bool WorkInProgress { get; init; }

    /// <summary>True if the user asked us not to do this.</summary>
    public required bool DisabledBySetting { get; init; }
}

/// <summary>Whether to end a stuck Bloom, and why or why not.</summary>
public readonly record struct ZombieDecision
{
    /// <summary>Whether to go ahead.</summary>
    public required bool ShouldEnd { get; init; }

    /// <summary>The reasoning, for the Doctor's log and the report.</summary>
    public required string Explanation { get; init; }
}

/// <summary>
/// Ends a Bloom whose UI is gone but whose process is still running — the state a user experiences as
/// "Bloom won't start", because the dead process still holds the single-instance token.
///
/// **This is the only thing the Doctor does that is not read-only, so the guards matter more than the
/// mechanism.** In particular it will not touch a *frozen* Bloom (state 1): a frozen Bloom may be holding
/// edits that live in the WebView2 DOM and have not yet reached C#, and killing it would throw away the
/// user's work. A zombie has no UI at all, so there is nothing left for the user to reach or save — which
/// is exactly what makes ending it safe.
///
/// Ending it is also a complete cure rather than half a fix: Bloom's single-instance token is a lock file
/// holding a process id, and the next Bloom takes the lock as soon as that process is gone. Nothing needs
/// cleaning up by hand.
/// </summary>
public static class ZombieEnder
{
    /// <summary>
    /// How long to wait after detecting a zombie before ending it, so that a shutdown which is merely slow
    /// gets the chance to finish by itself. Bloom's own safety net gives up after 20 seconds, so this is
    /// comfortably beyond it.
    /// </summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(45);

    /// <summary>How long to give Bloom to exit under its own power before killing it.</summary>
    public static readonly TimeSpan SelfExitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Decides whether to end a stuck Bloom. Pure, so every guard can be tested without a process to kill.
    /// </summary>
    public static ZombieDecision Decide(ZombieDecisionFacts facts)
    {
        if (facts.DisabledBySetting)
            return No("ending stuck Blooms is switched off in the Doctor's settings");

        if (facts.State != TargetState.Zombie)
            return No(
                $"this Bloom is {facts.State.ToString().ToLowerInvariant()}, not a zombie. A frozen Bloom "
                    + "may be holding edits that have not yet reached C#, so we never kill one"
            );

        if (facts.DebuggerCouldExplainIt)
            return No("a debugger is or was just attached to this Bloom, so it is not ours to end");

        if (!facts.ReportGathered)
            return No(
                "the evidence has not been gathered yet, and killing it first would destroy it"
            );

        if (facts.WorkInProgress)
            return No(
                "Bloom says it is still saving or publishing. Waiting is free; interrupting a save is not"
            );

        if (facts.SinceDetected < GracePeriod)
            return No(
                $"only {facts.SinceDetected.TotalSeconds:F0}s since we noticed; giving a slow shutdown "
                    + $"until {GracePeriod.TotalSeconds:F0}s to finish on its own"
            );

        return new ZombieDecision
        {
            ShouldEnd = true,
            Explanation =
                "its UI is gone, the evidence is gathered, nothing is in flight, and it is holding the "
                + "single-instance token that stops Bloom starting again",
        };
    }

    /// <summary>
    /// Ends the process, preferring to ask rather than to kill.
    ///
    /// Asking is much better than killing when it works: Bloom exits under its own power, so its
    /// `ProcessExit` handler runs, its single-instance token is released properly and its own record of the
    /// shutdown is written. Killing is the fallback for a Bloom too far gone to answer — which, since we
    /// only do this to a zombie, is a real possibility.
    /// </summary>
    public static ZombieEndOutcome End(int processId, DateTime startedAt)
    {
        // Before anything else, prove this id is still the Bloom we mean. Windows reuses process ids, and
        // everything below acts on the id alone: the quit signal is named after it, and the kill takes it
        // straight from the process table. Both would land on whatever inherited the id.
        //
        // This is not theoretical for the quit signal in particular. A NEW Bloom handed the dead one's id
        // would be listening on exactly that event name and would dutifully shut itself down - the user
        // watching their working Bloom close for no reason they could see.
        if (!ProcessIdentity.IsStillTheSameProcess(processId, startedAt))
            return ZombieEndOutcome.AlreadyGone;

        // Ask first. Bloom's watchdog thread waits on this, and that thread is still running even when the
        // UI thread has been gone for minutes.
        if (DoctorSignals.TrySignal(DoctorSignals.QuitRequestName(processId)))
        {
            if (WaitForExit(processId, startedAt, SelfExitTimeout))
                return ZombieEndOutcome.ExitedOnRequest;
        }

        // Nobody listening, or it did not manage it. Kill it: a zombie is useless to the user and is
        // actively in their way. Re-check identity: the wait above can have let it exit and the id be
        // handed on in the meantime, which is precisely the window that makes this worth doing twice.
        try
        {
            if (!ProcessIdentity.IsStillTheSameProcess(processId, startedAt))
                return ZombieEndOutcome.AlreadyGone;
            using var process = Process.GetProcessById(processId);
            process.Kill();
            return WaitForExit(processId, startedAt, TimeSpan.FromSeconds(10))
                ? ZombieEndOutcome.Killed
                : ZombieEndOutcome.CouldNotEnd;
        }
        catch (ArgumentException)
        {
            // It went away while we were deciding. The best possible outcome.
            return ZombieEndOutcome.AlreadyGone;
        }
        catch (Exception)
        {
            return ZombieEndOutcome.CouldNotEnd;
        }
    }

    /// <summary>
    /// Waits for that particular process to go. Watches the identity, not just the id: an id that has been
    /// reused would otherwise read as "still running" for ever, and we would report CouldNotEnd about a
    /// Bloom that died on request.
    /// </summary>
    private static bool WaitForExit(int processId, DateTime startedAt, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (!ProcessIdentity.IsStillTheSameProcess(processId, startedAt))
                    return true;
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                    return true;
            }
            catch (ArgumentException)
            {
                return true; // gone
            }
            catch (Exception)
            {
                return false;
            }
            Thread.Sleep(250);
        }
        return false;
    }

    private static ZombieDecision No(string why) => new() { ShouldEnd = false, Explanation = why };
}

/// <summary>How an attempt to end a stuck Bloom turned out.</summary>
public enum ZombieEndOutcome
{
    /// <summary>Bloom exited under its own power, which is the outcome we want.</summary>
    ExitedOnRequest,

    /// <summary>It had to be killed from outside.</summary>
    Killed,

    /// <summary>It had already gone.</summary>
    AlreadyGone,

    /// <summary>It is still there. Reported, and left alone rather than retried indefinitely.</summary>
    CouldNotEnd,
}
