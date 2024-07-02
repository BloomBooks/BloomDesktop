using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using Bloom.Spreadsheet;
using BloomTemp;
using BloomTests.TeamCollection;
using NUnit.Framework;
using SIL.IO;
using SIL.Xml;

namespace BloomTests.Spreadsheet
{
    public class SpreadsheetAudioImportTests
    {
        private HtmlDom _dom;

        private TemporaryFolder _bookFolder;
        private TemporaryFolder _otherAudioFolder;
        private ProgressSpy _progressSpy;
        private TemporaryFolder _spreadsheetFolder;
        private List<string> _warnings;
        private List<SafeXmlElement> _contentPages;
        private SafeXmlElement _firstPage;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            // We will re-use the audio files from another test.
            // Copy them into the expected place in the book folder.
            var testAudioPath = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                "src/BloomTests/Spreadsheet/testAudio/"
            );
            _bookFolder = new TemporaryFolder("SpreadsheetAudioImportTests");
            _spreadsheetFolder = new TemporaryFolder("SpreadsheetAudioImportDest");
            var spreadsheetAudioPath = Path.Combine(_spreadsheetFolder.FolderPath, "audio");
            Directory.CreateDirectory(spreadsheetAudioPath);
            // A place to put audio that is not in the spreadsheet folder so we can test absolute path import.
            // Also gives us more choices of non-conflicting audio files.
            _otherAudioFolder = new TemporaryFolder("other audio folder");
            var audioFilePaths = Directory.EnumerateFiles(testAudioPath).ToArray();
            foreach (var audioFilePath in audioFilePaths)
            {
                RobustFile.Copy(
                    audioFilePath,
                    Path.Combine(spreadsheetAudioPath, Path.GetFileName(audioFilePath))
                );
                var anotherId = Path.GetFileNameWithoutExtension(audioFilePath) + "x"; // slightly different to avoid dup here
                var anotherPath = Path.Combine(_otherAudioFolder.FolderPath, anotherId + ".mp3");
                RobustFile.Copy(audioFilePath, anotherPath);
            }

            // Yet another copy of this file to be the source for the title.
            var titleAudioPath = Path.Combine(
                _otherAudioFolder.FolderPath,
                "i9c7f4e02-4685-48fc-8653-71d88f218706t.mp3"
            );
            RobustFile.Copy(
                Path.Combine(testAudioPath, "i9c7f4e02-4685-48fc-8653-71d88f218706.mp3"),
                titleAudioPath
            );

            // Make a source file. This name is actually allowed by Windows, but not by our code,
            // as it is a problem in HTML. Ampersand is not allowed in files, and spaces are not allowed in IDs
            var dest = Path.Combine(spreadsheetAudioPath, "a bad & problematic name.mp3");
            RobustFile.Copy(audioFilePaths[0], dest);
            File.WriteAllText(Path.Combine(spreadsheetAudioPath, "an empty file.mp3"), "");

            // Create an HtmlDom for a template to import into.
            // No content pages, for this test importing a default page will work.
            var xml = string.Format(
                SpreadsheetImageAndTextImportTests.templateDom,
                SpreadsheetImageAndTextImportTests.coverPage
                    + SpreadsheetImageAndTextImportTests.insideBackCoverPage
                    + SpreadsheetImageAndTextImportTests.backCoverPage
            );
            _dom = new HtmlDom(xml, true);

            // Set up the internal spreadsheet rows directly.
            var ss = new InternalSpreadsheet();
            var columnForFr = ss.AddColumnForLang("fr", "French");
            var columnForFrAudio = ss.AddColumnForLangAudio("fr", "French");
            var columnForFrAlignment = ss.AddColumnForAudioAlignment("fr", "French");

            var titleRow = new ContentRow(ss);
            titleRow.SetCell(0, "[bookTitle]");
            titleRow.SetCell(columnForFr, "<p>My Title</p>");
            titleRow.SetCell(columnForFrAudio, titleAudioPath);
            // Actual duration of this audio is 3.996735; we should get that, not 2.5
            titleRow.SetCell(columnForFrAlignment, "2.5");

            // Data to let us test for missing file error message in xmatter
            var creditsRow = new ContentRow(ss);
            creditsRow.SetCell(0, "[smallCoverCredits]");
            creditsRow.SetCell(columnForFr, "<p>My credits</p>");
            creditsRow.SetCell(columnForFrAudio, "missing file.mp3");

            // Will make a TG on page 1. It will be treated as a single split,
            // as there is only one sentence and data in the alignment column
            var contentRow1 = new ContentRow(ss);
            contentRow1.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow1.SetCell(columnForFr, "<p>This is page 1</p>");
            contentRow1.SetCell(
                columnForFrAudio,
                "./audio/i9c7f4e02-4685-48fc-8653-71d88f218706.mp3"
            );
            // Actual duration of this audio is 3.996735; we should get that, not 4.5
            // Also, we should NOT get a warning, even though this is too large; we just fix it.
            contentRow1.SetCell(columnForFrAlignment, "4.5");

