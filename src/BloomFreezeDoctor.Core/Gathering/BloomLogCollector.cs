using System.Diagnostics;
using System.Text;
using SIL.IO;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// Bloom's own log, its installer's log, and what Windows recorded about the crash.
///
/// The log is the section Bloom developers reach for first, so getting the *right* log matters more than
/// it might seem. See <see cref="BloomLogLocator"/>: choosing the most recently modified file is not
/// merely imprecise but systematically wrong in the restart-after-a-freeze case, which is the case this
/// tool exists for.
/// </summary>
public sealed class BloomLogCollector : IEvidenceCollector
{
    /// <summary>How many lines of the log tail to include. Enough for context, not the whole session.</summary>
    private const int TailLines = 120;

    /// <inheritdoc />
    public string Title => "Logs, and what Windows recorded";

    /// <inheritdoc />
    public TimeSpan Budget => TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public bool AppliesTo(GatherContext context) => true;

    /// <inheritdoc />
    public Task<ReportSection> CollectAsync(GatherContext context, CancellationToken cancellation)
    {
        var timer = Stopwatch.StartNew();
        var text = new StringBuilder();
        var artifacts = new List<string>();
        string? headline = null;

        headline = AppendBloomLog(text, context, artifacts);
        AppendVelopackLog(text, artifacts);
        var windowsSaid = AppendWindowsCrashRecords(text, context);

        // FailFast's reason, which exists nowhere else: it runs no managed handlers by design, so there is
        // no dump and nothing in Bloom's own log. Without this, failfast was the only crash kind whose
        // report never said what went wrong. Only if Bloom's log has not already supplied a headline -
        // that one is closer to Bloom's own account of itself.
        if (headline == null)
        {
            var failFast = BloomsOwnException.FindFailFastReason(windowsSaid);
            if (failFast != null)
                headline = $"Bloom called FailFast: {failFast}";
        }

        return Task.FromResult(
            new ReportSection
            {
                Title = Title,
                Body = text.ToString(),
                Duration = timer.Elapsed,
                Artifacts = artifacts,
                Headline = headline,
            }
        );
    }

