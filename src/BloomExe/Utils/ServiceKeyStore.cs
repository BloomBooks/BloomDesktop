using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.Utils
{
    /// <summary>
    /// The one place Bloom keeps a key that belongs to the user rather than to a book, a
    /// collection, or a copy of Bloom: an API key the user fetched from a service's own site
    /// (OpenRouter, Pixabay, ElevenLabs, a translation service, and whatever comes next).
    ///
    /// Two rules shape it.
    ///
    /// It is per Windows user, and independent of the release channel and the build version.
    /// That rules out <see cref="Properties.Settings"/>, whose file is
    /// %LocalAppData%\SIL\&lt;product&gt;\&lt;version&gt;\user.config: the product name carries
    /// the channel (Bloom, BloomAlpha, BloomBeta) and the folder carries the version, so a key
    /// entered in one channel is invisible in the next, and Settings.Upgrade() cannot help
    /// because it only copies a value forward within one channel. This store lives in
    /// <see cref="ProjectContext.GetBloomAppDataFolder"/>, which is %LocalAppData%\SIL\Bloom
    /// whatever the channel or version, so every Bloom the user runs reads the same file.
    ///
    /// It is encrypted with the Windows user login. Each value is protected with DPAPI in
    /// CurrentUser scope, so the file opens only for that Windows account on that computer.
    /// What that buys: a file that is copied, backed up, synced to the cloud, or picked up in a
    /// support log is useless to anyone else. What it does not buy: protection from a program
    /// running as that same Windows user, which can call Unprotect exactly as we do. What it
    /// costs: the keys do not travel to a new computer. Get cannot decrypt a value written
    /// by another account or another machine, and reports it as absent, so the user is asked
    /// for the key again. A caller that has anything better to say than silence should say it.
    ///
    /// Since DPAPI stops nothing that runs as the user, two cheap measures aim at the one
    /// attacker they can reach: the untargeted credential stealer that sweeps a profile for
    /// filenames and words like key, token and api, and that calls CryptUnprotectData on any
    /// blob it finds. First, the file says nothing about itself. It is called services.blm, its
    /// properties are dull ("services", "value", "method"), the method is a bare number, and
    /// there is no note explaining the format -- that explanation lives here, in the source,
    /// where it does a maintainer good and a scavenger none. Second, DPAPI is given
    /// <see cref="kEntropy"/>, so unprotecting a found blob takes knowing about Bloom.
    /// Anyone who reads this file still wins, and that is accepted: Bloom is open source, so
    /// a scheme that had to stay secret was never on offer.
    ///
    /// The format is
    /// <code>{ "version": 1, "services": { "openRouter": { "value": "&lt;base64&gt;", "method": "1" } } }</code>.
    ///
    /// Nothing here knows what any key is for. A caller picks a name and owns its meaning,
    /// so a new service needs no change to this class.
    /// </summary>
    public static class ServiceKeyStore
    {
        private const string kFileName = "services.blm";
        private const int kCurrentFormatVersion = 1;

        /// <summary>
        /// What a key's "method" field says when its value was encrypted the way this version
        /// of Bloom encrypts: Windows DPAPI, CurrentUser scope (not LocalMachine, which any
        /// account on the computer could decrypt), with exactly the bytes in
        /// <see cref="kEntropy"/> as the optional entropy. Those three facts together are what
        /// "1" means, and none of them can change without a new number.
        ///
        /// Every method Bloom ever uses gets its own number here, and the number is recorded on
        /// each key rather than once for the file. That is what makes a later change of method
        /// a migration rather than a loss: a future Bloom reads the field, keeps reading the
        /// keys it recognizes, converts the ones it wants to move, and leaves alone anything
        /// written by a version newer than itself. <see cref="GetProtectionMethod"/> reports
        /// the field without decrypting, so such a pass can see what it is dealing with.
        /// </summary>
        private const string kDpapiCurrentUserProtection = "1";

        /// <summary>
        /// The optional entropy handed to DPAPI along with each value. It is not a secret: it
        /// is right here in the source of an open-source program, and anyone who reads this
        /// file can use it. What it buys is narrow and worth having anyway. A credential
        /// stealer that sweeps a profile and calls CryptUnprotectData on every blob it finds
        /// gets nothing from this file without knowing about Bloom, because DPAPI refuses to
        /// unprotect a value unless it is given the same entropy that protected it.
        ///
        /// These bytes must never change. Every value on every user's disk becomes unreadable
        /// if they do, and Bloom would have no way to tell that from a file belonging to
        /// another Windows account. A later change of entropy is therefore a new method number
        /// (see <see cref="kDpapiCurrentUserProtection"/>) written alongside a reader for the
        /// old one, not an edit to this array.
        /// </summary>
        private static readonly byte[] kEntropy =
        {
            0xC4,
            0xA6,
            0x55,
            0x5E,
            0x6F,
            0x3F,
            0x70,
            0xBA,
            0xFE,
            0x36,
            0x0E,
            0x97,
            0xD3,
            0x13,
            0xA3,
            0xC1,
            0x7A,
            0xBB,
            0xDE,
            0xB3,
            0x46,
            0x0F,
            0x74,
            0x9A,
            0x47,
            0x3F,
            0xB3,
            0x8A,
            0xD5,
            0xAD,
            0x18,
            0x05,
        };

        /// <summary>
        /// The name under which the user's OpenRouter API key is stored (the "Edit with AI"
        /// feature).
        /// </summary>
        public const string kOpenRouterName = "openRouter";

        /// <summary>
        /// The start of the name of every image gallery provider key, for example
        /// "imageGallery.pixabay". The rest of the name is the gallery's own provider id, so
        /// Bloom needs no change when the gallery gains a provider.
        /// </summary>
        public const string kImageGalleryNamePrefix = "imageGallery.";

        /// <summary>
        /// Serializes this process's read-modify-write cycles. Two instances of Bloom are not
        /// serialized against each other, so a key written by one while the other is
        /// writing a different one can be lost. That is acceptable here: a user enters a key
        /// once, by hand, in one window.
        /// </summary>
        private static readonly object s_lock = new object();

        /// <summary>
        /// Set by a test so that it works on a folder of its own. A test must never run
        /// against the real file: it holds the developer's own keys, and a test that wrote
        /// there would destroy them.
        /// </summary>
        internal static string FolderForTests;

        /// <summary>The folder holding the file. See <see cref="FolderForTests"/>.</summary>
        private static string Folder => FolderForTests ?? ProjectContext.GetBloomAppDataFolder();

        /// <summary>The one file, shared by every channel and version.</summary>
        public static string FilePath => Path.Combine(Folder, kFileName);

        /// <summary>
        /// Returns the secret stored under this name, or null if there is none, or if the
        /// stored value cannot be decrypted for this Windows account on this computer.
        /// </summary>
        public static string Get(string name)
        {
            lock (s_lock)
            {
                var store = Load();
                if (!store.Keys.TryGetValue(name, out var storedKey))
                    return null;
                if (string.IsNullOrEmpty(storedKey?.Value))
                    return null;
                if (storedKey.Protection != kDpapiCurrentUserProtection)
                {
                    // Most likely a file written by a newer Bloom that protects keys some
                    // other way. Guessing at the bytes would be worse than asking the user
                    // again, and this version must not overwrite what it cannot read.
                    Logger.WriteEvent(
                        $"ServiceKeyStore: the key '{name}' says it is protected by '{storedKey.Protection}', which this version of Bloom does not know how to read. Treating it as absent."
                    );
                    return null;
                }
                return Unprotect(storedKey.Value);
            }
        }

        /// <summary>
        /// Reports how the key of this name is protected, without decrypting it, or null when
        /// there is no such key. This is the seam a later change of method needs: it can list
        /// the names, ask each how it is protected, and convert only what it means to convert.
        /// </summary>
        public static string GetProtectionMethod(string name)
        {
            lock (s_lock)
            {
                return Load().Keys.TryGetValue(name, out var storedKey)
                    ? storedKey?.Protection
                    : null;
            }
        }

        /// <summary>
        /// True when there is a key of this name and <see cref="Get"/> can actually read it.
        /// A caller that removes the keys the user no longer wants asks this first, because a
        /// key it cannot read never reached the user: its absence from what they are looking at
        /// means nothing, and removing it would throw away a key that is not theirs to lose.
        /// Two kinds of key are unreadable, and both must survive: one protected by a method
        /// only a newer Bloom understands, and one written by another Windows account or on
        /// another computer, which this account cannot decrypt.
        /// </summary>
        public static bool CanRead(string name)
        {
            return Get(name) != null;
        }

        /// <summary>
        /// Stores a secret under this name, replacing any previous one. A null or empty secret
        /// removes the key, which is how a caller handles the user clearing one.
        /// </summary>
        public static void Set(string name, string secret)
        {
            lock (s_lock)
            {
                var store = Load();
                if (string.IsNullOrEmpty(secret))
                {
                    if (!store.Keys.Remove(name))
                        return; // nothing there, so nothing to write
                }
                else
                {
                    store.Keys[name] = new StoredKey
                    {
                        Value = Protect(secret),
                        Protection = kDpapiCurrentUserProtection,
                    };
                }
                Save(store);
            }
        }

        /// <summary>
        /// The names of the keys on file, optionally only those starting with a prefix.
        /// A caller that keeps a family of keys (one per provider, say) uses this to
        /// find them all without knowing in advance which providers the user has keys for.
        /// The names are returned whether or not their values can still be decrypted.
        /// </summary>
        public static IEnumerable<string> GetNames(string namePrefix = null)
        {
            lock (s_lock)
            {
                return Load()
                    .Keys.Keys.Where(name =>
                        string.IsNullOrEmpty(namePrefix)
                        || name.StartsWith(namePrefix, StringComparison.Ordinal)
                    )
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList();
            }
        }

        /// <summary>
        /// Encrypts a string with the Windows user login (DPAPI, CurrentUser scope) and returns
        /// it as base64. Public so that a test can prove the round trip.
        ///
        /// A failure throws, for the same reason <see cref="Save"/> does: the caller is in the
        /// middle of telling the user their key is saved. Bloom targets net8.0-windows, so this
        /// is not expected at all; it becomes real on the day Bloom runs somewhere without
        /// DPAPI, and on that day we want to hear about it rather than have every user quietly
        /// re-enter their key at each launch. Note that <see cref="Unprotect"/> is different: a
        /// value it cannot read is an ordinary thing to find in the file, so it returns null.
        /// </summary>
        public static string Protect(string plaintext)
        {
            var encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext),
                kEntropy,
                DataProtectionScope.CurrentUser
            );
            return Convert.ToBase64String(encrypted);
        }

        /// <summary>
        /// Reverses <see cref="Protect"/>. Returns null when the value cannot be decrypted for
        /// this Windows account on this computer, which is the expected outcome for a file
        /// brought from another computer, another account, or a reinstalled Windows. Public so
        /// that a test can prove the round trip.
        /// </summary>
        public static string Unprotect(string protectedBase64)
        {
            try
            {
                var bytes = ProtectedData.Unprotect(
                    Convert.FromBase64String(protectedBase64),
                    kEntropy,
                    DataProtectionScope.CurrentUser
                );
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception error)
                when (error is CryptographicException || error is FormatException)
            {
                Logger.WriteEvent(
                    $"ServiceKeyStore: a stored key could not be decrypted on this computer and account ({error.Message}). The user must enter it again."
                );
                return null;
            }
        }

        /// <summary>One key as it sits in the file.</summary>
        private class StoredKey
        {
            [JsonProperty("value")]
            public string Value;

            [JsonProperty("method")]
            public string Protection;

            /// <summary>See <see cref="StoreFile.Extra"/>.</summary>
            [JsonExtensionData]
            public IDictionary<string, JToken> Extra;
        }

        /// <summary>The whole file.</summary>
        private class StoreFile
        {
            [JsonProperty("version")]
            public int Version = kCurrentFormatVersion;

            [JsonProperty("services")]
            public Dictionary<string, StoredKey> Keys = new Dictionary<string, StoredKey>();

            /// <summary>
            /// Anything in the file that this version of Bloom has no field for, kept so that
            /// writing the file back does not throw it away. Every write is a whole-file
            /// rewrite, so without this an older Bloom storing one key would quietly strip
            /// whatever a newer one had added -- and the newer Bloom would find its own data
            /// gone with nothing to say why.
            /// </summary>
            [JsonExtensionData]
            public IDictionary<string, JToken> Extra;
        }

        /// <summary>
        /// Reads the file, or reports an empty store when there is none yet. Damaged content is
        /// reported and treated as empty rather than thrown, because losing a saved key is a
        /// smaller harm than a feature that cannot open. Callers hold s_lock.
        /// </summary>
        private static StoreFile Load()
        {
            if (!RobustFile.Exists(FilePath))
                return new StoreFile();
            try
            {
                var store = JsonConvert.DeserializeObject<StoreFile>(
                    RobustFile.ReadAllText(FilePath)
                );
                if (store?.Keys == null)
                    return new StoreFile();
                if (store.Version > kCurrentFormatVersion)
                {
                    // A newer Bloom wrote this. Reading goes ahead anyway, because what
                    // decides whether a value can be decrypted is the "method" on the value
                    // itself, and a newer Bloom has to leave the methods it inherited
                    // readable. Say so in the log, though: if a later format ever changes the
                    // shape of the file rather than adding to it, this line is what tells us
                    // an older Bloom was looking at it.
                    Logger.WriteEvent(
                        $"ServiceKeyStore: {FilePath} says it is format version {store.Version}, and this version of Bloom knows version {kCurrentFormatVersion}. Reading it anyway; each key's own method decides whether it can be read."
                    );
                }
                return store;
            }
            catch (Exception error)
            {
                Logger.WriteError(
                    "ServiceKeyStore could not read " + FilePath + "; treating it as empty",
                    error
                );
                return new StoreFile();
            }
        }

        /// <summary>
        /// Writes the file. A failure throws, on purpose: the caller has just told the user
        /// their key is saved, so swallowing the error would leave them to discover next time
        /// that it never was. Callers hold s_lock.
        /// </summary>
        private static void Save(StoreFile store)
        {
            // Never lower it: a file a newer Bloom wrote keeps saying so, because the number
            // is how that Bloom will know its own format when it next reads the file. Raise a
            // file with no version, or one from before the field existed, to ours.
            store.Version = Math.Max(store.Version, kCurrentFormatVersion);
            RobustFile.WriteAllText(
                FilePath,
                JsonConvert.SerializeObject(store, Formatting.Indented)
            );
        }
    }
}
