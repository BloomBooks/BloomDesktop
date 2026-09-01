namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// The exception Bloom's own error handling last recorded, pulled out of Bloom's log.
///
/// This exists because of what a simulated unhandled exception produced. The Doctor caught it perfectly -
/// Bloom's fatal handler asked to be dumped, the dump was taken, the stack showed the whole crash path -
/// and the report's headlines were:
///
///     Verdict: Bloom was crashing and asked to be dumped before it died
///     The UI thread is blocked in System.Threading.WaitHandle.WaitOneCore.
///
/// Not one word about WHAT was thrown, which for a crash is the first thing anybody wants. The answer was
/// in the report, 370 lines down, inside the tail of Bloom's log:
///
///     exception = System.ApplicationException: FreezeSimulator was asked to throw
///
/// A fact that is present but unfindable is not much better than a fact that is missing, and headlines are
/// what a reader reads.
/// </summary>
public static class BloomsOwnException
{
    /// <summary>How Bloom's ProblemReportApi writes the exception into the log.</summary>
    private const string Marker = "exception = ";

    /// <summary>
    /// The last exception recorded in these log lines, or null if there is none.
    ///
    /// The LAST, because a session can log several and the one that matters is the one nearest the trouble
    /// we are reporting. Only the first line is taken: what follows is the stack, which the report already
    /// shows in a form of its own and which would swamp a headline.
    /// </summary>
    public static string? FindIn(IEnumerable<string> logLines)
    {
        string? found = null;
        foreach (var line in logLines)
        {
            var at = line.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                continue;
            var text = line.Substring(at + Marker.Length).Trim();
            // Guard against the marker appearing with nothing after it, which would otherwise produce a
            // headline announcing an exception and then naming none.
            if (text.Length > 0)
                found = text;
        }
        return found;
    }

    /// <summary>
    /// A headline naming it, or null. Cut to a length that belongs at the top of a report: some exception
    /// messages carry a whole file path or a book's entire name, and the full text is in the log below.
    /// </summary>
    public static string? Headline(IEnumerable<string> logLines)
    {
        var exception = FindIn(logLines);
        if (exception == null)
            return null;
        const int limit = 200;
        var shown = exception.Length <= limit ? exception : exception.Substring(0, limit) + "...";
        return $"Bloom's own error handling recorded: {shown}";
    }
}
