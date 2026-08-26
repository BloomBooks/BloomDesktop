using System.Text;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// Marks the long, skim-past parts of a report — the log tail, the other threads' stacks, the installer
/// log — so that the tracker card can show them collapsed while the file on disk stays ordinary Markdown.
///
/// **Why markers rather than just writing the HTML.** The same report text has two readers. On a card it
/// wants `&lt;details&gt;`, because a reader meets three screens of thread stacks before the part that says
/// what happened. On disk it is opened in a text editor, where `&lt;details&gt;&lt;pre&gt;` and
/// `&amp;lt;` escapes are noise, and a fenced block is exactly right. Writing the HTML in the collector
/// would serve the card and spoil the file; writing markers serves both, and puts the one piece of
/// knowledge that is really about the tracker — what YouTrack renders — in the submitter, where the rest
/// of that knowledge already lives.
///
/// The markers are HTML comments, so a Markdown viewer ignores them and a text editor shows one
/// self-explanatory line.
/// </summary>
public static class CollapsibleSections
{
    /// <summary>Opens a region that a card should show collapsed. <paramref name="label"/> names it.</summary>
    public const string BeginPrefix = "<!-- collapse-on-card: ";

    /// <summary>Closes the most recently opened region.</summary>
    public const string End = "<!-- /collapse-on-card -->";

    /// <summary>Marks the start of a collapsible region.</summary>
    public static void Begin(StringBuilder text, string label) =>
        text.AppendLine(BeginPrefix + label + " -->");

    /// <summary>Marks the end of a collapsible region.</summary>
    public static void Finish(StringBuilder text) => text.AppendLine(End);

    /// <summary>
    /// Rewrites the marked regions as collapsed `&lt;details&gt;` blocks for a tracker card, and leaves
    /// everything else untouched.
    ///
    /// Three details of YouTrack's rendering shape this, and the first two are borrowed from Bloom's own
    /// problem reporter, which has been doing this for years:
    ///
    /// - Markdown is **not** styled inside `&lt;details&gt;`, so the content becomes `&lt;pre&gt;` and any
    ///   fence lines that were there for the on-disk reader are dropped.
    /// - The label goes *outside* as a heading, since Bloom's version does that and is known to render.
    ///   `&lt;summary&gt;` would be nicer and is probably allowed too, but "probably" is not a good enough
    ///   reason to risk a stray tag showing as text on every card.
    /// - Inside `&lt;pre&gt;` the content is HTML, so `&lt;` and `&amp;` must be escaped or a generic type
    ///   in a stack frame (`List&lt;int&gt;`) is read as a tag and silently disappears.
    /// </summary>
    public static string RenderForACard(string report)
    {
        if (string.IsNullOrEmpty(report) || !report.Contains(BeginPrefix))
            return report;

        var output = new StringBuilder();
        var collapsing = false;
        foreach (var line in report.Replace("\r\n", "\n").Split('\n'))
        {
            if (!collapsing && line.StartsWith(BeginPrefix, StringComparison.Ordinal))
            {
                var label = line.Substring(BeginPrefix.Length).Replace("-->", "").Trim();
                output.AppendLine();
                output.AppendLine("#### " + label);
                output.AppendLine("<details>");
                output.AppendLine("<pre>");
                collapsing = true;
                continue;
            }
            if (collapsing && line.StartsWith(End, StringComparison.Ordinal))
            {
                output.AppendLine("</pre>");
                output.AppendLine("</details>");
                output.AppendLine();
                collapsing = false;
                continue;
            }
            if (collapsing)
            {
                // The fences existed for the on-disk reader; inside <pre> they would show as backticks.
                if (line.TrimEnd() == "```")
                    continue;
                output.AppendLine(Escape(line));
                continue;
            }
            output.AppendLine(line);
        }

        // An unterminated region would otherwise swallow the rest of the card into a collapsed block.
        if (collapsing)
        {
            output.AppendLine("</pre>");
            output.AppendLine("</details>");
        }
        return output.ToString();
    }

    private static string Escape(string line) =>
        line.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
