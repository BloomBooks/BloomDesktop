using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using BloomTests.TestDoubles.CollectionTab;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using SIL.TestUtilities;

namespace BloomTests.CollectionTab
{
	[TestFixture]
	[SuppressMessage("ReSharper", "LocalizableElement")]
	class LibraryModelTests
	{
		private TemporaryFolder _collection;
		private TemporaryFolder _folder;
		private FakeLibraryModel _testLibraryModel;

		[SetUp]
		public void Setup()
		{
			ErrorReport.IsOkToInteractWithUser = false;
			_folder = new TemporaryFolder("LibraryModelTests");

			// ENHANCE: Sometimes making the FakeCollection temporary folder causes an UnauthorizedAccessException.
			// Not exactly sure why or what to do about it. Possibly it could be related to file system operations
			// being async in nature???
			_collection = new TemporaryFolder(_folder, "FakeCollection");
			MakeFakeCssFile();
			_testLibraryModel = new FakeLibraryModel(_collection);
		}

		private void MakeFakeCssFile()
		{
			var cssName = Path.Combine(_collection.Path, "FakeCss.css");
			File.WriteAllText(cssName, "Fake CSS file");
		}

		private string MakeBook()
		{
			var f = new TemporaryFolder(_collection, "unittest-" + Guid.NewGuid());
			File.WriteAllText(Path.Combine(f.Path, "one.htm"), "test");
			File.WriteAllText(Path.Combine(f.Path, "one.css"), "test");
			File.WriteAllText(Path.Combine(f.Path, "meta.json"), new BookMetaData().Json);
			return f.Path;
		}

		private void AddThumbsFile(string bookFolderPath)
		{
			File.WriteAllText(Path.Combine(bookFolderPath, "thumbs.db"), "test thumbs.db file");
		}

		private void AddPdfFile(string bookFolderPath)
		{
			File.WriteAllText(Path.Combine(bookFolderPath, "xfile1.pdf"), "test pdf file");
		}

		private void AddUnnecessaryHtmlFile(string srcBookPath)
		{
			string extraHtmDir = Path.Combine(srcBookPath, "unnecessaryExtraFiles");
			Directory.CreateDirectory(extraHtmDir);
			string htmContents = "<html><body><w:sdtPr></w:sdtPr></body></html>";
			File.WriteAllText(Path.Combine(extraHtmDir, "causesException.htm"), htmContents);
		}

		// Imitate LibraryModel.MakeBloomPack(), but bypasses the user interaction
		private void MakeTestBloomPack(string path, bool forReaderTools)
		{
			var (dirName, dirPrefix) = _testLibraryModel.GetDirNameAndPrefixForCollectionBloomPack();
			_testLibraryModel.MakeBloomPackInternal(path, dirName, dirPrefix, forReaderTools, isCollection: true);
		}

		// Imitate LibraryModel.MakeBloomPack(), but bypasses the user interaction
		private void MakeTestSingleBookBloomPack(string path, string bookSrcPath, bool forReaderTools)
		{
			var (dirName, dirPrefix) = _testLibraryModel.GetDirNameAndPrefixForSingleBookBloomPack(bookSrcPath);
			_testLibraryModel.MakeBloomPackInternal(path, dirName, dirPrefix, forReaderTools, isCollection: false);
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
			MakeTestBloomPack(bloomPackName, false);

			// Don't do anything with the zip file except read in the filenames
			var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

			// +2 for collection-level CustomCollectionSettings and FakeCss.css file, -1 for pdf file, so the count is +1
			Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Count() + 1));

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
			MakeTestBloomPack(bloomPackName, false);

			// Don't do anything with the zip file except read in the filenames
			var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

