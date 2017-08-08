using System;

namespace Bloom.Book
{
	public class BookSelection
	{
		private Book _currentSelection;
		public event EventHandler<BookSelectionChangedEventArgs> SelectionChanged;


		public void SelectBook(Book book, bool aboutToEdit = false)
		{
			if (_currentSelection == book)
				return;

			//enhance... send out cancellable pre-change event

		   _currentSelection = book;

			InvokeSelectionChanged(aboutToEdit);
		}

		public Book CurrentSelection
		{
			get { return _currentSelection; }
		}

		private void InvokeSelectionChanged(bool aboutToEdit)
		{
			SelectionChanged?.Invoke(this, new BookSelectionChangedEventArgs() {AboutToEdit = aboutToEdit});
		}
	}
}
