using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Pins the native layout of <c>WAITCHAIN_NODE_INFO</c>.
///
/// **This exists because getting it wrong is silent.** The second half of the native structure is a UNION
/// in which the thread fields overlap the 256-byte object name. Declare them *after* that name, as the
/// documentation's field order suggests, and Windows writes ProcessId and ThreadId at offset 8 while we
/// read them from around offset 276; make the managed struct the wrong SIZE and every node after the first
/// in the array is misaligned as well. The result is a report that states thread and process ids which are
/// pure garbage — no crash, no failing test, nothing to notice, just a card that sends whoever reads it
/// looking for a thread that never existed.
///
/// Wait chains are one of the more useful things a freeze report can carry, so quietly wrong ones are
/// worse than none. These three numbers are cheap to assert.
/// </summary>
[TestFixture]
public class WaitChainCollectorLayoutTests
{
    [Test]
    public void DescribeNativeNodeLayout_MatchesNativeStruct()
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
