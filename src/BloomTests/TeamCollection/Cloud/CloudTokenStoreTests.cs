using System;
using System.IO;
using Bloom.TeamCollection.Cloud;
using NUnit.Framework;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// Unit tests for <see cref="DpapiCloudTokenStore"/> (task 12's persistent token store).
    /// Runs against a real temp file + real Windows DPAPI -- there is nothing to mock here (no
    /// network, no server), and both BloomExe and BloomTests are Windows-only builds
    /// (net8.0-windows), so this is a genuine, always-applicable unit test rather than a
    /// platform-guarded one.
    /// </summary>
    [TestFixture]
    public class DpapiCloudTokenStoreTests
    {
        private string _tempFilePath;

        [SetUp]
        public void Setup()
        {
            _tempFilePath = Path.Combine(
                Path.GetTempPath(),
                "BloomDpapiCloudTokenStoreTests-" + Guid.NewGuid().ToString("N") + ".dat"
            );
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFilePath))
                File.Delete(_tempFilePath);
        }

        private static CloudSession MakeSession() =>
            new CloudSession
            {
                AccessToken = "access-1",
                RefreshToken = "refresh-1",
                Email = "alice@example.com",
                UserId = "firebase-uid-1",
                ExpiresAtUtc = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc),
                EmailVerified = true,
            };

        [Test]
        public void Load_NoFileYet_ReturnsNull()
        {
            var store = new DpapiCloudTokenStore(_tempFilePath);

            Assert.That(store.Load(), Is.Null);
        }

        [Test]
        public void SaveThenLoad_RoundTripsAllFields()
        {
            var store = new DpapiCloudTokenStore(_tempFilePath);
            var original = MakeSession();

            store.Save(original);
            var loaded = store.Load();

            Assert.That(loaded, Is.Not.Null, "a session was just saved; loading it must not fail");
            Assert.That(loaded.AccessToken, Is.EqualTo(original.AccessToken));
            Assert.That(loaded.RefreshToken, Is.EqualTo(original.RefreshToken));
            Assert.That(loaded.Email, Is.EqualTo(original.Email));
            Assert.That(loaded.UserId, Is.EqualTo(original.UserId));
            Assert.That(loaded.ExpiresAtUtc, Is.EqualTo(original.ExpiresAtUtc));
            Assert.That(loaded.EmailVerified, Is.EqualTo(original.EmailVerified));
        }

        [Test]
        public void Save_EncryptsOnDisk_PlainRefreshTokenNotRecoverableFromRawBytes()
        {
            var store = new DpapiCloudTokenStore(_tempFilePath);
            store.Save(MakeSession());

            var rawBytes = File.ReadAllBytes(_tempFilePath);
            var rawText = System.Text.Encoding.UTF8.GetString(rawBytes);

            // The whole point of DPAPI here: the refresh token must not appear in the clear
            // anywhere in the file (this is the "NOT plain text, NOT in user.config" requirement
            // from the task brief).
            Assert.That(rawText, Does.Not.Contain("refresh-1"));
            Assert.That(rawText, Does.Not.Contain("alice@example.com"));
        }

        [Test]
        public void Clear_DeletesFile_SubsequentLoadReturnsNull()
        {
            var store = new DpapiCloudTokenStore(_tempFilePath);
            store.Save(MakeSession());
            Assert.That(
                File.Exists(_tempFilePath),
                Is.True,
                "sanity check: save must create the file"
            );

            store.Clear();

            Assert.That(File.Exists(_tempFilePath), Is.False);
            Assert.That(store.Load(), Is.Null);
        }

        [Test]
        public void Clear_NoFileYet_DoesNotThrow()
        {
            var store = new DpapiCloudTokenStore(_tempFilePath);

            Assert.DoesNotThrow(() => store.Clear());
        }

        [Test]
        public void Load_CorruptedFile_ReturnsNullRatherThanThrowing()
        {
            File.WriteAllBytes(_tempFilePath, new byte[] { 1, 2, 3, 4, 5 });
            var store = new DpapiCloudTokenStore(_tempFilePath);

            Assert.That(
                store.Load(),
                Is.Null,
                "corrupted/undecryptable data must be treated as 'no stored session', not crash"
            );
        }
    }
}
