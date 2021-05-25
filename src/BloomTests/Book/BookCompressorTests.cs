using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;
using ICSharpCode.SharpZipLib.Zip;

namespace BloomTests.Book
{
	class BookCompressorTests
	{
		private const string kMinimumValidBookHtml =
			@"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css'></link></head><body>
					<div class='bloom-page' id='guid1'></div>
			</body></html>";

		private TemporaryFolder _testFolder;
		private TemporaryFolder _bookFolder;

		private string GetTestFolderName() => "BookCompressorTests";

		[SetUp]
		public void Setup()
		{
			_testFolder = new TemporaryFolder(GetTestFolderName());			
		}

		[TearDown]
		public void TearDown()
		{
			if (_testFolder != null)
			{
				_testFolder.Dispose();
				_testFolder = null;
			}
		}

		private void SetupDirectoryWithHtml(string bookHtml,
			Action<string> actionsOnFolderBeforeCompressing = null)
		{
			_bookFolder = new TemporaryFolder(_testFolder, "book");
			File.WriteAllText(Path.Combine(_bookFolder.Path, "book.htm"), bookHtml);

			actionsOnFolderBeforeCompressing?.Invoke(_bookFolder.Path);
		}

		[Test]
		public void CompressDirectory_CompressBookWithExtraHtmlFiles_ExtrasAreIgnored()
		{
			// Seeing w:stdPr (part of urn:schemas-microsoft-com:office:word) would cause XmlHtmlConvert.GetXmlDomFromHtml to crash
			string htmContents = "<html><body><w:sdtPr></w:sdtPr></body></html>";

			// Setup //
			SetupDirectoryWithHtml(kMinimumValidBookHtml,
				actionsOnFolderBeforeCompressing: folderPath =>
				{
					// We expect this file to just be passed through, but not parsed
					string extraHtmDir = Path.Combine(folderPath, "unnecessaryExtraFiles");
					Directory.CreateDirectory(extraHtmDir);
					File.WriteAllText(Path.Combine(extraHtmDir, "causesException.htm"), htmContents);
				});

			if (_bookFolder == null)
			{
				Assert.Fail("Test setup error: Test setup failed to initialize bookFolder.");
			}

			// System Under Test //
			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder("BookCompressorBloomd" + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookDirectory(bloomdTempFile.Path, _bookFolder.Path, "");
			}

			// Verification //
			// Just make sure it didn't crash
			return;
		}

		[Test]
		public void CompressDirectory_CompressBooksWithBackgroundAudioFiles()
		{
			// Setup //
			var bookHtml = @"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css'></link></head><body>
					<div class='bloom-page' id='guid1' data-backgroundaudio='musicfile1.mp3'></div>
					<div class='bloom-page' id='guid2' data-backgroundaudio='musicfile2.ogg'></div>
					<div class='bloom-page' id='guid3' data-backgroundaudio='musicfile3.wav'></div>
			</body></html>";
			SetupDirectoryWithHtml(bookHtml,
				actionsOnFolderBeforeCompressing: folderPath =>
				{
					// We expect this file to just be passed through, but not parsed
					string audioDir = Path.Combine(folderPath, "audio");
					Directory.CreateDirectory(audioDir);
					File.WriteAllText(Path.Combine(audioDir, "musicfile1.mp3"), "dummy mp3 content");
					File.WriteAllText(Path.Combine(audioDir, "musicfile2.ogg"), "dummy ogg content");
					File.WriteAllText(Path.Combine(audioDir, "musicfile3.wav"), "dummy wav content");
					File.WriteAllText(Path.Combine(audioDir, "narration.mp3"), "more dummy mp3 content");	// file should be included (even though not referenced)
					File.WriteAllText(Path.Combine(audioDir, "narration.wav"), "more dummy wav content");	// file should not be included
				});

			// System Under Test //
			using (var bloomdTempFile = TempFile.WithFilenameInTempFolder("BookCompressorWithAudio" + BookCompressor.ExtensionForDeviceBloomBook))
			{
				BookCompressor.CompressBookDirectory(bloomdTempFile.Path, _bookFolder.Path, "");
				// Test by looking at the temp file content.
				using (var zippedFile = new ZipFile(bloomdTempFile.Path))
				{
					var count = 0;
					foreach (ZipEntry zipEntry in zippedFile)
					{
						++count;
						//Console.Out.WriteLine($"DEBUG: name={zipEntry.Name}, IsFile={zipEntry.IsFile}");
						Assert.Contains(zipEntry.Name, new[] { "book/book.htm",
							"book/audio/musicfile1.mp3", "book/audio/musicfile2.ogg", "book/audio/musicfile3.wav", "book/audio/narration.mp3" });
					}
					Assert.AreEqual(5, count, "Should be five files stored in test .bloomd file");
				}
			}
		}
	}
}
