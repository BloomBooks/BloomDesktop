using System;
using System.Collections.Generic;
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
using SIL.IO;

namespace BloomTests.Spreadsheet
{
    public class SpreadsheetExportVideosAndWidgetsTests
    {
        public const string videoAndWidgetBook =
            @"

<html>
<head>
</head>

<body data-l1=""en"" data-l2="""" data-l3="""">
	<div id=""bloomDataDiv"">
        <div data-book=""outside-back-cover-bottom-html"" lang=""*""><img class=""branding"" src=""BloomWithTaglineAgainstLight.svg"" alt="""" data-copyright="""" data-creator="""" data-license=""""></img></div>
	</div>
    <div class=""bloom-page numberedPage customPage bloom-combinedPage side-right Device16x9Landscape bloom-monolingual"" data-page="""" id=""958a4d5c-053a-456d-996d-5fe779397ed5"" data-tool-id=""signLanguage"" data-pagelineage=""08422e7b-9406-4d11-8c71-02005b1b8095"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Big Text Diglot"">
            Big Text Diglot
        </div>
        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""split-pane horizontal-percent"" style=""min-height: 42px;"">
                <div class=""split-pane-component position-top"" style=""bottom: 33.9339%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""split-pane vertical-percent"" style=""min-width: 0px;"">
                            <div class=""split-pane-component position-left"">
                                <div class=""split-pane-component-inner"" min-height=""60px 150px 250px"" min-width=""60px 150px"" style=""position: relative;"">
									<div class=""bloom-widgetContainer bloom-leadingElement"">
					                    <iframe src=""activities/balldragTouch/sub/index.html"">Must have a closing tag in HTML</iframe>
					                </div>
								</div>
                            </div>
                            <div class=""split-pane-divider vertical-divider""></div>

                            <div class=""split-pane-component position-right"">
                                <div class=""split-pane-component-inner"" min-height=""60px 150px 250px"" min-width=""60px 150px"" style=""position: relative;"">
                                    <div class=""bloom-videoContainer bloom-leadingElement bloom-selected"">
                                        <video>
                                        <source src=""video/ac2e237a-140f-45e4-b3e8-257dd12f4793.mp4""></source></video>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                <div class=""split-pane-divider horizontal-divider"" style=""bottom: 33.9339%"" title=""CTRL for precision. Double click to match previous page."" data-splitter-label=""66%""></div>

                <div class=""split-pane-component position-bottom"" style=""height: 33.9339%"">
                    <div class=""split-pane-component-inner"">
                        <div class=""bloom-translationGroup bloom-trailingElement"" data-default-languages=""auto"">
                            <div class=""bloom-editable normal-style"" lang=""z"" contenteditable=""true"" style="""">
                                <p></p>
                            </div>

                            <div class=""bloom-editable normal-style bloom-content1 bloom-visibility-code-on"" lang=""en"" contenteditable=""true"" style=""min-height: 24px;"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"" data-languagetipcontent=""English"">
                                <p>A castle with a very big flag</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
	<div class=""bloom-page simple-comprehension-quiz bloom-ignoreForReaderStats bloom-interactive-page numberedPage side-right A5Portrait bloom-monolingual"" data-feature=""game"" id=""ca363e76-9474-4e54-bd99-c3f554b67784"" data-page="""" data-analyticscategories=""comprehension"" data-reader-version=""2"" data-pagelineage=""F125A8B6-EA15-4FB7-9F8D-271D7B3C8D4D"" data-page-number=""1"" lang="""">
        <div class=""pageLabel"" lang=""en"" data-i18n=""TemplateBooks.PageLabel.Quiz Page"">
            Quiz Page
        </div>
        <div class=""pageDescription"" lang=""en""></div>

        <div class=""marginBox"">
            <div class=""quiz"">
                <div class=""bloom-translationGroup bloom-ignoreChildrenForBookLanguageList"" data-default-languages=""auto"" data-hasqtip=""true"" aria-describedby=""qtip-0"">
                    <div class=""bloom-editable QuizHeader-style bloom-content1 bloom-visibility-code-on"" lang=""akl"" contenteditable=""true"" data-collection=""simpleComprehensionQuizHeading"" data-languagetipcontent=""Aklanon"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                        <p>Check your understanding</p>
                    </div>

                    <div class=""bloom-editable QuizHeader-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" data-collection=""simpleComprehensionQuizHeading"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                        <p>Check Your Understanding</p>
                    </div>
                </div>

