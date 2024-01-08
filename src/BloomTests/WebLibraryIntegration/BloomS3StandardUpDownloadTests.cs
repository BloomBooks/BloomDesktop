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
        private readonly string[] ExcludedFiles =
        {
            "thumbs.db",
            "book.userprefs",
            "extra.pdf",
            "preview.pdf",
            "my.bloompack",
            "extra.css.map"
        };
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
            // If we want to upload and download to separate (collection) folders, we need another layer for the actual book

            _storageKeyOfBookFolder = Guid.NewGuid().ToString();

            // First create folder to upload from
            var unittestGuid = Guid.NewGuid();
            var srcFolder = new TemporaryFolder(_workFolder, "unittest-src-" + unittestGuid);
            _srcCollectionPath = srcFolder.FolderPath;

            // Then create standard book
            var book = MakeBookIncludingThumbs(srcFolder);

            // Upload standard book
            UploadBook(book);

            // Create folder to download to
            var destFolder = new TemporaryFolder(_workFolder, "unittest-dest-" + unittestGuid);
            _destCollectionPath = destFolder.FolderPath;

            // Download standard book
            DownloadBook();
        }

        [OneTimeTearDown]
        public void TearDown()
        {
            _client.DeleteFromUnitTestBucket(_storageKeyOfBookFolder);
            _workFolder.Dispose();
            _client.Dispose();
            _clientWhichMustWorkAroundUploadPermissions.Dispose();
        }

        private string MakeBookIncludingThumbs(TemporaryFolder srcFolder)
        {
            var bookFolder = new TemporaryFolder(srcFolder, BookName).FolderPath;
            File.WriteAllText(Path.Combine(bookFolder, "one.htm"), @"test");
            File.WriteAllText(Path.Combine(bookFolder, "one.css"), @"test");
            File.WriteAllText(Path.Combine(bookFolder, "preview.pdf"), @"test pdf file");
            File.WriteAllText(Path.Combine(bookFolder, "extra.pdf"), @"unwanted pdf file");
            File.WriteAllText(Path.Combine(bookFolder, "extra.css.map"), @"unwanted map file");
            File.WriteAllText(Path.Combine(bookFolder, "thumbs.db"), @"test thumbs.db file");
            File.WriteAllText(
                Path.Combine(bookFolder, "book.userPrefs"),
                @"test book.userPrefs file"
            );
            File.WriteAllText(Path.Combine(bookFolder, "my.bloompack"), @"test bloompack file");
            return bookFolder;
        }

        private void UploadBook(string bookFolder)
        {
            _clientWhichMustWorkAroundUploadPermissions.UploadBook(
                _storageKeyOfBookFolder,
                bookFolder,
                new NullProgress(),
                pdfToInclude: "preview.pdf",
                true,
                true,
                null,
                null,
                null,
                null,
                true
            );
        }

        private void DownloadBook()
        {
            var expectedBookDestination = Path.Combine(_destCollectionPath, BookName);
            var actualDestination = _client.DownloadBook(
                BloomS3Client.UnitTestBucketName,
                _storageKeyOfBookFolder,
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
            var srcFileCount = Directory.GetFiles(fullBookSrcPath).Count();

            // Do not count the excluded files (thumbs.db, extra.pdf, etc.)
            // preview.pdf exists in the source, but is not pulled down to the destination.
            Assert.That(
                _client.GetBookFileCountForUnitTest(_storageKeyOfBookFolder),
                Is.EqualTo(srcFileCount - ExcludedFiles.Length + 1)
            );
            var matching = Directory.GetFiles(fullBookDestPath);
            Assert.That(matching.Length, Is.EqualTo(srcFileCount - ExcludedFiles.Length));
            foreach (
                var fileName in Directory
                    .GetFiles(fullBookSrcPath)
                    .Select(Path.GetFileName)
                    .Where(file => !ExcludedFiles.Contains(file.ToLower()))
            )
            {
                Assert.IsTrue(File.Exists(Path.Combine(fullBookDestPath, fileName)));
            }
        }

        [Test]
        public void UploadDownloadStandardBook_ExcludedFilesFileDidNotGetSent()
        {
            // Verify that excluded files did NOT get uploaded
            foreach (var file in ExcludedFiles)
            {
                var notexpectedDestPath = Path.Combine(_destCollectionPath, BookName, file);
                Assert.IsFalse(File.Exists(notexpectedDestPath), notexpectedDestPath);
            }
        }
    }
}
