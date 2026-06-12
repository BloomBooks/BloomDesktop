using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Bloom.History;
using Bloom.TeamCollection;
using BloomTemp;
using Moq;
using NUnit.Framework;

namespace BloomTests.TeamCollection
{
    /// <summary>
    /// FolderTeamCollection subclass that lets tests control which books appear to be
    /// downloading, without the real Thread.Sleep and file-size check of the production path.
    /// </summary>
    internal class ControllableFolderTeamCollection : FolderTeamCollection
    {
        /// <summary>Books in this set are reported as actively downloading by IsBookDownloading.</summary>
        public readonly HashSet<string> DownloadingBooks = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase
        );

        public ControllableFolderTeamCollection(
            ITeamCollectionManager mgr,
            string localCollectionFolder,
            string repoFolderPath,
            TeamCollectionMessageLog tcLog
        )
            : base(mgr, localCollectionFolder, repoFolderPath, tcLog: tcLog) { }

        protected override bool IsBookDownloading(string bookName) =>
            DownloadingBooks.Contains(bookName);
    }

    /// <summary>
    /// FolderTeamCollection subclass that exposes the protected IsBookDownloading method
    /// as a public helper for direct unit testing.
    /// </summary>
    internal class ExposedFolderTeamCollection : FolderTeamCollection
    {
        public ExposedFolderTeamCollection(
            ITeamCollectionManager mgr,
            string localCollectionFolder,
            string repoFolderPath,
            TeamCollectionMessageLog tcLog
        )
            : base(mgr, localCollectionFolder, repoFolderPath, tcLog: tcLog) { }

        /// <summary>Thin public wrapper so tests can call the protected IsBookDownloading.</summary>
        public bool IsBookDownloadingPublic(string bookName) => IsBookDownloading(bookName);
    }

    /// <summary>
    /// Verifies SyncAtStartup behaviour when an unreadable .bloom file is detected as
    /// actively downloading (IsBookDownloading returns true, firstTimeJoin = false).
    /// Setup runs SyncAtStartup once; individual tests verify the resulting state.
    /// </summary>
    [TestFixture]
    public class SyncAtStartup_DownloadingBook_Tests
    {
        private TemporaryFolder _repoFolder;
        private TemporaryFolder _collectionFolder;
        private ControllableFolderTeamCollection _collection;
        private ProgressSpy _progressSpy;
        private TeamCollectionMessageLog _tcLog;
        private const string kDownloadingBookName = "A Book Being Downloaded";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _repoFolder = new TemporaryFolder("DownloadingBook_Repo");
            _collectionFolder = new TemporaryFolder("DownloadingBook_Local");

            FolderTeamCollection.CreateTeamCollectionLinkFile(
                _collectionFolder.FolderPath,
                _repoFolder.FolderPath
            );

            var mockTcManager = new Mock<ITeamCollectionManager>();
            _tcLog = new TeamCollectionMessageLog(
                TeamCollectionManager.GetTcLogPathFromLcPath(_collectionFolder.FolderPath)
            );
            _collection = new ControllableFolderTeamCollection(
                mockTcManager.Object,
                _collectionFolder.FolderPath,
                _repoFolder.FolderPath,
                _tcLog
            );
            _collection.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
            TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");

            // Corrupt/partial .bloom file simulating a Dropbox in-progress download
            var booksDir = Path.Combine(_repoFolder.FolderPath, "Books");
            Directory.CreateDirectory(booksDir);
            File.WriteAllText(
                Path.Combine(booksDir, kDownloadingBookName + ".bloom"),
                "This is a partial download, not a valid zip!"
            );

            // Tell the collection this book is actively downloading
            _collection.DownloadingBooks.Add(kDownloadingBookName);

            _progressSpy = new ProgressSpy();
            // SUT — sync should abort early and return true (has problems)
            Assert.That(_collection.SyncAtStartup(_progressSpy, firstTimeJoin: false), Is.True);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _repoFolder?.Dispose();
            _collectionFolder?.Dispose();
        }

        [Test]
        public void SyncAtStartup_DownloadingBook_LogsBookFileDownloadingMessageAsErrorType()
        {
            // The downloading message must be Error (not ErrorNoReload) so the TC dialog
            // keeps the Reload button visible even after LogDisplayed is written.
            Assert.That(
                _tcLog.Messages,
                Has.Exactly(1)
                    .Matches<TeamCollectionMessage>(m =>
                        m.L10NId == "TeamCollection.BookFileDownloading"
                        && m.Param0 == kDownloadingBookName
                        && m.MessageType == MessageAndMilestoneType.Error
                    )
            );
        }

        [Test]
        public void SyncAtStartup_DownloadingBook_MessageAppearsInProgressErrors()
        {
            // The message should also be piped to the progress dialog (ProgressSpy).
            Assert.That(_progressSpy.Errors, Has.Some.Contains(kDownloadingBookName));
        }

        [Test]
        public void SyncAtStartup_DownloadingBook_NoLogDisplayedMilestone()
        {
            // Deliberately omitting LogDisplayed keeps CurrentErrors populated so
            // the TC button remains in the Error state after the dialog closes.
            Assert.That(
                _tcLog.Messages,
                Has.None.Matches<TeamCollectionMessage>(m =>
                    m.MessageType == MessageAndMilestoneType.LogDisplayed
                )
            );
        }

        [Test]
        public void SyncAtStartup_DownloadingBook_CollectionStatusIsError()
        {
            Assert.That(_tcLog.TeamCollectionStatus, Is.EqualTo(TeamCollectionStatus.Error));
        }

        [Test]
        public void SyncAtStartup_DownloadingBook_ShouldShowReloadButton()
        {
            // ReloadMessages still contains the Error message, so the Reload button must appear.
            Assert.That(_tcLog.ShouldShowReloadButton, Is.True);
        }

        [Test]
        public void SyncAtStartup_DownloadingBook_NoLocalFolderCreated()
        {
            // Sync aborted before the book was copied locally.
            Assert.That(
                Directory.Exists(Path.Combine(_collectionFolder.FolderPath, kDownloadingBookName)),
                Is.False
            );
        }
    }

    /// <summary>
    /// Verifies SyncAtStartup behaviour when a downloading book is encountered during a
    /// first-time join (firstTimeJoin = true). The local collection folder is incomplete
    /// and unusable, so SyncAtStartup must throw BookDownloadingException to let
    /// SynchronizeRepoAndLocal clean up and show a fatal message.
    /// </summary>
    [TestFixture]
    public class SyncAtStartup_DownloadingBook_FirstTimeJoin_Tests
    {
        private TemporaryFolder _repoFolder;
        private TemporaryFolder _collectionFolder;
        private const string kDownloadingBookName = "Downloading Book During Join";

        [SetUp]
        public void SetUp()
        {
            _repoFolder = new TemporaryFolder("DownloadingBookJoin_Repo");
            _collectionFolder = new TemporaryFolder("DownloadingBookJoin_Local");

            FolderTeamCollection.CreateTeamCollectionLinkFile(
                _collectionFolder.FolderPath,
                _repoFolder.FolderPath
            );

            var booksDir = Path.Combine(_repoFolder.FolderPath, "Books");
            Directory.CreateDirectory(booksDir);
            File.WriteAllText(
                Path.Combine(booksDir, kDownloadingBookName + ".bloom"),
                "Partial download — not a valid zip"
            );
        }

        [TearDown]
        public void TearDown()
        {
            _repoFolder?.Dispose();
            _collectionFolder?.Dispose();
        }

        private ControllableFolderTeamCollection MakeCollection()
        {
            var mockTcManager = new Mock<ITeamCollectionManager>();
            var tcLog = new TeamCollectionMessageLog(
                TeamCollectionManager.GetTcLogPathFromLcPath(_collectionFolder.FolderPath)
            );
            var collection = new ControllableFolderTeamCollection(
                mockTcManager.Object,
                _collectionFolder.FolderPath,
                _repoFolder.FolderPath,
                tcLog
            );
            collection.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();
            collection.DownloadingBooks.Add(kDownloadingBookName);
            TeamCollectionManager.ForceCurrentUserForTests("test@somewhere.org");
            return collection;
        }

        [Test]
        public void SyncAtStartup_FirstTimeJoin_DownloadingBook_ThrowsBookDownloadingException()
        {
            var collection = MakeCollection();
            Assert.Throws<BookDownloadingException>(() =>
                collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: true)
            );
        }

        [Test]
        public void SyncAtStartup_FirstTimeJoin_DownloadingBook_ExceptionHasCorrectBookName()
        {
            var collection = MakeCollection();
            var ex = Assert.Throws<BookDownloadingException>(() =>
                collection.SyncAtStartup(new ProgressSpy(), firstTimeJoin: true)
            );
            Assert.That(ex.BookName, Is.EqualTo(kDownloadingBookName));
        }
    }

    /// <summary>
    /// Direct unit tests for FolderTeamCollection.IsBookDownloading.
    /// Some tests invoke Thread.Sleep (500 ms) so this fixture is intentionally small.
    /// </summary>
    [TestFixture]
    public class IsBookDownloadingTests
    {
        private TemporaryFolder _repoFolder;
        private TemporaryFolder _collectionFolder;
        private ExposedFolderTeamCollection _collection;

        [SetUp]
        public void SetUp()
        {
            _repoFolder = new TemporaryFolder("IsBookDownloading_Repo");
            _collectionFolder = new TemporaryFolder("IsBookDownloading_Local");

            FolderTeamCollection.CreateTeamCollectionLinkFile(
                _collectionFolder.FolderPath,
                _repoFolder.FolderPath
            );

            var mockTcManager = new Mock<ITeamCollectionManager>();
            var tcLog = new TeamCollectionMessageLog(
                TeamCollectionManager.GetTcLogPathFromLcPath(_collectionFolder.FolderPath)
            );
            _collection = new ExposedFolderTeamCollection(
                mockTcManager.Object,
                _collectionFolder.FolderPath,
                _repoFolder.FolderPath,
                tcLog
            );
            _collection.CollectionId = Bloom.TeamCollection.TeamCollection.GenerateCollectionId();

            Directory.CreateDirectory(Path.Combine(_repoFolder.FolderPath, "Books"));
        }

        [TearDown]
        public void TearDown()
        {
            _repoFolder?.Dispose();
            _collectionFolder?.Dispose();
        }

        private string BookPath(string bookName) =>
            Path.Combine(_repoFolder.FolderPath, "Books", bookName + ".bloom");

        [Test]
        public void IsBookDownloading_FileDoesNotExist_ReturnsFalse()
        {
            Assert.That(_collection.IsBookDownloadingPublic("no such book"), Is.False);
        }

        [Test]
        public void IsBookDownloading_FileOlderThanTenMinutes_ReturnsFalse()
        {
            // A stably corrupt file that has not been touched in a long time should not
            // be mistaken for an active download.
            var path = BookPath("old corrupt book");
            File.WriteAllText(path, "This is not a valid zip!");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-11));

            Assert.That(_collection.IsBookDownloadingPublic("old corrupt book"), Is.False);
        }

        [Test]
        public void IsBookDownloading_RecentStableFile_ReturnsFalse()
        {
            // A recently written file whose size does not change during the observation
            // window is treated as stably corrupt, not as an active download.
            var path = BookPath("recent stable book");
            File.WriteAllText(path, "This is not a valid zip!");
            // LastWriteTime is just now (recent), but no further writes will occur.

            Assert.That(_collection.IsBookDownloadingPublic("recent stable book"), Is.False);
        }

        [Test]
        public void IsBookDownloading_RecentGrowingFile_ReturnsTrue()
        {
            // A recently written file that keeps growing during the 500 ms window is
            // actively downloading.
            var path = BookPath("growing book");
            File.WriteAllText(path, "Initial content");

            // Append data to the file during the 500 ms Thread.Sleep inside IsBookDownloading.
            var appendTask = Task.Run(async () =>
            {
                await Task.Delay(100);
                File.AppendAllText(path, new string('x', 2000));
            });

            var result = _collection.IsBookDownloadingPublic("growing book");
            appendTask.Wait();

            Assert.That(result, Is.True);
        }
    }
}
