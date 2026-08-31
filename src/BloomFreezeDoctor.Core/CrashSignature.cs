using System.Text;
using System.Text.RegularExpressions;

namespace BloomFreezeDoctor;

/// <summary>
/// Pulls "which crash was this" out of the .NET Runtime event Windows writes when a process dies of an
/// unhandled exception (Application log, event 1026).
///
/// This exists because the report fingerprint was nearly meaningless for a crash. It hashes the reason,
/// Bloom's version, the channel and the top frames of the UI THREAD - which is exactly right for a freeze,
/// where the UI thread's stack IS the problem. In a crash the fault is on some other thread and the UI
/// thread is sitting in its message pump, so those frames are identical for every crash on a given build:
/// the fingerprint degenerated to reason+version+channel, and every unexplained crash on one build landed
/// on one card, however unrelated. Measured, not theorised - three separate simulated crashes in one
/// afternoon all produced fingerprint 1ec8760ad8a5.
///
/// The event text carries what is actually wanted. A real one, from this machine:
///
/// <code>
/// Application: Bloom.exe
/// CoreCLR Version: 8.0.3026.36720
/// .NET Version: 8.0.30
/// Description: The process was terminated due to an unhandled exception.
/// Exception Info: System.ApplicationException: FreezeSimulator was asked to crash a background thread
///    at Bloom.FreezeDoctor.FreezeSimulator.&lt;&gt;c.&lt;Simulate&gt;b__8_0() in C:\github\...\FreezeSimulator.cs:line 243
/// </code>
/// </summary>
public static class CrashSignature
{
    /// <summary>How many frames of the faulting stack to keep. Enough to distinguish, few enough to
    /// survive a refactor - the same reasoning, and the same number, as ReportFingerprint.</summary>
    private const int FramesToKeep = 5;

    /// <summary>Matches the exception type at the head of the "Exception Info:" line.</summary>
    private static readonly Regex ExceptionInfo = new(
        @"^Exception Info:\s*(?<type>[A-Za-z_][A-Za-z0-9_.`+]*Exception)",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    /// <summary>
    /// Matches a stack frame, keeping the method and dropping the source location. `in C:\...:line 243`
    /// is deliberately excluded: it changes every time anyone edits the file above the fault, so keeping
    /// it would make the same crash look new after any rebuild.
    /// </summary>
    private static readonly Regex Frame = new(
        @"^\s+at\s+(?<method>.+?)(?:\s+in\s.*)?$",
        RegexOptions.Multiline | RegexOptions.Compiled
    );

    /// <summary>
    /// The identifying part of a crash: the exception type and the top frames of the faulting stack.
    /// Null when the message is not a crash report of this shape, in which case the caller keeps
    /// whatever identity it had before.
    ///
    /// The exception's MESSAGE is left out on purpose. It routinely carries a file path, a book name or an
    /// id, so including it would give the same fault a new identity on every machine and defeat the
    /// deduplication this feeds.
    /// </summary>
    public static string? FromEventLogMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var type = ExceptionInfo.Match(message);
        if (!type.Success)
            return null;

        var signature = new StringBuilder(type.Groups["type"].Value);
        var kept = 0;
        foreach (Match frame in Frame.Matches(message))
        {
            if (kept++ >= FramesToKeep)
                break;
            signature.Append('|').Append(frame.Groups["method"].Value.Trim());
        }
        return signature.ToString();
    }
}
