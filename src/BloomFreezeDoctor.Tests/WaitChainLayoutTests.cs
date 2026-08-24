using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Pins the native layout of <c>WAITCHAIN_NODE_INFO</c>.
///
/// **This exists because getting it wrong is silent.** The struct originally declared the thread fields
/// *after* a 256-byte object name, not knowing that the second half of the native structure is a UNION in
/// which they overlap that name. Windows wrote ProcessId and ThreadId at offset 8; we read them from
/// around offset 276; and because the managed struct was also the wrong SIZE, every node after the first
/// in the array was misaligned as well. The result was a report that stated thread and process ids which
/// were pure garbage — no crash, no failing test, nothing to notice, just a card that sent whoever read it
/// looking for a thread that never existed.
///
/// Wait chains are one of the more useful things a freeze report can carry, so quietly wrong ones are
/// worse than none. These three numbers are cheap to assert and would have caught it.
/// </summary>
[TestFixture]
public class WaitChainLayoutTests
{
    [Test]
    public void The_native_node_layout_is_what_windows_expects()
    {
        var (size, processIdOffset, threadIdOffset) = WaitChainCollector.DescribeNativeNodeLayout();

        // Two 4-byte enums, then the union. The union's larger branch is 272 bytes: a 256-byte
        // ObjectName[128], an 8-byte LARGE_INTEGER Timeout landing on the next 8-boundary, a 4-byte BOOL
        // Alertable, and 4 bytes of tail padding forced by the timeout's alignment. 8 + 272 = 280.
        Assert.That(
            size,
            Is.EqualTo(280),
            "the struct must be the same size as the native one, or the API's array is read with the "
                + "wrong stride and every node after the first is misaligned"
        );

        // The union begins immediately after the two enums, so the thread branch starts at 8 - NOT after
        // the object name, which is the mistake this test exists to prevent.
        Assert.That(processIdOffset, Is.EqualTo(8), "ProcessId is the first DWORD of the union");
        Assert.That(threadIdOffset, Is.EqualTo(12), "ThreadId is the second");
    }
}
