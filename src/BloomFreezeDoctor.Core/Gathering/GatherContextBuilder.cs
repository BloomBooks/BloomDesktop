using System.Diagnostics;
using SIL.IO;
// Aliased rather than imported plainly, so uses below still read as Protocol.DoctorSession - it is worth
// saying at each use that this is the shared wire format, not something local to the Doctor.
using Protocol = BloomFreezeDoctor.Protocol;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// Works out the things a gather needs to know about its target that take looking up: which log file is
/// this Bloom's, and where its WebView2 is listening.
///
/// Kept in one place because both the Doctor and its test harnesses need it, and because getting either
/// answer wrong is quiet rather than loud — the report simply carries the wrong log or talks to another
/// Bloom's browser, and nobody notices until a card misleads someone.
/// </summary>
public static class GatherContextBuilder
{
    /// <summary>
    /// Builds the context for gathering evidence about one Bloom. Never throws: every lookup here is a
    /// nice-to-have, and a report with a missing log beats no report.
    /// </summary>
    public static GatherContext Build(
        BloomTargetFacts target,
        DetectorVerdict verdict,
        bool processWasAlive,
        string artifactDirectory,
        string? logDirectory = null,
        Protocol.DoctorChannelSnapshot? lastSeenPublishedState = null,
        Action? targetNoLongerNeeded = null
    )
    {
        // Bloom's own session file, when there is one, is better than anything we can work out from
        // outside — and for the log path it is better in a way that matters: guessing from the filesystem
        // is systematically wrong in the restart-after-a-freeze case (see BloomLogLocator).
        var session = Protocol.DoctorSessionStore.TryRead(target.ProcessId);

        return new GatherContext
        {
            Target = target,
            Verdict = verdict,
            ProcessWasAlive = processWasAlive,
            ArtifactDirectory = artifactDirectory,
            BloomLogPath = FirstUsable(session?.LogPath, () => FindLog(target, logDirectory)),
            CdpPort = session is { CdpPort: > 0 }
                ? session.CdpPort
                : FindCdpPort(target, processWasAlive),
            Session = session,
            PublishedState = ReadPublishedState(target.ProcessId) ?? lastSeenPublishedState,
            TargetNoLongerNeeded = targetNoLongerNeeded,
        };
    }

    /// <summary>
    /// Reads Bloom's live published state, if it publishes any. Read at gather time rather than taken from
    /// the watcher so that the report quotes the state at the moment we decided to gather, which is the
    /// moment a reader will be asking about.
    ///
    /// It falls back to the watcher's last reading (above) for one case that the live read cannot serve at
    /// all: a Bloom that has already died. The channel lives in the process's own memory, so it goes when
    /// the process goes, and a death report was reduced to declaring that Bloom "does not publish a health
    /// channel" — of a Bloom that had been publishing one a second earlier.
    /// </summary>
    private static Protocol.DoctorChannelSnapshot? ReadPublishedState(int processId)
    {
        try
        {
            return Protocol.DoctorChannelReader.TryRead(processId, out var snapshot)
                ? snapshot
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Prefers what Bloom told us, falling back to working it out ourselves. The fallback is a function so
    /// that the expensive inference only happens when Bloom has not answered.
    /// </summary>
    private static string? FirstUsable(string? preferred, Func<string?> fallback)
    {
        if (!string.IsNullOrEmpty(preferred) && RobustFile.Exists(preferred))
            return preferred;
        return fallback();
    }

    /// <summary>
    /// Identifies this Bloom's log by matching each candidate's "App Launched with" line against the
    /// process's own exe folder and start time. See <see cref="BloomLogLocator"/> for why the obvious
    /// alternative is wrong.
    /// </summary>
    private static string? FindLog(BloomTargetFacts target, string? logDirectory)
    {
        try
        {
            var candidates = BloomLogLocator.ReadCandidates(logDirectory);
            var chosen = BloomLogLocator.ChooseFor(candidates, target.ExePath, target.StartTime);
            return chosen?.Path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds the WebView2 debugging port for this Bloom, attributed to *this* process rather than
    /// whichever WebView2 happened to answer first — which matters as soon as a machine runs two Blooms,
    /// routine on a developer's machine.
    ///
    /// Falls back to the port 6.3 hardcodes, but only after the command lines, and only if this Bloom
    /// looks like a 6.3: on a machine where something else owns 9222 we would otherwise interrogate a
    /// stranger's browser and put the answers on a Bloom card.
    /// </summary>
    private static int? FindCdpPort(BloomTargetFacts target, bool processWasAlive)
    {
        // Nothing to find, and worse than nothing to guess: a dead process has no WebView2 children, so
        // the "no children means this is a 6.3" test below would be satisfied by every exited Bloom and
        // hand back a port somebody else may now own. WebViewCollector refuses a dead process outright as
        // well; this is the same rule at the other end, so neither has to rely on the other.
        if (!processWasAlive)
            return null;
        try
        {
            var fromCommandLine = WebView2Processes.FindDebuggingPort(target.ProcessId);
            if (fromCommandLine.HasValue)
                return fromCommandLine;

            // No WebView2 child advertised a port. If this Bloom has WebView2 children at all, it is
            // not running the 6.3 arrangement, so guessing 9222 would be guessing about someone else.
            // With no children, this looks like 6.3, which hardcodes the port and does not advertise it.
            var children = WebView2Processes.FindChildrenOf(target.ProcessId);
            return children.Count == 0 ? WebView2Processes.LegacyHardcodedPort : (int?)null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Describes a running process as a target, reading its command line so headless runs can be
    /// recognised. Returns null if the process went away while we were asking.
    /// </summary>
    public static BloomTargetFacts? DescribeRunningProcess(int processId) =>
        DescribeRunningProcess(processId, out _);

    /// <summary>
    /// As above, and says why when it cannot - so a caller that keeps meeting the same process can report
    /// what is wrong with it instead of silently passing over it every few seconds.
    /// </summary>
    public static BloomTargetFacts? DescribeRunningProcess(int processId, out string? whyNot)
    {
        whyNot = null;
        try
        {
            using var process = Process.GetProcessById(processId);
            return BloomTargetWatcher.DescribeProcess(
                process,
                WebView2Processes.ReadCommandLine(processId),
                out whyNot
            );
        }
        catch (Exception e)
        {
            whyNot = $"{e.GetType().Name}: {e.Message}";
            return null;
        }
    }
}
