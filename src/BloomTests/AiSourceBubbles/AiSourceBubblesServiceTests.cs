using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
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
        public void GetGoogleProjectIdFromServiceAccountEmail_ParsesProjectId()
        {
            Assert.That(
                AiSourceBubblesService.GetGoogleProjectIdFromServiceAccountEmail(
                    "translator@test-project-123.iam.gserviceaccount.com"
                ),
                Is.EqualTo("test-project-123")
            );
        }

        [Test]
        public void GetConfigurationFingerprint_ChangesWhenRelevantSettingsChange()
        {
            var settings = MakeCollectionSettings("deepl");
            settings.AiSourceBubblesDeepLApiKey = "first-key";

            var originalFingerprint = AiSourceBubblesService.GetConfigurationFingerprint(settings);

            settings.AiSourceBubblesDeepLApiKey = "second-key";

            Assert.That(
                AiSourceBubblesService.GetConfigurationFingerprint(settings),
                Is.Not.EqualTo(originalFingerprint)
            );
        }

        [Test]
        public void GetConfigurationFingerprint_IgnoresUnusedProviderCredentials()
        {
            var settings = MakeCollectionSettings("deepl");
            settings.AiSourceBubblesDeepLApiKey = "deepl-key";
            settings.AiSourceBubblesGoogleServiceAccountEmail = "first@example.com";
            settings.AiSourceBubblesGooglePrivateKey = "first-private-key";

            var originalFingerprint = AiSourceBubblesService.GetConfigurationFingerprint(settings);

            settings.AiSourceBubblesGoogleServiceAccountEmail = "second@example.com";
            settings.AiSourceBubblesGooglePrivateKey = "second-private-key";

            Assert.That(
                AiSourceBubblesService.GetConfigurationFingerprint(settings),
                Is.EqualTo(originalFingerprint)
            );
        }

        [Test]
        public void NormalizeProviderId_Alpha2Alias_ReturnsEmpty()
        {
            Assert.That(
                AiSourceBubblesService.NormalizeProviderId("alpha2"),
                Is.EqualTo(string.Empty)
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

        [Test]
        public async Task TranslateAsync_WritesRequestAndResponseToConsole()
        {
            var collectionSettings = MakeCollectionSettings("deepl");
            var fakeProvider = new FakeAiSourceBubblesTranslationProvider(
                "deepl",
                "Bonjour le monde."
            );
            var service = new AiSourceBubblesService(
                collectionSettings,
                new Dictionary<string, IAiSourceBubblesTranslationProvider>
                {
                    { "deepl", fakeProvider },
                }
            );
            var originalConsoleOut = Console.Out;
            using (var output = new StringWriter())
            {
                Console.SetOut(output);

                try
                {
                    var result = await service.TranslateAsync(
                        new AiSourceBubblesTranslateRequest
                        {
                            SourceText = "Hello world.",
                            SourceLanguageTag = "en",
                        }
                    );

                    Assert.That(result.Text, Is.EqualTo("Bonjour le monde."));
                }
                finally
                {
                    Console.SetOut(originalConsoleOut);
                }

                var log = output.ToString();
                Assert.That(log, Does.Contain("[AiSourceBubbles][request]"));
                Assert.That(log, Does.Contain("[AiSourceBubbles][response]"));
                Assert.That(log, Does.Contain("provider=deepl"));
                Assert.That(log, Does.Contain("sourceLanguage=en"));
                Assert.That(log, Does.Contain("targetLanguage=fr"));
                Assert.That(log, Does.Contain("input=\"Hello world.\""));
                Assert.That(log, Does.Contain("output=\"Bonjour le monde.\""));
                Assert.That(log, Does.Contain("elapsedMs="));
                Assert.That(log, Does.Contain("time="));
            }
        }

        [Test]
        public void TranslateAsync_WhenProviderThrows_WritesFailureToConsole()
        {
            var collectionSettings = MakeCollectionSettings("deepl");
            var fakeProvider = new FakeAiSourceBubblesTranslationProvider(
                "deepl",
                exceptionToThrow: new InvalidOperationException("boom")
            );
            var service = new AiSourceBubblesService(
                collectionSettings,
                new Dictionary<string, IAiSourceBubblesTranslationProvider>
                {
                    { "deepl", fakeProvider },
                }
            );
            var originalConsoleOut = Console.Out;
            using (var output = new StringWriter())
            {
                Console.SetOut(output);

                try
                {
                    var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                        await service.TranslateAsync(
                            new AiSourceBubblesTranslateRequest
                            {
                                SourceText = "Hello world.",
                                SourceLanguageTag = "en",
                            }
                        )
                    );

                    Assert.That(exception.Message, Is.EqualTo("boom"));
                }
                finally
                {
                    Console.SetOut(originalConsoleOut);
                }

                var log = output.ToString();
                Assert.That(log, Does.Contain("[AiSourceBubbles][request]"));
                Assert.That(log, Does.Contain("[AiSourceBubbles][response]"));
                Assert.That(log, Does.Contain("error=\"boom\""));
                Assert.That(log, Does.Contain("input=\"Hello world.\""));
                Assert.That(log, Does.Contain("elapsedMs="));
                Assert.That(log, Does.Contain("time="));
            }
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

        private sealed class FakeAiSourceBubblesTranslationProvider
            : IAiSourceBubblesTranslationProvider
        {
            private readonly string _translatedText;
            private readonly Exception _exceptionToThrow;

            public FakeAiSourceBubblesTranslationProvider(
                string providerId,
                string translatedText = null,
                Exception exceptionToThrow = null
            )
            {
                ProviderId = providerId;
                _translatedText = translatedText;
                _exceptionToThrow = exceptionToThrow;
            }

            public string ProviderId { get; }

            public Task<List<AiSourceBubblesTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
                CollectionSettings collectionSettings,
                HttpClient httpClient
            )
            {
                return Task.FromResult(new List<AiSourceBubblesTargetLanguageOption>());
            }

            public Task<string> TranslateAsync(
                CollectionSettings collectionSettings,
                string sourceText,
                string sourceLanguageTag,
                string targetLanguageTag,
                HttpClient httpClient
            )
            {
                if (_exceptionToThrow != null)
                {
                    throw _exceptionToThrow;
                }

                return Task.FromResult(_translatedText);
            }
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
