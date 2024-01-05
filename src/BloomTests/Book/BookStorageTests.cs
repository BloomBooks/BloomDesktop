using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Edit;
using Moq;
using NUnit.Framework;
using SIL.Extensions;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.TestUtilities;

namespace BloomTests.Book
{
    [TestFixture]
    public class BookStorageTests
    {
        private FileLocator _fileLocator;
        private TemporaryFolder _fixtureFolder;
        private TemporaryFolder _folder;
        private string _bookPath;
        private List<TemporaryFolder> _thingsToDispose;

        [SetUp]
        public void Setup()
        {
            ErrorReport.IsOkToInteractWithUser = false;
            _fileLocator = new FileLocator(
                new string[]
                {
                    //FileLocationUtilities.GetDirectoryDistributedWithApplication( "factoryCollections"),
                    BloomFileLocator.GetFactoryBookTemplateDirectory("Basic Book"),
                    BloomFileLocator.GetFactoryXMatterDirectory()
                }
            );
            _fixtureFolder = new TemporaryFolder("BloomBookStorageTest");
            _folder = new TemporaryFolder(_fixtureFolder, "theBook");

            _bookPath = _folder.Combine("theBook.htm");
            _thingsToDispose = new List<TemporaryFolder>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var folder in _thingsToDispose)
            {
                folder.Dispose();
            }
            _thingsToDispose = null;

            _fixtureFolder.Dispose();
        }

        [Test]
        public void MoveBookToSafeName_NameGood_DoesNothing()
        {
            var oldName = "Some nice book";
            var bookFolder = Path.Combine(_fixtureFolder.Path, oldName);
            Directory.CreateDirectory(bookFolder);
            var bookPath = Path.Combine(bookFolder, oldName + ".htm");
            File.WriteAllText(bookPath, "this is a test");

            Assert.That(BookStorage.MoveBookToSafeName(bookFolder), Is.EqualTo(bookFolder));
        }

        [Test]
        public void MoveBookToSafeName_NameNotNFC_Moves()
        {
            var oldName = "Some nice\x0301 book";
            var bookFolder = Path.Combine(_fixtureFolder.Path, oldName);
            Directory.CreateDirectory(bookFolder);
            var bookPath = Path.Combine(bookFolder, oldName + ".htm");
            File.WriteAllText(bookPath, "this is a test");

            var expectedFolder = bookFolder.Replace("e\x0301", "\x00e9");

            Assert.That(BookStorage.MoveBookToSafeName(bookFolder), Is.EqualTo(expectedFolder));
            Assert.That(Directory.Exists(expectedFolder));
            var expectedBookPath = bookPath.Replace("e\x0301", "\x00e9");
            Assert.That(File.ReadAllText(expectedBookPath), Is.EqualTo("this is a test"));
        }

        [Test]
        public void MoveBookToSafeName_NameNotNFC_NewNameConflicts_Moves()
        {
            var oldName = "Some nice\x0301 book";
            var bookFolder = Path.Combine(_fixtureFolder.Path, oldName);
            Directory.CreateDirectory(bookFolder);
            var bookPath = Path.Combine(bookFolder, oldName + ".htm");
            File.WriteAllText(bookPath, "this is a test");

            var desiredFolder = bookFolder.Replace("e\x0301", "\x00e9");
            Directory.CreateDirectory(desiredFolder);
            var expectedFolder = desiredFolder + " 2";

            Assert.That(BookStorage.MoveBookToSafeName(bookFolder), Is.EqualTo(expectedFolder));
            Assert.That(Directory.Exists(expectedFolder));
            var expectedBookPath = Path.Combine(
                expectedFolder,
                Path.GetFileName(expectedFolder) + ".htm"
            );
            Assert.That(File.ReadAllText(expectedBookPath), Is.EqualTo("this is a test"));
        }

        [Test]
        public void Save_BookHadOnlyPaperSizeStyleSheet_StillHasIt()
        {
            GetInitialStorageWithCustomHtml(
                "<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
            );
            AssertThatXmlIn
                .HtmlFile(_bookPath)
                .HasSpecifiedNumberOfMatchesForXpath("//link[contains(@href, 'Basic Book')]", 1);
        }

        [Test]
        public void Save_HasEmptyParagraphs_RetainsEmptyParagraphs()
        {
            var pattern = "<p></p><p></p><p>a</p><p></p><p>b</p>";
            GetInitialStorageWithCustomHtml(
                "<html><body><div class='bloom-page'><div class='bloom-translationGroup'><div class='bloom-editable'>"
                    + pattern
                    + "</div></div></div></body></html>"
            );
            AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//p", 5);
        }

        // Review JohnH: We no longer migrate to Themes except when creating a new book.
        // So just initializing a storage won't migrate the HTML to use Themes if the HTML doesn't
        // already have the relevant links.
        // I added tests that book starter creates a book with the right theme (depending on customStyles),
        // but we may need another on Book to verify that BBUD inserts the right stylesheet links for
        // appearance.css (in default) and basePage-legacy-5-6.css (in legacy).
        //[Test]
        //public void Save_BookHadEditStyleSheet_NowHasPreviewAndBaseAndAppearance()
        //{
        //	GetInitialStorageWithCustomHtml("<html><head><link rel='stylesheet' href='editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>");
        //	AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[@href = 'editMode.css']", 0);
        //	AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[@href = 'basePage.css']", 1);
        //	AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[@href = 'previewMode.css']", 1);
        //	AssertThatXmlIn.HtmlFile(_bookPath).HasSpecifiedNumberOfMatchesForXpath("//link[@href = 'appearance.css']", 1);
        //}
        [Test]
        public void Save_BookHadNarrationAudioRecordedByWholeTextBox_AddsFeatureRequirementMetadata()
        {
            // Enhance: need an example in the future to test the result if two are generated. But right now this is the only feature that generates it.
            GetInitialStorageWithCustomHtml(
                "<html><head></head><body><div class='bloom-page'><div class='bloom-translationGroup'><div class='bloom-editable' data-audiorecordingmode='TextBox'></div></div></div></body></html>"
            );
            AssertThatXmlIn
                .HtmlFile(_bookPath)
                .HasSpecifiedNumberOfMatchesForXpath("//meta[@name='FeatureRequirement']", 1);

            // Note: No need to HTML-encode the XPath. The comparison will automatically figure that out (I guess by decoding the encoding version)
            string expectedContent =
                "[{\"BloomDesktopMinVersion\":\"4.4\",\"BloomReaderMinVersion\":\"1.0\",\"FeatureId\":\"wholeTextBoxAudio\",\"FeaturePhrase\":\"Whole Text Box Audio\"}]";
            AssertThatXmlIn
                .HtmlFile(_bookPath)
                .HasSpecifiedNumberOfMatchesForXpath($"//meta[@content='{expectedContent}']", 1);
        }

        [Test]
        public void Save_BookHadNoAudio_CleansUpFeatureRequirementMetadata()
        {
            // Enhance: need an example in the future to test the result if two are generated. But right now this is the only feature that generates it.
            GetInitialStorageWithCustomHtml(
                "<html><head><meta name='FeatureRequirement' content='[{&quot;BloomDesktopMinVersion&quot;:&quot;4.4&quot;,&quot;BloomReaderMinVersion&quot;:&quot;1.0&quot;,&quot;FeatureId&quot;:&quot;wholeTextBoxAudio&quot;,&quot;FeaturePhrase&quot;:&quot;Whole Text Box Audio&quot;}]'></meta></head><body><div class='bloom-page'><div class='bloom-translationGroup'><div class='bloom-editable'></div></div></div></body></html>"
            );
            AssertThatXmlIn
                .HtmlFile(_bookPath)
                .HasSpecifiedNumberOfMatchesForXpath("//meta[@name='FeatureRequirement']", 0);
        }

