using System;
using System.Collections.Concurrent;
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

        // The /v2/translation_languages endpoint is queried once per (apiKey, src|trg iso3) key.
        // AiTranslationService is constructed fresh per API call, so this cache MUST be static to
        // survive across calls; a short TTL keeps it from going stale while still collapsing the
        // burst of queries a single dropdown-open produces.
        private static readonly TimeSpan kLanguageCacheTtl = TimeSpan.FromMinutes(10);
        private static readonly ConcurrentDictionary<
            string,
            (DateTime Expiry, List<AiTranslationTargetLanguageOption> Languages)
        > _languageCache =
            new ConcurrentDictionary<string, (DateTime, List<AiTranslationTargetLanguageOption>)>();

        // Lazily-built reverse map from ISO 639-3 code to the shortest registered Bloom/BCP-47
        // language tag, so alpha2's "fra" can dedup with DeepL's "fr" in the service-level union
        // (which keys on the option value). See MapIso6393ToBloomTag.
        private static readonly Lazy<Dictionary<string, string>> _iso6393ToBloomTag = new Lazy<
            Dictionary<string, string>
        >(BuildIso6393ToBloomTagMap);

        public string ProviderId => "alpha2";
        public int MaxSegmentsPerRequest => 500;
        public int MaxRequestBytes => 400_000;

        /// <summary>
        /// Lists the target languages Alpha2 can translate INTO from the given likely sources. Since
        /// Alpha2's supported targets are per source language, this queries
        /// /v2/translation_languages?src={iso3} once for each distinct (mappable) likely source and
        /// unions the results, deduped by the reverse-mapped Bloom tag. Sources that can't be mapped
        /// to ISO 639-3 are skipped; a query that fails for one source doesn't fail the whole list
        /// (unless every source query fails); an empty result for a source is not an error.
        /// </summary>
        public async Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
            AiTranslationEngineSettings engine,
            IReadOnlyList<string> likelySourceLanguageTags,
            HttpClient httpClient,
            CancellationToken ct
        )
        {
            EnsureCredentials(engine);
            return await GetLanguagesForAxisAsync(
                httpClient,
                engine.ApiKey.Trim(),
                "src",
                likelySourceLanguageTags,
                ct
            );
        }

        /// <summary>
        /// Lists the source languages Alpha2 can translate FROM into the given target language, via
        /// /v2/translation_languages?trg={iso3}. Used by the Alpha2 source-language chooser in
        /// Collection Settings. Returns an empty list if the target can't be mapped to ISO 639-3.
        /// </summary>
        public async Task<List<AiTranslationTargetLanguageOption>> GetSupportedSourceLanguagesAsync(
            AiTranslationEngineSettings engine,
            string targetLanguageTag,
            HttpClient httpClient,
            CancellationToken ct
        )
        {
            EnsureCredentials(engine);
            return await GetLanguagesForAxisAsync(
                httpClient,
                engine.ApiKey.Trim(),
                "trg",
                new[] { targetLanguageTag },
                ct
            );
        }

        /// <summary>
        /// Shared implementation for both directions: maps each pivot language tag to ISO 639-3
        /// (skipping unmappable ones), queries /v2/translation_languages with the given axis
        /// parameter ("src" or "trg") once per distinct pivot, and unions the results deduped by
        /// reverse-mapped Bloom tag. Throws only if every pivot query failed.
        /// </summary>
        private static async Task<List<AiTranslationTargetLanguageOption>> GetLanguagesForAxisAsync(
            HttpClient httpClient,
            string apiKey,
            string axisParam,
            IReadOnlyList<string> pivotLanguageTags,
            CancellationToken ct
        )
        {
            var pivotIso3Codes = new List<string>();
            foreach (var pivotTag in pivotLanguageTags ?? Array.Empty<string>())
            {
                try
                {
                    var iso3 = MapToIso6393(pivotTag);
                    if (!pivotIso3Codes.Contains(iso3))
                        pivotIso3Codes.Add(iso3);
                }
                catch (InvalidOperationException)
                {
                    // An unmappable pivot language (e.g. a collection language with no ISO 639-3
                    // code) must not break the whole list; just skip it.
                }
            }

            var optionsByTag = new Dictionary<string, AiTranslationTargetLanguageOption>(
                StringComparer.OrdinalIgnoreCase
            );
            var failures = new List<string>();
            var anySucceeded = false;
            foreach (var iso3 in pivotIso3Codes)
            {
                try
                {
                    var languages = await GetLanguagesForKeyAsync(
                        httpClient,
                        apiKey,
                        axisParam,
                        iso3,
                        ct
                    );
                    anySucceeded = true;
                    foreach (var option in languages)
                    {
                        if (!optionsByTag.ContainsKey(option.Value))
                            optionsByTag[option.Value] = option;
                    }
                }
                catch (Exception e)
                    when (e is HttpRequestException || e is InvalidOperationException)
                {
                    failures.Add(e.Message);
                }
            }

            // If we had pivots to query but every one failed, surface the error; otherwise return
            // whatever union we managed to build (possibly empty, which is a valid answer).
            if (!anySucceeded && failures.Count > 0)
                throw new InvalidOperationException(string.Join(" ", failures));

            return optionsByTag
                .Values.OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Fetches (and caches for kLanguageCacheTtl) the languages Alpha2 reports for one
        /// axis/iso3 key. Only successful responses are cached.
        /// </summary>
        private static async Task<List<AiTranslationTargetLanguageOption>> GetLanguagesForKeyAsync(
            HttpClient httpClient,
            string apiKey,
            string axisParam,
            string iso3,
            CancellationToken ct
        )
        {
            var cacheKey = $"{apiKey}\n{axisParam}:{iso3}";
            if (
                _languageCache.TryGetValue(cacheKey, out var cached)
                && cached.Expiry > DateTime.UtcNow
            )
            {
                return cached.Languages;
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{kApiBaseUrl}/v2/translation_languages?{axisParam}={Uri.EscapeDataString(iso3)}"
            );
            request.Headers.Add("api_key", apiKey);

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(response, responseContent, "Alpha2");

            var languages = ParseTranslationLanguages(responseContent);
            _languageCache[cacheKey] = (DateTime.UtcNow + kLanguageCacheTtl, languages);
            return languages;
        }

        /// <summary>
        /// Parses a /v2/translation_languages response body -- a JSON array of
        /// {iso, name, display, models:[...]} objects -- into target-language options, mapping each
        /// entry's ISO 639-3 code back to the shortest registered Bloom tag. Kept internal and
        /// HTTP-free so tests can exercise the parsing/reverse-mapping without mocking the network.
        /// </summary>
        internal static List<AiTranslationTargetLanguageOption> ParseTranslationLanguages(
            string responseContent
        )
        {
            var array = JArray.Parse(responseContent);
            var options = new List<AiTranslationTargetLanguageOption>();
            var seenTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in array)
            {
                var iso = entry["iso"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(iso))
                    continue;

                var bloomTag = MapIso6393ToBloomTag(iso);
                if (!seenTags.Add(bloomTag))
                    continue;

                var name = entry["name"]?.Value<string>();
                options.Add(
                    new AiTranslationTargetLanguageOption
                    {
                        Value = bloomTag,
                        Label = string.IsNullOrWhiteSpace(name) ? bloomTag : name,
                        ProviderIds = new List<string> { "alpha2" },
                    }
                );
            }

            options.Sort(
                (first, second) =>
                    StringComparer.CurrentCultureIgnoreCase.Compare(first.Label, second.Label)
            );
            return options;
        }

        /// <summary>
        /// Reverse of MapToIso6393: maps an ISO 639-3 code (as Alpha2 returns) to the shortest
        /// registered Bloom/BCP-47 language tag (e.g. "fra" -> "fr"), so an Alpha2 language dedups
        /// with the same language from DeepL/Google in the settings union. An iso3 with no shorter
        /// registered tag (the usual case for languages that only have a 3-letter code) passes
        /// through unchanged.
        /// </summary>
        internal static string MapIso6393ToBloomTag(string iso3)
        {
            if (string.IsNullOrWhiteSpace(iso3))
                return iso3;

            var normalized = iso3.Trim().ToLowerInvariant();
            if (_iso6393ToBloomTag.Value.TryGetValue(normalized, out var bloomTag))
                return bloomTag;

            return normalized;
        }

        /// <summary>
        /// Builds the ISO-639-3 -> shortest-registered-Bloom-tag map once. When several registered
        /// tags share an ISO 639-3 code, the shortest wins (ties broken lexicographically) so we
        /// prefer the familiar 2-letter tag (e.g. "fr" over "fra").
        /// </summary>
        private static Dictionary<string, string> BuildIso6393ToBloomTagMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var language in StandardSubtags.RegisteredLanguages)
            {
                var iso3 = language.Iso3Code;
                var code = language.Code;
                if (string.IsNullOrWhiteSpace(iso3) || string.IsNullOrWhiteSpace(code))
                    continue;

                var key = iso3.ToLowerInvariant();
                var candidate = code.ToLowerInvariant();
                if (
                    !map.TryGetValue(key, out var existing)
                    || candidate.Length < existing.Length
                    || (
                        candidate.Length == existing.Length
                        && string.CompareOrdinal(candidate, existing) < 0
                    )
                )
                {
                    map[key] = candidate;
                }
            }

            return map;
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
                // Use CancellationToken.None: this path also runs when the user cancels (ct is
                // already tripped), and passing ct would make the very first cleanup request throw
                // OperationCanceledException, silently orphaning the collections we came here to delete.
                await TryDeleteBestEffortAsync(
                    httpClient,
                    apiKey,
                    sourceCollectionId,
                    CancellationToken.None
                );
                if (outputCollectionId.HasValue)
                {
                    await TryDeleteBestEffortAsync(
                        httpClient,
                        apiKey,
                        outputCollectionId.Value,
                        CancellationToken.None
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
