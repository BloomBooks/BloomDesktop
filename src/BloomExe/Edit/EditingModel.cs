using System;
using System.Diagnostics;
using System.Xml;
using Palaso.UI.WindowsForms.ImageToolbox;
using Skybound.Gecko;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly LanguageSettings _languageSettings;
		private XmlDocument _domForCurrentPage;
		private bool _visible;
		private Book _currentlyDisplayedBook;
		private EditingView _view;


		public event EventHandler UpdatePageList;

		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection,
			LanguageSettings languageSettings,
			TemplateInsertionCommand templateInsertionCommand,
			PageListChangedEvent pageListChangedEvent,
			RelocatePageEvent relocatePageEvent,
			DeletePageCommand deletePageCommand)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_languageSettings = languageSettings;

			//bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			templateInsertionCommand.InsertPage += new EventHandler(OnInsertTemplatePage);
			deletePageCommand.Subscribe(OnDeletePage);
			pageListChangedEvent.Subscribe(x=>  InvokeUpdatePageList());
			relocatePageEvent.Subscribe(OnRelocatePage);
		}

		private void OnDeletePage(IPage page)
		{
			_currentlyDisplayedBook.DeletePage(page);
			_view.UpdatePageList();
		}

		private void OnRelocatePage(RelocatePageInfo info)
		{
			_bookSelection.CurrentSelection.RelocatePage(info.Page, info.IndexOfPageAfterMove);

		}


		private void OnInsertTemplatePage(object sender, EventArgs e)
		{
			_bookSelection.CurrentSelection.InsertPageAfter(_pageSelection.CurrentSelection, sender as Page);
			_view.UpdatePageList();
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

		public bool ShowTranslationPanel
		{
			get
			{
				return _bookSelection.CurrentSelection.HasSourceTranslations;
			}
		}

		public bool GetBookHasChanged()
		{
			return _currentlyDisplayedBook != CurrentBook;
		}

		public void ActualVisibiltyChanged(bool visible)
		{
			_visible = visible;
			Debug.WriteLine("EditingModel._visible =" + _visible);
			if (!visible || _currentlyDisplayedBook == CurrentBook)
				return;

			_currentlyDisplayedBook = CurrentBook;
			//if (_bookSelection.CurrentSelection.Type == Book.BookType.Publication)
			{
				var page = _bookSelection.CurrentSelection.FirstPage;
				if (page != null)
					_pageSelection.SelectPage(page);
			}
			if (_view != null)
			{
				_view.UpdateTemplateList();
				_view.UpdatePageList();
			}
		}

		void OnPageSelectionChanged(object sender, EventArgs e)
		{
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
		}

		public XmlDocument GetXmlDocumentForCurrentPage()
		{
			_domForCurrentPage = _bookSelection.CurrentSelection.GetEditableHtmlDomForPage(_pageSelection.CurrentSelection);
			return _domForCurrentPage;
		}

		public void SaveNow()
		{
			_bookSelection.CurrentSelection.SavePage(_domForCurrentPage);
		}

		public void ChangePicture(string id, PalasoImage imageInfo)
		{
			var editor = new PageEditingModel();

			editor.ChangePicture(_bookSelection.CurrentSelection.FolderPath, _domForCurrentPage, id, imageInfo);
			SaveNow();

			//review: this is spagetti
			_bookSelection.CurrentSelection.UpdatePagePreview(_pageSelection.CurrentSelection);

			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
			InvokeUpdatePageList();
		}

		private void InvokeUpdatePageList()
		{
			if (UpdatePageList != null)
			{
				UpdatePageList(this, null);
			}
		}

		public void SetView(EditingView view)
		{
			_view = view;
		}

		public void HandleUserEnteredArea(GeckoElement element)
		{
			var sourceTexts = _pageSelection.CurrentSelection.GetSourceTexts(element.Id);
			if (sourceTexts.Count == 0)
			{
				_view.SetSourceText(string.Empty);
			}
			else
			{
				_view.SetSourceText(_languageSettings.ChooseBestSource(sourceTexts, string.Empty));
			}
		}
	}
			//_book.DeletePage(_pageSelection.CurrentSelection);

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
