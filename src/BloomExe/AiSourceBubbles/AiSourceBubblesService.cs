using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Bloom.Collection;
using Bloom.SubscriptionAndFeatures;
using Bloom.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.WritingSystems;

namespace Bloom.AiSourceBubbles
{
    /// <summary>
    /// Request payload for translating a single source bubble.
    /// </summary>
    public class AiSourceBubblesTranslateRequest
    {
        public string SourceText { get; set; }
        public string SourceLanguageTag { get; set; }
    }

    /// <summary>
    /// Response payload for translating a single source bubble.
    /// </summary>
    public class AiSourceBubblesTranslateResponse
    {
        public string ProviderId { get; set; }
        public string TargetLanguageTag { get; set; }
        public string AiLanguageTag { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// Coordinates collection-backed AI Source Bubbles translation.
    /// </summary>
    public class AiSourceBubblesService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly CollectionSettings _collectionSettings;
        private readonly Dictionary<string, IAiSourceBubblesTranslationProvider> _providers;

        public AiSourceBubblesService(CollectionSettings collectionSettings)
        {
            _collectionSettings = collectionSettings;
            _providers = new Dictionary<string, IAiSourceBubblesTranslationProvider>(
                StringComparer.OrdinalIgnoreCase
            )
            {
                { "deepl", new DeepLAiSourceBubblesTranslationProvider() },
                { "google", new GoogleAiSourceBubblesTranslationProvider() },
            };
        }

        /// <summary>
        /// Translates one source-bubble text block using the provider configured on the current collection.
        /// </summary>
        public async Task<AiSourceBubblesTranslateResponse> TranslateAsync(
            AiSourceBubblesTranslateRequest request
        )
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.SourceText))
                throw new ArgumentException("Source text is required.", nameof(request));

            var featureStatus = FeatureStatus.GetFeatureStatus(
                _collectionSettings.Subscription,
                FeatureName.AiSourceBubbles
            );
            if (!featureStatus.Visible || !featureStatus.Enabled)
            {
                throw new InvalidOperationException(
                    "AI Source Bubbles is not enabled for this collection."
                );
            }

            var provider = GetSelectedProvider();
            var targetLanguageTag = NormalizeBloomLanguageTag(
                _collectionSettings.AiSourceBubblesTargetLanguageTag
            );
            if (string.IsNullOrWhiteSpace(targetLanguageTag))
            {
                throw new InvalidOperationException(
                    "Set a target language tag in Collection Settings > AI Source Bubbles."
                );
            }

            var translatedText = await provider.TranslateAsync(
                _collectionSettings,
                request.SourceText,
                request.SourceLanguageTag,
                targetLanguageTag,
                _httpClient
            );

            return new AiSourceBubblesTranslateResponse
            {
                ProviderId = provider.ProviderId,
                TargetLanguageTag = targetLanguageTag,
                AiLanguageTag = GetAiLanguageTag(targetLanguageTag, provider.ProviderId),
                Text = translatedText,
            };
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
            return normalized == "googletranslate" ? "google" : normalized;
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

        private IAiSourceBubblesTranslationProvider GetSelectedProvider()
        {
            var providerId = NormalizeProviderId(_collectionSettings.AiSourceBubblesProviderId);
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new InvalidOperationException(
                    "Select an AI Source Bubbles provider in Collection Settings."
                );
            }

            if (!_providers.TryGetValue(providerId, out var provider))
            {
                throw new InvalidOperationException($"Unsupported AI provider '{providerId}'.");
            }