            // Will make a TG on page 2. It will be treated as a single split,
            // as there is only one sentence and there is data in the alignment column
            var contentRow2 = new ContentRow(ss);
            contentRow2.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow2.SetCell(columnForFr, "<p>This is page 2.</p>");
            contentRow2.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "i9c7f4e02-4685-48fc-8653-71d88f218706x.mp3"
                )
            );
            // Actual duration of this audio is 3.996735; we should get that
            contentRow2.SetCell(columnForFrAlignment, "2.9");

            // Will make a TG on page 3. It will be treated as an unsplit TextBox,
            // as there are two sentences and a single number in the alignment column
            var contentRow3 = new ContentRow(ss);
            contentRow3.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow3.SetCell(
                columnForFr,
                "<p>This is page 3. <strong>This</strong> <em>'sentence'</em> <u>has</u> <sup>many</sup> <span style=\"color:#ff1616;\">text</span> variations.</p>"
            );
            contentRow3.SetCell(
                columnForFrAudio,
                "./audio/a9d7b794-7a83-473a-8307-7968176ae4bc.mp3"
            );
            // Actual duration of this audio is 4.388571; we should get that
            contentRow3.SetCell(columnForFrAlignment, "2.9");

            // Will make a TG on page 4. It will be treated as a split TextBox,
            // as there are two sentences and two numbers in the alignment column
            var contentRow4 = new ContentRow(ss);
            contentRow4.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow4.SetCell(columnForFr, "<p>This is page 4. It has two sentences.</p>");
            contentRow4.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "a9d7b794-7a83-473a-8307-7968176ae4bcx.mp3"
                )
            );
            // Actual duration of this audio is 4.996745; we should get that in place of the 5.9
            contentRow4.SetCell(columnForFrAlignment, "1.5 5.9");

            // Will make a TG on page 5. It will be treated as sentence mode,
            // as there are two sentences and nothing in the alignment column.
            // Also tests that we can cope with sentences with dangerous characters, particularly single quote.
            var contentRow5 = new ContentRow(ss);
            contentRow5.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow5.SetCell(
                columnForFr,
                "<p>This is page 5. <strong>This</strong> <em>'sentence'</em> <u>has</u> <sup>many</sup> <span style=\"color:#ff1616;\">text</span> variations, single quotes and other dangerous characters: {}[]\"@().</p>"
            );
            // actual durations are 2.899592 and 2.194286
            contentRow5.SetCell(
                columnForFrAudio,
                "./audio/i991ae1ac-db9a-4ad1-984b-8e679c1ae901.mp3, ./audio/i77c18c83-0224-405f-bb97-70d32078855c.mp3"
            );

            // Will fail, because file is not found
            var contentRow6 = new ContentRow(ss);
            contentRow6.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow6.SetCell(columnForFr, "<p>This is page 6.</p>");
            contentRow6.SetCell(columnForFrAudio, "./audio/missingJunk.mp3");

            // Invalid file name. Should correct it.
            var contentRow7 = new ContentRow(ss);
            contentRow7.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow7.SetCell(columnForFr, "<p>This is page 7</p>");
            contentRow7.SetCell(columnForFrAudio, "./audio/a bad & problematic name.mp3");
            // Actual duration of this audio is 3.996735; we should get that, not 2.5
            contentRow7.SetCell(columnForFrAlignment, "2.5");

            // Duplicate file name. Should make a new file.
            var contentRow8 = new ContentRow(ss);
            contentRow8.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow8.SetCell(columnForFr, "<p>This is page 8</p>");
            contentRow8.SetCell(columnForFrAudio, "./audio/a bad & problematic name.mp3");
            // Actual duration of this audio is 3.996735; we should get that, not 2.5
            contentRow8.SetCell(columnForFrAlignment, "2.5");

            // This page will fail to import audio, since more than one audio file is not compatible
            // with having alignment data.
            var contentRow9 = new ContentRow(ss);
            contentRow9.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow9.SetCell(columnForFr, "<p>This is page 9. It has two sentences.</p>");
            contentRow9.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "a9d7b794-7a83-473a-8307-7968176ae4bcx.mp3"
                ) + ", ./audio/i77c18c83-0224-405f-bb97-70d32078855c.mp3"
            );
            contentRow9.SetCell(columnForFrAlignment, "2.9");

            // This page will fail to import audio alignment, since there are two sentences, but three alignment values.
            var contentRow10 = new ContentRow(ss);
            contentRow10.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow10.SetCell(columnForFr, "<p>This is page 10. It has two sentences.</p>");
            contentRow10.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "a9d7b794-7a83-473a-8307-7968176ae4bcx.mp3"
                )
            );
            contentRow10.SetCell(columnForFrAlignment, "1 1.5 2.9");

            // This page fails to import audio because there are unequal numbers of sentences and audio files.
            var contentRow11 = new ContentRow(ss);
            contentRow11.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow11.SetCell(
                columnForFr,
                "<p>This is page 11. It has three sentences. But only two audio files!</p>"
            );
            // actual durations are 2.899592 and 2.194286
            contentRow11.SetCell(
                columnForFrAudio,
                "./audio/i991ae1ac-db9a-4ad1-984b-8e679c1ae901.mp3, ./audio/i77c18c83-0224-405f-bb97-70d32078855c.mp3"
            );

            // This page imports correctly, making a sentence mode text, even though one is missing.
            var contentRow12 = new ContentRow(ss);
            contentRow12.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow12.SetCell(
                columnForFr,
                "<p>This is page 12. It has three sentences. But only two audio files!</p>"
            );
            // actual durations are 2.899592 and 2.194286
            contentRow12.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "i991ae1ac-db9a-4ad1-984b-8e679c1ae901x.mp3"
                )
                    + ", missing, "
                    + Path.Combine(
                        _otherAudioFolder.FolderPath,
                        "i77c18c83-0224-405f-bb97-70d32078855cx.mp3"
                    )
            );

            // Will make a TG on page 13. It will be treated as an unsplit TextBox and produce a warning,
            // since although there are two sentences and two numbers in the alignment column,
            // the first alignment number is larger than the total duration.
            var contentRow13 = new ContentRow(ss);
            contentRow13.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow13.SetCell(columnForFr, "<p>This is page 13. It has two sentences.</p>");
            contentRow13.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "a9d7b794-7a83-473a-8307-7968176ae4bcx.mp3"
                )
            );
            // Actual duration of this audio is 4.996745
            contentRow13.SetCell(columnForFrAlignment, "6 2.9");

            // Will make a TG on page 14. It will be treated as an unsplit TextBox,
            // since although there are two sentences and two numbers in the alignment column,
            // the first alignment number is not a valid number.
            var contentRow14 = new ContentRow(ss);
            contentRow14.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow14.SetCell(columnForFr, "<p>This is page 14. It has two sentences.</p>");
            contentRow14.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "a9d7b794-7a83-473a-8307-7968176ae4bcx.mp3"
                )
            );
            // Actual duration of this audio is 4.996745
            contentRow14.SetCell(columnForFrAlignment, "half 2.9");

            // This page fails to import audio because the audio file is not valid mp3.
            var contentRow15 = new ContentRow(ss);
            contentRow15.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow15.SetCell(columnForFr, "<p>This is page 15.</p>");
            // file is literally empty, not a valid mp3
            contentRow15.SetCell(columnForFrAudio, "./audio/an empty file.mp3");

            // This will make an empty editable. (It should not complain about an excess audio file, as there is none.)
            // (Note: this state is not really adequate. The problem I was trying to reproduce occurs when
            // we are reusing an existing page from the book, and it already contains an empty element for this language.
            // However, I don't think it's worth rewriting the whole test suite to include pre-existing pages, and this
            // case just might catch some other problem sometime.)
            var contentRow16 = new ContentRow(ss);
            contentRow16.AddCell(InternalSpreadsheet.PageContentRowLabel);
            var columnForEn = ss.AddColumnForLang("en", "English");
            contentRow16.SetCell(columnForFr, "<p>[blank]</p>");
            contentRow16.SetCell(columnForEn, "<p>This is page 16.</p>");

            // This row has no audio at all. This should not produce an error.
            var contentRow17 = new ContentRow(ss);
            contentRow17.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow17.SetCell(columnForFr, "<p>This is page 17.</p>");

            // Will make a TG on page 18. It will be treated as a split TextBox,
            // as there are five sentences/phrases and five numbers in the alignment column.
            // data-duration="14.602449"
            var contentRow18 = new ContentRow(ss);
            contentRow18.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow18.SetCell(
                columnForFr,
                "<p>This page has some phrase markers | which are used to indicate audio segments.  The page​ is recorded as a whole.  This is a test,| this is only a test.</p>"
            );
            contentRow18.SetCell(
                columnForFrAudio,
                "./audio/i4b3ef0cf-213c-4fad-82f3-6341bf415707.mp3"
            );
            contentRow18.SetCell(columnForFrAlignment, "3.500 7.320 10.320 12.160 14.560");

            // not sure if we need this
            var settings = new NewCollectionSettings();
            settings.Language1.Tag = "es";
            settings.Language1.SetName("Spanish", false);

            // Do the import
            _progressSpy = new ProgressSpy();
            var importer = new TestSpreadsheetImporter(
                null,
                _dom,
                _spreadsheetFolder.FolderPath,
                _bookFolder.FolderPath,
                settings
            );
            _warnings = await importer.ImportAsync(ss, _progressSpy);

            _contentPages = _dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();

            // Remove the xmatter to get just the content pages.
            _firstPage = _contentPages[0];
            _contentPages.RemoveAt(0);
            //_lastPage = _contentPages.Last();
            _contentPages.RemoveAt(_contentPages.Count - 1);
            //_secondLastPage = _contentPages.Last();
            _contentPages.RemoveAt(_contentPages.Count - 1);

            // (individual test methods will evaluate the result)
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _bookFolder?.Dispose();
            _otherAudioFolder.Dispose();
        }

        [TestCase(0, "i9c7f4e02-4685-48fc-8653-71d88f218706", "div")]
        [TestCase(1, "i9c7f4e02-4685-48fc-8653-71d88f218706x", "div")]
        [TestCase(2, "a9d7b794-7a83-473a-8307-7968176ae4bc", "div")]
        [TestCase(3, "a9d7b794-7a83-473a-8307-7968176ae4bcx", "div")]
        [TestCase(4, "i991ae1ac-db9a-4ad1-984b-8e679c1ae901", "span")]
        [TestCase(4, "i77c18c83-0224-405f-bb97-70d32078855c", "span")]
        [TestCase(11, "i991ae1ac-db9a-4ad1-984b-8e679c1ae901x", "span")]
        [TestCase(11, "i77c18c83-0224-405f-bb97-70d32078855cx", "span")]
        [TestCase(17, "i4b3ef0cf-213c-4fad-82f3-6341bf415707", "div")]
        public void GotAudioOnPageN(int pageIndex, string id, string elt)
        {
            AssertThatXmlIn
                .Element(_contentPages[pageIndex])
                .HasSpecifiedNumberOfMatchesForXpath($".//{elt}[@id='{id}']", 1);
        }

        [TestCase("i9c7f4e02-4685-48fc-8653-71d88f218706")]
        [TestCase("i9c7f4e02-4685-48fc-8653-71d88f218706x")]
        [TestCase("a9d7b794-7a83-473a-8307-7968176ae4bc")]
        [TestCase("a9d7b794-7a83-473a-8307-7968176ae4bcx")]
        [TestCase("i991ae1ac-db9a-4ad1-984b-8e679c1ae901")]
        [TestCase("i77c18c83-0224-405f-bb97-70d32078855c")]
        [TestCase("i991ae1ac-db9a-4ad1-984b-8e679c1ae901x")]
        [TestCase("i77c18c83-0224-405f-bb97-70d32078855cx")]
        [TestCase("i9c7f4e02-4685-48fc-8653-71d88f218706t")]
        [TestCase("i4b3ef0cf-213c-4fad-82f3-6341bf415707")]
        public void GotAudioFile(string id)
        {
            var path = Path.Combine(_bookFolder.FolderPath, "audio", id + ".mp3");
            Assert.That(File.Exists(path), Is.True, $"expected file {path} not found");
        }

        [TestCase(0, "i9c7f4e02-4685-48fc-8653-71d88f218706", "div", 3.996735)]
        [TestCase(1, "i9c7f4e02-4685-48fc-8653-71d88f218706x", "div", 3.996735)]
        [TestCase(2, "a9d7b794-7a83-473a-8307-7968176ae4bc", "div", 4.388571)]
        [TestCase(3, "a9d7b794-7a83-473a-8307-7968176ae4bcx", "div", 4.388571)]
        [TestCase(4, "i991ae1ac-db9a-4ad1-984b-8e679c1ae901", "span", 2.899592)]
        [TestCase(4, "i77c18c83-0224-405f-bb97-70d32078855c", "span", 2.194286)]
        [TestCase(11, "i991ae1ac-db9a-4ad1-984b-8e679c1ae901x", "span", 2.899592)]
        [TestCase(11, "i77c18c83-0224-405f-bb97-70d32078855cx", "span", 2.194286)]
        [TestCase(17, "i4b3ef0cf-213c-4fad-82f3-6341bf415707", "div", 14.602449)]
        public void GotAudioDurationAndOtherEssentialsOnPageN(
            int pageIndex,
            string id,
            string elt,
            double expectedDuration
        )
        {
            var target = _contentPages[pageIndex]
                .SafeSelectNodes($".//{elt}[@id='{id}']")
                .Cast<SafeXmlElement>()
                .First();
            var durationStr = target.GetAttribute("data-duration");
            // NumberStyles and CultureInfo - make test robust enough to run if in English(Sweden) region.
            Assert.That(
                double.TryParse(
                    durationStr,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double duration
                ),
                Is.True,
                $"Failed to parse '{durationStr}' as double"
            );
            Assert.That(duration, Is.EqualTo(expectedDuration).Within(0.01));
            Assert.That(target.GetAttribute("class"), Does.Contain("audio-sentence"));
            Assert.That(target.GetAttribute("recordingmd5"), Is.Not.Empty.And.Not.Null);
        }

        [TestCase(0, "i9c7f4e02-4685-48fc-8653-71d88f218706", "", 3.996735)]
        [TestCase(1, "i9c7f4e02-4685-48fc-8653-71d88f218706x", "", 3.996735)]
        [TestCase(3, "a9d7b794-7a83-473a-8307-7968176ae4bcx", "1.5", 4.388571)]
        public void GotAudioRecordingTimes(
            int pageIndex,
            string id,
            string otherSplits,
            double expectedDuration
        )
        {
            var target = _contentPages[pageIndex]
                .SafeSelectNodes($".//div[@id='{id}']")
                .Cast<SafeXmlElement>()
                .First();
            var times = target.GetAttribute("data-audiorecordingendtimes") ?? "";
            var index = times.LastIndexOf(" "); // not found produces -1, which happens to work just right.
            var durationStr = times.Substring(index + 1, times.Length - index - 1);
            // NumberStyles and CultureInfo - make test robust enough to run if in English(Sweden) region.
            Assert.That(
                double.TryParse(
                    durationStr,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double duration
                ),
                Is.True,
                $"Failed to parse '{durationStr}' as double"
            );
            Assert.That(duration, Is.EqualTo(expectedDuration).Within(0.01));
            Assert.That(times.Substring(0, index + 1).Trim(), Is.EqualTo(otherSplits));
            Assert.That(target.GetAttribute("class"), Does.Contain("bloom-postAudioSplit"));
        }

        [TestCase(2)] // Page 3 is an unsplit TextBox
        [TestCase(4)] // Page 5 is sentence mode.
        [TestCase(11)] // Page 12 is sentence mode.
        public void GotNoAudioRecordingTimes(int pageIndex)
        {
            AssertThatXmlIn
                .Element(_contentPages[pageIndex])
                .HasNoMatchForXpath(".//div[@data-audiorecordingendtimes]");
        }

        [TestCase(0, "TextBox")]
        [TestCase(1, "TextBox")]
        [TestCase(2, "TextBox")]
        [TestCase(3, "TextBox")]
        [TestCase(4, "Sentence")]
        [TestCase(11, "Sentence")]
        public void GotRightModeOnPageN(int pageIndex, string expectedMode)
        {
            AssertThatXmlIn
                .Element(_contentPages[pageIndex])
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[contains(@class, 'bloom-editable') and @data-audiorecordingmode='{expectedMode}']",
                    1
                );
        }

        [TestCase(0, "i9c7f4e02-4685-48fc-8653-71d88f218706", "div", new[] { "This is page 1" })]
        [TestCase(1, "i9c7f4e02-4685-48fc-8653-71d88f218706x", "div", new[] { "This is page 2." })]
        [TestCase(2, "a9d7b794-7a83-473a-8307-7968176ae4bc", "div", new string[0])]
        [TestCase(
            3,
            "a9d7b794-7a83-473a-8307-7968176ae4bcx",
            "div",
            new[] { "This is page 4.", "It has two sentences." }
        )]
        [TestCase(
            17,
            "i4b3ef0cf-213c-4fad-82f3-6341bf415707",
            "div",
            new[]
            {
                "This page has some phrase markers ",
                "which are used to indicate audio segments.",
                "The page​ is recorded as a whole.",
                "This is a test,",
                "this is only a test."
            }
        )]
        public void GotSentenceSplitsOnPageN(
            int pageIndex,
            string id,
            string elt,
            string[] sentences
        )
        {
            var target = _contentPages[pageIndex]
                .SafeSelectNodes($".//{elt}[@id='{id}']")
                .Cast<SafeXmlElement>()
                .First();
            foreach (var s in sentences)
            {
                AssertThatXmlIn
                    .Element(target)
                    .HasSpecifiedNumberOfMatchesForXpath(
                        $".//span[@id and @class='bloom-highlightSegment' and text()='{s}']",
                        1
                    );
            }

            if (sentences.Length == 0)
            {
                AssertThatXmlIn
                    .Element(target)
                    .HasNoMatchForXpath($".//span[@id and @class='bloom-highlightSegment']");
            }
        }

        [Test]
        public void GotPhraseMarkingsOnSplitsOnPageN()
        {
            var rawSpans = new[]
            {
                // Note that spans get a new id for every run of the test.
                " class=\"bloom-highlightSegment\">This page has some phrase markers <span class=\"bloom-audio-split-marker\">\u200B</span></span>",
                " class=\"bloom-highlightSegment\">which are used to indicate audio segments.</span>",
                " class=\"bloom-highlightSegment\">The page​ is recorded as a whole.</span>",
                " class=\"bloom-highlightSegment\">This is a test,<span class=\"bloom-audio-split-marker\">\u200B</span></span>",
                " class=\"bloom-highlightSegment\">this is only a test.</span>"
            };
            var target = _contentPages[17]
                .SafeSelectNodes($".//div[@id='i4b3ef0cf-213c-4fad-82f3-6341bf415707']")
                .Cast<SafeXmlElement>()
                .First();
            var innerXml = target.InnerXml;
            foreach (var raw in rawSpans)
            {
                Assert.That(innerXml.Contains(raw), "Did not match " + raw);
            }
        }

        [Test]
        public void TitleAudioImported()
        {
            var titleDiv = _dom.SafeSelectNodes(".//div[@data-book='bookTitle' and @lang='fr']")
                .Cast<SafeXmlElement>()
                .First();
            Assert.That(titleDiv, Is.Not.Null);
            Assert.That(
                titleDiv.GetAttribute("id"),
                Is.EqualTo("i9c7f4e02-4685-48fc-8653-71d88f218706t")
            );
        }

        [Test]
        public void MissingAudioFileReported()
        {
            var misssingAudioFilePath = Path.Combine(
                    _spreadsheetFolder.FolderPath,
                    "audio",
                    "missingJunk.mp3"
                )
                .Replace("\\", "/");
            Assert.That(
                _progressSpy.Errors,
                Does.Contain(
                    $"Did not import audio on page 6 because '{misssingAudioFilePath}' was not found."
                )
            );
        }

        [Test]
        public void MissingXmatterAudioFileReported()
        {
            var misssingAudioFilePath = Path.Combine(
                    _spreadsheetFolder.FolderPath,
                    "audio",
                    "missingJunk.mp3"
                )
                .Replace("\\", "/");
            Assert.That(
                _progressSpy.Errors,
                Does.Contain(
                    $"Did not import audio for smallCoverCredits because 'missing file.mp3' was not found."
                )
            );
        }

        [Test]
        public void BadAudioFileNameHandled()
        {
            var target = _contentPages[6]
                .SafeSelectNodes(".//div[contains(@class, bloom-editable) and @lang='fr']")
                .Cast<SafeXmlElement>()
                .First();
            var id = target.GetAttribute("id");
            Assert.That(id, Is.EqualTo("abadproblematicname")); // name corrected to something valid
            var path = Path.Combine(_bookFolder.FolderPath, "audio", id + ".mp3");
            Assert.That(RobustFile.Exists(path));
        }

        [Test]
        public void NoErrorForEmptyAudio()
        {
            Assert.That(_progressSpy.Errors, Has.None.Match(".*17.*"));
        }

        [Test]
        public void DuplicateAudioFileHandled()
        {
            var target = _contentPages[7]
                .SafeSelectNodes(".//div[contains(@class, bloom-editable) and @lang='fr']")
                .Cast<SafeXmlElement>()
                .First();
            var id = target.GetAttribute("id");
            Assert.That(id, Is.Not.EqualTo("abadproblematicname")); // the ID we'd expect but for the duplication
            var path = Path.Combine(_bookFolder.FolderPath, "audio", id + ".mp3");
            Assert.That(RobustFile.Exists(path));
        }

        [Test]
        public void MultipleFilesWithAlignmentReported()
        {
            Assert.That(
                _progressSpy.Errors,
                Does.Contain(
                    $"Did not import audio on page 9 because there should be only one audio file when audio alignment is specified."
                )
            );
        }

        [Test]
        public void MismatchedAlignmentReported()
        {
            Assert.That(
                _progressSpy.Errors,
                Does.Contain(
                    $"Did not import audio alignments on page 10 because there are 3 audio alignments for 2 sentences; they should match up."
                )
            );
        }

        [Test]
        public void MismatchedAudioFilesReported()
        {
            Assert.That(
                _progressSpy.Errors,
                Does.Contain(
                    $"Did not import audio on page 11 because there are 2 audio files for 3 sentences; they should match up. Use 'missing' if necessary."
                )
            );
        }

        [Test]
        public void NoComplaintForLiteralMissing()
        {
            // We should NOT get a missing-file error for the literal string 'missing'.
            Assert.That(
                _progressSpy.Errors,
                Has.None.Matches("Did not import audio on page 12 because .* was not found")
            );
        }

        [Test]
        public void NoDurationForLiteralMissing()
        {
            // There are three spans for three sentences, but only the first and last have real audio files, so only two should have durations.
            AssertThatXmlIn
                .Element(_contentPages[11])
                .HasSpecifiedNumberOfMatchesForXpath(
                    ".//span[@class='audio-sentence' and @data-duration]",
                    2
                );
        }

        [Test]
        public void TooBigSplitProducesWarning()
        {
            Assert.That(
                _progressSpy.Warnings,
                Does.Contain(
                    $"Removed audio alignments on page 13 because some values in the list given ('6 2.9') are larger than the duration of the audio file (4.389)."
                )
            );
        }

        [Test]
        public void NoWarningFixingSingleAlignment()
        {
            Assert.That(
                _progressSpy.Warnings,
                Has.None.Matches("^Removed audio alignments on page 1 because.*")
            );
        }

        [Test]
        public void NoWarningFixingLastAlignment()
        {
            Assert.That(
                _progressSpy.Warnings,
                Has.None.Matches("^Removed audio alignments on page 4 because.*")
            );
        }

        [Test]
        public void InvalidSplitProducesWarning()
        {
            Assert.That(
                _progressSpy.Warnings,
                Does.Contain(
                    $"Removed audio alignments on page 14 because some values in 'half 2.9' are not valid numbers."
                )
            );
        }

        [TestCase(12)]
        [TestCase(13)]
        public void InvalidSplitRemoved(int pageNumber)
        {
            var assertThatXmlInPageN = AssertThatXmlIn.Element(_contentPages[pageNumber]);
            assertThatXmlInPageN.HasNoMatchForXpath(".//div[@data-audiorecordingendtimes]");
            assertThatXmlInPageN.HasNoMatchForXpath(".//span[@bloom-highlightSegment]");
        }

        [Test]
        public void InvalidMp3ProducesError()
        {
            var badAudioFilePath = Path.Combine(
                    _spreadsheetFolder.FolderPath,
                    "audio",
                    "an empty file.mp3"
                )
                .Replace("\\", "/");
            Assert.That(
                _progressSpy.Errors,
                Does.Contain(
                    $"Did not import audio on page 15 because the audio file '{badAudioFilePath}' is not a valid mp3 file."
                )
            );
        }

        [TestCase(2)] // TextBlock mode sample
        [TestCase(4)] // Sentence mode sample
        public void CharacterMarkupSurvives(int pageNumber)
        {
            // Should include this sentence: <strong>This</strong> <em>sentence</em> <u>has</u> <sup>many</sup> <span style="color:#ff1616;">text</span> variations.
            var assertThatXmlInPageN = AssertThatXmlIn.Element(_contentPages[pageNumber]);
            assertThatXmlInPageN.HasSpecifiedNumberOfMatchesForXpath(".//strong[text()='This']", 1);
            assertThatXmlInPageN.HasSpecifiedNumberOfMatchesForXpath(
                ".//em[text()=\"'sentence'\"]",
                1
            );
            assertThatXmlInPageN.HasSpecifiedNumberOfMatchesForXpath(".//u[text()='has']", 1);
            assertThatXmlInPageN.HasSpecifiedNumberOfMatchesForXpath(".//sup[text()='many']", 1);
            assertThatXmlInPageN.HasSpecifiedNumberOfMatchesForXpath(
                ".//span[@style='color:#ff1616;' and text()='text']",
                1
            );
        }

        [Test]
        public void EmptyEditableNotAProblem()
        {
            AssertThatXmlIn
                .Element(_contentPages[15])
                .HasNoMatchForXpath(".//div[@lang='fr' and contains(@class, 'bloom-editable')]");
            Assert.That(_progressSpy.Errors, Has.None.Matches("Did not import audio on page 16"));
        }

        [TestCase("abc", "abc")]
        [TestCase("abc def", "abcdef")]
        [TestCase("1233", "i1233")]
        [TestCase("ab+*", "ab")]
        [TestCase("$*&", "defaultId")]
        public void SanitizeXHmlId(string input, string expectedOutput)
        {
            Assert.That(SpreadsheetImporter.SanitizeXHtmlId(input), Is.EqualTo(expectedOutput));
        }
    }

    // This class handles some special cases that need to be checked when the import is modifying existing pages.
    class SpreadsheetAudioImportModifyTests
    {
        private HtmlDom _dom;

        private TemporaryFolder _bookFolder;
        private TemporaryFolder _otherAudioFolder;
        private ProgressSpy _progressSpy;
        private TemporaryFolder _spreadsheetFolder;
        private List<string> _warnings;
        private List<SafeXmlElement> _contentPages;

        public static string PageWithJustText(
            int pageNumber,
            string editableAttrs,
            string editableContent,
            string classData
        )
        {
            return string.Format(
                @"	<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""dc90dbe0-7584-4d9f-bc06-0e0326060054"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""{0}"" lang="""">
        <div class=""pageLabel"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"" lang=""en"">
            Basic Text &amp; Picture
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""split-pane-component marginBox"" style="""">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"">
                    <div class=""split-pane-component-inner"" min-width=""60px 150px 250px"" min-height=""60px 150px 250px"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                           <div class=""bloom-editable normal-style"" style="""" lang=""z"" contenteditable=""true"">
                                <p></p>
                            </div>
							<div class=""bloom-editable normal-style {3}"" lang=""fr"" contenteditable=""true"" {1}>
								{2}
							</div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>",
                pageNumber,
                editableAttrs,
                editableContent,
                classData
            );
        }

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            // We will re-use the audio files from another test.
            // Copy them into the expected place in the book folder.
            var testAudioPath = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                "src/BloomTests/Spreadsheet/testAudio/"
            );
            _bookFolder = new TemporaryFolder("SpreadsheetAudioImportModifyTests");
            _spreadsheetFolder = new TemporaryFolder("SpreadsheetAudioImportModifyDest");
            var spreadsheetAudioPath = Path.Combine(_spreadsheetFolder.FolderPath, "audio");
            Directory.CreateDirectory(spreadsheetAudioPath);
            // A place to put audio that is not in the spreadsheet folder so we can test absolute path import.
            // Also gives us more choices of non-conflicting audio files.
            _otherAudioFolder = new TemporaryFolder("other audio folder");
            var audioFilePaths = Directory.EnumerateFiles(testAudioPath).ToArray();
            foreach (var audioFilePath in audioFilePaths)
            {
                RobustFile.Copy(
                    audioFilePath,
                    Path.Combine(spreadsheetAudioPath, Path.GetFileName(audioFilePath))
                );
                var anotherId = Path.GetFileNameWithoutExtension(audioFilePath) + "x"; // slightly different to avoid dup here
                var anotherPath = Path.Combine(_otherAudioFolder.FolderPath, anotherId + ".mp3");
                RobustFile.Copy(audioFilePath, anotherPath);
            }

            // Make a source file. This name is actually allowed by Windows, but not by our code,
            // as it is a problem in HTML. Ampersand is not allowed in files, and spaces are not allowed in IDs
            var dest = Path.Combine(spreadsheetAudioPath, "a bad & problematic name.mp3");
            RobustFile.Copy(audioFilePaths[0], dest);
            File.WriteAllText(Path.Combine(spreadsheetAudioPath, "an empty file.mp3"), "");

            // Create an HtmlDom for a template to import into.
            var xml = string.Format(
                SpreadsheetImageAndTextImportTests.templateDom,
                SpreadsheetImageAndTextImportTests.coverPage
                    // Original page 1 has two sentences and is in sentence mode.
                    + PageWithJustText(
                        1,
                        "data-audiorecordingmode=\"Sentence\"",
                        "<p><span id=\"i991ae1ac-db9a-4ad1-984b-8e679c1ae901\" class=\"audio-sentence\" recordingmd5=\"f766dd9023afe0d86acf6330e99261b3\" data-duration=\"2.899592\">A boy sitting on a stone.</span> <span id=\"i77c18c83-0224-405f-bb97-70d32078855c\" class=\"audio-sentence\" recordingmd5=\"0f433ef9ef7693f968a8559e0210c57e\" data-duration=\"2.194286\">His foot is wet.</span></p>\r\n",
                        ""
                    )
                    // Original page 2 is in text box mode, unsplit
                    + PageWithJustText(
                        2,
                        "data-audiorecordingmode=\"TextBox\" recordingmd5=\"69f7226e062f6b4accaa8110787f81fb\" data-duration=\"4.388571\"",
                        "<p>Man touching nose. He's using a finger.</p>",
                        "audio-sentence"
                    )
                    // Original page 3 has two sentences and is in sentence mode.
                    + PageWithJustText(
                        3,
                        "data-audiorecordingmode=\"Sentence\"",
                        "<p><span id=\"i991ae1ac-db9a-4ad1-984b-8e679c1ae901\" class=\"audio-sentence\" recordingmd5=\"f766dd9023afe0d86acf6330e99261b3\" data-duration=\"2.899592\">A boy sitting on a stone.</span> <span id=\"i77c18c83-0224-405f-bb97-70d32078855c\" class=\"audio-sentence\" recordingmd5=\"0f433ef9ef7693f968a8559e0210c57e\" data-duration=\"2.194286\">His foot is wet.</span></p>\r\n",
                        ""
                    )
                    + PageWithJustText(
                        4,
                        "data-audiorecordingmode=\"Sentence\"",
                        "<p><span id=\"i991ae1ac-db9a-4ad1-984b-8e679c1ae901\" class=\"audio-sentence\" recordingmd5=\"f766dd9023afe0d86acf6330e99261b3\" data-duration=\"2.899592\">A boy sitting on a stone.</span> <span id=\"i77c18c83-0224-405f-bb97-70d32078855c\" class=\"audio-sentence\" recordingmd5=\"0f433ef9ef7693f968a8559e0210c57e\" data-duration=\"2.194286\">His foot is wet.</span></p>\r\n",
                        ""
                    )
                    + PageWithJustText(
                        5,
                        "data-audiorecordingmode=\"TextBox\" id=\"a9d7b794-7a83-473a-8307-7968176ae4bc\" recordingmd5=\"69f7226e062f6b4accaa8110787f81fb\" data-duration=\"4.388571\" data-audiorecordingendtimes=\"3.900 4.360\"",
                        @"<p><span id=""i4356108a-4647-4b01-8cb9-8acc227b4eea"" class=""bloom-highlightSegment"">A hand with a sore finger.</span> <span id=""cf66ff0d-f882-48b0-b789-cba83aa3ae84"" class=""bloom-highlightSegment"">It has a bandage.​</span></p>",
                        "audio-sentence"
                    )
                    + PageWithJustText(
                        6,
                        "data-audiorecordingmode=\"TextBox\" id=\"a9d7b794-7a83-473a-8307-7968176ae4bc\" recordingmd5=\"69f7226e062f6b4accaa8110787f81fb\" data-duration=\"4.388571\" data-audiorecordingendtimes=\"3.900 4.360\"",
                        @"<p><span id=""i4356108a-4647-4b01-8cb9-8acc227b4eea"" class=""bloom-highlightSegment"">A hand with a sore finger.</span> <span id=""cf66ff0d-f882-48b0-b789-cba83aa3ae84"" class=""bloom-highlightSegment"">It has a bandage.​</span></p>",
                        "audio-sentence bloom-postAudioSplit"
                    )
                    + SpreadsheetImageAndTextImportTests.insideBackCoverPage
                    + SpreadsheetImageAndTextImportTests.backCoverPage
            );
            _dom = new HtmlDom(xml, true);

            // Set up the internal spreadsheet rows directly.
            var ss = new InternalSpreadsheet();
            var columnForFr = ss.AddColumnForLang("fr", "French");
            var columnForFrAudio = ss.AddColumnForLangAudio("fr", "French");
            var columnForFrAlignment = ss.AddColumnForAudioAlignment("fr", "French");

            // Will make a TG on page 1. It will be treated as a single split,
            // as there is only one sentence and data in the alignment column.
            // We want to check that all the sentence-mode stuff in the original page 1 is gone.
            var contentRow1 = new ContentRow(ss);
            contentRow1.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow1.SetCell(columnForFr, "<p>This is page 1</p>");
            contentRow1.SetCell(
                columnForFrAudio,
                "./audio/i9c7f4e02-4685-48fc-8653-71d88f218706.mp3"
            );
            // Actual duration of this audio is 3.996735; we should get that, not 2.5
            contentRow1.SetCell(columnForFrAlignment, "2.5");

            // Will make a TG on page 2. It will be treated as a single split,
            // as there is only one sentence and there is data in the alignment column.
            // Want to make sure this works when the original page 2 is an unsplit text box.
            var contentRow2 = new ContentRow(ss);
            contentRow2.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow2.SetCell(columnForFr, "<p>This is page 2.</p>");
            contentRow2.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "i9c7f4e02-4685-48fc-8653-71d88f218706x.mp3"
                )
            );
            // Actual duration of this audio is 3.996735; we should get that
            contentRow2.SetCell(columnForFrAlignment, "2.9");

            // Will make a TG on page 3. It will be treated as an unsplit TextBox,
            // as there are two sentences and a single number in the alignment column.
            // Want to make sure the sentence-mode stuff in the original gets cleaned up.
            var contentRow3 = new ContentRow(ss);
            contentRow3.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow3.SetCell(
                columnForFr,
                "<p>This is page 3. <strong>This</strong> <em>'sentence'</em> <u>has</u> <sup>many</sup> <span style=\"color:#ff1616;\">text</span> variations.</p>"
            );
            contentRow3.SetCell(
                columnForFrAudio,
                "./audio/a9d7b794-7a83-473a-8307-7968176ae4bc.mp3"
            );
            // Actual duration of this audio is 4.388571; we should get that
            contentRow3.SetCell(columnForFrAlignment, "2.9");

            // Will make a TG on page 4. It will be treated as a split TextBox,
            // as there are two sentences and two numbers in the alignment column
            // Want to make sure the sentence-mode stuff gets cleaned up.
            var contentRow4 = new ContentRow(ss);
            contentRow4.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow4.SetCell(columnForFr, "<p>This is page 4. It has two sentences.</p>");
            contentRow4.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "a9d7b794-7a83-473a-8307-7968176ae4bcx.mp3"
                )
            );
            // Actual duration of this audio is 4.996745; we should get that in place of the 2.9
            contentRow4.SetCell(columnForFrAlignment, "1.5 2.9");

            // Will make a TG on page 5. It will be treated as sentence mode,
            // as there are two sentences and nothing in the alignment column.
            // We want to make sure that the split text box stuff in the original page 5 is cleaned up.
            var contentRow5 = new ContentRow(ss);
            contentRow5.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow5.SetCell(
                columnForFr,
                "<p>This is page 5. <strong>This</strong> <em>'sentence'</em> <u>has</u> <sup>many</sup> <span style=\"color:#ff1616;\">text</span> variations, single quotes and other dangerous characters: {}[]\"@().</p>"
            );
            // actual durations are 2.899592 and 2.194286
            contentRow5.SetCell(
                columnForFrAudio,
                "./audio/i991ae1ac-db9a-4ad1-984b-8e679c1ae901.mp3, ./audio/i77c18c83-0224-405f-bb97-70d32078855c.mp3"
            );

            // Will make a TG on page 6. It will be treated as an unsplit TextBox,
            // as there are two sentences and a three numbers in the alignment column.
            // Want to make sure the sentence-mode stuff in the original gets cleaned up.
            var contentRow6 = new ContentRow(ss);
            contentRow6.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow6.SetCell(
                columnForFr,
                "<p>This is page 6. <strong>This</strong> <em>'sentence'</em> <u>has</u> <sup>many</sup> <span style=\"color:#ff1616;\">text</span> variations.</p>"
            );
            contentRow6.SetCell(
                columnForFrAudio,
                Path.Combine(
                    _otherAudioFolder.FolderPath,
                    "i77c18c83-0224-405f-bb97-70d32078855cx.mp3"
                )
            );
            // Actual duration of this audio is 2.194286; we should get that
            contentRow6.SetCell(columnForFrAlignment, "1.0 1.5 2.9");

            // not sure if we need this
            var settings = new NewCollectionSettings();
            settings.Language1.Tag = "es";
            settings.Language1.SetName("Spanish", false);

            // Do the import
            _progressSpy = new ProgressSpy();
            var importer = new TestSpreadsheetImporter(
                null,
                _dom,
                _spreadsheetFolder.FolderPath,
                _bookFolder.FolderPath,
                settings
            );
            _warnings = await importer.ImportAsync(ss, _progressSpy);

            _contentPages = _dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();

            // Remove the xmatter to get just the content pages.
            //_firstPage = _contentPages[0];
            _contentPages.RemoveAt(0);
            //_lastPage = _contentPages.Last();
            _contentPages.RemoveAt(_contentPages.Count - 1);
            //_secondLastPage = _contentPages.Last();
            _contentPages.RemoveAt(_contentPages.Count - 1);

            // (individual test methods will evaluate the result)
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _bookFolder?.Dispose();
            _otherAudioFolder.Dispose();
        }

        [TestCase(4)]
        public void PageNDataNotTextBox(int pageNumber)
        {
            var assertThatXmlInPageN = AssertThatXmlIn.Element(_contentPages[pageNumber]);
            assertThatXmlInPageN.HasNoMatchForXpath(
                ".//div[contains(@class, 'bloom-editable') and @lang='fr' and contains(@class, 'audio-sentence')]"
            );
            assertThatXmlInPageN.HasNoMatchForXpath(
                ".//div[contains(@class, 'bloom-editable') and @lang='fr' and @data-audiorecordingendtimes]"
            );
            assertThatXmlInPageN.HasNoMatchForXpath(
                ".//div[contains(@class, 'bloom-editable') and @lang='fr' and @recordingmd5]"
            );
            assertThatXmlInPageN.HasNoMatchForXpath(
                ".//div[contains(@class, 'bloom-editable') and @lang='fr' and @data-duration]"
            );
            assertThatXmlInPageN.HasNoMatchForXpath(
                ".//span[contains(@class, 'bloom-highlightSegment')]"
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        public void PageNDataNotSentence(int pageNumber)
        {
            var assertThatXmlInPageN = AssertThatXmlIn.Element(_contentPages[pageNumber]);
            assertThatXmlInPageN.HasNoMatchForXpath(".//span[contains(@class, 'audio-sentence')]");
            assertThatXmlInPageN.HasNoMatchForXpath(".//span[@recordingmd5]");
            assertThatXmlInPageN.HasNoMatchForXpath(".//span[@data-duration]");
        }

        [Test]
        public void PostAudioSplit_Removed_WhenAlignmentsMismatched()
        {
            AssertThatXmlIn
                .Element(_contentPages[5])
                .HasNoMatchForXpath(".//div[contains(@class, 'bloom-postAudioSplit')]");
        }

        [Test]
        public void MismatchedAlignmentReported()
        {
            Assert.That(
                _progressSpy.Errors,
                Does.Contain(
                    $"Did not import audio alignments on page 6 because there are 3 audio alignments for 2 sentences; they should match up."
                )
            );
        }

        [TestCase(0, "i9c7f4e02-4685-48fc-8653-71d88f218706", "div", 3.996735)]
        [TestCase(1, "i9c7f4e02-4685-48fc-8653-71d88f218706x", "div", 3.996735)]
        [TestCase(2, "a9d7b794-7a83-473a-8307-7968176ae4bc", "div", 4.388571)]
        [TestCase(3, "a9d7b794-7a83-473a-8307-7968176ae4bcx", "div", 4.388571)]
        [TestCase(4, "i991ae1ac-db9a-4ad1-984b-8e679c1ae901", "span", 2.899592)]
        [TestCase(4, "i77c18c83-0224-405f-bb97-70d32078855c", "span", 2.194286)]
        [TestCase(5, "i77c18c83-0224-405f-bb97-70d32078855cx", "div", 2.194286)]
        public void GotAudioDurationAndOtherEssentialsOnPageN(
            int pageIndex,
            string id,
            string elt,
            double expectedDuration
        )
        {
            var target = _contentPages[pageIndex]
                .SafeSelectNodes($".//{elt}[@id='{id}']")
                .Cast<SafeXmlElement>()
                .First();
            var durationStr = target.GetAttribute("data-duration");
            // NumberStyles and CultureInfo - make test robust enough to run if in English(Sweden) region.
            Assert.That(
                double.TryParse(
                    durationStr,
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out double duration
                ),
                Is.True,
                $"Failed to parse '{durationStr}' as double"
            );
            Assert.That(duration, Is.EqualTo(expectedDuration).Within(0.01));
            Assert.That(target.GetAttribute("class"), Does.Contain("audio-sentence"));
            Assert.That(target.GetAttribute("recordingmd5"), Is.Not.Empty.And.Not.Null);
        }

        [TestCase(0, "i9c7f4e02-4685-48fc-8653-71d88f218706", "div", new[] { "This is page 1" })]
        [TestCase(1, "i9c7f4e02-4685-48fc-8653-71d88f218706x", "div", new[] { "This is page 2." })]
        [TestCase(2, "a9d7b794-7a83-473a-8307-7968176ae4bc", "div", new string[0])]
        [TestCase(
            3,
            "a9d7b794-7a83-473a-8307-7968176ae4bcx",
            "div",
            new[] { "This is page 4.", "It has two sentences." }
        )]
        public void GotSentenceSplitsOnPageN(
            int pageIndex,
            string id,
            string elt,
            string[] sentences
        )
        {
            var target = _contentPages[pageIndex]
                .SafeSelectNodes($".//{elt}[@id='{id}']")
                .Cast<SafeXmlElement>()
                .First();
            foreach (var s in sentences)
            {
                AssertThatXmlIn
                    .Element(target)
                    .HasSpecifiedNumberOfMatchesForXpath(
                        $".//span[@id and @class='bloom-highlightSegment' and text()='{s}']",
                        1
                    );
            }

            if (sentences.Length == 0)
            {
                AssertThatXmlIn
                    .Element(target)
                    .HasNoMatchForXpath($".//span[@id and @class='bloom-highlightSegment']");
            }
        }

        [TestCase(0, "TextBox")]
        [TestCase(1, "TextBox")]
        [TestCase(2, "TextBox")]
        [TestCase(3, "TextBox")]
        [TestCase(4, "Sentence")]
        [TestCase(5, "TextBox")]
        public void GotRightModeOnPageN(int pageIndex, string expectedMode)
        {
            AssertThatXmlIn
                .Element(_contentPages[pageIndex])
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[contains(@class, 'bloom-editable') and @data-audiorecordingmode='{expectedMode}']",
                    1
                );
        }
    }
}