        [Test]
        public void CleanupUnusedVideoFiles_BookHadUnusedVideo_VideosRemoved()
        {
            const string usedVideoGuid = "f57db6b0-3bbe-42be-ab09-2a67b2e6f0d7"; //The files to keep.
            const string usedVideo2Guid = "3c4557eb-e644-4b48-82e0-443fcca62a6b"; //The files to keep.
            const string unusedVideoGuid = "746b30ce-2fc7-4c84-9ef0-1036d6883cba"; //The files to drop.
            const string usedVidMp4 = usedVideoGuid + ".mp4";
            const string usedVid2Mp4 = usedVideo2Guid + ".mp4";
            const string unusedVidMp4 = unusedVideoGuid + ".mp4";
            var videoPath = Path.Combine(_folder.Path, "video"); //Path to the video files.
            Directory.CreateDirectory(videoPath);
            var storage = GetInitialStorageWithCustomHtml(
                @"
		<html><body>
			<div class='bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual'
				data-page='' id='566c4a7a-0789-43f5-abcb-e4a16532dedd' data-page-number='1' lang=''>
				<div class='marginBox'>
					<div>
						<div class='bloom-videoContainer bloom-leadingElement bloom-selected'>
							<video controls='controls'>
								<source src='video/"
                    + usedVidMp4
                    + @"' type='video/mp4'></source>
							</video>
						</div>
					</div>
				</div>
			</div>
			<div class='bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual'
				data-page='' id='11623f8e-77c2-4718-a594-abd787a37458' data-page-number='2' lang=''>
				<div class='marginBox'>
					<div>
						<div class='bloom-videoContainer bloom-leadingElement bloom-selected'>
							<video controls='controls'>
								<source src='video/"
                    + usedVid2Mp4
                    + @"' type='video/mp4'></source>
							</video>
						</div>
					</div>
				</div>
			</div>
		</body></html>"
            );
            var usedOrigFilename = usedVideoGuid + ".orig";
            var usedVidMp4Path = MakeSampleMp4Video(Path.Combine(videoPath, usedVidMp4), true);
            var usedVid2Mp4Path = MakeSampleMp4Video(Path.Combine(videoPath, usedVid2Mp4), false);
            var uncreatedOrigFilename = usedVideo2Guid + ".orig";
            var usedVidOrigPath = Path.Combine(videoPath, usedOrigFilename);
            var uncreatedVidOrigPath = Path.Combine(videoPath, uncreatedOrigFilename);
            var unusedVidMp4Path = MakeSampleMp4Video(Path.Combine(videoPath, unusedVidMp4), true);
            var unusedVidOrigPath = unusedVideoGuid + ".orig";
            storage.CleanupUnusedVideoFiles();
            Assert.IsTrue(File.Exists(usedVidMp4Path.Path));
            Assert.IsTrue(File.Exists(usedVidOrigPath));
            Assert.IsTrue(File.Exists(usedVid2Mp4Path.Path));
            Assert.IsFalse(File.Exists(unusedVidMp4Path.Path));
            Assert.IsFalse(File.Exists(uncreatedVidOrigPath));
            Assert.IsFalse(File.Exists(unusedVidOrigPath));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CleanupUnusedAudioFiles_BookHadUnusedAudio_AudiosRemoved(bool isForPublish)
        {
            const string usedAudioGuid = "i3afb14d9-6362-40bf-9dca-de1b24d793f3"; //The files to keep.
            const string unusedAudioGuid = "d3afb14d9-6362-40bf-9dca-de1b24d793f3"; //The files to drop.
            const string potentiallyUsefulAudioGuid = "textBox1";
            const string usedSpanGuid1 = "span1";
            const string usedSpanGuid2 = "span2";
            const string usedFrontMatterAudioGuid = "usedFrontMatterAudioGuid"; // This file should be kept (even though it's not referenced in the normal section) because it's referenced in the bloomDataDiv instead
            const string usedFrontMatterBackgroundAudio = "usedFrontMatterBackgroundAudio"; // This file should be kept (even though it's not referenced in the normal section) because it's referenced in the bloomDataDiv instead
            const string usedBackgroundAudio = "Fur-elise-music-box"; //Background file to keep.
            const string unusedBackgroundAudio = "Eine-kleine-Nachtmusik";

            var audioPath = Path.Combine(_folder.Path, "audio"); //Path to the audio files.
            Directory.CreateDirectory(audioPath);

            var usedBgWavFilename = usedBackgroundAudio + ".wav";
            var usedBgMp3Filename = usedBackgroundAudio + ".mp3";
            var usedBgOggFilename = usedBackgroundAudio + ".ogg";
            var usedWavFilename = usedAudioGuid + ".wav";
            var usedMp3Filename = usedAudioGuid + ".mp3";
            var unusedWavFilename = unusedAudioGuid + ".wav";
            var unusedMp3Filename = unusedAudioGuid + ".mp3";
            var potentiallyUsefulWavFilename = potentiallyUsefulAudioGuid + ".wav";
            var potentiallyUsefulMp3Filename = potentiallyUsefulAudioGuid + ".mp3";
            var usedSpan1WavFilename = usedSpanGuid1 + ".wav";
            var usedSpan1Mp3Filename = usedSpanGuid1 + ".mp3";
            var usedSpan2WavFilename = usedSpanGuid2 + ".wav";
            var usedSpan2Mp3Filename = usedSpanGuid2 + ".mp3";
            var unusedBgWavFilename = unusedBackgroundAudio + ".wav";
            var unusedBgMp3Filename = unusedBackgroundAudio + ".mp3";
            var unusedBgOggFilename = unusedBackgroundAudio + ".ogg";
            var usedFrontMatterWavFilename = usedFrontMatterAudioGuid + ".wav";
            var usedFrontMatterMp3Filename = usedFrontMatterAudioGuid + ".mp3";
            var usedFrontMatterBgWavFilename = usedFrontMatterBackgroundAudio + ".wav";
            var usedFrontMatterBgMp3Filename = usedFrontMatterBackgroundAudio + ".mp3";

            var storage = GetInitialStorageWithCustomHtml(
                $"<html><body><div id='bloomDataDiv'><div data-backgroundaudio='{usedFrontMatterBgWavFilename}'</div>"
                    + $"<div data-book='bookTitle' lang='en'><p><span id='{usedFrontMatterAudioGuid}' class='audio-sentence'>Title</span></p></div></div>"
                    + "<div class='bloom-page numberedPage customPage bloom-combinedPage "
                    + "A5Portrait side-right bloom-monolingual' data-page='' "
                    + "id='ab5bf932-b9ea-432c-84e6-f37d58d2f632' data-pagelineage="
                    + "'adcd48df-e9ab-4a07-afd4-6a24d0398383' data-page-number='1' "
                    + $"lang='' data-backgroundaudio='{usedBgWavFilename}'>"
                    + "<div class='marginBox'>"
                    + $"<p><span data-duration='2.300227' id='{usedAudioGuid}' "
                    + "class='audio-sentence' recordingmd5='undefined'>Who are you?</span></p>"
                    + "</div>"
                    + $"<div class='bloom-editable' data-audiorecordingmode='TextBox' id='{potentiallyUsefulAudioGuid}'>"
                    + "<p>"
                    + $"<span id='{usedSpanGuid1}' class='audio-sentence'>Sentence 1</span>"
                    + $"<span id='{usedSpanGuid2}' class='audio-sentence'>Sentence 2</span>"
                    + "</p>"
                    + "</div>"
                    + "</div>"
                    + "<div class='bloom-page numberedPage customPage bloom-combinedPage "
                    + "A5Portrait side-right bloom-monolingual' data-page-number='2' "
                    + $"lang='' data-backgroundaudio='{usedBgMp3Filename}'>"
                    + "<div class='marginBox'>"
                    + "<p>I am me.</p>"
                    + "</div></div>"
                    + "<div class='bloom-page numberedPage customPage bloom-combinedPage "
                    + "A5Portrait side-right bloom-monolingual' data-page-number='2' "
                    + $"lang='' data-backgroundaudio='{usedBgOggFilename}'>"
                    + "<div class='marginBox'>"
                    + "<p>I am me.</p>"
                    + "</div></div>"
                    + "</body></html>"
            );

            // Note: These must be executed after GetInitialStorage
            var usedBGWavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, usedBgWavFilename),
                true,
                true
            ).Path;
            var usedBGMp3Path = Path.Combine(audioPath, usedBgMp3Filename);
            var usedBGOggPath = Path.Combine(audioPath, usedBgOggFilename);

