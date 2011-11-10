using System;
using Bloom.Book;

namespace Bloom.Edit
{
	public class PageSelection
	{
		private readonly BookSelection _bookSelection;
		private IPage _currentSelection;
		private Book.Book _currentBook;
		public event EventHandler SelectionChanged;

		public PageSelection()//BookSelection bookSelection)
		{
//            _bookSelection = bookSelection;
//            bookSelection.SelectionChanged += new EventHandler(bookSelection_SelectionChanged);
		}

//        void bookSelection_SelectionChanged(object sender, EventArgs e)
//        {
//            if (_currentBook != null)
//            {
//                _currentBook.PageInserted -= OnPageInserted;
//                _currentBook.PageDeleted -= OnPageDeleted;
//            }
//            _currentBook = _bookSelection.CurrentSelection;
//            _currentBook.PageInserted += OnPageInserted;
//            _currentBook.PageDeleted += OnPageDeleted;
//        }

		public bool SelectPage(IPage page)
		{
			//enhance... send out cancellable pre-change event

			_currentSelection = page;

			InvokeSelectionChanged();
			return true;
		}

		public IPage CurrentSelection
		{
			get { return _currentSelection; }
		}

		private void InvokeSelectionChanged()
		{
			EventHandler handler = SelectionChanged;
			if (handler != null)
			{
				handler(this, null);
			}
		}

		private void OnPageDeleted(object sender, EventArgs e)
		{

		}

		private void OnPageInserted(object sender, EventArgs e)
		{

		}

	}
}