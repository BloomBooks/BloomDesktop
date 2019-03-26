using System;
using System.IO;
using Bloom.Book;
using Bloom.WebLibraryIntegration;
using BloomTemp;
using NUnit.Framework;
using Logger = SIL.Reporting.Logger;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class BookTransferDupIdTests
	{
		private string _thisTestId;
		private TemporaryFolder _workFolder;
		private string _workFolderPath;
		private TemporaryFolder[] _foldersToDispose;
		private string _guid1;
		private string _guid2;

		[OneTimeSetUp]
		public void TestFixtureSetUp()
		{
			Logger.Init();
			_thisTestId = Guid.NewGuid().ToString().Replace('-', '_');
		}

		[SetUp]
		public void Setup()
		{
			_workFolder = new TemporaryFolder("unittest-" + _thisTestId);
			_workFolderPath = _workFolder.FolderPath;
			Assert.AreEqual(0, Directory.GetDirectories(_workFolderPath).Length, "Some stuff was left over from a previous test");
			Assert.AreEqual(0, Directory.GetFiles(_workFolderPath).Length, "Some stuff was left over from a previous test");
			SetupTestData();
		}

		[TearDown]
		public void TearDown()
		{
			foreach (var folder in _foldersToDispose)
			{
				folder.Dispose();
			}
			_workFolder.Dispose();
		}

		[OneTimeTearDown]
		public void FinalTearDown()
		{
			Logger.ShutDown();
		}

		private void SetupTestData()
		{
			// _workFolder
			//  | subFolder1
			//  |  | subFolder2
			//  |  |  | bookFolder1
			//  |  |  | bookFolder2
			//  | subFolder3
			//  |  | bookFolder3
			//  |  | bookFolder4
			//  |  | bookFolder5
			//
			// bookFolder3 contains oldest timestamp of guid1
			// bookFolder4 contains guid2
			// bookFolder1 contains guid1 (should get new guid)
			// bookFolder2 contains guid1 (should get new guid)
			// bookFolder5 contains guid2 (same timestamp)
			var subFolder1 = new TemporaryFolder(_workFolder, "subFolder1");
			var subFolder2 = new TemporaryFolder(subFolder1, "subFolder2");
			var subFolder3 = new TemporaryFolder(_workFolder, "subFolder3");
			var bookFolder1 = new TemporaryFolder(subFolder2, "bookFolder1");
			var bookFolder2 = new TemporaryFolder(subFolder2, "bookFolder2");
			var bookFolder3 = new TemporaryFolder(subFolder3, "bookFolder3");
			var bookFolder4 = new TemporaryFolder(subFolder3, "bookFolder4");
			var bookFolder5 = new TemporaryFolder(subFolder3, "bookFolder5");
			_foldersToDispose = new[]
			{
				subFolder1, subFolder2, subFolder3, bookFolder1, bookFolder2, bookFolder3, bookFolder4, bookFolder5
			};
			_guid1 = Guid.NewGuid().ToString();
			_guid2 = Guid.NewGuid().ToString();
			var book3Time = new DateTime(2019,3,5,15,33,30);
			var smallSpan = new TimeSpan(100000000); // only 10 seconds! (100ns is too short for Linux file timestamps)
			SetupMetaData(bookFolder1, _guid1, book3Time + smallSpan);
			SetupMetaData(bookFolder2, _guid1, book3Time + smallSpan + smallSpan);
			SetupMetaData(bookFolder3, _guid1, book3Time);
			SetupMetaData(bookFolder4, _guid2, book3Time + smallSpan + smallSpan + smallSpan);
			SetupMetaData(bookFolder5, _guid2, book3Time + smallSpan + smallSpan + smallSpan);
		}

		private static void SetupMetaData(TemporaryFolder folder, string guid, DateTime timestamp)
		{
			var jsonPath = Path.Combine(folder.FolderPath, BookInfo.MetaDataFileName);
			var metaData = @"{'experimental':'false','bookInstanceId':'" + guid + @"','suitableForMakingShells':'true'}";
			File.WriteAllText(jsonPath, metaData);
			File.SetLastWriteTimeUtc(jsonPath, timestamp);
		}

		private static string GetInstanceIdFromMetadataFile(TemporaryFolder folder)
		{
			var bi = new BookInfo(folder.FolderPath, false);
			return bi.Id;
		}

		[Test]
		public void DuplicateIdTest()
		{
			// SUT (use workFolder as the root directory)
			BookTransfer.BulkRepairInstanceIds(_workFolderPath);

			// Verification
			var book1 = _foldersToDispose[3];
			var book2 = _foldersToDispose[4];
			var book3 = _foldersToDispose[5];
			var book4 = _foldersToDispose[6];
			var book5 = _foldersToDispose[7];
			Assert.That(GetInstanceIdFromMetadataFile(book1), Is.Not.EqualTo(_guid1), "book1 should have changed guid1");
			Assert.That(GetInstanceIdFromMetadataFile(book2), Is.Not.EqualTo(_guid1), "book2 should have changed guid1");
			Assert.That(GetInstanceIdFromMetadataFile(book1), Is.Not.EqualTo(GetInstanceIdFromMetadataFile(book2)), "book1 should have different guid than book2");
			Assert.That(GetInstanceIdFromMetadataFile(book3), Is.EqualTo(_guid1), "book3 should have guid1");
			Assert.That(GetInstanceIdFromMetadataFile(book4), Is.EqualTo(_guid2), "book4 should have guid2");
			Assert.That(GetInstanceIdFromMetadataFile(book4), Is.Not.EqualTo(GetInstanceIdFromMetadataFile(book5)), "book4 should have different guid than book5");
		}
	}
}