                <div class=""bloom-translationGroup"" data-default-languages=""auto"" data-hint=""Put a comprehension question here"" data-hasqtip=""true"" aria-describedby=""qtip-1"">
                    <div class=""bloom-editable QuizQuestion-style"" lang=""z"" contenteditable=""true"" style="""">
                        <p></p>
                    </div>

                    <div class=""bloom-editable QuizQuestion-style bloom-content1 bloom-visibility-code-on"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                        <p>What does this book say?</p>
                    </div>

                    <div class=""bloom-editable QuizQuestion-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                        <p></p>
                    </div>
                </div>

                <div class=""checkbox-and-textbox-choice"">
                    <input class=""styled-check-box"" type=""checkbox"" name=""Correct""></input>

                    <div class=""bloom-translationGroup"" data-default-languages=""auto"" data-hint=""Put a possible answer here. Check it if it is correct."" data-hasqtip=""true"" aria-describedby=""qtip-2"">
                        <div class=""bloom-editable QuizAnswer-style"" lang=""z"" contenteditable=""true"" style="""">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-content1 bloom-visibility-code-on"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p>Lots and lots</p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>
                    </div>
                    <div class=""placeToPutVariableCircle""></div>
                </div>

                <div class=""checkbox-and-textbox-choice correct-answer"">
                    <input class=""styled-check-box"" type=""checkbox"" name=""Correct""></input>

                    <div class=""bloom-translationGroup"" data-default-languages=""auto"" data-hint=""Put a possible answer here. Check it if it is correct."" data-hasqtip=""true"" aria-describedby=""qtip-3"">
                        <div class=""bloom-editable QuizAnswer-style"" lang=""z"" contenteditable=""true"" style="""">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-content1 bloom-visibility-code-on"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p>Nothing at all.</p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>
                    </div>
                    <div class=""placeToPutVariableCircle""></div>
                </div>

                <div class=""checkbox-and-textbox-choice correct-answer"">
                    <input class=""styled-check-box"" type=""checkbox"" name=""Correct""></input>

                    <div class=""bloom-translationGroup"" data-default-languages=""auto"" data-hint=""Put a possible answer here. Check it if it is correct."" data-hasqtip=""true"" aria-describedby=""qtip-4"">
                        <div class=""bloom-editable QuizAnswer-style"" lang=""z"" contenteditable=""true"" style="""">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-content1 bloom-visibility-code-on"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p>You forgot the content!</p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>
                    </div>
                    <div class=""placeToPutVariableCircle""></div>
                </div>

                <div class=""checkbox-and-textbox-choice empty"">
                    <input class=""styled-check-box"" type=""checkbox"" name=""Correct""></input>

                    <div class=""bloom-translationGroup"" data-default-languages=""auto"" data-hint=""Put a possible answer here. Check it if it is correct."" data-hasqtip=""true"">
                        <div class=""bloom-editable QuizAnswer-style"" lang=""z"" contenteditable=""true"" style="""">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-content1 bloom-visibility-code-on"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>
                    </div>
                    <div class=""placeToPutVariableCircle""></div>
                </div>

                <div class=""checkbox-and-textbox-choice empty"">
                    <input class=""styled-check-box"" type=""checkbox"" name=""Correct""></input>

                    <div class=""bloom-translationGroup"" data-default-languages=""auto"" data-hint=""Put a possible answer here. Check it if it is correct."" data-hasqtip=""true"" aria-describedby=""qtip-6"">
                        <div class=""bloom-editable QuizAnswer-style"" lang=""z"" contenteditable=""true"" style="""">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-content1 bloom-visibility-code-on"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>
                    </div>
                    <div class=""placeToPutVariableCircle""></div>
                </div>

                <div class=""checkbox-and-textbox-choice empty"">
                    <input class=""styled-check-box"" type=""checkbox"" name=""Correct""></input>

                    <div class=""bloom-translationGroup"" data-default-languages=""auto"" data-hint=""Put a possible answer here. Check it if it is correct."" data-hasqtip=""true"" aria-describedby=""qtip-7"">
                        <div class=""bloom-editable QuizAnswer-style"" lang=""z"" contenteditable=""true"" style="""">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-content1 bloom-visibility-code-on"" lang=""akl"" contenteditable=""true"" data-languagetipcontent=""Aklanon"" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>

                        <div class=""bloom-editable QuizAnswer-style bloom-contentNational1"" lang=""en"" contenteditable=""true"" data-languagetipcontent=""English"" style="""" tabindex=""0"" spellcheck=""true"" role=""textbox"" aria-label=""false"">
                            <p></p>
                        </div>
                    </div>
                    <div class=""placeToPutVariableCircle""></div>
                </div><script src=""simpleComprehensionQuiz.js""></script>
            </div>
        </div>
    </div>
</body>
</html>
";
        private SpreadsheetExporter _exporter;
        private InternalSpreadsheet _sheet;
        private List<ContentRow> _rows;
        private List<ContentRow> _pageContentRows;

