using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// Persists a <see cref="CloudSession"/> (in particular its refresh token) to a small file
    /// under Bloom's app-data folder, encrypted with Windows DPAPI (CurrentUser scope) so it
    /// cannot be read by another Windows account on the same machine, nor casually by opening
    /// the file in a text editor. This is the "persistent token store" GOING-LIVE.md Phase 3.4
    /// calls out as something the local provider deliberately skips: it is what lets a real Cloud
    /// Team Collection session survive a Bloom restart without asking the user to sign in again
    /// (CloudAuth.InitializeAtStartup calls <see cref="Load"/> and, on success, refreshes it).
    ///
    /// DPAPI (System.Security.Cryptography.ProtectedData) is Windows-only, which is fine here:
    /// Bloom itself is Windows-only (BloomExe.csproj targets net8.0-windows), so there is no
    /// cross-platform concern to guard against, unlike a library that might run elsewhere.
    /// </summary>
    public class DpapiCloudTokenStore : ICloudTokenStore
    {
        private readonly string _filePath;

        public DpapiCloudTokenStore()
            : this(
                Path.Combine(
                    ProjectContext.GetBloomAppDataFolder(),
                    "CloudTeamCollectionSession.dat"
                )
            ) { }

        /// <summary>Test-only: lets tests point at a temp file instead of the real app-data
        /// folder, so tests never touch a real user's stored session.</summary>
        internal DpapiCloudTokenStore(string filePath)
        {
            _filePath = filePath;
        }

        /// <summary>
        /// Reads and decrypts the stored session, or null if there is none or it could not be
        /// read/decrypted (e.g. corrupted, or encrypted under a different Windows user profile).
        /// Never throws: a failure here is exactly the "please sign in again" case
        /// CloudAuth.InitializeAtStartup already handles gracefully for a missing/invalid
        /// refresh token, so callers don't need a separate code path for it.
        /// </summary>
        public CloudSession Load()
        {
            if (!RobustFile.Exists(_filePath))
                return null;

            try
            {
                var encrypted = RobustFile.ReadAllBytes(_filePath);
                var json = ProtectedData.Unprotect(
                    encrypted,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser
                );
                return JsonConvert.DeserializeObject<CloudSession>(Encoding.UTF8.GetString(json));
            }
            catch (Exception e)
            {
                Logger.WriteError(
                    "DpapiCloudTokenStore: failed to load the stored session; treating as signed out",
                    e
                );
                return null;
            }
        }

        /// <summary>
        /// Encrypts and writes the session, overwriting whatever was stored before. Best-effort:
        /// a failure to persist must not break the sign-in that just succeeded in memory (the
        /// user simply has to sign in again after a restart, no worse than today).
        /// </summary>
        public void Save(CloudSession session)
        {
            try
            {
                var json = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(session));
                var encrypted = ProtectedData.Protect(
                    json,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser
                );
                RobustFile.WriteAllBytes(_filePath, encrypted);
            }
            catch (Exception e)
            {
                Logger.WriteError("DpapiCloudTokenStore: failed to persist the session", e);
            }
        }

        /// <summary>Deletes the stored session file, if any. Best-effort, same rationale as
        /// <see cref="Save"/>: sign-out must succeed in memory regardless of disk state.</summary>
        public void Clear()
        {
            try
            {
                if (RobustFile.Exists(_filePath))
                    RobustFile.Delete(_filePath);
            }
            catch (Exception e)
            {
                Logger.WriteError("DpapiCloudTokenStore: failed to delete the stored session", e);
            }
        }
    }
}
