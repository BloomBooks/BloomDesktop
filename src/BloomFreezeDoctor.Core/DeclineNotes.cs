namespace BloomFreezeDoctor;

/// <summary>
/// Remembers what the discovery sweep has already said about a process it could not describe, so the log
/// records the reason once instead of every few seconds.
///
/// Without it, a Bloom that sits unadopted for a minute - a dozen discovery ticks that see the process,
/// fail to describe it, and move on without a word - leaves no trace of why. A freeze in that window goes
/// unreported, and afterwards there is nothing to work from.
///
/// The two obvious ways to fix that are both wrong. Logging every decline fills the file at a line every
/// five seconds for as long as the process lives; logging only the first loses it when the reason CHANGES,
/// which is exactly what a process part-way through starting does. So the pair is what counts: a reason is
/// said once per process, and again if the reason itself changes.
///
/// Deliberately holds ONE process rather than a dictionary keyed by process id. The Doctor watches one
/// Bloom, and a map keyed by pid accumulates stale entries for processes that have gone; a diagnostic is
/// not a good reason to take on that bookkeeping.
/// </summary>
public sealed class DeclineNotes
{
    private int _processId;
    private string? _reason;
    private DateTimeOffset _firstSeenAt;

    /// <summary>
    /// Whether this decline is worth a line in the log, remembering it either way.
    ///
    /// <paramref name="now"/> is passed in so a test does not have to wait.
    /// </summary>
    public bool ShouldSay(int processId, string reason, DateTimeOffset now)
    {
        if (_processId == processId && _reason == reason)
            return false;
        if (_processId != processId)
            _firstSeenAt = now;
        _processId = processId;
        _reason = reason;
        return true;
    }

    /// <summary>
    /// How long we had been declining this process before adopting it, or null if we never declined it -
    /// which is the normal case, and says nothing rather than "0s".
    ///
    /// Forgets the process as it answers: it has been adopted, so there is nothing left to suppress.
    /// </summary>
    public TimeSpan? HowLongWeWereDeclining(int processId, DateTimeOffset now)
    {
        if (_processId != processId)
            return null;
        var waited = now - _firstSeenAt;
        _processId = 0;
        _reason = null;
        return waited;
    }
}
