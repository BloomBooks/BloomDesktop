using System.Globalization;
using Bloom.Book;
using Bloom.SafeXml;
using Moq;
using NUnit.Framework;

namespace BloomTests.Book
{
    /// <summary>
    /// Tests for BookProcessor.NeedsPerPageFixup and the per-page stamps it reads — the decision that
    /// governs whether the automatic per-page browser fix-up (BL-16852) runs when a book is opened for
    /// editing or before publishing. The heavy fix-up itself needs a real browser and is covered by a
    /// manual process-book run and by BookProcessorTests; this fixture exercises only the pure "is it
    /// due?" logic and how stamps travel, which need no browser, so it runs in CI.
    /// </summary>
    [TestFixture]
    public class PerPageFixupDecisionTests : BookTestsBase
    {
        private static string Ours =>
            BookStorage.kBrowserMaintenanceLevel.ToString(CultureInfo.InvariantCulture);

        // Two pages whose size/orientation class we control, so GetLayout() is deterministic.
        private void SetTwoPageDom(string sizeClass)
        {
            SetDom(
                $@"<div class='bloom-page numberedPage {sizeClass}' id='guid1'>
                        <p><textarea lang='xyz' data-book='bookTitle'>dog</textarea></p>
                    </div>
                    <div class='bloom-page numberedPage {sizeClass}' id='guid2'>
                        <p>cat</p>
                    </div>"
            );
        }

        private static SafeXmlElement PageOf(Bloom.Book.Book book, string id) =>
            book.OurHtmlDom.SelectSingleNode($"//div[@id='{id}']");

        private static void Stamp(SafeXmlElement page, string level, string layout)
        {
            page.SetAttribute(BookProcessor.kPageLevelAttribute, level);
            page.SetAttribute(BookProcessor.kPageLayoutAttribute, layout);
        }

        private static void StampAll(Bloom.Book.Book book, string level, string layout)
        {
            Stamp(PageOf(book, "guid1"), level, layout);
            Stamp(PageOf(book, "guid2"), level, layout);
        }

        private static string CurrentLayoutOf(Bloom.Book.Book book) =>
            book.GetLayout().SizeAndOrientation.ClassName;

        [Test]
        public void NeedsPerPageFixup_NeverStamped_IsTrue()
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();

            // SANITY: the book must be saveable and error-free, or NeedsPerPageFixup short-circuits
            // to false for a reason unrelated to the stamps we are testing.
            Assert.That(book.IsSaveable, Is.True, "SANITY: test book should be saveable");
            Assert.That(book.CheckForErrors(), Is.Empty, "SANITY: test book should have no errors");

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
            Assert.That(BookProcessor.PagesNeedingFixup(book).Count, Is.EqualTo(2));
        }

        [Test]
        public void NeedsPerPageFixup_EveryPageStampedCurrentLevelAndLayout_IsFalse()
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();
            StampAll(book, Ours, CurrentLayoutOf(book));

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void PagesNeedingFixup_OnePageStamped_ReturnsOnlyTheOther()
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();
            // As if guid1 had been visited in the Edit tab and guid2 had not.
            Stamp(PageOf(book, "guid1"), Ours, CurrentLayoutOf(book));

            var due = BookProcessor.PagesNeedingFixup(book);

