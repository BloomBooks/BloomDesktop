namespace BloomFreezeDoctor.Outbox;

/// <summary>
/// What a recurrence should contribute to a card that already exists.
///
/// A recurrence deliberately attaches almost nothing: two reports sharing a fingerprint share their
/// reason, Bloom's version, the channel and the top frames of the UI thread, so a second dump is usually a
/// near-duplicate of the first at some 16 MB, and a card carrying several of them becomes unreadable
/// exactly when it matters most. See BuildRecurrenceComment.
///
/// That reasoning holds only while the card HAS a first dump to be a near-duplicate of, and often it does
/// not. The Doctor only gets a dump when Bloom notices it is crashing and asks to be dumped; a Bloom that
/// dies without noticing - an unhandled exception on a thread it does not control, which is the case the
/// Doctor exists for - leaves the exit examination to report it, and that runs after the process has gone,
/// when no dump can be taken. So the first card for a problem frequently has no dump at all, and under the
/// blanket rule the first occurrence that COULD have supplied one silently did not: the comment said the
/// evidence was "near enough a copy" of what was already there, when what was already there was nothing.
///
/// A real run showed exactly that, and the dump we had held a dying Bloom open for three seconds to
/// collect stayed on the user's machine.
/// </summary>
public static class RecurrenceArtifacts
{
    /// <summary>
    /// Which of a recurrence's artifacts are worth adding to the card, given what it already carries.
    ///
    /// One rule: a crash dump, and only when the card has none. That gives a card exactly one dump per
    /// problem - the thing a developer needs and cannot reconstruct - without letting a machine in a bad
    /// state pile 16 MB onto the same card every time it crashes.
    ///
    /// Logs are not included even when the card has no log attachment, because they are not attached in
    /// the first place: the report body inlines the tail of Bloom's log in a collapsed section, so the
    /// card already carries one and a second adds noise rather than evidence.
    /// </summary>
    /// <param name="artifactPaths">The recurrence bundle's artifacts, as full paths.</param>
    /// <param name="cardAlreadyHasADump">
    /// Whether the card already carries a dump BY ANY ROUTE - attached, or linked in the support bucket
    /// because it was too large to attach.
    ///
    /// Asking only about attachments was wrong, and wrong in the way that produces a confident false
    /// statement rather than a missing one. A dump a few hundred bytes over the attachment ceiling is
    /// uploaded to the bucket and linked from a comment, so the card HAS the dump and has no attachment;
    /// the recurrence then saw no attachment, uploaded a second copy, and announced that the card had none.
    /// Measured: 8,389,030 bytes went to the bucket, and the next run's 8,054,542 was attached beside it.
    /// </param>
    public static IReadOnlyList<string> WorthAttaching(
        IEnumerable<string> artifactPaths,
        bool cardAlreadyHasADump
    )
    {
        if (cardAlreadyHasADump)
            return Array.Empty<string>();
        return artifactPaths.Where(path => IsADump(Path.GetFileName(path))).ToList();
    }

    /// <summary>
    /// Whether any of these - a card's attachment names, and the text of its description and comments -
    /// shows that it already carries a crash dump.
    /// </summary>
    public static bool ShowsADump(IEnumerable<string> attachmentNames, IEnumerable<string> cardText)
    {
        if (attachmentNames.Any(IsADump))
            return true;
        // A dump too large to attach appears as a link into the support bucket. Both halves are required:
        // the bucket name alone would match a book somebody uploaded, and ".dmp" alone would match this
        // very comment's own prose about a dump that could NOT be sent.
        return cardText.Any(text =>
            text.Contains(SupportBucketMarker, StringComparison.OrdinalIgnoreCase)
            && text.Contains(".dmp", StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>
    /// Enough of the support bucket's URL to recognise a link into it. Matches
    /// SupportFileUploader.BucketName, and is a literal here for the same reason that one is: this project
    /// does not depend on Bloom.
    /// </summary>
    private const string SupportBucketMarker = "bloom-problem-books";

    /// <summary>
    /// Whether a file name is a crash dump. Extension only: the Doctor names its own dumps
    /// <c>bloom-1234.dmp</c>, but a card can also carry one a person attached by hand under any name.
    /// </summary>
    private static bool IsADump(string fileName) =>
        fileName.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase);
}
