using BloomFreezeDoctor;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// The case the tool has to handle well: somebody starts the Doctor **because** Bloom is already
/// frozen. Waiting out a fresh threshold before telling them what they already know would be a poor
/// showing, and Bloom's published heartbeat means we do not have to.
/// </summary>
[TestFixture]
public class FreezeDetectorAlreadyFrozenTests
{
    private static TargetObservation FrozenForAges(double atSeconds, double alreadyFrozenSeconds) =>
        new()
        {
            Uptime = TimeSpan.FromSeconds(atSeconds),
            IsAlive = true,
            // The signature of the freeze that cannot be seen from outside: the window still answers.
            WindowResponds = true,
            HasVisibleWindow = true,
            HeartbeatIsStale = true,
            UiBlockCorroborated = true,
            AlreadyUnresponsiveFor = TimeSpan.FromSeconds(alreadyFrozenSeconds),
        };

    [Test]
    public void Observe_AlreadyFrozenPastThreshold_ReportsImmediately()
    {
        var detector = new FreezeDetector();

        // The very first look, at a Bloom whose UI thread stopped six minutes ago.
        var verdict = detector.Observe(FrozenForAges(atSeconds: 0, alreadyFrozenSeconds: 360));

        Assert.That(verdict.State, Is.EqualTo(TargetState.Frozen));
        Assert.That(
            verdict.Report,
            Is.EqualTo(ReportReason.Frozen),
            "a Doctor started because Bloom is frozen must not make the user wait another minute"
        );
        Assert.That(
            verdict.Explanation,
            Does.Contain("heartbeat"),
            "and it should say which signal caught it, since the window looked fine"
        );
    }

    [Test]
    public void Observe_AlreadyUnresponsiveBelowThreshold_NoReport()
    {
        // Backdating must not become "report anything that twitches": ten seconds of staleness is not a
        // freeze, and treating it as one would bury us in noise from ordinary slow moments.
        var detector = new FreezeDetector();

        var verdict = detector.Observe(FrozenForAges(atSeconds: 0, alreadyFrozenSeconds: 10));

        Assert.That(verdict.ShouldReport, Is.False);
        Assert.That(verdict.State, Is.EqualTo(TargetState.Healthy));
    }

    [Test]
    public void Observe_NoPublishedHeartbeat_ClockStartsAtFirstLook()
    {
        // Watching from outside has no way to know how long a Bloom has been frozen, so it must not
        // pretend: it starts counting from its first look. This is one of the concrete reasons Bloom
        // publishes a heartbeat.
        var detector = new FreezeDetector();

        var first = detector.Observe(
            new TargetObservation
            {
                Uptime = TimeSpan.Zero,
                IsAlive = true,
                WindowResponds = false,
                HasVisibleWindow = true,
                // No AlreadyUnresponsiveFor: nothing published, nothing knowable.
            }
        );

        Assert.That(first.ShouldReport, Is.False, "we cannot know, so we cannot claim");
        Assert.That(first.State, Is.EqualTo(TargetState.Healthy));
    }
}
