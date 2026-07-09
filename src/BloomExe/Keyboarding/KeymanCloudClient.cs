using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using SIL.Reporting;

namespace Bloom.Keyboarding
{
    /// <summary>
    /// One keyboard as returned by the Keyman search API. Used to populate the chooser and to
    /// pick the de-facto default (the top result by <see cref="FinalWeight"/>).
    /// </summary>
    public class KeymanKeyboardSearchResult
    {
        /// <summary>The keyboard id, e.g. "thai_kedmanee". This is what we pass to GetDownloadInfo.</summary>
        public string Id;

        /// <summary>Human-readable name, e.g. "Thai Kedmanee".</summary>
        public string Name;

        /// <summary>Recent download count for this keyboard/language match (popularity signal).</summary>
        public int Downloads;

        /// <summary>
        /// The Keyman search "finalWeight" score. There is no official "default keyboard for a
        /// language" flag, so we treat the highest finalWeight as the suggested default.
        /// </summary>
        public double FinalWeight;
    }

    /// <summary>
    /// Font information for a keyboard/language: the CSS font-family name plus the concrete font
    /// files (and the URLs to download them from). Used for both the display font and the OSK font.
    /// </summary>
    public class KeymanFontInfo
    {
        /// <summary>The font-family name to use in CSS, e.g. "Pyidaungsu".</summary>
        public string Family;

        /// <summary>The font file names, e.g. ["Pyidaungsu-Regular.ttf"].</summary>
        public List<string> FileNames = new List<string>();

        /// <summary>Absolute download URLs for each file in <see cref="FileNames"/> (same order).</summary>
        public List<string> Urls = new List<string>();
    }

    /// <summary>
    /// Everything needed to install a specific keyboard for a specific language: where to get the
    /// KeymanWeb .js stub, and any fonts the keyboard wants for display and for the on-screen keyboard.
    /// </summary>
    public class KeymanDownloadInfo
    {
        /// <summary>The keyboard id this info is for.</summary>
        public string KeyboardId;

        /// <summary>The BCP-47 language tag this info is for.</summary>
        public string LanguageTag;

        /// <summary>The keyboard version, e.g. "1.7.5".</summary>
        public string Version;

        /// <summary>The path/filename of the keyboard .js relative to the keyboard base URI.</summary>
        public string Filename;

        /// <summary>Absolute URL of the keyboard's KeymanWeb .js (keyboardBaseUri + filename).</summary>
        public string JsUrl;

        /// <summary>Font for displaying text in this language, or null if the keyboard specifies none.</summary>
        public KeymanFontInfo FontInfo;

        /// <summary>Font for the on-screen keyboard, or null if the keyboard specifies none.</summary>
        public KeymanFontInfo OskFontInfo;
    }