            var unusedBGWavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, unusedBgWavFilename),
                true,
                true
            ).Path;
            var unusedBGMp3Path = Path.Combine(audioPath, unusedBgMp3Filename);
            var unusedBGOggPath = Path.Combine(audioPath, unusedBgOggFilename);

            var usedWavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, usedWavFilename),
                true,
                true
            ).Path;
            var usedMp3Path = Path.Combine(audioPath, usedMp3Filename);
            var unusedWavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, unusedWavFilename),
                true,
                true
            ).Path;
            var unusedMp3Path = Path.Combine(audioPath, unusedMp3Filename);

            var potentiallyUsefulWavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, potentiallyUsefulWavFilename),
                true,
                false
            ).Path;
            var potentiallyUsefulMp3Path = Path.Combine(audioPath, potentiallyUsefulMp3Filename);
            var usedSpan1WavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, usedSpan1WavFilename),
                true,
                false
            ).Path;
            var usedSpan1Mp3Path = Path.Combine(audioPath, usedSpan1Mp3Filename);
            var usedSpan2WavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, usedSpan2WavFilename),
                true,
                false
            ).Path;
            var usedSpan2Mp3Path = Path.Combine(audioPath, usedSpan2Mp3Filename);

            var usedFrontMatterWavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, usedFrontMatterWavFilename),
                true
            ).Path;
            var usedFrontMatterMp3Path = Path.Combine(audioPath, usedFrontMatterMp3Filename);
            var usedFrontMatterBGWavPath = MakeSampleWavAudio(
                Path.Combine(audioPath, usedFrontMatterBgWavFilename),
                true
            ).Path;
            var unusedFrontMatterBGMp3Path = Path.Combine(audioPath, usedFrontMatterBgMp3Filename); // Note: For Background music, we only need to keep around the one directly referenced. Other extensions should be removed.

            // Verify setup
            Assert.IsTrue(File.Exists(usedWavPath), "Pre: Audio Sentence WAV");
            Assert.IsTrue(File.Exists(usedMp3Path), "Pre: Audio Sentence MP3");
            Assert.IsTrue(File.Exists(unusedWavPath), "Pre: Unused Audio Sentence WAV");
            Assert.IsTrue(File.Exists(unusedMp3Path), "Pre: Unused Audio Sentence MP3");
            Assert.IsTrue(
                File.Exists(potentiallyUsefulWavPath),
                "Pre: Potentially Useful Audio (Text Box) WAV"
            );
            Assert.IsTrue(
                File.Exists(potentiallyUsefulMp3Path),
                "Pre: Potentially Useful Audio (Text Box) MP3"
            );
            Assert.IsTrue(File.Exists(usedSpan1WavPath), "Pre: Used Audio Sentence Span 1 WAV");
            Assert.IsTrue(File.Exists(usedSpan1Mp3Path), "Pre: Used Audio Sentence Span 1 MP3");
            Assert.IsTrue(File.Exists(usedSpan2WavPath), "Pre: Used Audio Sentence Span 2 WAV");
            Assert.IsTrue(File.Exists(usedSpan2Mp3Path), "Pre: Used Audio Sentence Span 2 MP3");
            Assert.IsTrue(File.Exists(usedBGWavPath), "Pre: Background Music WAV");
            Assert.IsTrue(File.Exists(usedBGMp3Path), "Pre: Background Music MP3");
            Assert.IsTrue(File.Exists(usedBGOggPath), "Pre: Background Music Ogg");
            Assert.IsTrue(File.Exists(unusedBGWavPath), "Pre: Unused Background Music WAV");
            Assert.IsTrue(File.Exists(unusedBGMp3Path), "Pre: Unused Background Music MP3");
            Assert.IsTrue(File.Exists(unusedBGOggPath), "Pre: Unused Background Music Ogg");
            Assert.IsTrue(
                File.Exists(usedFrontMatterWavPath),
                "Pre: Front Matter Audio Sentence WAV"
            );
            Assert.IsTrue(
                File.Exists(usedFrontMatterMp3Path),
                "Pre: Front Matter Audio Sentence MP3"
            );
            Assert.IsTrue(
                File.Exists(usedFrontMatterBGWavPath),
                "Pre: Front Matter Background Music WAV"
            );
            Assert.IsTrue(
                File.Exists(unusedFrontMatterBGMp3Path),
                "Pre: Front Matter Background Music MP3"
            );

            // SUT
            storage.CleanupUnusedAudioFiles(isForPublish);

            Assert.IsTrue(File.Exists(usedWavPath), "Audio Sentence WAV");
            Assert.IsTrue(File.Exists(usedMp3Path), "Audio Sentence MP3");
            Assert.IsFalse(File.Exists(unusedWavPath), "Unused Audio Sentence WAV");
            Assert.IsFalse(File.Exists(unusedMp3Path), "Unused Audio Sentence MP3");

            bool isPotentiallyUsefulFileExpectedToExist = !isForPublish;
            Assert.AreEqual(
                isPotentiallyUsefulFileExpectedToExist,
                File.Exists(potentiallyUsefulWavPath),
                "Potentially Useful Audio (Text Box) WAV"
            );
            Assert.AreEqual(
                isPotentiallyUsefulFileExpectedToExist,
                File.Exists(potentiallyUsefulMp3Path),
                "Potentially Useful Audio (Text Box) MP3"
            );
            Assert.IsTrue(File.Exists(usedSpan1WavPath), "Used Audio Sentence Span 1 WAV");
            Assert.IsTrue(File.Exists(usedSpan2Mp3Path), "Used Audio Sentence Span 2 MP3");
            Assert.IsTrue(File.Exists(usedSpan2WavPath), "Used Audio Sentence Span 2 WAV");
            Assert.IsTrue(File.Exists(usedSpan1Mp3Path), "Used Audio Sentence Span 1 MP3");

            Assert.IsTrue(File.Exists(usedBGWavPath), "Background Music WAV");
            Assert.IsTrue(File.Exists(usedBGMp3Path), "Background Music MP3");
            Assert.IsFalse(File.Exists(unusedBGWavPath), "Unused Background Music WAV");
            Assert.IsFalse(File.Exists(unusedBGMp3Path), "Unused Background Music MP3");
            Assert.IsTrue(File.Exists(usedFrontMatterWavPath), "Front Matter Audio Sentence WAV");
            Assert.IsTrue(File.Exists(usedFrontMatterMp3Path), "Front Matter Audio Sentence MP3");
            Assert.IsTrue(
                File.Exists(usedFrontMatterBGWavPath),
                "Front Matter Background Music WAV"
            );
            Assert.IsFalse(
                File.Exists(unusedFrontMatterBGMp3Path),
                "Front Matter Background Music MP3"
            );
            Assert.IsTrue(File.Exists(usedBGOggPath), "Background Music Ogg");
            Assert.IsFalse(File.Exists(unusedBGOggPath), "Unused Background Music Ogg");
        }

        [Test]
        // Regression test that verifies that the function does not mistakenly preserve the contents of a data div rather than the value of a data div's data-backgroundaudio attribute.
        public void CleanupUnusedAudioFiles_BloomDataDivWithMalformedFrontMatterHTML_AudiosRemoved()
        {
            // Test setup //

            var audioPath = Path.Combine(_folder.Path, "audio"); //Path to the audio files.
            Directory.CreateDirectory(audioPath);

            // This HTML is messed up and contains data-backgroundaudio as the inner text instead of as an attribute
            var storage = GetInitialStorageWithCustomHtml(
                "<html><body><div id='bloomDataDiv'><div>data-backgroundaudio</div><div>audio-sentence</div><span>audio-sentence</span></div><div class='bloom-page numberedPage customPage bloom-combinedPage "
                    + "A5Portrait side-right bloom-monolingual' data-page='' "
                    + "id='ab5bf932-b9ea-432c-84e6-f37d58d2f632' data-pagelineage="
                    + "'adcd48df-e9ab-4a07-afd4-6a24d0398383' data-page-number='1' "
                    + "lang=''><div class='marginBox'>"
                    + "<p>Who are you?</p>"
                    + "</div></div></body></html>"
            );

            string[] unneededFilenames = { "audio-sentence", "data-backgroundaudio" };
            var expectedPathsToBeRemoved = new List<string>();

            foreach (var filename in unneededFilenames)
            {
                string wavFilename = $"{filename}.wav";
                var wavPath = MakeSampleWavAudio(Path.Combine(audioPath, wavFilename), true).Path;
                expectedPathsToBeRemoved.Add(wavPath);

                string mp3Filename = $"{filename}.mp3";
                var mp3Path = Path.Combine(audioPath, mp3Filename);
                expectedPathsToBeRemoved.Add(mp3Path);
            }

            // Test exercise //
            storage.CleanupUnusedAudioFiles(isForPublish: true);

            // Test verification //
            // The files should no longer exist because they are not correctly referenced in the bloomDataDiv
            foreach (var unneededPath in expectedPathsToBeRemoved)
            {
                Assert.IsFalse(File.Exists(unneededPath), $"File path: {unneededPath}");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CleanupUnusedAudioFiles_TimingFile_DeleteOrPreservedAppropriately(
            bool isForPublish
        )
        {
            // Test setup //
            var audioPath = Path.Combine(_folder.Path, "audio"); //Path to the audio files.
            Directory.CreateDirectory(audioPath);

            var storage = GetInitialStorageWithCustomHtml(
                "<html><body><div class='bloom-page><div class='bloom-editable' id='textBox1'>"
                    + "<p><span class='audio-sentence' id='span1'>Sentence 1</span></p>"
                    + "</div></div></body></html>"
            );

            string timingsFilepath = Path.Combine(audioPath, "span1_timings.tsv");
            TempFile.WithFilename(timingsFilepath);

            // Test exercise //
            storage.CleanupUnusedAudioFiles(isForPublish);

            // Test verification //
            Assert.AreEqual(!isForPublish, File.Exists(timingsFilepath));
        }

        private (string indexPath, string cssPath) makeDummyActivity(
            string activityRootPath,
            string activityName
        )
        {
            var myActivityPath = Path.Combine(activityRootPath, activityName);
            var indexPath = Path.Combine(myActivityPath, "index.htm");
            Directory.CreateDirectory(myActivityPath);
            File.WriteAllText(indexPath, @"nonsense for testing");
            var cssPath = Path.Combine(myActivityPath, "rubbish.css");
            File.WriteAllText(cssPath, "rubbish css");

            return (indexPath, cssPath);
        }

        [Test]
        public void CleanupUnusedActivities_BookHadUsedAndUnusedActivities_OnlyUnusedOnesRemoved()
        {
            var activityPath = BookStorage.GetActivityFolderPath(_folder.Path);
            Directory.CreateDirectory(activityPath);

            var ball = makeDummyActivity(activityPath, "ball #1"); // includes a space and punc to test URL decoding.
            var unused = makeDummyActivity(activityPath, "unused");

            // Verify setup
            Assert.IsTrue(File.Exists(ball.indexPath));
            Assert.IsTrue(File.Exists(ball.cssPath));
            Assert.IsTrue(File.Exists(unused.indexPath));
            Assert.IsTrue(File.Exists(unused.cssPath));

            var storage = GetInitialStorageWithCustomHtml(
                $"<html><body><div class='bloom-page'><div class='bloom-widgetContainer'>"
                    + "    <iframe src='activities/ball%20%231/index.htm'/>"
                    + $"</div></div>"
                    + "</body></html>"
            );

            // I make this call here as a reminder that it's the function the test is about,
            // and so the test will survive if we should happen to remove the call to it
            // during GetInitialStorageWithCustomHtml(). But actually the redundant files
            // have already been removed.
            storage.CleanupUnusedActivities();

            Assert.IsTrue(File.Exists(ball.indexPath), "ballIndex should be preserved");
            Assert.IsTrue(File.Exists(ball.cssPath), "ball CSS should be preserved");
            Assert.IsFalse(
                File.Exists(unused.indexPath),
                "unused index file should've been removed"
            );
            Assert.IsFalse(File.Exists(unused.cssPath), "unused CSS file  should've been removed");
        }

        [Test]
        public void CleanupUnusedImageFiles_BookHadUnusedImages_ImagesRemoved()
        {
            var storage = GetInitialStorageWithCustomHtml(
                "<html><body><div class='bloom-page'><div class='marginBox'>"
                    + "<div style='background-image:url(\"keepme.png\")'></div>"
                    + "<img src='keepme2.png'></img>"
                    + "</div></div></body></html>"
            );
            var keepName =
                Environment.OSVersion.Platform == PlatformID.Win32NT ? "KeEpMe.pNg" : "keepme.png";
            var keepNameImg =
                Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? "KeEpMe2.pNg"
                    : "keepme2.png";
            var keepTempDiv = MakeSamplePngImage(Path.Combine(_folder.Path, keepName));
            var keepTempImg = MakeSamplePngImage(Path.Combine(_folder.Path, keepNameImg));
            var dropmeTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "dropme.png"));
            storage.CleanupUnusedImageFiles();
            Assert.IsTrue(File.Exists(keepTempDiv.Path));
            Assert.IsTrue(File.Exists(keepTempImg.Path));
            Assert.IsFalse(File.Exists(dropmeTemp.Path));
        }

        [Test]
        public void CleanupUnusedImageFiles_ImageHasQuery_ImagesNotRemoved()
        {
            var storage = GetInitialStorageWithCustomHtml(
                "<html><body><div class='bloom-page'><div class='marginBox'>"
                    + "<img src='keepme.png?1234'></img>"
                    + "</div></div></body></html>"
            );
            var keepTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "keepme.png"));
            storage.CleanupUnusedImageFiles();
            Assert.IsTrue(File.Exists(keepTemp.Path));
        }

        [Test]
        public void CleanupUnusedImageFiles_ImageOnlyReferencedInDataDiv_ImageNotRemoved()
        {
            var storage = GetInitialStorageWithCustomHtml(
                "<html><body>"
                    + "<div id ='bloomDataDiv'><div data-book='coverImage'>keepme.png</div>"
                    + "<div data-book='coverImage'> keepme.jpg </div></div>"
                    + "<div class='bloom-page'><div class='marginBox'>"
                    + "</div></div></body></html>"
            );
            var keepTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "keepme.png"));
            var keepTempJPG = MakeSamplePngImage(Path.Combine(_folder.Path, "keepme.jpg"));
            storage.CleanupUnusedImageFiles();
            Assert.IsTrue(File.Exists(keepTemp.Path));
            Assert.IsTrue(File.Exists(keepTempJPG.Path));
        }

        [Test]
        public void CleanupUnusedImageFiles_ThumbnailsAndPlaceholdersNotRemoved()
        {
            var storage = GetInitialStorageWithCustomHtml(
                "<html><body><div class='bloom-page'><div class='marginBox'>"
                    + "</div></div></body></html>"
            );
            var p1 = MakeSamplePngImage(Path.Combine(_folder.Path, "thumbnail.png"));
            var p2 = MakeSamplePngImage(Path.Combine(_folder.Path, "thumbnail88.png"));
            var p3 = MakeSamplePngImage(Path.Combine(_folder.Path, "placeholder.png"));
            var dropmeTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "dropme.png"));
            storage.CleanupUnusedImageFiles();
            Assert.IsTrue(File.Exists(p1.Path));
            Assert.IsTrue(File.Exists(p2.Path));
            Assert.IsTrue(File.Exists(p3.Path));
            Assert.IsFalse(File.Exists(dropmeTemp.Path));
        }

        [Test]
        public void CleanupUnusedImageFiles_UnusedImageIsLocked_NotException()
        {
            var storage = GetInitialStorageWithCustomHtml(
                "<html><body><div class='bloom-page'><div class='marginBox'></div></body></html>"
            );
            var dropmeTemp = MakeSamplePngImage(Path.Combine(_folder.Path, "dropme.png"));
            //make it undelete-able
            using (Image.FromFile(dropmeTemp.Path))
            {
                storage.CleanupUnusedImageFiles();
            }
        }

        [Test]
        public void ValidateBook_ReportsInvalidHtml()
        {
            // BL-6273 Hand-edited Html could pass ValidateBook, which led to improper handling of the resulting error.
            // (ValidateBook is where we determine whether to try and use the .bak file instead, or not.)
            var storage = GetInitialStorageWithCustomHtml(
                "<html><body><div class='bloom-page' id='someId'><div class='marginBox'><div class='bloom-translationGroup'>"
                    + "<div class='bloom-editable'>"
                    + "</div></div>"
                    + // not enough closing tags due to "hand-editing"
                    "<div class='bloom-page' id='someOtherId'><div class='marginBox'><div class='bloom-translationGroup'>"
                    + "</div></div></div>"
                    + "</body></html>",
                false
            );
            var result = storage.ValidateBook(storage.PathToExistingHtml);
            Assert.IsTrue(
                result.StartsWith("Bloom-page element not found at root level: someOtherId"),
                "Bad Html should fail ValidateBook()."
            );
            Assert.IsTrue(storage.ErrorAllowsReporting, "ErrorAllowsReporting");
        }

        [Test]
        public void ValidateBook_ReportsNewerVersionRequiredSingle()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"<html>
	<head>
		<meta name=""FeatureRequirement"" content=""[{&quot;BloomDesktopMinVersion&quot;:&quot;999999999.8&quot;,&quot;BloomReaderMinVersion&quot;:&quot;1.0&quot;,&quot;FeatureId&quot;:&quot;wholeTextBoxAudio&quot;,&quot;FeaturePhrase&quot;:&quot;Breaking Feature 1&quot;}]""></meta>
	</head>
	<body>
		<div class='bloom-page' id='someOtherId'><div class='marginBox'><div class='bloom-translationGroup'>
		</div></div></div>
	</body>
