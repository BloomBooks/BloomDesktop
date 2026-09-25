using System.Globalization;
using Bloom.Book;
using Bloom.SafeXml;
using Moq;
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

        private void Stamp(Bloom.Book.Book book, int level)
        {
            book.OurHtmlDom.UpdateMetaElement(
                BookProcessor.kPageLayoutUpdateLevelMeta,
                level.ToString(CultureInfo.InvariantCulture)
            );
        }

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
                book.OurHtmlDom.GetMetaValue(BookProcessor.kPageLayoutUpdateLevelMeta, ""),
                Is.Empty,
                "SANITY: a fresh book should carry no page layout update level"
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_StampedCurrentLevel_IsFalse()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            Stamp(book, BookStorage.kPageLayoutUpdateLevel);

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void NeedsPerPageFixup_StampedEarlierLevel_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            // A book brought to an earlier level than this Bloom knows about needs redoing, which is
            // exactly what bumping kPageLayoutUpdateLevel is for.
            Stamp(book, BookStorage.kPageLayoutUpdateLevel - 1);

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_StampedLaterLevel_IsFalse()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            // A newer Bloom took this book past what we know how to do; leave it alone rather than
            // dragging it back.
            Stamp(book, BookStorage.kPageLayoutUpdateLevel + 1);

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void NeedsPerPageFixup_UnreadableLevel_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            // Hand-edited or corrupt: we cannot tell what was done, so redo it.
            Stamp(book, 0);
            book.OurHtmlDom.UpdateMetaElement(
                BookProcessor.kPageLayoutUpdateLevelMeta,
                "not a number"
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void SetLayout_ToADifferentSize_MakesTheBookDue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            Stamp(book, BookStorage.kPageLayoutUpdateLevel);
            Assert.That(
                BookProcessor.NeedsPerPageFixup(book),
                Is.False,
                "SANITY: a book at the current level is not due"
            );

            // The measurements the fix-ups record are relative to the page, so they are stale at a
            // new size even though the level was current.
            book.SetLayout(
                new Layout() { SizeAndOrientation = SizeAndOrientation.FromString("A4Landscape") }
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void SetLayout_ToTheSameSize_LeavesTheBookAlone()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            Stamp(book, BookStorage.kPageLayoutUpdateLevel);

            book.SetLayout(
                new Layout() { SizeAndOrientation = SizeAndOrientation.FromString("A5Portrait") }
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void StampPerPageFixupDone_RecordsOurLevel_AndDropsThePreReleaseNames()
        {
            var dom = new HtmlDom("<html><head></head><body></body></html>");
            dom.UpdateMetaElement("browserMaintenanceLevel", "1");
            dom.UpdateMetaElement("browserMaintenanceLayout", "A5Portrait");

            BookProcessor.StampPerPageFixupDone(dom);

            Assert.That(
                dom.GetMetaValue(BookProcessor.kPageLayoutUpdateLevelMeta, ""),
                Is.EqualTo(
                    BookStorage.kPageLayoutUpdateLevel.ToString(CultureInfo.InvariantCulture)
                )
            );
            Assert.That(dom.GetMetaValue("browserMaintenanceLevel", null), Is.Null);
            Assert.That(dom.GetMetaValue("browserMaintenanceLayout", null), Is.Null);
        }

        // The clamp works on a DOM alone, so these need no book.
        private static HtmlDom DomWithLevel(string level)
        {
            var dom = new HtmlDom("<html><head></head><body></body></html>");
            if (level != null)
                dom.UpdateMetaElement(BookProcessor.kPageLayoutUpdateLevelMeta, level);
            return dom;
        }

        private static string LevelIn(HtmlDom dom) =>
            dom.GetMetaValue(BookProcessor.kPageLayoutUpdateLevelMeta, "");

        [Test]
        public void ClampPageLayoutUpdateLevel_HigherThanOurs_ComesDownToOurs()
        {
            var higher = BookStorage.kPageLayoutUpdateLevel + 7;
            var dom = DomWithLevel(higher.ToString(CultureInfo.InvariantCulture));

            // SANITY: it really is above ours before we clamp, or this proves nothing.
            Assert.That(
                int.Parse(LevelIn(dom)),
                Is.GreaterThan(BookStorage.kPageLayoutUpdateLevel),
                "SANITY: the test level should start above ours"
            );

            BookProcessor.ClampPageLayoutUpdateLevelToOurs(dom);

            Assert.That(
                LevelIn(dom),
                Is.EqualTo(
                    BookStorage.kPageLayoutUpdateLevel.ToString(CultureInfo.InvariantCulture)
                )
            );
        }

        [Test]
        public void ClampPageLayoutUpdateLevel_AtOrBelowOurs_IsLeftAlone()
        {
            // The clamp is one-way. Raising a lower level would claim work we never did, and the
            // book would then never get the pass it still needs.
            var lower = (BookStorage.kPageLayoutUpdateLevel - 1).ToString(
                CultureInfo.InvariantCulture
            );
            var domLower = DomWithLevel(lower);
            BookProcessor.ClampPageLayoutUpdateLevelToOurs(domLower);
            Assert.That(LevelIn(domLower), Is.EqualTo(lower), "a lower level must not be raised");

            var same = BookStorage.kPageLayoutUpdateLevel.ToString(CultureInfo.InvariantCulture);
            var domSame = DomWithLevel(same);
            BookProcessor.ClampPageLayoutUpdateLevelToOurs(domSame);
            Assert.That(LevelIn(domSame), Is.EqualTo(same));
        }

        [Test]
        public void ClampPageLayoutUpdateLevel_MissingOrUnreadable_StaysThatWay()
        {
            // Absent means "never done", which NeedsPerPageFixup already handles; inventing a number
            // here would tell a later Bloom the pass had run when it had not.
            var domMissing = DomWithLevel(null);
            BookProcessor.ClampPageLayoutUpdateLevelToOurs(domMissing);
            Assert.That(LevelIn(domMissing), Is.Empty);

            var domJunk = DomWithLevel("not a number");
            BookProcessor.ClampPageLayoutUpdateLevelToOurs(domJunk);
            Assert.That(LevelIn(domJunk), Is.EqualTo("not a number"));
        }

        // The ordinary single-page save streams the existing file through and replaces one page, so
        // it never rewrites the head. A book carrying a level from a newer Bloom therefore has to be
        // pushed onto the full-save path, which is where the clamp lives.
        private void SavePageWithRecordedLevel(int level, out Mock<IBookStorage> storage)
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            book.OurHtmlDom.UpdateMetaElement(
                BookProcessor.kPageLayoutUpdateLevelMeta,
                level.ToString(CultureInfo.InvariantCulture)
            );
            var page = book.OurHtmlDom.SelectSingleNode("//div[@id='guid1']");
            Assert.That(page, Is.Not.Null, "SANITY: the test page should be findable");

            book.SavePageToDisk(page, reallyNeedFullSave: false);
            storage = _storage;
        }

        [Test]
        public void SavePageToDisk_LevelAboveOurs_TakesTheFullSavePath()
        {
            SavePageWithRecordedLevel(BookStorage.kPageLayoutUpdateLevel + 1, out var storage);

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
        public void SavePageToDisk_LevelAtOrBelowOurs_KeepsTheFastPath()
        {
            // SANITY/contrast: the promotion above must not cost every book the efficient save.
            SavePageWithRecordedLevel(BookStorage.kPageLayoutUpdateLevel, out var storage);

            storage.Verify(
                s => s.SaveForPageChanged(It.IsAny<string>(), It.IsAny<SafeXmlElement>()),
                Times.Once
            );
            storage.Verify(s => s.Save(), Times.Never);
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
