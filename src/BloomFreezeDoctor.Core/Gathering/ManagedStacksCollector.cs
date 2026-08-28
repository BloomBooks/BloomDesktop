using System.Diagnostics;
using System.Text;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Runtime;
using SIL.IO;

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
            //
            // **Chosen deliberately, knowing what it costs.** Normal carries thread stacks
            // but NOT the GC heap - measured at 16-17 MB against a 234 MB working set - so the analysis a
            // dump is usually wanted for is out of reach: no walking the object graph to the Task the UI
            // thread awaits, no `syncblk` to name a monitor's owner (and Windows' wait-chain API cannot see
            // Monitor either, so nothing else can answer that), no `dumpheap` for swallowed exceptions.
            // What it does still give over the report's own prose is arguments and locals per frame
            // (`clrstack -a`) and thread-pool state, which is often enough.
            //
            // DumpType.WithHeap would unlock the rest at something like 150-250 MB a dump, written more
            // slowly, attached to a card, from a machine that may be on a poor connection - and a dump
            // already carries book text and file paths, which is why the attachment is restricted to
            // Developers. If this is revisited, the likely answer is WithHeap for a one-off CRASH and
            // Normal for a FREEZE the user may hit repeatedly in one session.
            client.WriteDump(DumpType.Normal, dumpPath, logDumpGeneration: false);
            dumpTimer.Stop();

            // The dump file now exists, and nothing below needs the process: LoadDump and everything
            // after it read the FILE. So if this is a dying Bloom waiting on us, this is the moment it
            // may go - holding it longer makes it wait on work it has no stake in, including uploading
            // its own dump. See GatherContext.TargetNoLongerNeeded.
            context.TargetNoLongerNeeded?.Invoke();

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
            var headline = AppendThreads(text, runtime, context.IsAboutAFreeze);
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
            var headline = AppendThreads(text, runtime, context.IsAboutAFreeze);
            // Unlike the dump path, this one has been reading the LIVE process, so it could not release
            // the target any earlier than this.
            context.TargetNoLongerNeeded?.Invoke();
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
    private static string? AppendThreads(
        StringBuilder text,
        ClrRuntime runtime,
        bool isAboutAFreeze
    )
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

        var serverWorkers = threads
            .Where(t => t.Frames.Any(f => f.Contains(ServerWorkerFrame, StringComparison.Ordinal)))
            .ToList();
        if (serverWorkers.Count > 0)
        {
            text.AppendLine(DescribeServerPool(serverWorkers.Select(t => t.Frames).ToList()));
            text.AppendLine();
        }

        string? headline = null;
        if (uiThread != null)
        {
            var blocking = DescribeBlockingCall(uiThread.Frames);
            headline =
                blocking != null ? $"The UI thread is blocked in {blocking}."
                // Not blocked. On a freeze report that is a finding worth stating as one; on a crash it is
                // just where the thread happened to be, and phrasing it as a verdict makes the report
                // appear to argue with its own headline. See GatherContext.IsAboutAFreeze.
                : isAboutAFreeze ? "The UI thread is in its message loop (idle or pumping)."
                : "The UI thread was in its message loop when this snapshot was taken.";
            text.AppendLine("### The UI thread (the one running the message loop)");
            text.AppendLine();
            AppendFrames(text, uiThread.Frames);
            text.AppendLine();
        }

        // Collapsed on the card. The UI thread's stack stays open above, because that is usually the
        // answer; these are the ones a reader scrolls past to reach anything else. See CollapsibleSections.
        CollapsibleSections.Begin(text, "All other managed threads");
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
            var label = serverWorkers.Contains(thread) ? " (a server worker)" : "";
            text.AppendLine($"Thread {thread.Thread.OSThreadId}{label}:");
            AppendFrames(text, thread.Frames);
            text.AppendLine();
        }
        CollapsibleSections.Finish(text);
        return headline;
    }

    /// <summary>
    /// The frame every BloomServer worker thread sits on top of. Threads are recognised by their stack
    /// rather than by name because a name is not available here: it lives on the managed Thread object,
    /// which is on the GC heap, and the dumps this walks deliberately do not include the heap.
    /// </summary>
    private const string ServerWorkerFrame = "BloomServer.RequestProcessorLoop";

    /// <summary>What a worker is waiting for, as a category, or null if it is working rather than waiting.</summary>
    private static string? WhatAWorkerIsWaitingFor(IReadOnlyList<string> frames)
    {
        foreach (var frame in frames)
        {
            // Innermost-first, so the first of these we meet is the actual wait. Control.Invoke is the
            // interesting one: a worker sitting there is waiting for the UI thread, which is how a blocked
            // UI thread drags the server pool down with it.
            if (
                frame.Contains("Control.Invoke", StringComparison.Ordinal)
                || frame.Contains("Control.MarshaledInvoke", StringComparison.Ordinal)
                || frame.Contains("WindowsFormsSynchronizationContext", StringComparison.Ordinal)
            )
                return "the UI thread";
            if (frame.Contains("SemaphoreSlim", StringComparison.Ordinal))
                return "an API lock";
            if (frame.Contains("Monitor.", StringComparison.Ordinal))
                return "a lock";
            if (
                frame.Contains("Task.Wait", StringComparison.Ordinal)
                || frame.Contains("GetAwaiter().GetResult", StringComparison.Ordinal)
            )
                return "a task";
            // Where a worker sits when it has no request: waiting for the queue to hand it one.
            if (frame.Contains("RequestProcessorLoop", StringComparison.Ordinal))
                return null;
        }
        return null;
    }

    /// <summary>
    /// The server pool in a sentence, grouped by what its workers are waiting for.
    ///
    /// This is the sentence the counts Bloom publishes cannot produce. "6 blocked of 8" says the pool is
    /// nearly out; "6 of 8 waiting on the UI thread" says WHY, and if the UI thread is itself blocked (the
    /// headline above), the two together are the whole deadlock.
    /// </summary>
    private static string DescribeServerPool(IReadOnlyList<IReadOnlyList<string>> workerStacks)
    {
        var byWait = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var working = 0;
        foreach (var frames in workerStacks)
        {
            var waiting = WhatAWorkerIsWaitingFor(frames);
            if (waiting == null)
                working++;
            else
                byWait[waiting] = byWait.TryGetValue(waiting, out var count) ? count + 1 : 1;
        }

        var text = new StringBuilder($"{workerStacks.Count} of these are BloomServer workers");
        if (byWait.Count == 0)
            return text.Append(", none of them waiting on anything.").ToString();

        text.Append(": ");
        text.Append(
            string.Join(", ", byWait.Select(pair => $"{pair.Value} waiting on {pair.Key}"))
        );
        if (working > 0)
            text.Append($", {working} idle or working");
        return text.Append('.').ToString();
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
            if (RobustFile.Exists(path))
                RobustFile.Delete(path);
        }
        catch (Exception) { }
    }
}
