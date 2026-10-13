using System.Diagnostics;
using BloomFreezeDoctor.Protocol;
using SIL.IO;

namespace BloomFreezeDoctor;

/// <summary>
/// Gathers the after-the-fact evidence that <see cref="ExitClassifier"/> judges: Windows' own opinion
/// of the crash, Windows Error Reporting's files, the tail of Bloom's log, and whether the whole
/// machine went down.
///
/// Every reader here is individually failure-tolerant. A missing Event Log entry and a *failure to read*
/// the Event Log look identical to the classifier, which is a real limitation and is why the classifier
/// never treats absence as proof of anything.
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
        string? logPath,
        int? exitCode,
        bool neverFile,
        bool? cleanExitProofPresent = null,
        BloomShutdownPhase? shutdownPhaseReached = null,
        bool exitRecordedAsForced = false,
        string? exeFileName = null
    )
    {
        var crash = ScanForCrashEntries(processId, diedAt, exeFileName);
        return new ExitEvidence
        {
            ExitCode = exitCode,
            NeverFile = neverFile,
            CleanExitProofPresent = cleanExitProofPresent,
            ShutdownPhaseReached = shutdownPhaseReached,
            ExitRecordedAsForced = exitRecordedAsForced,
            // One pass, two answers: whether Windows logged a crash for this Bloom, and - when the
            // entry is a managed one - which crash it was. See ExitEvidence.CrashSignature.
            HasEventLogCrashEntry = crash.Found,
            CrashSignature = crash.Signature,
            HasWerReport = LookForWerReport(diedAt),
            LogShowsForcedShutdown = LogEndsWithForcedShutdown(logPath),
        };
    }

    /// <summary>
    /// Which crash Windows recorded for this Bloom, for a caller that is not classifying an exit and only
    /// wants the identity - the crash-dump path, which reports a death it was told about rather than one it
    /// examined afterwards.
    ///
    /// Without it, every crash on a build would share one fingerprint and land on one card. The runtime
    /// writes the event as it terminates the process, and this is called at the END of gathering, several
    /// seconds later, so the entry is normally there. When it is not, this returns null and the
    /// fingerprint falls back to the UI thread's frames.
    /// </summary>
    public static string? CrashSignatureFor(
        int processId,
        DateTime around,
        string? exeFileName = null
    ) => ScanForCrashEntries(processId, around, exeFileName).Signature;

    /// <summary>
    /// One walk of the Application log, answering two questions: did Windows log a crash for this Bloom,
    /// and - if any of those entries is a managed one - which crash was it.
    ///
    /// **It must not stop at the first match**, and that is the whole reason this is one method returning
    /// two things rather than a lookup returning one entry. Windows writes several entries for a single
    /// crash and this walk sees the newest first, so one crash looks like:
    ///
    /// <code>
    /// 15:18:59  Windows Error Reporting 1001   Fault bucket 2085764476734548794, type 4
    /// 15:18:55  Application Error       1000   Faulting application name: Bloom.exe, version: 6.5.0.0
    /// 15:18:55  .NET Runtime            1026   Application: Bloom.exe ... Exception Info: System...
    /// </code>
    ///
    /// The Application Error entry names Bloom and so matches, but carries no exception - only the 1026
    /// entry four lines later does. Returning the first match would yield an unparseable message and a
    /// null crash identity every time. So the walk carries on after its first match, looking for one that
    /// actually identifies the fault, and stops as soon as it has both answers.
    ///
    /// <paramref name="exeFileName"/> is the file name of the Bloom that died, because there is no one name
    /// to look for: the installer renames the exe per channel, so a release machine has <c>Bloom.exe</c>
    /// but an alpha has <c>BloomAlpha.exe</c>.
    /// </summary>
    private static (bool Found, string? Signature) ScanForCrashEntries(
        int processId,
        DateTime diedAt,
        string? exeFileName
    )
    {
        // Collected rather than judged in the loop, so the judging is a pure function with its own tests -
        // see PickTheCrashThatIdentifiesItself. There are only ever a handful of these: entries naming
        // Bloom within five minutes of its death.
        var candidates = new List<string>();
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

                if (!EntryNamesThisBloom(entry.Message ?? "", processId, exeFileName))
                    continue;

                candidates.Add(entry.Message ?? "");
            }
        }
        catch (Exception)
        {
            // Unreadable Event Log: indistinguishable from no entry, which the classifier already treats
            // as "say nothing". Whatever the walk managed before failing still counts.
        }
        return PickTheCrashThatIdentifiesItself(candidates);
    }

    /// <summary>
    /// Given the entries that name this Bloom, newest first: was there a crash entry at all, and which
    /// crash was it.
    ///
    /// Separated from the walk above so it can be tested: taking the first match looks obviously right
    /// and is wrong, and nothing but a test would notice. See the walk's own comment for the entry order.
    /// </summary>
    internal static (bool Found, string? Signature) PickTheCrashThatIdentifiesItself(
        IEnumerable<string> namingThisBloomNewestFirst
    )
    {
        var found = false;
        string? signature = null;
        foreach (var message in namingThisBloomNewestFirst)
        {
            // Any match answers "did Windows log a crash", which is all the classifier needs.
            found = true;
            // Only a managed one answers "which crash", so keep looking past the ones that cannot.
            signature ??= CrashSignature.FromEventLogMessage(message);
            if (signature != null)
                break;
        }
        return (found, signature);
    }

    /// <summary>
    /// True when this Event Log entry is about *this* Bloom. Deliberately narrow, because the cost of the
    /// two answers is not symmetric: a missed entry loses one piece of corroboration, while a wrong one
    /// puts "Bloom crashed: Windows logged an Application Error" on a card about a crash that was some
    /// other program's.
    ///
    /// Two tempting matches are therefore excluded on purpose, because each of them fires on other
    /// programs' crashes:
    ///
    /// - **the bare hex pid, anywhere in the message and without delimiters.** Every Application Error
    ///   entry is full of hex - exception codes, fault offsets, module base addresses - so a short pid is
    ///   near-certain to appear inside one of them. Pid 4096 is "1000", which matches a fault offset of
    ///   0x00007ff81000a4c0.
    /// - **<c>msedgewebview2.exe</c>, unqualified.** Bloom is far from the only WebView2 host on a Windows
    ///   machine: Teams, Outlook and the Widgets panel all crash renderers of their own, and any of them
    ///   doing so within five minutes would become evidence that Bloom crashed. Narrowing the clause is not
    ///   possible either - the pid in a renderer's entry is the RENDERER's, not Bloom's, so it can never
    ///   tell ours from theirs. Little is lost by leaving it out: Bloom normally survives a renderer crash,
    ///   and this code only runs when Bloom itself has gone.
    /// </summary>
    public static bool EntryNamesThisBloom(string message, int processId, string? exeFileName)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        if (!string.IsNullOrEmpty(exeFileName))
        {
            if (message.Contains(exeFileName!, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        // Nobody told us what this Bloom was called, so accept any of the channel names the installer
        // produces (Bloom.exe, BloomAlpha.exe, BloomBetaInternal.exe...) rather than guessing one.
        else if (
            System.Text.RegularExpressions.Regex.IsMatch(
                message,
                @"\bBloom\w*\.exe\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
        )
            return true;

        return PidAppearsAsAProcessId(message, processId);
    }

    /// <summary>
    /// True when the message quotes our pid **as a process id** — "Faulting process id: 0x1a2c" — rather
    /// than merely containing those hex digits somewhere. Both halves of that matter: the digits must be a
    /// whole hex number (not the middle of a longer address), and they must be labelled as a process id.
    ///
    /// This clause earns its place on ".NET Runtime" entries, which identify the process by id and do not
    /// always name the executable.
    /// </summary>
    private static bool PidAppearsAsAProcessId(string message, int processId)
    {
        var hex = processId.ToString("x");
        var from = 0;
        while (from < message.Length)
        {
            var at = message.IndexOf(hex, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;
            from = at + 1;

            // A whole hex number, not part of a longer one. Note that the "x" of a "0x" prefix is not
            // itself a hex digit, so a prefixed number passes this on the left.
            if (at > 0 && Uri.IsHexDigit(message[at - 1]))
                continue;
            var after = at + hex.Length;
            if (after < message.Length && Uri.IsHexDigit(message[after]))
                continue;

            // And labelled as what we think it is. Windows writes "Faulting process id: 0x1a2c" and
            // "Process ID: 1a2c"; the label is always close in front of the number.
            var start = Math.Max(0, at - 32);
            var lead = message.Substring(start, at - start);
            if (
                lead.Contains("process id", StringComparison.OrdinalIgnoreCase)
                || lead.Contains("processid", StringComparison.OrdinalIgnoreCase)
            )
                return true;
        }
        return false;
    }

    /// <summary>
    /// Looks for a per-user Windows Error Reporting report written around the time of death. The
    /// machine-wide archive under ProgramData usually needs administrator rights; we try and skip
    /// silently, never prompting.
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
        if (string.IsNullOrEmpty(logPath) || !RobustFile.Exists(logPath))
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
        // robustfile-hook: allow FileStream
        // Same documented case as BloomLogLocator.ReadLaunchLine: the sharing flags ARE the requirement,
        // because the process being diagnosed is still writing this file and may delete it underneath us.
        // Retrying would not substitute for being allowed to read a file somebody else holds.
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
    /// Copies a file that another process is still writing to.
    ///
    /// **Why not <c>RobustFile.Copy</c>, which is this repository's rule.** It is refused outright by a
    /// file whose owner holds it for writing, and Bloom holds its log open for the whole of its run — so
    /// for a FREEZE, where the process is by definition still alive, it can never work. Retrying does not
    /// help: the refusal is not transient. WindowsExitEvidenceCollectorLogCopyTests pins this, because getting it wrong is
    /// silent — the copy throws and the report simply has no log.
    ///
    /// Same sharing flags, and the same reasoning, as <see cref="ReadLastLines"/>: the flags ARE the
    /// requirement here, which is why this is one of the documented exceptions to the RobustFile rule.
    /// </summary>
    public static void CopyWhileInUse(string path, string destination)
    {
        // robustfile-hook: allow FileStream
        // The process being diagnosed is still writing this file; permissive sharing is the whole point.
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete
        );
        // robustfile-hook: allow FileStream
        // The destination is ours alone, inside the bundle we are building.
        using var target = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None
        );
        source.CopyTo(target);
    }
}
