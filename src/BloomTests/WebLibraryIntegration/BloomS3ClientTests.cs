using System;
using System.IO;
using System.Linq;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;
using Palaso.Progress;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class BloomS3ClientTests
	{
		private BloomS3Client _client;
		private TemporaryFolder _workFolder;
		private string _workFolderPath;
		private string _storageKeyOfBookFolder;

		[SetUp]
		public void Setup()
		{
			_workFolder = new TemporaryFolder("unittest");
			_workFolderPath = _workFolder.FolderPath;
			Assert.AreEqual(0,Directory.GetDirectories(_workFolderPath).Count(),"Some stuff was left over from a previous test");
			Assert.AreEqual(0, Directory.GetFiles(_workFolderPath).Count(),"Some stuff was left over from a previous test");

			_storageKeyOfBookFolder = Guid.NewGuid().ToString();
			_client = new BloomS3Client(BloomS3Client.UnitTestBucketName);
		}

		[TearDown]
		public void TearDown()
		{
			_workFolder.Dispose();
			_client.EmptyUnitTestBucket(_storageKeyOfBookFolder);
			_client.Dispose();
		}

		private string MakeBook()
		{
			var f = new TemporaryFolder(_workFolder, "unittest-" + Guid.NewGuid());
			File.WriteAllText(Path.Combine(f.FolderPath, "one.htm"), "test");
			File.WriteAllText(Path.Combine(f.FolderPath, "one.css"), "test");
			return f.FolderPath;
		}

		private void AddThumbsFile(string bookFolderPath)
		{
			File.WriteAllText(Path.Combine(bookFolderPath, "thumbs.db"), "test thumbs.db file");
		}

		private string UploadBook(string path)
		{
			_client.UploadBook(_storageKeyOfBookFolder, path, new NullProgress());
			return _storageKeyOfBookFolder;
		}

		[Test]
		public void UploadBook_Simple_FilesAreOnS3InExpectedDirectory()
		{
			string srcBookPath = MakeBook();
			var storageKeyOfBookFolder = UploadBook(srcBookPath);

			// It's possible that another unit test uploads a book at the same time, so we can't
			// test for strict equality.
			Assert.That(_client.GetCountOfAllFilesInBucket(), Is.AtLeast(Directory.GetFiles(srcBookPath).Count()));

			var expectedDir = Path.GetFileName(srcBookPath);
			foreach (var file in Directory.GetFiles(srcBookPath))
			{
				Assert.IsTrue(_client.FileExists(storageKeyOfBookFolder, expectedDir, Path.GetFileName(file)));
			}
		}

		[Test]
		public void UploadBook_ContainsThumbsFile_DontCopyItToS3()
		{
			string srcBookPath = MakeBook();
			AddThumbsFile(srcBookPath);
			const string excludedFile = "thumbs.db";
			var storageKeyOfBookFolder = UploadBook(srcBookPath);

			// It's possible that another unit test uploads a book at the same time, so we can't
			// test for strict equality.
			Assert.That(_client.GetCountOfAllFilesInBucket(), Is.AtLeast(Directory.GetFiles(srcBookPath).Count()));

			var expectedDir = Path.GetFileName(srcBookPath);
			foreach (var file in Directory.GetFiles(srcBookPath))
			{
				if (Path.GetFileName(file) == excludedFile)
					continue; // should NOT be uploaded
				Assert.IsTrue(_client.FileExists(storageKeyOfBookFolder, expectedDir, Path.GetFileName(file)));
			}
			Assert.IsFalse(_client.FileExists(storageKeyOfBookFolder, expectedDir, excludedFile));
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
				_client.UploadBook(storageKeyOfBookFolder, f.FolderPath, new NullProgress());
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
