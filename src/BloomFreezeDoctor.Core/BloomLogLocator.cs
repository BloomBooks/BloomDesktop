namespace BloomFreezeDoctor;

/// <summary>One Bloom log file on disk, and what its opening lines say about who owns it.</summary>
public readonly record struct BloomLogCandidate
{
    /// <summary>Full path to the log file.</summary>
    public required string Path { get; init; }

    /// <summary>Time of day in the log's "App Launched with [...]" line, if we found one.</summary>
    public required TimeSpan? LaunchedAtTimeOfDay { get; init; }

    /// <summary>
    /// The command line the log says Bloom was launched with, including the path to Bloom.dll. Also
    /// tells us whether that run was a headless job.
    /// </summary>
    public required string? LaunchCommandLine { get; init; }

    /// <summary>When the file was last written; only a tie-breaker, never the primary evidence.</summary>
    public required DateTime LastWriteTime { get; init; }
}

/// <summary>
/// Decides which of the Bloom log files in <c>%TEMP%\SIL\Bloom</c> belongs to a given Bloom process.
///
/// This exists because the obvious answer is wrong, and provably so. Bloom's logger writes to
/// <c>Log.txt</c>, recreating it on every run, and falls back to <c>Log-tmpXXXX.txt</c> **only when
/// Log.txt cannot be created** — that is, when another Bloom is already holding it. So the
/// restart-after-a-freeze case, which is exactly the case we care about, is the one where the frozen
/// Bloom owns <c>Log.txt</c> and the healthy new one owns a tmp file. Picking the most recently
/// modified file therefore attaches the *wrong* log to the report; measured on a real machine during
/// the spike, where the newest log belonged to a Bloom in an entirely different worktree.
///
/// Instead we match on what each log says about itself: its opening "App Launched with [exe]" line
/// carries the launching path and the time, which we compare against the process's own exe folder and
/// start time. No handle enumeration required.
/// </summary>
public static class BloomLogLocator
{
    /// <summary>
    /// How far apart the log's launch line and the process's start time may be and still be believed
    /// the same run. Generous, because the log line is written after the process starts and startup
    /// work happens in between.
    /// </summary>
    public static readonly TimeSpan LaunchTimeTolerance = TimeSpan.FromSeconds(90);

    private const string LaunchMarker = "App Launched with [";

    /// <summary>
    /// Picks the log belonging to the given process, or null if none matches. Candidates are usually
    /// read from <see cref="ReadCandidates"/>; taking them as an argument keeps this decision
    /// testable without a filesystem.
    /// </summary>
    public static BloomLogCandidate? ChooseFor(
        IEnumerable<BloomLogCandidate> candidates,
        string processExePath,
        DateTime processStartTime
    )
    {
        var wanted = FolderOf(processExePath);
        BloomLogCandidate? best = null;

        foreach (var candidate in candidates)
        {
            if (candidate.LaunchCommandLine == null || candidate.LaunchedAtTimeOfDay == null)
                continue;
            // The log names Bloom.dll while the process reports Bloom.exe, so compare folders.
            if (
                !string.Equals(
                    FolderOf(FirstPathIn(candidate.LaunchCommandLine)),
                    wanted,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                continue;

            var difference = (
                candidate.LaunchedAtTimeOfDay.Value - processStartTime.TimeOfDay
            ).Duration();
            if (difference > LaunchTimeTolerance)
                continue;

            // Two runs of the same build could both match within the tolerance; prefer the closer
            // launch time, and only then the more recently written file.
            if (best == null)
            {
                best = candidate;
                continue;
            }
            var bestDifference = (
                best.Value.LaunchedAtTimeOfDay!.Value - processStartTime.TimeOfDay
            ).Duration();
            if (
                difference < bestDifference
                || (
                    difference == bestDifference
                    && candidate.LastWriteTime > best.Value.LastWriteTime
                )
            )
                best = candidate;
        }

        return best;
    }

    /// <summary>
    /// Reads the log candidates out of Bloom's log directory (<c>%TEMP%\SIL\Bloom</c> by default).
    /// Never throws: a log we cannot read is a log we cannot attach, which is a lesser problem than
    /// a Doctor that falls over while diagnosing.
    /// </summary>
    public static List<BloomLogCandidate> ReadCandidates(
        string? logDirectory = null,
        int maxFiles = 30
    )
    {
        var directory = logDirectory ?? Path.Combine(Path.GetTempPath(), "SIL", "Bloom");
        var result = new List<BloomLogCandidate>();
        if (!Directory.Exists(directory))
            return result;

        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(directory)
                .GetFiles("Log*.txt")
                .OrderByDescending(f => f.LastWriteTime)
                .Take(maxFiles);
        }
        catch (Exception)
        {
            return result;
        }

        foreach (var file in files)
        {
            var (launchedAt, commandLine) = ReadLaunchLine(file.FullName);
            result.Add(
                new BloomLogCandidate
                {
                    Path = file.FullName,
                    LaunchedAtTimeOfDay = launchedAt,
                    LaunchCommandLine = commandLine,
                    LastWriteTime = file.LastWriteTime,
                }
            );
        }
        return result;
    }

    /// <summary>
    /// Pulls the launch time and command line out of a log's opening lines. Shares the file for
    /// reading and deleting, because the Bloom that owns it holds it open for writing.
    /// </summary>
    public static (TimeSpan? LaunchedAt, string? CommandLine) ReadLaunchLine(string path)
    {
        try
        {
            // robustfile-hook: allow FileStream
            // The documented exception, and this is the case it was written for: we need
            // FileShare.ReadWrite | FileShare.Delete specifically, because the Bloom that owns this log
            // holds it open for writing and may delete it while we read. A robust wrapper that retried
            // would not help — the sharing flags are the whole point, not a transient failure.
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            using var reader = new StreamReader(stream);
            // The launch line is the second line of a healthy log; read a few in case something
            // (Velopack chatter, say) got in first.
            for (var i = 0; i < 40; i++)
            {
                var line = reader.ReadLine();
                if (line == null)
                    break;
                var at = line.IndexOf(LaunchMarker, StringComparison.Ordinal);
                if (at < 0)
                    continue;
                var commandLine = line.Substring(at + LaunchMarker.Length).TrimEnd(']');
                var stamp = line.Split('\t')[0].Trim();
                return (
                    DateTime.TryParse(stamp, out var parsed) ? parsed.TimeOfDay : null,
                    commandLine
                );
            }
        }
        catch (Exception)
        {
            // Unreadable or vanished; it simply is not a candidate.
        }
        return (null, null);
    }

    /// <summary>
    /// The first path in a logged command line, i.e. the Bloom.dll being run, allowing for it being
    /// quoted or followed by arguments.
    /// </summary>
    private static string FirstPathIn(string commandLine)
    {
        var line = commandLine.Trim();
        if (line.StartsWith('"'))
        {
            var end = line.IndexOf('"', 1);
            return end > 0 ? line.Substring(1, end - 1) : line.Trim('"');
        }
        // Unquoted: everything up to the first " --" style argument, or the whole thing.
        var argument = line.IndexOf(" --", StringComparison.Ordinal);
        return argument > 0 ? line.Substring(0, argument) : line;
    }

    private static string FolderOf(string path)
    {
        try
        {
            return Path.GetDirectoryName(path.Replace('/', '\\')) ?? path;
        }
        catch (Exception)
        {
            return path;
        }
    }
}
