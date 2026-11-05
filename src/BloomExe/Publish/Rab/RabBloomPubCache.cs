using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bloom.Book;
using Bloom.Publish.BloomPub;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.IO;

namespace Bloom.Publish.Rab
{
    /// <summary>
    /// Decides whether a BloomPUB that the Apps screen made earlier still matches its book, so it
    /// can be reused, and its size reported as the actual size, instead of being made again
    /// (BL-17062). Before this, every BloomPUB was thrown away each time the Apps screen opened.
    ///
    /// Next to each BloomPUB we keep a small record (<c>name.bloompub.cache.json</c>) of what it
    /// was made from:
    /// - the version of Bloom that made it, since a different Bloom may make a different BloomPUB;
    /// - the publish settings that affect it (languages, motion book, picture resolution);
    /// - the book's content hash, the same one BloomPUBs carry in version.txt and Team
    ///   Collections use to notice changes (<see cref="Book.Book.ComputeHashForAllBookRelatedFiles"/>);
    /// - a cheap "signature" of the book's files (names, sizes and modified times).
    /// Working out the content hash means reading every file in the book, which is slow for a
    /// book with big pictures, so we only do it when the signature has changed. Bloom often
    /// resaves a book without changing it, which changes the signature but not the hash; then we
    /// update the record's signature so the next check is cheap again.
    /// </summary>
    /// <remarks>
    /// The content hash ignores meta.json, which does go into the BloomPUB, so the record also
    /// keeps a hash of meta.json without its editor-state fields (see ComputeMetadataHash). Both
    /// leave out PDFs, which a BloomPUB leaves out too.
    /// </remarks>
    public class RabBloomPubCache
    {
        internal const string kRecordSuffix = ".cache.json";

        /// <summary>
        /// What we know about a book just before a BloomPUB is made from it.
        /// </summary>
        public class BookSnapshot
        {
            public string ContentHash;
            public string FileSignature;
            public string MetadataHash;
        }

        /// <summary>
        /// The record stored next to a BloomPUB.
        /// </summary>
        internal class Record
        {
            public string BloomVersion;
            public string SettingsKey;
            public string ContentHash;
            public string FileSignature;
            public string MetadataHash;
        }

        // meta.json fields that only record the state of the editor, not the book.
        private static readonly string[] kEditorStateMetadataFields =
        {
            "currentTool",
            "toolboxIsOpen",
        };

        // The modified time of Bloom.dll makes every developer build count as a new version, not
        // just each release.
        private static readonly Lazy<string> s_bloomVersion = new Lazy<string>(() =>
        {
            var assembly = typeof(RabBloomPubCache).Assembly;
            return assembly.GetName().Version
                + "|"
                + new FileInfo(assembly.Location).LastWriteTimeUtc.Ticks;
        });

        /// <summary>
        /// The path of the record kept next to the BloomPUB at <paramref name="bloomPubPath"/>.
        /// </summary>
        public static string GetRecordPath(string bloomPubPath) => bloomPubPath + kRecordSuffix;

        /// <summary>
        /// A string that changes whenever a publish setting that affects the BloomPUB changes.
        /// </summary>
        public static string MakeSettingsKey(BloomPubPublishSettings settings)
        {
            return string.Join(
                "|",
                string.Join(
                    ",",
                    settings.LanguagesToInclude.OrderBy(lang => lang, StringComparer.Ordinal)
                ),
                string.Join(
                    ",",
                    settings.AudioLanguagesToExclude.OrderBy(lang => lang, StringComparer.Ordinal)
                ),
                settings.PublishAsMotionBookIfApplicable,
                settings.ImagePublishSettings.MaxWidth,
                settings.ImagePublishSettings.MaxHeight
            );
        }

        /// <summary>
        /// Capture the state of the book in <paramref name="bookFolderPath"/>. Call this before
        /// making a BloomPUB from it, so an edit made while the BloomPUB is being made shows up as
        /// a change next time.
        /// </summary>
        public BookSnapshot TakeSnapshot(string bookFolderPath)
        {
            return new BookSnapshot
            {
                FileSignature = MakeFileSignature(bookFolderPath),
                ContentHash = ComputeContentHash(bookFolderPath),
                MetadataHash = ComputeMetadataHash(bookFolderPath),
            };
        }

