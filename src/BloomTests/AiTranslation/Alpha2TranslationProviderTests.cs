using System;
using System.Linq;
using System.Threading;
using Bloom.AiTranslation;
using Bloom.Collection;
using NUnit.Framework;

namespace BloomTests.AiTranslation
{
    [TestFixture]
    public class Alpha2TranslationProviderTests
    {
        [Test]
        public void MapToIso6393_TwoLetterEnglish_ReturnsEng()
        {
            Assert.That(Alpha2TranslationProvider.MapToIso6393("en"), Is.EqualTo("eng"));
        }

        [Test]
        public void MapToIso6393_TwoLetterFrench_ReturnsFra()
        {
            Assert.That(Alpha2TranslationProvider.MapToIso6393("fr"), Is.EqualTo("fra"));
        }

        [Test]
        public void MapToIso6393_AlreadyThreeLetterTag_PassesThrough()
        {
            Assert.That(Alpha2TranslationProvider.MapToIso6393("eng"), Is.EqualTo("eng"));
        }

        [Test]
        public void MapToIso6393_RegionQualifiedTag_MapsPrimarySubtag()
        {
            Assert.That(Alpha2TranslationProvider.MapToIso6393("es-ES"), Is.EqualTo("spa"));
        }

        [Test]
        public void MapToIso6393_UnmappableTag_Throws()
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                Alpha2TranslationProvider.MapToIso6393("zzzzz")
            );

