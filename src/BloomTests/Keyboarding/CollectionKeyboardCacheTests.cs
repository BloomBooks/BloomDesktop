using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Keyboarding;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.Keyboarding
{
    /// <summary>
    /// Tests CollectionKeyboardCache: manifest round-trip, atomic writes, skip-if-present, and the
    /// GetJsUrl localhost mapping. Network is faked so nothing here hits the real Keyman API.
    /// </summary>
    [TestFixture]
    public class CollectionKeyboardCacheTests
    {
        private const string kFixtureDir = "src/BloomTests/Keyboarding/fixtures";
        private TemporaryFolder _collectionFolder;

        // The canned bytes we hand back for the .js and font downloads, so we can assert they land
        // on disk unchanged.
        private static readonly byte[] s_jsBytes = Encoding.UTF8.GetBytes(
            "// fake keymanweb keyboard js"
        );
        private static readonly byte[] s_fontBytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        [SetUp]
        public void SetUp()
        {
            _collectionFolder = new TemporaryFolder("CollectionKeyboardCacheTests");
        }

        [TearDown]
        public void TearDown()
        {
            _collectionFolder.Dispose();
            // Restore a real client so we don't leak the fake into other fixtures.
            KeymanCloudClient.SetHttpClientForTests(new HttpClient());
        }

        // A handler that returns canned content for the metadata query, the keyboard .js, and the
        // font file, and 404s anything else. Counts requests so tests can assert caching skips them.
        private sealed class CannedHandler : HttpMessageHandler
        {
            private readonly string _downloadJson;
            public int RequestCount;

            public CannedHandler(string downloadJson)
            {
                _downloadJson = downloadJson;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                RequestCount++;
                var url = request.RequestUri.ToString();
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                if (url.Contains("api.keyman.com/cloud"))
                    response.Content = new StringContent(_downloadJson, Encoding.UTF8);
                else if (url.EndsWith(".js"))
                    response.Content = new ByteArrayContent(s_jsBytes);
                else if (url.EndsWith(".ttf"))
                    response.Content = new ByteArrayContent(s_fontBytes);
                else
                    response.StatusCode = HttpStatusCode.NotFound;
                return Task.FromResult(response);
            }
        }

        private CannedHandler UseCannedNetwork()
        {
            var downloadJson = RobustFile.ReadAllText(
                FileLocationUtilities.GetFileDistributedWithApplication(
                    kFixtureDir,
                    "download-sil_myanmar_my3-my.json"
                )
            );
            var handler = new CannedHandler(downloadJson);
            KeymanCloudClient.SetHttpClientForTests(new HttpClient(handler));
            return handler;
        }

        [Test]
        public void EnsureDownloaded_ThenTryGetInfo_RoundTripsManifestAndFiles()
        {
            UseCannedNetwork();
            var cache = new CollectionKeyboardCache(
                _collectionFolder.FolderPath,
                new KeymanCloudClient()
            );

            // Sanity: nothing there before we start.
            Assert.That(
                cache.TryGetInfo("sil_myanmar_my3"),
                Is.Null,
                "should be empty before download"
            );

            var manifest = cache.EnsureDownloaded("sil_myanmar_my3", "my");

            Assert.That(
                manifest,
                Is.Not.Null,
                "EnsureDownloaded should succeed with canned network"
            );
            Assert.That(manifest.Id, Is.EqualTo("sil_myanmar_my3"));
            Assert.That(manifest.LanguageTag, Is.EqualTo("my"));
            Assert.That(manifest.Version, Is.EqualTo("1.7.5"));
            Assert.That(manifest.FontFamily, Is.EqualTo("Pyidaungsu"));
            Assert.That(manifest.OskFontFamily, Is.EqualTo("Pyidaungsu"));
            Assert.That(manifest.FontFiles, Is.EqualTo(new[] { "Pyidaungsu-Regular.ttf" }));

            // The files should be on disk in the documented layout.
            var keyboardsFolder = Path.Combine(_collectionFolder.FolderPath, ".keyboards");
            var jsPath = Path.Combine(keyboardsFolder, "sil_myanmar_my3.js");
            var jsonPath = Path.Combine(keyboardsFolder, "sil_myanmar_my3.json");
            var fontPath = Path.Combine(keyboardsFolder, "fonts", "Pyidaungsu-Regular.ttf");
            Assert.That(File.Exists(jsPath), "keyboard .js written");
            Assert.That(File.Exists(jsonPath), "manifest .json written");
            Assert.That(File.Exists(fontPath), "font written to fonts/ subfolder");
            Assert.That(File.ReadAllBytes(jsPath), Is.EqualTo(s_jsBytes), "js content intact");
            Assert.That(
                File.ReadAllBytes(fontPath),
                Is.EqualTo(s_fontBytes),
                "font content intact"
            );

            // Round-trip: reading the manifest back matches what EnsureDownloaded returned.
            var reread = cache.TryGetInfo("sil_myanmar_my3");
            Assert.That(reread, Is.Not.Null);
            Assert.That(reread.Id, Is.EqualTo(manifest.Id));
            Assert.That(reread.Version, Is.EqualTo(manifest.Version));
            Assert.That(reread.FontFamily, Is.EqualTo(manifest.FontFamily));
            Assert.That(reread.FontFiles, Is.EqualTo(manifest.FontFiles));
        }

        [Test]
        public void EnsureDownloaded_AlreadyCached_SkipsNetwork()
        {
            var handler = UseCannedNetwork();
            var cache = new CollectionKeyboardCache(
                _collectionFolder.FolderPath,
                new KeymanCloudClient()
            );

            cache.EnsureDownloaded("sil_myanmar_my3", "my");
            var countAfterFirst = handler.RequestCount;
            Assert.That(countAfterFirst, Is.GreaterThan(0), "sanity: first call hit the network");

            // Second call should be satisfied entirely from disk.
            var manifest = cache.EnsureDownloaded("sil_myanmar_my3", "my");
            Assert.That(manifest, Is.Not.Null);
            Assert.That(
                handler.RequestCount,
                Is.EqualTo(countAfterFirst),
                "second EnsureDownloaded should not touch the network"
            );
        }

        [Test]
        public void EnsureDownloaded_LeavesNoTempFiles()
        {
            UseCannedNetwork();
            var cache = new CollectionKeyboardCache(
                _collectionFolder.FolderPath,
                new KeymanCloudClient()
            );

            cache.EnsureDownloaded("sil_myanmar_my3", "my");

            // The atomic-write dance uses ".tmp" files; none should survive a successful download.
            var strays = Directory
                .EnumerateFiles(
                    Path.Combine(_collectionFolder.FolderPath, ".keyboards"),
                    "*.tmp",
                    SearchOption.AllDirectories
                )
                .ToList();
            Assert.That(strays, Is.Empty, "no leftover temp files after atomic writes");
        }

        [Test]
        public void EnsureDownloaded_OverwritesStaleFileAtomically()
        {
            UseCannedNetwork();
            var keyboardsFolder = Path.Combine(_collectionFolder.FolderPath, ".keyboards");
            Directory.CreateDirectory(keyboardsFolder);
            // Pre-seed a stale .js so the write path must replace an existing destination.
            var jsPath = Path.Combine(keyboardsFolder, "sil_myanmar_my3.js");
            File.WriteAllText(jsPath, "STALE CONTENT that must be replaced");
            Assert.That(
                File.ReadAllText(jsPath),
                Does.Contain("STALE"),
                "sanity: stale content is present before download"
            );

            var cache = new CollectionKeyboardCache(
                _collectionFolder.FolderPath,
                new KeymanCloudClient()
            );
            cache.EnsureDownloaded("sil_myanmar_my3", "my");

            Assert.That(
                File.ReadAllBytes(jsPath),
                Is.EqualTo(s_jsBytes),
                "stale file should have been atomically replaced with the downloaded content"
            );
        }

        [Test]
        public void EnsureDownloaded_Offline_ReturnsNullAndWritesNothing()
        {
            // A handler that always fails simulates being offline.
            KeymanCloudClient.SetHttpClientForTests(new HttpClient(new AlwaysFailHandler()));
            var cache = new CollectionKeyboardCache(
                _collectionFolder.FolderPath,
                new KeymanCloudClient()
            );

            var manifest = cache.EnsureDownloaded("sil_myanmar_my3", "my");

            Assert.That(manifest, Is.Null, "offline download should return null");
            Assert.That(
                Directory.Exists(Path.Combine(_collectionFolder.FolderPath, ".keyboards"))
                    && Directory
                        .EnumerateFiles(Path.Combine(_collectionFolder.FolderPath, ".keyboards"))
                        .Any(),
                Is.False,
                "offline download should not leave any files behind"
            );
        }

        [Test]
        public void TryGetInfo_NotCached_ReturnsNull()
        {
            var cache = new CollectionKeyboardCache(_collectionFolder.FolderPath);
            Assert.That(cache.TryGetInfo("never_downloaded"), Is.Null);
        }

        [Test]
        public void GetJsUrl_ReturnsLocalhostUrlToCachedJs()
        {
            var cache = new CollectionKeyboardCache(_collectionFolder.FolderPath);
            var url = cache.GetJsUrl("sil_myanmar_my3");
            Assert.That(url, Does.Contain("sil_myanmar_my3.js"));
            Assert.That(
                url,
                Does.StartWith("http"),
                "should be a localhost http url the browser can load"
            );
            Assert.That(url, Does.Contain(".keyboards"));
        }

        private sealed class AlwaysFailHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken
            )
            {
                throw new HttpRequestException("simulated offline");
            }
        }
    }
}
