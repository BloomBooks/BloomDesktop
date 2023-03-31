using System;
using System.IO;
using Bloom.Book;
using Bloom.Publish.BloomPub;
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
			using (var bloomPubTempFile = TempFile.WithFilenameInTempFolder("BookCompressorBloomPub" + BloomPubMaker.BloomPubExtensionWithDot))
			{
				BookCompressor.CompressBookDirectory(bloomPubTempFile.Path, _bookFolder.Path, BloomPubMaker.MakeFilter(_bookFolder.Path), "");
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
					<div class='bloom-page' id='guid3' data-backgroundaudio='musicfile3.wav'>
						<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 44px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" data-audiorecordingmode=""Sentence"">
                                <p><span id=""narration"" class=""audio-sentence"" recordingmd5=""5b5efdab7f705554614a6383ae6d9469"" data-duration=""3.004082"">This is some akl data</span></p>
                            </div>
							<div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" style=""min-height: 44px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" lang=""es"" contenteditable=""true"" data-languagetipcontent=""Spanish"" data-audiorecordingmode=""Sentence"">
                                <p><span id=""i1083a390-c1ef-41d2-a55d-815eacb5c08d"" class=""audio-sentence"" recordingmd5=""5b5efdab7f705554614a6383ae6d9469"" data-duration=""3.004082"">This is Spanish</span></p>
                            </div>
						</div>
					</div>
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
					File.WriteAllText(Path.Combine(audioDir, "narration.mp3"), "more dummy mp3 content");   // file should be included (since it is referenced)
					File.WriteAllText(Path.Combine(audioDir, "narration.wav"), "more dummy wav content");   // file should not be included (only mp3 narration)
					File.WriteAllText(Path.Combine(folderPath, "temp.tmp"), "dummy temporary file data");   // file should not be included
				});

			// System Under Test //
			using (var bloomPubTempFile = TempFile.WithFilenameInTempFolder("BookCompressorWithAudio" + BloomPubMaker.BloomPubExtensionWithDot))
			{
				BookCompressor.CompressBookDirectory(bloomPubTempFile.Path, _bookFolder.Path, BloomPubMaker.MakeFilter(_bookFolder.Path), "");
				// Test by looking at the temp file content.
				using (var zippedFile = new ZipFile(bloomPubTempFile.Path))
				{
					var count = 0;
					foreach (ZipEntry zipEntry in zippedFile)
					{
						++count;
						//Console.Out.WriteLine($"DEBUG: name={zipEntry.Name}, IsFile={zipEntry.IsFile}");
						Assert.Contains(zipEntry.Name, new[] { "book/book.htm",
							"book/audio/musicfile1.mp3", "book/audio/musicfile2.ogg", "book/audio/musicfile3.wav", "book/audio/narration.mp3" });
					}
					Assert.AreEqual(5, count, "Should be five files stored in test .bloompub file");
				}
			}
		}

		[Test]
		public void CompressDirectory_CompressBooksWithExtraFiles()
		{
			// Setup //
			var bookHtml = @"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css'></link></head><body>
					<div class='bloom-page' id='guid1'></div>
					<div class='bloom-page' id='guid2' data-backgroundaudio='musicfile.ogg'></div>
					<div class='bloom-page' id='guid3'>
						<div class=""bloom-videoContainer bloom-noVideoSelected bloom-leadingElement bloom-selected"">
	                            <video>
	                            <source src=""video/signlanguageVid.mp4""></source></video>
	                        </div>
					</div>
			</body></html>";
			SetupDirectoryWithHtml(bookHtml,
				actionsOnFolderBeforeCompressing: folderPath =>
				{
					// Extra top-level book files should get ignored (non-whitelisted extensions).
					File.WriteAllText(Path.Combine(folderPath, "temp.tmp"), "dummy temporary file data");
					File.WriteAllText(Path.Combine(folderPath, "temp.xyz"), "dummy temporary file data");
					// Valid entries should get through.
					File.WriteAllText(Path.Combine(folderPath, "meta.json"), "dummy meta.json file");
					File.WriteAllText(Path.Combine(folderPath, "thumbnail.png"), "dummy thumbnail file");

					string audioDir = Path.Combine(folderPath, "audio");
					Directory.CreateDirectory(audioDir);
					File.WriteAllText(Path.Combine(audioDir, "musicfile.ogg"), "dummy ogg content");
					// These 2 should not be included (non-whitelisted extensions)
					File.WriteAllText(Path.Combine(audioDir, "midiMusic.mid"), "dummy midi file data");
					File.WriteAllText(Path.Combine(audioDir, "other.aif"), "dummy audio file data");
					// This subsubfolder should be completely ignored (only 'activities' can have
					// subsubfolders).
					string subAudioDir = Path.Combine(audioDir, "audio2");
					Directory.CreateDirectory(subAudioDir);
					File.WriteAllText(Path.Combine(subAudioDir, "other.mp3"), "dummy audio file data");

					string videoDir = Path.Combine(folderPath, "video");
					Directory.CreateDirectory(videoDir);
					File.WriteAllText(Path.Combine(videoDir, "signlanguageVid.mp4"), "dummy video content");
					// These 2 should not be included (non-whitelisted extensions)
					File.WriteAllText(Path.Combine(videoDir, "sign.mov"), "dummy movie file data");
					File.WriteAllText(Path.Combine(videoDir, "sign.wmv"), "dummy movie file data");
					// This subsubfolder should be completely ignored (only 'activities' can have
					// subsubfolders).
					string subVideoDir = Path.Combine(videoDir, "video2");
					Directory.CreateDirectory(subVideoDir);
					File.WriteAllText(Path.Combine(subVideoDir, "other.mp4"), "dummy video file data");

					// We don't know all the file types that might be in a widget,
					// so we expect to pass through every file and folder within the 'activities' folder.
					string activityDir = Path.Combine(folderPath, "activities");
					Directory.CreateDirectory(activityDir);
					string widgetDir = Path.Combine(activityDir, "My Widget");
					Directory.CreateDirectory(widgetDir);
					File.WriteAllText(Path.Combine(widgetDir, "odd.html"), "dummy html content");
					File.WriteAllText(Path.Combine(widgetDir, "strange.js"), "dummy js content");
					var widgetResources = Path.Combine(widgetDir, "Resources");
					Directory.CreateDirectory(widgetResources);
					File.WriteAllText(Path.Combine(widgetResources, "includable.blob"),
						"some unknown blob-type file contents");

					// We expect this folder to be excluded completely (non-whitelisted book subfolder).
					string bookWithinABookDir = Path.Combine(folderPath, "Book Within A Book");
					Directory.CreateDirectory(bookWithinABookDir);
					File.WriteAllText(Path.Combine(bookWithinABookDir, "extra book.html"), "dummy html content");
				});

			// System Under Test //
			using (var bloomPubTempFile = TempFile.WithFilenameInTempFolder("BookCompressorWithExtraFiles" + BloomPubMaker.BloomPubExtensionWithDot))
			{
				BookCompressor.CompressBookDirectory(bloomPubTempFile.Path, _bookFolder.Path, BloomPubMaker.MakeFilter(_bookFolder.Path), "");
				// Test by looking at the temp file content.
				using (var zippedFile = new ZipFile(bloomPubTempFile.Path))
				{
					var count = 0;
					foreach (ZipEntry zipEntry in zippedFile)
					{
						++count;
						//Console.Out.WriteLine($"DEBUG: name={zipEntry.Name}, IsFile={zipEntry.IsFile}");
						Assert.Contains(zipEntry.Name, new[] { "book/book.htm", "book/meta.json",
							"book/thumbnail.png", "book/audio/musicfile.ogg", "book/video/signlanguageVid.mp4",
							"book/activities/My Widget/odd.html", "book/activities/My Widget/strange.js",
							"book/activities/My Widget/Resources/includable.blob" });
					}
					Assert.AreEqual(8, count, "Should be eight files stored in test .bloompub file");
				}
			}
		}
	}
}
