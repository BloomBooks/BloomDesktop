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

		[Test]
		public void MakeBloomPack_DoesntIncludeThumbsFile()
		{
			var srcBookPath = MakeBook();
			AddThumbsFile(srcBookPath);
			const string excludedFile = "thumbs.db";
			var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

			// Imitate LibraryModel.MakeBloomPack() without the user interaction
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

			// Don't do anything with the zip file except read in the filenames
			var actualFiles = new List<string>();
			ZipFile zip = null;
			try
			{
				zip = new ZipFile(bloomPackName);
				actualFiles.AddRange(from ZipEntry entry in zip select entry.Name);
			}
			finally
			{
				if (zip != null)
					zip.Close();
			}
			// +1 for collection-level css file, -1 for thumbs file, so the count is right
			Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Count()));

			foreach (var filePath in actualFiles)
			{
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile));
			}
		}
	}
}
