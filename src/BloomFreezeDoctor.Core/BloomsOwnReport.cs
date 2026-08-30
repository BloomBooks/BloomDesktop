using BloomFreezeDoctor.Protocol;

namespace BloomFreezeDoctor;

/// <summary>
/// Whether Bloom's own problem report still accounts for what the Doctor is looking at.
///
/// When a user files a problem report from inside Bloom, a card exists already, and a Doctor card about
/// the same trouble is a duplicate. So the Doctor goes quiet. The question this class answers is: for how
/// long?
///
/// **It used to be "for the rest of the run", and that was too long.** Filing a report is not the end of a
/// session - developers and alpha testers routinely report something non-fatal and carry straight on
/// working, for hours. A freeze that afternoon has nothing to do with the layout bug they reported that
/// morning, and silence about it is a real loss, while the card that would have prevented is long since
/// filed and forgotten.
///
/// So the suppression now expires. After <see cref="Window"/> the Doctor is armed again.
/// </summary>
public static class BloomsOwnReport
{
    /// <summary>
    /// How long Bloom's own report keeps the Doctor quiet.
    ///
    /// Short on purpose. Its job is only to cover the trouble that was actually reported - the user files,
    /// and anything the Doctor notices in the next few minutes is plausibly the same thing still playing
    /// out. Beyond that the two are unrelated events that merely happened in one long session.
    ///
    /// Erring short is also the cheaper mistake: too long loses a real freeze silently, while too short
    /// costs a duplicate card that names the trouble it duplicates.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    /// <summary>
    /// True when Bloom's own report is recent enough that the Doctor should still say nothing.
    ///
    /// A session with no report at all, or one whose report has aged out, returns false - the Doctor is
    /// free to file, subject to every other guard.
    ///
    /// **A report with no timestamp keeps the old all-run suppression.** That means a Bloom built before
    /// the timestamp existed, which cannot tell us when it reported; suppressing for the run is exactly
    /// what such a Bloom got before this change, and inventing a time for it would be guessing.
    /// </summary>
    public static bool StillAccountsForTheTrouble(DoctorSession? session, DateTimeOffset now)
    {
        if (session?.BloomAlreadyReported != true)
            return false;
        if (session.ReportedAtUtc == null)
            return true;
        // Guard the clock going backwards (a time-zone change, an NTP correction) as well as forwards: a
        // report stamped in the future would otherwise suppress for as long as the skew lasts.
        return (now - session.ReportedAtUtc.Value).Duration() < Window;
    }
}
