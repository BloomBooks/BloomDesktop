using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bloom.Book;
using Newtonsoft.Json.Linq;
using SIL.IO;

namespace Bloom.TeamCollection.Cloud
{
    /// <summary>
    /// One file entry in a <see cref="BookVersionManifest"/>: content hash, size, and (once
    /// committed server-side) the S3 object version-id that pins the exact bytes stored under that
    /// path. Matches CONTRACTS.md's `version_files` shape (path → sha256, size, s3VersionId).
    /// </summary>
    public class BookVersionManifestEntry
    {
        /// <summary>
        /// Lower-case hex SHA-256 of the file's bytes. This convention (not base64) is chosen to
        /// match the server side: supabase/functions/_shared/s3.ts's `hexToBase64` doc comment says
        /// "the manifest's `sha256` field (matching the C# client, which uses
        /// Convert.ToHexString/SHA256) is lowercase hex" — S3's own `x-amz-checksum-sha256` attribute
        /// is base64, so callers must convert at the point they build the S3 request (see
        /// <see cref="CloudBookTransfer"/>).
        /// </summary>
        public string Sha256 { get; set; }

        /// <summary>Size in bytes.</summary>
        public long Size { get; set; }

        /// <summary>
        /// The S3 object version-id for this exact (path, sha256). Null for a manifest that hasn't
        /// been committed yet — e.g. the local proposed manifest built from disk before checkin-start/
        /// checkin-finish run, which by definition doesn't know the version-id S3 will assign.
        /// </summary>
        public string S3VersionId { get; set; }

        public BookVersionManifestEntry() { }

        public BookVersionManifestEntry(string sha256, long size, string s3VersionId = null)
        {
            Sha256 = sha256;
            Size = size;
            S3VersionId = s3VersionId;
        }
    }

    /// <summary>How a path differs between two manifests (or a manifest and a local folder).</summary>
    public enum ManifestDiffKind
    {
        /// <summary>Present in the "new" side, absent from the "base" side.</summary>
        Added,

        /// <summary>Present in both, but the content (sha256 or size) differs.</summary>
        Changed,

        /// <summary>Present in the "base" side, absent from the "new" side.</summary>
        Removed,

        /// <summary>Present in both with identical content.</summary>
        Unchanged,
    }

    /// <summary>One path's classification from <see cref="BookVersionManifest.DiffAgainst"/>.</summary>
    public class ManifestDiffEntry
    {
        public string Path { get; }
        public ManifestDiffKind Kind { get; }

        public ManifestDiffEntry(string path, ManifestDiffKind kind)
        {
            Path = path;
            Kind = kind;
        }

        public override string ToString() => $"{Kind}: {Path}";
    }

    /// <summary>
    /// An NFC-path-normalized map of relative path → (sha256, size, s3VersionId) for one book's
    /// content, per CONTRACTS.md's S3 layout and `version_files` table. Two things build one of
    /// these: the server (the currently-committed manifest, complete with s3VersionId, which
    /// <see cref="CloudBookTransfer"/> downloads by pinned version) and the client from a local book
    /// folder (the proposed manifest for a Send, with no s3VersionId yet — see
    /// <see cref="FromLocalFolder"/>). Path separators are always '/' (S3 key style, and the wire
    /// format's convention), independent of the local OS.
    /// </summary>
    public class BookVersionManifest
    {
        private readonly Dictionary<string, BookVersionManifestEntry> _entriesByPath;

        /// <summary>NFC-normalized relative path → entry. Read-only: build a new manifest (via the
        /// constructor, <see cref="FromJson"/>, or <see cref="FromLocalFolder"/>) rather than mutating
        /// one in place, so a manifest handed to another object (e.g. cached in
        /// <see cref="CloudRepoCache"/>) can't be changed out from under it.</summary>
        public IReadOnlyDictionary<string, BookVersionManifestEntry> Entries => _entriesByPath;

        public BookVersionManifest(IDictionary<string, BookVersionManifestEntry> entries = null)
        {
            _entriesByPath =
                entries == null
                    ? new Dictionary<string, BookVersionManifestEntry>(StringComparer.Ordinal)
                    : new Dictionary<string, BookVersionManifestEntry>(
                        entries,
                        StringComparer.Ordinal
                    );
        }

