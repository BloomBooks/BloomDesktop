using System;
using Bloom.Book;
using Bloom.CollectionTab;

namespace Bloom.web
{
    internal static class BookRequestResolver
    {
        internal static bool TryResolveBook(
            BookSelection bookSelection,
            CollectionModel collectionModel,
            string bookId,
            bool requireBook,
            out Book.Book book,
            out BookInfo bookInfo,
            out string failureMessage
        )
        {
            book = null;
            bookInfo = null;
            failureMessage = null;

            if (string.IsNullOrWhiteSpace(bookId))
            {
                var currentBook = bookSelection?.CurrentSelection;
                var currentBookInfo = currentBook?.BookInfo;

                if (currentBook == null || currentBookInfo == null)
                {
                    failureMessage = "No book is currently selected.";
                    return false;
                }

                bookInfo = currentBookInfo;
                if (requireBook)
                    book = currentBook;

                return true;
            }

            var editableCollection = collectionModel?.TheOneEditableCollection;
            if (editableCollection == null)
            {
                failureMessage = "No editable collection is available.";
                return false;
            }

            bookInfo = editableCollection.GetBookInfoById(bookId);
            if (bookInfo == null)
            {
                failureMessage = $"Book with id '{bookId}' was not found.";
                return false;
            }

            if (!requireBook)
                return true;

            book = collectionModel.GetBookFromBookInfo(bookInfo);
            if (book == null)
            {
                failureMessage = $"Book '{bookId}' could not be loaded.";
                return false;
            }

            return true;
        }
    }
}
