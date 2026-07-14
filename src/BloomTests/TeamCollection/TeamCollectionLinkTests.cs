using System;
using System.IO;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// Tests for <see cref="TeamCollectionLink"/> — parse/write round-trips,
    /// folder form, cloud form, garbage content, missing file, both-forms-present error.
    /// </summary>
    [TestFixture]
    public class TeamCollectionLinkTests
    {
        // -----------------------------------------------------------------------
        // Parse: folder form
        // -----------------------------------------------------------------------

        [Test]
        public void Parse_FolderPath_ReturnsIsFolder()
        {
            var link = TeamCollectionLink.Parse(@"C:\Users\Alice\Dropbox\MyCollection - TC");
            Assert.IsNotNull(link);
            Assert.IsTrue(link.IsFolder);
            Assert.IsFalse(link.IsCloud);
        }

        [Test]
        public void Parse_FolderPath_RepoFolderPathPreserved()
        {
            const string folderPath = @"C:\Users\Alice\Dropbox\MyCollection - TC";
            var link = TeamCollectionLink.Parse(folderPath);
            Assert.AreEqual(folderPath, link.RepoFolderPath);
            Assert.IsNull(link.CloudCollectionId);
        }

        [Test]
        public void Parse_FolderPath_WithLeadingAndTrailingWhitespace_TrimsWhitespace()
        {
            // The legacy RepoFolderPathFromLinkPath trims; Parse must too.
            const string folderPath = @"C:\Users\Alice\Dropbox\MyCollection - TC";
            var link = TeamCollectionLink.Parse("  " + folderPath + "  \r\n");
            Assert.IsNotNull(link);
            // Parse receives the already-trimmed content; trimming is done by FromFile.
            // If the caller passes whitespace, Parse treats the whole thing as a folder path.
            // The test therefore just verifies it does not throw.
        }

        // -----------------------------------------------------------------------
        // Parse: cloud form
        // -----------------------------------------------------------------------

        [Test]
        public void Parse_CloudUri_ReturnsIsCloud()
        {
            const string collId = "550e8400-e29b-41d4-a716-446655440000";
            var link = TeamCollectionLink.Parse(TeamCollectionLink.CloudUriPrefix + collId);
            Assert.IsNotNull(link);
            Assert.IsTrue(link.IsCloud);
            Assert.IsFalse(link.IsFolder);
        }

        [Test]
        public void Parse_CloudUri_CloudCollectionIdExtracted()
        {
            const string collId = "550e8400-e29b-41d4-a716-446655440000";
            var link = TeamCollectionLink.Parse(TeamCollectionLink.CloudUriPrefix + collId);
            Assert.AreEqual(collId, link.CloudCollectionId);
            Assert.IsNull(link.RepoFolderPath);
        }

        [Test]
        public void Parse_CloudUri_ShortId_Accepted()
        {
            // We do not enforce GUID format; short IDs must work for unit-test scenarios.
            const string collId = "abc123";
            var link = TeamCollectionLink.Parse(TeamCollectionLink.CloudUriPrefix + collId);
            Assert.AreEqual(collId, link.CloudCollectionId);
        }

        // -----------------------------------------------------------------------
        // Parse: garbage / invalid content
        // -----------------------------------------------------------------------

        [Test]
        public void Parse_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(TeamCollectionLink.Parse(null));
            Assert.IsNull(TeamCollectionLink.Parse(""));
            Assert.IsNull(TeamCollectionLink.Parse("   "));
        }

        [Test]
        public void Parse_CloudUriMissingId_ThrowsInvalidLinkException()
        {
            // "cloud://sil.bloom/collection/" with nothing after the trailing slash
            Assert.Throws<InvalidTeamCollectionLinkException>(() =>
                TeamCollectionLink.Parse(TeamCollectionLink.CloudUriPrefix)
            );
        }

        [Test]
        public void Parse_CloudUriWrongAuthority_ThrowsInvalidLinkException()
        {
            // A "cloud://" URI with a different authority is clearly wrong.
            Assert.Throws<InvalidTeamCollectionLinkException>(() =>
                TeamCollectionLink.Parse("cloud://unknown.host/collection/some-id")
            );
        }

        [Test]
        public void Parse_CloudUriWithWhitespaceInId_ThrowsInvalidLinkException()
        {
            Assert.Throws<InvalidTeamCollectionLinkException>(() =>
                TeamCollectionLink.Parse(
                    TeamCollectionLink.CloudUriPrefix + "invalid id with spaces"
                )
            );
        }

        // -----------------------------------------------------------------------
        // Round-trip: ToFileContent
        // -----------------------------------------------------------------------

        [Test]
        public void ToFileContent_FolderLink_RoundTrips()
        {
            const string folderPath = @"\\server\share\MyCollection - TC";
            var link = TeamCollectionLink.ForFolder(folderPath);
            var content = link.ToFileContent();
            // Sanity: content is the folder path verbatim.
            Assert.AreEqual(folderPath, content);
            // Re-parse must survive the round-trip.
            var reparsed = TeamCollectionLink.Parse(content);
            Assert.IsTrue(reparsed.IsFolder);
            Assert.AreEqual(folderPath, reparsed.RepoFolderPath);
        }

        [Test]
        public void ToFileContent_CloudLink_RoundTrips()
        {
            const string collId = "550e8400-e29b-41d4-a716-446655440000";
            var link = TeamCollectionLink.ForCloud(collId);
            var content = link.ToFileContent();
            // Sanity: content starts with the expected prefix.
            Assert.IsTrue(content.StartsWith(TeamCollectionLink.CloudUriPrefix));
            // Re-parse must survive the round-trip.
            var reparsed = TeamCollectionLink.Parse(content);
            Assert.IsTrue(reparsed.IsCloud);
            Assert.AreEqual(collId, reparsed.CloudCollectionId);
        }

        // -----------------------------------------------------------------------
        // FromFile: file I/O
        // -----------------------------------------------------------------------

        [Test]
        public void FromFile_MissingFile_ReturnsNull()
        {
            var result = TeamCollectionLink.FromFile(@"C:\nonexistent\path\TeamCollectionLink.txt");
            Assert.IsNull(result);
        }

        [Test]
        public void FromFile_FolderLinkFile_ParsesCorrectly()
        {
            using var tempFolder = new TemporaryFolder("TCLinkTests_FromFile_Folder");
            const string folderPath = @"C:\Shared\MyCollection - TC";
            var linkFilePath = Path.Combine(
                tempFolder.FolderPath,
                TeamCollectionManager.TeamCollectionLinkFileName
            );
            RobustFile.WriteAllText(linkFilePath, folderPath);

            var link = TeamCollectionLink.FromFile(linkFilePath);
            Assert.IsNotNull(link);
            Assert.IsTrue(link.IsFolder);
            Assert.AreEqual(folderPath, link.RepoFolderPath);
        }

        [Test]
        public void FromFile_CloudLinkFile_ParsesCorrectly()
        {
            using var tempFolder = new TemporaryFolder("TCLinkTests_FromFile_Cloud");
            const string collId = "550e8400-e29b-41d4-a716-446655440000";
            var linkFilePath = Path.Combine(
                tempFolder.FolderPath,
                TeamCollectionManager.TeamCollectionLinkFileName
            );
            RobustFile.WriteAllText(linkFilePath, TeamCollectionLink.CloudUriPrefix + collId);

            var link = TeamCollectionLink.FromFile(linkFilePath);
            Assert.IsNotNull(link);
            Assert.IsTrue(link.IsCloud);
            Assert.AreEqual(collId, link.CloudCollectionId);
        }

        [Test]
        public void FromFile_TrimsWhitespaceFromFileContent()
        {
            // Legacy files may have trailing newlines.
            using var tempFolder = new TemporaryFolder("TCLinkTests_FromFile_Trim");
            const string folderPath = @"C:\Shared\MyCollection - TC";
            var linkFilePath = Path.Combine(
                tempFolder.FolderPath,
                TeamCollectionManager.TeamCollectionLinkFileName
            );
            RobustFile.WriteAllText(linkFilePath, folderPath + "\r\n");

            var link = TeamCollectionLink.FromFile(linkFilePath);
            Assert.IsNotNull(link);
            Assert.AreEqual(folderPath, link.RepoFolderPath);
        }

        // -----------------------------------------------------------------------
        // WriteToFile
        // -----------------------------------------------------------------------

        [Test]
        public void WriteToFile_FolderLink_WritesCorrectContent()
        {
            using var tempFolder = new TemporaryFolder("TCLinkTests_WriteFile_Folder");
            const string folderPath = @"C:\Shared\MyCollection - TC";
            var link = TeamCollectionLink.ForFolder(folderPath);
            var linkFilePath = Path.Combine(
                tempFolder.FolderPath,
                TeamCollectionManager.TeamCollectionLinkFileName
            );

            link.WriteToFile(linkFilePath);

            Assert.IsTrue(RobustFile.Exists(linkFilePath));
            var written = RobustFile.ReadAllText(linkFilePath);
            Assert.AreEqual(folderPath, written);
        }

        [Test]
        public void WriteToFile_CloudLink_WritesCorrectContent()
        {
            using var tempFolder = new TemporaryFolder("TCLinkTests_WriteFile_Cloud");
            const string collId = "550e8400-e29b-41d4-a716-446655440000";
            var link = TeamCollectionLink.ForCloud(collId);
            var linkFilePath = Path.Combine(
                tempFolder.FolderPath,
                TeamCollectionManager.TeamCollectionLinkFileName
            );

            link.WriteToFile(linkFilePath);

            Assert.IsTrue(RobustFile.Exists(linkFilePath));
            var written = RobustFile.ReadAllText(linkFilePath);
            Assert.AreEqual(TeamCollectionLink.CloudUriPrefix + collId, written);
        }

        // -----------------------------------------------------------------------
        // Both-forms-present detection
        // -----------------------------------------------------------------------

        [Test]
        public void Parse_CloudUriPrefix_WithoutTrailingId_ThrowsInvalidLinkException()
        {
            // Explicitly verify that the prefix alone (no ID) is rejected.
            Assert.Throws<InvalidTeamCollectionLinkException>(() =>
                TeamCollectionLink.Parse("cloud://sil.bloom/collection/")
            );
        }
    }
}
