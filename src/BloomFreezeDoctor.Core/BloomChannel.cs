namespace BloomFreezeDoctor;

/// <summary>
/// Works out which Bloom we are looking at from the outside — its release channel, and whether it is
/// the sort of run that must never produce a report.
///
/// These are pure functions on strings so they can be tested exhaustively; the spike showed that both
/// of them have a trap that would fail in the dangerous direction.
/// </summary>
public static class BloomChannel
{
    /// <summary>
    /// Derives Bloom's release channel from the executable path, mirroring Bloom's own
    /// <c>ApplicationUpdateSupport.ChannelName</c>.
    ///
    /// **One deliberate difference from Bloom's version, and it matters.** Bloom asks about its entry
    /// assembly, so it tests for a path ending in <c>Bloom.dll</c>. From outside we see the *process*,
    /// whose main module is <c>Bloom.exe</c>. Requiring the ".dll" ending here would classify every
    /// developer build as "Release" — which is the dangerous direction, because it is exactly what
    /// would make a developer's `pnpm go` session file cards on the tracker.
    /// </summary>
    public static string DeriveFromExePath(string exePath)
    {
        var path = (exePath ?? "").Replace('\\', '/');
        if (path.Contains("/output/Debug/", StringComparison.OrdinalIgnoreCase))
            return "Developer/Debug";
        if (path.Contains("/output/Release/", StringComparison.OrdinalIgnoreCase))
            return "Developer/Release";

        // Installed builds live in .../Bloom{Channel}/current/. An empty channel means Release.
        var match = System.Text.RegularExpressions.Regex.Match(
            path,
            @"/Bloom([^/]*)/current/",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        if (match.Success && match.Groups[1].Value.Length > 0)
            return match.Groups[1].Value.Replace("-arm64", "");

        return "Release";
    }

    /// <summary>
    /// True when the channel is a developer build. Such a run is gathered and written to disk but
    /// never filed (plan §3.3), which is the first and most reliable of the four defences against
    /// reporting a developer stopping their debugger.
    /// </summary>
    public static bool IsDeveloperChannel(string channel) =>
        channel.StartsWith("Developer", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a command line says this Bloom is doing a job rather than serving a user: the
    /// command-line verbs, or an automated test run. Such a process legitimately has no window, so
    /// without this check every headless run would look like the zombie of plan §3.6.
    /// </summary>
    public static bool IsHeadlessOrAutomationRun(string commandLine)
    {
        var line = commandLine ?? "";
        if (line.Contains("--automation", StringComparison.OrdinalIgnoreCase))
            return true;

        // Bloom's console verbs, from Program.Main's command-line dispatch. They print to a console
        // and exit; none of them shows a window.
        string[] verbs =
        [
            "hydrate",
            "upload",
            "download",
            "getfonts",
            "changeLayout",
            "createArtifacts",
            "spreadsheetExport",
            "spreadsheetImport",
            "sendFontAnalytics",
        ];
        // Match a verb as its own argument, so a collection path that happens to contain the word
        // "upload" does not silence a real Bloom.
        var arguments = SplitArguments(line);
        return arguments.Any(a => verbs.Contains(a, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Splits a Windows command line into arguments well enough for the verb test above: quoted runs
    /// stay together, everything else splits on whitespace. Not a full CommandLineToArgvW.
    /// </summary>
    private static List<string> SplitArguments(string commandLine)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in commandLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }
}
