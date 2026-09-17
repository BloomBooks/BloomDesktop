using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Autofac;
using Bloom.web;
using NUnit.Framework;
using SIL.TestUtilities;

namespace BloomTests.web
{
    /// <summary>
    /// Tests for AvatarCache, the person-keyed cache of avatar image bytes. Fetching is stubbed via a
    /// testable subclass so nothing here touches the network; a TemporaryFolder isolates the on-disk
    /// cache so we neither read nor pollute the real one.
    /// </summary>
    [TestFixture]
    public class AvatarCacheTests
    {
        private TemporaryFolder _folder;

        /// <summary>
        /// An AvatarCache whose network fetch is replaced with a canned response, recording every URL
        /// it was asked to fetch so tests can assert which source was chosen and how many times we
        /// fetched (to prove cache hits avoid refetching).
        /// </summary>
        private class TestableAvatarCache : AvatarCache
        {
            public readonly List<string> FetchedUrls = new List<string>();

            // null => simulate a miss (offline / 404 / no image).
            public byte[] BytesToReturn;
            public string ContentTypeToReturn = "image/png";

            public TestableAvatarCache(string cacheFolder)
                : base(cacheFolder) { }

            protected override Task<FetchResult> FetchAsync(string url)
            {
                FetchedUrls.Add(url);
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
            _folder = new TemporaryFolder("AvatarCacheTests");
        }

        [TearDown]
        public void TearDown()
        {
            _folder.Dispose();
        }

        [Test]
        public void Md5OfEmail_NormalizesTrimAndCase_AndMatchesGravatarScheme()
        {
            // Trimming + lowercasing must yield the same key regardless of surrounding whitespace/case.
            Assert.That(
                AvatarCache.Md5OfEmail("  User@Example.COM  "),
                Is.EqualTo(AvatarCache.Md5OfEmail("user@example.com"))
            );

            // And it must equal Gravatar's own documented example hash, proving we use the same scheme
            // (so a person's key here lines up with their Gravatar).
            Assert.That(
                AvatarCache.Md5OfEmail("MyEmailAddress@example.com "),
                Is.EqualTo("0bc83cb571cd1c50ba6f3e8a78ef1346")
            );
        }

        [Test]
        public void GetAvatarBytes_WithKnownPhotoUrl_FetchesThatSource()
        {
            var cache = new TestableAvatarCache(_folder.Path);
            cache.BytesToReturn = new byte[] { 1, 2, 3 };
            var md5 = AvatarCache.Md5OfEmail("user@example.com");

            // Sanity: no known photo before we register one.
            Assert.That(cache.GetKnownPhotoUrlOrNull(md5), Is.Null);

            cache.SetKnownPhotoUrl(md5, "https://photos.example/pic.jpg");
            Assert.That(
                cache.GetKnownPhotoUrlOrNull(md5),
                Is.EqualTo("https://photos.example/pic.jpg")
            );

            var result = cache.GetAvatarBytesAsync(md5).Result;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Bytes, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(result.ContentType, Is.EqualTo("image/png"));
            Assert.That(cache.FetchedUrls, Has.Count.EqualTo(1));
            Assert.That(cache.FetchedUrls[0], Is.EqualTo("https://photos.example/pic.jpg"));
        }

        [Test]
        public void GetAvatarBytes_WithoutKnownPhotoUrl_FallsBackToGravatar()
        {
            var cache = new TestableAvatarCache(_folder.Path);
            cache.BytesToReturn = new byte[] { 4, 5 };
            var md5 = AvatarCache.Md5OfEmail("teammate@example.com");

            // Sanity: no known photo for this person (the normal teammate case).
            Assert.That(cache.GetKnownPhotoUrlOrNull(md5), Is.Null);

            var result = cache.GetAvatarBytesAsync(md5).Result;

            Assert.That(result, Is.Not.Null);
            Assert.That(cache.FetchedUrls, Has.Count.EqualTo(1));
            Assert.That(cache.FetchedUrls[0], Is.EqualTo(AvatarCache.GravatarUrl(md5)));
        }

        [Test]
        public void GetAvatarBytes_CacheHit_DoesNotRefetch()
        {
            var cache = new TestableAvatarCache(_folder.Path);
            cache.BytesToReturn = new byte[] { 7 };
            var md5 = AvatarCache.Md5OfEmail("user@example.com");

            var first = cache.GetAvatarBytesAsync(md5).Result;
            Assert.That(first, Is.Not.Null, "sanity: first lookup should populate the cache");
            Assert.That(cache.FetchedUrls, Has.Count.EqualTo(1));

            var second = cache.GetAvatarBytesAsync(md5).Result;

            Assert.That(second, Is.Not.Null);
            Assert.That(second.Bytes, Is.EqualTo(new byte[] { 7 }));
            // The second lookup must be served from the on-disk cache, so no additional fetch.
            Assert.That(cache.FetchedUrls, Has.Count.EqualTo(1));
        }

