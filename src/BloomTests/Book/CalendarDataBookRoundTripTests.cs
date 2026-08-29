using System.IO;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.Reporting;
using SIL.TestUtilities;

namespace BloomTests.Book
{
    /// <summary>
    /// The calendar grid pages hold the year and the first day of the week in plain,
    /// non-editable divs that carry data-book attributes, and rely on Bloom's data-div
    /// machinery to keep all twelve pages in step. These tests pin down that round trip,
    /// because nothing else in Bloom uses data-book on an element that is neither a
    /// bloom-editable nor an image.
    /// </summary>
    [TestFixture]
    public class CalendarDataBookRoundTripTests
    {
        private CollectionSettings _collectionSettings;
        private TemporaryFolder _folder;

        [SetUp]
        public void Setup()
        {
            _folder = new TemporaryFolder("CalendarDataBookRoundTripTests");
            _collectionSettings = new CollectionSettings(
                Path.Combine(_folder.Path, "test.bloomCollection")
            )
            {
                Language1Tag = "xyz",
                Language2Tag = "en",
            };
            ErrorReport.IsOkToInteractWithUser = false;
        }

        [TearDown]
        public void TearDown()
        {
            _folder.Dispose();
        }

        /// <summary>
        /// Two grid pages exactly as the template writes them, with the year filled in on the
        /// first one only, as it would be just after the setup dialog wrote to the open page.
        /// </summary>
        private static HtmlDom MakeTwoGridPageDom(string yearOnFirstPage, string firstDayOfWeek)
        {
            return new HtmlDom(
                $@"<html><head></head><body>
                    <div id='bloomDataDiv'></div>
                    <div class='bloom-page calendarMonthGrid' id='page0' data-calendar-month='0'>
                        <div class='marginBox'>
                            <div class='calendarYear' data-book='calendarYear' lang='*'>{yearOnFirstPage}</div>
                            <div class='calendarFirstDayOfWeek' data-book='calendarFirstDayOfWeek' lang='*'>{firstDayOfWeek}</div>
                        </div>
                    </div>
                    <div class='bloom-page calendarMonthGrid' id='page1' data-calendar-month='1'>
                        <div class='marginBox'>
                            <div class='calendarYear' data-book='calendarYear' lang='*'></div>
                            <div class='calendarFirstDayOfWeek' data-book='calendarFirstDayOfWeek' lang='*'></div>
                        </div>
                    </div>
                 </body></html>"
            );
        }

        private static string TextOf(HtmlDom dom, string xpath)
        {
            var node = dom.RawDom.SelectSingleNode(xpath) as SafeXmlElement;
            return node?.InnerText.Trim();
        }

        [Test]
        public void SaveThenSynchronize_YearTypedOnOnePage_LandsInDataDivAndOnTheOtherPage()
        {
            var dom = MakeTwoGridPageDom("2027", "1");
            Assert.That(
                TextOf(dom, "//div[@id='page1']//div[@data-book='calendarYear']"),
                Is.Empty,
                "Test setup: the second page should start with no year"
            );
            var bookData = new BookData(dom, _collectionSettings, null);
            var editedPage = dom.RawDom.SelectSingleNode("//div[@id='page0']") as SafeXmlElement;

            // What Bloom does when the page the user was on is saved, and then when the DOM
            // is brought back into step with the data-div.
            bookData.SuckInDataFromEditedDom(editedPage);
            bookData.SynchronizeDataItemsThroughoutDOM();

            Assert.That(
                TextOf(dom, "//div[@id='bloomDataDiv']/div[@data-book='calendarYear']"),
                Is.EqualTo("2027"),
                "the year should have been harvested into the data-div"
            );
            Assert.That(
                TextOf(dom, "//div[@id='page1']//div[@data-book='calendarYear']"),
                Is.EqualTo("2027"),
                "the year should have been injected into the other grid page"
            );
        }

        [Test]
        public void SaveThenSynchronize_FirstDayOfWeekTypedOnOnePage_LandsInDataDivAndOnTheOtherPage()
        {
            var dom = MakeTwoGridPageDom("2027", "1");
            var bookData = new BookData(dom, _collectionSettings, null);
            var editedPage = dom.RawDom.SelectSingleNode("//div[@id='page0']") as SafeXmlElement;

            bookData.SuckInDataFromEditedDom(editedPage);
            bookData.SynchronizeDataItemsThroughoutDOM();

            Assert.That(
                TextOf(dom, "//div[@id='bloomDataDiv']/div[@data-book='calendarFirstDayOfWeek']"),
                Is.EqualTo("1")
            );
            Assert.That(
                TextOf(dom, "//div[@id='page1']//div[@data-book='calendarFirstDayOfWeek']"),
                Is.EqualTo("1")
            );
        }

        [Test]
        public void SaveThenSynchronize_FirstDayOfWeekIsZero_StillRoundTrips()
        {
            // Sunday is 0, and an element whose text is "0" is easy to mistake for an empty one.
            var dom = MakeTwoGridPageDom("2027", "0");
            var bookData = new BookData(dom, _collectionSettings, null);
            var editedPage = dom.RawDom.SelectSingleNode("//div[@id='page0']") as SafeXmlElement;

            bookData.SuckInDataFromEditedDom(editedPage);
            bookData.SynchronizeDataItemsThroughoutDOM();

            Assert.That(
                TextOf(dom, "//div[@id='page1']//div[@data-book='calendarFirstDayOfWeek']"),
                Is.EqualTo("0")
            );
        }

        [Test]
        public void Synchronize_ValueOnlyInDataDiv_ReachesEveryGridPage()
        {
            // What happens when a book is reopened: the data-div holds the values, and the
            // pages are the way the template shipped them.
            var dom = MakeTwoGridPageDom("", "");
            var dataDiv =
                dom.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']") as SafeXmlElement;
            dataDiv.InnerXml =
                "<div data-book='calendarYear' lang='*'>2028</div>"
                + "<div data-book='calendarFirstDayOfWeek' lang='*'>6</div>";
            var bookData = new BookData(dom, _collectionSettings, null);

            bookData.SynchronizeDataItemsThroughoutDOM();

            foreach (var pageId in new[] { "page0", "page1" })
            {
                Assert.That(
                    TextOf(dom, $"//div[@id='{pageId}']//div[@data-book='calendarYear']"),
                    Is.EqualTo("2028"),
                    $"{pageId} should have got the year from the data-div"
                );
                Assert.That(
                    TextOf(dom, $"//div[@id='{pageId}']//div[@data-book='calendarFirstDayOfWeek']"),
                    Is.EqualTo("6"),
                    $"{pageId} should have got the first day of the week from the data-div"
                );
            }
        }
    }
}
