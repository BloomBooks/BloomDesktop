using System;
using System.IO;
using System.Linq;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;
using SIL.Progress;

namespace BloomTests.WebLibraryIntegration
{
    class BloomS3StandardUpDownloadTests
    {
        private BloomS3ClientTestDouble _client;
        private BloomS3ClientTestDouble _clientWhichMustWorkAroundUploadPermissions;
        private TemporaryFolder _workFolder;
        private string _srcCollectionPath;
        private string _destCollectionPath;
        private const string BookName = "Test Book";
        private string _storageKeyOfBookFolderParent;
        private string _storageKeyOfBookFolder;

        [OneTimeSetUp]
        public void SetupFixture()
        {
            // Basic setup
            _workFolder = new TemporaryFolder("unittest2");
            var workFolderPath = _workFolder.FolderPath;
            Assert.AreEqual(
                0,
                Directory.GetDirectories(workFolderPath).Count(),
                "Some stuff was left over from a previous test"
            );

            _client = new BloomS3ClientTestDouble(BloomS3Client.UnitTestBucketName);
            _clientWhichMustWorkAroundUploadPermissions = new BloomS3ClientTestDouble(
                BloomS3Client.UnitTestBucketName,
                shouldWorkAroundUploadPermissions: true
            );

            // Now do standard upload/download. We save time by making this whole class do one upload/download sequence
            // on the assumption that things that should be uploaded were if they make it through the download process too.
            // Individual tests just compare what was uploaded with what came back through the download.

            _storageKeyOfBookFolderParent = Guid.NewGuid() + "/";
            _storageKeyOfBookFolder = _storageKeyOfBookFolderParent + BookName + "/";

            // First create folder to upload from
            var unittestGuid = Guid.NewGuid();
            var srcFolder = new TemporaryFolder(_workFolder, "unittest-src-" + unittestGuid);
            _srcCollectionPath = srcFolder.FolderPath;

            // Then create standard book
            var book = MakeBook(srcFolder);

            // Upload standard book
            UploadBook(book);

            // Create folder to download to
            var destFolder = new TemporaryFolder(_workFolder, "unittest-dest-" + unittestGuid);
            _destCollectionPath = destFolder.FolderPath;

            // Download standard book
            DownloadBook();
        }

        [OneTimeTearDown]
        public async System.Threading.Tasks.Task TearDown()
        {
            await _client.DeleteFromUnitTestBucketAsync(_storageKeyOfBookFolderParent);
            _workFolder.Dispose();
            _client.Dispose();
            _clientWhichMustWorkAroundUploadPermissions.Dispose();
        }

        private string MakeBook(TemporaryFolder srcFolder)
        {
            var bookFolder = new TemporaryFolder(srcFolder, BookName).FolderPath;
            File.WriteAllText(Path.Combine(bookFolder, "one.htm"), @"test");
            File.WriteAllText(Path.Combine(bookFolder, "one.css"), @"test");
            File.WriteAllText(Path.Combine(bookFolder, "preview.pdf"), @"test pdf file");
            Directory.CreateDirectory(Path.Combine(bookFolder, "audio"));
            File.WriteAllText(Path.Combine(bookFolder, "audio", "one.mp3"), "test mp3 file");
            return bookFolder;
        }

        private void UploadBook(string bookFolder)
        {
            _clientWhichMustWorkAroundUploadPermissions.UploadBook(
                _storageKeyOfBookFolder,
                bookFolder,
                new[] { "one.htm", "one.css", "preview.pdf", "audio/one.mp3" },
                new NullProgress()
            );
        }

        private void DownloadBook()
        {
            var expectedBookDestination = Path.Combine(_destCollectionPath, BookName);
            var actualDestination = _client.DownloadBook(
                BloomS3Client.UnitTestBucketName,
                _storageKeyOfBookFolderParent,
                _destCollectionPath
            );
            Assert.AreEqual(expectedBookDestination, actualDestination);
        }

        [Test]
        public void UploadDownloadStandardBook_FilesAreInExpectedDirectory()
        {
            var fullBookSrcPath = Path.Combine(_srcCollectionPath, BookName);
            var fullBookDestPath = Path.Combine(_destCollectionPath, BookName);

            Assert.IsTrue(Directory.Exists(_destCollectionPath));
            var srcFileCount = Directory
                .GetFiles(fullBookSrcPath, "*.*", SearchOption.AllDirectories)
                .Count();

            Assert.That(
                _client.GetBookFileCountForUnitTest(_storageKeyOfBookFolderParent),
                Is.EqualTo(srcFileCount)
            );
            var matching = Directory.GetFiles(fullBookDestPath, "*.*", SearchOption.AllDirectories);
            // preview.pdf exists in the source, but is not pulled down to the destination.
            Assert.That(matching.Length, Is.EqualTo(srcFileCount - 1));
            foreach (
                var relativePath in Directory
                    .GetFiles(fullBookSrcPath, "*.*", SearchOption.AllDirectories)
                    .Select(fullPath => fullPath.Substring(fullBookSrcPath.Length + 1))
                    .Where(relativePath => !relativePath.EndsWith("preview.pdf"))
            )
            {
                Assert.IsTrue(
                    File.Exists(Path.Combine(fullBookDestPath, relativePath)),
                    Path.Combine(fullBookDestPath, relativePath) + " should exist but doesn't"
                );
            }
        }
    }
}
