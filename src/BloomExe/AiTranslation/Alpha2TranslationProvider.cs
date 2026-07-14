using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Collection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.WritingSystems;

namespace Bloom.AiTranslation
{
    /// <summary>
    /// AI translation provider backed by the SIL Alpha2 text-collection API
    /// (https://alpha2.multilingualai.com). Unlike DeepL/Google, translation here is
    /// collection-based and asynchronous: segments are uploaded as a "text collection", a
    /// translation job is kicked off against a chosen model, and the caller polls until every
    /// segment's translation is complete.
    /// </summary>
    internal sealed class Alpha2TranslationProvider : IAiTranslationProvider
    {
        private const string kApiBaseUrl = "https://alpha2.multilingualai.com/api";

        // How often to re-poll a text collection for completed translations, and how long to
        // keep polling before giving up. Observed live round trips for tiny batches complete
        // within one poll (~4s); larger/production batches are untested at scale, hence the
        // generous overall timeout.
        private static readonly TimeSpan kPollInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan kPollTimeout = TimeSpan.FromMinutes(5);

        public string ProviderId => "alpha2";
        public int MaxSegmentsPerRequest => 500;
        public int MaxRequestBytes => 400_000;

        /// <summary>
        /// Alpha2 has no supported-languages matrix: /v2/translation_models only returns
        /// {id, name} per model, with no src/trg metadata, so there is no way to enumerate which
        /// target languages are supported without querying every language pair up front. Alpha2
        /// viability for a given pair is instead proven by the validation probe
        /// (TranslateBatchAsync), which throws a clear "no translation model" error when a pair
        /// isn't supported. So the settings UI's union dropdown simply won't include
        /// Alpha2-only languages.
        /// </summary>
        public Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
            AiTranslationEngineSettings engine,
            HttpClient httpClient,
            CancellationToken ct
        )
        {
            return Task.FromResult(new List<AiTranslationTargetLanguageOption>());
        }

        /// <summary>
        /// Translates a batch of segments via Alpha2's text-collection flow: create a source
        /// collection, kick off a translation against the first available model for the
        /// language pair, poll until every segment is complete, then delete both the source and
        /// output collections.
        /// </summary>
        public async Task<string[]> TranslateBatchAsync(
            AiTranslationEngineSettings engine,
            string[] segments,
            string sourceLanguageTag,
            string targetLanguageTag,
            HttpClient httpClient,
            CancellationToken ct
        )
        {
            EnsureCredentials(engine);

            var apiKey = engine.ApiKey.Trim();
            var srcIso3 = MapToIso6393(sourceLanguageTag);
            var trgIso3 = MapToIso6393(targetLanguageTag);

            var modelId = await GetModelIdAsync(httpClient, apiKey, srcIso3, trgIso3, ct);
            var sourceCollectionId = await CreateTextCollectionAsync(
                httpClient,
                apiKey,
                srcIso3,
                segments,
                ct
            );
            long? outputCollectionId = null;
            string[] translations;
            try
            {
                outputCollectionId = await RequestTranslationAsync(
                    httpClient,
                    apiKey,
                    sourceCollectionId,
                    trgIso3,
                    modelId,
                    ct
                );

                translations = await PollForTranslationsAsync(
                    httpClient,
                    apiKey,
                    sourceCollectionId,
                    trgIso3,
                    modelId,
                    segments.Length,
                    ct
                );
            }
            catch
            {
                // Best-effort cleanup on the failure path: a collection is still a real,
                // storage-consuming resource even when translation failed, but a delete failure
                // here must never replace/mask the original, more informative exception.
                await TryDeleteBestEffortAsync(httpClient, apiKey, sourceCollectionId, ct);
                if (outputCollectionId.HasValue)
                {
                    await TryDeleteBestEffortAsync(
                        httpClient,
                        apiKey,
                        outputCollectionId.Value,
                        ct
                    );
                }
                throw;
            }

            // Both the source collection and the translate-created output collection are real,
            // storage-consuming resources on the Alpha2 side; clean up both. On this success
            // path a delete failure is a genuine problem, so let it propagate normally.
            await DeleteTextCollectionAsync(httpClient, apiKey, sourceCollectionId, ct);
            if (outputCollectionId.HasValue)
            {
                await DeleteTextCollectionAsync(httpClient, apiKey, outputCollectionId.Value, ct);
            }

            return translations;
        }

