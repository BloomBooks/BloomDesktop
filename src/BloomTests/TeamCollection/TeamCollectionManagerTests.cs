using System.IO;
using Bloom.TeamCollection;
using BloomTemp;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.TeamCollection
{
    // Covers task 10's "un-team adoption" guard rails, which live in TeamCollectionManager but
    // don't need a full TeamCollectionManager (or any network access) to unit test:
    //   - ThrowIfConflictingTeamCollectionLink: the "simultaneous folder-link + cloud-link"
    //     conflict check ConnectToCloudCollection runs before doing anything else.
    //   - TeamCollection.CleanStaleTeamCollectionArtifacts: the stale-artifact cleanup that same
    //     method runs before the collection's first cloud Send.
    public class TeamCollectionManagerTests
    {
        private TemporaryFolder _collectionFolder;

        [SetUp]
        public void Setup()
        {
            _collectionFolder = new TemporaryFolder("TeamCollectionManagerTests");
        }

        [TearDown]
        public void TearDown()
        {
            _collectionFolder.Dispose();
        }

        [Test]
        public void ThrowIfConflictingTeamCollectionLink_NoLinkFile_DoesNotThrow()
        {
            // Sanity check: this collection folder really doesn't have a link file yet.
            var linkPath = Path.Combine(_collectionFolder.FolderPath, "TeamCollectionLink.txt");
            Assert.That(RobustFile.Exists(linkPath), Is.False);

            Assert.DoesNotThrow(() =>
                Bloom.TeamCollection.TeamCollectionManager.ThrowIfConflictingTeamCollectionLink(
                    _collectionFolder.FolderPath
                )
            );
        }

        [Test]
        public void ThrowIfConflictingTeamCollectionLink_ExistingFolderLink_ThrowsWithFixInstructions()
        {
            var linkPath = Path.Combine(_collectionFolder.FolderPath, "TeamCollectionLink.txt");
            var sharedFolderPath = @"C:\Dropbox\SomeTeam\MyCollection - TC";
            RobustFile.WriteAllText(linkPath, sharedFolderPath);
            // Sanity check our setup actually produced a folder-type link.
            var parsedLink = TeamCollectionLink.FromFile(linkPath);
            Assert.That(parsedLink, Is.Not.Null);
            Assert.That(parsedLink.IsFolder, Is.True);

            var ex = Assert.Throws<TeamCollectionLinkConflictException>(() =>
                Bloom.TeamCollection.TeamCollectionManager.ThrowIfConflictingTeamCollectionLink(
                    _collectionFolder.FolderPath
                )
            );

            Assert.That(ex.Message, Does.Contain(sharedFolderPath));
            Assert.That(ex.Message, Does.Contain("TeamCollectionLink.txt"));
        }

        [Test]
        public void ThrowIfConflictingTeamCollectionLink_ExistingCloudLink_Throws()
        {
            var linkPath = Path.Combine(_collectionFolder.FolderPath, "TeamCollectionLink.txt");
            TeamCollectionLink
                .ForCloud("11111111-1111-1111-1111-111111111111")
                .WriteToFile(linkPath);

            var ex = Assert.Throws<TeamCollectionLinkConflictException>(() =>
                Bloom.TeamCollection.TeamCollectionManager.ThrowIfConflictingTeamCollectionLink(
                    _collectionFolder.FolderPath
                )
            );

            Assert.That(ex.Message, Does.Contain("11111111-1111-1111-1111-111111111111"));
        }

        [Test]
        public void CleanStaleTeamCollectionArtifacts_DeletesPerBookStatusFiles()
        {
            var book1 = Directory.CreateDirectory(
                Path.Combine(_collectionFolder.FolderPath, "Book One")
            );
            var book2 = Directory.CreateDirectory(
                Path.Combine(_collectionFolder.FolderPath, "Book Two")
            );
            var status1 = Path.Combine(book1.FullName, "TeamCollection.status");
            var status2 = Path.Combine(book2.FullName, "TeamCollection.status");
            RobustFile.WriteAllText(status1, "{\"checksum\":\"stale-checksum-from-old-tc\"}");
            RobustFile.WriteAllText(status2, "{\"checksum\":\"another-stale-checksum\"}");
            // Sanity check the fixture actually has the files we're about to assert get removed.
            Assert.That(RobustFile.Exists(status1), Is.True);
            Assert.That(RobustFile.Exists(status2), Is.True);

            Bloom.TeamCollection.TeamCollection.CleanStaleTeamCollectionArtifacts(
                _collectionFolder.FolderPath
            );

            Assert.That(RobustFile.Exists(status1), Is.False);
            Assert.That(RobustFile.Exists(status2), Is.False);
        }

        [Test]
        public void CleanStaleTeamCollectionArtifacts_DeletesCollectionLevelSyncAndLogFiles()
        {
            var lastSyncFile = Path.Combine(
                _collectionFolder.FolderPath,
                "lastCollectionFileSyncData.txt"
            );
            var logFile = Path.Combine(_collectionFolder.FolderPath, "log.txt");
            RobustFile.WriteAllText(lastSyncFile, "stale sync data");
            RobustFile.WriteAllText(logFile, "stale log content");
            Assert.That(RobustFile.Exists(lastSyncFile), Is.True);
            Assert.That(RobustFile.Exists(logFile), Is.True);

            Bloom.TeamCollection.TeamCollection.CleanStaleTeamCollectionArtifacts(
                _collectionFolder.FolderPath
            );

            Assert.That(RobustFile.Exists(lastSyncFile), Is.False);
            Assert.That(RobustFile.Exists(logFile), Is.False);
        }

        [Test]
        public void CleanStaleTeamCollectionArtifacts_LeavesTeamCollectionLinkAlone()
        {
            // The link file itself is a separate decision (ThrowIfConflictingTeamCollectionLink
            // above, and then ConnectToCloudCollection deliberately overwriting it with a fresh
            // cloud link) -- cleanup must not delete it out from under either of those.
            var linkPath = Path.Combine(_collectionFolder.FolderPath, "TeamCollectionLink.txt");
            RobustFile.WriteAllText(linkPath, @"C:\Dropbox\SomeTeam\MyCollection - TC");

            Bloom.TeamCollection.TeamCollection.CleanStaleTeamCollectionArtifacts(
                _collectionFolder.FolderPath
            );

            Assert.That(RobustFile.Exists(linkPath), Is.True);
        }

        [Test]
        public void CleanStaleTeamCollectionArtifacts_NeverBeenATeamCollection_IsANoOp()
        {
            Directory.CreateDirectory(Path.Combine(_collectionFolder.FolderPath, "Plain Book"));

            Assert.DoesNotThrow(() =>
                Bloom.TeamCollection.TeamCollection.CleanStaleTeamCollectionArtifacts(
                    _collectionFolder.FolderPath
                )
            );
        }
    }
}
