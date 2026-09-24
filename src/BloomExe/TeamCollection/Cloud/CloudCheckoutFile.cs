using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using SIL.IO;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// The `.checkout` record a cloud Team Collection keeps in a book folder while that book is
    /// checked out IN THIS COPY of the collection (CONTRACTS.md v1.9, "checkout GUID"). The
    /// server hands the checkout GUID only to the client that took the lock, and stores only its
    /// hash (tc.books.checkout_guid_hash, returned to every member as checkoutGuidHash); a copy
    /// of the book folder is "the one the book is checked out in" exactly when this file holds
    /// the GUID whose hash the server reports. Because the record travels with the book folder,
    /// the checkout survives the collection folder being moved, renamed or copied to another
    /// computer, and a duplicated collection folder has two copies of it (the first one to check
    /// in wins; the other becomes obsolete and is cancelled).
    ///
    /// The file is JSON: { "version": 1, "checkoutGuid", "bookId", "collectionId", "userEmail",
    /// "checkedOutAt" (ISO UTC) }. It is named without a ".json" extension on purpose (the book
    /// file filter whitelists .json at the book level), and it is never uploaded, packaged,
    /// copied into duplicates/publications, or zipped into recovery copies (see
    /// TeamCollection.AddTCSpecificFiles and the cloud upload/recovery paths). An unreadable or
    /// corrupt file is treated exactly like a missing one.
    /// </summary>
    public class CloudCheckoutFile
    {
        /// <summary>Name of the record inside the book folder.</summary>
        public const string FileName = ".checkout";

        private const int kCurrentVersion = 1;

        /// <summary>The checkout GUID (canonical lowercase form); v1.10: made by this client before it asks for the checkout.</summary>
        public string CheckoutGuid { get; set; }

        /// <summary>The server book id (tc.books.id) the checkout is for.</summary>
        public string BookId { get; set; }

        /// <summary>The cloud collection id the book belongs to.</summary>
        public string CollectionId { get; set; }

        /// <summary>The account that holds the checkout (rewritten on an account-switch
        /// takeover, which keeps the same GUID). Informational only.</summary>
        public string UserEmail { get; set; }

        /// <summary>When the checkout was taken (UTC). Informational only.</summary>
        public DateTime CheckedOutAtUtc { get; set; }

        /// <summary>Full path of the record for the book folder <paramref name="bookFolderPath"/>.</summary>
        public static string GetPath(string bookFolderPath) =>
            Path.Combine(bookFolderPath, FileName);

        /// <summary>
        /// Reads the record from <paramref name="bookFolderPath"/>, or returns null when there is
        /// none, or it can't be read or parsed, or it has no GUID (corrupt = missing).
        /// </summary>
        public static CloudCheckoutFile Read(string bookFolderPath)
        {
            var path = GetPath(bookFolderPath);
            if (!RobustFile.Exists(path))
                return null;
            try
            {
                var json = JObject.Parse(RobustFile.ReadAllText(path, Encoding.UTF8));
                var guid = (string)json["checkoutGuid"];
                if (string.IsNullOrWhiteSpace(guid))
                    return null;
                return new CloudCheckoutFile
                {
                    CheckoutGuid = guid,
                    BookId = (string)json["bookId"],
                    CollectionId = (string)json["collectionId"],
                    UserEmail = (string)json["userEmail"],
                    CheckedOutAtUtc = ReadUtcTime(json["checkedOutAt"]),
                };
            }
            catch (Exception)
            {
                // Unreadable/corrupt counts as missing (see the class comment).
                return null;
            }
        }

        /// <summary>The (informational) checkedOutAt time; JObject.Parse has usually already
        /// turned the ISO string into a Date token. DateTime.MinValue when absent/unparseable.</summary>
        private static DateTime ReadUtcTime(JToken token)
        {
            if (token == null)
                return DateTime.MinValue;
            if (token.Type == JTokenType.Date)
                return ((DateTime)token).ToUniversalTime();
            return DateTime.TryParse(
                (string)token,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var parsed
            )
                ? parsed
                : DateTime.MinValue;
        }

        /// <summary>The GUID recorded in <paramref name="bookFolderPath"/>'s record, or null.</summary>
        public static string ReadGuid(string bookFolderPath) => Read(bookFolderPath)?.CheckoutGuid;

        /// <summary>Writes (or replaces) this record in <paramref name="bookFolderPath"/>.</summary>
        public void Write(string bookFolderPath)
        {
            var json = new JObject
            {
                ["version"] = kCurrentVersion,
                ["checkoutGuid"] = CheckoutGuid,
                ["bookId"] = BookId,
                ["collectionId"] = CollectionId,
                ["userEmail"] = UserEmail,
                ["checkedOutAt"] = CheckedOutAtUtc
                    .ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
            };
            // No byte-order mark: other tools (and the E2E harness) read this as plain JSON.
            // Read() tolerates one anyway, since Encoding.UTF8 strips a leading BOM.
            RobustFile.WriteAllText(
                GetPath(bookFolderPath),
                json.ToString(),
                new UTF8Encoding(false)
            );
        }

        /// <summary>Removes the record from <paramref name="bookFolderPath"/>, if there is one.</summary>
        public static void Delete(string bookFolderPath)
        {
            var path = GetPath(bookFolderPath);
            if (RobustFile.Exists(path))
                RobustFile.Delete(path);
        }

        /// <summary>
        /// The server's stored form of a checkout GUID (CONTRACTS.md v1.9): lowercase hex SHA-256
        /// of the UTF-8 bytes of the GUID's lowercase string form. Must match the SQL
        /// <c>encode(sha256(convert_to(lower(guid), 'UTF8')), 'hex')</c> exactly.
        /// </summary>
        public static string HashGuid(string checkoutGuid)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(checkoutGuid.ToLowerInvariant()));
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
        }

        /// <summary>
        /// True when <paramref name="bookFolderPath"/> holds a readable record whose GUID hashes
        /// to <paramref name="serverCheckoutGuidHash"/> (the server's current checkoutGuidHash for
        /// the book); false when either is missing or they differ.
        /// </summary>
        public static bool MatchesServerHash(string bookFolderPath, string serverCheckoutGuidHash)
        {
            if (string.IsNullOrEmpty(serverCheckoutGuidHash))
                return false;
            var guid = ReadGuid(bookFolderPath);
            return guid != null
                && string.Equals(
                    HashGuid(guid),
                    serverCheckoutGuidHash,
                    StringComparison.OrdinalIgnoreCase
                );
        }
    }
}
