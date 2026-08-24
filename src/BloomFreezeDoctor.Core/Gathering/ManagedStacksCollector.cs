using System.Diagnostics;
using System.Text;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Runtime;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// The most valuable section of any report: what every managed thread was doing, in words, with the
/// blocked UI thread first.
///
/// The Phase 0 spike settled how to get this (docs/SPIKE-FINDINGS.md §1, §6, §7):
///
/// 1. **Primary — <c>DiagnosticsClient.WriteDump</c>.** The target's own runtime writes the dump over
///    the diagnostics IPC pipe. Measured at 2.2 MB / ~0.5 s for a small app and 7.5 MB / 1.4 s for a
///    real Bloom, and it works **while the UI thread is wedged**. Crucially it is also the safest
///    mechanism available, because the target does the work: if we die halfway, it simply finishes or
///    abandons on its own.
/// 2. **Fallback — a NON-suspending ClrMD attach.** ~200 ms, stacks only, and it cannot strand the
///    target.
/// 3. **Never a suspending attach.** Measured: hard-killing a process that held
///    <c>AttachToProcess(suspend: true)</c> left the target permanently suspended, unrecoverable
///    except by killing it. A Doctor crash must never convert a recoverable hang into an
///    unrecoverable one. If you are tempted to add it back, the review question is "what happens to
///    Bloom if this process is killed on the next line?"
/// </summary>
public sealed class ManagedStacksCollector : IEvidenceCollector
{
    /// <summary>How many frames of any one thread to print. Deep enough to explain, short enough to read.</summary>
    private const int MaxFramesPerThread = 40;

    /// <summary>How many threads to print in full before summarising the rest.</summary>
    private const int MaxThreadsInFull = 25;

    /// <inheritdoc />
    public string Title => "Managed stacks";

    /// <inheritdoc />
    /// <remarks>
    /// Generous, because a real Bloom's dump took 1.4 s and a loaded machine will be slower — but
    /// bounded, because the user may be waiting to restart Bloom.
    /// </remarks>
    public TimeSpan Budget => TimeSpan.FromSeconds(45);

    /// <inheritdoc />
    public bool AppliesTo(GatherContext context) => context.ProcessWasAlive;

    /// <inheritdoc />
    public async Task<ReportSection> CollectAsync(
        GatherContext context,
        CancellationToken cancellation
    )
    {
        var started = Stopwatch.StartNew();

        // Run the whole thing off the calling thread: ClrMD and the dump write are synchronous and
        // slow, and the Doctor's own UI must stay alive while it diagnoses (plan §2.1).
        var attempt = await Task.Run(() => TryDumpAndWalk(context, cancellation), cancellation)
            .ConfigureAwait(false);

        if (attempt != null)
            return attempt with { Duration = started.Elapsed };

        // The pipe did not answer. Fall back to reading the live process without suspending it.
        var live = await Task.Run(() => TryLiveWalk(context), cancellation).ConfigureAwait(false);
        return (
            live
            ?? ReportSection.Failed(
                "Managed stacks",
                "neither the runtime's dump pipe nor a live read could produce managed stacks; the "
                    + "OS-level evidence below is what we have"
            )
        ) with
        {
            Duration = started.Elapsed,
        };
    }

