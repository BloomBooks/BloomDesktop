using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Bloom.AiTranslation
{
    /// <summary>
    /// Option surfaced to the Collection Settings target-language picker.
    /// </summary>
    public class AiTranslationTargetLanguageOption
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        /// <summary>
        /// The ids of the (enabled) providers that were found to support this target language.
        /// </summary>
        [JsonProperty("providerIds")]
        public List<string> ProviderIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// Contract implemented by each AI translation backend (DeepL, Google, Alpha2, ...). All
    /// translation is batched: callers pass an array of text segments and get back translations
    /// in the same order, chunked to fit this provider's per-request limits.
    /// </summary>
    internal interface IAiTranslationProvider
    {
        string ProviderId { get; }

        /// <summary>
        /// Max number of text segments per HTTP request (DeepL: 50, Google: 128).
        /// </summary>
        int MaxSegmentsPerRequest { get; }

        /// <summary>
        /// Conservative max total UTF-8 bytes of segment text per request (DeepL: 120_000, Google: 100_000).
        /// </summary>
        int MaxRequestBytes { get; }

        Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
            AiTranslationEngineSettings engine,
            HttpClient httpClient,
            CancellationToken ct
        );

        /// <summary>
        /// Translates segments preserving order and count; implementations must throw if the
        /// service returns a different number of translations than segments.
        /// </summary>
        Task<string[]> TranslateBatchAsync(
            AiTranslationEngineSettings engine,
            string[] segments,
            string sourceLanguageTag,
            string targetLanguageTag,
            HttpClient httpClient,
            CancellationToken ct
        );
    }
}
