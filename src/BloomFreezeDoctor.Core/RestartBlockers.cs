namespace BloomFreezeDoctor;

/// <summary>
/// One still-running Bloom the Doctor is watching, the state it is in, and whether it holds the
/// single-instance token.
/// </summary>
/// <param name="ProcessId">The process id.</param>
/// <param name="State">What the detector currently makes of it.</param>
/// <param name="HoldsSingleInstanceToken">
/// From the Bloom's own session file. **Null means it did not say**, which is not the same as no - see
/// <see cref="RestartBlockers"/>.
/// </param>
public readonly record struct LiveBloom(
    int ProcessId,
    TargetState State,
    bool? HoldsSingleInstanceToken
);

/// <summary>
/// Works out which running Blooms actually stand between the user and a new Bloom.
///
/// This exists because "every Bloom we are watching" is the wrong answer, and answering it that way
/// offers to kill Blooms that were never in anybody's way. Bloom is single-instance through a mutex the
/// channels deliberately share, so normally at most one Bloom holds the token - but the Doctor watches
/// every Bloom it can see, and two kinds routinely hold nothing:
///
/// * an <c>--automation</c> run, which bypasses the token by design (Program.Main), and
/// * a Ctrl-held launch, which takes the token only if it happened to be first.
///
/// A developer running two worktrees has exactly this: two live Blooms, neither blocking anything.
/// Before the Doctor watched <c>--automation</c> runs at all they could not appear here; now they can,
/// which is what made this filter necessary.
/// </summary>
public static class RestartBlockers
{
    /// <summary>
    /// Whether this Bloom must go before a new one can start.
    ///
    /// **Null counts as blocking.** Null is "this Bloom did not tell us", and the Blooms that cannot tell
    /// us are precisely the old ones - which write no session file, or one written before the field
    /// existed - and an old Bloom is every bit as capable of holding the shared token as a new one. Read
    /// null as "no" and the Doctor leaves the real blocker running, starts a Bloom that finds the token
    /// held and quietly exits, and the user watches "Bloom will not start" happen again.
    ///
    /// The opposite mistake only costs a confirmation dialog naming a Bloom that need not have died - and
    /// that dialog says which ones we are unsure about, so the person decides.
    /// </summary>
    public static bool IsInTheWay(LiveBloom bloom) => bloom.HoldsSingleInstanceToken != false;

    /// <summary>Those of <paramref name="watched"/> that stand in the way of a restart.</summary>
    public static IReadOnlyList<LiveBloom> InTheWay(IEnumerable<LiveBloom> watched) =>
        watched.Where(IsInTheWay).ToList();

    /// <summary>
    /// How to describe one blocking Bloom to the person deciding whether to end it. Names what it is
    /// doing, and says plainly when we are only guessing that it is in the way.
    /// </summary>
    public static string Describe(LiveBloom bloom)
    {
        var state = bloom.State switch
        {
            TargetState.Healthy => "running normally",
            TargetState.Suspect => "not responding at the moment",
            TargetState.Frozen => "frozen",
            TargetState.Zombie => "running with no window",
            _ => bloom.State.ToString().ToLowerInvariant(),
        };
        // Said out loud rather than hidden, because this is the case where ending it may achieve nothing.
        var certainty =
            bloom.HoldsSingleInstanceToken == true
                ? ""
                : ", which is too old to tell us whether it is the one blocking a restart";
        return $"process {bloom.ProcessId} ({state}{certainty})";
    }
}
