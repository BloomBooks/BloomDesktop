using System;
using System.Xml;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly TemplateInsertionCommand _templateInsertionCommand;

		public event EventHandler UpdateDisplay;
		public event EventHandler UpdatePageList;



		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection, TemplateInsertionCommand templateInsertionCommand)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_templateInsertionCommand = templateInsertionCommand;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			templateInsertionCommand.InsertPage += new EventHandler(OnInsertTemplatePage);
		}

		private void OnInsertTemplatePage(object sender, EventArgs e)
		{
			_bookSelection.CurrentSelection.InsertPageAfter(_pageSelection.CurrentSelection, sender as Page);
			//_pageSelection.SelectPage(newPage);
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


			if (UpdateDisplay != null)
			{
				UpdateDisplay(this, null);
			}

			if (UpdatePageList != null)
			{
				UpdatePageList(this, null);
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

		public XmlDocument GetPathToHtmlForCurrentPage()
		{
			return _bookSelection.CurrentSelection.GetEditableHtmlFileForPage(_pageSelection.CurrentSelection);
		}
	}

	public class TemplateInsertionCommand
	{
		public event EventHandler InsertPage;

		public void Insert(Page page)
		{
			if (InsertPage != null)
			{
				InsertPage.Invoke(page, null);
			}
		}
	}
}
