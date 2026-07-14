using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Collection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bloom.AiTranslation
{
    /// <summary>
    /// AI translation provider backed by the DeepL API.
    /// </summary>
    internal sealed class DeepLTranslationProvider : IAiTranslationProvider
    {
        public string ProviderId => "deepl";
        public int MaxSegmentsPerRequest => 50;
        public int MaxRequestBytes => 120_000;

        public async Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
            AiTranslationEngineSettings engine,
            HttpClient httpClient,
            CancellationToken ct
        )
        {
            EnsureCredentials(engine);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                GetApiBaseUrl(engine.ApiKey) + "/v2/languages?type=target"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "DeepL-Auth-Key",
                engine.ApiKey.Trim()
            );

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(response, responseContent, "DeepL");

            var languages = JArray.Parse(responseContent);
            var options = new List<AiTranslationTargetLanguageOption>();
            foreach (var languageToken in languages)
            {
                var languageCode = languageToken["language"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(languageCode))
                {
                    continue;
                }

                var normalizedLanguageCode = AiTranslationService.NormalizeBloomLanguageTag(
                    languageCode
                );
                var name = languageToken["name"]?.Value<string>() ?? normalizedLanguageCode;
                options.Add(
                    new AiTranslationTargetLanguageOption
                    {
                        Value = normalizedLanguageCode,
                        Label = name,
                        ProviderIds = new List<string> { ProviderId },
                    }
                );
            }

            options.Sort(
                (first, second) =>
                    StringComparer.CurrentCultureIgnoreCase.Compare(first.Label, second.Label)
            );
            return options;
        }

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

            var requestBody = new
            {
                text = segments,
                source_lang = NormalizeDeepLLanguageTag(sourceLanguageTag),
                target_lang = NormalizeDeepLLanguageTag(targetLanguageTag),
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, GetEndpoint(engine.ApiKey));
            request.Content = new StringContent(
                JsonConvert.SerializeObject(requestBody),
                Encoding.UTF8,
                "application/json"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "DeepL-Auth-Key",
                engine.ApiKey.Trim()
            );

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(response, responseContent, "DeepL");

            var responseJson = JObject.Parse(responseContent);
            var translations = responseJson["translations"] as JArray;
            if (translations == null || translations.Count != segments.Length)
            {
                throw new InvalidOperationException(
                    $"DeepL returned {translations?.Count ?? 0} translations for {segments.Length} segments."
                );
            }

            return translations.Select(t => t["text"]?.Value<string>() ?? string.Empty).ToArray();
        }

        private static void EnsureCredentials(AiTranslationEngineSettings engine)
        {
            if (string.IsNullOrWhiteSpace(engine.ApiKey))
            {
                throw new InvalidOperationException(
                    "Set a DeepL API key in Collection Settings > AI Source Bubbles."
                );
            }
        }

        private static string GetEndpoint(string apiKey)
        {
            return GetApiBaseUrl(apiKey) + "/v2/translate";
        }

        private static string GetApiBaseUrl(string apiKey)
        {
            return apiKey.Trim().EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
                ? "https://api-free.deepl.com"
                : "https://api.deepl.com";
        }

        private static string NormalizeDeepLLanguageTag(string languageTag)
        {
            return AiTranslationService
                .NormalizeBloomLanguageTag(languageTag)
                .Replace('_', '-')
                .ToUpperInvariant();
        }
    }
}
