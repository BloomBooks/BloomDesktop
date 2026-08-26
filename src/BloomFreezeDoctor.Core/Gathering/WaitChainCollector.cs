using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BloomFreezeDoctor.Gathering;

/// <summary>
/// Windows' own wait-chain analysis — the data behind Resource Monitor's "Analyze Wait Chain".
///
/// **Set your expectations before reading its output.** The Wait Chain Traversal API does not see .NET
/// `Monitor`/`lock`, `SemaphoreSlim`, or async waits, which are the dominant managed deadlock kinds; the
/// spike confirmed it returns nothing useful for a thread blocked in a managed wait. It earns its place
/// for the cases the managed stacks cannot explain: a cross-process `SendMessage` into WebView2 that
/// never returns, a classic Win32 critical section, or a loader-lock deadlock. So this is a bonus
/// section, and no triage logic is built on it.
/// </summary>
public sealed class WaitChainCollector : IEvidenceCollector
{
    /// <inheritdoc />
    public string Title => "Wait chains";

    /// <inheritdoc />
    public TimeSpan Budget => TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public bool AppliesTo(GatherContext context) => context.ProcessWasAlive;

    /// <inheritdoc />
    public Task<ReportSection> CollectAsync(GatherContext context, CancellationToken cancellation)
    {
        var timer = Stopwatch.StartNew();
        var text = new StringBuilder();
        string? headline = null;

        var session = OpenThreadWaitChainSession(0, IntPtr.Zero);
        if (session == IntPtr.Zero)
            return Task.FromResult(
                ReportSection.Failed(
                    Title,
                    $"could not open a wait-chain session (win32 error {Marshal.GetLastWin32Error()})",
                    timer.Elapsed
                )
            );

        try
        {
            Process process;
            try
            {
                process = Process.GetProcessById(context.Target.ProcessId);
            }
            catch (ArgumentException)
            {
                return Task.FromResult(
                    ReportSection.Failed(Title, "the process had already exited", timer.Elapsed)
                );
            }

            var described = 0;
            var deadlocked = false;
            // Counted once, up front, rather than read again after the loop. `Process.Threads` re-queries
            // the process, and this one may die while we are walking it - which would then throw and lose
            // the chains we had already rendered, for the sake of a number in a footnote.
            var threadCount = process.Threads.Count;
            foreach (ProcessThread thread in process.Threads)
            {
                cancellation.ThrowIfCancellationRequested();
                var nodes = new WaitChainNode[MaxNodes];
                var count = MaxNodes;
                if (
                    !GetThreadWaitChain(
                        session,
                        IntPtr.Zero,
                        0,
                        thread.Id,
                        ref count,
                        nodes,
                        out var isCycle
                    )
                )
                    continue;
                // A chain of one is just the thread itself: it is waiting on nothing Windows can see,
                // which for managed code is the common and uninformative case.
                if (count <= 1)
                    continue;

                described++;
                if (isCycle != 0)
                    deadlocked = true;

                text.AppendLine(
                    $"- **thread {thread.Id}**{(isCycle != 0 ? " — Windows reports a DEADLOCK CYCLE" : "")}"
                );
                for (var i = 0; i < count; i++)
                {
                    var node = nodes[i];
                    // The API's first node IS the thread we asked about, not something it waits on. Calling
                    // it "waiting on thread N" makes the report claim a thread is blocked on itself, which
                    // reads as a self-deadlock and sends whoever holds the card looking for one.
                    if (i == 0 && node.ObjectType == WaitChainObjectType.Thread)
                        text.AppendLine(
                            $"    - this thread ({node.ThreadId} in process {node.ProcessId}) is {node.ObjectStatus}"
                        );
                    else if (node.ObjectType == WaitChainObjectType.Thread)
                        text.AppendLine(
                            $"    - waiting on thread {node.ThreadId} in process {node.ProcessId} ({node.ObjectStatus})"
                        );
                    else
                        text.AppendLine(
                            $"    - waiting on a {node.ObjectType} ({node.ObjectStatus})"
                        );
                }
            }

            if (described == 0)
                text.AppendLine(
                    "No thread reported a wait chain. That is the expected answer for a managed "
                        + "deadlock — this API cannot see `Monitor`, `SemaphoreSlim` or async waits — so "
                        + "it is not evidence that nothing is blocked. The managed stacks are the "
                        + "authority."
                );
            else
            {
                // Written because a reader who knows Bloom well still could not tell what these lines
                // claimed, or why only some threads appeared. A chain nobody can interpret is no better
                // than no chain, and the two questions it raises have short, definite answers.
                text.AppendLine();
                text.AppendLine(
                    "> **How to read these.** Each entry is one thread and what Windows can see it waiting "
                        + "for, in order: the thread itself, then the object it is blocked on, then the "
                        + $"thread that owns that object. Only {described} of this process's "
                        + $"{threadCount} threads appear, and that is not because the rest are "
                        + "idle: a thread is listed only where Windows could name something it waits on, "
                        + "and a `Monitor`, an `await` or a plain `Thread.Sleep` is invisible here."
                );
                text.AppendLine(">");
                text.AppendLine(
                    deadlocked
                        ? "> **Windows reports a cycle**, which is the strong form of this evidence: the "
                            + "chain leads back to a thread already in it, so these threads are each "
                            + "waiting for the other and none of them will ever proceed. That is a genuine "
                            + "deadlock, not merely a slow wait."
                        : "> **Windows did not report a cycle**, so what is above is a one-way block: this "
                            + "thread waits for that one, and that one is not waiting for this one. It says "
                            + "nothing about whether the owner will ever finish — it may be busy, sleeping, "
                            + "or waiting on something this API cannot see. Note that a cycle running "
                            + "*through* a managed lock is invisible here too, so no cycle reported is not "
                            + "proof that there is no deadlock."
                );
                if (deadlocked)
                    headline =
                        "Windows reports a deadlock cycle between threads (see the wait chains).";
            }

            return Task.FromResult(
                new ReportSection
                {
                    Title = Title,
                    Body = text.ToString(),
                    Duration = timer.Elapsed,
                    Headline = headline,
                }
            );
        }
        finally
        {
            CloseThreadWaitChainSession(session);
        }
    }

