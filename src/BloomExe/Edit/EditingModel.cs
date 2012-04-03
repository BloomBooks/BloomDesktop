using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Palaso.IO;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ImageToolbox;
using Gecko;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly LanguageSettings _languageSettings;
		private readonly LibrarySettings _librarySettings;
		private XmlDocument _domForCurrentPage;
		private bool _visible;
		private Book.Book _currentlyDisplayedBook;
		private EditingView _view;
		private List<ContentLanguage> _contentLanguages;


		public event EventHandler UpdatePageList;

		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection,
			LanguageSettings languageSettings,
			TemplateInsertionCommand templateInsertionCommand,
			PageListChangedEvent pageListChangedEvent,
			RelocatePageEvent relocatePageEvent,
			BookRefreshEvent bookRefreshEvent,
			DeletePageCommand deletePageCommand,
			SelectedTabChangedEvent selectedTabChangedEvent,
			LibraryClosing libraryClosingEvent,
			LibrarySettings librarySettings)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_languageSettings = languageSettings;
			_librarySettings = librarySettings;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			templateInsertionCommand.InsertPage += new EventHandler(OnInsertTemplatePage);

			bookRefreshEvent.Subscribe((book) => OnBookSelectionChanged(null, null));
			selectedTabChangedEvent.Subscribe(OnTabChanged);
			deletePageCommand.Implementer=OnDeletePage;
			pageListChangedEvent.Subscribe(x => InvokeUpdatePageList());
			relocatePageEvent.Subscribe(OnRelocatePage);
			libraryClosingEvent.Subscribe(o=>SaveNow());
			_contentLanguages = new List<ContentLanguage>();
		}

		private void OnTabChanged(TabChangedDetails details)
		{
			if(details.From == _view)
			{
				SaveNow();
			}
			//enhance: this might be more reliable than the current EditingView.OnVisibleChanged() for detecting when we move *to* this control.

			_visible = details.To == _view;
		}

		private void OnBookSelectionChanged(object sender, EventArgs e)
		{
			//prevent trying to save this page in whatever comes next
			var wasNull = _domForCurrentPage == null;
			_domForCurrentPage = null;
			_currentlyDisplayedBook = null;
			_view.ClearOutDisplay();
			if(!wasNull)
				InvokeUpdatePageList();
		}

		private void OnDeletePage()
		{
			_domForCurrentPage = null;//prevent us trying to save it later, as the page selection changes
			_currentlyDisplayedBook.DeletePage(_pageSelection.CurrentSelection);
			_view.UpdatePageList();
			UsageReporter.SendNavigationNotice("DeletePage");
		}

		private void OnRelocatePage(RelocatePageInfo info)
		{
			_bookSelection.CurrentSelection.RelocatePage(info.Page, info.IndexOfPageAfterMove);
			UsageReporter.SendNavigationNotice("RelocatePage");
		}

		private void OnInsertTemplatePage(object sender, EventArgs e)
		{
			_bookSelection.CurrentSelection.InsertPageAfter(DeterminePageWhichWouldPrecedeNextInsertion(), sender as Page);
			_view.UpdatePageList();
			//_pageSelection.SelectPage(newPage);
			UsageReporter.SendNavigationNotice("InsertTemplatePage");
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

		public Book.Book CurrentBook
		{
			get { return _bookSelection.CurrentSelection; }
		}

		public bool ShowTranslationPanel
		{
			get
			{
				return _bookSelection.CurrentSelection.HasSourceTranslations;
			}
		}

		public bool ShowTemplatePanel
		{
			get
			{
				if (_librarySettings.IsShellLibrary)
				{
					return true;
				}
				else
				{
				   // return !ShowTranslationPanel;
					return _bookSelection.CurrentSelection.NormallyHasTemplatePages;
				}
			}
		}

		public bool CanDeletePage
		{
			get
			{
				return _pageSelection != null && _pageSelection.CurrentSelection != null &&
					   !_pageSelection.CurrentSelection.Required && _currentlyDisplayedBook!=null && !_currentlyDisplayedBook.LockedExceptForTranslation;
			}

		}

		/// <summary>
		/// These are the languages available for selecting for bilingual and trilingual
		/// </summary>
		public IEnumerable<ContentLanguage> ContentLanguages
		{
			get
			{
				_contentLanguages.Clear();
				_contentLanguages.Add(new ContentLanguage(_librarySettings.VernacularIso639Code, _librarySettings.GetVernacularName("en")){Locked=true, Selected=true});
				//NB: these won't *alway* be tied to teh national and regional languages, but they are for now. We would need more UI, without making for extra complexity
				var item2 = new ContentLanguage(_librarySettings.NationalLanguage1Iso639Code, _librarySettings.GetNationalLanguage1Name("en")) {Selected = _bookSelection.CurrentSelection.MultilingualContentLanguage2 == _librarySettings.NationalLanguage1Iso639Code};
				_contentLanguages.Add(item2);
				if (!String.IsNullOrEmpty(_librarySettings.NationalLanguage2Iso639Code))
				{
					//NB: this could be the 2nd language (when the national 1 language is not selected)
					bool selected = _bookSelection.CurrentSelection.MultilingualContentLanguage2 ==_librarySettings.NationalLanguage2Iso639Code ||
									_bookSelection.CurrentSelection.MultilingualContentLanguage3 ==_librarySettings.NationalLanguage2Iso639Code;
					var item3 = new ContentLanguage(_librarySettings.NationalLanguage2Iso639Code, _librarySettings.GetNationalLanguage2Name("en")) { Selected = selected };
					_contentLanguages.Add(item3);
				}
				return _contentLanguages;
			}
		}

		public IEnumerable<string> GetPageSizeAndOrientationChoices()
		{
			return CurrentBook.GetPageSizeAndOrientationChoices();
		}

		public void SetPaperSizeAndOrientation(string paperSizeAndOrientationName)
		{
			SaveNow();
			CurrentBook.SetPaperSizeAndOrientation(paperSizeAndOrientationName);
			CurrentBook.PrepareForEditing();
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
		}

		/// <summary>
		/// user has selected or de-selected a content language
		/// </summary>
		public void ContentLanguagesSelectionChanged()
		{
			string l2 = null;
			string l3 = null;
			foreach (var language in _contentLanguages)
			{
				if (language.Locked)
					continue; //that's the vernacular
				if(language.Selected && l2==null)
					l2 = language.Iso639Code;
				else if(language.Selected)
				{
					l3 = language.Iso639Code;
					break;
				}
			}

			//Reload to display these changes
			SaveNow();
			CurrentBook.SetMultilingualContentLanguages(l2, l3);
			CurrentBook.PrepareForEditing();
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
		}

		public class ContentLanguage
		{
			public readonly string Iso639Code;
			public readonly string Name;

			public ContentLanguage(string iso639Code, string name)
			{
				Iso639Code = iso639Code;
				Name = name;
			}
			public override string ToString()
			{
				return Name;
			}

			public bool Selected;
			public bool Locked;
		}

		public bool GetBookHasChanged()
		{
			return _currentlyDisplayedBook != CurrentBook;
		}

		public void ActualVisibiltyChanged(bool unused)
		{
			//I don't trust this value in. I'm relying instead on setting this via the SelectedTabChangedEvent.
			//_visible = visible;

			Debug.WriteLine("EditingModel._visible =" + _visible);
			if (!_visible || _currentlyDisplayedBook == CurrentBook)
			{
				//review: it's not clear when this is needed... the browser validating also calls save, but it doesn't always validate.
				//perhaps we should drop that validating call, and just use this?
				SaveNow();
				return;
			}

			if(_currentlyDisplayedBook != CurrentBook)
			{
				CurrentBook.PrepareForEditing();
			}

			_currentlyDisplayedBook = CurrentBook;
			//if (_bookSelection.CurrentSelection.Type == Book.BookType.Publication)
			{
				var page = _bookSelection.CurrentSelection.FirstPage;
				if (page != null)
					_pageSelection.SelectPage(page);
			}
			if (_view != null)
			{
				if(ShowTemplatePanel)
					_view.UpdateTemplateList();
				_view.UpdatePageList();
			}
		}

		void OnPageSelectionChanged(object sender, EventArgs e)
		{
			if (_view != null)
			{
				if(_domForCurrentPage!=null)
					SaveNow();
				_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
				//_view.ClearSourceText();
			}
		}

		public XmlDocument GetXmlDocumentForCurrentPage()
		{

			_domForCurrentPage = _bookSelection.CurrentSelection.GetEditableHtmlDomForPage(_pageSelection.CurrentSelection);
			return _domForCurrentPage;
		}

		public void SaveNow()
		{
			if (_domForCurrentPage != null)
			{
				_view.ReadEditableAreasNow();
				_bookSelection.CurrentSelection.SavePage(_domForCurrentPage);
			}
		}

		public void ChangePicture(GeckoElement img, PalasoImage imageInfo)
		{
			var editor = new PageEditingModel();

			editor.ChangePicture(_bookSelection.CurrentSelection.FolderPath, _domForCurrentPage, img, imageInfo);

			//We have a problem where if we save at this point, any text changes are lost.
			//The hypothesis is that the "onblur" javascript has not run, so the value of the textareas have not yet changed.

			//_view.ReadEditableAreasNow();

			//SaveNow();//have to save now because we're going to reload the page to show the new picture

			//review: this is spagetti
			_bookSelection.CurrentSelection.UpdatePagePreview(_pageSelection.CurrentSelection);

			//_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
			//InvokeUpdatePageList();
			UsageReporter.SendNavigationNotice("ChangePicture");
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


		public IPage DeterminePageWhichWouldPrecedeNextInsertion()
		{
			if (_view != null)
			{
				var pagesStartingWithCurrentSelection =
					_bookSelection.CurrentSelection.GetPages().SkipWhile(p => p.Id != _pageSelection.CurrentSelection.Id);
				var candidates = pagesStartingWithCurrentSelection.ToArray();
				for (int i = 0; i < candidates.Length - 1; i++)
				{
					if (!candidates[i + 1].Required)
					{
						return candidates[i];
					}
				}
				return _bookSelection.CurrentSelection.GetPages().LastOrDefault();
			}
			return null;
		}

		public bool CanChangeImages()
		{
			return !_currentlyDisplayedBook.LockedExceptForTranslation;
		}


		public string GetCurrentPageSizeAndOrientation()
		{
			return CurrentBook.GetSizeAndOrientation().ToString();
		}
//
//    	public static string GetPathToStylizer()
//    	{
//    		return FileLocator.LocateInProgramFiles("Stylizer.exe", false, new string[] { "Skybound Stylizer 5" });
//    	}
//
//    	public void OpenPageInStylizer()
//    	{
//			string path = Path.GetTempFileName();
//    		var dom = GetXmlDocumentForCurrentPage();
//			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom);
//    		XmlHtmlConverter.SaveDOMAsHtml5(dom, path);
//			Process.Start(GetPathToStylizer(), path);
//    	}

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