            Assert.That(due.Count, Is.EqualTo(1));
            Assert.That(due[0].Id, Is.EqualTo("guid2"));
        }

        [Test]
        public void NeedsPerPageFixup_StampedEarlierLevel_IsTrue()
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();
            // A page brought to an earlier level than this Bloom knows about needs redoing, which is
            // exactly what bumping kBrowserMaintenanceLevel is for.
            StampAll(
                book,
                (BookStorage.kBrowserMaintenanceLevel - 1).ToString(CultureInfo.InvariantCulture),
                CurrentLayoutOf(book)
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_StampedLaterLevel_IsFalse()
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();
            // A newer Bloom took these pages past what we know how to do; leave them alone rather
            // than dragging them back.
            StampAll(
                book,
                (BookStorage.kBrowserMaintenanceLevel + 1).ToString(CultureInfo.InvariantCulture),
                CurrentLayoutOf(book)
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void NeedsPerPageFixup_UnreadableLevel_IsTrue()
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();
            // Hand-edited or corrupt: we cannot tell what was done, so redo it.
            StampAll(book, Ours, CurrentLayoutOf(book));
            PageOf(book, "guid2").SetAttribute(BookProcessor.kPageLevelAttribute, "not a number");

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_CurrentLevelButLayoutChanged_IsTrue()
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();
            StampAll(book, Ours, "A5Portrait");

            // SANITY: with a matching level and layout it is not due.
            Assert.That(
                BookProcessor.NeedsPerPageFixup(book),
                Is.False,
                "SANITY: pages at the current level and the book's current layout are not due"
            );

            // ...but the page size has since changed, so the layout-derived measurements are stale
            // even though the level is current.
            SetTwoPageDom("A4Landscape");
            var bookAtNewSize = CreateBook();
            StampAll(bookAtNewSize, Ours, "A5Portrait");

            Assert.That(BookProcessor.NeedsPerPageFixup(bookAtNewSize), Is.True);
        }

        [Test]
        public void GetEditableHtmlDomForPage_CarriesTheStampTargetOnBody()
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();

            var dom = book.GetEditableHtmlDomForPage(FirstPage(book));

            Assert.That(
                dom.Body.GetAttribute(BookProcessor.kTargetLevelAttribute),
                Is.EqualTo(Ours)
            );
            Assert.That(
                dom.Body.GetAttribute(BookProcessor.kTargetLayoutAttribute),
                Is.EqualTo(CurrentLayoutOf(book))
            );
        }

        private static IPage FirstPage(Bloom.Book.Book book)
        {
            foreach (var p in book.GetPages())
                return p;
            Assert.Fail("SANITY: the test book should have pages");
            return null;
        }

        private static SafeXmlElement PageDiv(string attributes)
        {
            var doc = SafeXmlDocument.Create();
            doc.LoadXml($"<div class='bloom-page' {attributes}><p>x</p></div>");
            return doc.DocumentElement;
        }

        [Test]
        public void ProcessPageAfterEditing_StampedEditedPage_StampsTheBookPage()
        {
            var destination = PageDiv("");
            var edited = PageDiv(
                $"{BookProcessor.kPageLevelAttribute}='{Ours}' {BookProcessor.kPageLayoutAttribute}='A5Portrait'"
            );

            HtmlDom.ProcessPageAfterEditing(destination, edited);

            Assert.That(
                destination.GetAttribute(BookProcessor.kPageLevelAttribute),
                Is.EqualTo(Ours)
            );
            Assert.That(
                destination.GetAttribute(BookProcessor.kPageLayoutAttribute),
                Is.EqualTo("A5Portrait")
            );
        }

        [Test]
        public void ProcessPageAfterEditing_UnstampedEditedPage_KeepsTheBookPagesStamp()
        {
            // A page saved before its load-time work finished comes back without a stamp; that says
            // nothing about the earlier visit that stamped it.
            var destination = PageDiv(
                $"{BookProcessor.kPageLevelAttribute}='{Ours}' {BookProcessor.kPageLayoutAttribute}='A5Portrait'"
            );
            var edited = PageDiv("");

            HtmlDom.ProcessPageAfterEditing(destination, edited);

            Assert.That(
                destination.GetAttribute(BookProcessor.kPageLevelAttribute),
                Is.EqualTo(Ours)
            );
        }

        [Test]
        public void ProcessPageAfterEditing_EditedPageAboveOurs_IsBroughtDownToOurs()
        {
            var above = (BookStorage.kBrowserMaintenanceLevel + 3).ToString(
                CultureInfo.InvariantCulture
            );
            var destination = PageDiv("");
            var edited = PageDiv(
                $"{BookProcessor.kPageLevelAttribute}='{above}' {BookProcessor.kPageLayoutAttribute}='A5Portrait'"
            );

            HtmlDom.ProcessPageAfterEditing(destination, edited);

            Assert.That(
                destination.GetAttribute(BookProcessor.kPageLevelAttribute),
                Is.EqualTo(Ours)
            );
        }

        [Test]
        public void ClearPageStamp_RemovesBothAttributes()
        {
            var page = PageDiv(
                $"{BookProcessor.kPageLevelAttribute}='{Ours}' {BookProcessor.kPageLayoutAttribute}='A5Portrait'"
            );

            BookProcessor.ClearPageStamp(page);

            Assert.That(page.HasAttribute(BookProcessor.kPageLevelAttribute), Is.False);
            Assert.That(page.HasAttribute(BookProcessor.kPageLayoutAttribute), Is.False);
        }

        // The clamp works on a DOM alone, so these need no book.
        private static HtmlDom DomWithPageLevel(string level)
        {
            var dom = new HtmlDom(
                "<html><head></head><body><div class='bloom-page' id='p1'></div></body></html>"
            );
            if (level != null)
                dom.SelectSingleNode("//div[@id='p1']")
                    .SetAttribute(BookProcessor.kPageLevelAttribute, level);
            return dom;
        }

        private static string PageLevelIn(HtmlDom dom) =>
            dom.SelectSingleNode("//div[@id='p1']").GetAttribute(BookProcessor.kPageLevelAttribute);

        [Test]
        public void ClampBrowserMaintenanceLevel_PageHigherThanOurs_ComesDownToOurs_AndCanBeUndone()
        {
            var higher = (BookStorage.kBrowserMaintenanceLevel + 7).ToString(
                CultureInfo.InvariantCulture
            );
            var dom = DomWithPageLevel(higher);

            // SANITY: it really is above ours before we clamp, or this proves nothing.
            Assert.That(
                BookProcessor.RecordsBrowserMaintenanceLevelAboveOurs(dom),
                Is.True,
                "SANITY: the test level should start above ours"
            );

            var undo = BookProcessor.ClampBrowserMaintenanceLevelToOurs(dom);
            Assert.That(PageLevelIn(dom), Is.EqualTo(Ours));

            undo();
            Assert.That(
                PageLevelIn(dom),
                Is.EqualTo(higher),
                "undo should restore the higher level"
            );
        }

        [Test]
        public void ClampBrowserMaintenanceLevel_LegacyBookMetaHigherThanOurs_ComesDownToOurs()
        {
            // Books written by earlier builds carry the level in a <meta> rather than on pages.
            var dom = DomWithPageLevel(null);
            var higher = (BookStorage.kBrowserMaintenanceLevel + 2).ToString(
                CultureInfo.InvariantCulture
            );
            dom.UpdateMetaElement(BookProcessor.kBrowserMaintenanceLevelMeta, higher);

            BookProcessor.ClampBrowserMaintenanceLevelToOurs(dom);

            Assert.That(
                dom.GetMetaValue(BookProcessor.kBrowserMaintenanceLevelMeta, ""),
                Is.EqualTo(Ours)
            );
        }

        [Test]
        public void ClampBrowserMaintenanceLevel_AtOrBelowOurs_IsLeftAlone()
        {
            // The clamp is one-way. Raising a lower level would claim work we never did, and the
            // page would then never get the pass it still needs.
            var lower = (BookStorage.kBrowserMaintenanceLevel - 1).ToString(
                CultureInfo.InvariantCulture
            );
            var domLower = DomWithPageLevel(lower);
            BookProcessor.ClampBrowserMaintenanceLevelToOurs(domLower);
            Assert.That(
                PageLevelIn(domLower),
                Is.EqualTo(lower),
                "a lower level must not be raised"
            );

            var domSame = DomWithPageLevel(Ours);
            BookProcessor.ClampBrowserMaintenanceLevelToOurs(domSame);
            Assert.That(PageLevelIn(domSame), Is.EqualTo(Ours));
        }

        [Test]
        public void ClampBrowserMaintenanceLevel_MissingOrUnreadable_StaysThatWay()
        {
            // Absent means "never done", which PageNeedsFixup already handles; inventing a number
            // here would tell a later Bloom the pass had run when it had not.
            var domMissing = DomWithPageLevel(null);
            BookProcessor.ClampBrowserMaintenanceLevelToOurs(domMissing);
            Assert.That(PageLevelIn(domMissing), Is.Empty);

            var domJunk = DomWithPageLevel("not a number");
            BookProcessor.ClampBrowserMaintenanceLevelToOurs(domJunk);
            Assert.That(PageLevelIn(domJunk), Is.EqualTo("not a number"));
        }

        // The ordinary single-page save streams the existing file through and replaces one page, so
        // it never rewrites the other pages. A book in which some page carries a level from a newer
        // Bloom therefore has to be pushed onto the full-save path, which is where the clamp lives.
        private void SavePageWithOtherPageAtLevel(int level, out Mock<IBookStorage> storage)
        {
            SetTwoPageDom("A5Portrait");
            var book = CreateBook();
            Stamp(
                PageOf(book, "guid2"),
                level.ToString(CultureInfo.InvariantCulture),
                CurrentLayoutOf(book)
            );
            var page = PageOf(book, "guid1");
            Assert.That(page, Is.Not.Null, "SANITY: the test page should be findable");

            book.SavePageToDisk(page, reallyNeedFullSave: false);
            storage = _storage;
        }

        [Test]
        public void SavePageToDisk_SomePageAboveOurs_TakesTheFullSavePath()
        {
            SavePageWithOtherPageAtLevel(BookStorage.kBrowserMaintenanceLevel + 1, out var storage);

            storage.Verify(
                s => s.Save(),
                Times.Once,
                "a level above ours must force the full save, which is what brings it down"
            );
            storage.Verify(
                s => s.SaveForPageChanged(It.IsAny<string>(), It.IsAny<SafeXmlElement>()),
                Times.Never
            );
        }

        [Test]
        public void SavePageToDisk_LevelsAtOrBelowOurs_KeepTheFastPath()
        {
            // SANITY/contrast: the promotion above must not cost every book the efficient save.
            SavePageWithOtherPageAtLevel(BookStorage.kBrowserMaintenanceLevel, out var storage);

            storage.Verify(
                s => s.SaveForPageChanged(It.IsAny<string>(), It.IsAny<SafeXmlElement>()),
                Times.Once
            );
            storage.Verify(s => s.Save(), Times.Never);
        }

        [Test]
        public void NeedsPerPageFixup_BookWithErrors_IsFalse()
        {
            SetTwoPageDom("A5Portrait");
            // Make the storage report a validation error; such a book shows an error page rather than
            // editable pages, so there is nothing for the per-page pass to do.
            _storage.Setup(x => x.GetValidateErrors()).Returns("a deliberate test error");
            var book = CreateBook();

            Assert.That(
                book.CheckForErrors(),
                Is.Not.Empty,
                "SANITY: the storage should report the error we set up"
            );
            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }
    }
}