        /// <summary>
        /// NFC-normalizes a relative path and forces '/' separators (CONTRACTS.md: paths in the S3
        /// layout and manifest are "NFC-normalized"). Safe to call on a path that's already in this
        /// form.
        /// </summary>
        public static string NormalizePath(string relativePath)
        {
            return relativePath.Replace('\\', '/').Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Builds a manifest from the wire array shape `[{path, sha256, size, s3VersionId?}]`.
        /// `s3VersionId` is absent on a client-proposed manifest (checkin-start's `files`) and present
        /// on a committed one.
        /// </summary>
        public static BookVersionManifest FromJson(JToken filesArray)
        {
            var entries = new Dictionary<string, BookVersionManifestEntry>(StringComparer.Ordinal);
            if (filesArray != null)
            {
                foreach (var file in filesArray)
                {
                    var path = NormalizePath((string)file["path"]);
                    entries[path] = new BookVersionManifestEntry(
                        (string)file["sha256"],
                        (long)file["size"],
                        (string)file["s3VersionId"]
                    );
                }
            }
            return new BookVersionManifest(entries);
        }

        /// <summary>Serializes to the wire array shape (see <see cref="FromJson"/>), in path order for
        /// deterministic output. Omits `s3VersionId` for entries that don't have one yet.</summary>
        public JArray ToJson()
        {
            var array = new JArray();
            foreach (var kvp in _entriesByPath.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var obj = new JObject
                {
                    ["path"] = kvp.Key,
                    ["sha256"] = kvp.Value.Sha256,
                    ["size"] = kvp.Value.Size,
                };
                if (kvp.Value.S3VersionId != null)
                    obj["s3VersionId"] = kvp.Value.S3VersionId;
                array.Add(obj);
            }
            return array;
        }

        /// <summary>
        /// Builds the manifest that reflects a book folder's CURRENT content on disk (no
        /// s3VersionId — those only exist once committed). Uses <see cref="BookFileFilter"/>
        /// configured exactly like the real Bloom Library upload-for-continued-editing path
        /// (Bloom.WebLibraryIntegration.BookUpload.SetUpStagingAsync: IncludeFilesForContinuedEditing,
        /// every narration language, video and music) so the same junk/derived files (placeholders,
        /// anything outside the whitelist) are excluded from cloud sync as from a Bloom Library
        /// upload — CONTRACTS.md's "junk-file exclusion reusing the publish path's filters".
        /// </summary>
        public static BookVersionManifest FromLocalFolder(string bookFolderPath)
        {
            var entries = new Dictionary<string, BookVersionManifestEntry>(StringComparer.Ordinal);
            var filter = new BookFileFilter(bookFolderPath)
            {
                IncludeFilesForContinuedEditing = true,
                NarrationLanguages = null, // null = include every narration language actually used
                WantVideo = true,
                WantMusic = true,
            };
            var prefixLength = bookFolderPath.Length + 1;
            foreach (var fullPath in BookFileFilter.GetAllFilePaths(bookFolderPath))
            {
                var relativePath = fullPath.Substring(prefixLength);
                if (!filter.ShouldAllowRelativePath(relativePath))
                    continue;
                var normalizedPath = NormalizePath(relativePath);
                var (sha256, size) = ComputeFileHash(fullPath);
                entries[normalizedPath] = new BookVersionManifestEntry(sha256, size);
            }
            return new BookVersionManifest(entries);
        }

        /// <summary>
        /// Computes the lower-case hex SHA-256 and byte length of one file, streaming it in a
        /// buffered fashion (like TeamCollection.MakeChecksumOnFilesInternal / Book.MakeVersionCode)
        /// rather than reading it whole into memory, but per-file rather than combined across a whole
        /// folder — the granularity CONTRACTS.md's `version_files` table needs. Also used by
        /// <see cref="CloudBookTransfer"/> to hash files immediately before upload and to verify
        /// downloaded bytes against the pinned manifest entry.
        /// </summary>
        internal static (string sha256Hex, long size) ComputeFileHash(string filePath)
        {
            using (var sha = SHA256.Create())
            using (var input = RobustIO.GetFileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var buffer = new byte[81920];
                int count;
                long size = 0;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    sha.TransformBlock(buffer, 0, count, buffer, 0);
                    size += count;
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return (Convert.ToHexString(sha.Hash).ToLowerInvariant(), size);
            }
        }

        /// <summary>
        /// Compares this manifest (typically the last-known-committed one) against a book folder's
        /// current content on disk, producing one <see cref="ManifestDiffEntry"/> per path that
        /// appears on either side. This is the local, no-network pre-check used both to decide
        /// whether a Send is needed at all and to build the `files` list checkin-start needs; the
        /// server still does the authoritative diff against the real current version (race
        /// protection), returning `changedPaths` for what actually needs uploading.
        /// </summary>
        public List<ManifestDiffEntry> DiffAgainstLocalFolder(string bookFolderPath)
        {
            return DiffAgainst(FromLocalFolder(bookFolderPath));
        }

        /// <summary>
        /// Core of <see cref="DiffAgainstLocalFolder"/>, taking an already-built manifest for the
        /// "new" side — exposed separately so tests (and a Receive-side diff against a freshly
        /// downloaded manifest) can supply one directly without touching disk.
        /// </summary>
        public List<ManifestDiffEntry> DiffAgainst(BookVersionManifest other)
        {
            var result = new List<ManifestDiffEntry>();
            var allPaths = _entriesByPath
                .Keys.Union(other._entriesByPath.Keys, StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (var path in allPaths)
            {
                var inThis = _entriesByPath.TryGetValue(path, out var thisEntry);
                var inOther = other._entriesByPath.TryGetValue(path, out var otherEntry);
                if (inThis && !inOther)
                    result.Add(new ManifestDiffEntry(path, ManifestDiffKind.Removed));
                else if (!inThis && inOther)
                    result.Add(new ManifestDiffEntry(path, ManifestDiffKind.Added));
                else if (thisEntry.Sha256 != otherEntry.Sha256 || thisEntry.Size != otherEntry.Size)
                    result.Add(new ManifestDiffEntry(path, ManifestDiffKind.Changed));
                else
                    result.Add(new ManifestDiffEntry(path, ManifestDiffKind.Unchanged));
            }
            return result;
        }
    }
}
