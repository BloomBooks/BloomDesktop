using System.IO;
using System.Linq;
using Bloom;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.Reporting;
using SIL.TestUtilities;

namespace BloomTests.Book
{
    /// <summary>
    /// What a book made from the Wall Calendar template has to look like for the front-end
    /// calendar tooling to work: 24 pages, the meta that names the tooling, a month index on
    /// each grid, and the two data-book elements that carry the year and the first day of
    /// the week book-wide.
    ///
    /// A grid page is a .bloom-page.calendarMonthGrid holding the title above a .bloom-table
    /// that carries data-calendar-month. So a test that is about the grid selects on the month
    /// attribute, and one that is about the whole page selects on the class.
    /// </summary>
    [TestFixture]
    public class WallCalendarTemplateTests
    {
        private BloomFileLocator _fileLocator;
        private BookStarter _starter;
        private TemporaryFolder _projectFolder;
        private SafeXmlDocument _newBookDom;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ErrorReport.IsOkToInteractWithUser = false;
            _projectFolder = new TemporaryFolder("WallCalendarTemplateTests");
            var collectionSettings = new CollectionSettings(
                Path.Combine(_projectFolder.Path, "test.bloomCollection")
            )
            {
                Language1Tag = "xyz",
                Language2Tag = "fr",
                XMatterPackName = "Factory",
            };
            var xmatterFinder = new XMatterPackFinder(
                new[] { BloomFileLocator.GetFactoryXMatterDirectory() }
            );
            _fileLocator = new BloomFileLocator(
                collectionSettings,
                xmatterFinder,
                ProjectContext.GetFactoryFileLocations(),
                ProjectContext.GetFoundFileLocations(),
                ProjectContext.GetAfterXMatterFileLocations()
            );
            _starter = new BookStarter(
                _fileLocator,
                (dir) =>
                    new BookStorage(dir, _fileLocator, new BookRenamedEvent(), collectionSettings),
                collectionSettings
            );

            var source = BloomFileLocator.GetFactoryBookTemplateDirectory("Wall Calendar");
            Assert.That(
                Directory.Exists(source),
                Is.True,
                "Test setup: could not find the Wall Calendar template. Has the content build run?"
            );
            var newBookFolder = _starter.CreateBookOnDiskFromTemplate(source, _projectFolder.Path);
            _newBookDom = SafeXmlDocument.Create();
            _newBookDom.LoadXml(
                File.ReadAllText(
                    Path.Combine(newBookFolder, Path.GetFileName(newBookFolder) + ".htm")
                )
            );
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _projectFolder.Dispose();
        }

        private SafeXmlElement[] Select(string xpath)
        {
            return _newBookDom.SafeSelectNodes(xpath).Cast<SafeXmlElement>().ToArray();
        }

        [Test]
        public void CreatedBook_HasTwelvePicturePagesAndTwelveGridPages()
        {
            Assert.That(
                Select("//div[contains(@class,'calendarMonthTop')]").Length,
                Is.EqualTo(12)
            );
            Assert.That(
                Select("//div[contains(@class,'calendarMonthGrid')]").Length,
                Is.EqualTo(12)
            );
        }

        [Test]
        public void CreatedBook_HasNoTemplateOnlyOrExtraPagesLeft()
        {
            Assert.That(Select("//div[contains(@class,'templateOnly')]"), Is.Empty);
            Assert.That(Select("//div[contains(@data-page,'extra')]"), Is.Empty);
        }

        [Test]
        public void CreatedBook_HasTheBookToolingMeta()
        {
            var metas = Select("//head/meta[@name='bookTooling']");
            Assert.That(metas.Length, Is.EqualTo(1));
            Assert.That(metas[0].GetAttribute("content"), Is.EqualTo("calendar"));
        }

        [Test]
        public void CreatedBook_HasEachMonthIndexExactlyOnce()
        {
            Assert.That(Select("//div[@data-calendar-month]").Length, Is.EqualTo(12));
            for (var month = 0; month < 12; month++)
            {
                Assert.That(
                    Select($"//div[@data-calendar-month='{month}']").Length,
                    Is.EqualTo(1),
                    $"expected exactly one grid for month {month}"
                );
            }
        }