</html>"
            );

            storage.GetHtmlMessageIfFeatureIncompatibility();
            Assert.That(
                storage.ErrorMessagesHtml.Contains("or greater because it uses the feature"),
                Is.True,
                "ErrorMessagesHtml"
            );
            Assert.That(storage.ErrorAllowsReporting, Is.False, "ErrorAllowsReporting");
        }

        [Test]
        public void ValidateBook_ReportsNewerVersionRequiredMultiple()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"<html>
	<head>
		<meta name=""FeatureRequirement"" content=""[{&quot;BloomDesktopMinVersion&quot;:&quot;999999999.8&quot;,&quot;BloomReaderMinVersion&quot;:&quot;1.0&quot;,&quot;FeatureId&quot;:&quot;wholeTextBoxAudio&quot;,&quot;FeaturePhrase&quot;:&quot;Breaking Feature 1&quot;},{&quot;BloomDesktopMinVersion&quot;:&quot;999999999.10&quot;,&quot;BloomReaderMinVersion&quot;:&quot;1.0&quot;,&quot;FeatureId&quot;:&quot;customSpanAudio&quot;,&quot;FeaturePhrase&quot;:&quot;Breaking Feature 2&quot;}]""></meta>
	</head>
	<body>
		<div class='bloom-page' id='someOtherId'><div class='marginBox'><div class='bloom-translationGroup'>
		</div></div></div>
	</body>