        /// <summary>
        /// Write the record for a BloomPUB just made from a book in the state
        /// <paramref name="snapshot"/> describes, with publish settings
        /// <paramref name="settingsKey"/> (from <see cref="MakeSettingsKey"/>).
        /// </summary>
        public void RecordNewBloomPub(
            string bloomPubPath,
            BookSnapshot snapshot,
            string settingsKey
        )
        {
            WriteRecord(
                bloomPubPath,
                new Record
                {
                    BloomVersion = s_bloomVersion.Value,
                    SettingsKey = settingsKey,
                    ContentHash = snapshot.ContentHash,
                    FileSignature = snapshot.FileSignature,
                    MetadataHash = snapshot.MetadataHash,
                }
            );
        }

        /// <summary>
        /// Delete the BloomPUB at <paramref name="bloomPubPath"/>, if any, and its record.
        /// The record goes first, so that a BloomPUB is never left with a record it doesn't match.
        /// </summary>
        public static void Delete(string bloomPubPath)
        {
            RobustFile.Delete(GetRecordPath(bloomPubPath));
            RobustFile.Delete(bloomPubPath);
        }

        /// <summary>
        /// True if the BloomPUB at <paramref name="bloomPubPath"/> exists and was made by this
        /// Bloom, with publish settings <paramref name="settingsKey"/>, from the book in
        /// <paramref name="bookFolderPath"/> as it is now.
        /// </summary>
        public bool IsCurrent(string bloomPubPath, string bookFolderPath, string settingsKey)
        {
            if (!RobustFile.Exists(bloomPubPath))
                return false;
            var record = ReadRecord(bloomPubPath);
            if (
                record == null
                || record.BloomVersion != s_bloomVersion.Value
                || record.SettingsKey != settingsKey
                || !Directory.Exists(bookFolderPath)
            )
                return false;

            var signature = MakeFileSignature(bookFolderPath);
            if (signature == record.FileSignature)
                return true;
            if (
                ComputeContentHash(bookFolderPath) != record.ContentHash
                || ComputeMetadataHash(bookFolderPath) != record.MetadataHash
            )
                return false;

            // The files were touched but their content is the same. Remember the new signature
            // so we don't have to read the whole book again next time.
            record.FileSignature = signature;
            WriteRecord(bloomPubPath, record);
            return true;
        }

        private static Record ReadRecord(string bloomPubPath)
        {
            var recordPath = GetRecordPath(bloomPubPath);
            if (!RobustFile.Exists(recordPath))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<Record>(RobustFile.ReadAllText(recordPath));
            }
            catch (Exception error) when (error is JsonException || error is IOException)
            {
                // A damaged record, or one being written right now, just means "make it again".
                return null;
            }
        }

        private static void WriteRecord(string bloomPubPath, Record record)
        {
            RobustFile.WriteAllText(
                GetRecordPath(bloomPubPath),
                JsonConvert.SerializeObject(record)
            );
        }

        private static string ComputeContentHash(string bookFolderPath)
        {
            return Book.Book.ComputeHashForAllBookRelatedFiles(
                BookStorage.FindBookHtmlInFolder(bookFolderPath)
            );
        }

        /// <summary>
        /// A hash of the book's meta.json, which goes into the BloomPUB but which the content hash
        /// leaves out (BL-17062: e.g. a changed summary, tags or credits). The fields that only
        /// record the editor's state are dropped first, so that switching tools doesn't make a
        /// BloomPUB look out of date. Null if the book has no meta.json.
        /// </summary>
        internal static string ComputeMetadataHash(string bookFolderPath)
        {
            var metaPath = Path.Combine(bookFolderPath, "meta.json");
            if (!RobustFile.Exists(metaPath))
                return null;
            var text = RobustFile.ReadAllText(metaPath);
            try
            {
                var metadata = JObject.Parse(text);
                foreach (var field in kEditorStateMetadataFields)
                    metadata.Remove(field);
                text = metadata.ToString(Formatting.None);
            }
            catch (JsonException)
            {
                // Not valid JSON: hash it as it is, which only errs towards making it again.
            }
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        /// <summary>
        /// A hash of the name, size and modified time of every file in the book folder, plus the
        /// collection-level files that the content hash also covers. Cheap: it reads no file
        /// contents.
        /// </summary>
        internal static string MakeFileSignature(string bookFolderPath)
        {
            // DirectoryInfo.EnumerateFiles fills in each file's size and time from the directory
            // listing itself, so a file deleted part way through can't make this throw.
            var collectionFolder = new DirectoryInfo(Path.GetDirectoryName(bookFolderPath));
            var collectionFiles = collectionFolder
                .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                .Where(file => Book.Book.IsCollectionFileThatAffectsBooks(file.Name));
            var lines = new DirectoryInfo(bookFolderPath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Concat(collectionFiles)
                .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(file => $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
            using var sha = SHA256.Create();
            return Convert.ToBase64String(
                sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", lines)))
            );
        }
    }
}