        [Test]
        public void GetAvatarBytes_FailedSource_ReturnsNull()
        {
            var cache = new TestableAvatarCache(_folder.Path);
            cache.BytesToReturn = null; // simulate 404 / offline
            var md5 = AvatarCache.Md5OfEmail("nobody@example.com");

            var result = cache.GetAvatarBytesAsync(md5).Result;

            // null → the endpoint returns 404 → the front end shows generated initials.
            Assert.That(result, Is.Null);
            Assert.That(cache.FetchedUrls, Has.Count.EqualTo(1), "sanity: we did attempt a fetch");
        }

        [Test]
        public void GetAvatarBytes_KnownPhotoUrlChanged_Refetches()
        {
            var cache = new TestableAvatarCache(_folder.Path);
            cache.BytesToReturn = new byte[] { 1 };
            var md5 = AvatarCache.Md5OfEmail("user@example.com");

            cache.SetKnownPhotoUrl(md5, "https://photos.example/old.jpg");
            cache.GetAvatarBytesAsync(md5).Wait();
            Assert.That(cache.FetchedUrls.Last(), Is.EqualTo("https://photos.example/old.jpg"));

            // A new login supplies a different Google photo; the next lookup must refetch from it,
            // not serve the stale cached bytes.
            cache.SetKnownPhotoUrl(md5, "https://photos.example/new.jpg");
            cache.BytesToReturn = new byte[] { 2 };
            var result = cache.GetAvatarBytesAsync(md5).Result;

            Assert.That(result.Bytes, Is.EqualTo(new byte[] { 2 }));
            Assert.That(cache.FetchedUrls.Last(), Is.EqualTo("https://photos.example/new.jpg"));
            Assert.That(cache.FetchedUrls, Has.Count.EqualTo(2));
        }

        [Test]
        public void KnownPhotoUrlMap_PersistsAcrossSaveRestore()
        {
            var md5 = AvatarCache.Md5OfEmail("user@example.com");

            var cache1 = new TestableAvatarCache(_folder.Path);
            // Sanity: nothing persisted yet.
            Assert.That(cache1.GetKnownPhotoUrlOrNull(md5), Is.Null);
            cache1.SetKnownPhotoUrl(md5, "https://photos.example/pic.jpg");

            // A fresh instance over the SAME folder simulates restarting Bloom; it must reload the map.
            var cache2 = new TestableAvatarCache(_folder.Path);
            Assert.That(
                cache2.GetKnownPhotoUrlOrNull(md5),
                Is.EqualTo("https://photos.example/pic.jpg")
            );
        }

        [Test]
        public void RemoveKnownPhotoUrl_ClearsMapping_AndRevertsToGravatar()
        {
            var cache = new TestableAvatarCache(_folder.Path);
            cache.BytesToReturn = new byte[] { 1 };
            var md5 = AvatarCache.Md5OfEmail("user@example.com");

            cache.SetKnownPhotoUrl(md5, "https://photos.example/pic.jpg");
            // Sanity: populated before the "logout".
            Assert.That(
                cache.GetKnownPhotoUrlOrNull(md5),
                Is.EqualTo("https://photos.example/pic.jpg")
            );

            cache.RemoveKnownPhotoUrl(md5);

            Assert.That(cache.GetKnownPhotoUrlOrNull(md5), Is.Null);

            // With the mapping gone the person now resolves via Gravatar.
            cache.GetAvatarBytesAsync(md5).Wait();
            Assert.That(cache.FetchedUrls.Last(), Is.EqualTo(AvatarCache.GravatarUrl(md5)));
        }

        [Test]
        public void SetKnownPhotoUrl_WithEmptyOrNull_IsNoOpAndDoesNotThrow()
        {
            var cache = new TestableAvatarCache(_folder.Path);
            var md5 = AvatarCache.Md5OfEmail("user@example.com");

            // Mirrors the login handler's rule for a login WITHOUT a photoUrl (older website build):
            // registering an empty/null value must not create a mapping and must not throw.
            Assert.DoesNotThrow(() => cache.SetKnownPhotoUrl(md5, null));
            Assert.DoesNotThrow(() => cache.SetKnownPhotoUrl(md5, ""));
            Assert.That(cache.GetKnownPhotoUrlOrNull(md5), Is.Null);
        }

