using System;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for the DPAPI encrypt/decrypt core of <see cref="OpenRouterCredentialStore"/>.
    /// These exercise the two public helpers directly; the Save/Clear/Get methods are not
    /// tested here because they read and write the real per-user Properties.Settings singleton
    /// (and thus the machine's user.config), which we don't want a unit test to mutate.
    ///
    /// The important behavior these lock down is the one E5 in the manual test plan depends on:
    /// a stored blob that cannot be decrypted on this machine/account (e.g. a user.config copied
    /// from another computer) is treated as "no key" — Unprotect returns null — rather than
    /// throwing, so the user is simply asked to sign in again.
    /// </summary>
    [TestFixture]
    public class OpenRouterCredentialStoreTests
    {
        [Test]
        public void ProtectThenUnprotect_RoundTripsThePlaintext()
        {
            const string original = "sk-or-v1-EXAMPLE-key_0123456789";

            var protectedText = OpenRouterCredentialStore.Protect(original);

            // Sanity: encryption actually transformed the value, so a successful round-trip
            // below is meaningful and isn't just echoing the plaintext back.
            Assert.That(
                protectedText,
                Is.Not.EqualTo(original),
                "setup: Protect should not return the plaintext unchanged"
            );

            var recovered = OpenRouterCredentialStore.Unprotect(protectedText);

            Assert.That(recovered, Is.EqualTo(original));
        }

        [Test]
        public void ProtectThenUnprotect_RoundTripsUnicode()
        {
            // Keys are ASCII, but the encryption is UTF-8 based, so prove non-ASCII survives too.
            const string original = "clé-secrète-日本語-😀";

            var recovered = OpenRouterCredentialStore.Unprotect(
                OpenRouterCredentialStore.Protect(original)
            );

            Assert.That(recovered, Is.EqualTo(original));
        }

        [Test]
        public void Protect_ProducesValidBase64()
        {
            var protectedText = OpenRouterCredentialStore.Protect("anything");

            // Should be storable as-is in user.config; Convert.FromBase64String must accept it.
            Assert.DoesNotThrow(() => Convert.FromBase64String(protectedText));
        }

        [Test]
        public void Unprotect_NonBase64Input_ReturnsNull()
        {
            // A stored value that isn't even base64 (FormatException path).
            var result = OpenRouterCredentialStore.Unprotect("this is not base64 !!!");

            Assert.That(
                result,
                Is.Null,
                "a malformed (non-base64) stored blob must be treated as absent, not throw"
            );
        }

        [Test]
        public void Unprotect_ValidBase64ButNotADpapiBlob_ReturnsNull()
        {
            // Base64 that decodes fine but is not a DPAPI blob for this user
            // (the CryptographicException path). This is the stand-in for a user.config
            // copied from another machine/account.
            var notADpapiBlob = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

            var result = OpenRouterCredentialStore.Unprotect(notADpapiBlob);

            Assert.That(
                result,
                Is.Null,
                "a blob that can't be decrypted on this machine/account must be treated as absent"
            );
        }
    }
}
