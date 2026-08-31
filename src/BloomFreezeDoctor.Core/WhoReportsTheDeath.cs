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
/// A pure function with its own tests because this decision has been wrong twice, both times silently,
/// and both times it cost a whole manual run to find out:
///
/// - Claiming the death in the discovery sweep and then calling the examination, which begins by refusing
///   a death already claimed. Every crash was "examined" by a call that returned immediately, so a real
///   crashing Bloom produced no report at all.
/// - Letting the exit examination run alongside the crash-dump path. Both filed, the outbox's fingerprint
///   dedup kept whichever arrived first, and on a real run that was the DUMPLESS one - so the card got the
///   thinner report and the dump we held a dying Bloom open to collect was left on the user's machine.
///
/// Three booleans with four outcomes is small enough to look obviously right and be wrong, which is exactly
/// the kind of thing to pin down in a table.
/// </summary>
public static class WhoReportsTheDeath
{
    /// <summary>
    /// Which path should report this death.
    ///
    /// Order matters, and it is the order of how much better the alternative is than a bare examination:
    /// our own doing needs no report at all; a dump-bearing report beats a dumpless one; and failing both,
    /// the first pass to claim it keeps it.
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
        if (weEndedIt)
            return ExitExamination.WeCausedIt;
        if (aDumpIsBeingReported)
            return ExitExamination.TheDumpHasIt;
        if (alreadyClaimed)
            return ExitExamination.AlreadyClaimed;
        return ExitExamination.Examine;
    }
}
