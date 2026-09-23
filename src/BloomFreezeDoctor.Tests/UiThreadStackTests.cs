using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Recognising the UI thread, and saying honestly what its stack shows.
///
/// Both stacks below are REAL, copied from reports the Doctor produced for a simulated blocked wait and a
/// simulated spin. The spin one is the hard case: every frame above the base is gone, so a report can name
/// the thread burning a whole core and say nothing about where, which is the only thing anybody needs.
/// </summary>
[TestFixture]
public class UiThreadStackTests
{
    /// <summary>A UI thread stopped dead in a managed wait. Walks perfectly, because it is not moving.</summary>
    private static readonly string[] Blocked =
    {
        "System.Threading.Monitor.ObjWait",
        "System.Threading.Monitor.Wait",
        "System.Threading.ManualResetEventSlim.Wait",
        "Bloom.FreezeDoctor.FreezeSimulator.Simulate",
        "System.Windows.Forms.Timer+TimerNativeWindow.WndProc",
        "(dynamicClass).IL_STUB_ReversePInvoke",
        "InlinedCallFrame",
        "(native)",
        "System.Windows.Forms.Application+ThreadContext.RunMessageLoop",
        "Bloom.Program.Run",
        "Bloom.Program.Main",
    };

    /// <summary>
    /// The same UI thread while SPINNING, verbatim from a real report. Everything above the base is gone -
    /// including the RunMessageLoop frame, so the thread cannot be identified by that alone.
    /// </summary>
    private static readonly string[] Spinning =
    {
        "InlinedCallFrame",
        "InlinedCallFrame",
        "(native)",
        "(native)",
        "(native)",
        "(native)",
        "Bloom.Program.Run",
        "Bloom.Program.Main",
    };

    [Test]
    public void LooksLikeTheUiThread_SpinningStack_True()
    {
        Assert.That(
            UiThreadStack.LooksLikeTheUiThread(Blocked),
            Is.True,
            "setup: the easy case must work"
        );
        Assert.That(
            UiThreadStack.LooksLikeTheUiThread(Spinning),
            Is.True,
            "the message-loop frame is gone, but Program.Main is not - and no other thread has it"
        );
    }

    [Test]
    public void LooksLikeTheUiThread_WorkerThread_False()
    {
        var worker = new[]
        {
            "System.Threading.LowLevelLifoSemaphore.WaitForSignal",
            "System.Threading.PortableThreadPool+WorkerThread.WorkerThreadStart",
            "DebuggerU2MCatchHandlerFrame",
        };

        Assert.That(UiThreadStack.LooksLikeTheUiThread(worker), Is.False);
    }

    [Test]
    public void Describe_UnreadableStack_SaysSoAndNamesThread()
    {
        // Guards against describing this stack as "The UI thread is blocked in Bloom.Program.Run" - wrong
        // twice, since it is not blocked and that frame is the bottom of every UI thread ever. Saying the
        // stack could not be read is worth more than a confident sentence about a frame that carries no
        // information.
        var said = UiThreadStack.Describe(Spinning, isAboutAFreeze: true, threadId: 93808);

        Assert.That(said, Does.Contain("could not be read"));
        Assert.That(
            said,
            Does.Contain("93808"),
            "name the thread, so the dump can be opened at it"
        );
        Assert.That(
            said,
            Does.Not.Contain("blocked in"),
            "it is running, not blocked - that is why the walk failed"
        );
    }

    [Test]
    public void Describe_BlockedStack_NamesBlockingCall()
    {
        Assert.That(
            UiThreadStack.Describe(Blocked, isAboutAFreeze: true, threadId: 1),
            Is.EqualTo("The UI thread is blocked in System.Threading.Monitor.ObjWait.")
        );
    }

    [Test]
    public void Describe_IdleMessagePump_NotCalledBlocked()
    {
        var idle = new[]
        {
            "System.Windows.Forms.UnsafeNativeMethods.WaitMessage",
            "System.Windows.Forms.Application+ThreadContext.RunMessageLoop",
            "Bloom.Program.Run",
            "Bloom.Program.Main",
        };

        Assert.That(
            UiThreadStack.Describe(idle, isAboutAFreeze: true, threadId: 1),
            Does.Contain("idle or pumping")
        );
    }

    [Test]
    public void DescribeBlockingCall_OnlyBaseFrames_NothingUseful()
    {
        // Program.Run matches the "starts with Bloom." rule that finds a genuine Bloom frame, so it has to
        // be excluded explicitly or it wins on any stack where nothing else survived.
        Assert.That(UiThreadStack.DescribeBlockingCall(Spinning), Is.Null.Or.Empty);
        Assert.That(UiThreadStack.SaysAnythingUseful(Spinning), Is.False);
        Assert.That(
            UiThreadStack.SaysAnythingUseful(Blocked),
            Is.True,
            "sanity check: a stack with real frames must not be called unreadable"
        );
    }
}
