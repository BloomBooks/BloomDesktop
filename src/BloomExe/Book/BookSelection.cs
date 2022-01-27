using System;
using Bloom.Api;
using Bloom.Collection;
using Bloom.Properties;
using SIL.Progress;

namespace Bloom.Book
{
	public class BookSelection
	{
		private Book _currentSelection;
		// Both of these are raised when the selected book changes, but the HighPriority subscribers
		// are notified first.
		public event EventHandler<BookSelectionChangedEventArgs> SelectionChanged;
		public event EventHandler<BookSelectionChangedEventArgs> SelectionChangedHighPriority;

		// We used to have 2 ctors here; the one used by autofac had an unused websocketserver.
		// The other was an empty default ctor. We no longer need either.

		public void SelectBook(Book book, bool aboutToEdit = false)
		{
			if (_currentSelection == book)
				return;
			// We don't need to reload the collection just because we make changes bringing the book up to date.
			if (book != null)
				BookCollection.TemporariliyIgnoreChangesToFolder(book.FolderPath);

			// The bookdata null test prevents doing this on books not sufficiently initialized to
			// BringUpToDate, typically only in unit tests.
			if (book!=null && book.BookData != null && book.IsEditable)
			{
				book?.BringBookUpToDate(new NullProgress());
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
