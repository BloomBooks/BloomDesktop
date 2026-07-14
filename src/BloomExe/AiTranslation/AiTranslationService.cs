using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Collection;
using Bloom.Utils;
using Newtonsoft.Json.Linq;
using SIL.WritingSystems;

namespace Bloom.AiTranslation
{
    /// <summary>
    /// Result of validating one AI translation engine's configuration.
    /// </summary>
    public class AiTranslationValidationResult
    {
        public bool Succeeded { get; set; }
        public string ConfigurationFingerprint { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Coordinates collection-backed AI translation across the 0..n engines
    /// configured on a collection (deepl, google, alpha2). Translation is always batched: callers
    /// pass an array of text segments per engine and get back translations in the same order.
    /// </summary>
    public class AiTranslationService
    {
        public const string kValidationProbeText = "Today a reader, tomorrow a leader.";
        public const string kValidationProbeSourceLanguageTag = "en";

        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly CollectionSettings _collectionSettings;
        private readonly Dictionary<string, IAiTranslationProvider> _providers;

        public AiTranslationService(CollectionSettings collectionSettings)
            : this(collectionSettings, null) { }

        internal AiTranslationService(
            CollectionSettings collectionSettings,
            Dictionary<string, IAiTranslationProvider> providers
        )
        {
            _collectionSettings = collectionSettings;
            _providers =
                providers
                ?? new Dictionary<string, IAiTranslationProvider>(StringComparer.OrdinalIgnoreCase)
                {
                    { "deepl", new DeepLTranslationProvider() },
                    { "google", new GoogleTranslationProvider() },
                    { "alpha2", new Alpha2TranslationProvider() },
                };
        }

        /// <summary>
        /// Translates all segments via the given engine, chunking requests to the provider's
        /// per-request segment-count and byte-size limits. Chunks are sent sequentially; the
        /// returned array preserves segment order and count, and this throws if any chunk's
        /// response doesn't have exactly as many translations as segments sent.
        /// </summary>
        public async Task<string[]> TranslateSegmentsAsync(
            AiTranslationEngineSettings engine,
            string[] segments,
            string sourceLanguageTag,
            CancellationToken ct
        )
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));
            if (segments == null)
                throw new ArgumentNullException(nameof(segments));

            var targetLanguageTag = NormalizeBloomLanguageTag(
                _collectionSettings.AiTranslationTargetLanguageTag
            );
            if (string.IsNullOrWhiteSpace(targetLanguageTag))
            {
                throw new InvalidOperationException(
                    "Set a target language tag in Collection Settings > AI Source Bubbles."
                );
            }

            var provider = GetProvider(engine.ProviderId);
            var results = new string[segments.Length];
            var resultIndex = 0;
            foreach (
                var chunk in ChunkSegments(
                    segments,
                    provider.MaxSegmentsPerRequest,
                    provider.MaxRequestBytes
                )
            )
            {
                var translatedChunk = await provider.TranslateBatchAsync(
                    engine,
                    chunk,
                    sourceLanguageTag,
                    targetLanguageTag,
                    _httpClient,
                    ct
                );
                if (translatedChunk.Length != chunk.Length)
                {
                    throw new InvalidOperationException(
                        $"{provider.ProviderId} returned {translatedChunk.Length} translations for a chunk of {chunk.Length} segments."
                    );
                }
                Array.Copy(translatedChunk, 0, results, resultIndex, translatedChunk.Length);
                resultIndex += translatedChunk.Length;
            }

            return results;
        }

        /// <summary>
        /// Splits segments into chunks that respect both a maximum segment count and a maximum
        /// total UTF-8 byte size per chunk. A single segment whose own byte size exceeds the
        /// byte cap is still sent, alone, in its own chunk.
        /// </summary>
        internal static IEnumerable<string[]> ChunkSegments(
            string[] segments,
            int maxSegmentsPerRequest,
            int maxRequestBytes
        )
        {
            var currentChunk = new List<string>();
            var currentBytes = 0;
            foreach (var segment in segments)
            {
                var segmentBytes = Encoding.UTF8.GetByteCount(segment ?? string.Empty);
                var wouldExceedCount = currentChunk.Count >= maxSegmentsPerRequest;
                var wouldExceedBytes =
                    currentChunk.Count > 0 && currentBytes + segmentBytes > maxRequestBytes;
                if (wouldExceedCount || wouldExceedBytes)
                {
                    yield return currentChunk.ToArray();
                    currentChunk = new List<string>();
                    currentBytes = 0;
                }

                currentChunk.Add(segment);
                currentBytes += segmentBytes;
            }

            if (currentChunk.Count > 0)
            {
                yield return currentChunk.ToArray();
            }
        }

