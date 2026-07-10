using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SIL.IO;

namespace Bloom.Keyboarding
{
    /// <summary>
    /// The on-disk manifest (`&lt;id&gt;.json`) describing a cached keyboard: enough to register it
    /// with KeymanWeb offline and to know which local files back it. Serialized with Newtonsoft.
    /// </summary>
    public class KeyboardCacheManifest
    {
        /// <summary>The keyboard id, e.g. "sil_myanmar_my3".</summary>
        public string Id;

        /// <summary>The BCP-47 language tag this keyboard was cached for, e.g. "my".</summary>
        public string LanguageTag;

        /// <summary>The keyboard version, e.g. "1.7.5".</summary>
        public string Version;

        /// <summary>The original cloud URL the keyboard .js was downloaded from (for diagnostics/refresh).</summary>
        public string JsUrl;

        /// <summary>The display font-family name, or null if the keyboard specifies none.</summary>
        public string FontFamily;

        /// <summary>The display font file names stored in the `fonts/` subfolder.</summary>
        public List<string> FontFiles = new List<string>();

        /// <summary>The on-screen-keyboard font-family name, or null if the keyboard specifies none.</summary>
        public string OskFontFamily;

        /// <summary>The OSK font file names stored in the `fonts/` subfolder.</summary>
        public List<string> OskFontFiles = new List<string>();
    }

    /// <summary>
    /// Manages the per-collection cache of KeymanWeb keyboards so typing works offline. Layout under
    /// the collection folder:
    /// <code>
    ///   .keyboards/
    ///     &lt;id&gt;.js       -- the KeymanWeb keyboard stub
    ///     &lt;id&gt;.json     -- the KeyboardCacheManifest
    ///     fonts/          -- downloaded font files (ttf/woff) referenced by manifests
    /// </code>
    /// The <c>.keyboards/</c> folder is a per-machine local cache and is deliberately NOT synced via
    /// Team Collections (the leading dot marks it as local-only): each machine acquires keyboards on
    /// demand, so a teammate does not need the lead's cached files. Downloads are written with a
    /// temp-file-then-rename dance so a crash mid-download never leaves a half-written file that
    /// looks valid, and the manifest is written LAST so its presence implies the other files exist.
    /// </summary>
    public class CollectionKeyboardCache
    {
        private readonly string _keyboardsFolder;
        private readonly KeymanCloudClient _client;

        /// <summary>
        /// Create a cache rooted at the given collection folder. The <c>.keyboards/</c> subfolder is
        /// created lazily on first download.
        /// </summary>
        /// <param name="collectionFolder">The collection folder that will hold <c>.keyboards/</c>.</param>
        /// <param name="client">The Keyman cloud client to fetch metadata/files with; a default is
        /// created if null.</param>
        public CollectionKeyboardCache(string collectionFolder, KeymanCloudClient client = null)
        {
            if (string.IsNullOrEmpty(collectionFolder))
                throw new ArgumentException(
                    "A collection folder is required.",
                    nameof(collectionFolder)
                );
            _keyboardsFolder = Path.Combine(collectionFolder, ".keyboards");
            _client = client ?? new KeymanCloudClient();
        }

        /// <summary>The absolute path of the <c>.keyboards/</c> folder.</summary>
        public string KeyboardsFolder => _keyboardsFolder;

