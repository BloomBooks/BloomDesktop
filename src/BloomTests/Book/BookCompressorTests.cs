using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.Book;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Book
{
	class BookCompressorTests//: BookTestsBase
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
	}
}
