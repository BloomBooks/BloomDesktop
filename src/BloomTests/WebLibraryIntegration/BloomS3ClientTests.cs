using System;
using System.IO;
using System.Linq;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;
using SIL.Progress;

namespace BloomTests.WebLibraryIntegration
{
	/// <summary>
	/// This file now contains only edge cases. For a more efficient upload/download test,
	/// the more standard tests have been separated out into BloomS3StandardUpDownloadTests.
	/// </summary>
	[TestFixture]
	public class BloomS3ClientTests
	{
		private BloomS3Client _client;
		private TemporaryFolder _workFolder;

		[SetUp]
		public void Setup()
		{
			_workFolder = new TemporaryFolder("unittest");
			var workFolderPath = _workFolder.FolderPath;
			Assert.AreEqual(0, Directory.GetDirectories(workFolderPath).Count(), "Some stuff was left over from a previous test");
			Assert.AreEqual(0, Directory.GetFiles(workFolderPath).Count(), "Some stuff was left over from a previous test");

			_client = new BloomS3Client(BloomS3Client.UnitTestBucketName);
		}

		[TearDown]
		public void TearDown()
		{
			_workFolder.Dispose();
			_client.Dispose();
		}

		/// <summary>
		/// I actually don't care at the moment if we throw or not, I just want to specify what the behavior is.
		/// </summary>
		[Test]
		public void UploadBook_EmptyFolder_DoesntThrow()
		{
			var storageKeyOfBookFolder = Guid.NewGuid().ToString();
			using (var f = new TemporaryFolder(_workFolder, "emptyFolder"))
			{
				_client.UploadBook(storageKeyOfBookFolder, f.FolderPath, new NullProgress());
			}
			// This doesn't actually create an entry, since the folder is empty,
			// so no need to delete it after our test
		}

		[Test]
		public void DownloadBook_DoesNotExist_Throws()
		{
			Assert.Throws<DirectoryNotFoundException>(() => _client.DownloadBook(BloomS3Client.UnitTestBucketName, "notthere", _workFolder.FolderPath));
		}
	}
}
