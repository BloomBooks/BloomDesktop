using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Bloom.Utils;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Utils
{
    /// <summary>
    /// Tests for <see cref="UserKeyStore"/>.
    ///
    /// Every test works on a temporary folder, set through
    /// <see cref="UserKeyStore.FolderForTests"/>. The real file holds the developer's
    /// own API keys, so a test that wrote there would destroy them.
    ///
    /// The behavior that matters most here is what happens to a key that cannot be
    /// decrypted, which is what a user gets on a new computer: Get reports it as absent rather
    /// than throwing, so the feature asks for the key again.
    /// </summary>
    [TestFixture]
    public class UserKeyStoreTests
    {
        private TemporaryFolder _folder;

        [SetUp]
        public void Setup()
        {
            _folder = new TemporaryFolder("UserKeyStoreTests");
            UserKeyStore.FolderForTests = _folder.Path;
        }

        [TearDown]
        public void TearDown()
        {
            UserKeyStore.FolderForTests = null;
            _folder.Dispose();
        }

        [Test]
        public void Get_NothingStored_ReturnsNull()
        {
            Assert.That(UserKeyStore.Get("someService"), Is.Null);
        }

        [Test]
        public void SetThenGet_ReturnsTheSecret()
        {
            const string secret = "sk-or-v1-EXAMPLE-key_0123456789";

            UserKeyStore.Set("someService", secret);

            // Sanity: the file exists and does not contain the secret in the clear, so the
            // successful read below proves decryption rather than a plain-text round trip.
            Assert.That(
                RobustFile.Exists(UserKeyStore.FilePath),
                Is.True,
                "setup: Set should have written the file"
            );
            Assert.That(
                RobustFile.ReadAllText(UserKeyStore.FilePath),
                Does.Not.Contain(secret),
                "setup: the secret must not be stored in the clear"
            );

            Assert.That(UserKeyStore.Get("someService"), Is.EqualTo(secret));
        }

        [Test]
        public void SetThenGet_UnicodeSecret_RoundTrips()
        {
            // Keys are ASCII, but the encryption is UTF-8 based, so prove non-ASCII survives.
            const string secret = "clé-secrète-日本語-😀";

            UserKeyStore.Set("someService", secret);

            Assert.That(UserKeyStore.Get("someService"), Is.EqualTo(secret));
        }

        [Test]
        public void Set_SecondValue_ReplacesTheFirst()
        {
            UserKeyStore.Set("someService", "first");
            Assert.That(
                UserKeyStore.Get("someService"),
                Is.EqualTo("first"),
                "setup: the first value should be readable before we replace it"
            );

            UserKeyStore.Set("someService", "second");

            Assert.That(UserKeyStore.Get("someService"), Is.EqualTo("second"));
        }

        [Test]
        public void Set_EmptySecret_RemovesTheKey()
        {
            UserKeyStore.Set("someService", "a key");
            Assert.That(
                UserKeyStore.GetNames().ToList(),
                Has.Count.EqualTo(1),
                "setup: the key should be on file before we clear it"
            );

            UserKeyStore.Set("someService", "");

            Assert.That(UserKeyStore.Get("someService"), Is.Null);
            Assert.That(UserKeyStore.GetNames(), Is.Empty);
        }

        [Test]
        public void Set_TwoKeys_KeepsBoth()
        {
            UserKeyStore.Set("serviceOne", "one");
            UserKeyStore.Set("serviceTwo", "two");

            Assert.That(UserKeyStore.Get("serviceOne"), Is.EqualTo("one"));
            Assert.That(UserKeyStore.Get("serviceTwo"), Is.EqualTo("two"));
        }

        [Test]
        public void GetNames_WithPrefix_ReturnsOnlyTheMatchingOnes()
        {
            UserKeyStore.Set("imageGallery.pixabay", "one");
            UserKeyStore.Set("imageGallery.somethingElse", "two");
            UserKeyStore.Set("openRouter", "three");

            var names = UserKeyStore.GetNames("imageGallery.").ToList();

            Assert.That(
                names,
                Is.EqualTo(new[] { "imageGallery.pixabay", "imageGallery.somethingElse" })
            );
        }

        [Test]
        public void Get_ValueThatCannotBeDecrypted_ReturnsNull()
        {
            // The stand-in for a file brought from another computer or another Windows account.
            // Base64 that decodes but is not a DPAPI blob for this user.
            var notADpapiBlob = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            WriteRawFile(
                $"{{'version':1,'services':{{'someService':{{'value':'{notADpapiBlob}','method':'1'}}}}}}"
            );

            Assert.That(
                UserKeyStore.Get("someService"),
                Is.Null,
                "a key that cannot be decrypted here must read as absent, not throw"
            );
        }

        [Test]
        public void Get_ValueThatIsNotEvenBase64_ReturnsNull()
        {
            WriteRawFile(
                "{'version':1,'services':{'someService':{'value':'not base64 !!!','method':'1'}}}"
            );

            Assert.That(UserKeyStore.Get("someService"), Is.Null);
        }

        [Test]
        public void Get_UnknownProtectionMethod_ReturnsNull()
        {
            // What a file written by a future Bloom, or by hand, could look like. Reading the
            // value as if we knew how it was protected would be worse than asking again.
            WriteRawFile(
                "{'version':1,'services':{'someService':{'value':'anything','method':'somethingElse'}}}"
            );

            Assert.That(UserKeyStore.Get("someService"), Is.Null);
        }

        [Test]
        public void Get_DamagedFile_ReturnsNullRatherThanThrowing()
        {
            WriteRawFile("this is not JSON at all");

            Assert.That(UserKeyStore.Get("someService"), Is.Null);
        }

        [Test]
        public void Set_AfterDamagedFile_StillStoresTheSecret()
        {
            WriteRawFile("this is not JSON at all");

            UserKeyStore.Set("someService", "a key");

            Assert.That(UserKeyStore.Get("someService"), Is.EqualTo("a key"));
        }

        [Test]
        public void Set_TheFileRecordsTheMethodAndNothingChatty()
        {
            // Each value carries the code for how it was encrypted, so that a change of method
            // is a migration rather than a loss. What the code means is written in the Bloom
            // source and deliberately not in the file: an explanation there would tell a
            // scavenger what it had found and a maintainer nothing they cannot read in
            // UserKeyStore.
            UserKeyStore.Set("someService", "a key");

            var fileText = RobustFile.ReadAllText(UserKeyStore.FilePath);

            Assert.That(
                fileText,
                Does.Contain("\"method\": \"1\""),
                "each value must record the method it was encrypted with"
            );
            Assert.That(fileText, Does.Not.Contain("about"), "the file must not explain itself");
        }

        [Test]
        public void Set_TheFileUsesNoGiveawayWords()
        {
            // The one thing obscurity buys here: an untargeted credential stealer sweeping the
            // profile for files whose names or contents say key, token or api passes this one
            // over. Anyone who reads Bloom's source still finds it, and that is accepted.
            UserKeyStore.Set("someService", "a key");
            Assert.That(
                UserKeyStore.Get("someService"),
                Is.EqualTo("a key"),
                "setup: the key must really be stored, or this proves nothing"
            );

            // The encrypted values are base64 of random-looking bytes, so any of these words
            // can turn up inside one by chance. They are not what a scavenger reads, so strip
            // them before looking at the words the file itself chose.
            var fileText = System.Text.RegularExpressions.Regex.Replace(
                RobustFile.ReadAllText(UserKeyStore.FilePath),
                "\"value\": \"[^\"]*\"",
                "\"value\": \"\""
            );

            foreach (var giveaway in new[] { "key", "token", "secret", "password", "api", "dpapi" })
            {
                Assert.That(
                    fileText.ToLowerInvariant(),
                    Does.Not.Contain(giveaway),
                    $"the file's own words must not include '{giveaway}'"
                );
            }
            Assert.That(
                Path.GetFileName(UserKeyStore.FilePath).ToLowerInvariant(),
                Does.Not.Contain("key"),
                "nor must its name"
            );
        }

        [Test]
        public void GetProtectionMethod_ReportsTheMethodWithoutDecrypting()
        {
            UserKeyStore.Set("someService", "a key");

            Assert.That(UserKeyStore.GetProtectionMethod("someService"), Is.EqualTo("1"));
        }

        [Test]
        public void GetProtectionMethod_MethodThisVersionCannotRead_StillReportsIt()
        {
            // What a migration pass needs: Get refuses the value, but the method is still
            // legible, so the pass can see what it is dealing with and leave it alone.
            WriteRawFile(
                "{'version':1,'services':{'someService':{'value':'anything','method':'some-future-method'}}}"
            );

            Assert.That(
                UserKeyStore.Get("someService"),
                Is.Null,
                "setup: this version must refuse a method it does not know"
            );
            Assert.That(
                UserKeyStore.GetProtectionMethod("someService"),
                Is.EqualTo("some-future-method")
            );
        }

        [Test]
        public void GetProtectionMethod_NoSuchKey_ReturnsNull()
        {
            Assert.That(UserKeyStore.GetProtectionMethod("someService"), Is.Null);
        }

        [Test]
        public void CanRead_KeyThisVersionWrote_IsTrue()
        {
            UserKeyStore.Set("someService", "a-secret");

            Assert.That(UserKeyStore.CanRead("someService"), Is.True);
        }

        [Test]
        public void CanRead_NoSuchKey_IsFalse()
        {
            Assert.That(UserKeyStore.CanRead("someService"), Is.False);
        }

        [Test]
        public void CanRead_MethodThisVersionCannotRead_IsFalse()
        {
            // A caller that removes keys the user cleared asks this before removing one, so
            // that a key a newer Bloom protected some other way survives.
            WriteRawFile(
                "{ 'version': 1, 'services': { 'someService': { 'value': 'AAAA',"
                    + " 'method': 'something-a-later-bloom-invented' } } }"
            );

            Assert.That(UserKeyStore.CanRead("someService"), Is.False);
            Assert.That(
                UserKeyStore.GetProtectionMethod("someService"),
                Is.EqualTo("something-a-later-bloom-invented"),
                "sanity: the key is on file, it is only unreadable"
            );
        }

        [Test]
        public void CanRead_ValueThisAccountCannotDecrypt_IsFalse()
        {
            // What a file copied from another computer or another Windows account looks like:
            // the protection method is one this Bloom knows, but the value will not decrypt.
            // Such a key must survive, so a caller that removes cleared keys leaves it alone.
            WriteRawFile(
                "{ 'version': 1, 'services': { 'someService': { 'value': 'bm90LWEtcHJvdGVjdGVkLWJsb2I=',"
                    + " 'method': '1' } } }"
            );
            Assert.That(
                UserKeyStore.GetProtectionMethod("someService"),
                Is.EqualTo("1"),
                "sanity: the key is on file with a protection method this version knows"
            );

            Assert.That(UserKeyStore.CanRead("someService"), Is.False);
        }

        [Test]
        public void ProtectThenUnprotect_RoundTripsThePlaintext()
        {
            const string original = "sk-or-v1-EXAMPLE-key_0123456789";

            var protectedText = UserKeyStore.Protect(original);

            // Sanity: encryption actually transformed the value, so the round trip below is
            // meaningful and is not just echoing the plaintext back.
            Assert.That(
                protectedText,
                Is.Not.EqualTo(original),
                "setup: Protect should not return the plaintext unchanged"
            );
            Assert.DoesNotThrow(
                () => Convert.FromBase64String(protectedText),
                "setup: Protect must produce base64, because that is what we store"
            );

            Assert.That(UserKeyStore.Unprotect(protectedText), Is.EqualTo(original));
        }

        [Test]
        public void Unprotect_BlobMadeWithoutBloomEntropy_ReturnsNull()
        {
            // Bloom hands DPAPI a fixed extra input, so a tool that finds an encrypted value and
            // calls CryptUnprotectData on it the obvious way gets nothing. This test is what
            // proves that extra input is actually in play.
            const string original = "sk-or-v1-EXAMPLE-key_0123456789";
            var bytes = System.Text.Encoding.UTF8.GetBytes(original);
            var blobWithNoEntropy = ProtectedData.Protect(
                bytes,
                null,
                DataProtectionScope.CurrentUser
            );

            // Sanity: this blob is perfectly good DPAPI for this user, so the refusal below is
            // about the entropy and not about a blob Windows could never have read.
            Assert.That(
                System.Text.Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(
                        blobWithNoEntropy,
                        null,
                        DataProtectionScope.CurrentUser
                    )
                ),
                Is.EqualTo(original),
                "setup: DPAPI itself must be able to read this blob"
            );

            Assert.That(UserKeyStore.Unprotect(Convert.ToBase64String(blobWithNoEntropy)), Is.Null);
        }

        /// <summary>
        /// Writes the file as given, so a test can set up content Bloom itself would not
        /// write. Single quotes stand in for double quotes, to keep the test strings readable.
        /// </summary>
        private void WriteRawFile(string contentWithSingleQuotes)
        {
            RobustFile.WriteAllText(
                UserKeyStore.FilePath,
                contentWithSingleQuotes.Replace('\'', '"')
            );
        }
    }
}
