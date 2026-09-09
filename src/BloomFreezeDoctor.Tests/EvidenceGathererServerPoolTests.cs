using BloomFreezeDoctor.Gathering;
using BloomFreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The sentence a card carries about the server's thread pool.
///
/// Bloom publishes four raw numbers; the value is in the judgement made from them, and the judgement is one
/// comparison that is easy to write backwards. A diagnostic that announces starvation on a healthy pool, or
/// stays quiet on an exhausted one, is worse than no diagnostic at all — so the comparison is pinned here
/// rather than left to be read off a real report months later.
/// </summary>
[TestFixture]
public class EvidenceGathererServerPoolTests
{
    /// <summary>
    /// A snapshot carrying nothing but the server counts. Every other member is required, and none of them
    /// matters here, which is exactly why they are set once in one place.
    /// </summary>
    private static DoctorChannelSnapshot Pool(int workers, int busy, int blocked, int queued) =>
        new()
        {
            SchemaVersion = DoctorChannelLayout.SchemaVersion,
            PayloadBytes = DoctorChannelLayout.PayloadBytes,
            ProcessId = 1234,
            UiTicks = 1,
            WatchdogTicks = 1,
            UiHeartbeatAge = TimeSpan.Zero,
            WatchdogHeartbeatAge = TimeSpan.Zero,
            Activity = "",
            ShutdownPhase = BloomShutdownPhase.None,
            CleanExitRecorded = false,
            DebuggerAttached = false,
            DebuggerEverAttached = false,
            DebuggerLastDetachedAge = TimeSpan.Zero,
            LongOperationInProgress = false,
            ServerBusyWorkers = busy,
            ServerBlockedWorkers = blocked,
            ServerWorkers = workers,
            ServerQueuedRequests = queued,
        };

    [Test]
    public void DescribeServerWorkers_HealthyPool_NoStarvationWarning()
    {
        var text = EvidenceGatherer.DescribeServerWorkers(Pool(8, busy: 3, blocked: 1, queued: 0));

        Assert.That(
            text,
            Does.Contain("8 threads"),
            "the pool size is what makes the rest readable"
        );
        Assert.That(text, Does.Contain("3 busy"));
        Assert.That(text, Does.Contain("1 blocked"));
        Assert.That(
            text,
            Does.Not.Contain("every worker"),
            "one blocked of eight is ordinary traffic, and calling it starvation would cry wolf"
        );
    }

    [Test]
    public void DescribeServerWorkers_AllWorkersBlocked_SaysEveryWorkerBlocked()
    {
        // BloomServer's own rule for growing the pool: every live worker blocked. Nothing is queued, so
        // work is not yet held up.
        var text = EvidenceGatherer.DescribeServerWorkers(Pool(4, busy: 4, blocked: 4, queued: 0));

        Assert.That(text, Does.Contain("every worker is blocked"));
        Assert.That(
            text,
            Does.Not.Contain("waiting behind"),
            "with an empty queue nothing is actually held up, and saying so would overstate it"
        );
    }

    [Test]
    public void DescribeServerWorkers_AllBlockedWithQueue_SaysRequestsWaiting()
    {
        // The shape of a server-side deadlock rather than a slow operation: no worker can take the next
        // request, and requests are waiting.
        var text = EvidenceGatherer.DescribeServerWorkers(Pool(4, busy: 4, blocked: 4, queued: 6));

        Assert.That(text, Does.Contain("6 request(s) queued"));
        Assert.That(
            text,
            Does.Contain("every worker is blocked and requests are waiting behind them")
        );
    }

    [Test]
    public void DescribeServerWorkers_MoreBlockedThanWorkers_StillExhausted()
    {
        // The counts are read without a lock and from two different places, so they can disagree by one.
        // The comparison is >= rather than == precisely so a transient over-count does not make the
        // report go quiet at the moment it matters most.
        var text = EvidenceGatherer.DescribeServerWorkers(Pool(3, busy: 3, blocked: 4, queued: 1));

        Assert.That(text, Does.Contain("every worker is blocked"));
    }

    [Test]
    public void DescribeServerWorkers_EmptyPool_NoStarvationWarning()
    {
        // Before the server starts, or if Bloom is too old to publish these, the counts are all zero.
        // Zero blocked of zero satisfies >= arithmetically, and announcing starvation there would be a
        // false alarm on every such report.
        var text = EvidenceGatherer.DescribeServerWorkers(Pool(0, busy: 0, blocked: 0, queued: 0));

        Assert.That(text, Does.Not.Contain("every worker"), "no pool is not an exhausted pool");
    }
}
