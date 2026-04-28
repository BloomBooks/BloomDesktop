using System;
using System.Linq;
using System.Threading.Tasks;
using Bloom;
using Bloom.AiSourceBubbles;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;
using NUnit.Framework;

namespace BloomTests.AiSourceBubbles
{
    [TestFixture]
    public class AiSourceBubblesServiceTests
    {
        private bool _previousAiSourceBubblesEnabled;

        [SetUp]
        public void Setup()
        {
            _previousAiSourceBubblesEnabled = ExperimentalFeatures.IsFeatureEnabled(
                ExperimentalFeatures.kAiSourceBubbles
            );
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kAiSourceBubbles, true);
        }

        [TearDown]
        public void TearDown()
        {
            ExperimentalFeatures.SetValue(
                ExperimentalFeatures.kAiSourceBubbles,
                _previousAiSourceBubblesEnabled
            );
        }

        [Test]
        public void NormalizeProviderId_GoogleTranslateAlias_ReturnsGoogle()
        {
            Assert.That(
                AiSourceBubblesService.NormalizeProviderId("googleTranslate"),
                Is.EqualTo("google")
            );
        }

        [Test]
        public void GetAiLanguageTag_AppendsNormalizedProviderSuffix()
        {
            Assert.That(
                AiSourceBubblesService.GetAiLanguageTag("fr", "googleTranslate"),
                Is.EqualTo("fr-x-ai-google")
            );
        }

        [Test]
        public void TranslateAsync_WithoutTargetLanguageTag_ThrowsHelpfulError()
        {
            var collectionSettings = MakeCollectionSettings("deepl");
            collectionSettings.AiSourceBubblesTargetLanguageTag = "";
            var service = new AiSourceBubblesService(collectionSettings);

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.TranslateAsync(
                    new AiSourceBubblesTranslateRequest
                    {
                        SourceText = "Hello world.",
                        SourceLanguageTag = "en",
                    }
                )
            );

            Assert.That(exception.Message, Does.Contain("target language tag"));
        }

        private static CollectionSettings MakeCollectionSettings(string providerId)
        {
            var collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiSourceBubblesProviderId = providerId,
                AiSourceBubblesTargetLanguageTag = "fr",
            };
            return collectionSettings;
        }
    }

    public abstract class AiSourceBubblesLiveTranslationTestsBase
    {
        private bool _previousAiSourceBubblesEnabled;

        protected abstract string ProviderId { get; }
        protected abstract string[] RequiredEnvironmentVariables { get; }
        protected abstract void PopulateCredentials(CollectionSettings collectionSettings);

        [SetUp]
        public void Setup()
        {
            _previousAiSourceBubblesEnabled = ExperimentalFeatures.IsFeatureEnabled(
                ExperimentalFeatures.kAiSourceBubbles
            );
            ExperimentalFeatures.SetValue(ExperimentalFeatures.kAiSourceBubbles, true);
        }

        [TearDown]
        public void TearDown()
        {
            ExperimentalFeatures.SetValue(
                ExperimentalFeatures.kAiSourceBubbles,
                _previousAiSourceBubblesEnabled
            );
        }

        [Test]
        public async Task TranslateAsync_ConfiguredProvider_ReturnsTranslatedText()
        {
            var missingVariables = RequiredEnvironmentVariables
                .Where(variableName =>
                    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName))
                )
                .ToArray();
            if (missingVariables.Any())
            {
                Assert.Ignore(
                    $"Manual AI Source Bubbles provider test. Set {string.Join(", ", missingVariables)} to run it."
                );
            }

            var collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiSourceBubblesProviderId = ProviderId,
                AiSourceBubblesTargetLanguageTag = "fr",
            };
            PopulateCredentials(collectionSettings);
            var service = new AiSourceBubblesService(collectionSettings);

            var result = await service.TranslateAsync(
                new AiSourceBubblesTranslateRequest
                {
                    SourceText = "Hello world.",
                    SourceLanguageTag = "en",
                }
            );

            Assert.That(result.ProviderId, Is.EqualTo(ProviderId));
            Assert.That(result.TargetLanguageTag, Is.EqualTo("fr"));
            Assert.That(result.AiLanguageTag, Is.EqualTo($"fr-x-ai-{ProviderId}"));
            Assert.That(result.Text, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Text, Is.Not.EqualTo("Hello world."));
        }
    }

    [TestFixture]
    [Category("SkipOnTeamCity")]
    [NonParallelizable]
    public class DeepLAiSourceBubblesLiveTranslationTests : AiSourceBubblesLiveTranslationTestsBase
    {
        protected override string ProviderId => "deepl";

        protected override string[] RequiredEnvironmentVariables => new[] { "BLOOM_DEEPL_KEY" };

        protected override void PopulateCredentials(CollectionSettings collectionSettings)
        {
            collectionSettings.AiSourceBubblesDeepLApiKey = Environment.GetEnvironmentVariable(
                "BLOOM_DEEPL_KEY"
            );
        }
    }

    [TestFixture]
    [Category("SkipOnTeamCity")]
    [NonParallelizable]
    public class GoogleAiSourceBubblesLiveTranslationTests : AiSourceBubblesLiveTranslationTestsBase
    {
        protected override string ProviderId => "google";

        protected override string[] RequiredEnvironmentVariables =>
            new[]
            {
                "BLOOM_GOOGLE_TRANSLATION_SERVICE_ACCOUNT_EMAIL",
                "BLOOM_GOOGLE_TRANSLATION_SERVICE_PRIVATE_KEY",
            };

        protected override void PopulateCredentials(CollectionSettings collectionSettings)
        {
            collectionSettings.AiSourceBubblesGoogleServiceAccountEmail =
                Environment.GetEnvironmentVariable(
                    "BLOOM_GOOGLE_TRANSLATION_SERVICE_ACCOUNT_EMAIL"
                );
            collectionSettings.AiSourceBubblesGooglePrivateKey = Environment.GetEnvironmentVariable(
                "BLOOM_GOOGLE_TRANSLATION_SERVICE_PRIVATE_KEY"
            );
        }
    }
}
