using System;
using Bloom.Api;
using Bloom.Properties;
using SIL.Progress;

namespace Bloom.Book
{
	public class BookSelection
	{
		private readonly BloomWebSocketServer _webSocketServer;
		private Book _currentSelection;
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

			book?.BringBookUpToDate(new NullProgress());

			_currentSelection = book;

			InvokeSelectionChanged(aboutToEdit);
			Settings.Default.CurrentBookPath = book?.FolderPath ?? "";
			Settings.Default.Save();


			// notify browser components that are listening to this event
			// REVIEW: are we even going to use the id?
			_webSocketServer.SendString("book-selection", "changed", book.ID);
		}

		// virtual for mocking
		public virtual Book CurrentSelection
		{
			get { return _currentSelection; }
		}

		public void InvokeSelectionChanged(bool aboutToEdit)
		{
			SelectionChanged?.Invoke(this, new BookSelectionChangedEventArgs() {AboutToEdit = aboutToEdit});
		}
	}
}
