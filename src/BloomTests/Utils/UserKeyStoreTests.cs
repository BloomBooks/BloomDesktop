using System;
using System.IO;
using System.Linq;
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
                $"{{'version':1,'keys':{{'someService':{{'value':'{notADpapiBlob}','protection':'windows-dpapi-currentuser'}}}}}}"
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
                "{'version':1,'keys':{'someService':{'value':'not base64 !!!','protection':'windows-dpapi-currentuser'}}}"
            );

            Assert.That(UserKeyStore.Get("someService"), Is.Null);
        }

        [Test]
        public void Get_UnknownProtectionMethod_ReturnsNull()
        {
            // What a file written by a future Bloom, or by hand, could look like. Reading the
            // value as if we knew how it was protected would be worse than asking again.
            WriteRawFile(
                "{'version':1,'keys':{'someService':{'value':'anything','protection':'somethingElse'}}}"
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
        public void Set_TheFileSaysHowTheValueIsEncrypted()
        {
            // A future Bloom, or a person looking at the file, must be able to tell how each
            // value was encrypted without reading the Bloom source that wrote it. That is what
            // makes a change of method a migration rather than a loss.
            UserKeyStore.Set("someService", "a key");

            var fileText = RobustFile.ReadAllText(UserKeyStore.FilePath);

            Assert.That(
                fileText,
                Does.Contain("windows-dpapi-currentuser"),
                "each key must name its own protection method"
            );
            Assert.That(
                fileText,
                Does.Contain("about"),
                "the file must carry a note explaining what that method means"
            );
        }

        [Test]
        public void GetProtectionMethod_ReportsTheMethodWithoutDecrypting()
        {
            UserKeyStore.Set("someService", "a key");

            Assert.That(
                UserKeyStore.GetProtectionMethod("someService"),
                Is.EqualTo("windows-dpapi-currentuser")
            );
        }

        [Test]
        public void GetProtectionMethod_MethodThisVersionCannotRead_StillReportsIt()
        {
            // What a migration pass needs: Get refuses the value, but the method is still
            // legible, so the pass can see what it is dealing with and leave it alone.
            WriteRawFile(
                "{'version':1,'keys':{'someService':{'value':'anything','protection':'some-future-method'}}}"
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
