using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using Palaso.TestUtilities;

namespace BloomTests.CollectionTab
{
	[TestFixture]
	class LibraryModelTests
	{
		private TemporaryFolder _collection;
		private TemporaryFolder _folder;
		private TestLibraryModel _testLibraryModel;

		[SetUp]
		public void Setup()
		{
			_folder = new TemporaryFolder("LibraryModelTests");
			_collection = new TemporaryFolder(_folder, "FakeCollection");
			MakeFakeCssFile();
			_testLibraryModel = new TestLibraryModel(_collection);
		}

		private void MakeFakeCssFile()
		{
			var cssName = Path.Combine(_collection.Path, "FakeCss.css");
			File.WriteAllText(cssName, "Fake CSS file");
		}

		private string MakeBook()
		{
			var f = new TemporaryFolder(_collection, "unittest-" + Guid.NewGuid());
			File.WriteAllText(Path.Combine(f.FolderPath, "one.htm"), "test");
			File.WriteAllText(Path.Combine(f.FolderPath, "one.css"), "test");
			return f.FolderPath;
		}

		private void AddThumbsFile(string bookFolderPath)
		{
			File.WriteAllText(Path.Combine(bookFolderPath, "thumbs.db"), "test thumbs.db file");
		}

		private void AddPdfFile(string bookFolderPath)
		{
			File.WriteAllText(Path.Combine(bookFolderPath, "xfile1.pdf"), "test pdf file");
		}

		// Imitate LibraryModel.MakeBloomPack() without the user interaction
		private void MakeTestBloomPack(string bloomPackName)
		{
			using (var fsOut = File.Create(bloomPackName))
			{
				using (var zipStream = new ZipOutputStream(fsOut))
				{
					zipStream.SetLevel(9);

					_testLibraryModel.RunCompressDirectoryTest(zipStream);

					zipStream.IsStreamOwner = true; // makes the Close() also close the underlying stream
					zipStream.Close();
				}
			}
		}

		// Don't do anything with the zip file except read in the filenames
		private static List<string> GetActualFilenamesFromZipfile(string bloomPackName)
		{
			var actualFiles = new List<string>();
			using (var zip = new ZipFile(bloomPackName))
			{
				actualFiles.AddRange(from ZipEntry entry in zip select entry.Name);
				zip.Close();
			}
			return actualFiles;
		}

		[Test]
		public void MakeBloomPack_DoesntIncludePdfFile()
		{
			var srcBookPath = MakeBook();
			AddPdfFile(srcBookPath);
			const string excludedFile = "xfile1.pdf";
			var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

			// Imitate LibraryModel.MakeBloomPack() without the user interaction
			MakeTestBloomPack(bloomPackName);

			// Don't do anything with the zip file except read in the filenames
			var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

			// +1 for collection-level css file, -1 for pdf file, so the count is right
			Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Count()));

			foreach (var filePath in actualFiles)
			{
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile));
			}
		}

		[Test]
		public void MakeBloomPack_DoesntIncludeThumbsFile()
		{
			var srcBookPath = MakeBook();
			AddThumbsFile(srcBookPath);
			const string excludedFile = "thumbs.db";
			var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

			// Imitate LibraryModel.MakeBloomPack() without the user interaction
			MakeTestBloomPack(bloomPackName);

			// Don't do anything with the zip file except read in the filenames
			var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

			// +1 for collection-level css file, -1 for thumbs file, so the count is right
			Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Count()));

			foreach (var filePath in actualFiles)
			{
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile));
			}
		}
	}
}
