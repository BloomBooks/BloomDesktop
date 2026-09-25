using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom;
using Bloom.AiTranslation;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;
using NUnit.Framework;

namespace BloomTests.AiTranslation
{
    [TestFixture]
    public class AiTranslationServiceTests
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
                AiTranslationService.NormalizeProviderId("googleTranslate"),
                Is.EqualTo("google")
            );
        }

        [Test]
        public void GetAiLanguageTag_AppendsNormalizedProviderSuffix()
        {
            Assert.That(
                AiTranslationService.GetAiLanguageTag("fr", "googleTranslate"),
                Is.EqualTo("fr-x-ai-google")
            );
        }

        [Test]
        public void GetGoogleProjectIdFromServiceAccountEmail_ParsesProjectId()
        {
            Assert.That(
                AiTranslationService.GetGoogleProjectIdFromServiceAccountEmail(
                    "translator@test-project-123.iam.gserviceaccount.com"
                ),
                Is.EqualTo("test-project-123")
            );
        }

        [Test]
        public void GetEngineFingerprint_ChangesWhenApiKeyChanges()
        {
            var engine = new AiTranslationEngineSettings
            {
                ProviderId = "deepl",
                ApiKey = "first-key",
            };
            var originalFingerprint = AiTranslationService.GetEngineFingerprint(engine, "fr");

            engine.ApiKey = "second-key";

            Assert.That(
                AiTranslationService.GetEngineFingerprint(engine, "fr"),
                Is.Not.EqualTo(originalFingerprint)
            );
        }

        [Test]
        public void GetEngineFingerprint_StableForUnchangedSettings()
        {
            var engine = new AiTranslationEngineSettings
            {
                ProviderId = "deepl",
                ApiKey = "same-key",
            };

            var first = AiTranslationService.GetEngineFingerprint(engine, "fr");
            var second = AiTranslationService.GetEngineFingerprint(engine, "fr");

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void GetEngineFingerprint_ChangesWhenTargetLanguageChanges()
        {
            var engine = new AiTranslationEngineSettings
            {
                ProviderId = "deepl",
                ApiKey = "same-key",
            };

            var frenchFingerprint = AiTranslationService.GetEngineFingerprint(engine, "fr");
            var spanishFingerprint = AiTranslationService.GetEngineFingerprint(engine, "es");

            Assert.That(spanishFingerprint, Is.Not.EqualTo(frenchFingerprint));
        }

        [Test]
        public void GetEngineFingerprint_IsIndependentOfOtherEnginesCredentials()
        {
            // Two independently-configured engines should each fingerprint based only on their
            // own provider/target/credentials, regardless of what other engines are configured with.
            var deeplEngine = new AiTranslationEngineSettings
            {
                ProviderId = "deepl",
                ApiKey = "deepl-key",
            };
            var googleEngine = new AiTranslationEngineSettings
            {
                ProviderId = "google",
                ServiceAccountEmail = "first@example.com",
                PrivateKey = "first-private-key",
            };

            var originalFingerprint = AiTranslationService.GetEngineFingerprint(deeplEngine, "fr");

            googleEngine.ServiceAccountEmail = "second@example.com";
            googleEngine.PrivateKey = "second-private-key";

            Assert.That(
                AiTranslationService.GetEngineFingerprint(deeplEngine, "fr"),
                Is.EqualTo(originalFingerprint)
            );
        }

        [Test]
        public void TranslateSegmentsAsync_WithoutTargetLanguageTag_ThrowsHelpfulError()
        {
            var collectionSettings = MakeCollectionSettings();
            collectionSettings.AiTranslationTargetLanguageTag = "";
            var service = new AiTranslationService(collectionSettings);
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", ApiKey = "key" };

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.TranslateSegmentsAsync(
                    engine,
                    new[] { "Hello world." },
                    "en",
                    CancellationToken.None
                )
            );

            Assert.That(exception.Message, Does.Contain("target language tag"));
        }

        [Test]
        public async Task TranslateSegmentsAsync_FewerSegmentsThanLimit_MakesOneCall()
        {
            var fakeProvider = new FakeAiTranslationProvider(
                "deepl",
                maxSegmentsPerRequest: 50,
                maxRequestBytes: 1_000_000
            );
            var service = MakeService(fakeProvider);
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", ApiKey = "key" };
            var segments = Enumerable.Range(0, 50).Select(i => $"segment {i}").ToArray();

            var results = await service.TranslateSegmentsAsync(
                engine,
                segments,
                "en",
                CancellationToken.None
            );

            Assert.That(fakeProvider.Calls.Count, Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(segments.Select(s => "[fr] " + s)));
        }

        [Test]
        public async Task TranslateSegmentsAsync_MoreSegmentsThanLimit_ChunksBySegmentCount()
        {
            var fakeProvider = new FakeAiTranslationProvider(
                "deepl",
                maxSegmentsPerRequest: 50,
                maxRequestBytes: 1_000_000
            );
            var service = MakeService(fakeProvider);
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", ApiKey = "key" };
            var segments = Enumerable.Range(0, 51).Select(i => $"segment {i}").ToArray();

            var results = await service.TranslateSegmentsAsync(
                engine,
                segments,
                "en",
                CancellationToken.None
            );

            Assert.That(fakeProvider.Calls.Count, Is.EqualTo(2));
            Assert.That(fakeProvider.Calls[0].Length, Is.EqualTo(50));
            Assert.That(fakeProvider.Calls[1].Length, Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(segments.Select(s => "[fr] " + s)));
        }

        [Test]
        public async Task TranslateSegmentsAsync_ChunksByByteSize()
        {
            var fakeProvider = new FakeAiTranslationProvider(
                "deepl",
                maxSegmentsPerRequest: 1000,
                maxRequestBytes: 30
            );
            var service = MakeService(fakeProvider);
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", ApiKey = "key" };
            // Each segment is 9 bytes; a byte cap of 30 should limit each chunk to 3 segments.
            var segments = Enumerable.Range(0, 7).Select(i => $"aaaaaaaa{i}").ToArray();
            Assert.That(
                segments.All(s => Encoding.UTF8.GetByteCount(s) == 9),
                "sanity check: each segment should be 9 bytes"
            );

            var results = await service.TranslateSegmentsAsync(
                engine,
                segments,
                "en",
                CancellationToken.None
            );

            Assert.That(fakeProvider.Calls.Count, Is.EqualTo(3));
            Assert.That(fakeProvider.Calls[0].Length, Is.EqualTo(3));
            Assert.That(fakeProvider.Calls[1].Length, Is.EqualTo(3));
            Assert.That(fakeProvider.Calls[2].Length, Is.EqualTo(1));
            Assert.That(results, Is.EqualTo(segments.Select(s => "[fr] " + s)));
        }

        [Test]
        public async Task TranslateSegmentsAsync_OversizedSingleSegment_GetsItsOwnChunk()
        {
            var fakeProvider = new FakeAiTranslationProvider(
                "deepl",
                maxSegmentsPerRequest: 1000,
                maxRequestBytes: 5
            );
            var service = MakeService(fakeProvider);
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", ApiKey = "key" };
            var segments = new[] { "short", "this-segment-is-longer-than-the-byte-cap", "short2" };

            var results = await service.TranslateSegmentsAsync(
                engine,
                segments,
                "en",
                CancellationToken.None
            );

            Assert.That(fakeProvider.Calls.Count, Is.EqualTo(3));
            Assert.That(fakeProvider.Calls[1].Single(), Is.EqualTo(segments[1]));
            Assert.That(results, Is.EqualTo(segments.Select(s => "[fr] " + s)));
        }

        [Test]
        public void TranslateSegmentsAsync_WhenProviderReturnsWrongCount_Throws()
        {
            var fakeProvider = new FakeAiTranslationProvider(
                "deepl",
                maxSegmentsPerRequest: 50,
                maxRequestBytes: 1_000_000,
                mismatchCount: true
            );
            var service = MakeService(fakeProvider);
            var engine = new AiTranslationEngineSettings { ProviderId = "deepl", ApiKey = "key" };

            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.TranslateSegmentsAsync(
                    engine,
                    new[] { "one", "two" },
                    "en",
                    CancellationToken.None
                )
            );
        }

        [Test]
        public async Task GetSupportedTargetLanguagesAsync_OneEngineFailsToListLanguages_StillReturnsOthers()
        {
            var service = MakeLanguageListService(
                new FakeLanguageListProvider(
                    "deepl",
                    throwMessage: "403 Forbidden. Missing required scope(s): languages:read."
                ),
                new FakeLanguageListProvider("google", languages: new[] { ("es", "Spanish (es)") })
            );

            var options = await service.GetSupportedTargetLanguagesAsync(CancellationToken.None);

            Assert.That(
                options.Count,
                Is.EqualTo(1),
                "Google's language should still be returned even though DeepL failed to list languages"
            );
            Assert.That(options[0].Value, Is.EqualTo("es"));
            Assert.That(options[0].ProviderIds, Does.Contain("google"));
            Assert.That(options[0].ProviderIds, Does.Not.Contain("deepl"));
        }

        [Test]
        public void GetSupportedTargetLanguagesAsync_EveryEngineFailsToListLanguages_ThrowsWithCombinedMessage()
        {
            var service = MakeLanguageListService(
                new FakeLanguageListProvider(
                    "deepl",
                    throwMessage: "403 Forbidden. Missing required scope(s): languages:read."
                ),
                new FakeLanguageListProvider("google", throwMessage: "boom")
            );

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.GetSupportedTargetLanguagesAsync(CancellationToken.None)
            );

            // Sanity check: with no engine succeeding, the user still gets an informative error
            // that names the failing engine and its reason.
            Assert.That(exception.Message, Does.Contain("DeepL"));
            Assert.That(exception.Message, Does.Contain("languages:read"));
        }

        [Test]
        public void GetEngineFingerprint_ChangesWhenSourceLanguageTagChanges()
        {
            var engine = new AiTranslationEngineSettings
            {
                ProviderId = "alpha2",
                ApiKey = "same-key",
                SourceLanguageTag = "en",
            };

            var englishSource = AiTranslationService.GetEngineFingerprint(engine, "fr");
            engine.SourceLanguageTag = "es";
            var spanishSource = AiTranslationService.GetEngineFingerprint(engine, "fr");

            Assert.That(spanishSource, Is.Not.EqualTo(englishSource));
        }

        [Test]
        public async Task GetSupportedTargetLanguagesAsync_LooksUpAlpha2TargetsForItsConfiguredSourceOnly()
        {
            var provider = new FakeLanguageListProvider(
                "alpha2",
                languages: new[] { ("es", "Spanish") }
            );
            var collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiTranslationTargetLanguageTag = "de",
                Language1Tag = "fr",
                AiTranslationEngines = new List<AiTranslationEngineSettings>
                {
                    new AiTranslationEngineSettings
                    {
                        ProviderId = "alpha2",
                        Enabled = true,
                        SourceLanguageTag = "es",
                    },
                },
            };
            var service = new AiTranslationService(
                collectionSettings,
                new Dictionary<string, IAiTranslationProvider> { { "alpha2", provider } }
            );

            await service.GetSupportedTargetLanguagesAsync(CancellationToken.None);

            // Only the engine's configured source is used. We must NOT union in English or the
            // collection languages: alpha2 unions its target results across every source given, and
            // English reaches a near-superset of targets, so including it made the list look the
            // same regardless of the chosen source (BL-16549).
            Assert.That(provider.LastLikelySourceLanguageTags, Is.EquivalentTo(new[] { "es" }));
        }

        [Test]
        public async Task ValidateEngineAsync_TargetMissingFromNonEmptyList_ReturnsTargetLanguageNotSupported_WithoutProbing()
        {
            var provider = new FakeValidatingProvider(
                "alpha2",
                supportedTargets: new[] { "es", "fr" }
            );
            var collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiTranslationTargetLanguageTag = "de", // not in the provider's supported list
            };
            var service = new AiTranslationService(
                collectionSettings,
                new Dictionary<string, IAiTranslationProvider> { { "alpha2", provider } }
            );
            var engine = new AiTranslationEngineSettings { ProviderId = "alpha2", ApiKey = "key" };

            var result = await service.ValidateEngineAsync(engine, CancellationToken.None);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.TargetLanguageNotSupported, Is.True);
            Assert.That(
                provider.TranslateCallCount,
                Is.EqualTo(0),
                "an unsupported target must short-circuit before the probe translation"
            );
        }

        [Test]
        public async Task ValidateEngineAsync_TargetSupported_ProbesUsingConfiguredSourceLanguage()
        {
            var provider = new FakeValidatingProvider("alpha2", supportedTargets: new[] { "es" });
            var collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiTranslationTargetLanguageTag = "es",
            };
            var service = new AiTranslationService(
                collectionSettings,
                new Dictionary<string, IAiTranslationProvider> { { "alpha2", provider } }
            );
            var engine = new AiTranslationEngineSettings
            {
                ProviderId = "alpha2",
                ApiKey = "key",
                SourceLanguageTag = "fr",
            };

            var result = await service.ValidateEngineAsync(engine, CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.TargetLanguageNotSupported, Is.False);
            Assert.That(
                provider.LastTranslateSourceLanguageTag,
                Is.EqualTo("fr"),
                "the probe should translate FROM the engine's configured source language"
            );
        }

        [Test]
        public async Task ValidateEngineAsync_EmptySupportedList_FallsThroughToProbe()
        {
            var provider = new FakeValidatingProvider("alpha2", supportedTargets: new string[0]);
            var collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiTranslationTargetLanguageTag = "de",
            };
            var service = new AiTranslationService(
                collectionSettings,
                new Dictionary<string, IAiTranslationProvider> { { "alpha2", provider } }
            );
            var engine = new AiTranslationEngineSettings { ProviderId = "alpha2", ApiKey = "key" };

            var result = await service.ValidateEngineAsync(engine, CancellationToken.None);

            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.TargetLanguageNotSupported, Is.False);
            Assert.That(
                provider.TranslateCallCount,
                Is.EqualTo(1),
                "an empty supported-languages list is not proof of non-support, so we still probe"
            );
        }

        private static AiTranslationService MakeLanguageListService(
            params FakeLanguageListProvider[] providers
        )
        {
            var collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiTranslationTargetLanguageTag = "fr",
                AiTranslationEngines = providers
                    .Select(p => new AiTranslationEngineSettings
                    {
                        ProviderId = p.ProviderId,
                        Enabled = true,
                    })
                    .ToList(),
            };
            return new AiTranslationService(
                collectionSettings,
                providers.ToDictionary(p => p.ProviderId, p => (IAiTranslationProvider)p)
            );
        }

        private static AiTranslationService MakeService(IAiTranslationProvider fakeProvider)
        {
            var collectionSettings = MakeCollectionSettings();
            return new AiTranslationService(
                collectionSettings,
                new Dictionary<string, IAiTranslationProvider>
                {
                    { fakeProvider.ProviderId, fakeProvider },
                }
            );
        }

        private static CollectionSettings MakeCollectionSettings()
        {
            return new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiTranslationTargetLanguageTag = "fr",
            };
        }

        private sealed class FakeAiTranslationProvider : IAiTranslationProvider
        {
            private readonly bool _mismatchCount;

            public FakeAiTranslationProvider(
                string providerId,
                int maxSegmentsPerRequest,
                int maxRequestBytes,
                bool mismatchCount = false
            )
            {
                ProviderId = providerId;
                MaxSegmentsPerRequest = maxSegmentsPerRequest;
                MaxRequestBytes = maxRequestBytes;
                _mismatchCount = mismatchCount;
            }

            public string ProviderId { get; }
            public int MaxSegmentsPerRequest { get; }
            public int MaxRequestBytes { get; }
            public List<string[]> Calls { get; } = new List<string[]>();

            public Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
                AiTranslationEngineSettings engine,
                IReadOnlyList<string> likelySourceLanguageTags,
                HttpClient httpClient,
                CancellationToken ct
            )
            {
                return Task.FromResult(new List<AiTranslationTargetLanguageOption>());
            }

            public Task<string[]> TranslateBatchAsync(
                AiTranslationEngineSettings engine,
                string[] segments,
                string sourceLanguageTag,
                string targetLanguageTag,
                HttpClient httpClient,
                CancellationToken ct
            )
            {
                Calls.Add(segments);
                if (_mismatchCount)
                {
                    return Task.FromResult(new[] { "only one translation" });
                }
                return Task.FromResult(
                    segments.Select(s => $"[{targetLanguageTag}] " + s).ToArray()
                );
            }
        }

        /// <summary>
        /// A provider used only to exercise GetSupportedTargetLanguagesAsync's aggregation: it
        /// either returns a fixed set of (tag, name) languages, or throws the given message to
        /// simulate an engine that can't list its languages (e.g. a missing API scope).
        /// </summary>
        private sealed class FakeLanguageListProvider : IAiTranslationProvider
        {
            private readonly (string Tag, string Name)[] _languages;
            private readonly string _throwMessage;

            public FakeLanguageListProvider(
                string providerId,
                (string Tag, string Name)[] languages = null,
                string throwMessage = null
            )
            {
                ProviderId = providerId;
                _languages = languages ?? new (string, string)[0];
                _throwMessage = throwMessage;
            }

            public string ProviderId { get; }
            public int MaxSegmentsPerRequest => 100;
            public int MaxRequestBytes => 100_000;

            /// <summary>The likelySourceLanguageTags the service passed on the most recent call.</summary>
            public IReadOnlyList<string> LastLikelySourceLanguageTags { get; private set; }

            public Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
                AiTranslationEngineSettings engine,
                IReadOnlyList<string> likelySourceLanguageTags,
                HttpClient httpClient,
                CancellationToken ct
            )
            {
                LastLikelySourceLanguageTags = likelySourceLanguageTags;
                if (_throwMessage != null)
                    throw new HttpRequestException(_throwMessage);

                return Task.FromResult(
                    _languages
                        .Select(l => new AiTranslationTargetLanguageOption
                        {
                            Value = l.Tag,
                            Label = l.Name,
                            ProviderIds = new List<string> { ProviderId },
                        })
                        .ToList()
                );
            }

            public Task<string[]> TranslateBatchAsync(
                AiTranslationEngineSettings engine,
                string[] segments,
                string sourceLanguageTag,
                string targetLanguageTag,
                HttpClient httpClient,
                CancellationToken ct
            )
            {
                throw new System.NotSupportedException(
                    "FakeLanguageListProvider is only for supported-languages tests."
                );
            }
        }

        /// <summary>
        /// A provider that both lists a fixed set of supported target languages AND "translates",
        /// recording how the probe was called, so ValidateEngineAsync's target-support check and
        /// probe-source behavior can be exercised without any network.
        /// </summary>
        private sealed class FakeValidatingProvider : IAiTranslationProvider
        {
            private readonly string[] _supportedTargets;

            public FakeValidatingProvider(string providerId, string[] supportedTargets)
            {
                ProviderId = providerId;
                _supportedTargets = supportedTargets ?? new string[0];
            }

            public string ProviderId { get; }
            public int MaxSegmentsPerRequest => 100;
            public int MaxRequestBytes => 100_000;
            public int TranslateCallCount { get; private set; }
            public string LastTranslateSourceLanguageTag { get; private set; }

            public Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
                AiTranslationEngineSettings engine,
                IReadOnlyList<string> likelySourceLanguageTags,
                HttpClient httpClient,
                CancellationToken ct
            )
            {
                return Task.FromResult(
                    _supportedTargets
                        .Select(tag => new AiTranslationTargetLanguageOption
                        {
                            Value = tag,
                            Label = tag,
                            ProviderIds = new List<string> { ProviderId },
                        })
                        .ToList()
                );
            }

            public Task<string[]> TranslateBatchAsync(
                AiTranslationEngineSettings engine,
                string[] segments,
                string sourceLanguageTag,
                string targetLanguageTag,
                HttpClient httpClient,
                CancellationToken ct
            )
            {
                TranslateCallCount++;
                LastTranslateSourceLanguageTag = sourceLanguageTag;
                return Task.FromResult(segments.Select(s => "translated:" + s).ToArray());
            }
        }
    }

    public abstract class AiTranslationLiveTranslationTestsBase
    {
        private bool _previousAiSourceBubblesEnabled;

        protected abstract string ProviderId { get; }
        protected abstract string[] RequiredEnvironmentVariables { get; }
        protected abstract AiTranslationEngineSettings MakeEngine();

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
        public async Task TranslateSegmentsAsync_ConfiguredProvider_ReturnsTranslationsInOrder()
        {
            var missingVariables = RequiredEnvironmentVariables
                .Where(variableName =>
                    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variableName))
                )
                .ToArray();
            if (missingVariables.Any())
            {
                Assert.Ignore(
                    $"Manual AI translation provider test. Set {string.Join(", ", missingVariables)} to run it."
                );
            }

            var collectionSettings = new CollectionSettings
            {
                Subscription = Subscription.CreateTempSubscriptionForTier(SubscriptionTier.Pro),
                AiTranslationTargetLanguageTag = "fr",
            };
            var service = new AiTranslationService(collectionSettings);
            var engine = MakeEngine();
            var segments = new[] { "Hello world.", "Good morning.", "See you tomorrow." };

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
        }
    }

    [TestFixture]
    [Category("SkipOnTeamCity")]
    [NonParallelizable]
    public class DeepLLiveTranslationTests : AiTranslationLiveTranslationTestsBase
    {
        protected override string ProviderId => "deepl";

        protected override string[] RequiredEnvironmentVariables => new[] { "BLOOM_DEEPL_KEY" };

        protected override AiTranslationEngineSettings MakeEngine()
        {
            return new AiTranslationEngineSettings
            {
                ProviderId = "deepl",
                ApiKey = Environment.GetEnvironmentVariable("BLOOM_DEEPL_KEY"),
            };
        }
    }

    [TestFixture]
    [Category("SkipOnTeamCity")]
    [NonParallelizable]
    public class GoogleLiveTranslationTests : AiTranslationLiveTranslationTestsBase
    {
        protected override string ProviderId => "google";

        protected override string[] RequiredEnvironmentVariables =>
            new[]
            {
                "BLOOM_GOOGLE_TRANSLATION_SERVICE_ACCOUNT_EMAIL",
                "BLOOM_GOOGLE_TRANSLATION_SERVICE_PRIVATE_KEY",
            };

        protected override AiTranslationEngineSettings MakeEngine()
        {
            return new AiTranslationEngineSettings
            {
                ProviderId = "google",
                ServiceAccountEmail = Environment.GetEnvironmentVariable(
                    "BLOOM_GOOGLE_TRANSLATION_SERVICE_ACCOUNT_EMAIL"
                ),
                PrivateKey = Environment.GetEnvironmentVariable(
                    "BLOOM_GOOGLE_TRANSLATION_SERVICE_PRIVATE_KEY"
                ),
            };
        }
    }
}