        private TemporaryFolder _spreadsheetFolder;
        private TemporaryFolder _bookFolder;
        private ProgressSpy _progressSpy;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var dom = new HtmlDom(videoAndWidgetBook, true);

            _spreadsheetFolder = new TemporaryFolder("SpreadsheetVideoWidgetsTests");
            _bookFolder = new TemporaryFolder("SpreadsheetVideoWidgetsTests_Book");

            var mockLangDisplayNameResolver = new Mock<ILanguageDisplayNameResolver>();
            mockLangDisplayNameResolver
                .Setup(x => x.GetLanguageDisplayName("en"))
                .Returns("English");

            _exporter = new SpreadsheetExporter(mockLangDisplayNameResolver.Object);

            // We need all these files in place so we can verify that all of them get copied
            var videoFolder = Path.Combine(_bookFolder.FolderPath, "video");
            Directory.CreateDirectory(videoFolder);
            RobustFile.WriteAllText(
                Path.Combine(videoFolder, "ac2e237a-140f-45e4-b3e8-257dd12f4793.mp4"),
                "this is a fake video"
            );

            var htmlPath = Path.Combine(
                _bookFolder.FolderPath,
                "activities",
                "balldragTouch",
                "sub",
                "index.html"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(htmlPath));
            RobustFile.WriteAllText(htmlPath, "<html><body>This is a fake</body></html>");
            RobustFile.WriteAllText(
                Path.Combine(_bookFolder.FolderPath, "activities", "balldragTouch", "junk.png"),
                "This is a fake image"
            );

            _progressSpy = new ProgressSpy();
            _sheet = _exporter.ExportToFolder(
                dom,
                _bookFolder.FolderPath,
                _spreadsheetFolder.FolderPath,
                out string outputPath,
                _progressSpy,
                OverwriteOptions.Overwrite
            );
            _rows = _sheet.ContentRows.ToList();
            _pageContentRows = _rows
                .Where(r => r.MetadataKey == InternalSpreadsheet.PageContentRowLabel)
                .ToList();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _spreadsheetFolder?.Dispose();
            _bookFolder?.Dispose();
        }

        [Test]
        public void CopiesVideoToDestFolder()
        {
            var destVideoFolder = Path.Combine(_spreadsheetFolder.FolderPath, "video");
            Assert.That(Directory.Exists(destVideoFolder));
            Assert.That(
                File.Exists(
                    Path.Combine(destVideoFolder, "ac2e237a-140f-45e4-b3e8-257dd12f4793.mp4")
                )
            );
        }

        [Test]
        public void CopiesWidgetFilesToDestFolder()
        {
            var destWidgetFolder = Path.Combine(_spreadsheetFolder.FolderPath, "activities");
            Assert.That(Directory.Exists(destWidgetFolder));
            Assert.That(
                File.Exists(Path.Combine(destWidgetFolder, "balldragTouch", "sub", "index.html"))
            );
            Assert.That(File.Exists(Path.Combine(destWidgetFolder, "balldragTouch", "junk.png")));
        }

        [Test]
        public void PutsTextWithVideoAndWidget()
        {
            Assert.That(
                _pageContentRows[0].GetCell("[en]").Text,
                Is.EqualTo("A castle with a very big flag")
            );
        }

        [Test]
        public void MarksCorrectQuizAnswer()
        {
            var attributeIndex = _sheet.GetColumnForTag(InternalSpreadsheet.AttributeColumnLabel);
            Assert.That(attributeIndex >= 0);
            Assert.That(
                _pageContentRows[4].GetCell(attributeIndex).Content,
                Is.EqualTo("../class=correct-answer")
            );
        }

        [Test]
        public void AddsPageTypes()
        {
            var pageTypeIndex = _sheet.GetColumnForTag(InternalSpreadsheet.PageTypeColumnLabel);

            Assert.That(
                _pageContentRows[0].GetCell(pageTypeIndex).Content,
                Is.EqualTo("Big Text Diglot")
            );
            Assert.That(
                _pageContentRows[1].GetCell(pageTypeIndex).Content,
                Is.EqualTo("Quiz Page")
            );
            Assert.That(_pageContentRows[2].GetCell(pageTypeIndex).Content, Is.EqualTo(""));
            Assert.That(_pageContentRows[3].GetCell(pageTypeIndex).Content, Is.EqualTo(""));
            Assert.That(_pageContentRows[4].GetCell(pageTypeIndex).Content, Is.EqualTo(""));
        }
    }
}
