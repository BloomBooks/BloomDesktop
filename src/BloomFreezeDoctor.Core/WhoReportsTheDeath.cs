namespace BloomFreezeDoctor;

/// <summary>
/// What the exit examination should do about a Bloom that has gone.
/// </summary>
public enum ExitExamination
{
    /// <summary>Nobody else has this death. Examine it, and report if the evidence warrants.</summary>
    Examine,

    /// <summary>We ended this Bloom ourselves, so its untidy exit is our doing and not a problem.</summary>
    WeCausedIt,

    /// <summary>Bloom asked to be dumped as it crashed; that report covers this death, with the dump.</summary>
    TheDumpHasIt,

    /// <summary>Some earlier pass already claimed this death.</summary>
    AlreadyClaimed,
}

/// <summary>
/// Decides which of the Doctor's paths owns the report for a Bloom that has just died.
///
/// A pure function with its own tests because three booleans with four outcomes is small enough to look
/// obviously right and be wrong, and wrong silently. Two mistakes it guards against:
///
/// - Claiming the death in the discovery sweep before calling an examination that begins by refusing a
///   claimed death, so every crash is "examined" by a call that returns at once and nothing is reported.
/// - Letting the exit examination run alongside the crash-dump path, so both file, the outbox's
///   fingerprint dedup keeps whichever arrives first, and the card can get the DUMPLESS report while the
///   dump we held a dying Bloom open to collect is left on the user's machine.
/// </summary>
public static class WhoReportsTheDeath
{
    /// <summary>
    /// Which path should report this death.
    ///
    /// "Already claimed" is tested FIRST, and that ordering is load-bearing: an earlier pass has not just
    /// decided, it has ACTED - logged its reason and released the process handle - so a later pass must do
    /// nothing at all, whatever the reason would have been. Testing the reasons first would let a second
    /// pass re-take the same branch, log the same reason again and dispose an already-disposed handle.
    ///
    /// After that it is the order of how much better the alternative is than a bare examination: our own
    /// doing needs no report at all, and a dump-bearing report beats a dumpless one.
    /// </summary>
    /// <param name="weEndedIt">We asked this Bloom to stop, or killed it.</param>
    /// <param name="aDumpIsBeingReported">
    /// Bloom asked to be dumped because it was crashing, and that gather is under way or done.
    /// </param>
    /// <param name="alreadyClaimed">An earlier pass has already claimed this death.</param>
    public static ExitExamination Decide(
        bool weEndedIt,
        bool aDumpIsBeingReported,
        bool alreadyClaimed
    )
    {
        if (alreadyClaimed)
            return ExitExamination.AlreadyClaimed;
        if (weEndedIt)
            return ExitExamination.WeCausedIt;
        if (aDumpIsBeingReported)
            return ExitExamination.TheDumpHasIt;
        return ExitExamination.Examine;
    }
}
