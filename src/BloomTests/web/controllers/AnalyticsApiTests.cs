using System.Globalization;
using System.Threading;
using Bloom.web.controllers;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for the one piece of real logic in <see cref="AnalyticsApi"/>: turning the JSON
    /// properties a front-end caller sends into the string dictionary DesktopAnalytics wants.
    /// HandleTrack itself is not tested here because it ends in a call to the static
    /// DesktopAnalytics.Analytics, which a unit test cannot observe.
    ///
    /// The behaviors that matter, and why:
    /// - booleans arrive as "true"/"false", not .NET's "True"/"False", so a chart grouping on
    ///   them matches what the same property looks like coming from our TypeScript.
    /// - numbers are formatted invariantly, so a user in a comma-decimal locale does not send
    ///   us "1,5" and quietly poison the numeric columns.
    /// - a null or absent value is dropped rather than sent as "", so a caller can pass an
    ///   optional property without deciding whether to include the key.
    /// </summary>
    [TestFixture]
    public class AnalyticsApiTests
    {
        [Test]
        public void GetProperties_Booleans_UseJsonSpellingNotDotNets()
        {
            var result = AnalyticsApi.GetProperties(
                JObject.Parse("{\"cleared\": true, \"demoOnly\": false}")
            );

            Assert.That(result["cleared"], Is.EqualTo("true"));
            Assert.That(result["demoOnly"], Is.EqualTo("false"));
        }

        [Test]
        public void GetProperties_Numbers_AreFormattedInvariantlyWhateverTheLocale()
        {
            var originalCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                // German uses a comma as the decimal separator, so a locale-sensitive
                // conversion would produce "1,5" here.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                // Sanity check that this culture really would have changed the answer;
                // otherwise the assertion below would pass for the wrong reason.
                Assert.That(
                    1.5.ToString(),
                    Is.EqualTo("1,5"),
                    "setup: expected the de-DE culture to be in effect"
                );

                var result = AnalyticsApi.GetProperties(
                    JObject.Parse("{\"elapsedSeconds\": 1.5, \"searchIndex\": 3}")
                );

                Assert.That(result["elapsedSeconds"], Is.EqualTo("1.5"));
                Assert.That(result["searchIndex"], Is.EqualTo("3"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        [Test]
        public void GetProperties_NullValues_AreDroppedRatherThanSentAsEmpty()
        {
            var result = AnalyticsApi.GetProperties(
                JObject.Parse("{\"term\": \"dog\", \"acceptedTerm\": null}")
            );

            Assert.That(
                result.ContainsKey("term"),
                Is.True,
                "setup: the real value should survive"
            );
            Assert.That(result.ContainsKey("acceptedTerm"), Is.False);
        }

        [Test]
        public void GetProperties_Strings_ComeThroughUnquoted()
        {
            var result = AnalyticsApi.GetProperties(JObject.Parse("{\"outcome\": \"local disk\"}"));

            // A JSON-serializing conversion would have given us "\"local disk\"".
            Assert.That(result["outcome"], Is.EqualTo("local disk"));
        }

        [Test]
        public void GetProperties_NoProperties_GivesAnEmptyDictionary()
        {
            Assert.That(AnalyticsApi.GetProperties(null), Is.Empty);
        }

        // The next two go through ParseTrackRequestBody rather than JObject.Parse, because the
        // coercion these guard against happens at parse time, not in GetProperties.

        private static JObject ParsedProperties(string propertiesJson)
        {
            return AnalyticsApi.ParseTrackRequestBody("{\"properties\": " + propertiesJson + "}")[
                    "properties"
                ] as JObject;
        }

        [Test]
        public void GetProperties_AValueThatParsesAsADateTime_ComesThroughExactlyAsSent()
        {
            // Newtonsoft's default turns a JSON string like this into a DateTime, and we would then
            // record whatever the local culture makes of it ("1/1/2024 10:00:00 AM" here). Several of
            // these properties are free user text -- an image search term above all -- so the record
            // has to say what was sent. A date-ONLY string is left alone by Newtonsoft, which is why
            // this test uses a full timestamp: that is the reachable case.
            var result = AnalyticsApi.GetProperties(
                ParsedProperties("{\"term\": \"2024-01-01T10:00:00Z\"}")
            );

            Assert.AreEqual("2024-01-01T10:00:00Z", result["term"]);
        }

        [Test]
        public void GetProperties_AValueThatParsesAsADateTime_IsNotLocaleFormattedEither()
        {
            var wasCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                // Sanity check: this culture formats date-times its own way, so a coerced value
                // would be visibly wrong here rather than coincidentally right.
                Assert.AreNotEqual(
                    "1/1/2024 10:00:00 AM",
                    new System.DateTime(2024, 1, 1, 10, 0, 0).ToString(),
                    "test setup: de-DE should not format date-times the way en-US does"
                );

                var result = AnalyticsApi.GetProperties(
                    ParsedProperties("{\"term\": \"2024-01-01T10:00:00Z\"}")
                );

                Assert.AreEqual("2024-01-01T10:00:00Z", result["term"]);
            }
            finally
            {
                CultureInfo.CurrentCulture = wasCulture;
            }
        }
    }
}
