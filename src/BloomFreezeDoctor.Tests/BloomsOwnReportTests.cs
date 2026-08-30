using BloomFreezeDoctor;
using BloomFreezeDoctor.Protocol;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// How long Bloom's own problem report keeps the Doctor quiet.
///
/// Both directions cost something real, which is why the rule is pinned rather than left to whoever reads
/// the flag next. Too short and the Doctor files a duplicate of the card the user just raised. Too long -
/// which is what "for the rest of the run" turned out to be - and a freeze hours later goes unreported
/// because of a layout bug somebody mentioned that morning.
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
    public void A_report_from_moments_ago_still_speaks_for_the_trouble()
    {
        Assert.That(
            BloomsOwnReport.StillAccountsForTheTrouble(Reported(Now.AddSeconds(-30)), Now),
            Is.True,
            "the Doctor must not duplicate the card the user has just raised"
        );
    }

    [Test]
    public void A_report_from_hours_ago_does_not()
    {
        // The case that prompted this. A developer or alpha tester files something non-fatal and carries on
        // working; the freeze that afternoon is a different event and deserves its own card.
        Assert.That(
            BloomsOwnReport.StillAccountsForTheTrouble(Reported(Now.AddHours(-2)), Now),
            Is.False,
            "a session that has moved on must not still be silenced"
        );
    }

    [Test]
    public void The_window_is_where_the_change_happens()
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
    public void A_Bloom_that_never_reported_anything_does_not_silence_us()
    {
        var quiet = new DoctorSession { ProcessId = 4242 };

        Assert.That(BloomsOwnReport.StillAccountsForTheTrouble(quiet, Now), Is.False);
    }

    [Test]
    public void No_session_at_all_does_not_silence_us()
    {
        // A Bloom too old to leave a session file has told us nothing, which is not the same as telling us
        // it has the problem in hand.
        Assert.That(BloomsOwnReport.StillAccountsForTheTrouble(null, Now), Is.False);
    }

    [Test]
    public void A_report_with_no_time_keeps_the_old_all_run_suppression()
    {
        // Only a Bloom built before the timestamp existed. It cannot tell us when it reported, and
        // inventing a time would be guessing, so it gets exactly the behaviour it had before this change.
        Assert.That(
            BloomsOwnReport.StillAccountsForTheTrouble(Reported(null), Now),
            Is.True,
            "silence for the run is what such a Bloom has always had"
        );
    }

    [Test]
    public void A_report_stamped_in_the_future_ages_out_like_any_other()
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
