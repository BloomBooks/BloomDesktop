using BloomFreezeDoctor.Gathering;
using NUnit.Framework;

namespace BloomFreezeDoctor.Tests;

/// <summary>
/// Pulling the cause of a crash out of what Bloom and Windows recorded, so it can be said at the top of the
/// report instead of buried hundreds of lines down in the evidence.
///
/// Both samples are real: one from the log of a simulated unhandled exception, one from the Application
/// event log entry for a simulated FailFast.
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

    /// <summary>A real .NET Runtime event for a FailFast, copied from the Application log.</summary>
    private const string RealFailFastEvent = """
        Application: Bloom.exe
        CoreCLR Version: 8.0.3026.36720
        Description: The application requested process termination through System.Environment.FailFast.
        Message: FreezeSimulator was asked to fail fast
        Stack:
           at System.Environment.FailFast(System.String)
        """;

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

    [Test]
    public void It_finds_the_reason_a_FailFast_gave()
    {
        // FailFast is the only crash with no dump and nothing in Bloom's log - it runs no managed handlers
        // at all, by design - so this event text is the sole record of why the process was killed. Leaving
        // it out of the headlines made failfast the one crash kind whose report never said what went wrong.
        Assert.That(
            BloomsOwnException.FindFailFastReason(new[] { RealFailFastEvent }),
            Is.EqualTo("FreezeSimulator was asked to fail fast")
        );
    }

    [Test]
    public void An_ordinary_crash_event_is_not_read_as_a_FailFast()
    {
        var unhandled = """
            Application: Bloom.exe
            Description: The process was terminated due to an unhandled exception.
            Exception Info: System.ApplicationException: something else
            """;

        Assert.That(BloomsOwnException.FindFailFastReason(new[] { unhandled }), Is.Null);
    }

    [Test]
    public void A_FailFast_with_no_message_names_none()
    {
        // Environment.FailFast can be called with no message at all, and announcing an empty reason reads
        // as the tool being broken.
        var noMessage = """
            Description: The application requested process termination through System.Environment.FailFast.
            Message:
            """;

        Assert.That(BloomsOwnException.FindFailFastReason(new[] { noMessage }), Is.Null);
    }
}
