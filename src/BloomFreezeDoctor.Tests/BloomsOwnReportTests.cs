using BloomFreezeDoctor;
using BloomFreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// How long Bloom's own problem report keeps the Doctor quiet.
///
/// Both directions cost something real, which is why the rule is pinned rather than left to whoever reads
/// the flag next. Too short and the Doctor files a duplicate of the card the user just raised. Too long -
/// "for the rest of the run", say - and a freeze hours later goes unreported because of a layout bug
/// somebody mentioned that morning.
/// </summary>
[TestFixture]
public class BloomsOwnReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 14, 0, 0, TimeSpan.Zero);

    private static DoctorSession Reported(DateTimeOffset? at) =>
        new()
        {
            ProcessId = 4242,
            BloomAlreadyReported = true,
            ReportedId = "BL-99999",
            ReportedAtUtc = at,
        };

    [Test]
    public void StillAccountsForTheTrouble_ReportMomentsAgo_True()
    {
        Assert.That(
            BloomsOwnReport.StillAccountsForTheTrouble(Reported(Now.AddSeconds(-30)), Now),
            Is.True,
            "the Doctor must not duplicate the card the user has just raised"
        );
    }

    [Test]
    public void StillAccountsForTheTrouble_ReportHoursAgo_False()
    {
        // A developer or alpha tester files something non-fatal and carries on working; the freeze that
        // afternoon is a different event and deserves its own card.
        Assert.That(
            BloomsOwnReport.StillAccountsForTheTrouble(Reported(Now.AddHours(-2)), Now),
            Is.False,
            "a session that has moved on must not still be silenced"
        );
    }

    [Test]
    public void StillAccountsForTheTrouble_EitherSideOfWindow_ChangesAtWindow()
    {
        var justInside = Now - BloomsOwnReport.Window + TimeSpan.FromSeconds(1);
        var justOutside = Now - BloomsOwnReport.Window - TimeSpan.FromSeconds(1);

        Assert.Multiple(() =>
        {
            Assert.That(
                BloomsOwnReport.StillAccountsForTheTrouble(Reported(justInside), Now),
                Is.True
            );
            Assert.That(
                BloomsOwnReport.StillAccountsForTheTrouble(Reported(justOutside), Now),
                Is.False
            );
        });
    }

    [Test]
    public void StillAccountsForTheTrouble_NeverReported_False()
    {
        var quiet = new DoctorSession { ProcessId = 4242 };

        Assert.That(BloomsOwnReport.StillAccountsForTheTrouble(quiet, Now), Is.False);
    }

    [Test]
    public void StillAccountsForTheTrouble_NoSession_False()
    {
        // A Bloom too old to leave a session file has told us nothing, which is not the same as telling us
        // it has the problem in hand.
        Assert.That(BloomsOwnReport.StillAccountsForTheTrouble(null, Now), Is.False);
    }

    [Test]
    public void StillAccountsForTheTrouble_ReportStampedInFuture_False()
    {
        // Clocks move backwards - a time-zone change, an NTP correction - and a naive subtraction would
        // leave such a report suppressing the Doctor until the skew had passed.
        Assert.That(
            BloomsOwnReport.StillAccountsForTheTrouble(Reported(Now.AddHours(3)), Now),
            Is.False,
            "a report three hours in the future is skew, not a fresh report"
        );
    }
}
