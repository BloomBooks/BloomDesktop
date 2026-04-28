using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    /// Result of validating the current AI Source Bubbles configuration.
    /// </summary>
    public class AiSourceBubblesValidationResult
    {
        public bool Succeeded { get; set; }
        public string ConfigurationFingerprint { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Option surfaced to the Collection Settings target-language picker.
    /// </summary>
    public class AiSourceBubblesTargetLanguageOption
    {
        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }
    }

    /// <summary>
    /// Coordinates collection-backed AI Source Bubbles translation.
    /// </summary>
    public class AiSourceBubblesService
    {
        public const string kValidationProbeText = "Today a reader, tomorrow a leader.";
        public const string kValidationProbeSourceLanguageTag = "en";

        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly CollectionSettings _collectionSettings;
        private readonly Dictionary<string, IAiSourceBubblesTranslationProvider> _providers;

        public AiSourceBubblesService(CollectionSettings collectionSettings)
            : this(collectionSettings, null) { }

        internal AiSourceBubblesService(
            CollectionSettings collectionSettings,
            Dictionary<string, IAiSourceBubblesTranslationProvider> providers
        )
        {
            _collectionSettings = collectionSettings;
            _providers =
                providers
                ?? new Dictionary<string, IAiSourceBubblesTranslationProvider>(
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
            return await TranslateAsync(request, true);
        }

        /// <summary>
        /// Validates the configured provider, credentials, and target language with a probe translation.
        /// </summary>
        public async Task<AiSourceBubblesValidationResult> ValidateConfigurationAsync()
        {
            var response = await TranslateAsync(
                new AiSourceBubblesTranslateRequest
                {
                    SourceText = kValidationProbeText,
                    SourceLanguageTag = kValidationProbeSourceLanguageTag,
                },
                false
            );

            return new AiSourceBubblesValidationResult
            {
                Succeeded = true,
                ConfigurationFingerprint = GetConfigurationFingerprint(_collectionSettings),
                Message = response.Text,
            };
        }

        /// <summary>
        /// Gets the target languages currently supported by the configured provider.
        /// </summary>
        public async Task<
            List<AiSourceBubblesTargetLanguageOption>
        > GetSupportedTargetLanguagesAsync()
        {
            var provider = GetSelectedProvider();
            return await provider.GetSupportedTargetLanguagesAsync(
                _collectionSettings,
                _httpClient
            );
        }

        private async Task<AiSourceBubblesTranslateResponse> TranslateAsync(
            AiSourceBubblesTranslateRequest request,
            bool requireFeatureEnabled
        )
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.SourceText))
                throw new ArgumentException("Source text is required.", nameof(request));

            if (requireFeatureEnabled)
            {
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

            var startedAt = DateTimeOffset.Now;
            var stopwatch = Stopwatch.StartNew();
            WriteTranslationActivity(
                "request",
                startedAt,
                provider.ProviderId,
                request.SourceLanguageTag,
                targetLanguageTag,
                request.SourceText
            );

            try
            {
                var translatedText = await provider.TranslateAsync(
                    _collectionSettings,
                    request.SourceText,
                    request.SourceLanguageTag,
                    targetLanguageTag,
                    _httpClient
                );
                stopwatch.Stop();
                WriteTranslationActivity(
                    "response",
                    DateTimeOffset.Now,
                    provider.ProviderId,
                    request.SourceLanguageTag,
                    targetLanguageTag,
                    request.SourceText,
                    translatedText,
                    stopwatch.Elapsed
                );

                return new AiSourceBubblesTranslateResponse
                {
                    ProviderId = provider.ProviderId,
                    TargetLanguageTag = targetLanguageTag,
                    AiLanguageTag = GetAiLanguageTag(targetLanguageTag, provider.ProviderId),
                    Text = translatedText,
                };
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                Console.WriteLine(
                    $"[AiSourceBubbles][response] time={DateTimeOffset.Now:O} provider={provider.ProviderId} sourceLanguage={request.SourceLanguageTag} targetLanguage={targetLanguageTag} elapsedMs={stopwatch.ElapsedMilliseconds} input={JsonConvert.ToString(request.SourceText)} error={JsonConvert.ToString(exception.Message)}"
                );
                throw;
            }
        }

        private static void WriteTranslationActivity(
            string stage,
            DateTimeOffset time,
            string providerId,
            string sourceLanguageTag,
            string targetLanguageTag,
            string sourceText,
            string translatedText = null,
            TimeSpan? elapsed = null
        )
        {
            var logLine =
                $"[AiSourceBubbles][{stage}] time={time:O} provider={providerId} sourceLanguage={sourceLanguageTag} targetLanguage={targetLanguageTag} input={JsonConvert.ToString(sourceText)}";

            if (translatedText != null)
            {
                logLine += $" output={JsonConvert.ToString(translatedText)}";
            }

            if (elapsed.HasValue)
            {
                logLine += $" elapsedMs={elapsed.Value.TotalMilliseconds:F0}";
            }

            Console.WriteLine(logLine);
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
                "alpha-2" => string.Empty,
                "alpha2" => string.Empty,
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
        /// Builds a stable fingerprint of the AI Source Bubbles configuration without storing raw secrets.
        /// </summary>
        public static string GetConfigurationFingerprint(CollectionSettings collectionSettings)
        {
            if (collectionSettings == null)
                throw new ArgumentNullException(nameof(collectionSettings));

            return GetConfigurationFingerprint(
                collectionSettings.AiSourceBubblesProviderId,
                collectionSettings.AiSourceBubblesTargetLanguageTag,
                collectionSettings.AiSourceBubblesDeepLApiKey,
                collectionSettings.AiSourceBubblesGoogleServiceAccountEmail,
                collectionSettings.AiSourceBubblesGooglePrivateKey
            );
        }

        /// <summary>
        /// Builds a stable fingerprint of provider, target language, and provider-specific credentials.
        /// </summary>
        public static string GetConfigurationFingerprint(
            string providerId,
            string targetLanguageTag,
            string deepLApiKey,
            string googleServiceAccountEmail,
            string googlePrivateKey
        )
        {
            var normalizedProvider = NormalizeProviderId(providerId);
            var normalizedTargetLanguageTag = NormalizeBloomLanguageTag(targetLanguageTag);
            var credentialKey = normalizedProvider switch
            {
                "google" =>
                    $"{googleServiceAccountEmail?.Trim()}\n{AiSourceBubblesProviderHelpers.NormalizeGooglePrivateKey(googlePrivateKey)}",
                _ => deepLApiKey?.Trim() ?? string.Empty,
            };
            var fingerprintInput =
                $"{normalizedProvider}\n{normalizedTargetLanguageTag}\n{credentialKey}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintInput)));
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

        Task<List<AiSourceBubblesTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
            CollectionSettings collectionSettings,
            HttpClient httpClient
        );

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

        public async Task<
            List<AiSourceBubblesTargetLanguageOption>
        > GetSupportedTargetLanguagesAsync(
            CollectionSettings collectionSettings,
            HttpClient httpClient
        )
        {
            if (string.IsNullOrWhiteSpace(collectionSettings.AiSourceBubblesDeepLApiKey))
            {
                throw new InvalidOperationException(
                    "Set a DeepL API key in Collection Settings > AI Source Bubbles."
                );
            }

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                GetApiBaseUrl(collectionSettings.AiSourceBubblesDeepLApiKey)
                    + "/v2/languages?type=target"
            );
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "DeepL-Auth-Key",
                collectionSettings.AiSourceBubblesDeepLApiKey.Trim()
            );

            using var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiSourceBubblesProviderHelpers.EnsureSuccess(response, responseContent, "DeepL");

            var languages = JArray.Parse(responseContent);
            var options = new List<AiSourceBubblesTargetLanguageOption>();
            foreach (var languageToken in languages)
            {
                var languageCode = languageToken["language"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(languageCode))
                {
                    continue;
                }

                var normalizedLanguageCode = AiSourceBubblesService.NormalizeBloomLanguageTag(
                    languageCode
                );
                var name = languageToken["name"]?.Value<string>() ?? normalizedLanguageCode;
                options.Add(
                    new AiSourceBubblesTargetLanguageOption
                    {
                        Value = normalizedLanguageCode,
                        Label = name,
                    }
                );
            }

            options.Sort(
                (first, second) =>
                    StringComparer.CurrentCultureIgnoreCase.Compare(first.Label, second.Label)
            );
            return options;
        }

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
        private const string kSupportedLanguagesEndpointTemplate =
            "https://translation.googleapis.com/v3/projects/{0}/locations/global/supportedLanguages?display_language_code=en";

        public string ProviderId => "google";

        public async Task<
            List<AiSourceBubblesTargetLanguageOption>
        > GetSupportedTargetLanguagesAsync(
            CollectionSettings collectionSettings,
            HttpClient httpClient
        )
        {
            EnsureGoogleCredentials(collectionSettings);

            var accessToken = await GetAccessTokenAsync(collectionSettings, httpClient);
            var projectId = AiSourceBubblesService.GetGoogleProjectIdFromServiceAccountEmail(
                collectionSettings.AiSourceBubblesGoogleServiceAccountEmail
            );
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                string.Format(kSupportedLanguagesEndpointTemplate, Uri.EscapeDataString(projectId))
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiSourceBubblesProviderHelpers.EnsureSuccess(
                response,
                responseContent,
                "Google Translate"
            );

            var responseJson = JObject.Parse(responseContent);
            var languages = responseJson["languages"] as JArray;
            var options = new List<AiSourceBubblesTargetLanguageOption>();
            if (languages == null)
            {
                return options;
            }

            foreach (var languageToken in languages)
            {
                var supportsTarget = languageToken["supportTarget"]?.Value<bool>() ?? false;
                if (!supportsTarget)
                {
                    continue;
                }

                var languageCode = languageToken["languageCode"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(languageCode))
                {
                    continue;
                }

                var normalizedLanguageCode = AiSourceBubblesService.NormalizeBloomLanguageTag(
                    languageCode
                );
                var displayName =
                    languageToken["displayName"]?.Value<string>() ?? normalizedLanguageCode;
                options.Add(
                    new AiSourceBubblesTargetLanguageOption
                    {
                        Value = normalizedLanguageCode,
                        Label = displayName,
                    }
                );
            }

            options.Sort(
                (first, second) =>
                    StringComparer.CurrentCultureIgnoreCase.Compare(first.Label, second.Label)
            );
            return options;
        }

        public async Task<string> TranslateAsync(
            CollectionSettings collectionSettings,
            string sourceText,
            string sourceLanguageTag,
            string targetLanguageTag,
            HttpClient httpClient
        )
        {
            EnsureGoogleCredentials(collectionSettings);

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

        private static void EnsureGoogleCredentials(CollectionSettings collectionSettings)
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
            rsa.ImportFromPem(
                AiSourceBubblesProviderHelpers.NormalizeGooglePrivateKey(privateKey).ToCharArray()
            );
            var signature = rsa.SignData(
                Encoding.UTF8.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            return AiSourceBubblesProviderHelpers.Base64UrlEncode(signature);
        }
    }

    internal static class AiSourceBubblesProviderHelpers
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