    /// <summary>
    /// The tail of Bloom's log, plus the whole file as an attachment. Also looks for the line Bloom
    /// writes when its own shutdown safety net fires, since that identifies a distinct bug that would
    /// otherwise be mistaken for a user kill.
    /// </summary>
    private static string? AppendBloomLog(
        StringBuilder text,
        GatherContext context,
        List<string> artifacts
    )
    {
        text.AppendLine("**Bloom's log**");
        text.AppendLine();

        if (string.IsNullOrEmpty(context.BloomLogPath) || !RobustFile.Exists(context.BloomLogPath))
        {
            text.AppendLine(
                "We could not identify this Bloom's log file. Bloom recreates `Log.txt` on every run and "
                    + "falls back to a randomly-named `Log-tmpXXXX.txt` only when another Bloom already "
                    + "holds it, so we match on the log's own \"App Launched with\" line rather than "
                    + "guessing from timestamps — and here that match found nothing."
            );
            text.AppendLine();
            return null;
        }

        text.AppendLine($"`{context.BloomLogPath}`");
        text.AppendLine();

        // Attach the whole log FIRST, then read the tail out of our own copy - so the tail comes from a
        // file nobody else can touch, and the contended read happens once.
        //
        // CopyWhileInUse rather than RobustFile.Copy, and that is the substance of it: Bloom holds its log
        // open for writing for the whole of its run, and RobustFile.Copy is refused outright by such a file
        // - permanently, not transiently, so retrying cannot help. For a FREEZE the process is by
        // definition still alive, so the ordinary copy could only ever have worked for a Bloom that had
        // already exited. A real filed card (AUT-20929) went out with no log attached because of this.
        // AttachingTheLogTests pins both halves.
        //
        // Copy rather than reference, because Bloom overwrites Log.txt on its very next run - which, after
        // a freeze, is usually only minutes away.
        var whereToReadTheTail = context.BloomLogPath;
        string? attachmentFailure = null;
        try
        {
            var copy = Path.Combine(context.ArtifactDirectory, "bloom-log.txt");
            Directory.CreateDirectory(context.ArtifactDirectory);
            WindowsExitEvidenceCollector.CopyWhileInUse(context.BloomLogPath, copy);
            artifacts.Add(copy);
            whereToReadTheTail = copy;
        }
        catch (Exception e)
        {
            attachmentFailure = $"{e.GetType().Name}: {e.Message}";
        }

        string? headline = null;
        try
        {
            var lines = WindowsExitEvidenceCollector.ReadLastLines(whereToReadTheTail, TailLines);
            if (
                lines.Any(l =>
                    l.Contains(
                        WindowsExitEvidenceCollector.ForcedShutdownLogLine,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                text.AppendLine(
                    "> **Bloom's own shutdown safety net fired**: its log carries the line it writes when "
                        + "a clean shutdown stalls for 20 seconds and it forces the process out. That is a "
                        + "distinct bug from a crash, and one we would otherwise never hear about."
                );
                text.AppendLine();
                headline =
                    "Bloom's shutdown stalled and its 20-second safety net forced it to exit.";
            }

            // What was thrown, if Bloom's own error handling recorded it. For a crash this is the first
            // thing anybody wants, and before this it was only ever present 370 lines down inside the log
            // tail - see BloomsOwnException. It does not displace the shutdown headline above, which is a
            // rarer and more surprising finding.
            headline ??= BloomsOwnException.Headline(lines);

            // Errors first: Bloom marks them with "***Error", and a reader should not have to hunt.
            var errors = lines
                .Where(l => l.Contains("***Error", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (errors.Count > 0)
            {
                text.AppendLine($"Errors in the last {TailLines} lines:");
                text.AppendLine();
                text.AppendLine("```");
                foreach (var error in errors.TakeLast(20))
                    text.AppendLine(error);
                text.AppendLine("```");
                text.AppendLine();
            }

            // One sentence covering how much is shown AND whether the file itself is attached, because
            // writing those separately let them contradict each other: a real card said "The whole log (18
            // lines)" and then, further down, that the log could not be attached. Both were true - one is
            // about the text below, one about the artifact - and together they read as nonsense.
            var howMuch =
                lines.Count < TailLines
                    ? $"The whole log ({lines.Count} lines)"
                    : $"The last {TailLines} lines";
            text.AppendLine(
                attachmentFailure == null
                    ? $"{howMuch}, and the file itself is attached as `bloom-log.txt`:"
                    : $"{howMuch}. The file itself could not be attached, so this is all of it we have:"
            );
            text.AppendLine();
            // Collapsed on the card: the longest block in the report, and the least often the thing
            // somebody opened the card to read. See CollapsibleSections.
            CollapsibleSections.Begin(text, "Bloom's log");
            text.AppendLine("```");
            foreach (var line in lines)
                text.AppendLine(line);
            text.AppendLine("```");
            CollapsibleSections.Finish(text);
            text.AppendLine();
        }
        catch (Exception e)
        {
            text.AppendLine($"*(could not read the log: {e.GetType().Name}: {e.Message})*");
            text.AppendLine();
        }

        // Why the attachment failed, once, after the log itself - and deliberately not phrased as a failure
        // to READ, which is what the old shared catch said even when the tail had just been read fine.
        // The line above has already told the reader whether the file is attached; this only says why not.
        if (attachmentFailure != null)
        {
            text.AppendLine($"*(the attachment failed: {attachmentFailure})*");
            text.AppendLine();
        }
        return headline;
    }

    /// <summary>
    /// Velopack's log, which is where an update that went wrong leaves its story. Worth having because
    /// "Bloom started misbehaving after it updated itself" is a common shape of report.
    /// </summary>
    private static void AppendVelopackLog(StringBuilder text, List<string> artifacts)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bloom",
            "velopack.log"
        );
        if (!RobustFile.Exists(path))
        {
            text.AppendLine("**Installer log (Velopack)**: no `velopack.log` found.");
            text.AppendLine();
            return;
        }
        try
        {
            var lines = WindowsExitEvidenceCollector.ReadLastLines(path, 25);
            CollapsibleSections.Begin(text, "Installer log (Velopack)");
            text.AppendLine("```");
            foreach (var line in lines)
                text.AppendLine(line);
            text.AppendLine("```");
            CollapsibleSections.Finish(text);
        }
        catch (Exception e)
        {
            text.AppendLine($"*(could not read it: {e.GetType().Name})*");
        }
        text.AppendLine();
    }

    /// <summary>
    /// Windows' own record: Application Error / Application Hang / .NET Runtime entries naming Bloom or
    /// its WebView2 children, and any Windows Error Reporting folders written around the same time.
    /// </summary>
    private static List<string> AppendWindowsCrashRecords(StringBuilder text, GatherContext context)
    {
        text.AppendLine("**What Windows recorded**");
        text.AppendLine();
        // Kept as well as printed: FailFast's reason lives only here, and it belongs in the headlines.
        var messages = new List<string>();
        var found = 0;
        try
        {
            using var log = new EventLog("Application");
            var since = DateTime.Now - TimeSpan.FromHours(2);
            for (var i = log.Entries.Count - 1; i >= 0 && i > log.Entries.Count - 500; i--)
            {
                EventLogEntry entry;
                try
                {
                    entry = log.Entries[i];
                }
                catch (Exception)
                {
                    continue;
                }
                if (entry.TimeGenerated < since)
                    break;
                var isRelevantSource =
                    entry.Source.Equals("Application Error", StringComparison.OrdinalIgnoreCase)
                    || entry.Source.Equals("Application Hang", StringComparison.OrdinalIgnoreCase)
                    || entry.Source.StartsWith(".NET Runtime", StringComparison.OrdinalIgnoreCase);
                if (!isRelevantSource)
                    continue;
                var message = entry.Message ?? "";
                if (
                    !message.Contains("Bloom", StringComparison.OrdinalIgnoreCase)
                    && !message.Contains("msedgewebview2", StringComparison.OrdinalIgnoreCase)
                )
                    continue;

                text.AppendLine(
                    $"- {entry.TimeGenerated:HH:mm:ss} **{entry.Source}** (event {entry.InstanceId})"
                );
                messages.Add(message);
                text.AppendLine("```");
                text.AppendLine(Shorten(message, 1200));
                text.AppendLine("```");
                if (++found >= 5)
                    break;
            }
        }
        catch (Exception e)
        {
            text.AppendLine($"*(could not read the Application event log: {e.GetType().Name})*");
        }

        if (found == 0)
            text.AppendLine(
                "Nothing in the Application event log about Bloom in the last two hours. For a freeze "
                    + "that is expected — Windows only logs a hang when it decides the app is not coming "
                    + "back — so this is not evidence that nothing was wrong."
            );
        text.AppendLine();

        AppendWerFolders(text);
        return messages;
    }

    private static void AppendWerFolders(StringBuilder text)
    {
        text.AppendLine("**Windows Error Reporting**");
        text.AppendLine();
        var reported = 0;
        foreach (
            var root in new[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft",
                    "Windows",
                    "WER"
                ),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Microsoft",
                    "Windows",
                    "WER"
                ),
            }
        )
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (
                    var directory in Directory
                        .EnumerateDirectories(root, "*Bloom*", SearchOption.AllDirectories)
                        .OrderByDescending(Directory.GetLastWriteTime)
                        .Take(5)
                )
                {
                    text.AppendLine(
                        $"- `{directory}` (written {Directory.GetLastWriteTime(directory):yyyy-MM-dd HH:mm})"
                    );
                    reported++;
                }
            }
            catch (Exception)
            {
                // The machine-wide archive normally needs administrator rights. Per the plan we try and
                // move on without ever prompting.
            }
        }
        if (reported == 0)
            text.AppendLine(
                "No Bloom reports found (the machine-wide archive usually needs admin rights)."
            );
        text.AppendLine();
    }

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max - 1) + "…";
}