</html>"
            );

            storage.GetHtmlMessageIfFeatureIncompatibility();
            Assert.That(
                storage.ErrorMessagesHtml.Contains(
                    "or greater because it uses the following features"
                ),
                Is.True,
                "ErrorMessagesHtml"
            );
            Assert.That(storage.ErrorAllowsReporting, Is.False, "ErrorAllowsReporting");
            Assert.That(
                storage.ErrorMessagesHtml.IndexOf("Breaking Feature 1"),
                Is.GreaterThan(storage.ErrorMessagesHtml.IndexOf("Breaking Feature 2")),
                "sort order wrong"
            );
        }

        [Test]
        public void Save_BookHasMissingImages_NoCrash()
        {
            var storage = GetInitialStorageWithCustomHtml(
                "<html><body><div class='bloom-page'><div class='marginBox'><img src='keepme.png'></img></div></div></body></html>"
            );
            storage.Save();
        }

        private TempFile MakeSamplePngImage(string name)
        {
            var temp = TempFile.WithFilename(name);
            var x = new Bitmap(10, 10);
            x.Save(temp.Path, ImageFormat.Png);
            x.Dispose();
            return temp;
        }

        internal static void MakeSampleFiles(
            string folderPath,
            string filenameWithoutExtension,
            params string[] extensions
        )
        {
            Directory.CreateDirectory(folderPath);

            foreach (string extension in extensions)
            {
                string filename;
                if (extension.StartsWith("."))
                    filename = $"{filenameWithoutExtension}{extension}";
                else
                    filename = $"{filenameWithoutExtension}.{extension}";

                TempFile.WithFilename(Path.Combine(folderPath, filename));
            }
        }

        internal static void MakeSampleAudioFiles(
            string bookfolderPath,
            string filenameWithoutExtension,
            params string[] extensions
        )
        {
            var audioPath = Path.Combine(bookfolderPath, "audio");
            MakeSampleFiles(audioPath, filenameWithoutExtension, extensions);
        }

        internal static void MakeSampleVideoFiles(
            string bookfolderPath,
            string mp4Filename,
            bool makeOrigAlso = false
        )
        {
            Assert.That(
                Path.GetExtension(mp4Filename),
                Is.EqualTo(".mp4"),
                "Extension of video file should be '.mp4'"
            );
            var videoPath = Path.Combine(bookfolderPath, "video", mp4Filename);
            MakeSampleMp4Video(videoPath, makeOrigAlso);
        }

        private TempFile MakeSampleWavAudio(
            string name,
            bool makeMp3Also = false,
            bool makeOggAlso = false
        )
        {
            var temp = TempFile.WithFilename(name);
            var ext = Path.GetExtension(name);
            if (makeMp3Also && (ext == ".wav"))
            {
                TempFile.WithFilename(Path.ChangeExtension(name, ".mp3"));
            }
            if (makeOggAlso)
                TempFile.WithFilename(Path.ChangeExtension(name, ".ogg"));
            return temp;
        }

        private static TempFile MakeSampleMp4Video(string name, bool makeOrigAlso = false)
        {
            var temp = TempFile.WithFilename(name);
            var ext = Path.GetExtension(name);
            if (makeOrigAlso && ext == ".mp4")
            {
                TempFile.WithFilename(Path.ChangeExtension(name, ".orig"));
            }
            return temp;
        }

        [Test]
        [Platform(
            Exclude = "Linux",
            Reason = "UNC paths for network drives are only used on Windows"
        )]
        public void Save_PathIsUNCRatherThanDriveLetter()
        {
            var storage = GetInitialStorageUsingUNCPath();
            storage.Save();
        }

        private BookStorage GetInitialStorageUsingUNCPath()
        {
            var testFolder = new TemporaryFolder(_fixtureFolder.Path);
            var bookPath = testFolder.Combine("theBook.htm");
            File.WriteAllText(
                bookPath,
                "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
            );
            var collectionSettings = new CollectionSettings(
                Path.Combine(testFolder.Path, "test.bloomCollection")
            );
            var folderPath = ConvertToNetworkPath(testFolder.Path);
            Debug.WriteLine(Path.GetPathRoot(folderPath));
            var storage = new BookStorage(
                folderPath,
                _fileLocator,
                new BookRenamedEvent(),
                collectionSettings
            );
            return storage;
        }

        private string ConvertToNetworkPath(string drivePath)
        {
            string driveLetter = Directory.GetDirectoryRoot(drivePath);
            return drivePath.Replace(
                driveLetter,
                "//localhost/" + driveLetter.Replace(":\\", "") + "$/"
            );
        }

        private BookStorage GetInitialStorageWithCustomHtml(string html, bool doSave = true)
        {
            RobustFile.WriteAllText(_bookPath, html);
            var projectFolder = new TemporaryFolder("BookStorageTests_ProjectCollection");
            _thingsToDispose.Add(projectFolder);
            var collectionSettings = new CollectionSettings(
                Path.Combine(projectFolder.Path, "test.bloomCollection")
            );
            var storage = new BookStorage(
                _folder.Path,
                _fileLocator,
                new BookRenamedEvent(),
                collectionSettings
            );
            if (doSave)
                storage.Save();
            return storage;
        }

        private BookStorage GetInitialStorage()
        {
            return GetInitialStorageWithCustomHtml(
                "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
            );
        }

        private BookStorage GetInitialStorageWithCustomHead(string head)
        {
            File.WriteAllText(_bookPath, "<html><head>" + head + " </head></body></html>");
            var storage = new BookStorage(
                _folder.Path,
                _fileLocator,
                new BookRenamedEvent(),
                new CollectionSettings()
            );
            storage.Save();
            return storage;
        }

        private BookStorage GetInitialStorageWithDifferentFileName(string bookName)
        {
            var bookPath = _folder.Combine(bookName + ".htm");
            File.WriteAllText(
                bookPath,
                "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
            );
            var projectFolder = new TemporaryFolder("BookStorageTests_ProjectCollection");
            _thingsToDispose.Add(projectFolder);
            var collectionSettings = new CollectionSettings(
                Path.Combine(projectFolder.Path, "test.bloomCollection")
            );
            var storage = new BookStorage(
                _folder.Path,
                _fileLocator,
                new BookRenamedEvent(),
                collectionSettings
            );
            storage.Save();
            return storage;
        }

        [Test]
        public void SetBookName_EasyCase_ChangesFolderAndFileName()
        {
            var storage = GetInitialStorage();
            using (var newFolder = new TemporaryFolder(_fixtureFolder, "newName"))
            {
                Directory.Delete(newFolder.Path);
                ChangeNameAndCheck(newFolder, storage);
            }
        }

        [Test]
        public void SetBookName_FolderWithNameAlreadyExists_AddsANumberToName()
        {
            using (var original = new TemporaryFolder(_folder, "original"))
            using (var x = new TemporaryFolder(_folder, "foo"))
            using (new TemporaryFolder(_folder, "foo 2"))
            using (var z = new TemporaryFolder(_folder, "foo 3"))
            using (var projectFolder = new TemporaryFolder("BookStorage_ProjectCollection"))
            {
                File.WriteAllText(
                    Path.Combine(original.Path, "original.htm"),
                    "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
                );

                var collectionSettings = new CollectionSettings(
                    Path.Combine(projectFolder.Path, "test.bloomCollection")
                );
                var storage = new BookStorage(
                    original.Path,
                    _fileLocator,
                    new BookRenamedEvent(),
                    collectionSettings
                );
                storage.Save();

                Directory.Delete(z.Path);
                //so, we ask for "foo", but should get "foo 3", because there is already a "foo" and "foo 2"
                var newBookName = Path.GetFileName(x.Path);
                storage.SetBookName(newBookName);
                var newPath = z.Combine("foo 3.htm");
                Assert.IsTrue(Directory.Exists(z.Path), "Expected folder:" + z.Path);
                Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
            }
        }

        [Test]
        public void SetBookName_FolderWithSanitizedNameAlreadyExists_AddsANumberToName()
        {
            using (var original = new TemporaryFolder(_folder, "original"))
            using (new TemporaryFolder(_folder, "foo"))
            using (new TemporaryFolder(_folder, "foo 2"))
            using (var z = new TemporaryFolder(_folder, "foo 3"))
            using (var projectFolder = new TemporaryFolder("BookStorage_ProjectCollection"))
            {
                File.WriteAllText(
                    Path.Combine(original.Path, "original.htm"),
                    "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
                );

                var collectionSettings = new CollectionSettings(
                    Path.Combine(projectFolder.Path, "test.bloomCollection")
                );
                var storage = new BookStorage(
                    original.Path,
                    _fileLocator,
                    new BookRenamedEvent(),
                    collectionSettings
                );
                storage.Save();

                Directory.Delete(z.Path);
                //so, we ask for "foo", but should get "foo 3", because there is already a "foo" and "foo 2"
                // BL-7816 We added some new characters to the sanitization routine
                const string newBookName = "foo?:&<>\'\"{}";
                storage.SetBookName(newBookName);
                var newPath = z.Combine("foo 3.htm");
                Assert.IsTrue(Directory.Exists(z.Path), "Expected folder:" + z.Path);
                Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
            }
        }

        [Test]
        public void SanitizeNameForFileSystem_FileNameIsLong_Truncates()
        {
            // variable that is 51 characters long
            const string longName = "012345678901234567890123456789012345678901234567890";
            var s = BookStorage.SanitizeNameForFileSystem(longName);
            Assert.AreEqual(BookStorage.kMaxFilenameLength, s.Length);
            Assert.AreEqual("01234567890123456789012345678901234567890123456789", s);

            // now if the last two characters are a surrogate pair (Earth emoji), we need to cut off more
            const string dangerous =
                "0123456789012345678901234567890123456789012345678\uD83C\uDF0D";
            s = BookStorage.SanitizeNameForFileSystem(dangerous);
            Assert.AreEqual(BookStorage.kMaxFilenameLength - 1, s.Length);
            Assert.AreEqual("0123456789012345678901234567890123456789012345678", s);

            // Once more for historical purposes: ref BL-12587
            const string tirhutaScript =
                "\ud805\udcae\ud805\udca9\ud805\udcc2\ud805\udcab\ud805\udcb9\u0020\ud805\udca7\ud805\udcb0\ud805\udca2\ud805\udcab\ud805\udcb0\ud805\udcc1\u0020\ud805\udcae\ud805\udcc2\ud805\udcab\ud805\udc9e\ud805\udca2\ud805\udcc2\ud805\udc9e\ud805\udcc2\ud805\udca9\ud805\udcb0\ud805\udcc1\u0020\ud805\udcae\ud805\udca7\ud805\udcb3\ud805\udc9e\ud805\udcc2\ud805\udca3\ud805\udca2\ud805\udcc2\ud805\udca2\ud805\udcb0\ud805\udcc1\u0020\ud805\udcab\ud805\udca9\ud805\udcc2\ud805\udc9e\ud805\udca2\ud805\udcc2\ud805\udc9e\ud805\udcb9";
            s = BookStorage.SanitizeNameForFileSystem(tirhutaScript);
            Assert.AreEqual(BookStorage.kMaxFilenameLength - 1, s.Length);
            Assert.IsFalse(char.IsHighSurrogate(s[s.Length - 1]));
        }

        [Test]
        public void SetBookName_FolderWithSanitizedNameAlreadyExists_DoesNotChangeFolderOrBookName()
        {
            using (new TemporaryFolder(_folder, "foo"))
            using (var x = new TemporaryFolder(_folder, "foo1"))
            using (var projectFolder = new TemporaryFolder("BookStorage_ProjectCollection"))
            {
                File.WriteAllText(
                    Path.Combine(x.Path, "foo1.htm"),
                    "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
                );

                var collectionSettings = new CollectionSettings(
                    Path.Combine(projectFolder.Path, "test.bloomCollection")
                );
                var storage = new BookStorage(
                    x.Path,
                    _fileLocator,
                    new BookRenamedEvent(),
                    collectionSettings
                );
                storage.Save();

                // So, we ask for "foo", and should get "foo1", because there is already a foo1
                // BL-7816 We added some new characters to the sanitization routine
                const string newBookName = "foo?:&<>\'\"{}";
                storage.SetBookName(newBookName);
                var newPath = x.Combine("foo1.htm");
                Assert.IsTrue(Directory.Exists(x.Path), "Expected folder:" + x.Path);
                Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
            }
        }

        [Test]
        [TestCase("foobar?:&<>\'\"{}")] // post-junk
        [TestCase("?:&<>\'\"{}foobar")] // pre-junk
        public void SetBookName_SanitizedName_ChangesFolder(string newBookName)
        {
            using (var x = new TemporaryFolder(_folder, "foo"))
            using (var y = new TemporaryFolder(_folder, "foobar"))
            using (var projectFolder = new TemporaryFolder("BookStorage_ProjectCollection"))
            {
                File.WriteAllText(
                    Path.Combine(x.Path, "foo.htm"),
                    "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
                );

                var collectionSettings = new CollectionSettings(
                    Path.Combine(projectFolder.Path, "test.bloomCollection")
                );
                var storage = new BookStorage(
                    x.Path,
                    _fileLocator,
                    new BookRenamedEvent(),
                    collectionSettings
                );
                storage.Save();

                Directory.Delete(y.Path);
                // BL-7816 We added some new characters to the sanitization routine
                storage.SetBookName(newBookName);
                var newPath = y.Combine("foobar.htm");
                Assert.IsTrue(Directory.Exists(y.Path), "Expected folder:" + y.Path);
                Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
            }
        }

        [Test]
        public void SetBookName_SanitizedName_JunkMidFoo_ChangesFolder()
        {
            using (var x = new TemporaryFolder(_folder, "foo"))
            using (var y = new TemporaryFolder(_folder, "fo         o"))
            using (var projectFolder = new TemporaryFolder("BookStorage_ProjectCollection"))
            {
                File.WriteAllText(
                    Path.Combine(x.Path, "foo.htm"),
                    "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
                );

                var collectionSettings = new CollectionSettings(
                    Path.Combine(projectFolder.Path, "test.bloomCollection")
                );
                var storage = new BookStorage(
                    x.Path,
                    _fileLocator,
                    new BookRenamedEvent(),
                    collectionSettings
                );
                storage.Save();

                Directory.Delete(y.Path);
                // BL-7816 We added some new characters to the sanitization routine
                const string newBookName = "fo?:&<>\'\"{}o";
                storage.SetBookName(newBookName);
                var newPath = y.Combine("fo         o.htm");
                Assert.IsTrue(Directory.Exists(y.Path), "Expected folder:" + y.Path);
                Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
            }
        }

        [Test]
        public void SetBookName_ShortenBooknameWorks()
        {
            using (var x = new TemporaryFolder(_folder, "foo and cap"))
            using (var y = new TemporaryFolder(_folder, "foo"))
            using (var projectFolder = new TemporaryFolder("BookStorage_ProjectCollection"))
            {
                File.WriteAllText(
                    Path.Combine(x.Path, "foo and cap.htm"),
                    "<html><head> href='file://blahblah\\editMode.css' type='text/css' /></head><body><div class='bloom-page'></div></body></html>"
                );

                var collectionSettings = new CollectionSettings(
                    Path.Combine(projectFolder.Path, "test.bloomCollection")
                );
                var storage = new BookStorage(
                    x.Path,
                    _fileLocator,
                    new BookRenamedEvent(),
                    collectionSettings
                );
                storage.Save();

                Directory.Delete(y.Path);

                // We are taking a longer name and shortening it.
                const string newBookName = "foo";
                storage.SetBookName(newBookName);
                var newPath = y.Combine("foo.htm");
                Assert.IsTrue(Directory.Exists(y.Path), "Expected folder:" + y.Path);
                Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
            }
        }

        [Test]
        public void SetBookName_FolderNameWasDifferentThanFileName_ChangesFolderAndFileName()
        {
            var storage = GetInitialStorageWithDifferentFileName("foo");
            using (var newFolder = new TemporaryFolder(_fixtureFolder, "newName"))
            {
                Directory.Delete(newFolder.Path);
                ChangeNameAndCheck(newFolder, storage);
            }
        }

        [Test]
        public void SetBookName_NameIsNotValidFileName_UsesSanitizedName()
        {
            var storage = GetInitialStorage();
            storage.SetBookName("/b?loom*test/");
            Assert.IsTrue(Directory.Exists(_fixtureFolder.Combine("b loom test")));
            Assert.IsTrue(File.Exists(_fixtureFolder.Combine("b loom test", "b loom test.htm")));
        }

        [TestCase("...Whenever")]
        [TestCase(". ..Whenever")]
        [TestCase(".. .Whenever")]
        [TestCase(" ...Whenever")]
        [TestCase(" . . . Whenever")]
        [TestCase("\t . . . Whenever")]
        [TestCase(" \t . . . Whenever")]
        [TestCase(".\t . . . Whenever")]
        [TestCase(" . \t . . . Whenever")]
        [TestCase("Whenever...")]
        [TestCase("Whenever. ..")]
        [TestCase("Whenever.. .")]
        [TestCase("Whenever... ")]
        [TestCase("Whenever.. . ")]
        public void SetBookName_NameHasLeadingOrTrailingPeriods_UsesSanitizedName(string bookTitle)
        {
            var storage = GetInitialStorage();
            storage.SetBookName(bookTitle);
            Assert.IsTrue(Directory.Exists(_fixtureFolder.Combine("Whenever")));
            Assert.IsTrue(File.Exists(_fixtureFolder.Combine("Whenever", "Whenever.htm")));
            Assert.That(Path.GetFileName(storage.FolderPath), Is.EqualTo("Whenever"));
        }

        /// <summary>
        /// regression test
        /// </summary>
        [Test]
        [Platform(
            Exclude = "Linux",
            Reason = "UNC paths for network drives are only used on Windows"
        )]
        public void SetBookName_PathIsAUNCToLocalHost_NoErrors()
        {
            var storage = GetInitialStorageUsingUNCPath();
            var path = storage.FolderPath;
            var newName = Guid.NewGuid().ToString();
            path = path.Replace(Path.GetFileName(path), newName);
            storage.SetBookName(newName);

            Assert.IsTrue(Directory.Exists(path));
            Assert.IsTrue(File.Exists(Path.Combine(path, newName + ".htm")));
        }

        [Test]
        public void PathToExistingHtml_WorksWithFullHtmlName()
        {
            var filenameOnly = "Big Book";
            var fullFilename = "Big Book.html";
            var storage = GetInitialStorageWithDifferentFileName(filenameOnly);
            var oldFullPath = Path.Combine(storage.FolderPath, filenameOnly + ".htm");
            var newFullPath = Path.Combine(storage.FolderPath, fullFilename);
            File.Move(oldFullPath, newFullPath); // rename to .html
            var path = storage.PathToExistingHtml;
            Assert.AreEqual(
                fullFilename,
                Path.GetFileName(path),
                "If this fails, 'path' will be empty string."
            );
        }

        /// <summary>
        /// This is really testing some Book.cs functionality, but it has to manipulate real files with a real storage,
        /// so it seems to fit better here.
        /// </summary>
        [Test]
        public void BringBookUpToDate_ConvertsTagsToJsonWithExpectedDefaults()
        {
            var storage = GetInitialStorage();
            var locator = (FileLocator)storage.GetFileLocator();
            string root = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                BloomFileLocator.BrowserRoot
            );
            locator.AddPath(root.CombineForPath("bookLayout"));
            var folder = storage.FolderPath;
            var tagsPath = Path.Combine(folder, "tags.txt");
            File.WriteAllText(tagsPath, "suitableForMakingShells\nexperimental\nfolio\n");
            var collectionSettings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(folder, "test"),
                    Language1Tag = "xyz",
                    Language2Tag = "en",
                    Language3Tag = "fr"
                }
            );
            var book = new Bloom.Book.Book(
                new BookInfo(folder, true),
                storage,
                new Mock<ITemplateFinder>().Object,
                collectionSettings,
                new Mock<PageSelection>().Object,
                new PageListChangedEvent(),
                new BookRefreshEvent()
            );

            book.BringBookUpToDate(new NullProgress());

            Assert.That(!File.Exists(tagsPath), "The tags.txt file should have been removed");

            // BL-2163, we are no longer migrating suitableForMakingShells
            Assert.That(storage.BookInfo.IsSuitableForMakingShells, Is.False);

            // BL-9223, we expect BringBookUpToDate to update the bookInfo with the value determined from the book's html.
            // The HTML in this test case does not indicate its a folio, so we expect BringBookUpToDate to change the value from True to False.
            Assert.That(storage.BookInfo.IsFolio, Is.False);

            Assert.That(storage.BookInfo.IsExperimental, Is.True);
            Assert.That(storage.BookInfo.BookletMakingIsAppropriate, Is.True);
            Assert.That(storage.BookInfo.AllowUploading, Is.True);
        }

        [Test]
        public void BringBookUpToDate_MigratesReaderToolsAvailableToToolboxIsOpen()
        {
            var oldMetaData =
                "{\"bookInstanceId\":\"3328aa4a - 2ef3 - 43a8 - a656 - 1d7c6f00444c\",\"folio\":false,\"title\":\"Landscape basic book\",\"baseUrl\":null,\"bookOrder\":null,\"isbn\":\"\",\"bookLineage\":\"056B6F11-4A6C-4942-B2BC-8861E62B03B3\",\"downloadSource\":null,\"license\":\"cc-by\",\"formatVersion\":\"2.0\",\"licenseNotes\":null,\"copyright\":null,\"authors\":null,\"credits\":\"\",\"tags\":[\"<p>\r\n</p>\"],\"pageCount\":0,\"languages\":[],\"langPointers\":null,\"summary\":null,\"allowUploadingToBloomLibrary\":true,\"bookletMakingIsAppropriate\":true,\"uploader\":null,\"tools\":null,\"readerToolsAvailable\":true}";
            var storage = GetInitialStorage();

            // This seems to be needed to let it locate some kind of collection settings.
            var folder = storage.FolderPath;
            var locator = (FileLocator)storage.GetFileLocator();
            string root = FileLocationUtilities.GetDirectoryDistributedWithApplication(
                BloomFileLocator.BrowserRoot
            );

            locator.AddPath(root.CombineForPath("bookLayout"));
            var collectionSettings = new CollectionSettings(
                new NewCollectionSettings()
                {
                    PathToSettingsFile = CollectionSettings.GetPathForNewSettings(folder, "test"),
                    Language1Tag = "xyz",
                    Language2Tag = "en",
                    Language3Tag = "fr"
                }
            );
            var book = new Bloom.Book.Book(
                new BookInfo(folder, true),
                storage,
                new Mock<ITemplateFinder>().Object,
                collectionSettings,
                new Mock<PageSelection>().Object,
                new PageListChangedEvent(),
                new BookRefreshEvent()
            );
            var jsonPath = book.BookInfo.MetaDataPath;
            File.WriteAllText(jsonPath, oldMetaData);

            book.BringBookUpToDate(new NullProgress());

            Assert.That(book.BookInfo.ToolboxIsOpen, Is.True);
        }

        [Test]
        public void MakeBookStorage_CorruptFile_Backup_ForSelect_RestoresBackup()
        {
            var badContent = "<htmlBlah>This is not good HTML";
            RobustFile.WriteAllText(_bookPath, badContent);
            var goodContent =
                "<html><head> </head><body><div class='bloom-page'>Some text</div></body></html>";
            RobustFile.WriteAllText(
                Path.Combine(Path.GetDirectoryName(_bookPath), "bookhtml.bak"),
                goodContent
            );
            var collectionSettings = new CollectionSettings(
                Path.Combine(_fixtureFolder.Path, "test.bloomCollection")
            );
            BookStorage storage;
            using (new ErrorReport.NonFatalErrorReportExpected())
            {
                storage = new BookStorage(
                    _folder.Path,
                    _fileLocator,
                    new BookRenamedEvent(),
                    collectionSettings
                );
            }
            Assert.That(File.ReadAllText(_bookPath), Is.EqualTo(goodContent));
            Assert.That(
                File.ReadAllText(
                    Path.Combine(_folder.Path, BookStorage.PrefixForCorruptHtmFiles + ".htm")
                ),
                Is.EqualTo(badContent)
            );
            AssertThatXmlIn
                .Dom(storage.Dom.RawDom)
                .HasAtLeastOneMatchForXpath("//div[@class='bloom-page']");
        }

        [Test]
        public void Save_SetsJsonFormatVersion()
        {
            var storage = GetInitialStorage();
            Assert.That(
                storage.BookInfo.FormatVersion,
                Is.EqualTo(BookStorage.kBloomFormatVersionToWrite)
            );
        }

        [Test]
        public void Save_ExistingFormatVersionIsHigher_SavesExistingFormatVersion()
        {
            // Setup
            var storage = GetInitialStorage();
            var higherFormatVersion = (
                float.Parse(BookStorage.kBloomFormatVersionToWrite, CultureInfo.InvariantCulture)
                + 1
            ).ToString(CultureInfo.InvariantCulture);
            storage.BookInfo.FormatVersion = higherFormatVersion;
            // Verify setup
            Assert.That(storage.BookInfo.FormatVersion, Is.EqualTo(higherFormatVersion));

            // SUT
            storage.Save();

            Assert.That(storage.BookInfo.FormatVersion, Is.EqualTo(higherFormatVersion));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1.0")]
        [TestCase(BookStorage.kBloomFormatVersionToWrite)]
        public void Save_ExistingFormatVersionIsLowerOrEqual_SavesCurrentFormatVersion(
            string lowerFormatVersion
        )
        {
            // Setup
            var storage = GetInitialStorage();
            storage.BookInfo.FormatVersion = lowerFormatVersion;
            // Verify setup
            Assert.That(storage.BookInfo.FormatVersion, Is.EqualTo(lowerFormatVersion));

            //SUT
            storage.Save();

            Assert.That(
                storage.BookInfo.FormatVersion,
                Is.EqualTo(BookStorage.kBloomFormatVersionToWrite)
            );
        }

        [TestCase("foo", "foo.html")] //normal case
        [TestCase("foobar", "foo.html")] //changed folder name
        [TestCase("foo", "foo.html", "bar.html")] //use folder name to decide (not sure this is good idea, but it's in existing code)
        [TestCase("foobar", "foo.html", "foo.htm.bak", "foo.htmbak")]
        [TestCase("foobar", "foo.html", "foo_conflict.htm")] //own cloud
        [TestCase("foobar", "foo.html", "foo_conflict.htm", "foo_conflict2.htm")] //two conflict files
        [TestCase("foobar", "foo.html", "foo (Scott's conflicted copy 2009-10-15).htm")] //dropbox
        [TestCase("foobar", "foo.html", "foo[conflict].htm")] // google
        [TestCase("foobar", "avoid conflict.html")] // only this one file with conflict in the name
        public void FindBookHtmlInFolder_MayHaveOtherFiles_ChoosesCorrectOne(
            string folderName,
            string expected,
            string decoy1 = null,
            string decoy2 = null
        )
        {
            using (
                var outerFolder = new TemporaryFolder(
                    $"FindBookHtmlInFolder_MayHaveOtherFiles_ChoosesCorrectOne_{Path.GetRandomFileName()}"
                )
            ) // using a different name each time to avoid conflicts with other tests
            {
                using (var folder = new TemporaryFolder(outerFolder, folderName))
                {
                    // Using WriteAllText rather than Create or CreateText so the file is closed and can thus be deleted
                    File.WriteAllText(folder.Combine(expected), "");
                    if (decoy1 != null)
                        File.WriteAllText(folder.Combine(decoy1), "");
                    if (decoy2 != null)
                        File.WriteAllText(folder.Combine(decoy2), "");

                    var path = BookStorage.FindBookHtmlInFolder(folder.Path);
                    Assert.AreEqual(expected, Path.GetFileName(path));
                }
            }
        }

        private void ChangeNameAndCheck(TemporaryFolder newFolder, BookStorage storage)
        {
            var newBookName = Path.GetFileName(newFolder.Path);
            storage.SetBookName(newBookName);
            var newPath = newFolder.Combine(newBookName + ".htm");
            Assert.IsTrue(Directory.Exists(newFolder.Path), "Expected folder:" + newFolder.Path);
            Assert.IsTrue(File.Exists(newPath), "Expected file:" + newPath);
        }

        [Test]
        public void Duplicate_CopyGetsNewGuid()
        {
            var storage = GetInitialStorage();
            var originalInstanceId = "aaaaaa-bbbb-cccc-dddd-eeeeeeeeee";
            File.WriteAllText(
                Path.Combine(storage.FolderPath, "meta.json"),
                $"{{'some':'thing', 'bookInstanceId':'{originalInstanceId}', 'other':'stuff'}}".Replace(
                    "'",
                    "\""
                )
            );

            var folderForDuplicate = storage.Duplicate();
            Assert.AreNotEqual(folderForDuplicate, storage.FolderPath, "Should have a new name");
            Assert.AreEqual(
                Path.GetDirectoryName(folderForDuplicate),
                Path.GetDirectoryName(storage.FolderPath),
                "Should be in same collection folder"
            );
            var metaPath = Path.Combine(folderForDuplicate, "meta.json");
            var meta = DynamicJson.Parse(File.ReadAllText(metaPath));
            Assert.AreNotEqual(
                originalInstanceId,
                meta.bookInstanceId,
                "The Duplicate should have a new InstanceId"
            );
            Assert.AreEqual("thing", meta.some, "rest of meta.json should be preserved");
            Assert.AreEqual("stuff", meta.other, "rest of meta.json should be preserved");
            Guid.Parse(meta.bookInstanceId); // will throw if we didn't actually get a guid
        }

        [Test]
        public void Duplicate_UnwantedFilesDropped()
        {
            var storage = GetInitialStorage();
            File.WriteAllText(Path.Combine(storage.FolderPath, "something.bak"), "hello");
            File.WriteAllText(
                Path.Combine(storage.FolderPath, "something.bloombookorder"),
                "hello"
            );
            File.WriteAllText(Path.Combine(storage.FolderPath, "something.pdf"), "hello");
            File.WriteAllText(Path.Combine(storage.FolderPath, "something.map"), "hello");

            var folderForDuplicate = storage.Duplicate();
            Assert.IsFalse(File.Exists(Path.Combine(folderForDuplicate, "something.bak")));
            Assert.IsFalse(
                File.Exists(Path.Combine(folderForDuplicate, "something.bloombookorder"))
            );
            Assert.IsFalse(File.Exists(Path.Combine(folderForDuplicate, "something.pdf")));
            Assert.IsFalse(File.Exists(Path.Combine(folderForDuplicate, "something.map")));
        }

        [Test]
        public void GetRequiredVersions_TextBoxAndComic_ReturnsBoth()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css' /></head>
			<body><div class='bloom-page'>
				<div data-audiorecordingmode='TextBox'/>
				<div class='bloom-imageContainer'>
					<svg class='comical-generated' />
				</div>
			</div></body></html>"
            );
            var requiredVersions = BookStorage.GetRequiredVersions(storage.Dom).ToArray();

            Assert.That(requiredVersions.Length, Is.EqualTo(2));

            Assert.That(requiredVersions[1].FeatureId, Is.EqualTo("wholeTextBoxAudio"));
            Assert.That(requiredVersions[1].FeaturePhrase, Is.EqualTo("Whole Text Box Audio"));
            Assert.That(requiredVersions[1].BloomDesktopMinVersion, Is.EqualTo("4.4"));
            Assert.That(requiredVersions[1].BloomReaderMinVersion, Is.EqualTo("1.0"));

            Assert.That(requiredVersions[0].FeatureId, Is.EqualTo("comical-1"));
            Assert.That(requiredVersions[0].FeaturePhrase, Is.EqualTo("Support for Comics"));
            Assert.That(requiredVersions[0].BloomDesktopMinVersion, Is.EqualTo("4.7"));
            Assert.That(requiredVersions[0].BloomReaderMinVersion, Is.EqualTo("1.0"));
        }

        private const string MinimalDataBubbleValue =
            "{`version`:`1.0`,`level`:1,`style`:`none`,`tails`:[]}";

        [Test]
        public void GetRequiredVersions_ComicStyleNoSvg_ReturnsNone()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css' /></head>
			<body><div class='bloom-page'>
				<div class='bloom-imageContainer'>
					<div class='bloom-textOverPicture' data-bubble='"
                    + MinimalDataBubbleValue
                    + @"'/>
				</div>
			</div></body></html>"
            );
            var requiredVersions = BookStorage.GetRequiredVersions(storage.Dom).ToArray();

            Assert.That(requiredVersions.Length, Is.EqualTo(0));
        }

        [Test]
        public void GetRequiredVersions_MixtureOfComicStyles_ReturnsComic()
        {
            const string captionBubbleWithNoTail =
                "{`version`:`1.0`,`level`:1,`style`:`caption`,`tails`:[]}";
            var storage = GetInitialStorageWithCustomHtml(
                @"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css' /></head>
				<body><div class='bloom-page'>
					<div class='bloom-imageContainer'>
						<div class='bloom-textOverPicture' data-bubble='"
                    + MinimalDataBubbleValue
                    + @"'/>
						<svg class='comical-generated' />
						<div class='bloom-textOverPicture' data-bubble='"
                    + captionBubbleWithNoTail
                    + @"'/>
					</div>
				</div></body></html>"
            );
            var requiredVersions = BookStorage.GetRequiredVersions(storage.Dom).ToArray();

            Assert.That(requiredVersions.Length, Is.EqualTo(1));

            Assert.That(requiredVersions[0].FeatureId, Is.EqualTo("comical-1"));
            Assert.That(requiredVersions[0].FeaturePhrase, Is.EqualTo("Support for Comics"));
            Assert.That(requiredVersions[0].BloomDesktopMinVersion, Is.EqualTo("4.7"));
            Assert.That(requiredVersions[0].BloomReaderMinVersion, Is.EqualTo("1.0"));
        }

        [Test]
        public void GetRequiredVersions_MixtureOfComics_ReturnsBothComic1and2()
        {
            const string ellipseBubble =
                "{`version`:`1.0`,`level`:1,`style`:`ellipse`,`tails`:[{`tipX`:578,`tipY`:11,`midpointX`:566,`midpointY`:32,`autoCurve`:true}]}";
            const string captionBubbleWithTail =
                "{`version`:`1.0`,`level`:1,`style`:`caption`,`tails`:[{`tipX`:332,`tipY`:287,`midpointX`:293,`midpointY`:124,`autoCurve`:true}]}";
            // Added .ui-resizable because of an initial xpath error that only worked if the class had only .bloom-textOverPicture
            var storage = GetInitialStorageWithCustomHtml(
                @"<html><head><link rel='stylesheet' href='Basic Book.css' type='text/css' /></head>
			<body><div class='bloom-page'>
				<div class='bloom-imageContainer'>
					<div class='bloom-textOverPicture ui-resizable' data-bubble='"
                    + ellipseBubble
                    + @"'/>
					<svg class='comical-generated' />
					<div class='bloom-textOverPicture ui-resizable' data-bubble='"
                    + captionBubbleWithTail
                    + @"'/>
				</div>
			</div></body></html>"
            );
            var requiredVersions = BookStorage.GetRequiredVersions(storage.Dom).ToArray();

            Assert.That(requiredVersions.Length, Is.EqualTo(2));

            Assert.That(requiredVersions[1].FeatureId, Is.EqualTo("comical-1"));
            Assert.That(requiredVersions[1].FeaturePhrase, Is.EqualTo("Support for Comics"));
            Assert.That(requiredVersions[1].BloomDesktopMinVersion, Is.EqualTo("4.7"));
            Assert.That(requiredVersions[1].BloomReaderMinVersion, Is.EqualTo("1.0"));

            Assert.That(requiredVersions[0].FeatureId, Is.EqualTo("comical-2"));
            Assert.That(
                requiredVersions[0].FeaturePhrase,
                Is.EqualTo("Support for Comic Captions with Straight Line Tails")
            );
            Assert.That(requiredVersions[0].BloomDesktopMinVersion, Is.EqualTo("5.0"));
            Assert.That(requiredVersions[0].BloomReaderMinVersion, Is.EqualTo("1.0"));
        }

        [Test]
        public void PerformNecessaryMaintenanceOnBook_DeletesSVGIfOnlyNoneStyle()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"
