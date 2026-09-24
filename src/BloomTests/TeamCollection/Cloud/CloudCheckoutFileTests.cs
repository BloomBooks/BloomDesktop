using System;
using System.Collections.Generic;
using System.IO;
using Bloom.TeamCollection.Cloud;
using BloomTemp;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BloomTests.TeamCollection.Cloud
{
    /// <summary>
    /// Tests for the per-book-folder `.checkout` record (CONTRACTS.md v1.9 checkout GUID): its
    /// file format, "corrupt = missing", the hash that must match the server's exactly, and that
    /// it never travels with copies of the book (AddTCSpecificFiles).
    /// </summary>
    [TestFixture]
    public class CloudCheckoutFileTests
    {
        private const string kGuid = "0b8f5a3e-9c1d-4e2f-8a7b-6c5d4e3f2a1b";

        // sha256 of the UTF-8 bytes of kGuid, as lowercase hex -- computed independently (with
        // PowerShell's SHA256), i.e. what the server's
        // encode(sha256(convert_to(lower(guid), 'UTF8')), 'hex') gives.
        private const string kGuidHash =
            "ec6896cddc382afb7d57aee0050d487b74e39b0d6ff3f8f3841b9d0965c0eee5";

        private TemporaryFolder _bookFolder;

        [SetUp]
        public void SetUp()
        {
            _bookFolder = new TemporaryFolder("CloudCheckoutFileTests");
        }

        [TearDown]
        public void TearDown()
        {
            _bookFolder.Dispose();
        }

        private string BookFolderPath => _bookFolder.FolderPath;

        private static CloudCheckoutFile MakeRecord() =>
            new CloudCheckoutFile
            {
                CheckoutGuid = kGuid,
                BookId = "book-1",
                CollectionId = "collection-1",
                UserEmail = "me@example.com",
                CheckedOutAtUtc = new DateTime(2026, 9, 24, 10, 11, 12, 345, DateTimeKind.Utc),
            };

        [Test]
        public void WriteThenRead_RoundTripsEveryField()
        {
            MakeRecord().Write(BookFolderPath);

            var read = CloudCheckoutFile.Read(BookFolderPath);

            Assert.That(read.CheckoutGuid, Is.EqualTo(kGuid));
            Assert.That(read.BookId, Is.EqualTo("book-1"));
            Assert.That(read.CollectionId, Is.EqualTo("collection-1"));
            Assert.That(read.UserEmail, Is.EqualTo("me@example.com"));
            Assert.That(
                read.CheckedOutAtUtc,
                Is.EqualTo(new DateTime(2026, 9, 24, 10, 11, 12, 345, DateTimeKind.Utc))
            );
        }

        [Test]
        public void Write_HasNoByteOrderMark()
        {
            MakeRecord().Write(BookFolderPath);

            var bytes = File.ReadAllBytes(Path.Combine(BookFolderPath, ".checkout"));
            Assert.That(bytes.Length, Is.GreaterThan(3), "sanity: the record has content");
            Assert.That(
                (char)bytes[0],
                Is.EqualTo('{'),
                "plain JSON readers (e.g. JSON.parse) choke on a leading UTF-8 BOM"
            );
        }

        [Test]
        public void Read_ToleratesAByteOrderMark()
        {
            MakeRecord().Write(BookFolderPath);
            var path = Path.Combine(BookFolderPath, ".checkout");
            File.WriteAllText(path, File.ReadAllText(path), new System.Text.UTF8Encoding(true));
            Assert.That(File.ReadAllBytes(path)[0], Is.EqualTo(0xEF), "sanity: now has a BOM");

            Assert.That(CloudCheckoutFile.ReadGuid(BookFolderPath), Is.EqualTo(kGuid));
        }

        [Test]
        public void Write_UsesTheAgreedFileNameAndJsonShape()
        {
            MakeRecord().Write(BookFolderPath);

            var path = Path.Combine(BookFolderPath, ".checkout");
            Assert.That(File.Exists(path), Is.True, "the record is <bookFolder>/.checkout");
            var json = JObject.Parse(File.ReadAllText(path));
            Assert.That((int)json["version"], Is.EqualTo(1));
            Assert.That((string)json["checkoutGuid"], Is.EqualTo(kGuid));
            Assert.That((string)json["bookId"], Is.EqualTo("book-1"));
            Assert.That((string)json["collectionId"], Is.EqualTo("collection-1"));
            Assert.That((string)json["userEmail"], Is.EqualTo("me@example.com"));
            Assert.That(
                json["checkedOutAt"].ToString(Newtonsoft.Json.Formatting.None),
                Does.Contain("2026-09-24T10:11:12.345Z")
            );
        }

        [Test]
        public void Read_NoFile_ReturnsNull()
        {
            Assert.That(CloudCheckoutFile.Read(BookFolderPath), Is.Null);
            Assert.That(CloudCheckoutFile.ReadGuid(BookFolderPath), Is.Null);
        }

        [TestCase("{ this is not json")]
        [TestCase("{\"version\":1,\"bookId\":\"book-1\"}")] // no GUID
        [TestCase("{\"version\":1,\"checkoutGuid\":\"  \"}")]
        [TestCase("[1,2,3]")]
        public void Read_CorruptFile_CountsAsMissing(string content)
        {
            File.WriteAllText(CloudCheckoutFile.GetPath(BookFolderPath), content);

            Assert.That(CloudCheckoutFile.Read(BookFolderPath), Is.Null);
            Assert.That(CloudCheckoutFile.MatchesServerHash(BookFolderPath, kGuidHash), Is.False);
        }

        [Test]
        public void Delete_RemovesTheRecord_AndIsHarmlessWhenAbsent()
        {
            MakeRecord().Write(BookFolderPath);
            Assert.That(File.Exists(CloudCheckoutFile.GetPath(BookFolderPath)), Is.True);

            CloudCheckoutFile.Delete(BookFolderPath);
            Assert.That(File.Exists(CloudCheckoutFile.GetPath(BookFolderPath)), Is.False);

            Assert.DoesNotThrow(() => CloudCheckoutFile.Delete(BookFolderPath));
        }

        [Test]
        public void HashGuid_MatchesTheServersDefinition()
        {
            Assert.That(CloudCheckoutFile.HashGuid(kGuid), Is.EqualTo(kGuidHash));
        }

        [Test]
        public void HashGuid_HashesTheLowercaseForm()
        {
            Assert.That(
                CloudCheckoutFile.HashGuid(kGuid.ToUpperInvariant()),
                Is.EqualTo(kGuidHash)
            );
        }

        [Test]
        public void MatchesServerHash_TrueOnlyForTheRecordsOwnGuidHash()
        {
            MakeRecord().Write(BookFolderPath);

            Assert.That(CloudCheckoutFile.MatchesServerHash(BookFolderPath, kGuidHash), Is.True);
            Assert.That(
                CloudCheckoutFile.MatchesServerHash(BookFolderPath, kGuidHash.ToUpperInvariant()),
                Is.True,
                "hex case is not significant"
            );
            Assert.That(
                CloudCheckoutFile.MatchesServerHash(
                    BookFolderPath,
                    CloudCheckoutFile.HashGuid(Guid.NewGuid().ToString())
                ),
                Is.False
            );
            Assert.That(CloudCheckoutFile.MatchesServerHash(BookFolderPath, null), Is.False);
        }

        [Test]
        public void AddTCSpecificFiles_IncludesTheCheckoutRecord()
        {
            // So duplicates, publications, BloomPacks and non-TC copies of the book drop it.
            var paths = new List<string>();
            Bloom.TeamCollection.TeamCollection.AddTCSpecificFiles(BookFolderPath, paths);
            Assert.That(
                paths,
                Does.Not.Contain(CloudCheckoutFile.GetPath(BookFolderPath)),
                "sanity check: nothing to list before it exists"
            );

            MakeRecord().Write(BookFolderPath);
            paths.Clear();
            Bloom.TeamCollection.TeamCollection.AddTCSpecificFiles(BookFolderPath, paths);

            Assert.That(paths, Does.Contain(CloudCheckoutFile.GetPath(BookFolderPath)));
        }
    }
}