        [Test]
        public void GetAvatarBytes_RefreshesOncePerRun()
        {
            var md5 = AvatarCache.Md5OfEmail("user@example.com");

            // First run: two lookups, but only the first fetches; the second is served from cache.
            var run1 = new TestableAvatarCache(_folder.Path);
            run1.BytesToReturn = new byte[] { 1 };
            run1.GetAvatarBytesAsync(md5).Wait();
            run1.GetAvatarBytesAsync(md5).Wait();
            Assert.That(
                run1.FetchedUrls,
                Has.Count.EqualTo(1),
                "within one run we should fetch only once, then serve from cache"
            );

            // A new run (fresh instance over the same folder) re-attempts a fresh fetch even though the
            // bytes are already cached from the previous run -- this is how a changed avatar is picked up
            // by restarting Bloom -- and uses the newly fetched bytes.
            var run2 = new TestableAvatarCache(_folder.Path);
            run2.BytesToReturn = new byte[] { 2 };
            var result = run2.GetAvatarBytesAsync(md5).Result;
            Assert.That(run2.FetchedUrls, Has.Count.EqualTo(1), "a new run should fetch fresh");
            Assert.That(result.Bytes, Is.EqualTo(new byte[] { 2 }), "and use the fresh bytes");
        }

        [Test]
        public void GetAvatarBytes_FetchFailsButHaveCachedCopy_ReturnsCachedCopy()
        {
            var md5 = AvatarCache.Md5OfEmail("user@example.com");

            // First run caches a successful fetch.
            var run1 = new TestableAvatarCache(_folder.Path);
            run1.BytesToReturn = new byte[] { 9 };
            var first = run1.GetAvatarBytesAsync(md5).Result;
            Assert.That(
                first?.Bytes,
                Is.EqualTo(new byte[] { 9 }),
                "sanity: the first run should have cached the avatar"
            );

            // Second run with the fetch failing (offline): we attempt a fresh fetch, it fails, and we
            // fall back to the cached copy rather than showing nothing (initials).
            var run2 = new TestableAvatarCache(_folder.Path);
            run2.BytesToReturn = null; // simulate offline / fetch failure
            var second = run2.GetAvatarBytesAsync(md5).Result;

            Assert.That(
                run2.FetchedUrls,
                Has.Count.EqualTo(1),
                "sanity: we should have attempted a fresh fetch this run"
            );
            Assert.That(
                second,
                Is.Not.Null,
                "should fall back to the cached copy when the fetch fails"
            );
            Assert.That(second.Bytes, Is.EqualTo(new byte[] { 9 }));
        }

        [Test]
        public void ProductionRegistration_UsesAppDataFolder_NotAnUnrelatedRegisteredString()
        {
            // Reproduces ProjectContext's container shape: a plain string is registered (there it is
            // the editable collection directory). AvatarCache's constructor has an OPTIONAL
            // `string cacheFolder = null` parameter, and Autofac injects a registered service into an
            // optional parameter when one exists. This guards the ProjectContext fix (register via a
            // `new AvatarCache()` factory, NOT RegisterType) against being "cleaned up" back into the
            // bug, which would scatter the avatar cache into the user's collection folder.
            var unrelatedDir = _folder.Path; // stands in for editableCollectionDirectory

            // Sanity check that the gotcha is real: RegisterType DOES adopt the registered string as
            // the cache folder. If this ever stops happening, the guard below would pass vacuously.
            var buggyBuilder = new ContainerBuilder();
            buggyBuilder.Register(c => unrelatedDir).InstancePerLifetimeScope();
            buggyBuilder.RegisterType<AvatarCache>().AsSelf().SingleInstance();
            using (var buggy = buggyBuilder.Build())
            {
                Assert.That(
                    buggy.Resolve<AvatarCache>().CacheFolder,
                    Is.EqualTo(unrelatedDir),
                    "sanity: RegisterType is expected to (mis)inject the registered string here"
                );
            }

            // The production registration must NOT adopt that string; it uses the app-data folder.
            var fixedBuilder = new ContainerBuilder();
            fixedBuilder.Register(c => unrelatedDir).InstancePerLifetimeScope();
            fixedBuilder.Register(c => new AvatarCache()).AsSelf().SingleInstance();
            using (var fixedContainer = fixedBuilder.Build())
            {
                var folder = fixedContainer.Resolve<AvatarCache>().CacheFolder;
                Assert.That(
                    folder,
                    Is.Not.EqualTo(unrelatedDir),
                    "AvatarCache must not adopt an unrelated registered string as its cache folder"
                );
                Assert.That(
                    folder,
                    Does.EndWith(Path.Combine("Bloom", "AvatarCache")),
                    "AvatarCache should default to the app-data AvatarCache folder"
                );
            }
        }
    }
}
