using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// <summary>
    /// Tests various cases of importing SS data that contains video and various kinds of activities.
    /// This also covers (along with SpreadsheetImageAndTextImportTests) various cases of how to
    /// import when page type is specified.
    /// </summary>
    public class SpreadsheetPageTypeImportTests
    {
        private HtmlDom _dom;
        private string _imagesFolder;
        private ProgressSpy _progressSpy;

        private List<SafeXmlElement> _contentPages;

        private List<string> _warnings;
        private TemporaryFolder _bookFolder;
        private TemporaryFolder _ssFolder;

        ContentRow CreateTextRow(InternalSpreadsheet ss, int columnForEn, string content)
        {
            var row = new ContentRow(ss);
            row.AddCell(InternalSpreadsheet.PageContentRowLabel);
            row.SetCell(columnForEn, content);
            return row;
        }

        [OneTimeSetUp]
        public async Task OneTimeSetUp()
        {
            // We will re-use the images from another test.
            // Conveniently the images are in a folder called "images" which is what the importer expects.
            // So we give it the parent directory of that images folder.
            _imagesFolder = SIL.IO.FileLocationUtilities.GetDirectoryDistributedWithApplication(
                "src/BloomTests/ImageProcessing/images"
            );
            _ssFolder = new TemporaryFolder("SpreadsheetPageTypeImportTests_ss");
            var whereToPutImages = Path.Combine(_ssFolder.FolderPath, "images");
            Directory.CreateDirectory(whereToPutImages);
            var whereToPutVideo = Path.Combine(_ssFolder.FolderPath, "video");
            Directory.CreateDirectory(whereToPutVideo);
            var whereToPutActivities = Path.Combine(_ssFolder.FolderPath, "activities");
            Directory.CreateDirectory(whereToPutActivities);

            StringBuilder pageText = new StringBuilder(
                SpreadsheetImageAndTextImportTests.coverPage
            );

            var ss = new InternalSpreadsheet();
            var columnForEn = ss.AddColumnForLang("en", "English");
            var columnForImage = ss.GetColumnForTag(InternalSpreadsheet.ImageSourceColumnLabel);
            var columnForVideo = ss.AddColumnForTag(
                InternalSpreadsheet.VideoSourceColumnLabel,
                "Video"
            );
            var columnForActivities = ss.AddColumnForTag(
                InternalSpreadsheet.WidgetSourceColumnLabel,
                "Activity"
            );
            var columnForPageType = ss.AddColumnForTag(
                InternalSpreadsheet.PageTypeColumnLabel,
                "Page type"
            );
            var columnForAttributeData = ss.AddColumnForTag(
                InternalSpreadsheet.AttributeColumnLabel,
                "Attribute data"
            );

            // Test1. A row with one of everything will import into a template page with plenty of space.
            var contentRow1 = new ContentRow(ss);
            contentRow1.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow1.SetCell(columnForEn, "this is block 1 on page 1");
            contentRow1.SetCell(columnForImage, "images/lady24b.png");
            RobustFile.Copy(
                Path.Combine(_imagesFolder, "lady24b.png"),
                Path.Combine(whereToPutImages, "lady24b.png")
            );
            RobustFile.WriteAllText(
                Path.Combine(whereToPutVideo, "fakeVideo.mp4"),
                "This is just a fake"
            );
            contentRow1.SetCell(columnForVideo, "video/fakeVideo.mp4");
            var activity1Folder = Path.Combine(whereToPutActivities, "drag ball");
            Directory.CreateDirectory(activity1Folder);
            RobustFile.WriteAllText(
                Path.Combine(activity1Folder, "drag.html"),
                "This is just a fake HTML file"
            );
            var activity1Subfolder = Path.Combine(activity1Folder, "sub");
            Directory.CreateDirectory(activity1Subfolder);
            RobustFile.WriteAllText(
                Path.Combine(activity1Subfolder, "nonsense.jpg"),
                "This is just a fake JPG file"
            );
            contentRow1.SetCell(columnForActivities, "activities/drag ball/drag.html");
            pageText.Append(SpreadsheetImageAndTextImportTests.PageWith2OfEverything(1, 1));

            // Test row2. Another row with one of everything should fit into the same destination page.
            // Also verifies that a widget root file can be in a subdirectory.
            var contentRow2 = new ContentRow(ss);
            contentRow2.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow2.SetCell(columnForEn, "this is block 2 on page 1");
            contentRow2.SetCell(columnForImage, "images/LakePendOreille.jpg");
            RobustFile.Copy(
                Path.Combine(_imagesFolder, "LakePendOreille.jpg"),
                Path.Combine(whereToPutImages, "LakePendOreille.jpg")
            );
            RobustFile.WriteAllText(
                Path.Combine(whereToPutVideo, "anotherVideo.mp4"),
                "This is just a fake"
            );
            contentRow2.SetCell(columnForVideo, "video/anotherVideo.mp4");
            var activity2Folder = Path.Combine(whereToPutActivities, "extra", "mygame");
            Directory.CreateDirectory(activity2Folder);
            RobustFile.WriteAllText(
                Path.Combine(activity2Folder, "mygame.html"),
                "This is just a fake HTML file"
            );
            contentRow2.SetCell(columnForActivities, "activities/extra/mygame/mygame.html");

            // Test row3 interacts with a second page in the template. The row has image, text, and video,
            // but the page only has text and picture. Therefore, a template page will have to be inserted.
            var contentRow3 = new ContentRow(ss);
            contentRow3.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow3.SetCell(columnForEn, "this is block 1 on page 2");
            contentRow3.SetCell(columnForImage, "images/bird.png");
            RobustFile.Copy(
                Path.Combine(_imagesFolder, "bird.png"),
                Path.Combine(whereToPutImages, "bird.png")
            );
            RobustFile.WriteAllText(
                Path.Combine(whereToPutVideo, "video3.mp4"),
                "This is fake video 3"
            );
            contentRow3.SetCell(columnForVideo, "video/video3.mp4");
            pageText.Append(SpreadsheetImageAndTextImportTests.PageWithImageAndText(1, 3, 3));

            // Test row4 again interacts with a second page in the template. The row has image and video,
            // but the page only has text and picture. Therefore, a template page will have to be inserted.
            var contentRow4 = new ContentRow(ss);
            contentRow4.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow4.SetCell(columnForImage, "images/aor_Nab037.png");
            RobustFile.Copy(
                Path.Combine(_imagesFolder, "aor_Nab037.png"),
                Path.Combine(whereToPutImages, "aor_Nab037.png")
            );
            RobustFile.WriteAllText(
                Path.Combine(whereToPutVideo, "video4.mp4"),
                "This is fake video 4"
            );
            contentRow4.SetCell(columnForVideo, "video/video4.mp4");

            // Test row5 again interacts with a second page in the template. The row has just video,
            // but the page only has text and picture. Therefore, a template page will have to be inserted.
            var contentRow5 = new ContentRow(ss);
            contentRow5.AddCell(InternalSpreadsheet.PageContentRowLabel);
            RobustFile.WriteAllText(
                Path.Combine(whereToPutVideo, "video5.mp4"),
                "This is fake video 5"
            );
            contentRow5.SetCell(columnForVideo, "video/video5.mp4");

            // Test row6 again interacts with a second page in the template. The row has just video, text, and a widget,
            // but the page only has text and picture. Two template pages will have to be inserted (6 and 7)!
            var contentRow6 = new ContentRow(ss);
            contentRow6.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow6.SetCell(columnForEn, "this is block 1 on page 5");
            RobustFile.WriteAllText(
                Path.Combine(whereToPutVideo, "video6.mp4"),
                "This is fake video 6"
            );
            contentRow6.SetCell(columnForVideo, "video/video6.mp4");
            var activity3Folder = Path.Combine(whereToPutActivities, "game3");
            Directory.CreateDirectory(activity3Folder);
            RobustFile.WriteAllText(
                Path.Combine(activity2Folder, "game3Root.html"),
                "This is just a fake HTML file"
            );
            contentRow6.SetCell(columnForActivities, "activities/game3/game3Root.html");

            // Test row7 interacts with the second page in the template. The row has image, text, and video,
            // but the page only has text and picture. However, the row explicitly says to make a text-and-picture
            // page. So, the importer uses the template page, and then inserts a Just-video one (page 8).
            var contentRow7 = new ContentRow(ss);
            contentRow7.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow7.SetCell(columnForEn, "this is block 1 on page 7");
            contentRow7.SetCell(columnForImage, "images/Mars 2.png");
            RobustFile.Copy(
                Path.Combine(_imagesFolder, "Mars 2.png"),
                Path.Combine(whereToPutImages, "Mars 2.png")
            );
            RobustFile.WriteAllText(
                Path.Combine(whereToPutVideo, "video7.mp4"),
                "This is fake video 7"
            );
            contentRow7.SetCell(columnForVideo, "video/video7.mp4");
            contentRow7.SetCell(columnForPageType, "Basic Text & Picture");

            // Test row8 interacts with the third page in the template. That page has the right slots
            // (text and picture), but it's the wrong type, so we will instead insert a page.
            pageText.Append(SpreadsheetImageAndTextImportTests.PageWithImageAndText(2, 5, 5));
            var contentRow8 = new ContentRow(ss);
            contentRow8.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow8.SetCell(columnForEn, "this is block 1 on page 9");
            contentRow8.SetCell(columnForImage, "images/bluebird.png");
            RobustFile.Copy(
                Path.Combine(_imagesFolder, "bluebird.png"),
                Path.Combine(whereToPutImages, "bluebird.png")
            );
            contentRow8.SetCell(columnForPageType, "Picture in Middle");

            // We have room for another block of text in that Picture in Middle page.
            // But row9, though it only has text, specifies a text-only page, so we will insert one.
            var contentRow9 = new ContentRow(ss);
            contentRow9.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow9.SetCell(columnForEn, "this is block 1 on page 10");
            contentRow9.SetCell(columnForPageType, "Just Text");

            // We still have a text-and-picture row in the template. Use it up.
            CreateTextRow(ss, columnForEn, "ignore this");

            // The next case involves pulling some new data into an existing quiz page.
            // The template doc purposely has more answers than the spreadsheet, and the
            // extra one is purposely correct, so we can check that it gets cleaned up.
            pageText.Append(
                SpreadsheetImageAndTextImportTests.Quiz(
                    10,
                    "This is the old question",
                    new[] { "Wrong", "Worse", "Right" },
                    2
                )
            );
            var contentRow11 = new ContentRow(ss);
            contentRow11.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow11.SetCell(columnForEn, "Test your comprehension of testing");
            contentRow11.SetCell(columnForPageType, "Quiz Page");
            CreateTextRow(ss, columnForEn, "Is this a good test?");
            CreateTextRow(ss, columnForEn, "No");
            var contentRow14 = CreateTextRow(ss, columnForEn, "Yes");
            contentRow14.SetCell(columnForAttributeData, "../class=correct-answer");

            // This is purposely an error case. The row specifes "Just a Picture" but only has text.
            var contentRow15 = CreateTextRow(ss, columnForEn, "this is block 1 on page 14");
            contentRow15.SetCell(columnForPageType, "Just a Picture");

            // This will require us to import a quiz page. That should pull the appropriate style sheet
            // into our list.
            pageText.Append(SpreadsheetImageAndTextImportTests.PageWithJustText(5, 21));
            var contentRow16 = new ContentRow(ss);
            contentRow16.AddCell(InternalSpreadsheet.PageContentRowLabel);
            contentRow16.SetCell(columnForEn, "Test your comprehension of importing test pages");
            contentRow16.SetCell(columnForPageType, "Quiz Page");
            CreateTextRow(ss, columnForEn, "Is this a trick test?");
            CreateTextRow(ss, columnForEn, "No");
            var contentRow19 = CreateTextRow(ss, columnForEn, "Yes");
            contentRow19.SetCell(columnForAttributeData, "../class=correct-answer");

            // A realistic "Choose word from picture" should also have a picture and more answers.
            // But the point of adding this case is to test that we get the appropriate
            // user-defined style imported.
            var contentRow20 = CreateTextRow(ss, columnForEn, "Choose the matching word");
            contentRow20.SetCell(columnForPageType, "Choose Word from Picture");
            CreateTextRow(ss, columnForEn, "horse");

            pageText.Append(SpreadsheetImageAndTextImportTests.insideBackCoverPage);
            pageText.Append(SpreadsheetImageAndTextImportTests.backCoverPage);
            // Create an HtmlDom for a template to import into
            var xml = string.Format(
                SpreadsheetImageAndTextImportTests.templateDom,
                pageText.ToString()
            );
            _dom = new HtmlDom(xml, true);

            _bookFolder = new TemporaryFolder("SpreadsheetPageTypeImportTests");

            _progressSpy = new ProgressSpy();

            var importer = new TestSpreadsheetImporter(
                null,
                _dom,
                _ssFolder.FolderPath,
                _bookFolder.FolderPath
            );
            _warnings = await importer.ImportAsync(ss, _progressSpy);

            _contentPages = _dom.SafeSelectNodes("//div[contains(@class, 'bloom-page')]")
                .Cast<SafeXmlElement>()
                .ToList();

            // Remove the xmatter to get just the content pages, but save so we can test that too.
            _contentPages.RemoveAt(0);
            _contentPages.RemoveAt(_contentPages.Count - 1);
            _contentPages.RemoveAt(_contentPages.Count - 1);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _bookFolder?.Dispose();
            _ssFolder?.Dispose();
        }

        [TestCase(0, "1", "lady24b.png")]
        [TestCase(0, "2", "LakePendOreille.jpg")]
        [TestCase(6, "3", "Mars 2.png", "Mars%202.png")]
        public void GotImageSourceAndFileOnPageN(
            int n,
            string tag,
            string fileName,
            string src = null
        )
        {
            AssertThatXmlIn
                .Element(_contentPages[n])
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[contains(@class, 'bloom-imageContainer') and @data-test-id='ic{tag}']/img[@src='{src ?? fileName}']",
                    1
                );
            Assert.That(RobustFile.Exists(Path.Combine(_bookFolder.FolderPath, fileName)));
        }

        [TestCase(1, "bird.png")]
        [TestCase(2, "aor_Nab037.png")]
        [TestCase(8, "bluebird.png")]
        public void GotImageSourceAndFileOnAddedPageN(int n, string fileName)
        {
            AssertThatXmlIn
                .Element(_contentPages[n])
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[contains(@class, 'bloom-imageContainer')]/img[@src='{fileName}']",
                    1
                );
            Assert.That(RobustFile.Exists(Path.Combine(_bookFolder.FolderPath, fileName)));
            // We always put a data-test-id on test template pages, so this acts as a check that we inserted a default.
            AssertThatXmlIn
                .Element(_contentPages[n])
                .HasNoMatchForXpath(
                    ".//div[contains(@class, 'bloom-imageContainer') and @data-test-id]"
                );
        }

        [TestCase("fakeVideo.mp4")]
        [TestCase("anotherVideo.mp4")]
        [TestCase("video5.mp4")]
        public void VideoCopiedToOutput(string fileName)
        {
            Assert.That(RobustFile.Exists(Path.Combine(_bookFolder.FolderPath, "video", fileName)));
        }

        [TestCase("drag ball/drag.html")]
        [TestCase("drag ball/sub/nonsense.jpg")]
        [TestCase("extra/mygame/mygame.html")]
        public void ActivityFileCopiedToOutput(string fileName)
        {
            Assert.That(
                RobustFile.Exists(Path.Combine(_bookFolder.FolderPath, "activities", fileName))
            );
        }

        [TestCase(0, 1, "video%2ffakeVideo.mp4")]
        [TestCase(0, 2, "video%2fanotherVideo.mp4")]
        public void VideoSourceSet(int pageIndex, int vcId, string src)
        {
            var page1 = _contentPages[pageIndex];
            // The check for id also confirms that we used the right slot in the template page.
            AssertThatXmlIn
                .Element(page1)
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[contains(@class, 'bloom-videoContainer') and @id='vc{vcId}']/video/source[@src='{src}']",
                    1
                );
            AssertThatXmlIn
                .Element(page1)
                .HasNoMatchForXpath(
                    ".//div[contains(@class, 'bloom-noVideoSelected') and @id='vc1']"
                );
        }

        [TestCase(1, "video%2fvideo3.mp4")]
        [TestCase(2, "video%2fvideo4.mp4")]
        [TestCase(3, "video%2fvideo5.mp4")]
        [TestCase(4, "video%2fvideo6.mp4")]
        [TestCase(7, "video%2fvideo7.mp4")]
        public void VideoSourceSetInAddedPage(int pageIndex, string src)
        {
            var page1 = _contentPages[pageIndex];
            AssertThatXmlIn
                .Element(page1)
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[contains(@class, 'bloom-videoContainer')]/video/source[@src='{src}']",
                    1
                );
            AssertThatXmlIn
                .Element(page1)
                .HasNoMatchForXpath(".//div[contains(@class, 'bloom-noVideoSelected')]");
            // This is one way to verify that we didn't reuse a template page, since we give all the template page elemnts unique IDs.
            AssertThatXmlIn
                .Element(page1)
                .HasNoMatchForXpath(".//div[contains(@class, 'bloom-videoContainer') and @id]");
        }

        [TestCase(0, 1, "activities/drag%20ball/drag.html")]
        [TestCase(0, 2, "activities/extra/mygame/mygame.html")]
        public void WidgetSourceSet(int pageIndex, int wdgtId, string src)
        {
            var page1 = _contentPages[0];
            // The check for "vc1" also confirms that we used the first slot in the template page.
            AssertThatXmlIn
                .Element(page1)
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[contains(@class, 'bloom-widgetContainer') and @data-test-id='wdgt{wdgtId}']/iframe[@src='{src}']",
                    1
                );
        }

        [TestCase(0, "1", "this is block 1 on page 1")]
        [TestCase(0, "2", "this is block 2 on page 1")]
        [TestCase(6, "3", "this is block 1 on page 7")]
        [TestCase(11, "10", "Test your comprehension of testing")]
        [TestCase(11, "11", "Is this a good test?")]
        [TestCase(11, "12", "No")]
        [TestCase(11, "13", "Yes")]
        public void GotTextOnPageN(int n, string tag, string text)
        {
            AssertThatXmlIn
                .Element(_contentPages[n])
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[@data-test-id='tg{tag}']/div[@lang='en' and text() = '{text}']",
                    1
                );
        }

        [TestCase(11, "14")] // The old third answer should have been cleared
        public void ElementIsEmpty(int n, string tag)
        {
            var bloomEditables = _contentPages[n]
                .SafeSelectNodes($".//div[@data-test-id='tg{tag}']/div")
                .Cast<SafeXmlElement>()
                .ToArray();
            Assert.That(bloomEditables.Length, Is.EqualTo(1));
            Assert.That(bloomEditables[0].InnerText.Trim(), Is.EqualTo(""));
            Assert.That(bloomEditables[0].GetAttribute("lang"), Is.EqualTo("z"));
        }

        [TestCase(1, "this is block 1 on page 2")]
        [TestCase(4, "this is block 1 on page 5")]
        [TestCase(8, "this is block 1 on page 9")]
        [TestCase(9, "this is block 1 on page 10")]
        [TestCase(12, "this is block 1 on page 14")]
        public void GotTextOnAddedPageN(int n, string text)
        {
            AssertThatXmlIn
                .Element(_contentPages[n])
                .HasSpecifiedNumberOfMatchesForXpath(
                    $".//div[contains(@class, 'bloom-translationGroup')]/div[@lang='en' and text() = '{text}']",
                    1
                );
            AssertThatXmlIn
                .Element(_contentPages[n])
                .HasNoMatchForXpath(
                    ".//div[contains(@class, 'bloom-translationGroup') and @data-test-id]"
                );
        }

        [TestCase(2, Bloom.Book.Book.PictureAndVideoGuid)] // page 3 should be inserted video-and-image
        [TestCase(3, Bloom.Book.Book.JustVideoGuid)] // page 4 should be inserted just-video
        [TestCase(4, Bloom.Book.Book.VideoOverTextGuid)] // page 5 should be inserted text and video
        [TestCase(5, "3a705ac1-c1f2-45cd-8a7d-011c009cf406")] // page 6 should be inserted widget
        [TestCase(8, "adcd48df-e9ab-4a07-afd4-6a24d0398383")] // page 9 should be inserted picture-in-middle
        [TestCase(9, Bloom.Book.Book.JustTextGuid)] // page 9 should be inserted just-text
        [TestCase(12, Bloom.Book.Book.JustTextGuid)] // page 14 should be inserted just-text
        public void PageType(int n, string guid)
        {
            Assert.That(_contentPages[n].GetAttribute("data-pagelineage"), Does.Contain(guid));
        }

        [Test]
        public void QuizClasses()
        {
            var assertThat = AssertThatXmlIn.Element(_contentPages[11]);
            // There should be only one correct answer
            assertThat.HasSpecifiedNumberOfMatchesForXpath(
                ".//div[contains(@class, 'correct-answer')]",
                1
            );
            // And it should be on the correct element
            assertThat.HasSpecifiedNumberOfMatchesForXpath(
                ".//div[contains(@class, 'correct-answer')]/div[@data-test-id='tg13']",
                1
            );
            // The 'empty' class should have been added to the old third answer
            assertThat.HasSpecifiedNumberOfMatchesForXpath(
                ".//div[contains(@class, 'empty')]/div[@data-test-id='tg14']",
                1
            );
        }

        [Test]
        public void ActivityCssFile_WasAdded()
        {
            var activityPath = Path.Combine(_bookFolder.FolderPath, "Activity.css");
            Assert.That(RobustFile.Exists(activityPath), Is.True);
        }

        [Test]
        public void ActivityCssLink_WasAdded()
        {
            AssertThatXmlIn
                .Element(_dom.Head)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//link[@rel='stylesheet' and @href='Activity.css']",
                    1
                );
        }

        [Test]
        public void PageCouldNotBeUsedWarningHappened()
        {
            Assert.That(
                _warnings,
                Does.Contain(
                    "Row 17 requested page type 'Just a Picture' but contains no data suitable for that page type."
                )
            );
        }

        [Test]
        public void StyleForChooseWord_WasAdded()
        {
            AssertThatXmlIn
                .Element(_dom.Head)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//style[@type='text/css' and contains(text(), 'ButtonText-style')]",
                    1
                );
        }
    }
}