<html><head>
	<link rel='stylesheet' href='Basic Book.css' type='text/css' />
	<meta name='maintenanceLevel' content='1'></meta>
</head>
<body>
	<div class='bloom-page'>
		<div class='bloom-imageContainer'>
			<div class='bloom-textOverPicture' data-bubble='"
                    + MinimalDataBubbleValue
                    + @"'/>
			<svg class='comical-generated' />
		</div>
	</div>
</body></html>"
            );

            //SUT
            storage.MigrateToMediaLevel1ShrinkLargeImages();
            storage.MigrateToLevel2RemoveTransparentComicalSvgs();
            storage.MigrateToLevel3PutImgFirst();

            //Verification
            var maintLevel = storage.Dom.GetMetaValue("maintenanceLevel", "0");
            Assert.That(maintLevel, Is.GreaterThanOrEqualTo("2"));
            Assert.That(
                storage.Dom.SafeSelectNodes("//*[@class='comical-generated']").Count,
                Is.EqualTo(0)
            );
        }

        [Test]
        public void PerformNecessaryMaintenanceOnBook_EnsuresImgAtStartOfImageContainer()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"
<html><head>
	<link rel='stylesheet' href='Basic Book.css' type='text/css' />
	<meta name='maintenanceLevel' content='2'></meta>
