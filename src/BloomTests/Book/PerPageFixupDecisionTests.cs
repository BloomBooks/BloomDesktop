using System.Globalization;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.Book
{
    /// <summary>
    /// Tests for BookProcessor.NeedsPerPageFixup — the decision that governs whether the automatic
    /// per-page browser fix-up (BL-16852) runs when a book is opened for editing or before publishing.
    /// The heavy fix-up itself needs a real browser and is covered by a manual process-book run and by
    /// BookProcessorTests; this fixture exercises only the pure "is it due?" logic, which needs no
    /// browser, so it runs in CI.
    /// </summary>
    [TestFixture]
    public class PerPageFixupDecisionTests : BookTestsBase
    {
        // A single page whose size/orientation class we control, so GetLayout() is deterministic.
        private void SetSinglePageDom(string sizeClass)
        {
            SetDom(
                $@"<div class='bloom-page numberedPage {sizeClass}' id='guid1'>
                        <p><textarea lang='xyz' data-book='bookTitle'>dog</textarea></p>
                    </div>"
            );
        }

        private void Stamp(Bloom.Book.Book book, int level, string layout)
        {
            book.OurHtmlDom.UpdateMetaElement(
                BookProcessor.kBrowserMaintenanceLevelMeta,
                level.ToString(CultureInfo.InvariantCulture)
            );
            book.OurHtmlDom.UpdateMetaElement(BookProcessor.kBrowserMaintenanceLayoutMeta, layout);
        }

        private static string CurrentLayoutOf(Bloom.Book.Book book) =>
            book.GetLayout().SizeAndOrientation.ClassName;

        [Test]
        public void NeedsPerPageFixup_NeverStamped_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();

            // SANITY: the book must be saveable and error-free, or NeedsPerPageFixup short-circuits
            // to false for a reason unrelated to the level we are testing.
            Assert.That(book.IsSaveable, Is.True, "SANITY: test book should be saveable");
            Assert.That(book.CheckForErrors(), Is.Empty, "SANITY: test book should have no errors");
            Assert.That(
                book.OurHtmlDom.GetMetaValue(BookProcessor.kBrowserMaintenanceLevelMeta, ""),
                Is.Empty,
                "SANITY: a fresh book should carry no browser maintenance level"
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_StampedCurrentLevelAndLayout_IsFalse()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            Stamp(book, BookStorage.kBrowserMaintenanceLevel, CurrentLayoutOf(book));

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void NeedsPerPageFixup_StampedEarlierLevel_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            // A book brought to an earlier level than this Bloom knows about needs redoing, which is
            // exactly what bumping kBrowserMaintenanceLevel is for.
            Stamp(book, BookStorage.kBrowserMaintenanceLevel - 1, CurrentLayoutOf(book));

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_StampedLaterLevel_IsFalse()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            // A newer Bloom took this book past what we know how to do; leave it alone rather than
            // dragging it back.
            Stamp(book, BookStorage.kBrowserMaintenanceLevel + 1, CurrentLayoutOf(book));

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void NeedsPerPageFixup_UnreadableLevel_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            // Hand-edited or corrupt: we cannot tell what was done, so redo it.
            Stamp(book, 0, CurrentLayoutOf(book));
            book.OurHtmlDom.UpdateMetaElement(
                BookProcessor.kBrowserMaintenanceLevelMeta,
                "not a number"
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_CurrentLevelButLayoutChanged_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            Stamp(book, BookStorage.kBrowserMaintenanceLevel, "A5Portrait");

            // SANITY: with a matching level and layout it is not due.
            Assert.That(
                BookProcessor.NeedsPerPageFixup(book),
                Is.False,
                "SANITY: a book at the current level and its current layout is not due"
            );

            // ...but the page size has since changed, so the layout-derived measurements are stale
            // even though the level is current.
            SetSinglePageDom("A4Landscape");
            var bookAtNewSize = CreateBook();
            Stamp(bookAtNewSize, BookStorage.kBrowserMaintenanceLevel, "A5Portrait");

            Assert.That(BookProcessor.NeedsPerPageFixup(bookAtNewSize), Is.True);
        }

        // The clamp works on a DOM alone, so these need no book.
        private static HtmlDom DomWithLevel(string level)
        {
            var dom = new HtmlDom("<html><head></head><body></body></html>");
            if (level != null)
                dom.UpdateMetaElement(BookProcessor.kBrowserMaintenanceLevelMeta, level);
            return dom;
        }

        private static string LevelIn(HtmlDom dom) =>
            dom.GetMetaValue(BookProcessor.kBrowserMaintenanceLevelMeta, "");

        [Test]
        public void ClampBrowserMaintenanceLevel_HigherThanOurs_ComesDownToOurs()
        {
            var higher = BookStorage.kBrowserMaintenanceLevel + 7;
            var dom = DomWithLevel(higher.ToString(CultureInfo.InvariantCulture));

            // SANITY: it really is above ours before we clamp, or this proves nothing.
            Assert.That(
                int.Parse(LevelIn(dom)),
                Is.GreaterThan(BookStorage.kBrowserMaintenanceLevel),
                "SANITY: the test level should start above ours"
            );

            BookProcessor.ClampBrowserMaintenanceLevelToOurs(dom);

            Assert.That(
                LevelIn(dom),
                Is.EqualTo(
                    BookStorage.kBrowserMaintenanceLevel.ToString(CultureInfo.InvariantCulture)
                )
            );
        }

        [Test]
        public void ClampBrowserMaintenanceLevel_AtOrBelowOurs_IsLeftAlone()
        {
            // The clamp is one-way. Raising a lower level would claim work we never did, and the
            // book would then never get the pass it still needs.
            var lower = (BookStorage.kBrowserMaintenanceLevel - 1).ToString(
                CultureInfo.InvariantCulture
            );
            var domLower = DomWithLevel(lower);
            BookProcessor.ClampBrowserMaintenanceLevelToOurs(domLower);
            Assert.That(LevelIn(domLower), Is.EqualTo(lower), "a lower level must not be raised");

            var same = BookStorage.kBrowserMaintenanceLevel.ToString(CultureInfo.InvariantCulture);
            var domSame = DomWithLevel(same);
            BookProcessor.ClampBrowserMaintenanceLevelToOurs(domSame);
            Assert.That(LevelIn(domSame), Is.EqualTo(same));
        }

        [Test]
        public void ClampBrowserMaintenanceLevel_MissingOrUnreadable_StaysThatWay()
        {
            // Absent means "never done", which NeedsPerPageFixup already handles; inventing a number
            // here would tell a later Bloom the pass had run when it had not.
            var domMissing = DomWithLevel(null);
            BookProcessor.ClampBrowserMaintenanceLevelToOurs(domMissing);
            Assert.That(LevelIn(domMissing), Is.Empty);

            var domJunk = DomWithLevel("not a number");
            BookProcessor.ClampBrowserMaintenanceLevelToOurs(domJunk);
            Assert.That(LevelIn(domJunk), Is.EqualTo("not a number"));
        }

        [Test]
        public void NeedsPerPageFixup_BookWithErrors_IsFalse()
        {
            SetSinglePageDom("A5Portrait");
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
