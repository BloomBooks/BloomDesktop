using System;
using Bloom.Api;
using Bloom.Collection;
using Bloom.Properties;
using SIL.Progress;

namespace Bloom.Book
{
    public class BookSelection
    {
        private readonly BloomWebSocketServer _webSocketServer;
        private Book _currentSelection;

        // Both of these are raised when the selected book changes, but the HighPriority subscribers
        // are notified first.
        public event EventHandler<BookSelectionChangedEventArgs> SelectionChanged;
        public event EventHandler<BookSelectionChangedEventArgs> SelectionChangedHighPriority;

        // this one is used for short-lived things other than the "global" one
        public BookSelection()
        {
            // this constructor doesn't do anything. It's just here so that these special cases don't need
            // to provide a websocketServer.
        }

        // This one is created by the ProjectContext and is used for the global current book
        // In actual fact, this ctor (and the associated instance variable) may not be needed at all.
        // Perhaps it used to?! 12 Aug 2021 gjm
        public BookSelection(BloomWebSocketServer webSocketServer)
        {
            _webSocketServer = webSocketServer;
        }

        public void SelectBook(Book book, bool aboutToEdit = false)
        {
            if (_currentSelection == book)
                return;
            // We don't need to reload the collection just because we make changes bringing the book up to date.
            if (book != null)
                BookCollection.TemporariliyIgnoreChangesToFolder(book.FolderPath);

            // The bookdata null test prevents doing this on books not sufficiently initialized to
            // BringUpToDate, typically only in unit tests.
            if (book != null && book.BookData != null && book.IsSaveable)
            {
                book.EnsureUpToDate();
            }

            _currentSelection = book;

            InvokeSelectionChanged(aboutToEdit);
            Settings.Default.CurrentBookPath = book?.FolderPath ?? "";
            Settings.Default.Save();
        }

        // virtual for mocking
        public virtual Book CurrentSelection
        {
            get { return _currentSelection; }
        }

        public void InvokeSelectionChanged(bool aboutToEdit)
        {
            var args = new BookSelectionChangedEventArgs() { AboutToEdit = aboutToEdit };
            SelectionChangedHighPriority?.Invoke(this, args);
            SelectionChanged?.Invoke(this, args);
        }
    }
}