</head>
<body>
	<div class='bloom-page'>
		<div class='bloom-imageContainer'>
			<div class='bloom-textOverPicture'/>
			<div class='bloom-textOverPicture'/>
			<img src='rubbish' id='moveMe' />
		</div>
	</div>
</body></html>"
            );

            //SUT
            storage.MigrateToMediaLevel1ShrinkLargeImages();
            storage.MigrateToLevel2RemoveTransparentComicalSvgs();
            storage.MigrateToLevel3PutImgFirst();

            //Verification
            var maintLevel = storage.Dom.GetMetaValue("maintenanceLevel", "0");
            var container = storage.Dom.SelectSingleNode("//*[@class='bloom-imageContainer']");
            Assert.That(container, Is.Not.Null);
            var firstChild =
                container.ChildNodes.Cast<XmlNode>().FirstOrDefault(x => x is XmlElement)
                as XmlElement;
            Assert.That(firstChild.Attributes["id"]?.Value, Is.EqualTo("moveMe"));
            Assert.That(maintLevel, Is.GreaterThanOrEqualTo("3"));
        }

        [Test]
        public void PerformNecessaryMaintenanceOnBook_DoesNotDeleteSVGIfOtherStylePresent()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"
<html><head>
	<link rel='stylesheet' href='Basic Book.css' type='text/css' />
	<meta name='maintenanceLevel' content='1'></meta>
