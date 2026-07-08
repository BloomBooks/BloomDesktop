using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Bloom.Utils;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.web
{
    /// <summary>
    /// A general, person-keyed cache of avatar image BYTES, served by Bloom's local server. Avatars
    /// are shown both for the logged-in user and for other team members in Team Collections, so the
    /// cache is keyed by person (the MD5 of a normalized email, matching Gravatar's scheme) rather
    /// than being tied to the current login. Caching the actual bytes lets avatars survive a restart
    /// and work offline (important for Bloom's low-connectivity users) and stops us re-pinging remote
    /// avatar hosts on every render. We attempt a fresh fetch once per run of Bloom (falling back to the
    /// cached bytes if it fails), so a user who changes their avatar sees the change after a restart.
    ///
    /// For a given key we resolve a source in this order: a "known" provider photo URL for that person
    /// (a nicer source than Gravatar) if we happen to have one, otherwise Gravatar. Right now the only
    /// known photo we can populate is the logged-in user's (captured from the browser login callback's
    /// photoUrl); teammates never logged into this Bloom, so they resolve via Gravatar. The known-photo
    /// map is persisted and built to grow later.
    /// </summary>
    public class AvatarCache
    {
        // A single shared HttpClient is the recommended pattern; reusing it avoids socket exhaustion.
        // The timeout keeps a slow/hung avatar host from blocking the request thread indefinitely; on
        // timeout we simply treat it as a miss (the front end then shows generated initials).
        private static readonly HttpClient s_httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };

        private readonly string _cacheFolder;
        private readonly string _knownPhotoUrlsPath;
        private readonly string _indexPath;

        // Guards the two in-memory maps and their persisted (small JSON) files. The potentially-large
        // image-byte files, and network fetches, happen OUTSIDE this lock so slow disk or a slow
        // download never blocks other avatar lookups.
        private readonly object _lock = new object();

        // md5(email) -> a provider photo URL (e.g. a Google/Firebase photoURL) that is a nicer source
        // than Gravatar for that person. Persisted so it survives a restart.
        private Dictionary<string, string> _knownPhotoUrls;

        // md5(email) -> metadata about the image bytes we have cached on disk for that person.
        private Dictionary<string, CacheIndexEntry> _index;

        // Keys we've already refreshed (attempted a fresh fetch for) during THIS run of Bloom. NOT
        // persisted: emptied on every launch, so the first lookup of each person after a restart
        // re-attempts a fresh fetch. That is how a user who changed their Gravatar/Google photo sees the
        // change -- by simply restarting Bloom. Within one run we then serve from the cache, so we do
        // not re-hit the remote host on every render.
        private readonly HashSet<string> _refreshedThisRun = new HashSet<string>();

        private class CacheIndexEntry
        {
            public string SourceUrl;
            public string ContentType;
        }

        /// <summary>
        /// The outcome of resolving an avatar: the image bytes and their content type. Null is used
        /// (rather than an instance) to mean "we have nothing", which the endpoint turns into a 404.
        /// </summary>
        public class AvatarResult
        {
            public byte[] Bytes;
            public string ContentType;
        }

        /// <summary>
        /// The result of fetching a source URL. Returned by FetchAsync (which tests override to avoid
        /// real network traffic). Null means the fetch failed / 404 / produced no usable image.
        /// </summary>
        public class FetchResult
        {
            public byte[] Bytes;
            public string ContentType;
        }

        /// <summary>
        /// Create the cache. cacheFolder defaults to a folder under Bloom's normal user-data area;
        /// tests pass a temporary folder so they neither read nor pollute the real cache. Loads any
        /// previously-persisted known-photo map and disk-cache index (this is what restores the map on
        /// startup).
        /// </summary>
        public AvatarCache(string cacheFolder = null)
        {
            _cacheFolder =
                cacheFolder ?? Path.Combine(ProjectContext.GetBloomAppDataFolder(), "AvatarCache");
            Directory.CreateDirectory(_cacheFolder);
            _knownPhotoUrlsPath = Path.Combine(_cacheFolder, "knownPhotoUrls.json");
            _indexPath = Path.Combine(_cacheFolder, "index.json");
            Load();
        }

        /// <summary>
        /// The folder this cache persists into. Exposed for the DI regression test that verifies the
        /// container gives the production (parameterless) instance the app-data folder, rather than
        /// accidentally injecting some other registered string as the cacheFolder argument.
        /// </summary>
        internal string CacheFolder => _cacheFolder;

        /// <summary>
        /// The MD5 (lowercase hex) of the normalized (trimmed, lowercased) email. This matches the
        /// Gravatar scheme (and what react-avatar/md5Util use), so the logged-in user and a teammate
        /// with the same email share one key.
        /// </summary>
        public static string Md5OfEmail(string email)
        {
            var normalized = (email ?? "").Trim().ToLowerInvariant();
            // MiscUtils.GetMd5HashOfString hashes the UTF8 bytes and returns lowercase hex -- exactly
            // Gravatar's scheme -- so we reuse it rather than hand-rolling the hash+hex conversion.
            return MiscUtils.GetMd5HashOfString(normalized);
        }

        /// <summary>
        /// The Gravatar URL for a key. d=404 makes Gravatar return a 404 (rather than a generic image)
        /// when the person has no Gravatar, so our fetch treats "no Gravatar" as a miss and the front
        /// end falls back to generated initials.
        /// </summary>
        public static string GravatarUrl(string md5)
        {
            return $"https://secure.gravatar.com/avatar/{md5}?d=404";
        }

        /// <summary>
        /// Register (or, when photoUrl is null/empty, clear) the known provider photo URL for a person,
        /// and persist the map. This is how the logged-in user gets a nicer-than-Gravatar source.
        /// </summary>
        public void SetKnownPhotoUrl(string md5, string photoUrl)
        {
            if (string.IsNullOrEmpty(md5))
                return;
            // An empty/null photoUrl means "no known photo" -- same as clearing it.
            if (string.IsNullOrEmpty(photoUrl))
            {
                RemoveKnownPhotoUrl(md5);
                return;
            }
            md5 = md5.ToLowerInvariant();
            lock (_lock)
            {
                if (_knownPhotoUrls.TryGetValue(md5, out var existing) && existing == photoUrl)
                    return; // unchanged; don't rewrite the file
                _knownPhotoUrls[md5] = photoUrl;
                SaveKnownPhotoUrls();
            }
        }

        /// <summary>
        /// Remove a person's known provider photo URL (e.g. on logout, so the logged-in user reverts to
        /// Gravatar). Cached image files may remain; that's harmless.
        /// </summary>
        public void RemoveKnownPhotoUrl(string md5)
        {
            if (string.IsNullOrEmpty(md5))
                return;
            md5 = md5.ToLowerInvariant();
            lock (_lock)
            {
                if (_knownPhotoUrls.Remove(md5))
                    SaveKnownPhotoUrls();
            }
        }

        /// <summary>
        /// The known provider photo URL for a person, or null if we have none. Mainly for the API layer
        /// and tests to inspect the map.
        /// </summary>
        public string GetKnownPhotoUrlOrNull(string md5)
        {
            if (string.IsNullOrEmpty(md5))
                return null;
            md5 = md5.ToLowerInvariant();
            lock (_lock)
            {
                return _knownPhotoUrls.TryGetValue(md5, out var url) ? url : null;
            }
        }

        /// <summary>
        /// Resolve the avatar bytes for a key. Once per run of Bloom (and also whenever the best source
        /// has changed, e.g. a new login supplied a Google photo) we attempt a FRESH fetch, so a changed
        /// avatar shows up on the next launch. If that fetch fails (offline) or the source has no image,
        /// we fall back to a previously cached copy when we have one -- so an offline user keeps seeing
        /// their last-known avatar and a restart will retry. Only when we have nothing at all do we
        /// return null, which the endpoint turns into a 404 so the front end shows generated initials.
        /// Later lookups within the same run are served from the cache without re-fetching.
        /// </summary>
        public async Task<AvatarResult> GetAvatarBytesAsync(string md5)
        {
            if (string.IsNullOrEmpty(md5))
                return null;
            md5 = md5.ToLowerInvariant();

            var desiredSource = GetSourceUrlFor(md5);

            // Attempt a fresh fetch if we haven't already this run, OR if the source we would use has
            // changed since we last cached (so a newly-registered Google photo replaces a cached Gravatar
            // right away). Otherwise, serve what we already have this run without re-fetching.
            bool alreadyTriedThisRun;
            bool sourceMatchesCache;
            lock (_lock)
            {
                alreadyTriedThisRun = _refreshedThisRun.Contains(md5);
                sourceMatchesCache =
                    _index.TryGetValue(md5, out var entry)
                    && entry != null
                    && entry.SourceUrl == desiredSource;
            }
            if (alreadyTriedThisRun && sourceMatchesCache)
                return ReadCachedOrNull(md5);

            // Attempt a fresh fetch (outside the lock). A failure here is not worth alarming the user
            // about; we fall back to any cached copy below.
            FetchResult fetched = null;
            try
            {
                fetched = await FetchAsync(desiredSource);
            }
            catch (Exception e)
            {
                Logger.WriteEvent(
                    "AvatarCache: fetch failed for " + desiredSource + ": " + e.Message
                );
            }
            lock (_lock)
            {
                _refreshedThisRun.Add(md5); // we've tried this run, whether or not it succeeded
            }

            if (fetched == null || fetched.Bytes == null || fetched.Bytes.Length == 0)
            {
                // Offline, timeout, or the source has no image. Keep using the cached copy if we have
                // one; otherwise there is nothing to show and the front end falls back to initials.
                return ReadCachedOrNull(md5);
            }

            // Success: update the cache (write the large file OUTSIDE the lock) and use the fresh bytes.
            try
            {
                RobustFile.WriteAllBytes(FilePathFor(md5), fetched.Bytes);
                lock (_lock)
                {
                    _index[md5] = new CacheIndexEntry
                    {
                        SourceUrl = desiredSource,
                        ContentType = fetched.ContentType,
                    };
                    SaveIndex();
                }
            }
            catch (Exception e)
            {
                // If we couldn't persist, still return the bytes we fetched; we just won't have a cache
                // hit next time.
                Logger.WriteEvent(
                    "AvatarCache: could not cache bytes for " + md5 + ": " + e.Message
                );
            }

            return new AvatarResult { Bytes = fetched.Bytes, ContentType = fetched.ContentType };
        }

        // Return the bytes cached on disk for this key (from an earlier successful fetch, possibly in a
        // previous run), or null if we have none. The (potentially large) file is read OUTSIDE the lock.
        private AvatarResult ReadCachedOrNull(string md5)
        {
            string path;
            string contentType;
            lock (_lock)
            {
                if (!_index.TryGetValue(md5, out var entry) || entry == null)
                    return null;
                path = FilePathFor(md5);
                contentType = entry.ContentType;
            }
            try
            {
                if (RobustFile.Exists(path))
                    return new AvatarResult
                    {
                        Bytes = RobustFile.ReadAllBytes(path),
                        ContentType = contentType,
                    };
            }
            catch (Exception e)
            {
                Logger.WriteEvent(
                    "AvatarCache: could not read cached bytes for " + md5 + ": " + e.Message
                );
            }
            return null;
        }

        /// <summary>
        /// Download the bytes at the given URL. Virtual so tests can override it to return canned data
        /// (and assert which source was chosen) without touching the network. Returns null on any
        /// non-success status (including Gravatar's 404 for "no avatar").
        /// </summary>
        protected virtual async Task<FetchResult> FetchAsync(string url)
        {
            var response = await s_httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes == null || bytes.Length == 0)
                return null;
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            return new FetchResult { Bytes = bytes, ContentType = contentType };
        }

        // The source we would use for this key right now: a known provider photo URL if we have one,
        // otherwise Gravatar.
        private string GetSourceUrlFor(string md5)
        {
            lock (_lock)
            {
                if (_knownPhotoUrls.TryGetValue(md5, out var url) && !string.IsNullOrEmpty(url))
                    return url;
            }
            return GravatarUrl(md5);
        }

        // The on-disk path of the cached image bytes for a key. No extension: the content type lives in
        // the index. The key is a 32-char hex string, so it is always a safe file name.
        private string FilePathFor(string md5)
        {
            return Path.Combine(_cacheFolder, md5);
        }

        private void Load()
        {
            _knownPhotoUrls =
                ReadJson<Dictionary<string, string>>(_knownPhotoUrlsPath)
                ?? new Dictionary<string, string>();
            _index =
                ReadJson<Dictionary<string, CacheIndexEntry>>(_indexPath)
                ?? new Dictionary<string, CacheIndexEntry>();
        }

        private static T ReadJson<T>(string path)
            where T : class
        {
            try
            {
                if (RobustFile.Exists(path))
                    return JsonConvert.DeserializeObject<T>(RobustFile.ReadAllText(path));
            }
            catch (Exception e)
            {
                // A corrupt cache file should never be fatal; just start fresh.
                Logger.WriteEvent("AvatarCache: could not read " + path + ": " + e.Message);
            }
            return null;
        }

        // Callers hold _lock.
        private void SaveKnownPhotoUrls()
        {
            WriteJson(_knownPhotoUrlsPath, _knownPhotoUrls);
        }

        // Callers hold _lock.
        private void SaveIndex()
        {
            WriteJson(_indexPath, _index);
        }

        private static void WriteJson(string path, object value)
        {
            try
            {
                RobustFile.WriteAllText(path, JsonConvert.SerializeObject(value));
            }
            catch (Exception e)
            {
                Logger.WriteEvent("AvatarCache: could not write " + path + ": " + e.Message);
            }
        }
    }
}
