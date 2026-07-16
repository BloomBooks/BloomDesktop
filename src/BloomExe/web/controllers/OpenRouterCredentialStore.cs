using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Bloom.web.controllers
{
    /// <summary>
    /// Per-user storage for the OpenRouter credentials used by the "Edit with AI…" feature.
    ///
    /// The key is the user's, tied to their OpenRouter billing account — not to a book or
    /// collection — so it is stored per-user in <see cref="Properties.Settings"/>
    /// (%LocalAppData%\SIL\Bloom\user.config) rather than travelling with a book or
    /// collection that gets shared or uploaded. Bloom is the single source of truth: the
    /// editor library no longer persists the key itself; it hands a newly obtained key up to
    /// Bloom (via aiImageEditor/saveCredentials) and Bloom supplies it back in the launch
    /// payload on each launch.
    ///
    /// The API key is encrypted at rest with Windows DPAPI (CurrentUser scope) so that a
    /// copied or cloud-synced user.config is useless on another machine or account. This is
    /// cheap insurance, not a guarantee: it does not defend against malware running as the
    /// same Windows user. Bloom currently targets net8.0-windows; when it becomes an Electron
    /// app on Mac/Linux this encryption step will need a per-platform equivalent.
    /// </summary>
    public static class OpenRouterCredentialStore
    {
        /// <summary>
        /// Saves (or, when <paramref name="apiKey"/> is null/empty, clears) the user's
        /// OpenRouter API key. The key is DPAPI-encrypted before being written. Only the
        /// API key is persisted: Bloom supports the API-key method, so the OAuth auth
        /// method and OpenRouter user name the editor may send are not stored.
        /// </summary>
        public static void Save(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Clear();
                return;
            }

            var settings = Properties.Settings.Default;
            settings.OpenRouterApiKey = Protect(apiKey);
            settings.Save();
        }

        /// <summary>Clears the stored OpenRouter API key (e.g. on sign-out).</summary>
        public static void Clear()
        {
            var settings = Properties.Settings.Default;
            settings.OpenRouterApiKey = "";
            settings.Save();
        }

        /// <summary>
        /// Returns the decrypted OpenRouter API key, or null if none is stored or the stored
        /// blob cannot be decrypted on this machine/account (e.g. a config copied from
        /// elsewhere). A non-decryptable blob is treated as "no key" — the user simply signs
        /// in again — rather than an error, because that is the expected outcome of DPAPI's
        /// machine/account binding.
        /// </summary>
        public static string GetApiKey()
        {
            var stored = Properties.Settings.Default.OpenRouterApiKey;
            if (string.IsNullOrEmpty(stored))
                return null;
            return Unprotect(stored);
        }

        /// <summary>
        /// Encrypts a string with Windows DPAPI (CurrentUser scope) and returns it as base64.
        /// Public for round-trip unit testing.
        /// </summary>
        public static string Protect(string plaintext)
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        /// <summary>
        /// Reverses <see cref="Protect"/>. Returns null if the base64/DPAPI blob can't be
        /// decrypted on this machine/account. Public for round-trip unit testing.
        /// </summary>
        public static string Unprotect(string protectedBase64)
        {
            try
            {
                var encrypted = Convert.FromBase64String(protectedBase64);
                var bytes = ProtectedData.Unprotect(
                    encrypted,
                    null,
                    DataProtectionScope.CurrentUser
                );
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex) when (ex is CryptographicException || ex is FormatException)
            {
                Debug.WriteLine(
                    $"OpenRouterCredentialStore: stored key could not be decrypted ({ex.Message}); treating as absent."
                );
                return null;
            }
        }
    }
}
