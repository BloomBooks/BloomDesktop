using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom;
using Bloom.Book;
using Bloom.Spreadsheet;
using BloomTemp;
using BloomTests.TeamCollection;
using Moq;
using NUnit.Framework;
using OfficeOpenXml;
using SIL.Extensions;
using SIL.IO;

namespace BloomTests.Spreadsheet
{
    public class SpreadsheetAudioTestsBase
    {
        protected InternalSpreadsheet Sheet;
        protected List<ContentRow> Rows;
        protected IEnumerable<SpreadsheetRow> AllRows;
        protected List<ContentRow> PageContentRows;
        protected SpreadsheetExporter _exporter;
        protected TemporaryFolder _spreadsheetFolder;
        protected TemporaryFolder _bookFolder;
        protected ProgressSpy _progressSpy;

        // The beginning of a real bloom book,  up to the first page.
        protected string StartOfFile =
            @"<!DOCTYPE html>

<html>
<head>
    <meta charset=""UTF-8""></meta>
    <meta name=""Generator"" content=""Bloom Version 5.4.0 (apparent build date: 06-Sep-2022)""></meta>
    <meta name=""BloomFormatVersion"" content=""2.1""></meta>
    <meta name=""pageTemplateSource"" content=""Basic Book""></meta>

    <title>audio test</title>
    <style type=""text/css"" title=""userModifiedStyles"">
    /*<![CDATA[*/
    .BigWords-style { font-size: 45pt !important; text-align: center !important; }/*]]>*/
    </style>
    <style type=""text/css"">
    DIV.bloom-page.coverColor       {               background-color: #C2A6BF !important;   }
    </style>
    <meta name=""maintenanceLevel"" content=""3""></meta>
    <meta name=""FeatureRequirement"" content=""[{&quot;BloomDesktopMinVersion&quot;:&quot;4.7&quot;,&quot;BloomPlayerMinVersion&quot;:&quot;1.0&quot;,&quot;FeatureId&quot;:&quot;wholeTextBoxAudioInXmatter&quot;,&quot;FeaturePhrase&quot;:&quot;Whole Text Box Audio in Front/Back Matter&quot;},{&quot;BloomDesktopMinVersion&quot;:&quot;4.4&quot;,&quot;BloomPlayerMinVersion&quot;:&quot;1.0&quot;,&quot;FeatureId&quot;:&quot;wholeTextBoxAudio&quot;,&quot;FeaturePhrase&quot;:&quot;Whole Text Box Audio&quot;}]""></meta>
    <link rel=""stylesheet"" href=""basePage.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""previewMode.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""origami.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""branding.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""Basic Book.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""Traditional-XMatter.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""defaultLangStyles.css"" type=""text/css""></link>
    <link rel=""stylesheet"" href=""customCollectionStyles.css"" type=""text/css""></link>
</head>

<body data-decodablestage=""0"" data-leveledreaderlevel=""0"" data-bookshelfurlkey="""" data-l1=""fr"" data-l2="""" data-l3="""">
    <div id=""bloomDataDiv"">
        <div data-book=""styleNumberSequence"" lang=""*"">
            0
        </div>

        <div data-book=""contentLanguage1"" lang=""*"">
            fr
        </div>

        <div data-book=""contentLanguage1Rtl"" lang=""*"">
            False
        </div>

        <div data-book=""languagesOfBook"" lang=""*"">
            French
        </div>

        <div data-book=""coverImage"" lang=""*"" src=""image-placeholder.png"" alt=""This picture, image-placeholder.png, is missing or was loading too slowly."">
            image-placeholder.png
        </div>

        {0}

        <div data-book=""originalTitle"" lang=""*"">
            audio test
        </div>

        <div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt=""""></img></div>

        <div data-book=""licenseUrl"" lang=""*"">
            http://creativecommons.org/licenses/by/4.0/
        </div>

        <div data-book=""licenseDescription"" lang=""fr"">
            http://creativecommons.org/licenses/by/4.0/<br />Cette création peut être utilisée à des fins commerciales. Cette création peut être adaptée ou complétée. Les mentions relatives aux droits d'auteur, d'illustrateur, etc. doivent être conservées.
        </div>

        <div data-book=""licenseImage"" lang=""*"">
            license.png
        </div>
        <div data-xmatter-page=""insideFrontCover"" data-page=""required singleton"" data-export=""front-matter-inside-front-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""titlePage"" data-page=""required singleton"" data-export=""front-matter-title-page"" data-page-number=""""></div>
        <div data-xmatter-page=""credits"" data-page=""required singleton"" data-export=""front-matter-credits"" data-page-number=""""></div>
        <div data-xmatter-page=""insideBackCover"" data-page=""required singleton"" data-export=""back-matter-inside-back-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""outsideBackCover"" data-page=""required singleton"" data-export=""back-matter-back-cover"" data-page-number=""""></div>
        <div data-xmatter-page=""frontCover"" data-page=""required singleton"" data-export=""front-matter-cover"" data-page-number=""""></div>
    </div>";

        // The end of a real Bloom book, everything after the last page.
        protected string EndOfFile =
            @"</body>
</html>";

        protected void Setup(string pagesContent, string titleOverride = null)
        {
            string title =
                titleOverride
                ?? @"<div data-book=""bookTitle"" lang=""fr"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow audio-sentence"" contenteditable=""true"" style=""padding-bottom: 0px;"" data-audiorecordingmode=""TextBox"" id=""i779b9abc-3d1b-4e74-926f-b99175396902"">
            <p>audio test</p>
        </div>";
            // Not using string.Format here because StartOfFile has other curly braces and we just need a simple fix.
            var dom = new HtmlDom(
                StartOfFile.Replace("{0}", title) + pagesContent + EndOfFile,
                true
            );
            _spreadsheetFolder = new TemporaryFolder("SpreadsheetImagesTests");
            _bookFolder = new TemporaryFolder("SpreadsheetImagesTests_Book");
            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("fr"))
                .Returns("French");

            // Each test subclass only needs some of these, so we could optimize...but one thing we want to verify is that unwanted ones are NOT copied.
            var testAudioPath = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                "src/BloomTests/Spreadsheet/testAudio/"
            );
            var bookAudioPath = Path.Combine(_bookFolder.FolderPath, "audio");
            Directory.CreateDirectory(bookAudioPath);
            foreach (var audioFilePath in Directory.EnumerateFiles(testAudioPath))
                RobustFile.Copy(
                    audioFilePath,
                    Path.Combine(bookAudioPath, Path.GetFileName(audioFilePath))
                );