    #region interop

    private const int MaxNodes = 16;

    private enum WaitChainObjectType
    {
        CriticalSection = 1,
        SendMessage,
        Mutex,
        Alpc,
        Com,
        ThreadWait,
        ProcessWait,
        Thread,
        ComActivation,
        Unknown,
    }

    private enum WaitChainObjectStatus
    {
        NoAccess = 1,
        Running,
        Blocked,
        PidOnly,
        PidOnlyRpcss,
        Owned,
        NotOwned,
        Abandoned,
        Unknown,
        Error,
    }

    /// <summary>
    /// Pads out the remainder of the native union so that this struct is the same SIZE as
    /// <c>WAITCHAIN_NODE_INFO</c>. That matters as much as the field offsets do: the API fills an array,
    /// so if our struct is a different size, every node after the first is read from the wrong place.
    ///
    /// 256 bytes: the union's larger branch is 272 (a 256-byte name, an 8-byte timeout at the next
    /// 8-boundary, a 4-byte BOOL, padded to 272 by the timeout's alignment), of which the four thread
    /// DWORDs above already account for 16.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = 256)]
    private struct WaitChainUnionTail { }

    /// <summary>
    /// <c>WAITCHAIN_NODE_INFO</c>. **Its second half is a UNION**, and getting that wrong is a bug that
    /// reports nonsense rather than failing. Declaring the thread fields *after* a 256-byte name — as one
    /// would for a struct — leaves Windows writing ProcessId and ThreadId at offset 8 while we read them
    /// from around 276, and the resulting size mismatch misaligns every node past the first, so a card
    /// states thread ids that are simply garbage.
    ///
    /// Only the thread branch is declared, because only it is ever read — the lock branch's ObjectName is
    /// not used anywhere. So the union is expressed as its four DWORDs followed by enough padding to make
    /// the whole struct the right size, which is both simpler and blittable (no marshalling on a call made
    /// once per thread).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WaitChainNode
    {
        public WaitChainObjectType ObjectType; // offset 0
        public WaitChainObjectStatus ObjectStatus; // offset 4

        // The union starts here, at offset 8. These are its thread branch, valid when ObjectType is
        // Thread; for a lock node the same bytes are the start of ObjectName and must not be read.
        public int ProcessId; // offset 8
        public int ThreadId; // offset 12
        public int WaitTime; // offset 16
        public int ContextSwitches; // offset 20

        public WaitChainUnionTail Tail; // offsets 24..279
    }

    /// <summary>
    /// The native layout as this build sees it, so a test can pin it without needing a frozen process.
    ///
    /// It exists because the layout was wrong once and nothing noticed: the wait-chain section simply
    /// printed rubbish, which no test and no build could catch. Asserting the size and the two offsets
    /// that are actually read turns the next such mistake into a failing test.
    /// </summary>
    public static (int Size, int ProcessIdOffset, int ThreadIdOffset) DescribeNativeNodeLayout() =>
        (
            Marshal.SizeOf<WaitChainNode>(),
            (int)Marshal.OffsetOf<WaitChainNode>(nameof(WaitChainNode.ProcessId)),
            (int)Marshal.OffsetOf<WaitChainNode>(nameof(WaitChainNode.ThreadId))
        );

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern IntPtr OpenThreadWaitChainSession(uint flags, IntPtr callback);

    [DllImport("advapi32.dll")]
    private static extern void CloseThreadWaitChainSession(IntPtr session);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetThreadWaitChain(
        IntPtr session,
        IntPtr context,
        uint flags,
        int threadId,
        ref int nodeCount,
        [In, Out] WaitChainNode[] nodes,
        out int isCycle
    );

    #endregion
}
