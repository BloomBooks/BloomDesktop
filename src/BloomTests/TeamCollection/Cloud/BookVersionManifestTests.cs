using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection.Cloud
{
    [TestFixture]
    public class BookVersionManifestTests
    {
        private TemporaryFolder _bookFolder;
        private string _bookFolderPath;

        [SetUp]
        public void SetUp()
        {
            _bookFolder = new TemporaryFolder("BookVersionManifestTests");
            _bookFolderPath = _bookFolder.FolderPath;
        }

        [TearDown]
        public void TearDown()
        {
            _bookFolder.Dispose();
        }

        private string BookName => Path.GetFileName(_bookFolderPath);

        private void WriteMinimalBook()
        {
            // A folder needs a matching .htm for BookFileFilter to find "the one html path";
            // content doesn't matter for these tests beyond that.
            RobustFile.WriteAllText(
                Path.Combine(_bookFolderPath, BookName + ".htm"),
                "<html><head></head><body></body></html>"
            );
        }

        // ------------------------------------------------------------------
        // NFC normalization
        // ------------------------------------------------------------------

        [Test]
        public void NormalizePath_ConvertsBackslashesToForwardSlashes()
        {
            Assert.That(
                BookVersionManifest.NormalizePath(@"audio\sound.mp3"),
                Is.EqualTo("audio/sound.mp3")
            );
        }

        [Test]
        public void NormalizePath_NormalizesToNfc()
        {
            // "é" as NFD (e + combining acute accent, 2 chars) must normalize to the same string
            // as "é" as NFC (1 precomposed char) — otherwise two Bloom instances on different OSes
            // (macOS tends to produce NFD from its filesystem) would disagree about a path.
            var nfd = "é.png"; // e + combining acute
            var nfc = "é.png"; // é (precomposed)

            Assert.That(
                nfd,
                Is.Not.EqualTo(nfc),
                "sanity check: the two forms must differ as raw strings"
            );
            Assert.That(
                BookVersionManifest.NormalizePath(nfd),
                Is.EqualTo(BookVersionManifest.NormalizePath(nfc))
            );
            Assert.That(BookVersionManifest.NormalizePath(nfc), Is.EqualTo(nfc));
        }

        // ------------------------------------------------------------------
        // JSON round-trip
        // ------------------------------------------------------------------

        [Test]
        public void FromJson_ThenToJson_RoundTrips()
        {
            var json = JArray.Parse(
                @"[
                    { ""path"": ""book.htm"", ""sha256"": ""abc123"", ""size"": 42, ""s3VersionId"": ""v1"" },
                    { ""path"": ""audio/one.mp3"", ""sha256"": ""def456"", ""size"": 7 }
                ]"
            );

            var manifest = BookVersionManifest.FromJson(json);

            Assert.That(manifest.Entries.Count, Is.EqualTo(2), "sanity check: both entries parsed");
            Assert.That(manifest.Entries["book.htm"].Sha256, Is.EqualTo("abc123"));
            Assert.That(manifest.Entries["book.htm"].Size, Is.EqualTo(42));
            Assert.That(manifest.Entries["book.htm"].S3VersionId, Is.EqualTo("v1"));
            Assert.That(
                manifest.Entries["audio/one.mp3"].S3VersionId,
                Is.Null,
                "no s3VersionId on the wire => null, not a proposed manifest yet"
            );

            var roundTripped = manifest.ToJson();
            Assert.That(roundTripped.Count, Is.EqualTo(2));
            var bookEntry = roundTripped.Single(f => (string)f["path"] == "book.htm");
            Assert.That((string)bookEntry["sha256"], Is.EqualTo("abc123"));
            Assert.That((long)bookEntry["size"], Is.EqualTo(42));
            Assert.That((string)bookEntry["s3VersionId"], Is.EqualTo("v1"));

            var audioEntry = roundTripped.Single(f => (string)f["path"] == "audio/one.mp3");
            Assert.That(
                audioEntry["s3VersionId"],
                Is.Null,
                "an entry with no version id must not gain one on re-serialization"
            );
        }

        [Test]
        public void FromJson_NullArray_ProducesEmptyManifest()
        {
            var manifest = BookVersionManifest.FromJson(null);
            Assert.That(manifest.Entries, Is.Empty);
        }

        // ------------------------------------------------------------------
        // FromLocalFolder / junk exclusion
        // ------------------------------------------------------------------

        [Test]
        public void FromLocalFolder_IncludesWhitelistedFiles_ExcludesJunk()
        {
            WriteMinimalBook();
            RobustFile.WriteAllText(
                Path.Combine(_bookFolderPath, "thumbnail.png"),
                "fake-png-bytes"
            );
            RobustFile.WriteAllText(Path.Combine(_bookFolderPath, "styles.css"), "body{}");
            // Not in BookFileFilter's whitelist (no recognized extension, not audio/video/template) —
            // this is exactly the kind of "junk the user might have happened to put in the folder"
            // BookFileFilter's own doc comment says it's designed to keep out.
            RobustFile.WriteAllText(Path.Combine(_bookFolderPath, "notes.junk"), "scratch notes");
            // placeHolder files are explicitly excluded by BookFileFilter itself.
            RobustFile.WriteAllText(
                Path.Combine(_bookFolderPath, "placeHolder.png"),
                "placeholder"
            );

            // Sanity check: all four files really are on disk before we ask the manifest to filter them.
            Assert.That(
                Directory.GetFiles(_bookFolderPath),
                Has.Length.EqualTo(
                    4 + 1 /* the .htm */
                )
            );

            var manifest = BookVersionManifest.FromLocalFolder(_bookFolderPath);

            Assert.That(manifest.Entries.Keys, Has.Member(BookName + ".htm"));
            Assert.That(manifest.Entries.Keys, Has.Member("thumbnail.png"));
            Assert.That(manifest.Entries.Keys, Has.Member("styles.css"));
            Assert.That(manifest.Entries.Keys, Has.None.EqualTo("notes.junk"));
            Assert.That(manifest.Entries.Keys, Has.None.EqualTo("placeHolder.png"));
        }

        [Test]
        public void FromLocalFolder_ComputesRealSha256AndSize()
        {
            WriteMinimalBook();
            var contentPath = Path.Combine(_bookFolderPath, "styles.css");
            var content = "body { color: red; }";
            RobustFile.WriteAllText(contentPath, content);

            var manifest = BookVersionManifest.FromLocalFolder(_bookFolderPath);

            var (expectedSha256, expectedSize) = BookVersionManifest.ComputeFileHash(contentPath);
            Assert.That(
                expectedSize,
                Is.EqualTo(content.Length),
                "sanity check: ASCII content, byte length == char length"
            );

            var entry = manifest.Entries["styles.css"];
            Assert.That(entry.Sha256, Is.EqualTo(expectedSha256));
            Assert.That(entry.Size, Is.EqualTo(expectedSize));
            Assert.That(
                entry.S3VersionId,
                Is.Null,
                "a local-folder manifest has no version ids yet"
            );
        }

        [Test]
        public void ComputeFileHash_DifferentContent_DifferentHash()
        {
            var pathA = Path.Combine(_bookFolderPath, "a.txt");
            var pathB = Path.Combine(_bookFolderPath, "b.txt");
            RobustFile.WriteAllText(pathA, "hello");
            RobustFile.WriteAllText(pathB, "world");

            var (hashA, _) = BookVersionManifest.ComputeFileHash(pathA);
            var (hashB, _) = BookVersionManifest.ComputeFileHash(pathB);

            Assert.That(hashA, Is.Not.EqualTo(hashB));
            Assert.That(
                hashA,
                Does.Match("^[0-9a-f]+$"),
                "must be lower-case hex, per the server-side convention"
            );
        }

        // ------------------------------------------------------------------
        // Diff matrix
        // ------------------------------------------------------------------

        private static BookVersionManifest MakeManifest(
            params (string path, string sha256, long size)[] entries
        )
        {
            var dict = new Dictionary<string, BookVersionManifestEntry>();
            foreach (var (path, sha256, size) in entries)
                dict[path] = new BookVersionManifestEntry(sha256, size);
            return new BookVersionManifest(dict);
        }

        [Test]
        public void DiffAgainst_ClassifiesAddedChangedRemovedUnchanged()
        {
            var baseManifest = MakeManifest(
                ("unchanged.txt", "hash-u", 1),
                ("changed.txt", "hash-c-old", 2),
                ("removed.txt", "hash-r", 3)
            );
            var otherManifest = MakeManifest(
                ("unchanged.txt", "hash-u", 1),
                ("changed.txt", "hash-c-new", 2),
                ("added.txt", "hash-a", 4)
            );

            // Sanity check on the fixtures themselves before exercising the method under test.
            Assert.That(baseManifest.Entries.Count, Is.EqualTo(3));
            Assert.That(otherManifest.Entries.Count, Is.EqualTo(3));

            var diff = baseManifest.DiffAgainst(otherManifest);

            Assert.That(
                diff,
                Has.Count.EqualTo(4),
                "unchanged+changed+removed+added = 4 distinct paths"
            );
            Assert.That(Kind(diff, "unchanged.txt"), Is.EqualTo(ManifestDiffKind.Unchanged));
            Assert.That(Kind(diff, "changed.txt"), Is.EqualTo(ManifestDiffKind.Changed));
            Assert.That(Kind(diff, "removed.txt"), Is.EqualTo(ManifestDiffKind.Removed));
            Assert.That(Kind(diff, "added.txt"), Is.EqualTo(ManifestDiffKind.Added));
        }

        [Test]
        public void DiffAgainst_SameSizeDifferentHash_IsChanged()
        {
            var baseManifest = MakeManifest(("f.txt", "hash-1", 10));
            var otherManifest = MakeManifest(("f.txt", "hash-2", 10));

            var diff = baseManifest.DiffAgainst(otherManifest);

            Assert.That(Kind(diff, "f.txt"), Is.EqualTo(ManifestDiffKind.Changed));
        }

        [Test]
        public void DiffAgainstLocalFolder_DetectsRealFileChange()
        {
            WriteMinimalBook();
            RobustFile.WriteAllText(Path.Combine(_bookFolderPath, "styles.css"), "body{color:red}");
            var baseManifest = BookVersionManifest.FromLocalFolder(_bookFolderPath);

            // Sanity check: the base manifest really did capture styles.css before we change it.
            Assert.That(baseManifest.Entries.Keys, Has.Member("styles.css"));

            RobustFile.WriteAllText(
                Path.Combine(_bookFolderPath, "styles.css"),
                "body{color:blue}"
            );

            var diff = baseManifest.DiffAgainstLocalFolder(_bookFolderPath);

            Assert.That(Kind(diff, "styles.css"), Is.EqualTo(ManifestDiffKind.Changed));
            Assert.That(Kind(diff, BookName + ".htm"), Is.EqualTo(ManifestDiffKind.Unchanged));
        }

        private static ManifestDiffKind Kind(List<ManifestDiffEntry> diff, string path) =>
            diff.Single(e => e.Path == path).Kind;
    }
}
