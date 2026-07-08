using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using Bloom.Properties;
using Bloom.web;
using Bloom.web.controllers;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.web
{
    /// <summary>
    /// Integration tests that exercise the real HTTP surface of the avatar feature: the
    /// GET /api/avatar endpoint (AvatarApi), and the external/login wiring in ExternalApi that
    /// registers the logged-in user's photo URL in the AvatarCache. Fetching is stubbed via a
    /// testable AvatarCache so nothing here touches the network, and a TemporaryFolder isolates the
    /// on-disk cache.
    /// </summary>
    [TestFixture]
    public class AvatarApiTests
    {
        private BloomServer _server;
        private TemporaryFolder _folder;
        private TestableAvatarCache _cache;

        // Login settings we touch via ExternalApi's SetLoginData call; saved/restored so we don't
        // clobber the developer's real persisted configuration.
        private string _origWebUserId;
        private string _origLastLoginSessionToken;
        private string _origLastLoginUserId;
        private string _origLastLoginDest;

        private class TestableAvatarCache : AvatarCache
        {
            public byte[] BytesToReturn;
            public string ContentTypeToReturn = "image/png";

            public TestableAvatarCache(string cacheFolder)
                : base(cacheFolder) { }

            protected override Task<FetchResult> FetchAsync(string url)
            {
                if (BytesToReturn == null)
                    return Task.FromResult<FetchResult>(null);
                return Task.FromResult(
                    new FetchResult { Bytes = BytesToReturn, ContentType = ContentTypeToReturn }
                );
            }
        }

        [SetUp]
        public void Setup()
        {
            // Share the same monitor as the other server tests so we never run two servers on the
            // fixed test port at once.
            Monitor.Enter(EndpointHandlerTests._portMonitor);

            _origWebUserId = Settings.Default.WebUserId;
            _origLastLoginSessionToken = Settings.Default.LastLoginSessionToken;
            _origLastLoginUserId = Settings.Default.LastLoginUserId;
            _origLastLoginDest = Settings.Default.LastLoginDest;

            _folder = new TemporaryFolder("AvatarApiTests");
            _cache = new TestableAvatarCache(_folder.Path);
            _server = new BloomServer(new BookSelection());
            new AvatarApi(_cache).RegisterWithApiHandler(_server.ApiHandler);
        }

        [TearDown]
        public void TearDown()
        {
            _server.Dispose();
            _server = null;
            _folder.Dispose();

            Settings.Default.WebUserId = _origWebUserId;
            Settings.Default.LastLoginSessionToken = _origLastLoginSessionToken;
            Settings.Default.LastLoginUserId = _origLastLoginUserId;
            Settings.Default.LastLoginDest = _origLastLoginDest;
            Settings.Default.Save();

            Monitor.Exit(EndpointHandlerTests._portMonitor);
        }

        private string AvatarUrl(string email)
        {
            return BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                + "api/avatar?email="
                + WebUtility.UrlEncode(email);
        }

        [Test]
        public void GetAvatar_WhenCacheHasImage_ReturnsBytes()
        {
            _cache.BytesToReturn = new byte[] { 10, 20, 30 };
            _server.EnsureListening();

            byte[] data;
            using (var client = new WebClient())
            {
                data = client.DownloadData(AvatarUrl("user@example.com"));
            }

            Assert.That(data, Is.EqualTo(new byte[] { 10, 20, 30 }));
        }

        [Test]
        public void GetAvatar_WhenNothingAvailable_Returns404()
        {
            _cache.BytesToReturn = null; // simulate no known photo, no Gravatar, offline
            _server.EnsureListening();

            using (var client = new WebClient())
            {
                var ex = Assert.Throws<WebException>(() =>
                    client.DownloadData(AvatarUrl("nobody@example.com"))
                );
                // 404 is the contract that makes react-avatar fall back to generated initials.
                Assert.That(
                    ((HttpWebResponse)ex.Response).StatusCode,
                    Is.EqualTo(HttpStatusCode.NotFound)
                );
            }
        }

        [Test]
        public void ExternalLogin_WithPhotoUrl_RegistersKnownPhotoMapping()
        {
            var client = new BloomLibraryBookApiClient();
            new ExternalApi(client, null, null, null, null, null, _cache).RegisterWithApiHandler(
                _server.ApiHandler
            );

            var md5 = AvatarCache.Md5OfEmail("user@example.com");
            // Sanity: no mapping before login.
            Assert.That(_cache.GetKnownPhotoUrlOrNull(md5), Is.Null);

            var result = ApiTest.PostString(
                _server,
                "external/login",
                "{\"sessionToken\":\"t\",\"email\":\"user@example.com\",\"userId\":\"u\",\"photoUrl\":\"https://photos.example/pic.jpg\"}",
                ApiTest.ContentType.JSON
            );

            Assert.That(result, Is.EqualTo("OK"));
            Assert.That(
                _cache.GetKnownPhotoUrlOrNull(md5),
                Is.EqualTo("https://photos.example/pic.jpg")
            );
        }

        [Test]
        public void ExternalLogin_WithoutPhotoUrl_DoesNotRegisterAndDoesNotThrow()
        {
            var client = new BloomLibraryBookApiClient();
            new ExternalApi(client, null, null, null, null, null, _cache).RegisterWithApiHandler(
                _server.ApiHandler
            );

            var md5 = AvatarCache.Md5OfEmail("user@example.com");
            // Sanity: no mapping before login.
            Assert.That(_cache.GetKnownPhotoUrlOrNull(md5), Is.Null);

            // A login body that omits photoUrl entirely (an older website build). This must succeed
            // and leave no mapping, so the avatar falls back to Gravatar/initials.
            var result = ApiTest.PostString(
                _server,
                "external/login",
                "{\"sessionToken\":\"t\",\"email\":\"user@example.com\",\"userId\":\"u\"}",
                ApiTest.ContentType.JSON
            );

            Assert.That(result, Is.EqualTo("OK"));
            Assert.That(_cache.GetKnownPhotoUrlOrNull(md5), Is.Null);
        }
    }
}
