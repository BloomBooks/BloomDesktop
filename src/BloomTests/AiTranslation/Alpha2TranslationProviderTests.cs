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