        /// <summary>
        /// Maps a Bloom language tag (BCP-47, e.g. "en", "en-US", or already ISO 639-3 like
        /// "eng") to the ISO 639-3 code Alpha2 requires. A tag whose primary subtag is already
        /// three letters is passed through as-is; otherwise it is looked up in libpalaso's
        /// registered-languages table. Throws if the tag can't be mapped.
        /// </summary>
        internal static string MapToIso6393(string bloomLanguageTag)
        {
            if (string.IsNullOrWhiteSpace(bloomLanguageTag))
            {
                throw new InvalidOperationException("Alpha2 requires a language tag.");
            }

            var normalized = AiTranslationService.NormalizeBloomLanguageTag(bloomLanguageTag);
            var primarySubtag = normalized.Split('-')[0].ToLowerInvariant();
            if (primarySubtag.Length == 3)
            {
                return primarySubtag;
            }

            if (
                !StandardSubtags.RegisteredLanguages.TryGet(primarySubtag, out var languageSubtag)
                || string.IsNullOrWhiteSpace(languageSubtag.Iso3Code)
            )
            {
                throw new InvalidOperationException(
                    $"Cannot map language tag '{bloomLanguageTag}' to an ISO 639-3 code for Alpha2."
                );
            }

            return languageSubtag.Iso3Code;
        }

