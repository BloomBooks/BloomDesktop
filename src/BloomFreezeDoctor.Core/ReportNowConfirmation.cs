namespace BloomFreezeDoctor;

/// <summary>
/// The question the window asks before "Report now" creates a real tracker card.
///
/// "Report now" always asks. It gathers a fresh report and files it whatever the Doctor would otherwise
/// have decided, and a card filed by accident wastes somebody's time at the other end. Asking only when a
/// filing guard was being overridden left the most ordinary case silent: a healthy Bloom on an Alpha or
/// Beta build has no guard to override, so one click on a button revealed for testing produced a real
/// card with no warning at all (BL-16719).
///
/// This lives apart from the window so the words can be tested without one.
/// </summary>
public static class ReportNowConfirmation
{
    /// <summary>
    /// Everything standing between this click and a card, in words fit to show someone: the filing
    /// guards being overridden, plus - when the Doctor itself sees nothing wrong with this Bloom - that.
    /// Empty when the Doctor would have filed a report about this Bloom of its own accord.
    /// </summary>
    public static IReadOnlyList<string> Reasons(
        TargetState state,
        IReadOnlyList<string> filingBlockers
    )
    {
        var reasons = new List<string>();
        // First, because it is the reason a person is most likely to have forgotten: the button is on
        // screen only because CTRL is held, not because anything has happened.
        switch (state)
        {
            case TargetState.Healthy:
                reasons.Add(
                    "the Freeze Doctor has not found anything wrong with this Bloom; it is running normally"
                );
                break;
            case TargetState.Suspect:
                reasons.Add(
                    "this Bloom has only just stopped answering; the Freeze Doctor has not yet decided "
                        + "that it is frozen"
                );
                break;
        }
        reasons.AddRange(filingBlockers);
        return reasons;
    }

    /// <summary>
    /// The text of the confirmation, naming the tracker project the card would land in. Never empty:
    /// even a genuinely frozen Bloom on a build that may file gets asked, because this is the one
    /// button that files on a click.
    /// </summary>
    public static string Message(
        TargetState state,
        IReadOnlyList<string> filingBlockers,
        string project
    )
    {
        var reasons = Reasons(state, filingBlockers);
        var question = $"Go ahead and create a real card in {project}?";
        if (reasons.Count == 0)
        {
            return "\"Report now\" gathers a fresh report on this Bloom and files it on the tracker."
                + Environment.NewLine
                + Environment.NewLine
                + question;
        }
        var bullets = string.Join(Environment.NewLine, reasons.Select(r => "  • " + r));
        return "The Freeze Doctor would not normally file this report:"
            + Environment.NewLine
            + Environment.NewLine
            + bullets
            + Environment.NewLine
            + Environment.NewLine
            + "\"Report now\" files anyway. "
            + question;
    }
}
