using System.Management;

namespace BloomFreezeDoctor;

/// <summary>One of Bloom's WebView2 helper processes.</summary>
public readonly record struct WebView2Child
{
    /// <summary>The child's process id.</summary>
    public required int ProcessId { get; init; }

    /// <summary>Its parent, which is either Bloom itself or the WebView2 browser process.</summary>
    public required int ParentProcessId { get; init; }

    /// <summary>
    /// What Chromium says this process is for: "browser", "renderer", "gpu-process", "utility"…
    /// A renderer at 100% CPU means the freeze is in JavaScript, not in .NET.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>The full command line, which is where the debugging port hides.</summary>
    public required string CommandLine { get; init; }
}

/// <summary>
/// Finds Bloom's WebView2 processes and the debugging port they listen on.
///
/// Reading the port from the child's command line is not a shortcut, it is the only way that works:
/// Bloom's own HTTP port belongs to http.sys (pid 4) and so never appears against Bloom in the TCP
/// table, and the port arithmetic differs between Bloom versions — 6.4 and later use
/// <c>httpPort + 2</c>, while **6.3 hardcodes 9222** (confirmed by talking to a real installed 6.3
/// during the spike).
/// </summary>
public static class WebView2Processes
{
    private const string WebViewExecutable = "msedgewebview2.exe";

    /// <summary>
    /// The port Bloom 6.3 hardcodes. Tried as a fallback, but only after the command lines, and with
    /// the caveat that 9222 is Chromium's universal default so something else may own it.
    /// </summary>
    public const int LegacyHardcodedPort = 9222;

    /// <summary>
    /// Finds the WebView2 processes belonging to one Bloom. Walks two levels, because the browser
    /// process is Bloom's child and the renderers are the browser's children.
    /// </summary>
    public static List<WebView2Child> FindChildrenOf(int bloomProcessId)
    {
        var all = ReadWebViewProcesses();
        var mine = all.Where(c => c.ParentProcessId == bloomProcessId).ToList();
        var browserIds = mine.Select(c => c.ProcessId).ToHashSet();
        mine.AddRange(all.Where(c => browserIds.Contains(c.ParentProcessId)));
        return mine;
    }

    /// <summary>
    /// Works out which port to talk CDP on for a given Bloom, or null if we cannot tell.
    ///
    /// Attributing the port to the right parent matters as soon as a machine has two Blooms running,
    /// which on a developer's machine is routine: the spike's first pass reported ports globally and
    /// would happily have handed the Doctor another Bloom's renderer.
    /// </summary>
    public static int? FindDebuggingPort(int bloomProcessId)
    {
        foreach (var child in FindChildrenOf(bloomProcessId))
        {
            var port = ExtractPort(child.CommandLine);
            if (port.HasValue)
                return port;
        }
        return null;
    }

    /// <summary>
    /// Pulls <c>--remote-debugging-port=N</c> out of a command line. Public so it can be tested
    /// without a running WebView2.
    /// </summary>
    public static int? ExtractPort(string commandLine)
    {
        const string marker = "--remote-debugging-port=";
        var at = (commandLine ?? "").IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return null;
        var digits = new string(
            commandLine!.Substring(at + marker.Length).TakeWhile(char.IsDigit).ToArray()
        );
        return int.TryParse(digits, out var port) ? port : null;
    }

    /// <summary>
    /// Reads the <c>--type=</c> argument Chromium gives its helper processes, or "browser" for the one
    /// that has none.
    /// </summary>
    public static string ExtractKind(string commandLine)
    {
        const string marker = "--type=";
        var at = (commandLine ?? "").IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return "browser";
        var rest = commandLine!.Substring(at + marker.Length);
        var end = rest.IndexOf(' ');
        return (end < 0 ? rest : rest.Substring(0, end)).Trim();
    }

    private static List<WebView2Child> ReadWebViewProcesses()
    {
        var result = new List<WebView2Child>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name = '"
                    + WebViewExecutable
                    + "'"
            );
            // Both the collection and every object in it are disposable and each holds unmanaged COM
            // state. This runs every few seconds for every Bloom being watched, for as long as the Doctor
            // lives, so leaving them to finalization accumulates that state in the one process which has
            // to stay healthy enough to diagnose everything else. Discover() already disposes its Process
            // objects for exactly this reason.
            using var found = searcher.Get();
            foreach (ManagementObject item in found)
            {
                using (item)
                {
                    var commandLine = item["CommandLine"] as string ?? "";
                    result.Add(
                        new WebView2Child
                        {
                            ProcessId = Convert.ToInt32(item["ProcessId"]),
                            ParentProcessId = Convert.ToInt32(item["ParentProcessId"]),
                            Kind = ExtractKind(commandLine),
                            CommandLine = commandLine,
                        }
                    );
                }
            }
        }
        catch (Exception)
        {
            // WMI can be slow or unavailable; the report simply says less.
        }
        return result;
    }

    /// <summary>
    /// Reads one process's command line. Used to spot headless runs (plan §3.3), which the process
    /// list alone cannot reveal.
    /// </summary>
    /// <summary>
    /// What we have already asked WMI, keyed by process id AND start time.
    ///
    /// The key includes the start time because a process id is reused: keyed on the id alone, a Bloom that
    /// inherited a dead process's id would be handed that process's command line, and the one thing this is
    /// used for is deciding whether a run is headless - so the wrong answer means either watching a console
    /// verb or, worse, silently refusing to watch a real Bloom.
    ///
    /// Unbounded, and that is fine here: entries are only added for processes matching a Bloom name, of
    /// which one Doctor sees a handful in its life.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        string,
        string
    > _commandLines = new();

    /// <summary>
    /// A process's command line, asked of WMI once and then remembered.
    ///
    /// **The caching is not an optimisation, it is the fix for a measured fault.** This query costs
    /// 1.9-2.7 seconds on an idle machine - measured, repeatedly - and the discovery sweep called it for
    /// every candidate every five seconds. Under the load of Bloom starting up it is far worse, and the
    /// sweep's timer fires regardless of whether the last one finished, so the calls pile up on top of each
    /// other and make WMI slower still. That is why a Bloom sat unadopted for 34, 54, 83 and 110 seconds on
    /// four separate runs, and the 110-second one cost a crash dump outright: Bloom only asks to be dumped
    /// if a Doctor is already watching, and it asked before we had noticed it existed.
    ///
    /// A command line cannot change while a process lives, so asking once is not merely cheaper, it is the
    /// correct number of times to ask.
    /// </summary>
    public static string ReadCommandLine(int processId, DateTime startedAt) =>
        _commandLines.GetOrAdd(
            $"{processId}@{startedAt.Ticks}",
            _ => AskWmiForCommandLine(processId)
        );

    /// <summary>The uncached query. See <see cref="ReadCommandLine"/> for why nothing should call this twice
    /// for one process.</summary>
    private static string AskWmiForCommandLine(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}"
            );
            using var found = searcher.Get();
            foreach (ManagementObject item in found)
                using (item)
                    return item["CommandLine"] as string ?? "";
        }
        catch (Exception) { }
        return "";
    }
}