        /// <summary>
        /// Ensure the given keyboard is downloaded into the cache. If a valid manifest and all its
        /// files are already present, this is a no-op and returns the existing manifest. Otherwise it
        /// fetches metadata and downloads the .js and fonts (atomically). Returns null if we're
        /// offline or any part of the download fails (callers stay silent and retry later).
        /// </summary>
        /// <param name="keyboardId">The keyboard id to cache, e.g. "sil_myanmar_my3".</param>
        /// <param name="bcp47">The BCP-47 language tag, e.g. "my".</param>
        public KeyboardCacheManifest EnsureDownloaded(string keyboardId, string bcp47)
        {
            if (string.IsNullOrWhiteSpace(keyboardId))
                throw new ArgumentException("A keyboard id is required.", nameof(keyboardId));
            if (string.IsNullOrWhiteSpace(bcp47))
                throw new ArgumentException("A language tag is required.", nameof(bcp47));

            var existing = TryGetInfo(keyboardId);
            if (existing != null && AllFilesPresent(existing))
                return existing;

            var info = _client.GetDownloadInfo(keyboardId, bcp47);
            if (info == null || string.IsNullOrEmpty(info.JsUrl))
                return null; // offline or not found; caller retries later

            Directory.CreateDirectory(_keyboardsFolder);
            var fontsFolder = Path.Combine(_keyboardsFolder, "fonts");

            // Download the keyboard .js first. If this fails we abort without writing a manifest.
            var jsBytes = _client.DownloadBytes(info.JsUrl);
            if (jsBytes == null)
                return null;

            var manifest = new KeyboardCacheManifest
            {
                Id = keyboardId,
                LanguageTag = bcp47,
                Version = info.Version,
                JsUrl = info.JsUrl,
                FontFamily = info.FontInfo?.Family,
                OskFontFamily = info.OskFontInfo?.Family,
            };

            // Download all referenced font files. We dedupe by file name because the display font and
            // the OSK font are frequently the same file.
            var downloadedFonts = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (
                !DownloadFontFiles(info.FontInfo, fontsFolder, manifest.FontFiles, downloadedFonts)
                || !DownloadFontFiles(
                    info.OskFontInfo,
                    fontsFolder,
                    manifest.OskFontFiles,
                    downloadedFonts
                )
            )
                return null; // a font failed to download; don't leave a manifest that lies

            // All files are down. Write the .js, then the manifest LAST so a reader that sees the
            // manifest can trust the rest is present.
            AtomicWriteAllBytes(Path.Combine(_keyboardsFolder, keyboardId + ".js"), jsBytes);
            AtomicWriteAllText(
                Path.Combine(_keyboardsFolder, keyboardId + ".json"),
                JsonConvert.SerializeObject(manifest, Formatting.Indented)
            );

            return manifest;
        }

        /// <summary>
        /// Read the cached manifest for a keyboard, or null if it hasn't been cached. Does not verify
        /// that the referenced files still exist (use <see cref="EnsureDownloaded"/> for that).
        /// </summary>
        /// <param name="keyboardId">The keyboard id, e.g. "sil_myanmar_my3".</param>
        public KeyboardCacheManifest TryGetInfo(string keyboardId)
        {
            if (string.IsNullOrWhiteSpace(keyboardId))
                throw new ArgumentException("A keyboard id is required.", nameof(keyboardId));
            var manifestPath = Path.Combine(_keyboardsFolder, keyboardId + ".json");
            if (!RobustFile.Exists(manifestPath))
                return null;
            return JsonConvert.DeserializeObject<KeyboardCacheManifest>(
                RobustFile.ReadAllText(manifestPath, Encoding.UTF8)
            );
        }

        /// <summary>
        /// Get the localhost URL that the browser can use to load a cached keyboard's .js, served by
        /// BloomServer from the cache on disk. Does not check that the file exists.
        /// </summary>
        /// <param name="keyboardId">The keyboard id, e.g. "sil_myanmar_my3".</param>
        public string GetJsUrl(string keyboardId)
        {
            if (string.IsNullOrWhiteSpace(keyboardId))
                throw new ArgumentException("A keyboard id is required.", nameof(keyboardId));
            return Path.Combine(_keyboardsFolder, keyboardId + ".js").ToLocalhost();
        }