            Assert.That(exception.Message, Does.Contain("zzzzz"));
        }

        [Test]
        public void IsTranslationComplete_CompleteStatus_ReturnsTrue()
        {
            Assert.That(Alpha2TranslationProvider.IsTranslationComplete("complete"), Is.True);
        }

        [Test]
        public void IsTranslationComplete_IsCaseInsensitive()
        {
            Assert.That(Alpha2TranslationProvider.IsTranslationComplete("COMPLETE"), Is.True);
        }

        // The live Alpha2 API reports a queued segment as "pending"; this must be treated as
        // still-in-progress, not a failure (regression guard for the "unexpected status 'pending'"
        // bug that aborted otherwise-successful translations).
        [TestCase("pending")]
        [TestCase("running")]
        [TestCase("")]
        [TestCase(null)]
        public void IsTranslationComplete_InProgressStatus_ReturnsFalse(string status)
        {
            Assert.That(Alpha2TranslationProvider.IsTranslationComplete(status), Is.False);
        }

        [Test]
        public void IsTranslationComplete_UnrecognizedStatus_Throws()
        {
            // Sanity check: a status we've never seen is a hard failure, not silently ignored.
            var exception = Assert.Throws<InvalidOperationException>(() =>
                Alpha2TranslationProvider.IsTranslationComplete("exploded")
            );

            Assert.That(exception.Message, Does.Contain("exploded"));
        }

        [Test]
        public void MapIso6393ToBloomTag_MapsToShortestRegisteredTag()
        {
            // Alpha2's 3-letter codes should collapse to the familiar 2-letter tags so they dedup
            // with DeepL/Google's "fr"/"en" in the settings union.
            Assert.That(Alpha2TranslationProvider.MapIso6393ToBloomTag("fra"), Is.EqualTo("fr"));
            Assert.That(Alpha2TranslationProvider.MapIso6393ToBloomTag("eng"), Is.EqualTo("en"));
        }

        [Test]
        public void MapIso6393ToBloomTag_UnmappedCodePassesThrough()
        {
            // A code with no shorter registered tag is returned unchanged (and lowercased).
            Assert.That(Alpha2TranslationProvider.MapIso6393ToBloomTag("zzz"), Is.EqualTo("zzz"));
            Assert.That(Alpha2TranslationProvider.MapIso6393ToBloomTag("FRA"), Is.EqualTo("fr"));
        }

        [Test]
        public void ParseTranslationLanguages_ReverseMapsSortsByLabelAndTagsAlpha2()
        {
            var json =
                @"[
                    {""iso"":""fra"",""name"":""French"",""display"":""Français"",""models"":[{""id"":1,""name"":""m1""}]},
                    {""iso"":""spa"",""name"":""Spanish"",""models"":[]},
                    {""iso"":""zzz"",""name"":""Madeup""}
                ]";

            var options = Alpha2TranslationProvider.ParseTranslationLanguages(json);

            // Sorted by label: French, Madeup, Spanish.
            Assert.That(
                options.Select(o => o.Label),
                Is.EqualTo(new[] { "French", "Madeup", "Spanish" })
            );
            var valueByLabel = options.ToDictionary(o => o.Label, o => o.Value);
            Assert.That(valueByLabel["French"], Is.EqualTo("fr"));
            Assert.That(valueByLabel["Spanish"], Is.EqualTo("es"));
            Assert.That(
                valueByLabel["Madeup"],
                Is.EqualTo("zzz"),
                "an unmapped ISO code should pass through unchanged"
            );
            Assert.That(
                options.All(o => o.ProviderIds.SequenceEqual(new[] { "alpha2" })),
                Is.True,
                "every option must be tagged as coming from alpha2"
            );
        }

        [Test]
        public void ParseTranslationLanguages_DedupsEntriesThatMapToTheSameTag()
        {
            var json =
                @"[
                    {""iso"":""eng"",""name"":""English""},
                    {""iso"":""eng"",""name"":""English (again)""}
                ]";

            var options = Alpha2TranslationProvider.ParseTranslationLanguages(json);

            Assert.That(options.Count, Is.EqualTo(1));
            Assert.That(options[0].Value, Is.EqualTo("en"));
        }

        [Test]
        public void ParseTranslationLanguages_SkipsEntriesWithNoIso()
        {
            var json =
                @"[
                    {""name"":""No iso here""},
                    {""iso"":""fra"",""name"":""French""}
                ]";

            var options = Alpha2TranslationProvider.ParseTranslationLanguages(json);

            Assert.That(options.Count, Is.EqualTo(1));
            Assert.That(options[0].Value, Is.EqualTo("fr"));
        }
    }

    /// <summary>
    /// Live round trip against the real Alpha2 service. Gated on BLOOM_ALPHA2_KEY, following the
    /// same pattern as the DeepL/Google live tests in AiTranslationServiceTests.cs.
    /// </summary>
    [TestFixture]
    [Category("SkipOnTeamCity")]
    [NonParallelizable]
    public class Alpha2LiveTranslationTests
    {
        [Test]
        public async System.Threading.Tasks.Task TranslateSegmentsAsync_ThreeDistinctSegments_ReturnsTranslationsInOrder()
        {
            var apiKey = Environment.GetEnvironmentVariable("BLOOM_ALPHA2_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Assert.Ignore(
                    "Manual AI translation provider test. Set BLOOM_ALPHA2_KEY to run it."
                );
            }

            var collectionSettings = new CollectionSettings
            {
                Subscription =
                    Bloom.SubscriptionAndFeatures.Subscription.CreateTempSubscriptionForTier(
                        Bloom.SubscriptionAndFeatures.SubscriptionTier.Pro
                    ),
                AiTranslationTargetLanguageTag = "es",
            };
            var service = new AiTranslationService(collectionSettings);
            var engine = new AiTranslationEngineSettings { ProviderId = "alpha2", ApiKey = apiKey };

            // Distinct number words per segment so we can verify order is preserved, not just
            // that translation happened.
            var segments = new[] { "I have one cat.", "I have two dogs.", "I have three birds." };

            var results = await service.TranslateSegmentsAsync(
                engine,
                segments,
                "en",
                CancellationToken.None
            );

            Assert.That(results.Length, Is.EqualTo(segments.Length));
            for (var i = 0; i < segments.Length; i++)
            {
                Assert.That(results[i], Is.Not.Null.And.Not.Empty);
                Assert.That(results[i], Is.Not.EqualTo(segments[i]));
            }

            // Spanish number words for one/two/three, verifying each translation landed in the
            // slot matching its source segment (not merged/reordered).
            Assert.That(results[0].ToLowerInvariant(), Does.Contain("uno").Or.Contain("un "));
            Assert.That(results[1].ToLowerInvariant(), Does.Contain("dos"));
            Assert.That(results[2].ToLowerInvariant(), Does.Contain("tres"));
        }
    }
}
