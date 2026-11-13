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
/// How it is obtained, in order of preference:
///
/// 1. **Primary — <c>DiagnosticsClient.WriteDump</c>.** The target's own runtime writes the dump over
///    the diagnostics IPC pipe: about 7.5 MB and 1.4 s for a real Bloom, and it works **while the UI
///    thread is wedged**. Crucially it is also the safest mechanism available, because the target does
///    the work: if we die halfway, it simply finishes or abandons on its own.
/// 2. **Fallback — a NON-suspending ClrMD attach.** ~200 ms, stacks only, and it cannot strand the
///    target.
/// 3. **Never a suspending attach.** If the Doctor dies while holding
///    <c>AttachToProcess(suspend: true)</c>, the target stays suspended for good. A Doctor crash must
///    never convert a recoverable hang into an unrecoverable one.
/// </summary>
public sealed class ManagedStacksCollector : IEvidenceCollector
{
    /// <summary>How many frames of any one thread to print. Deep enough to explain, short enough to read.</summary>
    private const int MaxFramesPerThread = 40;

    /// <summary>How many threads to print in full before summarising the rest.</summary>
    private const int MaxThreadsInFull = 25;

    private readonly Action<int, string> _writeDump;
    private readonly TimeSpan _freshDumpDeadlineWithEarlyDump;
    private readonly TimeSpan _freshDumpDeadline;
    private readonly TimeSpan _earlyDumpWait;

    /// <summary>A collector that dumps through the target's runtime, with the standard deadlines.</summary>
    public ManagedStacksCollector()
        : this(WriteDump, FreshDumpDeadlineWithEarlyDump, FreshDumpDeadline, EarlyDumpWait) { }

    /// <summary>
    /// For tests: a dump writer that can be made slow, and deadlines short enough to wait out.
    /// </summary>
    internal ManagedStacksCollector(
        Action<int, string> writeDump,
        TimeSpan freshDumpDeadlineWithEarlyDump,
        TimeSpan freshDumpDeadline,
        TimeSpan earlyDumpWait
    )
    {
        _writeDump = writeDump;
        _freshDumpDeadlineWithEarlyDump = freshDumpDeadlineWithEarlyDump;
        _freshDumpDeadline = freshDumpDeadline;
        _earlyDumpWait = earlyDumpWait;
    }

    /// <inheritdoc />
    public string Title => "Managed stacks";

    /// <inheritdoc />
    /// <remarks>
    /// Generous, because a real Bloom's dump took 1.4 s and a loaded machine will be slower — but
    /// bounded, because the user may be waiting to restart Bloom.
    /// </remarks>
    public TimeSpan Budget => TimeSpan.FromSeconds(45);

    /// <inheritdoc />
    /// <remarks>
    /// A Bloom that has gone can still have its stacks reported, from the dump taken earlier in the same
    /// freeze. See <see cref="EarlyFreezeDump"/>.
    /// </remarks>
    public bool AppliesTo(GatherContext context) =>
        context.ProcessWasAlive || context.EarlyDump != null;