        /// <summary>
        /// True if this keyboard is fully cached and ready to use offline: its manifest exists and all
        /// the files it names (the .js and every font) are present on disk. The edit-time endpoint uses
        /// this to decide whether it can tell the browser to attach the keyboard yet.
        /// </summary>
        /// <param name="keyboardId">The keyboard id, e.g. "sil_myanmar_my3".</param>
        public bool IsCached(string keyboardId)
        {
            var manifest = TryGetInfo(keyboardId);
            return manifest != null && AllFilesPresent(manifest);
        }

        /// <summary>
        /// The localhost URL the browser can use to load a cached font file (from the <c>fonts/</c>
        /// subfolder), served by BloomServer from disk. Does not check that the file exists.
        /// </summary>
        /// <param name="fontFileName">The font file name as stored in the manifest, e.g. "Pyidaungsu-Regular.ttf".</param>
        public string GetFontUrl(string fontFileName)
        {
            if (string.IsNullOrWhiteSpace(fontFileName))
                throw new ArgumentException("A font file name is required.", nameof(fontFileName));
            return Path.Combine(_keyboardsFolder, "fonts", fontFileName).ToLocalhost();
        }

        /// <summary>
        /// True if the .js and every font file named by the manifest are present on disk.
        /// </summary>
        private bool AllFilesPresent(KeyboardCacheManifest manifest)
        {
            if (!RobustFile.Exists(Path.Combine(_keyboardsFolder, manifest.Id + ".js")))
                return false;
            var fontsFolder = Path.Combine(_keyboardsFolder, "fonts");
            var allFontFiles = manifest.FontFiles.Concat(manifest.OskFontFiles);
            return allFontFiles.All(f => RobustFile.Exists(Path.Combine(fontsFolder, f)));
        }

        /// <summary>
        /// Download each file of a font (skipping ones already downloaded this run), recording the
        /// file names on <paramref name="fileNamesRecord"/>. Returns false if any download fails.
        /// </summary>
        private bool DownloadFontFiles(
            KeymanFontInfo fontInfo,
            string fontsFolder,
            List<string> fileNamesRecord,
            Dictionary<string, bool> alreadyDownloaded
        )
        {
            if (fontInfo == null)
                return true;
            for (var i = 0; i < fontInfo.FileNames.Count; i++)
            {
                var fileName = fontInfo.FileNames[i];
                fileNamesRecord.Add(fileName);
                if (alreadyDownloaded.ContainsKey(fileName))
                    continue;
                var bytes = _client.DownloadBytes(fontInfo.Urls[i]);
                if (bytes == null)
                    return false;
                Directory.CreateDirectory(fontsFolder);
                AtomicWriteAllBytes(Path.Combine(fontsFolder, fileName), bytes);
                alreadyDownloaded[fileName] = true;
            }
            return true;
        }

        /// <summary>
        /// Write text atomically: write to a temp file in the same folder, then rename over the
        /// destination so readers never see a partial file.
        /// </summary>
        private static void AtomicWriteAllText(string path, string contents)
        {
            var tempPath = path + ".tmp";
            RobustFile.WriteAllText(tempPath, contents, Encoding.UTF8);
            MoveTempOverDestination(tempPath, path);
        }

        /// <summary>
        /// Write bytes atomically: write to a temp file in the same folder, then rename over the
        /// destination so readers never see a partial file.
        /// </summary>
        private static void AtomicWriteAllBytes(string path, byte[] contents)
        {
            var tempPath = path + ".tmp";
            RobustFile.WriteAllBytes(tempPath, contents);
            MoveTempOverDestination(tempPath, path);
        }

        /// <summary>
        /// Atomically replace <paramref name="destPath"/> with <paramref name="tempPath"/>. Uses
        /// Replace when the destination already exists (atomic) and Move otherwise.
        /// </summary>
        private static void MoveTempOverDestination(string tempPath, string destPath)
        {
            if (RobustFile.Exists(destPath))
                RobustFile.Replace(tempPath, destPath, null);
            else
                RobustFile.Move(tempPath, destPath);
        }
    }
}