			// +2 for collection-level CustomCollectionSettings and FakeCss.css file, -1 for thumbs file, so the count is +1
			Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Count() + 1));

			foreach (var filePath in actualFiles)
			{
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile));
			}
		}

		[Test]
		public void MakeBloomPack_DoesntIncludeCorrupt_Map_OrBakFiles()
		{
			var srcBookPath = MakeBook();
			const string excludedFile1 = BookStorage.PrefixForCorruptHtmFiles + ".htm";
			const string excludedFile2 = BookStorage.PrefixForCorruptHtmFiles + "2.htm";
			const string excludedFile3 = "Basic Book.css.map";
			string excludedBackup = Path.GetFileName(srcBookPath) + ".bak";
			RobustFile.WriteAllText(Path.Combine(srcBookPath, excludedFile1), "rubbish");
			RobustFile.WriteAllText(Path.Combine(srcBookPath, excludedFile2), "rubbish");
			RobustFile.WriteAllText(Path.Combine(srcBookPath, excludedFile3), "rubbish");
			RobustFile.WriteAllText(Path.Combine(srcBookPath, excludedBackup), "rubbish");
			var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

			// Imitate LibraryModel.MakeBloomPack() without the user interaction
			MakeTestBloomPack(bloomPackName, false);

			// Don't do anything with the zip file except read in the filenames
			var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

			// +2 for collection-level CustomCollectionSettings and FakeCss.css file, -4 for corrupt, .map and .bak files, so the count is -2
			Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Length - 2));

			foreach (var filePath in actualFiles)
			{
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile1));
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile2));
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile3));
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedBackup));
			}
		}

		[Test]
		public void MakeBloomPack_AddslockFormattingMetaTagToReader()
		{
			var srcBookPath = MakeBook();

			// the html file needs to have the same name as its directory
			var testFileName = Path.GetFileName(srcBookPath) + ".htm";
			var readerName = Path.Combine(srcBookPath, testFileName);

			var bloomPackName = Path.Combine(_folder.Path, "testReaderPack.BloomPack");

			var sb = new StringBuilder();
			sb.AppendLine("<!DOCTYPE html>");
			sb.AppendLine("<html>");
			sb.AppendLine("<head>");
			sb.AppendLine("    <meta charset=\"UTF-8\"></meta>");
			sb.AppendLine("    <meta name=\"Generator\" content=\"Bloom Version 3.3.0 (apparent build date: 28-Jul-2015)\"></meta>");
			sb.AppendLine("    <meta name=\"BloomFormatVersion\" content=\"2.0\"></meta>");
			sb.AppendLine("    <meta name=\"pageTemplateSource\" content=\"Leveled Reader\"></meta>");
			sb.AppendLine("    <title>Leveled Reader</title>");
			sb.AppendLine("    <link rel=\"stylesheet\" href=\"basePage.css\" type=\"text/css\"></link>");
			sb.AppendLine("</head>");
			sb.AppendLine("<body>");
			sb.AppendLine("</body>");
			sb.AppendLine("</html>");

			File.WriteAllText(readerName, sb.ToString());

			// make the BloomPack
			MakeTestBloomPack(bloomPackName, true);

			// get the reader file from the BloomPack
			var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);
			var zipEntryName = actualFiles.FirstOrDefault(file => file.EndsWith(testFileName));
			Assert.That(zipEntryName, Is.Not.Null.And.Not.Empty);

			string outputText;
			using (var zip = new ZipFile(bloomPackName))
			{
				var ze = zip.GetEntry(zipEntryName);
				var buffer = new byte[4096];

				using (var instream = zip.GetInputStream(ze))
				using (var writer = new MemoryStream())
				{
					ICSharpCode.SharpZipLib.Core.StreamUtils.Copy(instream, writer, buffer);
					writer.Position = 0;
					using (var reader = new StreamReader(writer))
					{
						outputText = reader.ReadToEnd();
					}
				}
			}

			// check for the lockFormatting meta tag
			Assert.That(outputText, Is.Not.Null.And.Not.Empty);
			Assert.IsTrue(outputText.Contains("<meta name=\"lockFormatting\" content=\"true\">"));
		}

		[Test]
		public void MakeCollectionBloomPack_DoesntParseExtraHtmlFiles()
		{
			var srcBookPath = MakeBook();
			AddUnnecessaryHtmlFile(srcBookPath);
			var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

			// System Under Test
			// Imitate LibraryModel.MakeBloomPack() without the user interaction
			MakeTestBloomPack(bloomPackName, false);

			// Verification
			// Just make sure it doesn't throw an exception.
			return;
		}

		[Test]
		public void MakeSingleBookBloomPack_DoesntParseExtraHtmlFiles()
		{
			var srcBookPath = MakeBook();
			AddUnnecessaryHtmlFile(srcBookPath);
			var bloomPackName = Path.Combine(_folder.Path, "testPack.BloomPack");

			// System Under Test
			// Imitate LibraryModel.MakeBloomPack() without the user interaction
			MakeTestSingleBookBloomPack(bloomPackName, srcBookPath, false);

			// Verification
			// Just make sure it doesn't throw an exception.
			return;
		}
	}
}
