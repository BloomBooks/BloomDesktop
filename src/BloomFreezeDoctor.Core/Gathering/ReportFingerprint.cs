using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// Works out a stable identifier for "this particular problem", so that the same freeze happening
/// twenty times becomes one card with twenty occurrences rather than twenty cards (plan §5.2).
///
/// The hard part is choosing what to hash. Too much and every occurrence looks unique — thread ids,
/// timings and memory addresses all differ every time. Too little and unrelated freezes collapse
/// together. What identifies a freeze is *where the UI thread was stuck*, so we hash the top few
/// frames of the interesting stack, plus the state and the Bloom version.
/// </summary>
public static class ReportFingerprint
{
    /// <summary>How many frames to include. Enough to distinguish, few enough to survive a refactor.</summary>
    private const int FramesToHash = 5;

    /// <summary>
    /// Builds the fingerprint for a report. Also embedded in the card's text, so a tracker search can
    /// find the earlier card for the same problem.
    /// </summary>
    /// <param name="identifyingDetail">
    /// What makes this problem this problem, when the reason alone is not enough - see
    /// DetectorVerdict.IdentifyingDetail. Passed in rather than read off the verdict because for a crash it
    /// can only be resolved once the process has gone, which is after gathering begins.
    /// </param>
    public static string For(
        GatherContext context,
        IEnumerable<ReportSection> sections,
        string? identifyingDetail = null
    )
    {
        var ingredients = new StringBuilder();
        ingredients.Append(context.Verdict.Report).Append('|');
        ingredients.Append(VersionOf(context.Target.ExePath)).Append('|');
        ingredients.Append(context.Target.Channel).Append('|');

        // A crash brings its own identity and the UI thread's stack is not it: the fault was on another
        // thread, so those frames are the message pump and are the same for every crash on this build.
        // Hashing them made every unexplained crash on a build one card - measured, three unrelated
        // simulated crashes all fingerprinted 1ec8760ad8a5. See DetectorVerdict.IdentifyingDetail.
        var identity = identifyingDetail ?? context.Verdict.IdentifyingDetail;
        if (!string.IsNullOrEmpty(identity))
        {
            ingredients.Append(identity);
            return Hash(ingredients.ToString());
        }

        var stacks = sections.FirstOrDefault(s => s.Title == "Managed stacks");
        foreach (var frame in TopFramesOfUiThread(stacks?.Body ?? ""))
            ingredients.Append(frame).Append(';');

        return Hash(ingredients.ToString());
    }

    /// <summary>
    /// Pulls the UI thread's top frames out of the rendered stacks section. Working from the text
    /// rather than the objects keeps this independent of how stacks were obtained (a dump or a live
    /// read), which matters because the two paths can produce slightly different frame lists.
    /// </summary>
    private static List<string> TopFramesOfUiThread(string stacksBody)
    {
        var frames = new List<string>();
        var inUiSection = false;
        foreach (var rawLine in stacksBody.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("### The UI thread", StringComparison.Ordinal))
            {
                inUiSection = true;
                continue;
            }
            // End of the UI thread's frames. Two ways it ends, and only one of them was handled: the next
            // `###` heading, OR a collapse marker, which is what actually follows the UI thread in the
            // rendered stacks. Missing the marker let the loop keep going into the next thread, whose
            // frames are indented identically - so whenever the UI thread yielded fewer than five usable
            // frames (a shallow stack, or frames filtered as native or stubs), the hash was topped up from
            // whichever other thread happened to sort first. That varies between runs, so the same freeze
            // fingerprinted differently and the deduplication this exists for silently stopped working.
            if (
                inUiSection
                && (
                    line.StartsWith("###", StringComparison.Ordinal)
                    || line.StartsWith(CollapsibleSections.BeginPrefix, StringComparison.Ordinal)
                    || line.StartsWith(CollapsibleSections.End, StringComparison.Ordinal)
                )
            )
                break;
            if (!inUiSection || !line.StartsWith("    ", StringComparison.Ordinal))
                continue;

            var frame = line.Trim();
            // Skip the plumbing that appears in every stack and identifies nothing.
            if (
                frame.Length == 0
                || frame.StartsWith("InlinedCallFrame", StringComparison.Ordinal)
                || frame.StartsWith("(native)", StringComparison.Ordinal)
                || frame.Contains("IL_STUB", StringComparison.Ordinal)
            )
                continue;
            frames.Add(frame);
            if (frames.Count == FramesToHash)
                break;
        }
        return frames;
    }

    /// <summary>
    /// The version from the exe path, when the path carries one. Included so that the same freeze in a
    /// new release is treated as a new problem — it may well have a different cause, and a card that
    /// spans four releases helps nobody.
    /// </summary>
    private static string VersionOf(string exePath)
    {
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
            return info.FileVersion ?? "";
        }
        catch (Exception)
        {
            // A path we cannot read still fingerprints; it just loses the version component.
            return Regex.Match(exePath ?? "", @"\d+\.\d+\.\d+").Value;
        }
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        // Twelve hex characters is far more than enough to avoid collisions among our volumes, and
        // short enough for a human to compare two cards at a glance.
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