        [Test]
        public void CreatedBook_EveryGridPageHasTheYearAndFirstDayOfWeekElements()
        {
            foreach (var page in Select("//div[contains(@class,'calendarMonthGrid')]"))
            {
                var pageId = page.GetAttribute("id");
                Assert.That(
                    page.SafeSelectNodes(".//div[@data-book='calendarYear']").Length,
                    Is.EqualTo(1),
                    $"page {pageId} should have one year element"
                );
                Assert.That(
                    page.SafeSelectNodes(".//div[@data-book='calendarFirstDayOfWeek']").Length,
                    Is.EqualTo(1),
                    $"page {pageId} should have one first-day-of-week element"
                );
            }
        }

        /// <summary>
        /// The year element is plumbing, not text the user types or formats: it carries the
        /// value the data-div hands to all twelve grid pages, and wallCalendar.less hides it.
        /// So it is a plain div with the class the style sheet hides, and it starts out empty,
        /// which is how the tooling knows the book has not been set up yet.
        /// </summary>
        [Test]
        public void CreatedBook_YearElementIsHiddenPlumbingAndEmpty()
        {
            var year = Select("//div[@data-book='calendarYear']").First();
            Assert.That(year.GetAttribute("class"), Does.Contain("calendarYear"));
            Assert.That(year.GetAttribute("lang"), Is.EqualTo("*"));
            Assert.That(year.InnerText.Trim(), Is.Empty);
        }

        [Test]
        public void CreatedBook_EveryGridPageHasFortyTwoEmptyDayCells()
        {
            foreach (var page in Select("//div[@data-calendar-month]"))
            {
                var dayCells = page.SafeSelectNodes(
                    ".//div[contains(concat(' ', @class, ' '), ' calendarDayCell ')]"
                );
                Assert.That(dayCells.Length, Is.EqualTo(42));
                Assert.That(
                    page.SafeSelectNodes(".//div[contains(@class,'calendarUnusedDay')]"),
                    Is.Empty,
                    "an unconfigured grid page marks no day as unused"
                );
                foreach (SafeXmlElement cell in dayCells)
                {
                    var numbers = cell.SafeSelectNodes(
                        ".//div[contains(@class,'calendarDayNumber')]"
                    );
                    Assert.That(numbers.Length, Is.EqualTo(1));
                    Assert.That(((SafeXmlElement)numbers[0]).InnerText.Trim(), Is.Empty);
                }
            }
        }

        [Test]
        public void CreatedBook_MonthTitleIsSeededInFiveLanguagesWithAnEmptySlotForTheBookLanguage()
        {
            var januaryPage = Select(
                    "//div[contains(@class,'calendarMonthGrid')][.//div[@data-calendar-month='0']]"
                )
                .Single();
            var title = januaryPage
                .SafeSelectNodes(
                    ".//div[contains(@class,'bloom-translationGroup') and contains(@class,'calendarMonthName')]"
                )
                .Cast<SafeXmlElement>()
                .Single();

            foreach (
                var pair in new[]
                {
                    ("en", "January"),
                    ("fr", "janvier"),
                    ("es", "enero"),
                    ("id", "Januari"),
                    ("pt", "janeiro"),
                }
            )
            {
                var editable = title
                    .SafeSelectNodes(
                        $"div[contains(@class,'bloom-editable') and @lang='{pair.Item1}']"
                    )
                    .Cast<SafeXmlElement>()
                    .SingleOrDefault();
                Assert.That(editable, Is.Not.Null, $"no {pair.Item1} seed for January");
                Assert.That(editable.InnerText.Trim(), Is.EqualTo(pair.Item2));
            }

            var language1Slot = title
                .SafeSelectNodes("div[contains(@class,'bloom-editable') and @lang='xyz']")
                .Cast<SafeXmlElement>()
                .SingleOrDefault();
            Assert.That(
                language1Slot,
                Is.Not.Null,
                "book creation should have made an editable for the book's own language"
            );
            Assert.That(language1Slot.InnerText.Trim(), Is.Empty);
            Assert.That(language1Slot.GetAttribute("class"), Does.Contain("CalendarMonth-style"));
        }

        [Test]
        public void CreatedBook_WeekdayNamesAreSeededSundayFirst()
        {
            var januaryGrid = Select("//div[@data-calendar-month='0']").Single();
            var englishWeekdays = januaryGrid
                .SafeSelectNodes(
                    ".//div[contains(@class,'CalendarDayOfWeek-style') and @lang='en']"
                )
                .Cast<SafeXmlElement>()
                .Select(e => e.InnerText.Trim())
                .ToArray();

            Assert.That(
                englishWeekdays,
                Is.EqualTo(new[] { "Sun", "Mon", "Tues", "Wed", "Thur", "Fri", "Sat" })
            );
        }
    }
}
