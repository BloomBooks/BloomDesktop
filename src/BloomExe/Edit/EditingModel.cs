using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SendReceive;
using Bloom.ToPalaso.Experimental;
using Palaso.Progress;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.UI.WindowsForms.ImageToolbox;
using Gecko;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly LanguageSettings _languageSettings;
		private readonly DeletePageCommand _deletePageCommand;
		private readonly CollectionSettings _collectionSettings;
		private readonly SendReceiver _sendReceiver;
		private HtmlDom _domForCurrentPage;
		public bool Visible;
		private Book.Book _currentlyDisplayedBook;
		private EditingView _view;
		private List<ContentLanguage> _contentLanguages;
		private IPage _previouslySelectedPage;
		private bool _inProcessOfDeleting;

		//public event EventHandler UpdatePageList;

		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection,
			LanguageSettings languageSettings,
			TemplateInsertionCommand templateInsertionCommand,
			PageListChangedEvent pageListChangedEvent,
			RelocatePageEvent relocatePageEvent,
			BookRefreshEvent bookRefreshEvent,
			DeletePageCommand deletePageCommand,
			SelectedTabChangedEvent selectedTabChangedEvent,
			SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent,
			LibraryClosing libraryClosingEvent,
			CollectionSettings collectionSettings,
			SendReceiver sendReceiver)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_languageSettings = languageSettings;
			_deletePageCommand = deletePageCommand;
			_collectionSettings = collectionSettings;
			_sendReceiver = sendReceiver;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			templateInsertionCommand.InsertPage += new EventHandler(OnInsertTemplatePage);

			bookRefreshEvent.Subscribe((book) => OnBookSelectionChanged(null, null));
			selectedTabChangedEvent.Subscribe(OnTabChanged);
			selectedTabAboutToChangeEvent.Subscribe(OnTabAboutToChange);
			deletePageCommand.Implementer=OnDeletePage;
			pageListChangedEvent.Subscribe(x => _view.UpdatePageList(false));
			relocatePageEvent.Subscribe(OnRelocatePage);
			libraryClosingEvent.Subscribe(o=>SaveNow());
			_contentLanguages = new List<ContentLanguage>();
		}


		/// <summary>
		/// we need to guarantee that we save *before* any other tabs try to update, hence this "about to change" event
		/// </summary>
		/// <param name="details"></param>
		private void OnTabAboutToChange(TabChangedDetails details)
		{
			if (details.From == _view)
			{
				SaveNow();
				//note: if they didn't actually change anything, Chorus is not going to actually do a checkin, so this
				//won't polute the history
				_sendReceiver.CheckInNow(string.Format("Edited '{0}'", _bookSelection.CurrentSelection.TitleBestForUserDisplay));

			}
		}

		private void OnTabChanged(TabChangedDetails details)
		{
			_previouslySelectedPage = null;
			Visible = details.To == _view;
			_view.OnVisibleChanged(Visible);
		}

		private void OnBookSelectionChanged(object sender, EventArgs e)
		{
			//prevent trying to save this page in whatever comes next
			var wasNull = _domForCurrentPage == null;
			_domForCurrentPage = null;
			_currentlyDisplayedBook = null;
			if (Visible)
			{
				_view.ClearOutDisplay();
				if (!wasNull)
					_view.UpdatePageList(false);
			}
		}

		private void OnDeletePage()
		{
			try
			{
				_inProcessOfDeleting = true;
				_domForCurrentPage = null; //prevent us trying to save it later, as the page selection changes
				_currentlyDisplayedBook.DeletePage(_pageSelection.CurrentSelection);
				_view.UpdatePageList(false);
				Logger.WriteEvent("DeletePage");
				UsageReporter.SendNavigationNotice("DeletePage");
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,
																 "Could not delete that page. Try quiting Bloom, run it again, and then attempt to delete the page again. And please click 'details' below and report this to us.");
			}
			finally
			{
				_inProcessOfDeleting = false;
			}
		}

		private void OnRelocatePage(RelocatePageInfo info)
		{
			info.Cancel = !_bookSelection.CurrentSelection.RelocatePage(info.Page, info.IndexOfPageAfterMove);
			if(!info.Cancel)
			{
				UsageReporter.SendNavigationNotice("RelocatePage");
				Logger.WriteEvent("RelocatePage");
			}
		}

		private void OnInsertTemplatePage(object sender, EventArgs e)
		{
			_bookSelection.CurrentSelection.InsertPageAfter(DeterminePageWhichWouldPrecedeNextInsertion(), sender as Page);
			_view.UpdatePageList(false);
			//_pageSelection.SelectPage(newPage);
			UsageReporter.SendNavigationNotice("InsertTemplatePage");
			Logger.WriteEvent("InsertTemplatePage");
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
//                if (_librarySettings.IsSourceCollection)
//                {
//                    return true;
//                }
//                else
//                {

					return _bookSelection.CurrentSelection.UseSourceForTemplatePages;
//                }
			}
		}

		public bool CanDeletePage
		{
			get
			{
				return _pageSelection != null && _pageSelection.CurrentSelection != null &&
					   !_pageSelection.CurrentSelection.Required && _currentlyDisplayedBook!=null
					   && !_currentlyDisplayedBook.LockedDown;//this clause won't work when we start allowing custom front/backmatter pages
			}

		}

		/// <summary>
		/// These are the languages available for selecting for bilingual and trilingual
		/// </summary>
		public IEnumerable<ContentLanguage> ContentLanguages
		{
			get
			{
				//_contentLanguages.Clear();		CAREFUL... the tags in the dropdown are ContentLanguage's, so changing them breaks that binding
				if (_contentLanguages.Count() == 0)
				{
					_contentLanguages.Add(new ContentLanguage(_collectionSettings.Language1Iso639Code,
															  _collectionSettings.GetVernacularName("en"))
											{Locked = true, Selected = true});

					//NB: these won't *alway* be tied to teh national and regional languages, but they are for now. We would need more UI, without making for extra complexity
					var item2 = new ContentLanguage(_collectionSettings.Language2Iso639Code,
													_collectionSettings.GetLanguage2Name("en"))
									{
//					            		Selected =
//					            			_bookSelection.CurrentSelection.MultilingualContentLanguage2 ==
//					            			_librarySettings.Language2Iso639Code
									};
					_contentLanguages.Add(item2);
					if (!String.IsNullOrEmpty(_collectionSettings.Language3Iso639Code))
					{
						//NB: this could be the 2nd language (when the national 1 language is not selected)
//						bool selected = _bookSelection.CurrentSelection.MultilingualContentLanguage2 ==
//						                _librarySettings.Language3Iso639Code ||
//						                _bookSelection.CurrentSelection.MultilingualContentLanguage3 ==
//						                _librarySettings.Language3Iso639Code;
						var item3 = new ContentLanguage(_collectionSettings.Language3Iso639Code,
														_collectionSettings.GetLanguage3Name("en"));// {Selected = selected};
						_contentLanguages.Add(item3);
					}
				}
				//update the selections
				_contentLanguages.Where(l => l.Iso639Code == _collectionSettings.Language2Iso639Code).First().Selected =
					_bookSelection.CurrentSelection.MultilingualContentLanguage2 ==_collectionSettings.Language2Iso639Code;


				var contentLanguageMatchingNatLan2 =
					_contentLanguages.Where(l => l.Iso639Code == _collectionSettings.Language3Iso639Code).FirstOrDefault();

				if(contentLanguageMatchingNatLan2!=null)
				{
					contentLanguageMatchingNatLan2.Selected =
					_bookSelection.CurrentSelection.MultilingualContentLanguage2 ==_collectionSettings.Language3Iso639Code
					|| _bookSelection.CurrentSelection.MultilingualContentLanguage3 == _collectionSettings.Language3Iso639Code;
				}


				return _contentLanguages;
			}
		}

		public IEnumerable<Layout> GetLayoutChoices()
		{
			foreach(var layout in CurrentBook.GetLayoutChoices())
			{
				yield return layout;
			}
		}

		public void SetLayout(Layout layout)
		{
			SaveNow();
			CurrentBook.SetLayout(layout);
			CurrentBook.PrepareForEditing();
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

			_view.UpdatePageList(true);//counting on this to redo the thumbnails
		}

		/// <summary>
		/// user has selected or de-selected a content language
		/// </summary>
		public void ContentLanguagesSelectionChanged()
		{
			Logger.WriteEvent("Changing Content Languages");
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
			_view.UpdatePageList(true);//counting on this to redo the thumbnails

			Logger.WriteEvent("ChangingContentLanguages");
			UsageReporter.SendNavigationNotice("ChangingContentLanguages");
		}

		public int NumberOfDisplayedLanguages
		{
			get { return ContentLanguages.Where(l => l.Selected).Count(); }
		}

		public bool CanEditCopyrightAndLicense
		{
			get { return CurrentBook.CanChangeLicense; }

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

		public void ViewVisibleNowDoSlowStuff()
		{
			if(_currentlyDisplayedBook != CurrentBook)
			{
				CurrentBook.PrepareForEditing();
			}

			_currentlyDisplayedBook = CurrentBook;

			var page = _bookSelection.CurrentSelection.FirstPage;
			if (page != null)
				_pageSelection.SelectPage(page);

			if (_view != null)
			{
				if(ShowTemplatePanel)
				{
					_view.UpdateTemplateList();
				}
				_view.UpdatePageList(false);
			}
		}

		void OnPageSelectionChanged(object sender, EventArgs e)
		{
			Logger.WriteMinorEvent("changing page selection");
			if (_view != null)
			{
				if (_previouslySelectedPage != null && _domForCurrentPage != null)
				{
					if(!_inProcessOfDeleting)//this is a mess.. before if you did a delete and quickly selected another page, events transpired such that you're now trying to save a deleted page
						SaveNow();
					_view.UpdateThumbnailAsync(_previouslySelectedPage);
				}
				_previouslySelectedPage = _pageSelection.CurrentSelection;
				_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
				_deletePageCommand.Enabled = !_pageSelection.CurrentSelection.Required;
			}

			GC.Collect();//i put this in while looking for memory leaks, feel free to remove it.
		}

		public void RefreshDisplayOfCurrentPage()
		{
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
		}

		public HtmlDom GetXmlDocumentForCurrentPage()
		{
			_domForCurrentPage = _bookSelection.CurrentSelection.GetEditableHtmlDomForPage(_pageSelection.CurrentSelection);
			return _domForCurrentPage;
		}

		public void SaveNow()
		{
			if (_domForCurrentPage != null)
			{
				_view.CleanHtmlAndCopyToPageDom();
				_bookSelection.CurrentSelection.SavePage(_domForCurrentPage);
			}
		}

		public void ChangePicture(GeckoElement img, PalasoImage imageInfo, IProgress progress)
		{
			try
			{
				Logger.WriteMinorEvent("Starting ChangePicture {0}...", imageInfo.FileName);
				var editor = new PageEditingModel();
				editor.ChangePicture(_bookSelection.CurrentSelection.FolderPath, img, imageInfo, progress);

				//we have to save so that when asked by the thumbnailer, the book will give the proper image
				SaveNow();
				//but then, we need the non-cleaned version back there
				_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

				_view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
				Logger.WriteMinorEvent("Finished ChangePicture {0} (except for async thumbnail) ...", imageInfo.FileName);
				UsageReporter.SendNavigationNotice("ChangePicture");
				Logger.WriteEvent("ChangePicture {0}...", imageInfo.FileName);

			}
			catch (Exception e)
			{
				ErrorReport.NotifyUserOfProblem(e, "Could not change the picture");
			}
		}

//        private void InvokeUpdatePageList()
//        {
//            if (UpdatePageList != null)
//            {
//                UpdatePageList(this, null);
//            }
//        }

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
				var pages = _bookSelection.CurrentSelection.GetPages();
				// ReSharper disable PossibleMultipleEnumeration
				if (!pages.Any())
				{
					var exception = new ApplicationException(
						string.Format(
							@"_bookSelection.CurrentSelection.GetPages() gave no pages (BL-262 repro).
									  Book is '{0}'\r\nErrors known to book=[{1}]\r\n{2}\r\n{3}",
							_bookSelection.CurrentSelection.TitleBestForUserDisplay,
							_bookSelection.CurrentSelection.CheckForErrors(),
							_bookSelection.CurrentSelection.RawDom.OuterXml,
							new StackTrace().ToString()));

					ErrorReport.NotifyUserOfProblem(exception,
													"There was a problem looking through the pages of this book. If you can send emails, please click 'details' and send this report to the developers.");
					return null;
				}
				IPage lastGuyWHoCanHaveAnInsertionAfterHim = pages.Last(p => !p.IsBackMatter);
				// ReSharper restore PossibleMultipleEnumeration
				return lastGuyWHoCanHaveAnInsertionAfterHim;
			}
			return null;
		}

		public bool CanChangeImages()
		{
			return _currentlyDisplayedBook.CanChangeImages;
		}


		public Layout GetCurrentLayout()
		{
			return CurrentBook.GetLayout();
		}

#if TooExpensive
		public void BrowserFocusChanged()
		{
			//review: will this be too slow on some machines? It's just a luxury to update the thumbnail even when you tab to a different field
			SaveNow();
			_view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
		}
#endif

		public void CopyImageMetadataToWholeBook(Metadata metadata)
		{
			using (var dlg = new ProgressDialogForeground())//REVIEW: this foreground dialog has known problems in other contexts... it was used here because of its ability to handle exceptions well. TODO: make the background one handle exceptions well
			{
				dlg.ShowAndDoWork(progress => CurrentBook.CopyImageMetadataToWholeBookAndSave(metadata, progress));
			}
		}

		public string GetFontAvailabilityMessage()
		{
			var name = _collectionSettings.DefaultLanguage1FontName.ToLower();

			if(null == FontFamily.Families.FirstOrDefault(f => f.Name.ToLower() == name))
			{
				var s = Localization.LocalizationManager.GetString("EditTab.FontMissing",
														   "The current selected " +
														   "font is '{0}', but it is not installed on this computer. Some other font will be used.");
				return string.Format(s, _collectionSettings.DefaultLanguage1FontName);
			}
			return null;
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