            _exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);

            _progressSpy = new ProgressSpy();
            Sheet = _exporter.ExportToFolder(
                dom,
                _bookFolder.FolderPath,
                _spreadsheetFolder.FolderPath,
                out string outputPath,
                _progressSpy,
                OverwriteOptions.Overwrite
            );
            Rows = Sheet.ContentRows.ToList();
            AllRows = Sheet.AllRows();
            PageContentRows = Rows.Where(r =>
                    r.MetadataKey == InternalSpreadsheet.PageContentRowLabel
                )
                .ToList();
        }

        protected void TearDown()
        {
            _spreadsheetFolder?.Dispose();
            _bookFolder?.Dispose();
        }
    }

    public class Spreadsheet_BlockNotSplit_Tests : SpreadsheetAudioTestsBase
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // a page from a real book where audio was recorded in text mode and not split.
            var page =
                @"    <div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""26383607-a382-4201-bd09-ba32d177a594"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""3"" lang="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"">
            Basic Text &amp; Picture
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-canvas bloom-leadingElement"" title=""Name: aor_PMF30.png Size: 30.43 kb Dots: 1500 x 1441 For the current paper size: • The image container is 408 x 337 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ✓ This image would print at 410 DPI. • An image with 1275 x 1054 dots would fill this container at 300 DPI.""><img src=""aor_PMF30.png"" alt="""" data-copyright=""Copyright SIL International 2009"" data-creator="""" data-license=""cc-by-sa""></img></div>
                    </div>
                </div>

                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 50%""></div>

                <div class=""split-pane-component position-bottom"" style=""height: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style audio-sentence bloom-content1 bloom-visibility-code-on"" lang=""fr"" contenteditable=""true"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""i9c7f4e02-4685-48fc-8653-71d88f218706"" recordingmd5=""c93312969c38f815f4c9057f94bec2ab"" data-duration=""3.996735"" data-languagetipcontent=""français"">
                                <p>Man touching nose. He's using a finger.</p>
                            </div>

                            <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"" style="""">
                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"">
                                <p></p>
                            </div>
                        </div>
						<!-- very similar, but the audio will not be found -->
						<div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style audio-sentence bloom-content1 bloom-visibility-code-on"" lang=""fr"" contenteditable=""true"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""someMissingFile"" recordingmd5=""c93312969c38f815f4c9057f94bec2ab"" data-duration=""3.996735"" data-languagetipcontent=""français"">
                                <p>Man touching nose. He's using a finger.</p>
                            </div>

                            <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"" style="""">
                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"">
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>";
            Setup(page);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            TearDown();
        }

        // Somewhere there should be a test that we don't get audio columns if there is NO audio,
        // but I don't want to put another whole export operation into audio tests just
        // for that, so it's in SpreadsheetImagesTests (HasNoAudioColumns).

        [Test]
        public void AddsAudioColumns()
        {
            Assert.That(AllRows.First().CellContents, Has.Member("[audio fr]"));
            Assert.That(AllRows.Skip(1).First().CellContents, Has.Member("French audio"));
            Assert.That(AllRows.First().CellContents, Has.Member("[audio alignments fr]"));
            Assert.That(AllRows.Skip(1).First().CellContents, Has.Member("French alignments"));
        }

        [Test]
        public void SavesDuration()
        {
            var alignmentColIndex = AllRows.First().CellContents.IndexOf("[audio alignments fr]");
            var frIndex = AllRows.First().CellContents.IndexOf("[fr]");
            // sanity check that we got the right row.
            Assert.That(
                PageContentRows[0].GetCell(frIndex).Content,
                Does.Contain("Man touching nose. He's using a finger.")
            );
            // CultureInfo - make test robust enough to run if in English(Sweden) region.
            Assert.That(
                double.Parse(
                    PageContentRows[0].GetCell(alignmentColIndex).Content,
                    CultureInfo.InvariantCulture
                ),
                Is.EqualTo(3.996735).Within(0.001)
            );
        }

        [Test]
        public void SavesFilePath()
        {
            var audioColIndex = AllRows.First().CellContents.IndexOf("[audio fr]");
            Assert.That(
                PageContentRows[0].GetCell(audioColIndex).Content,
                Is.EqualTo("./audio/i9c7f4e02-4685-48fc-8653-71d88f218706.mp3")
            );
        }

        [Test]
        public void CopiesAudioFile()
        {
            var expectedAudioPath = Path.Combine(
                _spreadsheetFolder.FolderPath,
                "audio/i9c7f4e02-4685-48fc-8653-71d88f218706.mp3"
            );
            Assert.That(
                RobustFile.Exists(expectedAudioPath),
                Is.True,
                "audio file should be copied to output"
            );
            Assert.That(
                Directory
                    .EnumerateFiles(Path.Combine(_spreadsheetFolder.FolderPath, "audio"))
                    .Count(),
                Is.EqualTo(1)
            );
        }
    }

    public class Spreadsheet_BlockSplit_Tests : SpreadsheetAudioTestsBase
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // a page from a real book where audio was recorded in text mode and split.
            var page =
                @"<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-left bloom-monolingual"" data-page="""" id=""b658affd-69c8-4308-951c-ed7c8e6b99b2"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""2"" lang="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"">
            Basic Text &amp; Picture
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-canvas bloom-leadingElement"" title=""Name: aor_CMB362.png Size: 13.51 kb Dots: 706 x 525 For the current paper size: • The image container is 408 x 337 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 166 DPI. • An image with 1275 x 1054 dots would fill this container at 300 DPI.""><img src=""aor_CMB362.png"" alt="""" data-copyright=""Copyright SIL International 2009"" data-creator="""" data-license=""cc-by-sa""></img></div>
                    </div>
                </div>

                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 50%""></div>

                <div class=""split-pane-component position-bottom"" style=""height: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style audio-sentence bloom-postAudioSplit bloom-content1 bloom-visibility-code-on"" lang=""fr"" contenteditable=""true"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" id=""a9d7b794-7a83-473a-8307-7968176ae4bc"" recordingmd5=""69f7226e062f6b4accaa8110787f81fb"" data-duration=""4.388571"" data-audiorecordingendtimes=""3.900 4.360"" data-languagetipcontent=""français"">
                                <p><span id=""i4356108a-4647-4b01-8cb9-8acc227b4eea"" class=""bloom-highlightSegment"">A hand with a sore finger.</span> <span id=""cf66ff0d-f882-48b0-b789-cba83aa3ae84"" class=""bloom-highlightSegment"">It has a bandage.​</span></p>
							</div>

                            <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"">
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>";
            Setup(page);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            TearDown();
        }

        [Test]
        public void AddsAudioColumns()
        {
            Assert.That(AllRows.First().CellContents, Has.Member("[audio fr]"));
            Assert.That(AllRows.Skip(1).First().CellContents, Has.Member("French audio"));
            Assert.That(AllRows.First().CellContents, Has.Member("[audio alignments fr]"));
            Assert.That(AllRows.Skip(1).First().CellContents, Has.Member("French alignments"));
        }

        [Test]
        public void SavesAudioRecordingEndTimes()
        {
            var alignmentColIndex = AllRows.First().CellContents.IndexOf("[audio alignments fr]");
            var frIndex = AllRows.First().CellContents.IndexOf("[fr]");
            // sanity check that we got the right row.
            Assert.That(
                PageContentRows[0].GetCell(frIndex).Content,
                Does.Contain("A hand with a sore finger")
            );
            var alignment = PageContentRows[0].GetCell(alignmentColIndex).Content;
            Assert.That(alignment, Does.StartWith("3.900 4"));
            // CultureInfo - make test robust enough to run if in English(Sweden) region.
            Assert.That(
                double.Parse(alignment.Split(' ').Last(), CultureInfo.InvariantCulture),
                Is.EqualTo(4.360).Within(0.0001)
            );
        }

        [Test]
        public void SavesFilePath()
        {
            var audioColIndex = AllRows.First().CellContents.IndexOf("[audio fr]");
            Assert.That(
                PageContentRows[0].GetCell(audioColIndex).Content,
                Is.EqualTo("./audio/a9d7b794-7a83-473a-8307-7968176ae4bc.mp3")
            );
        }

        [Test]
        public void CopiesAudioFile()
        {
            var expectedAudioPath = Path.Combine(
                _spreadsheetFolder.FolderPath,
                "audio/a9d7b794-7a83-473a-8307-7968176ae4bc.mp3"
            );
            Assert.That(
                RobustFile.Exists(expectedAudioPath),
                Is.True,
                "audio file should be copied to output"
            );
            Assert.That(
                Directory
                    .EnumerateFiles(Path.Combine(_spreadsheetFolder.FolderPath, "audio"))
                    .Count(),
                Is.EqualTo(1)
            );
        }
    }

    public class Spreadsheet_SentenceAudio_Tests : SpreadsheetAudioTestsBase
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // A page from a real book recorded in sentence mode (and slightly edited)
            var page =
                @"<div class=""bloom-page numberedPage customPage bloom-combinedPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""b85c96d3-7207-47c1-9ed7-36fec82aa59b"" data-pagelineage=""adcd48df-e9ab-4a07-afd4-6a24d0398382"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Basic Text &amp; Picture"">
            Basic Text &amp; Picture
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-canvas bloom-leadingElement"" title=""Name: aor_HHG-9.png Size: 25.28 kb Dots: 619 x 772 For the current paper size: • The image container is 408 x 337 dots. • For print publications, you want between 300-600 DPI (Dots Per Inch). ⚠ This image would print at 220 DPI. • An image with 1275 x 1054 dots would fill this container at 300 DPI.""><img src=""aor_HHG-9.png"" alt="""" data-copyright=""Copyright SIL International 2009"" data-creator="""" data-license=""cc-by-sa""></img></div>
                    </div>
                </div>

                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 50%""></div>

                <div class=""split-pane-component position-bottom"" style=""height: 50%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" lang=""fr"" contenteditable=""true"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""Sentence"" data-languagetipcontent=""français"">
                                <p><span id=""i991ae1ac-db9a-4ad1-984b-8e679c1ae901"" class=""audio-sentence"" recordingmd5=""f766dd9023afe0d86acf6330e99261b3"" data-duration=""2.899592"">A boy sitting on a stone.</span> <span id=""i77c18c83-0224-405f-bb97-70d32078855c"" class=""audio-sentence"" recordingmd5=""0f433ef9ef7693f968a8559e0210c57e"" data-duration=""2.194286"">His foot is wet.</span></p>
                            </div>
							<!-- This tests handling of a missing and a duplicate ID -->
							<div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" lang=""qaa"" contenteditable=""true"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""Sentence"" data-languagetipcontent=""français"">
                                <p><span id=""not recorded yet"" class=""audio-sentence"" recordingmd5=""f766dd9023afe0d86acf6330e99261b3"" data-duration=""2.899592"">A boy sitting on a stone.</span> <span id=""i77c18c83-0224-405f-bb97-70d32078855c"" class=""audio-sentence"" recordingmd5=""0f433ef9ef7693f968a8559e0210c57e"" data-duration=""2.194286"">His foot is wet.</span></p>
                            </div>

                            <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"">
                                <p></p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>";
            Setup(page);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            TearDown();
        }

        [Test]
        public void AddsAudioColumnsButNoAlignments()
        {
            Assert.That(AllRows.First().CellContents, Has.Member("[audio fr]"));
            Assert.That(AllRows.Skip(1).First().CellContents, Has.Member("French audio"));
            Assert.That(AllRows.First().CellContents, Has.None.Matches(".*audio alignments.*"));
            Assert.That(AllRows.Skip(1).First().CellContents, Has.None.Matches(".*alignments.*"));
        }

        [Test]
        public void SavesFilePaths()
        {
            var audioColIndex = AllRows.First().CellContents.IndexOf("[audio fr]");
            Assert.That(
                PageContentRows[0].GetCell(audioColIndex).Content,
                Is.EqualTo(
                    "./audio/i991ae1ac-db9a-4ad1-984b-8e679c1ae901.mp3, ./audio/i77c18c83-0224-405f-bb97-70d32078855c.mp3"
                )
            );
        }

        [Test]
        public void CopiesAudioFiles()
        {
            var expectedAudioPath = Path.Combine(
                _spreadsheetFolder.FolderPath,
                "audio/i991ae1ac-db9a-4ad1-984b-8e679c1ae901.mp3"
            );
            Assert.That(
                RobustFile.Exists(expectedAudioPath),
                Is.True,
                "audio file should be copied to output"
            );
            expectedAudioPath = Path.Combine(
                _spreadsheetFolder.FolderPath,
                "audio/i77c18c83-0224-405f-bb97-70d32078855c.mp3"
            );
            Assert.That(
                RobustFile.Exists(expectedAudioPath),
                Is.True,
                "audio file should be copied to output"
            );
            Assert.That(
                Directory
                    .EnumerateFiles(Path.Combine(_spreadsheetFolder.FolderPath, "audio"))
                    .Count(),
                Is.EqualTo(2)
            );
        }

        [Test]
        public void GotMissingWhenNoFile()
        {
            var audioColIndex = AllRows.First().CellContents.IndexOf("[audio qaa]");
            Assert.That(
                PageContentRows[0].GetCell(audioColIndex).Content,
                Is.EqualTo("missing, ./audio/i77c18c83-0224-405f-bb97-70d32078855c.mp3")
            );
        }
    }

    public class Spreadsheet_DataDivAudio_Tests : SpreadsheetAudioTestsBase
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // Some slightly modified data-div data from a real book
            Setup(
                "",
                @"
		<div data-book=""bookTitle"" lang=""fr"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow audio-sentence"" contenteditable=""true"" style=""padding-bottom: 0px;"" data-audiorecordingmode=""TextBox"" id=""i9c7f4e02-4685-48fc-8653-71d88f218706"" recordingmd5=""8734124ad9e817bc544a1d8b7cd6bc3c"" data-duration=""3.996735"">
            <p>audio test</p>
        </div>
		<div data-book=""bookTitle"" lang=""en"" class="" bloom-editable bloom-nodefaultstylerule bloom-padForOverflow audio-sentence bloom-postAudioSplit"" contenteditable=""true"" style=""padding-bottom: 2px;"" data-audiorecordingmode=""TextBox"" id=""a9d7b794-7a83-473a-8307-7968176ae4bc"" recordingmd5=""dd88f01a349b69e23df8b3d91ca0b6fa"" data-duration=""4.388571"" data-audiorecordingendtimes=""4.360"">
            <p><span id=""i7da5dfe6-78a2-446e-803c-45a41472848b"" class=""bloom-highlightSegment"">English audio test</span></p>
        </div>"
            );
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            TearDown();
        }

        /// <summary>
        /// This is not a very exhaustive test of all the things that audio export should do.
        /// But it's enough to confirm that otherwise-tested code is being called.
        /// </summary>
        /// <param name="lang"></param>
        /// <param name="expectedId"></param>
        /// <param name="expectedDuration"></param>
        [TestCase("fr", "i9c7f4e02-4685-48fc-8653-71d88f218706", "3.996735")]
        [TestCase("en", "a9d7b794-7a83-473a-8307-7968176ae4bc", "4.360")]
        public void DataDivHasTitleAudio(string lang, string expectedId, string expectedDuration)
        {
            var row = AllRows.First(r => r.MetadataKey == InternalSpreadsheet.BookTitleRowLabel);
            Assert.That(AllRows.First().CellContents, Has.Member($"[audio {lang}]"));
            var frAudioIndex = AllRows.First().CellContents.IndexOf($"[audio {lang}]");
            Assert.That(
                row.CellContents.ToArray()[frAudioIndex],
                Is.EqualTo("./audio/" + expectedId + ".mp3")
            );
            var frAudioAlignmentIndex = AllRows
                .First()
                .CellContents.IndexOf($"[audio alignments {lang}]");
            Assert.That(
                row.CellContents.ToArray()[frAudioAlignmentIndex],
                Is.EqualTo(expectedDuration)
            );
        }
    }

    public class SpreadsheetAudioSegmentMarkerTests
    {
        static SpreadsheetAudioSegmentMarkerTests()
        {
            // The package requires us to do this as a way of acknowledging that we
            // accept the terms of the NonCommercial license.
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        private const string testBook =
            @"
<!DOCTYPE html>

<html>
<head>
</head>
<body data-l1=""es"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
		<div data-book=""bookTitle"" lang=""en"" id=""idShouldGetKept"">
			<p><em>Pineapple</em></p>

            <p>Farm</p>

		</div>
        <div data-book=""topic"" lang=""en"">
            Health
		</div>
		<div data-book=""coverImage"" lang=""*"" src=""cover.png"" alt=""This picture, cover.png, is missing or was loading too slowly."">
			cover.png
		</div>
		<div data-book=""licenseImage"" lang= ""*"" >
			license.png
		</div>
		<div data-book=""outside-back-cover-branding-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
	</div>




    <div class=""bloom-page numberedPage customPage A5Portrait side-right bloom-monolingual"" data-page="""" id=""f3ee3ee0-9ce8-4122-a426-01bfbef98a5e"" data-pagelineage=""a31c38d8-c1cb-4eb9-951b-d2840f6a8bdb"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Just Text"">
            Just Text
        </div>

        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane-component-inner"">
                <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                    <div class=""bloom-editable normal-style audio-sentence bloom-postAudioSplit bloom-content1 bloom-contentNational1 bloom-visibility-code-on"" id=""audio-test"" lang=""en"" contenteditable=""true"" data-languagetipcontent=""English"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""false"" role=""textbox"" aria-label=""false"" data-audiorecordingmode=""TextBox"" recordingmd5=""4047597f4107c0c810bd57adc8f00adf"" data-duration=""3.892245"" data-audiorecordingendtimes=""2.420 3.840"">
                        <p><span id=""i34299c13-8162-43ca-9a26-f2defd502951"" class=""bloom-highlightSegment"">There is a segment marker <span class=""bloom-audio-split-marker""></span></span><span id=""cdb6909e-0a60-4302-a114-836bd7f4d6f8"" class=""bloom-highlightSegment"">in this sentence.</span></p>
                    </div>

                    <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"" style="""">
                        <p></p>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>
";

        private TempFile _spreadsheetFile;
        private ExcelWorksheet _worksheet;
        private ExcelPackage _excelPackage;

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            var origDom = new HtmlDom(testBook, true);

            AssertThatXmlIn
                .Dom(origDom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div[@id='audio-test']", 1);
            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");
            var exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);
            exporter.Params = new SpreadsheetExportParams();
            var sheetFromExport = exporter.Export(origDom, "fakeImagesFolderpath");
            _spreadsheetFile = TempFile.WithExtension("xslx");
            sheetFromExport.WriteToFile(_spreadsheetFile.Path);
            var info = new FileInfo(_spreadsheetFile.Path);
            _excelPackage = new ExcelPackage(info);
            _worksheet = _excelPackage.Workbook.Worksheets[0];
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _spreadsheetFile.Dispose();
            _excelPackage.Dispose();
        }

        [Test]
        public void AudioSegmentMarkersInSpreadsheet()
        {
            var rowCount = _worksheet.Dimension.Rows;
            var colCount = _worksheet.Dimension.Columns;
            for (var c = 0; c < colCount; c++)
            {
                var header = _worksheet.Cells[1, c + 1];
                // Find the english column
                if (header.Value != null && header.Value.ToString().Contains("[en]"))
                {
                    // Look for a cell with the desired text with audio segment marker in it
                    for (var r = 0; r < rowCount; r++)
                    {
                        var cell = _worksheet.Cells[r + 1, c + 1];
                        if (
                            cell.Value != null
                            && cell.Value.ToString()
                                .Contains("There is a segment marker |in this sentence.")
                        )
                        {
                            return;
                        }
                    }
                }
            }
            Assert.Fail("Could not find the text with audio segment marker in the spreadsheet.");
        }
    }

    public class SpreadsheetAudioSegmentMarkerConversionTests
    {
        // Audio segment markers should get converted to | on export so that they get retained in spreadsheet
        [Test]
        public void ParseXmlConvertsSegmentMarkers()
        {
            string xmlString =
                "<p>I have put a segment marker <span class=\"bloom-audio-split-marker\"></span>in the middle of this sentences</p>";
            MarkedUpText parsed = MarkedUpText.ParseXml(xmlString);
            Assert.That(parsed.Count, Is.EqualTo(3));
            Assert.That(parsed.GetRun(0).Text, Is.EqualTo("I have put a segment marker "));
            Assert.That(parsed.GetRun(1).Text, Is.EqualTo("|"));
            Assert.That(parsed.GetRun(2).Text, Is.EqualTo("in the middle of this sentences"));
        }
    }
}
