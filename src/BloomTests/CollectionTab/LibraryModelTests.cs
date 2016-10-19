using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using NUnit.Framework;
using SIL.TestUtilities;

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
			File.WriteAllText(Path.Combine(f.Path, "one.htm"), "test");
			File.WriteAllText(Path.Combine(f.Path, "one.css"), "test");
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

		// Imitate LibraryModel.MakeBloomPack() without the user interaction
		private void MakeTestBloomPack(string bloomPackName, bool forReaderTools)
		{
			using (var fsOut = File.Create(bloomPackName))
			{
				using (var zipStream = new ZipOutputStream(fsOut))
				{
					zipStream.SetLevel(9);

					_testLibraryModel.RunCompressDirectoryTest(zipStream, forReaderTools);

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
			MakeTestBloomPack(bloomPackName, false);

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
			MakeTestBloomPack(bloomPackName, false);

			// Don't do anything with the zip file except read in the filenames
			var actualFiles = GetActualFilenamesFromZipfile(bloomPackName);

			// +1 for collection-level css file, -1 for thumbs file, so the count is right
			Assert.That(actualFiles.Count, Is.EqualTo(Directory.GetFiles(srcBookPath).Count()));

			foreach (var filePath in actualFiles)
			{
				Assert.IsFalse(Equals(Path.GetFileName(filePath), excludedFile));
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
	}
}
