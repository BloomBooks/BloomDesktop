using System.Collections.Generic;
using Bloom;
using NUnit.Framework;

namespace BloomTests
{
    /// <summary>
    /// Tests for the log line <see cref="BloomAnalytics"/> writes for every event.
    ///
    /// The line is the whole point of the wrapper: DesktopAnalytics decides not to send anything in
    /// a build configured with allowTracking:false, and it decides that inside its own Track
    /// method, so without this line a developer exercising a new event sees nothing whatsoever and
    /// cannot tell "it fired but was not sent" from "it never fired". These tests pin the two
    /// things a reader depends on -- that the line says which of those it is, and that the same
    /// event always reads the same way.
    /// </summary>
    [TestFixture]
    public class BloomAnalyticsTests
    {
        private static Dictionary<string, string> SomeProperties =>
            new Dictionary<string, string>
            {
                // Deliberately not in alphabetical order, so the sorting below is doing something.
                { "provider", "pixabay" },
                { "term", "dog" },
                { "BookId", "abc123" },
            };

        [Test]
        public void FormatForLog_TrackingOff_SaysTheEventIsNotBeingSent()
        {
            var line = BloomAnalytics.FormatForLog(
                "Image Search",
                SomeProperties,
                trackingAllowed: false
            );

            Assert.That(line, Does.Contain("NOT SENT"));
            // Still says what the event WAS: "nothing was sent" is only useful next to what it was
            // that went unsent.
            Assert.That(line, Does.Contain("Image Search"));
            Assert.That(line, Does.Contain("term=dog"));
        }

        [Test]
        public void FormatForLog_TrackingOn_DoesNotClaimTheEventWasDropped()
        {
            var line = BloomAnalytics.FormatForLog(
                "Image Search",
                SomeProperties,
                trackingAllowed: true
            );

            Assert.That(line, Does.StartWith("[analytics]"));
            Assert.That(line, Does.Not.Contain("NOT SENT"));
        }

        [Test]
        public void FormatForLog_Properties_AreOrderedByNameWhateverOrderTheyArrivedIn()
        {
            var line = BloomAnalytics.FormatForLog(
                "Image Search",
                SomeProperties,
                trackingAllowed: true
            );

            Assert.That(
                line,
                Is.EqualTo("[analytics] Image Search -- BookId=abc123, provider=pixabay, term=dog")
            );
        }

        [Test]
        public void FormatForLog_NoProperties_IsJustTheEventName()
        {
            Assert.That(
                BloomAnalytics.FormatForLog("Delete Page", null, trackingAllowed: true),
                Is.EqualTo("[analytics] Delete Page")
            );
            // An empty dictionary should read the same as none at all, rather than leaving a
            // trailing separator with nothing after it.
            Assert.That(
                BloomAnalytics.FormatForLog(
                    "Delete Page",
                    new Dictionary<string, string>(),
                    trackingAllowed: true
                ),
                Is.EqualTo("[analytics] Delete Page")
            );
        }

        [Test]
        public void FormatForLog_APropertyValueContainingABrace_IsLeftAlone()
        {
            // Search terms are user input and reach us verbatim. This is safe only because Log
            // passes the line to Logger.WriteEvent as the message and never as a format string
            // with arguments -- see the remarks on FormatForLog.
            var line = BloomAnalytics.FormatForLog(
                "Image Search",
                new Dictionary<string, string> { { "term", "a{0}b" } },
                trackingAllowed: true
            );

            Assert.That(line, Does.Contain("term=a{0}b"));
        }
    }
}
