using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Pulling the thrown exception out of Bloom's log, so it can be said at the top of the report instead of
/// buried 370 lines down in a log tail.
///
/// The sample is real, copied from the log of a simulated unhandled exception.
/// </summary>
[TestFixture]
public class BloomsOwnExceptionTests
{
    private static readonly string[] RealLogTail =
    {
        "10:56:41 PM\t*** FreezeSimulator: simulating 'throw' NOW",
        "10:56:41 PM\t[analytics NOT SENT: tracking is off in this build] (exception) ApplicationException",
        "10:56:41 PM\tAsking the Bloom Freeze Doctor for a dump of this crash",
        "10:56:44 PM\tThe Bloom Freeze Doctor captured a dump of this crash",
        "10:56:44 PM\t*** ProblemReportApi is about to report:",
        "    exception = System.ApplicationException: FreezeSimulator was asked to throw",
        "   at Bloom.FreezeDoctor.FreezeSimulator.Simulate(String kind) in C:\\github\\...:line 233",
    };

    [Test]
    public void It_finds_what_Bloom_recorded()
    {
        Assert.That(
            BloomsOwnException.FindIn(RealLogTail),
            Is.EqualTo("System.ApplicationException: FreezeSimulator was asked to throw")
        );
    }

    [Test]
    public void It_stops_at_the_exception_and_does_not_swallow_the_stack()
    {
        // The stack follows on the next lines and is shown properly elsewhere in the report. A headline
        // carrying it would be unreadable.
        Assert.That(BloomsOwnException.FindIn(RealLogTail), Does.Not.Contain("   at "));
    }

    [Test]
    public void The_last_one_wins()
    {
        // A session can log several. The one nearest the trouble being reported is the one that matters,
        // and the log tail is in time order.
        var twice = new[]
        {
            "    exception = System.IO.IOException: an earlier and unrelated problem",
            "    exception = System.NullReferenceException: the one we are reporting",
        };

        Assert.That(
            BloomsOwnException.FindIn(twice),
            Is.EqualTo("System.NullReferenceException: the one we are reporting")
        );
    }

    [Test]
    public void A_log_with_no_exception_yields_nothing()
    {
        Assert.That(
            BloomsOwnException.FindIn(new[] { "10:56:27 PM\tBookStorage Loading Dom from ..." }),
            Is.Null
        );
        Assert.That(BloomsOwnException.Headline(Array.Empty<string>()), Is.Null);
    }

    [Test]
    public void The_marker_with_nothing_after_it_is_not_an_exception()
    {
        // Otherwise the report announces that Bloom recorded an exception and then names none, which reads
        // as the tool being broken.
        Assert.That(BloomsOwnException.FindIn(new[] { "    exception = " }), Is.Null);
    }

    [Test]
    public void A_very_long_message_is_cut_for_the_headline()
    {
        // Some carry a whole file path or a book's entire name. The full text is in the log below.
        var huge = "    exception = System.Exception: " + new string('x', 500);

        var headline = BloomsOwnException.Headline(new[] { huge });

        Assert.That(headline, Does.Contain("System.Exception"));
        Assert.That(headline!.Length, Is.LessThan(300));
        Assert.That(headline, Does.EndWith("..."));
    }
}
