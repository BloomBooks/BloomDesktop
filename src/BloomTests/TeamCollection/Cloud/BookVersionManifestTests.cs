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
    }
}