        /// <summary>
        /// Picks the first translation model Alpha2 reports for the given language pair. Throws
        /// if no model supports the pair.
        /// </summary>
        private static async Task<int> GetModelIdAsync(
            HttpClient httpClient,
            string apiKey,
            string srcIso3,
            string trgIso3,
            CancellationToken ct
        )
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{kApiBaseUrl}/v2/translation_models?src={Uri.EscapeDataString(srcIso3)}&trg={Uri.EscapeDataString(trgIso3)}"
            );
            request.Headers.Add("api_key", apiKey);

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(response, responseContent, "Alpha2");

            var models = JArray.Parse(responseContent);
            if (models.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Alpha2 has no translation model for {srcIso3} -> {trgIso3}."
                );
            }

            return models[0]["id"].Value<int>();
        }

        /// <summary>
        /// Creates a source text collection holding one text per segment, preserving internal
        /// newlines within a segment (using the `texts` array, not the newline-splitting `text`
        /// field). Returns the new collection's id.
        /// </summary>
        private static async Task<long> CreateTextCollectionAsync(
            HttpClient httpClient,
            string apiKey,
            string srcIso3,
            string[] segments,
            CancellationToken ct
        )
        {
            var requestBody = new
            {
                name = $"bloom-{Guid.NewGuid():N}",
                language = srcIso3,
                texts = segments,
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{kApiBaseUrl}/v2/text_collections"
            );
            request.Headers.Add("api_key", apiKey);
            request.Content = new StringContent(
                JsonConvert.SerializeObject(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(response, responseContent, "Alpha2");

            var json = JObject.Parse(responseContent);
            return json["id"].Value<long>();
        }

        /// <summary>
        /// Kicks off an asynchronous translation job for a source collection. This creates a
        /// separate output collection on the Alpha2 side (returned here so it can be deleted
        /// later); the source collection's own texts remain untouched.
        /// </summary>
        private static async Task<long?> RequestTranslationAsync(
            HttpClient httpClient,
            string apiKey,
            long sourceCollectionId,
            string trgIso3,
            int modelId,
            CancellationToken ct
        )
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{kApiBaseUrl}/v2/text_collections/{sourceCollectionId}/translate?target_language={Uri.EscapeDataString(trgIso3)}&model_id={modelId}"
            );
            request.Headers.Add("api_key", apiKey);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(response, responseContent, "Alpha2");

            var json = JObject.Parse(responseContent);
            return json["id"]?.Value<long>();
        }

        /// <summary>
        /// Polls the source collection's texts (with translations included) until every segment
        /// reaches a "complete" status, then returns the translated strings in original
        /// submission order. The in-progress statuses "pending" and "running" (and a momentarily
        /// missing status) are treated as not-yet-done and keep the poll going. Throws if any
        /// segment reaches an unrecognized status, if the response's text count doesn't match the
        /// segment count, or if the overall poll timeout elapses.
        /// </summary>
        private static async Task<string[]> PollForTranslationsAsync(
            HttpClient httpClient,
            string apiKey,
            long sourceCollectionId,
            string trgIso3,
            int modelId,
            int expectedCount,
            CancellationToken ct
        )
        {
            var deadline = DateTime.UtcNow + kPollTimeout;
            while (true)
            {
                var texts = await GetTextsWithTranslationsAsync(
                    httpClient,
                    apiKey,
                    sourceCollectionId,
                    trgIso3,
                    modelId,
                    ct
                );
                // The endpoint returns texts newest-first; sort ascending by id to recover the
                // original submission order (ids are assigned monotonically at creation time).
                var sortedTexts = texts.OrderBy(t => t["id"]?.Value<long>() ?? 0L).ToList();

                // Observed live: immediately after the translate call returns, a poll can see
                // fewer texts than were submitted (the per-text translation rows aren't
                // queryable yet for a brief moment). Treat missing texts as still-pending rather
                // than an error; a genuinely wrong count only becomes an error once nothing is
                // pending anymore (or the poll times out).
                if (sortedTexts.Count > expectedCount)
                {
                    throw new InvalidOperationException(
                        $"Alpha2 returned {sortedTexts.Count} texts for {expectedCount} segments."
                    );
                }

                var pendingCount = expectedCount - sortedTexts.Count;
                foreach (var text in sortedTexts)
                {
                    var translation = (text["translations"] as JArray)?.FirstOrDefault();
                    var status = translation?["translation_status"]?.Value<string>();
                    // Complete segments are done; in-progress ones keep the poll going; anything
                    // else throws (see IsTranslationComplete).
                    if (!IsTranslationComplete(status))
                    {
                        pendingCount++;
                    }
                }

                if (pendingCount == 0)
                {
                    return sortedTexts
                        .Select(t =>
                            (t["translations"] as JArray)
                                ?.FirstOrDefault()
                                ?["text"]?.Value<string>() ?? string.Empty
                        )
                        .ToArray();
                }

                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        $"Alpha2 translation timed out after {kPollTimeout.TotalMinutes} minutes with {pendingCount} of {expectedCount} texts still pending."
                    );
                }

                await Task.Delay(kPollInterval, ct);
            }
        }

        /// <summary>
        /// Classifies a segment's Alpha2 translation_status while polling. Returns true when the
        /// segment is finished ("complete"). Returns false for the in-progress statuses Alpha2
        /// walks a translation through — "pending" (queued) and "running" (translating) — as well
        /// as a momentarily-missing status seen right after the translate call kicks off. Throws
        /// for any other (unrecognized) status, which we treat as a hard failure rather than
        /// silently waiting forever on a segment that will never complete.
        /// </summary>
        internal static bool IsTranslationComplete(string status)
        {
            if (string.Equals(status, "complete", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (
                string.IsNullOrEmpty(status)
                || string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "running", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            throw new InvalidOperationException(
                $"Alpha2 translation failed for a segment: unexpected status '{status}'."
            );
        }

        /// <summary>
        /// Fetches the source collection's texts along with their translations for the given
        /// target language/model, used both while polling and (implicitly) once complete.
        /// </summary>
        private static async Task<JArray> GetTextsWithTranslationsAsync(
            HttpClient httpClient,
            string apiKey,
            long sourceCollectionId,
            string trgIso3,
            int modelId,
            CancellationToken ct
        )
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{kApiBaseUrl}/v2/text_collections/{sourceCollectionId}/texts?include_translations=true&target_language={Uri.EscapeDataString(trgIso3)}&translation_model_id={modelId}"
            );
            request.Headers.Add("api_key", apiKey);

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(response, responseContent, "Alpha2");

            return JArray.Parse(responseContent);
        }

        /// <summary>
        /// Deletes a text collection. Note the delete endpoint uses the singular
        /// "text_collection" path, unlike every other Alpha2 endpoint which uses the plural
        /// "text_collections".
        /// </summary>
        private static async Task DeleteTextCollectionAsync(
            HttpClient httpClient,
            string apiKey,
            long collectionId,
            CancellationToken ct
        )
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{kApiBaseUrl}/v2/text_collection/{collectionId}"
            );
            request.Headers.Add("api_key", apiKey);

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(response, responseContent, "Alpha2");
        }

        /// <summary>
        /// Deletes a text collection, swallowing any failure. Used only when cleaning up after a
        /// translation that has already failed, where a secondary delete failure would otherwise
        /// mask the original, more informative exception.
        /// </summary>
        private static async Task TryDeleteBestEffortAsync(
            HttpClient httpClient,
            string apiKey,
            long collectionId,
            CancellationToken ct
        )
        {
            try
            {
                await DeleteTextCollectionAsync(httpClient, apiKey, collectionId, ct);
            }
            catch
            {
                // Deliberately swallowed: see summary above.
            }
        }

        /// <summary>
        /// Validates that an API key is configured.
        /// </summary>
        private static void EnsureCredentials(AiTranslationEngineSettings engine)
        {
            if (string.IsNullOrWhiteSpace(engine.ApiKey))
            {
                throw new InvalidOperationException(
                    "Set an Alpha2 API key in Collection Settings > AI Source Bubbles."
                );
            }
        }
    }
}