    /// <summary>
    /// Asks the target's runtime to dump itself, then reads the managed stacks back out of that dump.
    /// Returns null if the dump could not be produced or read, so the caller can fall back.
    /// </summary>
    private ReportSection? TryDumpAndWalk(GatherContext context, CancellationToken cancellation)
    {
        var dumpPath = Path.Combine(
            context.ArtifactDirectory,
            $"bloom-{context.Target.ProcessId}.dmp"
        );
        var text = new StringBuilder();
        try
        {
            Directory.CreateDirectory(context.ArtifactDirectory);
            var client = new DiagnosticsClient(context.Target.ProcessId);
            var dumpTimer = Stopwatch.StartNew();
            // DumpType.Normal is what dotnet-dump calls a mini dump: small, and still enough for
            // ClrMD to walk managed stacks. Verified in the spike against a real Bloom.
            client.WriteDump(DumpType.Normal, dumpPath, logDumpGeneration: false);
            dumpTimer.Stop();

            var size = new FileInfo(dumpPath).Length;
            text.AppendLine(
                $"Dump written by the target's own runtime: {size / 1024.0 / 1024.0:F1} MB in "
                    + $"{dumpTimer.ElapsedMilliseconds} ms."
            );
            text.AppendLine();

            cancellation.ThrowIfCancellationRequested();

            using var target = DataTarget.LoadDump(dumpPath);
            var clr = target.ClrVersions.FirstOrDefault();
            if (clr == null)
            {
                text.AppendLine("The dump contains no CLR, so no managed stacks could be read.");
                return new ReportSection
                {
                    Title = Title,
                    Body = text.ToString(),
                    Artifacts = new[] { dumpPath },
                };
            }

            using var runtime = clr.CreateRuntime();
            var headline = AppendThreads(text, runtime);
            return new ReportSection
            {
                Title = Title,
                Body = text.ToString(),
                Artifacts = new[] { dumpPath },
                Headline = headline,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // Most likely the diagnostics pipe is unreachable — the process may have died between the
            // detector noticing and us asking. Fall back rather than give up.
            TryDelete(dumpPath);
            Debug.WriteLine($"WriteDump path failed: {e}");
            return null;
        }
    }

    /// <summary>
    /// Reads managed stacks straight from the live process, without suspending it. Slightly less
    /// consistent than a dump, since the target keeps running underneath, but it cannot strand
    /// anything and it is fast.
    /// </summary>
    private ReportSection? TryLiveWalk(GatherContext context)
    {
        var text = new StringBuilder();
        try
        {
            text.AppendLine(
                "The runtime's dump pipe did not answer, so these stacks were read from the live "
                    + "process without suspending it. They may be slightly inconsistent."
            );
            text.AppendLine();
            using var target = DataTarget.AttachToProcess(context.Target.ProcessId, suspend: false);
            var clr = target.ClrVersions.FirstOrDefault();
            if (clr == null)
                return null;
            using var runtime = clr.CreateRuntime();
            var headline = AppendThreads(text, runtime);
            return new ReportSection
            {
                Title = Title,
                Body = text.ToString(),
                Headline = headline,
            };
        }
        catch (Exception e)
        {
            Debug.WriteLine($"Live walk failed: {e}");
            return null;
        }
    }

    /// <summary>
    /// Writes every managed thread's stack, most interesting first, and returns a one-line headline
    /// naming what the UI thread was doing — which is the sentence a human triaging the card reads
    /// first.
    /// </summary>
    private static string? AppendThreads(StringBuilder text, ClrRuntime runtime)
    {
        var threads = runtime
            .Threads.Where(t => t.IsAlive)
            .Select(t => new { Thread = t, Frames = SafeFrames(t) })
            .ToList();

        var uiThread = threads.FirstOrDefault(t =>
            t.Frames.Any(f => f.Contains("RunMessageLoop", StringComparison.Ordinal))
        );

        text.AppendLine(
            $"{threads.Count} managed thread(s); {threads.Count(t => t.Frames.Count > 0)} with walkable stacks."
        );
        text.AppendLine();

        string? headline = null;
        if (uiThread != null)
        {
            var blocking = DescribeBlockingCall(uiThread.Frames);
            headline =
                blocking == null
                    ? "The UI thread is in its message loop (idle or pumping)."
                    : $"The UI thread is blocked in {blocking}.";
            text.AppendLine("### The UI thread (the one running the message loop)");
            text.AppendLine();
            AppendFrames(text, uiThread.Frames);
            text.AppendLine();
        }

        text.AppendLine("### All other managed threads");
        text.AppendLine();
        var shown = 0;
        foreach (
            var thread in threads.Where(t => t != uiThread).OrderByDescending(t => t.Frames.Count)
        )
        {
            if (shown++ >= MaxThreadsInFull)
            {
                text.AppendLine(
                    $"({threads.Count - shown} further thread(s) omitted; the full dump has them.)"
                );
                break;
            }
            text.AppendLine($"Thread {thread.Thread.OSThreadId}:");
            AppendFrames(text, thread.Frames);
            text.AppendLine();
        }
        return headline;
    }

    /// <summary>
    /// Picks out the frame worth naming in the headline: the first frame that is Bloom's own code or a
    /// recognisable blocking primitive, skipping the plumbing nobody needs to read.
    /// </summary>
    private static string? DescribeBlockingCall(IReadOnlyList<string> frames)
    {
        // Frames run innermost-first. The innermost interesting thing is what it is stuck in.
        foreach (var frame in frames)
        {
            if (frame.Contains("WaitMessage", StringComparison.Ordinal))
                return null; // a healthy idle pump, not a block
            if (
                frame.StartsWith("Bloom.", StringComparison.Ordinal)
                || frame.Contains("Monitor.", StringComparison.Ordinal)
                || frame.Contains("WaitHandle", StringComparison.Ordinal)
                || frame.Contains("ManualResetEvent", StringComparison.Ordinal)
                || frame.Contains("SemaphoreSlim", StringComparison.Ordinal)
                || frame.Contains("Task.Wait", StringComparison.Ordinal)
                || frame.Contains("Thread.Sleep", StringComparison.Ordinal)
                || frame.Contains("Socket", StringComparison.Ordinal)
                || frame.Contains("HttpClient", StringComparison.Ordinal)
            )
                return frame;
        }
        return frames.FirstOrDefault();
    }

    private static void AppendFrames(StringBuilder text, IReadOnlyList<string> frames)
    {
        if (frames.Count == 0)
        {
            text.AppendLine("    (no walkable managed frames)");
            return;
        }
        foreach (var frame in frames.Take(MaxFramesPerThread))
            text.AppendLine("    " + frame);
        if (frames.Count > MaxFramesPerThread)
            text.AppendLine($"    ... {frames.Count - MaxFramesPerThread} more frame(s)");
    }

    /// <summary>
    /// Reads one thread's frames as strings. Stack walking can fail per-thread (a thread caught mid
    /// transition, say), and one awkward thread must not cost us the other forty-eight.
    /// </summary>
    private static List<string> SafeFrames(ClrThread thread)
    {
        try
        {
            return thread
                .EnumerateStackTrace()
                .Take(MaxFramesPerThread * 2)
                .Select(Describe)
                .ToList();
        }
        catch (Exception)
        {
            return new List<string>();
        }
    }

    private static string Describe(ClrStackFrame frame)
    {
        if (frame.Method == null)
            return frame.FrameName ?? "(native)";
        var type = frame.Method.Type?.Name;
        return type == null ? frame.Method.Name ?? "?" : $"{type}.{frame.Method.Name}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception) { }
    }
}
