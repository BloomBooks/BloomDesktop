using System;

namespace Bloom.CollectionTab
{
    public partial class CollectionModel
    {
        // this is
        private Book.Book _cachedBook;

        /// <summary>
        /// This is designed to be used by UI that needs access to the contents
        /// of a book that may not be the one we are are currently editing.
        /// For example, maybe we want to link to a page in another book or select
        /// a range of pages to insert into the current book.
        /// Loading a book is an expensive operation, so in order to
        /// support UI that shows page thumbnails and such, we cache
        /// the most recent book that is not the one we are editing.
        /// </summary>
        public Book.Book GetBookFromId(string bookId)
        {
            if (string.IsNullOrWhiteSpace(bookId))
            {
                throw new ArgumentException("bookId is required");
            }

            // If it's the currently selected book, return that (no need to load)
            var currentBook = _bookSelection?.CurrentSelection;
            if (currentBook != null && currentBook.BookInfo.Id == bookId)
            {
                return currentBook;
            }

            // Check if we have this book cached
            if (_cachedBook != null && _cachedBook.BookInfo.Id == bookId)
            {
                return _cachedBook;
            }

            // Need to load the book - first get the bookInfo (cheap), then load the full book (expensive)
            var bookInfo = TheOneEditableCollection.GetBookInfoById(bookId);
            if (bookInfo == null)
            {
                throw new ArgumentException($"Book with id '{bookId}' was not found.");
            }

            var book = GetBookFromBookInfo(bookInfo);
            if (book == null)
            {
                throw new ArgumentException($"Book '{bookId}' could not be loaded.");
            }

            _cachedBook = book;

            return book;
        }
    }
}