    /// <inheritdoc />
    public async Task<ReportSection> CollectAsync(
        GatherContext context,
        CancellationToken cancellation
    )
    {
        var started = Stopwatch.StartNew();

        // Both the fresh-dump attempt and the fallback below may need the early dump; they share one wait,
        // so a runtime that writes neither costs the live read below only one wait for it.
        Task<EarlyFreezeDump.Taken?>? sharedEarlyDumpWait = null;
        Task<EarlyFreezeDump.Taken?> WaitForEarlyDump() =>
            sharedEarlyDumpWait ??= WaitForEarlyDumpAsync(context.EarlyDump!, cancellation);

        var attempt = context.ProcessWasAlive
            ? await TryFreshDumpAsync(context, WaitForEarlyDump, cancellation).ConfigureAwait(false)
            : null;

        if (attempt != null)
            return attempt with { Duration = started.Elapsed };

        // No fresh dump - usually because Bloom has gone, sometimes because it went while we asked. The one
        // taken when the freeze began is older, but it is a dump, which a live read cannot give us.
        var taken =
            context.EarlyDump == null ? null : await WaitForEarlyDump().ConfigureAwait(false);
        if (taken != null)
        {
            var early = await Task.Run(
                    () => UseEarlyDump(context, taken, cancellation),
                    cancellation
                )
                .ConfigureAwait(false);
            return early with { Duration = started.Elapsed };
        }
        if (!context.ProcessWasAlive)
            return ReportSection.Failed(
                Title,
                "Bloom had gone by the time this report was gathered, and the dump started when it first "
                    + $"stopped responding could not be written, or not within {_earlyDumpWait.TotalSeconds:F0}s",
                started.Elapsed
            );

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
    /// How long a fresh dump may take when there is an early dump to fall back on. A dump takes a second or
    /// two; one that has not come in this long is from a runtime that is not answering.
    /// </summary>
    private static readonly TimeSpan FreshDumpDeadlineWithEarlyDump = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How long a fresh dump may take in any case. Waiting out the collector's whole <see cref="Budget"/>
    /// for a runtime that is not answering would leave no time to read the stacks from the live process
    /// instead.
    /// </summary>
    private static readonly TimeSpan FreshDumpDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// <see cref="TryDumpAndWalk"/> off the calling thread, since ClrMD and the dump write are synchronous and
    /// slow. Null if it fails, or misses its deadline: <see cref="FreshDumpDeadlineWithEarlyDump"/> when
    /// there is an early dump to use instead, otherwise <see cref="FreshDumpDeadline"/>, and none for a
    /// crashing Bloom that is waiting for it, which only the collector's budget ends. One that misses its
    /// deadline, or is cancelled, is left to finish on its own, the report does without what it produces,
    /// and <see cref="Abandon"/> cleans up after it. <paramref name="waitForEarlyDump"/> is the caller's
    /// shared wait for the early dump.
    /// </summary>
    private async Task<ReportSection?> TryFreshDumpAsync(
        GatherContext context,
        Func<Task<EarlyFreezeDump.Taken?>> waitForEarlyDump,
        CancellationToken cancellation
    )
    {
        var started = Stopwatch.StartNew();
        var fresh = Task.Run(() => TryDumpAndWalk(context, cancellation), cancellation);
        try
        {
            // A crashing Bloom is waiting for this dump before it exits, and will be released as soon as
            // the report goes on without it - so a slow one is never abandoned. It is the only dump of the
            // crash.
            if (context.TargetNoLongerNeeded != null)
                return await fresh.WaitAsync(cancellation).ConfigureAwait(false);
            if (context.EarlyDump != null)
            {
                if (await FinishesWithinAsync(fresh, _freshDumpDeadlineWithEarlyDump, cancellation))
                    return await fresh.ConfigureAwait(false);
                // Give up on the fresh dump only for an early one that is really there; one that failed
                // to be written is no fallback, and a slow fresh dump is better than none.
                var early = await waitForEarlyDump().ConfigureAwait(false);
                if (early != null && !fresh.IsCompleted)
                    return Abandon(fresh, context);
            }
            return await FinishesWithinAsync(
                fresh,
                _freshDumpDeadline - started.Elapsed,
                cancellation
            )
                ? await fresh.ConfigureAwait(false)
                : Abandon(fresh, context);
        }
        catch (OperationCanceledException)
        {
            Abandon(fresh, context);
            throw;
        }
    }

    /// <summary>
    /// Goes on without a fresh dump that is still being written. The runtime may yet finish it, into the
    /// report's folder, where nothing would ever delete it: the gather deletes that folder when it is done,
    /// which fails while the dump is still open. So the dump is deleted once the attempt ends. Only the
    /// dump: other collectors may still be writing into the folder. Always null.
    /// </summary>
    private static ReportSection? Abandon(Task fresh, GatherContext context)
    {
        var dumpPath = DumpPathFor(context);
        _ = fresh.ContinueWith(
            _ => TryDelete(dumpPath),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default
        );
        return null;
    }

    /// <summary>Whether <paramref name="task"/> finishes within <paramref name="time"/> (at once if that is past).</summary>
    private static async Task<bool> FinishesWithinAsync(
        Task task,
        TimeSpan time,
        CancellationToken cancellation
    )
    {
        var timeout = time > TimeSpan.Zero ? time : TimeSpan.Zero;
        var first = await Task.WhenAny(task, Task.Delay(timeout, cancellation))
            .ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        return first == task;
    }

    /// <summary>
    /// Asks the target's runtime to dump itself, then reads the managed stacks back out of that dump.
    /// Returns null if the dump could not be produced or read, so the caller can fall back.
    /// </summary>
    private ReportSection? TryDumpAndWalk(GatherContext context, CancellationToken cancellation)
    {
        var dumpPath = DumpPathFor(context);
        var text = new StringBuilder();
        try
        {
            Directory.CreateDirectory(context.ArtifactDirectory);
            var dumpTimer = Stopwatch.StartNew();
            _writeDump(context.Target.ProcessId, dumpPath);
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

            return WalkDump(dumpPath, text, context.IsAboutAFreeze);
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
    /// Has the target's own runtime write a dump of it, over the diagnostics IPC pipe. Throws if the pipe
    /// does not answer. Used both here and for the dump taken when a freeze begins (see
    /// <see cref="EarlyFreezeDump"/>), so that the two are the same kind of dump.
    /// </summary>
    public static void WriteDump(int processId, string dumpPath)
    {
        // DumpType.Normal is what dotnet-dump calls a mini dump: small, and still enough for
        // ClrMD to walk managed stacks.
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
        new DiagnosticsClient(processId).WriteDump(
            DumpType.Normal,
            dumpPath,
            logDumpGeneration: false
        );
    }

    /// <summary>
    /// Reports the managed stacks from the dump taken when this freeze began, because no fresh one could be
    /// taken. The dump is moved into the report's artifacts whether or not its stacks can be read: it is
    /// the evidence a developer most needs and cannot get any other way.
    /// </summary>
    private ReportSection UseEarlyDump(
        GatherContext context,
        EarlyFreezeDump.Taken early,
        CancellationToken cancellation
    )
    {
        // A name of its own: a fresh dump that missed its deadline may yet be written to the usual one.
        var dumpPath = Path.Combine(
            context.ArtifactDirectory,
            $"bloom-{context.Target.ProcessId}-early.dmp"
        );
        try
        {
            Directory.CreateDirectory(context.ArtifactDirectory);
            RobustFile.Move(early.DumpPath, dumpPath);
        }
        catch (Exception e)
        {
            return ReportSection.Failed(
                Title,
                $"a dump was taken when Bloom first stopped responding, but it could not be added to this "
                    + $"report: {e.GetType().Name}: {e.Message}"
            );
        }

        var size = new FileInfo(dumpPath).Length;
        var before = DateTimeOffset.UtcNow - early.TakenAtUtc;
        var text = new StringBuilder();
        text.AppendLine(
            (
                context.ProcessWasAlive
                    ? "The runtime's dump pipe did not answer when this report was gathered, so this is "
                    : "Bloom had gone by the time this report was gathered, so this is "
            )
                + $"the dump the Doctor took when Bloom first stopped responding, at "
                + $"{early.TakenAtUtc:HH:mm:ss}Z ({before.TotalSeconds:F0}s before this report), "
                + $"{size / 1024.0 / 1024.0:F1} MB. The stacks below are from that moment."
        );
        text.AppendLine();

        try
        {
            cancellation.ThrowIfCancellationRequested();
            return WalkDump(dumpPath, text, context.IsAboutAFreeze);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            text.AppendLine(
                $"Its managed stacks could not be read here ({e.GetType().Name}: {e.Message}); the dump "
                    + "is attached for reading elsewhere."
            );
            return DumpSection(text, dumpPath);
        }
    }

    /// <summary>The name the Doctor gives a dump of this process, wherever it is written.</summary>
    public static string DumpFileName(int processId) => $"bloom-{processId}.dmp";

    /// <summary>Where this report's dump goes. The caller creates the folder, where it can handle failing to.</summary>
    private static string DumpPathFor(GatherContext context) =>
        Path.Combine(context.ArtifactDirectory, DumpFileName(context.Target.ProcessId));

    /// <summary>
    /// How long a report that needs the early dump waits for one still being written. Writing takes a
    /// second or two, so this is far more than it should need, and short against the collector's budget.
    /// </summary>
    private static readonly TimeSpan EarlyDumpWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The early dump, once written, or null if writing it failed or did not finish within
    /// <see cref="EarlyDumpWait"/>. The wait starts here, when the dump is known to be needed, not when it
    /// was claimed. Whoever claimed it deletes it when it is written, however late.
    /// </summary>
    private async Task<EarlyFreezeDump.Taken?> WaitForEarlyDumpAsync(
        Task<EarlyFreezeDump.Taken?> earlyDump,
        CancellationToken cancellation
    )
    {
        var finished = await Task.WhenAny(earlyDump, Task.Delay(_earlyDumpWait, cancellation))
            .ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        return finished == earlyDump ? earlyDump.Result : null;
    }

    /// <summary>The section for stacks read, or not, from the dump at <paramref name="dumpPath"/>.</summary>
    private ReportSection DumpSection(
        StringBuilder text,
        string dumpPath,
        string? headline = null
    ) =>
        new()
        {
            Title = Title,
            Body = text.ToString(),
            Artifacts = new[] { dumpPath },
            Headline = headline,
        };

    /// <summary>
    /// Appends the managed stacks read from a dump file to <paramref name="text"/> and returns the section,
    /// carrying the dump as its artifact. Throws if the dump cannot be read.
    ///
    /// The file is opened here rather than by ClrMD, because when ClrMD fails to read a dump it opened
    /// itself, it leaves the file open until a finalizer runs, and the file has to be free to be moved into
    /// the report's bundle or deleted.
    /// </summary>
    private ReportSection WalkDump(string dumpPath, StringBuilder text, bool isAboutAFreeze)
    {
        using var stream = RobustFile.OpenRead(dumpPath);
        using var target = DataTarget.LoadDump(dumpPath, stream, leaveOpen: true);
        var clr = target.ClrVersions.FirstOrDefault();
        if (clr == null)
        {
            text.AppendLine("The dump contains no CLR, so no managed stacks could be read.");
            return DumpSection(text, dumpPath);
        }

        using var runtime = clr.CreateRuntime();
        return DumpSection(text, dumpPath, AppendThreads(text, runtime, isAboutAFreeze));
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

        // See UiThreadStack: a SPINNING thread's upper frames do not survive the stack walk, so matching
        // only on the message-loop frame would miss the UI thread exactly when it matters most.
        var uiThread = threads.FirstOrDefault(t => UiThreadStack.LooksLikeTheUiThread(t.Frames));

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
            headline = UiThreadStack.Describe(
                uiThread.Frames,
                isAboutAFreeze,
                // The OS id, because that is what the thread listing below and the CPU-per-thread
                // table both print. A different id here would be a puzzle rather than a pointer.
                (int)uiThread.Thread.OSThreadId
            );
            // The id is in the heading because every other section that mentions a thread uses it: the
            // wait chains name "thread 83756", the CPU table names "thread 109948", and the stack listing
            // below prints one per thread. Without it here the reader cannot tell whether the thread in the
            // wait chain IS the UI thread, which is usually the whole question.
            text.AppendLine(
                $"### The UI thread (thread {uiThread.Thread.OSThreadId}, the one running the message loop)"
            );
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