            return provider;
        }
    }

    internal interface IAiSourceBubblesTranslationProvider
    {
        string ProviderId { get; }

        Task<string> TranslateAsync(
            CollectionSettings collectionSettings,
            string sourceText,
            string sourceLanguageTag,
            string targetLanguageTag,
            HttpClient httpClient
        );
    }

    internal sealed class DeepLAiSourceBubblesTranslationProvider
        : IAiSourceBubblesTranslationProvider
    {
        public string ProviderId => "deepl";

        public async Task<string> TranslateAsync(
            CollectionSettings collectionSettings,
            string sourceText,
            string sourceLanguageTag,
            string targetLanguageTag,
            HttpClient httpClient
        )
        {
            if (string.IsNullOrWhiteSpace(collectionSettings.AiSourceBubblesDeepLApiKey))
            {
                throw new InvalidOperationException(
                    "Set a DeepL API key in Collection Settings > AI Source Bubbles."
                );
            }

            var requestBody = new
            {
                text = new[] { sourceText },
                source_lang = NormalizeDeepLLanguageTag(sourceLanguageTag),
                target_lang = NormalizeDeepLLanguageTag(targetLanguageTag),
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                GetEndpoint(collectionSettings.AiSourceBubblesDeepLApiKey)
            );
            request.Content = new StringContent(
                JsonConvert.SerializeObject(requestBody),
                Encoding.UTF8,
                "application/json"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "DeepL-Auth-Key",
                collectionSettings.AiSourceBubblesDeepLApiKey.Trim()
            );

            using var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiSourceBubblesProviderHelpers.EnsureSuccess(response, responseContent, "DeepL");

            var responseJson = JObject.Parse(responseContent);
            var translatedText = responseJson["translations"]?[0]?["text"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                throw new InvalidOperationException("DeepL returned no translated text.");
            }

            return translatedText;
        }

        private static string GetEndpoint(string apiKey)
        {
            return apiKey.Trim().EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
                ? "https://api-free.deepl.com/v2/translate"
                : "https://api.deepl.com/v2/translate";
        }

        private static string NormalizeDeepLLanguageTag(string languageTag)
        {
            return AiSourceBubblesService
                .NormalizeBloomLanguageTag(languageTag)
                .Replace('_', '-')
                .ToUpperInvariant();
        }
    }

    internal sealed class GoogleAiSourceBubblesTranslationProvider
        : IAiSourceBubblesTranslationProvider
    {
        private const string kScope = "https://www.googleapis.com/auth/cloud-translation";
        private const string kTokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string kTranslateEndpoint =
            "https://translation.googleapis.com/language/translate/v2";

        public string ProviderId => "google";

        public async Task<string> TranslateAsync(
            CollectionSettings collectionSettings,
            string sourceText,
            string sourceLanguageTag,
            string targetLanguageTag,
            HttpClient httpClient
        )
        {
            if (
                string.IsNullOrWhiteSpace(
                    collectionSettings.AiSourceBubblesGoogleServiceAccountEmail
                )
            )
            {
                throw new InvalidOperationException(
                    "Set a Google service account email in Collection Settings > AI Source Bubbles."
                );
            }
            if (string.IsNullOrWhiteSpace(collectionSettings.AiSourceBubblesGooglePrivateKey))
            {
                throw new InvalidOperationException(
                    "Set a Google service account private key in Collection Settings > AI Source Bubbles."
                );
            }

            var accessToken = await GetAccessTokenAsync(collectionSettings, httpClient);
            var fields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("q", sourceText),
                new KeyValuePair<string, string>("target", targetLanguageTag),
                new KeyValuePair<string, string>("format", "text"),
            };
            var normalizedSourceLanguage = AiSourceBubblesService.NormalizeBloomLanguageTag(
                sourceLanguageTag
            );
            if (!string.IsNullOrWhiteSpace(normalizedSourceLanguage))
            {
                fields.Add(new KeyValuePair<string, string>("source", normalizedSourceLanguage));
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, kTranslateEndpoint)
            {
                Content = new FormUrlEncodedContent(fields),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiSourceBubblesProviderHelpers.EnsureSuccess(
                response,
                responseContent,
                "Google Translate"
            );

            var responseJson = JObject.Parse(responseContent);
            var translatedText = responseJson["data"]
                ?["translations"]?[0]?["translatedText"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(translatedText))
            {
                throw new InvalidOperationException(
                    "Google Translate returned no translated text."
                );
            }

            return WebUtility.HtmlDecode(translatedText);
        }

        private static async Task<string> GetAccessTokenAsync(
            CollectionSettings collectionSettings,
            HttpClient httpClient
        )
        {
            var now = DateTimeOffset.UtcNow;
            var jwtHeader = AiSourceBubblesProviderHelpers.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(new { alg = "RS256", typ = "JWT" })
                )
            );
            var jwtPayload = AiSourceBubblesProviderHelpers.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(
                        new
                        {
                            iss = collectionSettings.AiSourceBubblesGoogleServiceAccountEmail,
                            scope = kScope,
                            aud = kTokenEndpoint,
                            iat = now.ToUnixTimeSeconds(),
                            exp = now.AddMinutes(59).ToUnixTimeSeconds(),
                        }
                    )
                )
            );
            var signingInput = $"{jwtHeader}.{jwtPayload}";
            var signedJwt =
                $"{signingInput}.{SignJwt(signingInput, collectionSettings.AiSourceBubblesGooglePrivateKey)}";

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, kTokenEndpoint)
            {
                Content = new FormUrlEncodedContent(
                    new[]
                    {
                        new KeyValuePair<string, string>(
                            "grant_type",
                            "urn:ietf:params:oauth:grant-type:jwt-bearer"
                        ),
                        new KeyValuePair<string, string>("assertion", signedJwt),
                    }
                ),
            };

            using var tokenResponse = await httpClient.SendAsync(tokenRequest);
            var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
            AiSourceBubblesProviderHelpers.EnsureSuccess(
                tokenResponse,
                tokenContent,
                "Google OAuth"
            );

            var tokenJson = JObject.Parse(tokenContent);
            var accessToken = tokenJson["access_token"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("Google OAuth returned no access token.");
            }

            return accessToken;
        }

        private static string SignJwt(string signingInput, string privateKey)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(NormalizePrivateKey(privateKey).ToCharArray());
            var signature = rsa.SignData(
                Encoding.UTF8.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            return AiSourceBubblesProviderHelpers.Base64UrlEncode(signature);
        }

        private static string NormalizePrivateKey(string privateKey)
        {
            return privateKey.Replace("\\r", "").Replace("\\n", "\n").Trim();
        }
    }

    internal static class AiSourceBubblesProviderHelpers
    {
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