        /// <summary>
        /// Validates one engine's configuration, credentials, and target language with a probe translation.
        /// </summary>
        public async Task<AiTranslationValidationResult> ValidateEngineAsync(
            AiTranslationEngineSettings engine,
            CancellationToken ct
        )
        {
            var translations = await TranslateSegmentsAsync(
                engine,
                new[] { kValidationProbeText },
                kValidationProbeSourceLanguageTag,
                ct
            );

            return new AiTranslationValidationResult
            {
                Succeeded = true,
                ConfigurationFingerprint = GetEngineFingerprint(
                    engine,
                    _collectionSettings.AiTranslationTargetLanguageTag
                ),
                Message = translations[0],
            };
        }

        /// <summary>
        /// Gets the union of target languages supported by all ENABLED engines on the collection,
        /// deduped by language tag (each option records which of those engines' providers support it).
        /// </summary>
        public async Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
            CancellationToken ct
        )
        {
            var optionsByTag = new Dictionary<string, AiTranslationTargetLanguageOption>(
                StringComparer.OrdinalIgnoreCase
            );
            var failures = new List<string>();
            foreach (var engine in _collectionSettings.AiTranslationEngines.Where(e => e.Enabled))
            {
                var provider = GetProvider(engine.ProviderId);
                List<AiTranslationTargetLanguageOption> options;
                try
                {
                    options = await provider.GetSupportedTargetLanguagesAsync(
                        engine,
                        _httpClient,
                        ct
                    );
                }
                catch (Exception e)
                    when (e is HttpRequestException
                        || e is InvalidOperationException
                        || e is ArgumentException
                        || e is CryptographicException
                        || e is Newtonsoft.Json.JsonException
                    )
                {
                    // One engine failing to list its languages -- e.g. a DeepL key that can
                    // translate but lacks the "languages:read" scope -- must not blank out the
                    // whole union. Other engines can still contribute the list, and this engine may
                    // still translate fine. Remember the failure only so we can report it if NO
                    // engine manages to return any languages.
                    failures.Add($"{GetProviderDisplayName(provider.ProviderId)}: {e.Message}");
                    continue;
                }

                foreach (var option in options)
                {
                    if (optionsByTag.TryGetValue(option.Value, out var existing))
                    {
                        existing.ProviderIds.Add(provider.ProviderId);
                    }
                    else
                    {
                        optionsByTag[option.Value] = option;
                    }
                }
            }

            // Only surface an error when every enabled engine failed to provide any languages; as
            // long as one succeeded, the union we return is enough to populate the dropdown.
            if (optionsByTag.Count == 0 && failures.Count > 0)
                throw new InvalidOperationException(string.Join(" ", failures));

            return optionsByTag
                .Values.OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Builds the Bloom language tag used for AI content in a translation group for the given engine.
        /// </summary>
        public string GetAiLanguageTagForEngine(AiTranslationEngineSettings engine)
        {
            return GetAiLanguageTag(
                _collectionSettings.AiTranslationTargetLanguageTag,
                engine.ProviderId
            );
        }

        /// <summary>
        /// Returns the human-facing name of a translation engine (e.g. "DeepL"), for use in
        /// progress messages and other UI. Engine/product names are not localized.
        /// </summary>
        public static string GetProviderDisplayName(string providerId)
        {
            switch (NormalizeProviderId(providerId))
            {
                case "deepl":
                    return "DeepL";
                case "google":
                    return "Google Translate";
                case "alpha2":
                    return "SIL Alpha2";
                default:
                    return providerId;
            }
        }

        /// <summary>
        /// Builds the Bloom language tag used for AI content in a translation group.
        /// </summary>
        public static string GetAiLanguageTag(string targetLanguageTag, string providerId)
        {
            var normalizedTarget = NormalizeBloomLanguageTag(targetLanguageTag);
            var normalizedProvider = NormalizeProviderId(providerId);
            if (
                string.IsNullOrWhiteSpace(normalizedTarget)
                || string.IsNullOrWhiteSpace(normalizedProvider)
            )
            {
                return string.Empty;
            }

            return $"{normalizedTarget}-x-ai-{normalizedProvider}";
        }

        /// <summary>
        /// Normalizes the provider id used in settings, API payloads, and AI language tags.
        /// </summary>
        public static string NormalizeProviderId(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
                return string.Empty;

            var normalized = providerId.Trim().ToLowerInvariant();
            return normalized switch
            {
                "googletranslate" => "google",
                _ => normalized,
            };
        }

        /// <summary>
        /// Normalizes a Bloom language tag for provider requests while preserving region when available.
        /// </summary>
        public static string NormalizeBloomLanguageTag(string languageTag)
        {
            if (string.IsNullOrWhiteSpace(languageTag))
                return string.Empty;

            var trimmed = languageTag.Trim();
            var privateUseIndex = trimmed.IndexOf("-x-", StringComparison.OrdinalIgnoreCase);
            if (privateUseIndex >= 0)
            {
                trimmed = trimmed.Substring(0, privateUseIndex);
            }

            trimmed = MiscUtils.NormalizeLanguageTagCapitalization(trimmed);
            if (
                !IetfLanguageTag.TryGetParts(
                    trimmed,
                    out var language,
                    out var script,
                    out var region,
                    out var variant
                ) || string.IsNullOrWhiteSpace(language)
            )
            {
                return trimmed;
            }

            language = language.ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(region))
            {
                return $"{language}-{region.ToUpperInvariant()}";
            }

            return language;
        }

        /// <summary>
        /// Extracts the Google Cloud project id from a service account email.
        /// </summary>
        public static string GetGoogleProjectIdFromServiceAccountEmail(string serviceAccountEmail)
        {
            if (string.IsNullOrWhiteSpace(serviceAccountEmail))
            {
                throw new InvalidOperationException(
                    "Set a Google service account email in Collection Settings > AI Source Bubbles."
                );
            }

            var trimmedEmail = serviceAccountEmail.Trim();
            var atIndex = trimmedEmail.IndexOf('@');
            if (atIndex < 0 || atIndex == trimmedEmail.Length - 1)
            {
                throw new InvalidOperationException(
                    "Google service account email is not in the expected format."
                );
            }

            var domain = trimmedEmail.Substring(atIndex + 1);
            const string kExpectedSuffix = ".iam.gserviceaccount.com";
            if (!domain.EndsWith(kExpectedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Google service account email must end with .iam.gserviceaccount.com."
                );
            }

            var projectId = domain.Substring(0, domain.Length - kExpectedSuffix.Length);
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new InvalidOperationException(
                    "Google service account email does not contain a project id."
                );
            }

            return projectId;
        }

        /// <summary>
        /// The subset of a collection's configured engines that are actually ready to translate with:
        /// enabled, and last validated against the exact configuration (provider, target language,
        /// credentials) they currently have. This is the single source of truth for "is this engine
        /// active" -- both the editor's allowAiSourceBubbles flag (see RuntimeInformationInjector) and
        /// the whole-book batch updater (AiTranslationBookUpdater) must agree on it.
        /// </summary>
        public static List<AiTranslationEngineSettings> GetActiveEngines(
            CollectionSettings collectionSettings
        )
        {
            var targetLanguageTag = collectionSettings.AiTranslationTargetLanguageTag;
            return collectionSettings
                .AiTranslationEngines.Where(engine =>
                    engine.Enabled
                    && engine.LastValidationSucceeded
                    && string.Equals(
                        engine.ValidatedConfigurationFingerprint,
                        GetEngineFingerprint(engine, targetLanguageTag),
                        StringComparison.Ordinal
                    )
                )
                .ToList();
        }

        /// <summary>
        /// Builds a stable fingerprint of one engine's provider, target language, and
        /// provider-specific credentials, without storing raw secrets.
        /// </summary>
        public static string GetEngineFingerprint(
            AiTranslationEngineSettings engine,
            string targetLanguageTag
        )
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            var normalizedProvider = NormalizeProviderId(engine.ProviderId);
            var normalizedTargetLanguageTag = NormalizeBloomLanguageTag(targetLanguageTag);
            var credentialKey = normalizedProvider switch
            {
                "google" =>
                    $"{engine.ServiceAccountEmail?.Trim()}\n{AiTranslationProviderHelpers.NormalizeGooglePrivateKey(engine.PrivateKey)}",
                _ => engine.ApiKey?.Trim() ?? string.Empty,
            };
            var fingerprintInput =
                $"{normalizedProvider}\n{normalizedTargetLanguageTag}\n{credentialKey}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)));
        }

        private IAiTranslationProvider GetProvider(string providerId)
        {
            var normalizedProviderId = NormalizeProviderId(providerId);
            if (string.IsNullOrWhiteSpace(normalizedProviderId))
            {
                throw new InvalidOperationException(
                    "An AI translation engine must have a provider id."
                );
            }

            if (!_providers.TryGetValue(normalizedProviderId, out var provider))
            {
                throw new InvalidOperationException(
                    $"Unsupported AI provider '{normalizedProviderId}'."
                );
            }

            return provider;
        }
    }

    internal static class AiTranslationProviderHelpers
    {
        internal static string NormalizeGooglePrivateKey(string privateKey)
        {
            return (privateKey ?? string.Empty).Replace("\\r", "").Replace("\\n", "\n").Trim();
        }

        internal static void EnsureSuccess(
            HttpResponseMessage response,
            string responseContent,
            string providerName
        )
        {
            if (response.IsSuccessStatusCode)
                return;

            var providerMessage = responseContent;
            try
            {
                var json = JObject.Parse(responseContent);
                providerMessage =
                    json["message"]?.Value<string>()
                    ?? json["error"]?.Value<string>()
                    ?? json["error"]?["message"]?.Value<string>()
                    ?? json["detail"]?[0]?["msg"]?.Value<string>()
                    ?? responseContent;
            }
            catch
            {
                // Keep the original response text when it isn't JSON.
            }

            throw new InvalidOperationException(
                $"{providerName} request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {providerMessage}".Trim()
            );
        }

        internal static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
