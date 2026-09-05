using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Recognising the UI thread, and saying honestly what its stack shows.
///
/// Both stacks below are REAL, copied from reports the Doctor produced for a simulated blocked wait and a
/// simulated spin. The spin one is the reason this class exists: the report named the thread burning a
/// whole core and then said nothing about where it was spinning, which is the only thing anybody needs.
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
    /// including the RunMessageLoop frame the old code identified the thread by.
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
    public void A_spinning_UI_thread_is_still_recognised_as_the_UI_thread()
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
    public void An_ordinary_worker_is_not_mistaken_for_the_UI_thread()
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
    public void A_stack_that_could_not_be_read_says_so_and_points_at_the_dump()
    {
        // The bug. Left to the old rules this stack produced "The UI thread is blocked in
        // Bloom.Program.Run" - wrong twice, since it is not blocked and that frame is the bottom of every
        // UI thread ever. Saying the stack could not be read is worth more than a confident sentence about
        // a frame that carries no information.
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
    public void A_real_block_is_still_named()
    {
        Assert.That(
            UiThreadStack.Describe(Blocked, isAboutAFreeze: true, threadId: 1),
            Is.EqualTo("The UI thread is blocked in System.Threading.Monitor.ObjWait.")
        );
    }

    [Test]
    public void An_idle_pump_is_not_called_a_block()
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
    public void The_bottom_of_every_UI_thread_is_never_the_answer()
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