    /// <summary>
    /// Talks to the public Keyman cloud API to (a) search for keyboards that support a language and
    /// (b) get the download metadata for a specific keyboard. Network failures are treated as
    /// "we're offline": we return empty/null quickly and let callers stay silent. Programmer errors
    /// (bad arguments) still throw.
    ///
    /// The JSON parsing is deliberately factored into pure static methods (string -> result) so it
    /// can be unit-tested against canned fixtures without touching the network.
    /// </summary>
    public class KeymanCloudClient
    {
        // A single shared HttpClient is the recommended pattern; reusing it avoids socket exhaustion.
        // 5s timeout: metadata queries are best-effort enrichment, so we fail fast to "offline"
        // rather than making the user wait.
        private static HttpClient s_httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5),
        };

        // A separate client for downloading the (larger) keyboard .js and font files. These are worth
        // waiting a bit longer for, since a successful download means the collection can type offline
        // from then on.
        private static HttpClient s_downloadHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        /// <summary>
        /// Test seam: lets tests inject an HttpClient backed by a fake handler (e.g. to simulate a
        /// network failure and exercise the offline path). Sets both the metadata and download clients.
        /// </summary>
        internal static void SetHttpClientForTests(HttpClient client)
        {
            s_httpClient = client;
            s_downloadHttpClient = client;
        }

        /// <summary>
        /// Search the Keyman cloud for keyboards that support the given language, ordered by
        /// popularity (finalWeight) descending, so the first result is the de-facto default.
        /// Returns an empty list if we're offline or the request fails.
        /// </summary>
        /// <param name="bcp47">The BCP-47 language tag, e.g. "th". Must not be null/empty.</param>
        public List<KeymanKeyboardSearchResult> SearchKeyboardsForLanguage(string bcp47)
        {
            if (string.IsNullOrWhiteSpace(bcp47))
                throw new ArgumentException("A language tag is required.", nameof(bcp47));

            var url = "https://api.keyman.com/search/2.0?q=l:id:" + Uri.EscapeDataString(bcp47);
            var json = TryGetString(url);
            if (json == null)
                return new List<KeymanKeyboardSearchResult>();
            return ParseSearchResults(json);
        }

        /// <summary>
        /// Get the download metadata (js URL and fonts) for a specific keyboard and language.
        /// Returns null if we're offline, the request fails, or the keyboard/language isn't found.
        /// </summary>
        /// <param name="keyboardId">The keyboard id, e.g. "sil_myanmar_my3". Must not be null/empty.</param>
        /// <param name="bcp47">The BCP-47 language tag, e.g. "my". Must not be null/empty.</param>
        public KeymanDownloadInfo GetDownloadInfo(string keyboardId, string bcp47)
        {
            if (string.IsNullOrWhiteSpace(keyboardId))
                throw new ArgumentException("A keyboard id is required.", nameof(keyboardId));
            if (string.IsNullOrWhiteSpace(bcp47))
                throw new ArgumentException("A language tag is required.", nameof(bcp47));

            var url =
                "https://api.keyman.com/cloud/4.0/keyboards/"
                + Uri.EscapeDataString(keyboardId)
                + "/"
                + Uri.EscapeDataString(bcp47)
                + "?languageidtype=bcp47";
            var json = TryGetString(url);
            if (json == null)
                return null;
            return ParseDownloadInfo(json, keyboardId, bcp47);
        }

        /// <summary>
        /// Download the raw bytes at a URL (used for keyboard .js and font files), or null on any
        /// failure. Like the metadata requests, network problems are treated as "offline", not errors.
        /// </summary>
        /// <param name="url">The absolute URL to download. Must not be null/empty.</param>
        public byte[] DownloadBytes(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("A url is required.", nameof(url));
            try
            {
                var response = s_downloadHttpClient.GetAsync(url).Result;
                if (!response.IsSuccessStatusCode)
                    return null;
                return response.Content.ReadAsByteArrayAsync().Result;
            }
            catch (Exception e)
            {
                Logger.WriteMinorEvent(
                    "KeymanCloudClient download failed for " + url + ": " + e.Message
                );
                return null;
            }
        }

        /// <summary>
        /// GET a URL and return the body, or null on any failure (timeout, offline, non-success
        /// status). We never throw on network problems: callers treat null as "offline".
        /// </summary>
        private static string TryGetString(string url)
        {
            try
            {
                var response = s_httpClient.GetAsync(url).Result;
                if (!response.IsSuccessStatusCode)
                    return null;
                return response.Content.ReadAsStringAsync().Result;
            }
            catch (Exception e)
            {
                // Offline or transient failure. This is expected and non-fatal; log at a low level
                // for diagnostics but stay silent to the user.
                Logger.WriteMinorEvent(
                    "KeymanCloudClient request failed for " + url + ": " + e.Message
                );
                return null;
            }
        }

        /// <summary>
        /// Pure parser (string -> results) for the Keyman search API response. Extracted so it can
        /// be unit-tested against canned fixtures. Results are ordered by finalWeight descending.
        /// Malformed JSON throws (that's a bug, not an offline condition).
        /// </summary>
        public static List<KeymanKeyboardSearchResult> ParseSearchResults(string json)
        {
            var results = new List<KeymanKeyboardSearchResult>();
            var root = JObject.Parse(json);
            var keyboards = root["keyboards"] as JArray;
            if (keyboards == null)
                return results;

            foreach (var kb in keyboards)
            {
                var id = (string)kb["id"];
                if (string.IsNullOrEmpty(id))
                    continue; // an entry with no id is useless to us
                var match = kb["match"];
                results.Add(
                    new KeymanKeyboardSearchResult
                    {
                        Id = id,
                        Name = (string)kb["name"] ?? id,
                        Downloads = (int?)match?["downloads"] ?? 0,
                        FinalWeight = (double?)match?["finalWeight"] ?? 0.0,
                    }
                );
            }

            return results.OrderByDescending(r => r.FinalWeight).ToList();
        }

        /// <summary>
        /// Pure parser (string -> result) for the Keyman keyboard-download API response. Extracted
        /// so it can be unit-tested against canned fixtures. Returns null if the response is the
        /// "Keyboard not found" error or lacks the keyboard block. Malformed JSON throws.
        /// </summary>
        /// <param name="keyboardId">The keyboard id we asked about (recorded on the result).</param>
        /// <param name="bcp47">The language tag we asked about; used to pick the language block.</param>
        public static KeymanDownloadInfo ParseDownloadInfo(
            string json,
            string keyboardId,
            string bcp47
        )
        {
            var root = JObject.Parse(json);
            // The API signals a miss with {"message": "Keyboard not found"} and no keyboard block.
            var keyboard = root["keyboard"] as JObject;
            if (keyboard == null)
                return null;

            var options = root["options"] as JObject;
            var keyboardBaseUri = (string)options?["keyboardBaseUri"] ?? "";
            var fontBaseUri = (string)options?["fontBaseUri"] ?? "";

            var filename = (string)keyboard["filename"];
            var info = new KeymanDownloadInfo
            {
                KeyboardId = keyboardId,
                LanguageTag = bcp47,
                Version = (string)keyboard["version"],
                Filename = filename,
                JsUrl = string.IsNullOrEmpty(filename) ? null : keyboardBaseUri + filename,
            };

            // Fonts are specified per language. Prefer the block whose id matches the language we
            // asked for; fall back to the first language block if there's no exact match.
            var languages = keyboard["languages"] as JArray;
            var langBlock =
                languages?.FirstOrDefault(l =>
                    string.Equals((string)l["id"], bcp47, StringComparison.OrdinalIgnoreCase)
                ) ?? languages?.FirstOrDefault();

            if (langBlock != null)
            {
                info.FontInfo = ParseFontInfo(langBlock["font"] as JObject, fontBaseUri);
                info.OskFontInfo = ParseFontInfo(langBlock["oskFont"] as JObject, fontBaseUri);
            }

            return info;
        }

        /// <summary>
        /// Build a <see cref="KeymanFontInfo"/> from a font/oskFont JSON block, resolving each
        /// source file name to an absolute URL under the font base URI. Returns null if the block
        /// is absent.
        /// </summary>
        private static KeymanFontInfo ParseFontInfo(JObject fontBlock, string fontBaseUri)
        {
            if (fontBlock == null)
                return null;

            var result = new KeymanFontInfo { Family = (string)fontBlock["family"] };
            var source = fontBlock["source"] as JArray;
            if (source != null)
            {
                foreach (var s in source)
                {
                    var fileName = (string)s;
                    if (string.IsNullOrEmpty(fileName))
                        continue;
                    result.FileNames.Add(fileName);
                    result.Urls.Add(fontBaseUri + fileName);
                }
            }

            return result;
        }
    }
}