</head>
<body>
	<div class='bloom-page'>
		<div class='bloom-imageContainer'>
			<div class='bloom-textOverPicture' data-bubble='"
                    + MinimalDataBubbleValue
                    + @"'/>
			<svg class='comical-generated'/>
			<div class='bloom-textOverPicture' data-bubble='{`version`:`1.0`,`tails`:[{`tipX`:5.5,`tipY`:99,`midpointX`:5.1,`midpointY`:1.95,`joiner`:true,`autoCurve`:true}],`level`:1,`style`:`speech`,`order`:2}'/>
		</div>
	</div>
</body></html>"
            );

            //SUT
            storage.MigrateToMediaLevel1ShrinkLargeImages();
            storage.MigrateToLevel2RemoveTransparentComicalSvgs();
            storage.MigrateToLevel3PutImgFirst();

            //Verification
            var maintLevel = storage.Dom.GetMetaValue("maintenanceLevel", "0");
            Assert.That(maintLevel, Is.GreaterThanOrEqualTo("2"));
            Assert.That(
                storage.Dom.SafeSelectNodes("//*[@class='comical-generated']").Count,
                Is.EqualTo(1)
            );
        }

        [Test]
        public void PerformNecessaryMaintenanceOnBook_HandlesMultipleSVGs()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"
<html><head>
	<link rel='stylesheet' href='Basic Book.css' type='text/css' />
	<meta name='maintenanceLevel' content='1'></meta>
</head>
<body>
	<div class='bloom-page'>
		<div class='bloom-imageContainer'>
			<div class='bloom-textOverPicture' data-bubble='"
                    + MinimalDataBubbleValue
                    + @"'/>
			<svg class='comical-generated'/>
			<div class='bloom-textOverPicture' data-bubble='"
                    + MinimalDataBubbleValue
                    + @"'/>
		</div>
		<div class='bloom-imageContainer'>
			<svg class='comical-generated'/>
			<div class='bloom-textOverPicture' data-bubble='{`version`:`1.0`,`tails`:[{`tipX`:5.5,`tipY`:99,`midpointX`:5.1,`midpointY`:1.95,`joiner`:true,`autoCurve`:true}],`level`:1,`style`:`speech`,`order`:2}'/>
			<div class='bloom-textOverPicture' data-bubble='"
                    + MinimalDataBubbleValue
                    + @"'/>
		</div>
	</div>
</body></html>"
            );

            //SUT
            storage.MigrateToMediaLevel1ShrinkLargeImages();
            storage.MigrateToLevel2RemoveTransparentComicalSvgs();
            storage.MigrateToLevel3PutImgFirst();

            //Verification
            var maintLevel = storage.Dom.GetMetaValue("maintenanceLevel", "0");
            Assert.That(maintLevel, Is.GreaterThanOrEqualTo("2"));
            Assert.That(
                storage.Dom.SafeSelectNodes("//*[@class='comical-generated']").Count,
                Is.EqualTo(1)
            );
        }

        [Test]
        public void PerformNecessaryMaintenanceOnBook_DoesNothingIfAlreadyProcessed()
        {
            var storage = GetInitialStorageWithCustomHtml(
                @"
<html><head>
	<link rel='stylesheet' href='Basic Book.css' type='text/css' />
	<meta name='maintenanceLevel' content='2'></meta>
</head>
<body>
	<div class='bloom-page'>
		<div class='bloom-imageContainer'>
			<div class='bloom-textOverPicture' data-bubble='{`version`:`1.0`,`level`:1,`style`:`none`,`backgroundColors`:[`#e09494`],`tails`:[]}'/>
			<svg class='comical-generated' />
		</div>
	</div>
</body></html>"
            );

            //SUT
            storage.MigrateToMediaLevel1ShrinkLargeImages();
            storage.MigrateToLevel2RemoveTransparentComicalSvgs();
            storage.MigrateToLevel3PutImgFirst();

            //Verification
            var maintLevel = storage.Dom.GetMetaValue("maintenanceLevel", "0");
            Assert.That(maintLevel, Is.GreaterThanOrEqualTo("2"));
            Assert.That(
                storage.Dom.SafeSelectNodes("//*[@class='comical-generated']").Count,
                Is.EqualTo(1)
            );
        }

        [Test]
        public void ShowAccessDeniedHtml_SetsErrorMessagesHtmlCorrectly()
        {
            var storage = GetInitialStorage();
            var exception = new UnauthorizedAccessException(
                "Access to the path 'blah blah' is denied."
            );

            //SUT
            storage.ShowAccessDeniedErrorHtml(exception);

            //Verification
            // Note: This expectation is based on the localized English text of several strings.
            // Hope neither the language nor localizations change...
            string expectedHtml =
                "Your computer denied Bloom access to the book. You may need technical help in setting the operating system permissions for this file.<br />Access to the path &#39;blah blah&#39; is denied.<br />See <a href='http://community.bloomlibrary.org/t/how-to-fix-file-permissions-problems/78'>http://community.bloomlibrary.org/t/how-to-fix-file-permissions-problems/78</a>.";
            Assert.That(storage.ErrorMessagesHtml, Is.EqualTo(expectedHtml));
        }

        [Test]
        public void GetUniqueFolderPath_templateUsedNoNumberNecessary_returnsUnnumberedPath()
        {
            using (
                var directory = new SIL.TestUtilities.TemporaryFolder(
                    "BookStorageTests_getUniqueFolderPath"
                )
            )
            {
                string unnumberedName = "Moon & Cap (before import overwrite)";
                string template = "Moon & Cap (before import overwrite-{0})";
                string dir1 = BookStorage.GetUniqueFolderPath(
                    directory.Path,
                    unnumberedName,
                    template
                );
                Assert.That(
                    dir1,
                    Is.EqualTo(Path.Combine(directory.Path, "Moon & Cap (before import overwrite)"))
                );
            }
        }

        [Test]
        public void GetUniqueFolderPath_templateUsedNumberNecessary_returnsNumberdPath()
        {
            using (
                var directory = new SIL.TestUtilities.TemporaryFolder(
                    "BookStorageTests_getUniqueFolderPath"
                )
            )
            {
                string unnumberedName = "Moon & Cap (before import overwrite)";
                string template = "Moon & Cap (before import overwrite-{0})";
                Directory.CreateDirectory(Path.Combine(directory.Path, unnumberedName));
                string dir2 = BookStorage.GetUniqueFolderPath(
                    directory.Path,
                    unnumberedName,
                    template
                );
                Assert.That(
                    dir2,
                    Is.EqualTo(
                        Path.Combine(directory.Path, "Moon & Cap (before import overwrite-2)")
                    )
                );
            }
        }

        [Test]
        public void GetUniqueFolderPath_badTemplateUsed_throwsException()
        {
            using (
                var directory = new SIL.TestUtilities.TemporaryFolder(
                    "BookStorageTests_getUniqueFolderPath"
                )
            )
            {
                string unnumberedName = "Moon & Cap (before import overwrite)";
                string template = "Moon & Cap (before import overwrite-)";
                Directory.CreateDirectory(Path.Combine(directory.Path, unnumberedName));
                Directory.CreateDirectory(Path.Combine(directory.Path, template));
                TestDelegate systemUnderTest = () =>
                {
                    BookStorage.GetUniqueFolderPath(directory.Path, unnumberedName, template);
                };

                Assert.Throws(typeof(System.ArgumentException), systemUnderTest);
            }
        }

        [Test]
        [TestCase("foo.html", true)]
        [TestCase("foo.htm", true)]
        [TestCase("My Htm file test book1.htm", true)]
        [TestCase("My Htm file test book1.html", true)]
        [TestCase("ReadMe-en.htm", false)]
        [TestCase("Readme-fr.htm", false)]
        [TestCase("configuration.htm", false)]
        public void FindDeletableHtmFiles_DeletesTheRightFiles(string fileName, bool expectToDelete)
        {
            const string folderAndCorrectFilename = "My Htm file test book";
            using (
                var directory = new TemporaryFolder(
                    "BookStorageTests_FindDeletableHtmFiles_DeletesTheRightFiles"
                )
            )
            {
                var bookFolderPath = Path.Combine(directory.Path, folderAndCorrectFilename);
                Directory.CreateDirectory(bookFolderPath);
                var correctFilePath = Path.Combine(
                    bookFolderPath,
                    folderAndCorrectFilename + ".htm"
                );
                File.WriteAllText(
                    correctFilePath,
                    "This is the correct filename and directory for this test book."
                );
                var testFilePath = Path.Combine(bookFolderPath, fileName);
                File.WriteAllText(
                    testFilePath,
                    "This is the contents for the current testcase file."
                );

                // SUT
                var results = BookStorage.FindDeletableHtmFiles(bookFolderPath);

                Assert.IsFalse(results.Contains(correctFilePath)); // don't delete the right .htm file!
                Assert.IsTrue(
                    expectToDelete
                        ? results.Contains(testFilePath)
                        : !results.Contains(testFilePath)
                );
            }
        }
    }
}
