using System;
using System.IO;
using System.Linq;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;

namespace BloomTests.WebLibraryIntegration
{
    [TestFixture]
    public class BloomS3ClientTests
    {
        private BloomS3Client _client;
        private TemporaryFolder _workFolder;
        private string _workFolderPath;

        [SetUp]
        public void Setup()
        {
            _workFolder = new TemporaryFolder("unittest");
            _workFolderPath = _workFolder.FolderPath;
            Assert.AreEqual(0,Directory.GetDirectories(_workFolderPath).Count(),"Some stuff was left over from a previous test");
            Assert.AreEqual(0, Directory.GetFiles(_workFolderPath).Count(),"Some stuff was left over from a previous test");

            _client = new BloomS3Client(BloomS3Client.UnitTestBucketName);
            _client.EmptyUnitTestBucket();
        }

        [TearDown]
        public void TearDown()
        {
            _workFolder.Dispose();
        }


        private string MakeBook()
        {
            var f = new TemporaryFolder(_workFolder, "unittest");
            File.WriteAllText(Path.Combine(f.FolderPath, "one.htm"), "test");
            File.WriteAllText(Path.Combine(f.FolderPath, "one.css"), "test");
            return f.FolderPath;
        }

        private string UploadBook(string path)
        {
            string storageKeyOfBookFolder = Guid.NewGuid().ToString();
            _client.UploadBook(storageKeyOfBookFolder, path);
            return storageKeyOfBookFolder;
        }

        [Test]
        public void UploadBook_Simple_FilesAreOnS3InExpectedDirectory()
        {
            string srcBookPath = MakeBook();
            var storageKeyOfBookFolder = UploadBook(srcBookPath);

            Assert.AreEqual(Directory.GetFiles(srcBookPath).Count(), _client.GetCountOfAllFilesInBucket());

            foreach (var file in Directory.GetFiles(srcBookPath))
            {
                Assert.IsTrue(_client.FileExists(storageKeyOfBookFolder, "unittest", Path.GetFileName(file)));
            }
        }


        /// <summary>
        /// I actually don't care at the moment if we throw or not, I just want to specify what the behavior is.
        /// </summary>
        [Test]
        public void UploadBook_EmptyFolder_DoesntThrow()
        {
            string storageKeyOfBookFolder = Guid.NewGuid().ToString();
            using (var f = new TemporaryFolder(_workFolder,"emptyFolder"))
            {
                _client.UploadBook(storageKeyOfBookFolder, f.FolderPath);
            }
        }

        [Test]
        [ExpectedException(typeof(DirectoryNotFoundException))]
        public void DownloadBook_DoesNotExist_Throws()
        {
            _client.DownloadBook("notthere", _workFolderPath);
        }

        [Test]
        public void DownloadBook_BookExists_PlacesFilesCorrectly()
        {
            var bookToUploadPath = MakeBook();
            var bookFolderName = Path.GetFileName(bookToUploadPath);
            var storageKeyOfBookFolder = UploadBook(bookToUploadPath);
            _client.DownloadBook(storageKeyOfBookFolder, _workFolderPath);
            string expectedDownloadPath = _workFolder.Combine(bookFolderName);
            Assert.IsTrue(Directory.Exists(expectedDownloadPath));
            Assert.AreEqual(Directory.GetFiles(bookToUploadPath).Count(),Directory.GetFiles(expectedDownloadPath).Count());
        }
    }
}
