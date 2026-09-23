using Bloom.Properties;
using NUnit.Framework;

namespace BloomTests.Book
{
    [TestFixture]
    public class BookSelectionTests
    {
        /// <summary>
        /// SelectBook() must not touch Settings.Default.CurrentBookPath. That setting is persisted
        /// and process-wide, so when SelectBook() wrote it, a test that selected a book in a
        /// temporary folder left later tests looking at a folder that no longer existed, and the
        /// command-line bulk uploader replaced the user's remembered book with each book it
        /// uploaded. Remembering the selection is now WorkspaceView's job. See BL-16660.
        /// </summary>
        [Test]
        public void SelectBook_DoesNotChangeCurrentBookPathSetting()
        {
            const string sentinel = @"C:\a\path\that\SelectBook\must\leave\alone";
            var originalPath = Settings.Default.CurrentBookPath;
            Settings.Default.CurrentBookPath = sentinel;
            try
            {
                Assert.That(
                    Settings.Default.CurrentBookPath,
                    Is.EqualTo(sentinel),
                    "Setup failed: could not put a known value in the setting"
                );

                var selection = new Bloom.Book.BookSelection();
                var book = new Bloom.Book.Book();
                selection.SelectBook(book);

                // Sanity check, so that a SelectBook() which did nothing at all could not pass.
                Assert.That(
                    selection.CurrentSelection,
                    Is.SameAs(book),
                    "SelectBook() did not actually select the book"
                );
                Assert.That(
                    Settings.Default.CurrentBookPath,
                    Is.EqualTo(sentinel),
                    "SelectBook() must not write the remembered book path"
                );

                selection.SelectBook(null);

                Assert.That(
                    selection.CurrentSelection,
                    Is.Null,
                    "SelectBook(null) did not actually clear the selection"
                );
                Assert.That(
                    Settings.Default.CurrentBookPath,
                    Is.EqualTo(sentinel),
                    "SelectBook(null) must not clear the remembered book path"
                );
            }
            finally
            {
                Settings.Default.CurrentBookPath = originalPath;
            }
        }
    }
}
