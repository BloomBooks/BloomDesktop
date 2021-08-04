using System;
using System.Dynamic;
using Bloom.Api;
using Bloom.Properties;
using Newtonsoft.Json;
using SIL.Progress;

namespace Bloom.Book
{
	public class BookSelection
	{
		private readonly BloomWebSocketServer _webSocketServer;
		private Book _currentSelection;
		internal bool HandlingSelectionChanged;
		public event EventHandler<BookSelectionChangedEventArgs> SelectionChanged;

		// this one is used for short-lived things other than the "global" one
		public BookSelection()
		{
			// this constructor doesn't do anything. It's just here so that these special cases don't need
			// to provide a websocketServer.	
		}

		// This one is created by the ProjectContext and is used for the global current book
		public BookSelection(BloomWebSocketServer webSocketServer)
		{
			_webSocketServer = webSocketServer;
		}

		public void SelectBook(Book book, bool aboutToEdit = false)
		{
			if (_currentSelection == book)
				return;

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
			try
			{
				HandlingSelectionChanged = true;
				SelectionChanged?.Invoke(this, new BookSelectionChangedEventArgs() { AboutToEdit = aboutToEdit });
			}
			finally
			{
				HandlingSelectionChanged = false;
			}
		}
	}
}
