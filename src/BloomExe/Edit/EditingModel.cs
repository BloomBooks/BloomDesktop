using System;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;

		public event EventHandler UpdateDisplay;


		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
		}

		public string CurrentBookName
		{
			get
			{
				if (_bookSelection.CurrentSelection == null)
					return "----";
				return _bookSelection.CurrentSelection.Title;
			}
		}

		public bool HaveCurrentEditableBook
		{
			get { return _bookSelection.CurrentSelection != null; }
		}

		public Book CurrentBook
		{
			get { return _bookSelection.CurrentSelection;  }
		}

		void OnBookSelectionChanged(object sender, EventArgs e)
		{
			if (_bookSelection.CurrentSelection.Type == Book.BookType.Publication)
			{
				var page = _bookSelection.CurrentSelection.FirstPage;
				if(page!=null)
					_pageSelection.SelectPage(page);
			}

			EventHandler handler = UpdateDisplay;
			if (handler != null)
			{
				handler(this, null);
			}
		}
		void OnPageSelectionChanged(object sender, EventArgs e)
		{
			EventHandler handler = UpdateDisplay;
			if (handler != null)
			{
				handler(this, null);
			}
		}

		public string GetPathToHtmlFileForCurrentPage()
		{
			return _bookSelection.CurrentSelection.GetHtmlFileForPage(_pageSelection.CurrentSelection);
		}
	}
}
