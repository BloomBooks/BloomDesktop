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

		[TestCase("bucket/my.pdf")]
		[TestCase("bucket/my.PDF")]
		[TestCase("bucket/thumbs.db")]
		// We don't download narration audio (for now)
		[TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.mp3")]
		[TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.MP3")]
		[TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.wav")]
		[TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.WAV")]
		[TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.ogg")]
		[TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.OGG")]
		// We sometimes prepend a guid which starts with a number with "i" to make epubs happy.
		[TestCase("bucket/i1EC14CB3-CAEC-4C83-8092-74C78CA7C515.mp3")]
		public void AvoidThisFile_ShouldAvoid(string objectKey)
		{
			Assert.True(BloomS3Client.AvoidThisFile(objectKey));
		}

		[TestCase("bucket/abc.def")]
		[TestCase("bucket/abc.def.ghi")]
		[TestCase("bucket/music.mp3")]
		[TestCase("bucket/music.MP3")]
		[TestCase("bucket/music.wav")]
		[TestCase("bucket/music.WAV")]
		[TestCase("bucket/music.ogg")]
		[TestCase("bucket/music.OGG")]
		[TestCase("bucket/1EC14CB3-CAEC-4C83-8092-74C78CA7C515.css")]
		public void AvoidThisFile_ShouldNotAvoid(string objectKey)
		{
			Assert.False(BloomS3Client.AvoidThisFile(objectKey));
		}
	}
}
