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
    /// Derives Bloom's release channel from the path its executable was launched from.
    ///
    /// **Deliberately indifferent to the file extension.** It looks only at folders, so <c>Bloom.exe</c>
    /// and <c>Bloom.dll</c> in the same directory give the same answer - which is what we need, because
    /// Bloom's own <c>ApplicationUpdateSupport.ChannelName</c> asks its entry assembly and so sees a
    /// <c>.dll</c>, while from outside we see the process, whose main module is the <c>.exe</c>. Bloom's
    /// version additionally requires the <c>.dll</c> ending on its developer-build check; matching that
    /// here would call every developer build "Release", which is the dangerous direction. A test pins the
    /// two forms agreeing.
    ///
    /// This is a NARROWER function than Bloom's, not a copy of it: Bloom's also has a Linux branch and a
    /// unit-test channel, neither of which a Windows-only watcher wants.
    ///
    /// **Why the Doctor works this out for itself rather than asking Bloom.** Bloom used to publish its
    /// own channel in the session file and nothing read it, so that field is gone. Deriving it here is not
    /// a duplication left to be teased out later: it is the only answer available for a Bloom that wrote no
    /// session file - one built before the Doctor existed, or one that died before it got the chance -
    /// which are exactly the runs the Doctor exists for. Unifying it with Bloom's own ChannelName would
    /// also mean editing a method with 21 callers feeding Sentry, analytics and the update channel, and no
    /// tests at all.
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
    /// The process names an installed Bloom can have, one per release channel — which are also the folder
    /// names, since an installed Bloom lives in
    /// <c>%LOCALAPPDATA%\Bloom{Channel}\current\Bloom{Channel}.exe</c> and Release is the one with no
    /// suffix.
    ///
    /// **Kept in one place because two consumers must agree.** The Doctor sweeps for these to find Blooms
    /// nobody told it about, and "Restart Bloom" searches the same names for something to launch; a list
    /// that drifted would let the Doctor watch a channel it could not then restart.
    ///
    /// **Beta and Release are deliberately here**, although whether the Doctor will ship enabled for them
    /// is not settled. Watching costs nothing until something goes wrong, and being able to try a report on
    /// a real Beta is how that question gets answered rather than guessed. Filing from them is governed
    /// separately, by <see cref="IsDeveloperChannel"/> and the guards around it.
    /// </summary>
    public static readonly string[] InstalledBloomProcessNames =
    [
        "Bloom", // Release: the channel with no suffix
        "BloomAlpha",
        "BloomBeta",
        "BloomBetaInternal",
        "BloomReleaseInternal",
    ];

    /// <summary>
    /// True when the channel is a developer build. Such a run is gathered and written to disk but
    /// never filed (plan §3.3), which is the first and most reliable of the four defences against
    /// reporting a developer stopping their debugger.
    /// </summary>
    public static bool IsDeveloperChannel(string channel) =>
        channel.StartsWith("Developer", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a command line says this Bloom is doing a job rather than serving a user: one of the
    /// console verbs. Such a process legitimately has no window, so without this check every headless
    /// run would look like the zombie of plan §3.6.
    ///
    /// **<c>--automation</c> is deliberately NOT one of these**, though it was until someone read what
    /// the flag actually does. In Bloom it means three things, none of them about windows: take the
    /// multi-instance path rather than the single-instance token (Program.Main), print
    /// <c>BLOOM_AUTOMATION_READY</c> with the ports so the launcher can find this instance
    /// (BloomServer.WriteAutomationStartupInfo), and show those ports in the title bar
    /// (Shell.ShouldShowPortSummaryInWindowTitle). It shows the ordinary Shell window like any other
    /// run, and there is no headless Bloom mode in this repo at all.
    ///
    /// That mattered because <c>go.sh</c>'s launcher passes <c>--automation</c> on **every** launch,
    /// developer UI sessions included. So calling it windowless silenced the Doctor for the one Bloom a
    /// developer actually watches their changes in, and left F5 the only way it ever got exercised.
    /// </summary>
    public static bool IsHeadlessRun(string commandLine)
    {
        var line = commandLine ?? "";

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
