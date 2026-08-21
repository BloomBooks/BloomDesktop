using System.Diagnostics;

namespace BloomFreezeDoctor;

/// <summary>
/// Gathers the after-the-fact evidence that <see cref="ExitClassifier"/> judges: Windows' own opinion
/// of the crash, Windows Error Reporting's files, the tail of Bloom's log, and whether the whole
/// machine went down.
///
/// Every reader here is individually failure-tolerant. A missing Event Log entry and a *failure to read*
/// the Event Log look identical to the classifier, which is a real limitation and is why Phase 1 stays
/// quiet by default rather than treating absence as proof of anything.
/// </summary>
public sealed class WindowsExitEvidenceCollector
{
    /// <summary>
    /// How far either side of the process's death to look for Event Log and WER entries. Generous,
    /// because WER can take a while to write its report, and stingy enough not to adopt an unrelated
    /// crash from earlier in the day.
    /// </summary>
    public static readonly TimeSpan EvidenceWindow = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The line Bloom's <c>ProgramExit</c> writes when a clean shutdown stalls and it forces the
    /// process out. Matching it is what separates that real bug from a user kill, since both exit
    /// with code 1.
    /// </summary>
    public const string ForcedShutdownLogLine =
        "Forcing Bloom to close after normal shutdown timed out";

    /// <summary>
    /// Assembles the evidence for one exit. <paramref name="diedAt"/> is when we noticed the process
    /// had gone, and <paramref name="logPath"/> the log we identified as this process's, if any.
    /// </summary>
    public ExitEvidence Collect(
        int processId,
        DateTime diedAt,
        DateTime startedAt,
        string? logPath,
        int? exitCode,
        bool debuggerCouldExplainIt,
        bool neverFile,
        bool? cleanExitProofPresent = null,
        int? shutdownPhaseReached = null
    )
    {
        return new ExitEvidence
        {
            ExitCode = exitCode,
            DebuggerCouldExplainIt = debuggerCouldExplainIt,
            NeverFile = neverFile,
            CleanExitProofPresent = cleanExitProofPresent,
            ShutdownPhaseReached = shutdownPhaseReached,
            HasEventLogCrashEntry = LookForCrashEntry(processId, diedAt),
            HasWerReport = LookForWerReport(diedAt),
            LogShowsForcedShutdown = LogEndsWithForcedShutdown(logPath),
            MachineWentDown = DidMachineGoDown(startedAt, diedAt),
        };
    }

    /// <summary>
    /// Looks for a Windows "Application Error" (1000), "Application Hang" (1002) or .NET Runtime entry
    /// naming Bloom, close to when the process died.
    /// </summary>
    private static bool LookForCrashEntry(int processId, DateTime diedAt)
    {
        try
        {
            using var log = new EventLog("Application");
            // Walk backwards: the entries we want are the most recent ones, and the Application log
            // can hold tens of thousands.
            for (var i = log.Entries.Count - 1; i >= 0 && i > log.Entries.Count - 400; i--)
            {
                EventLogEntry entry;
                try
                {
                    entry = log.Entries[i];
                }
                catch (Exception)
                {
                    continue; // entries can be aged out from under us mid-walk
                }

                if (entry.TimeGenerated < diedAt - EvidenceWindow)
                    break; // older than we care about, and they only get older from here
                if (entry.TimeGenerated > diedAt + EvidenceWindow)
                    continue;

                var isCrashSource =
                    entry.Source.Equals("Application Error", StringComparison.OrdinalIgnoreCase)
                    || entry.Source.Equals("Application Hang", StringComparison.OrdinalIgnoreCase)
                    || entry.Source.StartsWith(".NET Runtime", StringComparison.OrdinalIgnoreCase);
                if (!isCrashSource)
                    continue;

                var message = entry.Message ?? "";
                // Match on the executable name, and on the pid where Windows includes it. Bloom's
                // WebView2 children matter too: a renderer crash is Bloom's problem even when the
                // parent survives.
                if (
                    message.Contains("Bloom.exe", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)
                    || message.Contains($"{processId:x}", StringComparison.OrdinalIgnoreCase)
                )
                    return true;
            }
        }
        catch (Exception)
        {
            // Unreadable Event Log: indistinguishable from no entry, which Phase 1 already treats as
            // "say nothing".
        }
        return false;
    }

    /// <summary>
    /// Looks for a per-user Windows Error Reporting report written around the time of death. The
    /// machine-wide archive under ProgramData usually needs administrator rights; per plan §4.4 we try
    /// and skip silently, never prompting.
    /// </summary>
    private static bool LookForWerReport(DateTime diedAt)
    {
        var roots = new[]
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
        };

        foreach (var root in roots)
        {
            try
            {
                if (!Directory.Exists(root))
                    continue;
                foreach (
                    var directory in Directory.EnumerateDirectories(
                        root,
                        "*Bloom*",
                        SearchOption.AllDirectories
                    )
                )
                {
                    var written = Directory.GetLastWriteTime(directory);
                    if ((written - diedAt).Duration() <= EvidenceWindow)
                        return true;
                }
            }
            catch (Exception)
            {
                // Access denied on the machine-wide archive is the expected case, not an error.
            }
        }
        return false;
    }

    /// <summary>
    /// True when Bloom's log ends with the forced-shutdown line. Reads only the tail, and shares the
    /// file, since another Bloom may hold it open.
    /// </summary>
    private static bool LogEndsWithForcedShutdown(string? logPath)
    {
        if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
            return false;
        try
        {
            foreach (var line in ReadLastLines(logPath, 40))
            {
                if (line.Contains(ForcedShutdownLogLine, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception)
        {
            // Nothing to conclude.
        }
        return false;
    }

    /// <summary>
    /// Reads roughly the last <paramref name="count"/> lines of a file that another process may be
    /// writing to. Public because the gatherer wants log tails too.
    /// </summary>
    public static List<string> ReadLastLines(string path, int count)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        // Enough to cover the requested lines for any plausible line length, without reading a log
        // that may be megabytes.
        const int tailBytes = 64 * 1024;
        if (stream.Length > tailBytes)
            stream.Seek(-tailBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
            if (lines.Count > count * 4)
                lines.RemoveAt(0);
        }
        return lines.Count <= count ? lines : lines.GetRange(lines.Count - count, count);
    }

    /// <summary>
    /// True when the machine itself went down while Bloom was running: an unexpected-shutdown event
    /// (6008), or a boot that happened after the process died, which can only mean we are looking at
    /// the wreckage from before a restart.
    /// </summary>
    private static bool DidMachineGoDown(DateTime startedAt, DateTime diedAt)
    {
        try
        {
            var bootedAt = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64);
            // A boot after the process was last seen means the machine restarted in between.
            if (bootedAt > diedAt.AddSeconds(-30))
                return true;

            using var log = new EventLog("System");
            for (var i = log.Entries.Count - 1; i >= 0 && i > log.Entries.Count - 200; i--)
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
                if (entry.TimeGenerated < startedAt)
                    break;
                // 6008: "The previous system shutdown ... was unexpected."
                if (entry.InstanceId == 6008 && entry.TimeGenerated >= startedAt)
                    return true;
            }
        }
        catch (Exception)
        {
            // If we cannot tell, we do not claim the machine went down: that would silence real
            // crashes, which is the worse mistake.
        }
        return false;
    }
}
