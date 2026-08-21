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
                    text.AppendLine(
                        node.ObjectType == WaitChainObjectType.Thread
                            ? $"    - waiting on thread {node.ThreadId} in process {node.ProcessId} ({node.ObjectStatus})"
                            : $"    - waiting on a {node.ObjectType} ({node.ObjectStatus})"
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
            else if (deadlocked)
                headline =
                    "Windows reports a deadlock cycle between threads (see the wait chains).";

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WaitChainNode
    {
        public WaitChainObjectType ObjectType;
        public WaitChainObjectStatus ObjectStatus;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string ObjectName;

        public long Timeout;
        public bool Alertable;

        // The union's thread branch; valid when ObjectType is Thread.
        public int ProcessId;
        public int ThreadId;
        public int WaitTime;
        public int ContextSwitches;
    }

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
