using System;
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

        // The running Bloom version, formatted the same way the stamp is (major.minor.build).
        private static string RunningVersion()
        {
            var ok = Version.TryParse(Bloom.Shell.GetShortVersionInfo(), out var v);
            Assert.That(ok, Is.True, "SANITY: the running Bloom version should parse");
            return v.ToString();
        }

        private void Stamp(Bloom.Book.Book book, string version, string layout)
        {
            book.OurHtmlDom.UpdateMetaElement(BookProcessor.kPerPageFixupVersionMeta, version);
            book.OurHtmlDom.UpdateMetaElement(BookProcessor.kPerPageFixupLayoutMeta, layout);
        }

        [Test]
        public void NeedsPerPageFixup_NeverStamped_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();

            // SANITY: the book must be saveable and error-free, or NeedsPerPageFixup short-circuits
            // to false for a reason unrelated to the stamp we are testing.
            Assert.That(book.IsSaveable, Is.True, "SANITY: test book should be saveable");
            Assert.That(book.CheckForErrors(), Is.Empty, "SANITY: test book should have no errors");
            Assert.That(
                book.OurHtmlDom.GetMetaValue(BookProcessor.kPerPageFixupVersionMeta, ""),
                Is.Empty,
                "SANITY: a fresh book should carry no fix-up stamp"
            );

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_StampedThisVersionAndLayout_IsFalse()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            Stamp(book, RunningVersion(), book.GetLayout().SizeAndOrientation.ClassName);

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void NeedsPerPageFixup_StampedOlderVersion_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            // Older than any real Bloom, so it is unambiguously behind the running version.
            Stamp(book, "1.0.0", book.GetLayout().SizeAndOrientation.ClassName);

            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.True);
        }

        [Test]
        public void NeedsPerPageFixup_StampedNewerVersion_IsFalse()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            var running = Version.Parse(RunningVersion());
            var newer = new Version(running.Major + 1, 0, 0).ToString();
            Stamp(book, newer, book.GetLayout().SizeAndOrientation.ClassName);

            // A book a newer Bloom already processed should be left alone (the user's choice in BL-16852).
            Assert.That(BookProcessor.NeedsPerPageFixup(book), Is.False);
        }

        [Test]
        public void NeedsPerPageFixup_SameVersionButLayoutChanged_IsTrue()
        {
            SetSinglePageDom("A5Portrait");
            var book = CreateBook();
            // Stamp says it was done at A5Portrait...
            Stamp(book, RunningVersion(), "A5Portrait");

            // SANITY: with a matching stamp it is not due.
            Assert.That(
                BookProcessor.NeedsPerPageFixup(book),
                Is.False,
                "SANITY: a book stamped at its current version and layout is not due"
            );

            // ...but the page size has since changed, so the layout-derived measurements are stale.
            SetSinglePageDom("A4Landscape");
            var bookAtNewSize = CreateBook();
            Stamp(bookAtNewSize, RunningVersion(), "A5Portrait");

            Assert.That(BookProcessor.NeedsPerPageFixup(bookAtNewSize), Is.True);
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
