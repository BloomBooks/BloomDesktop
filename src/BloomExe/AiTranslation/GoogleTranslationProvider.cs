using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Collection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bloom.AiTranslation
{
    /// <summary>
    /// AI translation provider backed by the Google Cloud Translation API,
    /// authenticated via a service account.
    /// </summary>
    internal sealed class GoogleTranslationProvider : IAiTranslationProvider
    {
        private const string kScope = "https://www.googleapis.com/auth/cloud-translation";
        private const string kTokenEndpoint = "https://oauth2.googleapis.com/token";
        private const string kTranslateEndpoint =
            "https://translation.googleapis.com/language/translate/v2";
        private const string kSupportedLanguagesEndpointTemplate =
            "https://translation.googleapis.com/v3/projects/{0}/locations/global/supportedLanguages?display_language_code=en";

        public string ProviderId => "google";
        public int MaxSegmentsPerRequest => 128;
        public int MaxRequestBytes => 100_000;

        public async Task<List<AiTranslationTargetLanguageOption>> GetSupportedTargetLanguagesAsync(
            AiTranslationEngineSettings engine,
            IReadOnlyList<string> likelySourceLanguageTags,
            HttpClient httpClient,
            CancellationToken ct
        )
        {
            // Google's target list is source-independent, so likelySourceLanguageTags is ignored.
            EnsureCredentials(engine);

            var accessToken = await GetAccessTokenAsync(engine, httpClient, ct);
            var projectId = AiTranslationService.GetGoogleProjectIdFromServiceAccountEmail(
                engine.ServiceAccountEmail
            );
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                string.Format(kSupportedLanguagesEndpointTemplate, Uri.EscapeDataString(projectId))
            );
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(
                response,
                responseContent,
                "Google Translate"
            );

            var responseJson = JObject.Parse(responseContent);
            var languages = responseJson["languages"] as JArray;
            var options = new List<AiTranslationTargetLanguageOption>();
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

                var normalizedLanguageCode = AiTranslationService.NormalizeBloomLanguageTag(
                    languageCode
                );
                var displayName =
                    languageToken["displayName"]?.Value<string>() ?? normalizedLanguageCode;
                options.Add(
                    new AiTranslationTargetLanguageOption
                    {
                        Value = normalizedLanguageCode,
                        Label = displayName,
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

            var accessToken = await GetAccessTokenAsync(engine, httpClient, ct);
            var fields = new List<KeyValuePair<string, string>>();
            foreach (var segment in segments)
            {
                fields.Add(new KeyValuePair<string, string>("q", segment));
            }
            fields.Add(new KeyValuePair<string, string>("target", targetLanguageTag));
            fields.Add(new KeyValuePair<string, string>("format", "text"));
            var normalizedSourceLanguage = AiTranslationService.NormalizeBloomLanguageTag(
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

            using var response = await httpClient.SendAsync(request, ct);
            var responseContent = await response.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(
                response,
                responseContent,
                "Google Translate"
            );

            var responseJson = JObject.Parse(responseContent);
            var translations = responseJson["data"]?["translations"] as JArray;
            if (translations == null || translations.Count != segments.Length)
            {
                throw new InvalidOperationException(
                    $"Google Translate returned {translations?.Count ?? 0} translations for {segments.Length} segments."
                );
            }

            return translations
                .Select(t =>
                    WebUtility.HtmlDecode(t["translatedText"]?.Value<string>() ?? string.Empty)
                )
                .ToArray();
        }

        private static void EnsureCredentials(AiTranslationEngineSettings engine)
        {
            if (string.IsNullOrWhiteSpace(engine.ServiceAccountEmail))
            {
                throw new InvalidOperationException(
                    "Set a Google service account email in Collection Settings > AI Source Bubbles."
                );
            }
            if (string.IsNullOrWhiteSpace(engine.PrivateKey))
            {
                throw new InvalidOperationException(
                    "Set a Google service account private key in Collection Settings > AI Source Bubbles."
                );
            }
        }

        private static async Task<string> GetAccessTokenAsync(
            AiTranslationEngineSettings engine,
            HttpClient httpClient,
            CancellationToken ct
        )
        {
            var now = DateTimeOffset.UtcNow;
            var jwtHeader = AiTranslationProviderHelpers.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(new { alg = "RS256", typ = "JWT" })
                )
            );
            var jwtPayload = AiTranslationProviderHelpers.Base64UrlEncode(
                Encoding.UTF8.GetBytes(
                    JsonConvert.SerializeObject(
                        new
                        {
                            iss = engine.ServiceAccountEmail,
                            scope = kScope,
                            aud = kTokenEndpoint,
                            iat = now.ToUnixTimeSeconds(),
                            exp = now.AddMinutes(59).ToUnixTimeSeconds(),
                        }
                    )
                )
            );
            var signingInput = $"{jwtHeader}.{jwtPayload}";
            var signedJwt = $"{signingInput}.{SignJwt(signingInput, engine.PrivateKey)}";

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

            using var tokenResponse = await httpClient.SendAsync(tokenRequest, ct);
            var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
            AiTranslationProviderHelpers.EnsureSuccess(tokenResponse, tokenContent, "Google OAuth");

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
                AiTranslationProviderHelpers.NormalizeGooglePrivateKey(privateKey).ToCharArray()
            );
            var signature = rsa.SignData(
                Encoding.UTF8.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );
            return AiTranslationProviderHelpers.Base64UrlEncode(signature);
        }
    }
}
