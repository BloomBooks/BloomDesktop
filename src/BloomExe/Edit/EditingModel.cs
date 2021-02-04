//#define MEMORYCHECK
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ToPalaso.Experimental;
using Bloom.Api;
using Bloom.MiscUI;
using Bloom.ToPalaso;
using Bloom.Utils;
using Bloom.web.controllers;
using BloomTemp;
using DesktopAnalytics;
using Gecko;
using L10NSharp;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Reporting;
using SIL.Xml;

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly DuplicatePageCommand _duplicatePageCommand;
		private readonly DeletePageCommand _deletePageCommand;
		private readonly LocalizationChangedEvent _localizationChangedEvent;
		private readonly CollectionSettings _collectionSettings;
		private readonly SourceCollectionsList _sourceCollectionsList;
		//private readonly SendReceiver _sendReceiver;
		private HtmlDom _domForCurrentPage;
		// We dispose of this when we create a new one. It may hang around a little longer than needed, but memory
		// is the only resource being used, and there is only one instance of this object.
		private SimulatedPageFile _currentPage;
		public bool Visible;
		private Book.Book _currentlyDisplayedBook;
		private Book.Book _bookForToolboxContent;
		private EditingView _view;
		private List<ContentLanguage> _contentLanguages;
		private IPage _previouslySelectedPage;
		private bool _inProcessOfDeleting;
		private bool _inProcessOfLoading;
		private string _toolboxFolder;
		private BloomServer _server;
		private readonly BloomWebSocketServer _webSocketServer;
		private Dictionary<string, IPage> _templatePagesDict;
		internal IPage PageChangingLayout; // used to save the page on which the choose different layout command was invoked while the dialog is active.
		// This event fires after the EditingModel has finished responding to a PageSelection change.
		internal event EventHandler PageSelectModelChangesComplete;

		// These variables are not thread-safe. Access only on UI thread.
		private bool _inProcessOfSaving;
		private List<Action> _tasksToDoAfterSaving = new List<Action>();
		// Perhaps a bit hack-ish, but this causes a full save to be done when our datadiv has been modified
		// but it's not obvious from the dataset changes. If we make new 'data-derived' divs someday, changing them
		// must set this flag to ensure the information gets saved properly.
		private bool _pageHasUnsavedDataDerivedChange;

		readonly List<string> _activeStandardListeners = new List<string>();

		internal const string PageScalingDivId = "page-scaling-container";

		//public event EventHandler UpdatePageList;

		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection,
			TemplateInsertionCommand templateInsertionCommand,
			PageListChangedEvent pageListChangedEvent,
			RelocatePageEvent relocatePageEvent,
			BookRefreshEvent bookRefreshEvent,
			PageRefreshEvent pageRefreshEvent,
			DuplicatePageCommand duplicatePageCommand,
			DeletePageCommand deletePageCommand,
			SelectedTabChangedEvent selectedTabChangedEvent,
			SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent,
			LibraryClosing libraryClosingEvent,
			LocalizationChangedEvent localizationChangedEvent,
			CollectionSettings collectionSettings,
			//SendReceiver sendReceiver,
			BloomServer server,
			BloomWebSocketServer webSocketServer,
			SourceCollectionsList sourceCollectionsList)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_duplicatePageCommand = duplicatePageCommand;
			_deletePageCommand = deletePageCommand;
			_collectionSettings = collectionSettings;
			//_sendReceiver = sendReceiver;
			_server = server;
			_webSocketServer = webSocketServer;
			_sourceCollectionsList = sourceCollectionsList;
			_templatePagesDict = null;

			bookSelection.SelectionChanged += OnBookSelectionChanged;
			pageSelection.SelectionChanged += OnPageSelectionChanged;
			pageSelection.SelectionChanging += OnPageSelectionChanging;
			templateInsertionCommand.InsertPage += OnInsertPage;

			bookRefreshEvent.Subscribe((book) =>
			{
				if (book == CurrentBook)
				{
					OnBookSelectionChanged(null, null);
				}
			});
			pageRefreshEvent.Subscribe((PageRefreshEvent.SaveBehavior behavior) =>
			{
				switch (behavior)
				{
					case PageRefreshEvent.SaveBehavior.SaveBeforeRefresh:
						RethinkPageAndReloadIt();
						break;

					case PageRefreshEvent.SaveBehavior.JustRedisplay:
						RefreshDisplayOfCurrentPage();
						break;
				}
			});

			selectedTabChangedEvent.Subscribe(OnTabChanged);
			selectedTabAboutToChangeEvent.Subscribe(OnTabAboutToChange);
			duplicatePageCommand.Implementer = OnDuplicatePage;
			deletePageCommand.Implementer = OnDeletePage;
			pageListChangedEvent.Subscribe(x => _view.UpdatePageList(false));
			relocatePageEvent.Subscribe(OnRelocatePage);
			libraryClosingEvent.Subscribe(o =>
			{
				if (Visible)
					SaveNow();
			});
			localizationChangedEvent.Subscribe(o =>
			{
				//this is visible was added for https://jira.sil.org/browse/BL-267, where the edit tab has never been
				//shown so the view has never been full constructed, so we're not in a good state to do a refresh
				if (Visible)
				{
					SaveNow();
					_view.UpdateButtonLocalizations();
					RefreshDisplayOfCurrentPage();
					//_view.UpdateDisplay();
					_view.UpdatePageList(false);
				}
				else if (_view != null)
				{
					// otherwise changing UI language in Publish tab (for instance) won't update these localizations
					_view.UpdateButtonLocalizations();
				}
			});
			_contentLanguages = new List<ContentLanguage>();
		}

		private Form _oldActiveForm;
		private XmlElement _pageDivFromCopyPage;
		private string _bookPathFromCopyPage;

		internal BloomWebSocketServer EditModelSocketServer { get { return _webSocketServer; } }

		/// <summary>
		/// we need to guarantee that we save *before* any other tabs try to update, hence this "about to change" event
		/// </summary>
		/// <param name="details"></param>
		private void OnTabAboutToChange(TabChangedDetails details)
		{
			if (details.From == _view)
			{
				SaveNow();
				_view.RunJavaScript("if (typeof(FrameExports) !=='undefined') {FrameExports.getPageFrameExports().disconnectForGarbageCollection();}");
				// This bizarre behavior prevents BL-2313 and related problems.
				// For some reason I cannot discover, switching tabs when focus is in the Browser window
				// causes Bloom to get deactivated, which prevents various controls from working.
				// Moreover, it seems (BL-2329) that if the user types Alt-F4 while whatever-it-is is active,
				// things get into a very bad state indeed. So arrange to re-activate ourselves as soon as the dust settles.
				_oldActiveForm = Form.ActiveForm;
				Application.Idle += ReactivateFormOnIdle;
				//note: if they didn't actually change anything, Chorus is not going to actually do a checkin, so this
				//won't pollute the history
				#if Chorus
					_sendReceiver.CheckInNow(string.Format("Edited '{0}'", CurrentBook.TitleBestForUserDisplay));
				#endif
			}
		}

		private void ReactivateFormOnIdle(object sender, EventArgs eventArgs)
		{
			Application.Idle -= ReactivateFormOnIdle;
			if (_oldActiveForm != null)
				_oldActiveForm.Activate();
		}

		private void OnTabChanged(TabChangedDetails details)
		{
			_previouslySelectedPage = null;
			Visible = details.To == _view;
			_view.OnVisibleChanged(Visible);
		}

		private void OnBookSelectionChanged(object sender, BookSelectionChangedEventArgs bookSelectionChangedEventArgs)
		{
			//prevent trying to save this page in whatever comes next
			var wasNull = _domForCurrentPage == null;
			_domForCurrentPage = null;
			_currentlyDisplayedBook = null;
			_templatePagesDict = null;
			if (Visible)
			{
				_view.ClearOutDisplay();
				if (!wasNull)
					_view.UpdatePageList(false);
			}
		}

		internal void OnDuplicatePage()
		{
			DuplicatePage(_pageSelection.CurrentSelection);
		}

		internal void DuplicatePage(IPage page)
		{
			try
			{
				SaveNow(); //ensure current page is saved first
				_domForCurrentPage = null; //prevent us trying to save it later, as the page selection changes
				_currentlyDisplayedBook.DuplicatePage(page);
				// Book.DuplicatePage() updates the page list so we don't need to do it here.
				// (See http://issues.bloomlibrary.org/youtrack/issue/BL-3715.)
				//_view.UpdatePageList(false);
				Logger.WriteEvent("Duplicate Page");
				Analytics.Track("Duplicate Page");
			}
			catch (Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error,
					"Could not duplicate that page. Try quiting Bloom, run it again, and then attempt to duplicate the page again. And please click 'details' below and report this to us.");
			}
		}

		internal void OnDeletePage()
		{
			DeletePage(_pageSelection.CurrentSelection);
		}

		internal void DeletePage(IPage page)
		{
			// This can only be called on the UI thread in response to a user button click.
			// If that ever changed we might need to arrange locking for access to _inProcessOfSaving and _tasksToDoAfterSaving.
			Debug.Assert(!_view.InvokeRequired);
			if (_inProcessOfSaving && page == _pageSelection.CurrentSelection)
			{
				// Somehow (BL-431) it's possible that a Save is still in progress when we start executing a delete page.
				// If this happens, to prevent crashes we need to let the Save complete before we go ahead with the delete.
				_tasksToDoAfterSaving.Add(OnDeletePage);
				return;
			}
			try
			{
				try
				{
					// BL-4035 Save any style changes to the book before deleting the page.
					SaveNow();
				}
				catch(Exception saveError)
				{
					// we don't want to prevent deleting a problematic page, so just show a toast
					NonFatalProblem.Report(ModalIf.Alpha, PassiveIf.All, "Error during pre-delete save", exception: saveError);
				}

				_inProcessOfDeleting = true;
				_domForCurrentPage = null; //prevent us trying to save it later, as the page selection changes
				_currentlyDisplayedBook.DeletePage(page);
				//_view.UpdatePageList(false);  DeletePage calls this via pageListChangedEvent.  See BL-3632 for trouble this causes.
				Logger.WriteEvent("Delete Page");
				Analytics.Track("Delete Page");
			}
			catch (Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error,
					"Could not delete that page. Try quiting Bloom, run it again, and then attempt to delete the page again. And please click 'details' below and report this to us.");
			}
			finally
			{
				_inProcessOfDeleting = false;
			}
		}

		private void OnRelocatePage(RelocatePageInfo info)
		{
			info.Cancel = !CurrentBook.RelocatePage(info.Page, info.IndexOfPageAfterMove);
			if(!info.Cancel)
			{
				// Moving a page actually changes its html to have the new left/right side and page number,
				// The Book takes care of that, but now we need to actually reload it from the dom.
				RefreshDisplayOfCurrentPage();

				Analytics.Track("Relocate Page");
				Logger.WriteEvent("Relocate Page");
			}
		}

		/// <summary>
		/// This is used both to insert pages from the AddPageDialog, and also "paste page"
		/// </summary>
		private void OnInsertPage(object page, PageInsertEventArgs e)
		{
			CurrentBook.InsertPageAfter(DeterminePageWhichWouldPrecedeNextInsertion(), page as Page, e.NumberToAdd);
			//_view.UpdatePageList(false);  InsertPageAfter calls this via pageListChangedEvent.  See BL-3632 for trouble this causes.
			//_pageSelection.SelectPage(newPage);
			if(e.FromTemplate)
			{
				try
				{
					Analytics.Track("Insert Template Page", new Dictionary<string, string>
					{
						{"template-source", (page as IPage).Book.Title},
						{"page", (page as IPage).Caption}
					});
				}
				catch(Exception)
				{
				}
			}
			Logger.WriteEvent("InsertTemplatePage");
		}

		public bool HaveCurrentEditableBook
		{
			get { return CurrentBook != null; }
		}

		public Book.Book CurrentBook
		{
			get { return _bookSelection.CurrentSelection; }
		}

		public bool CanAddPages
		{
			get { return !(CurrentBook.LockedDown || CurrentBook.IsCalendar); }
		}

		public bool CanDuplicatePage
		{
			get
			{
				return _pageSelection != null && _pageSelection.CurrentSelection != null &&
					   !_pageSelection.CurrentSelection.Required && _currentlyDisplayedBook != null
					   && !_currentlyDisplayedBook.LockedDown;//this clause won't work when we start allowing custom front/backmatter pages
			}
		}

		public bool CanCopyPage
		{
			// Currently we don't want to allow copying xmatter pages. If we ever do, some research and non-trivial change
			// will probably be needed, not just removing the restriction. Xmatter pages have classes set on them which will cause
			// Bloom to delete them when the book is next opened. They also tend to be singletons, which may cause problems if
			// we let the user make multiple ones.
			// Note that we don't need the editability restrictions here, since copy doesn't modify this book.
			get { return _pageSelection != null && _pageSelection.CurrentSelection != null && !_pageSelection.CurrentSelection.IsXMatter; }
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
					// TODO: use BookData.GetAllBookLanguages() once we figure more out about how to handle more than
					// the three traditional languages.
					_contentLanguages.Add(new ContentLanguage(_bookSelection.CurrentSelection.BookData.Language1) { Locked = true, Selected = true });

					//NB: these won't *always* be tied to the national and regional languages, but they are for now. We would need more UI, without making for extra complexity
					var item2 = new ContentLanguage(_bookSelection.CurrentSelection.BookData.Language2);
					_contentLanguages.Add(item2);
					if (!String.IsNullOrEmpty(_bookSelection.CurrentSelection.BookData.Language3.Iso639Code))
					{
						var item3 = new ContentLanguage(_bookSelection.CurrentSelection.BookData.Language3);
						_contentLanguages.Add(item3);
					}
				}
				//update the selections
				var lang2 = _contentLanguages.FirstOrDefault(l => l.Iso639Code == _bookSelection.CurrentSelection.BookData.Language2.Iso639Code);
				if (lang2 != null)
					lang2.Selected = CurrentBook.MultilingualContentLanguage2 == _bookSelection.CurrentSelection.BookData.Language2.Iso639Code;
				else
					Logger.WriteEvent("Found no Lang2 in ContentLanguages; count= " + _contentLanguages.Count);

				//the first language is always selected. This covers the common situation in shellbook collections where
				//we have English as both the 1st and national language. https://jira.sil.org/browse/BL-756
				var lang1 = _contentLanguages.FirstOrDefault(l => l.Iso639Code == _bookSelection.CurrentSelection.BookData.Language1.Iso639Code);
				if (lang1 != null)
					lang1.Selected = true;
				else
					Logger.WriteEvent("Hit BL-2780 condition in ContentLanguages; count= " + _contentLanguages.Count);

				var contentLanguageMatchingNatLan2 =
					_contentLanguages.Where(l => l.Iso639Code == _bookSelection.CurrentSelection.BookData.Language3.Iso639Code).FirstOrDefault();

				if(contentLanguageMatchingNatLan2!=null)
				{
					contentLanguageMatchingNatLan2.Selected =
					CurrentBook.MultilingualContentLanguage2 ==_bookSelection.CurrentSelection.BookData.Language3.Iso639Code
					|| CurrentBook.MultilingualContentLanguage3 == _bookSelection.CurrentSelection.BookData.Language3.Iso639Code;
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
			var changedOrientation = CurrentBook.GetLayout().SizeAndOrientation.IsLandScape !=
			                         layout.SizeAndOrientation.IsLandScape;
			CurrentBook.SetLayout(layout);
			if (changedOrientation)
			{
				// We need to update the xmatter, since this process selects images to display based on orientation.
				CurrentBook.BringBookUpToDate(new NullProgress());
				// That wrecks everything. In particular guids stored in Page objects are obsolete.
				// Simulate switching to collection mode, force discarding everything problematic, and reinitialize.
				_view.OnVisibleChanged(false);
				_currentlyDisplayedBook = null;
				_previouslySelectedPage = null;
				_view.OnVisibleChanged(true);
				// If the Add Page dialog is open, we can still change layout.  The OnVisibleChanged calls close the dialog,
				// but can leave the PageListView disabled.  See https://issues.bloomlibrary.org/youtrack/issue/BL-6554.
				_view.SetModalState(false);
				return;
			}
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
			GetMultilingualContentLanguages(out l2, out l3);

			//Reload to display these changes
			CurrentBook.SetMultilingualContentLanguages(l2, l3);	// set langs before saving page
			SaveNow();
			CurrentBook.PrepareForEditing();
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
			_view.UpdatePageList(true);//counting on this to redo the thumbnails

			Logger.WriteEvent("ChangingContentLanguages");
			Analytics.Track("Change Content Languages");
		}

		// Get current MultilingualContentLanguage settings based on what's been recently checked/unchecked.
		// N.B. Unless we're calling this from a more general display update we do NOT want to update ContentLanguages
		// first, as that will change the 'checked' status back to what it was.
		private void GetMultilingualContentLanguages(out string lang2iso, out string lang3iso)
		{
			lang2iso = null;
			lang3iso = null;
			foreach (var language in _contentLanguages)
			{
				if (language.Locked)
					continue; //that's the vernacular (language1)
				if(language.Selected && lang2iso==null)
					lang2iso = language.Iso639Code;
				else if(language.Selected)
				{
					lang3iso = language.Iso639Code;
					break;
				}
			}
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
			private readonly WritingSystem _ws;

			public ContentLanguage(WritingSystem ws)
			{
				Iso639Code = ws.Iso639Code;
				Name = ws.Name;
				IsRtl = ws.IsRightToLeft;
				_ws = ws;
			}
			public override string ToString()
			{
				return _ws.Name;
			}

			public bool Selected;
			public bool Locked;
			public bool IsRtl;
		}

		public bool GetBookHasChanged()
		{
			return _currentlyDisplayedBook != CurrentBook;
		}

		public void ViewVisibleNowDoSlowStuff()
		{
			if (_currentlyDisplayedBook != CurrentBook)
			{
				if (_contentLanguages.Count == 0)
				{
					// BL-5973 GetMultilingualContentLanguages() doesn't want to update _contentLanguages
					// normally, but in this case we do.
					var dummy = ContentLanguages; // updates _contentLanguages based on CurrentBook and collection settings
				}
				// Reset the book's languages in case the user changed the collection's languages.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-5444.
				string lang2iso, lang3iso;
				GetMultilingualContentLanguages(out lang2iso, out lang3iso);
				CurrentBook.SetMultilingualContentLanguages(lang2iso, lang3iso);
				CurrentBook.PrepareForEditing();
				// As of 4.9, we are adding a hook here to do one-time maintenance to books, based on a
				// metadata 'maintenanceLevel'.
				// 0 -> 1 Deal with overlarge images.
				// 1 -> 2 Remove comical SVGs associated with 'none' style textboxes.
				// Takes almost no time if the maintenanceLevel has been set for this book.
				CurrentBook.Storage.PerformNecessaryMaintenanceOnBook();
			}

			_currentlyDisplayedBook = CurrentBook;

			var errors = _currentlyDisplayedBook.CheckForErrors();
			if (!String.IsNullOrEmpty(errors))
			{
				ErrorReport.NotifyUserOfProblem(errors);
				return;
			}

			// BL-2339: try to choose the last edited page
			var page = _currentlyDisplayedBook.GetPageByIndex(_currentlyDisplayedBook.UserPrefs.MostRecentPage) ?? _currentlyDisplayedBook.FirstPage;
			try
			{
				_inProcessOfLoading = true;
				if (page != null)
					_pageSelection.SelectPage(page);
				if (_view != null)
				{
					_view.UpdatePageList(false);
				}

			}
			finally
			{
				_inProcessOfLoading = false;
			}
		}

		// Invoked by an event handler just before we change pages. Unless we are in the process of deleting the
		// current page, we need to save changes to it. Currently this is a side effect of calling the JS
		// pageSelectionChanging(), which calls back to our 'FinishSavingPage()'
		// Note that this is fully synchronous event handling, all in the current thread:
		// PageSelection.SelectPage [CS] raises PageSelectionChanging
		//		OnPageSelectionChanging() [CS, here] responds to this event
		//			it calls pageSelectionChanging() [in JS]
		//				pageSelectionChanging raises HTML event finishSavingPage
		//					this class is listening for finishSavingPage, and handles it by calling FinishSavingPage() [CS]
		//		all those calls return
		//		SelectPage continues with actually changing the current page, and then calls PageChanged.
		// I am confident that RunJavaScript does not return until it has finished executing the JavaScript function,
		// because the wrapper code in Browser.cs is capable of returning a result from the JS function.
		// I am confident that fireCSharpEditEvent (in bloomEditing.js) does not return until all the event handlers
		// (in this case, our C# FinishSavingPage() method) have completed, because it is implemented using
		// document.dispatchEvent(), and this returns a result determined by the handlers, specifically whether
		// one of them canceled the event.
		// Thus, the whole sequence of steps above behaves like a series of nested function calls,
		// and SelectPage does not proceed with actually changing the current page until after FinishSavingPage has
		// completed saving it.
		private void OnPageSelectionChanging(object sender, EventArgs eventArgs)
		{
			CheckForBL2634("start of page selection changing--should have old IDs");
			if (_view != null && !_inProcessOfDeleting && !_inProcessOfLoading)
			{
				_view.ChangingPages = true;
				_view.RunJavaScript("if (typeof(FrameExports) !=='undefined') {FrameExports.getPageFrameExports().pageSelectionChanging();}");
				FinishSavingPage();
				_view.RunJavaScript("if (typeof(FrameExports) !=='undefined') {FrameExports.getPageFrameExports().disconnectForGarbageCollection();}");
			}
		}

		void OnPageSelectionChanged(object sender, EventArgs e)
		{
			Logger.WriteMinorEvent("changing page selection");
			Analytics.Track("Select Page");//not "edit page" because at the moment we don't have the capability of detecting that.

			// Trace memory usage in case it may be useful
			// First see if we seem to have a problem without taking time (~100ms in a large book/fast computer) to force GC.
			// If we seem to have a problem do it again forcing the GC and possibly warning the user.
			if (MemoryManagement.CheckMemory(false, "switched page in edit", false, false))
				MemoryManagement.CheckMemory(false, "switched page in edit", true);

			if (_view != null)
			{
				if (_previouslySelectedPage != null && _domForCurrentPage != null)
				{
					_view.UpdateThumbnailAsync(_previouslySelectedPage);
				}
				_previouslySelectedPage = _pageSelection.CurrentSelection;

				// BL-2339: remember last edited page
				if (_previouslySelectedPage != null)
				{
					var idx = _previouslySelectedPage.GetIndex();
					if (idx > -1)
						_previouslySelectedPage.Book.UserPrefs.MostRecentPage = idx;
				}

				_pageSelection.CurrentSelection.Book.BringPageUpToDate(_pageSelection.CurrentSelection.GetDivNodeForThisPage());
				_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
				_duplicatePageCommand.Enabled = !_pageSelection.CurrentSelection.Required;
				_deletePageCommand.Enabled = !_pageSelection.CurrentSelection.Required;

				CheckForBL8852();

				PageSelectModelChangesComplete?.Invoke(this, EventArgs.Empty);
			}
		}

		private void CheckForBL8852()
		{
			var page = _pageSelection.CurrentSelection;
			var contentPages = page.Book.OurHtmlDom.GetContentPageElements();
			if (contentPages == null)
			{
				return;
			}

			var idSet = new HashSet<string>();
			foreach (var contentPage in contentPages)
			{
				var nodeList = HtmlDom.SelectChildNarrationAudioElements(contentPage, true);
				if (nodeList == null) {
					return;
				}

				for (int i = 0; i < nodeList.Count; ++i)
				{
					var node = nodeList.Item(i);

					// GetOptionalStringAttribute needs this to be non-null, or else an exception will happen
					if (node.Attributes == null)
					{
						continue;
					}

					if (HtmlDom.IsNodePartOfDataBookOrDataCollection(node))
					{
						continue;
					}
										
					var id = node.GetOptionalStringAttribute("id", null);
					if (id != null)
					{
						var isNewlyAdded = idSet.Add(id);
						if (!isNewlyAdded)
						{
							// Uh-oh. That means an element like this already exists?
							var shortMsg = "Corrupt Book - Duplicate audio ID. Please report this issue (and to receive help fixing the audio IDs in this book).\nAudio files in this book may become lost or overwritten.";
							var longMsg = $"Duplicate GUID {id} on recordable with text \"{node.InnerText}\". See BL-8852.";
							NonFatalProblem.Report(ModalIf.All, PassiveIf.None, shortMsg, longMsg);

							// Only it report it once per book (per time),
							// No need to report multiple modals at the same time
							return;
						}
					
					}
				}
			}
		}

		public void RefreshDisplayOfCurrentPage()
		{
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
		}

		private DataSet _pageDataBeforeEdits;
		private string _featureRequirementsBeforeEdits;

		private DataSet GetPageData(XmlNode page)
		{
			var data = new DataSet();
			CurrentBook.BookData.GatherDataItemsFromXElement(data, page, new HashSet<Tuple<string, string>>());
			return data;
		}

		public void SetupServerWithCurrentPageIframeContents()
		{
			_domForCurrentPage = CurrentBook.GetEditableHtmlDomForPage(_pageSelection.CurrentSelection);
			_pageDataBeforeEdits = GetPageData(_domForCurrentPage.RawDom);
			_featureRequirementsBeforeEdits = CurrentBook.OurHtmlDom.GetMetaValue("FeatureRequirement", "");
			CheckForBL2634("setup");
			SetupPageZoom();
			CurrentBook.InsertFullBleedMarkup(_domForCurrentPage.Body);
			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(_domForCurrentPage.RawDom);
			CheckForBL2634("made tags safe");
			if (_currentPage != null)
				_currentPage.Dispose();
			InsertLabelAndLayoutTogglePane(_domForCurrentPage);
			_currentPage = BloomServer.MakeSimulatedPageFileInBookFolder(_domForCurrentPage, true);
			CheckForBL2634("made simulated page");
			CommonApi.AuthorMode = CanAddPages;
		}

		private static void InsertLabelAndLayoutTogglePane(HtmlDom dom)
		{
			// Add an empty div that will provide space for the page label and origami toggle above the displayed page.
			var node = dom.RawDom.CreateNode(XmlNodeType.Element, "div", "");
			var attr = dom.RawDom.CreateAttribute("id");
			attr.Value = "labelAndLayoutPane";
			node.Attributes.Append(attr);
			dom.Body.InsertBefore(node, dom.Body.FirstChild);
		}

		public bool AreToolboxAndOuterFrameCurrent()
		{
			return _currentlyDisplayedBook == _bookForToolboxContent;
		}

		public void ClearBookForToolboxContent()
		{
			_bookForToolboxContent = null;
		}

		public void SetupServerWithCurrentBookToolboxContents()
		{
			_server.ToolboxContent = ToolboxView.MakeToolboxContent(_currentlyDisplayedBook);
			_bookForToolboxContent = _currentlyDisplayedBook;
		}
		/// <summary>
		/// Insert a div into the body that contains the .bloom-page div and set a style on this new div that will
		/// zoom/scale the page content to the extent the user currently prefers.  This style cannot go on the body
		/// element because that make popup dialogs (and their combo box dropdowns) display in the wrong location.
		/// The style cannot go on the .bloom-page div itself because that makes hint bubbles squeeze to fit inside
		/// the zoomed page display limits.
		/// </summary>
		/// <remarks>
		/// See http://issues.bloomlibrary.org/youtrack/issue/BL-4172.
		/// </remarks>
		private void SetupPageZoom()
		{
			var pageZoom = (float)_view.Zoom / 100F;
			var body = _domForCurrentPage.Body;
			var pageDiv = body.SelectSingleNode("//div[contains(concat(' ', @class, ' '), ' bloom-page ')]") as XmlElement;
			if (pageDiv != null)
			{
				var outerDiv = InsertContainingScalingDiv(body, pageDiv);
				// The HTML expects floating point values in the invariant culture, not the current culture.
				// See https://issues.bloomlibrary.org/youtrack/issue/BL-5579.
				var zoomString = pageZoom.ToString(CultureInfo.InvariantCulture);
				outerDiv.SetAttribute("style", String.Format("transform: scale({0}); transform-origin: left top;", zoomString));
			}
			CheckForBL2634("set page zoom");
		}

		XmlElement InsertContainingScalingDiv(XmlElement body, XmlElement pageDiv)
		{
			// Note: because this extra div is OUTSIDE the page div, we don't have to remove it later,
			// because only the page div and its contents are saved back to the permanent file.
			var newDiv = body.OwnerDocument.CreateElement("div");
			newDiv.SetAttribute("id", PageScalingDivId);
			body.PrependChild(newDiv);
			newDiv.AppendChild(pageDiv);
			return newDiv;
		}

		/// <summary>
		/// Return the DOM that represents the content of the current page.
		/// Note that this is typically not the top-level thing displayed by the browser; rather, it is embedded in an
		/// iframe.
		/// </summary>
		/// <returns></returns>
		public HtmlDom GetXmlDocumentForCurrentPage()
		{
			return _domForCurrentPage;
		}

		public string GetUrlForCurrentPage()
		{
			return _currentPage.Key;
		}

		/// <summary>
		/// Return the top-level document that should be displayed in the browser for the current page.
		/// </summary>
		/// <returns></returns>
		public HtmlDom GetXmlDocumentForEditScreenWebPage()
		{
			var path = FileLocationUtilities.GetFileDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot, "bookEdit", "EditViewFrame.html"));
			// {simulatedPageFileInBookFolder} is placed in the template file where we want the source file for the 'page' iframe.
			// We don't really make a file for the page, the contents are just saved in our local server.
			// But we give it a url that makes it seem to be in the book folder so local urls work.
			// See BloomServer.MakeSimulatedPageFileInBookFolder() for more details.
			var frameText = RobustFile.ReadAllText(path, Encoding.UTF8).Replace("{simulatedPageFileInBookFolder}", _currentPage.Key);
			var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(frameText));

			if (_currentlyDisplayedBook.BookInfo.ToolboxIsOpen)
			{
				// Make the toolbox initially visible.
				// What we have to do to accomplish this is pretty non-intuitive. It's a consequence of the way
				// the pure-drawer CSS achieves the open/close effect. This input is a check-box, so clicking it
				// changes the state of things in a way that all the other CSS can depend on.
				var toolboxCheckBox = dom.SelectSingleNode("//input[@id='pure-toggle-right']");
				toolboxCheckBox?.SetAttribute("checked", "true");
			}

			return dom;
		}

		/// <summary>
		/// View calls this once the main document has completed loading.
		/// But this is not really reliable.
		/// Also see comments in EditingView.UpdateSingleDisplayedPage.
		/// TODO really need a more reliable way of determining when the document really is complete
		/// </summary>
		internal void DocumentCompleted()
		{
			Application.Idle += OnIdleAfterDocumentSupposedlyCompleted;
		}

		/// <summary>
		/// For some reason, we need to call this code OnIdle.
		/// We couldn't figure out the timing any other way.
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		void OnIdleAfterDocumentSupposedlyCompleted(object sender, EventArgs e)
		{
			Application.Idle -= OnIdleAfterDocumentSupposedlyCompleted;

			//Work-around for BL-422: https://jira.sil.org/browse/BL-422
			if (_currentlyDisplayedBook == null)
			{
				Debug.Fail(
					"Debug Only: BL-422 reproduction (currentlyDisplayedBook was null in OnIdleAfterDocumentSupposedlyCompleted).");
				Logger.WriteEvent("BL-422 happened just now (currentlyDisplayedBook was null in OnIdleAfterDocumentSupposedlyCompleted).");
				return;
			}
			AddStandardEventListeners();
		}

		/// <summary>
		/// listen for these events raised by javascript.
		/// </summary>
		internal void AddStandardEventListeners()
		{
			AddMessageEventListener("saveToolboxSettingsEvent", SaveToolboxSettings);
			AddMessageEventListener("setTopic", SetTopic);
			AddMessageEventListener("finishSavingPage", FinishSavingPage);
		}

		private void SaveToolboxSettings(string data)
		{
			ToolboxView.SaveToolboxSettings(_currentlyDisplayedBook,data);
		}

		private void AddMessageEventListener(string name, Action<string> listener)
		{
			_activeStandardListeners.Add(name);
			_view.AddMessageEventListener(name, listener);
		}

		/// <summary>
		/// stop listening for these events raised by javascript.
		/// </summary>
		internal void RemoveStandardEventListeners()
		{
			foreach (var name in _activeStandardListeners)
			{
				_view.RemoveMessageEventListener(name);
			}
			_activeStandardListeners.Clear();
		}


		/// <summary>
		/// When the user types ctrl+n, we do this:
		/// 1) If the user is on a page that is xmatter, or a singleton, then we just add the first page in the template
		/// 2) Else, make a new page of the same type as the current one
		/// </summary>
		/// <param name="unused"></param>
		///
		/// This is, for now, a TODO
		///
//		public void HandleAddNewPageKeystroke(string unused)
//		{
//			if (!HaveCurrentEditableBook || _currentlyDisplayedBook.LockedDown)
//				return;
//
//			try
//			{
//				if (CanDuplicatePage)
//				{
//					if (AddNewPageBasedOnTemplate(this._pageSelection.CurrentSelection.IdOfFirstAncestor))
//						return;
//				}
//				var idOfFirstPageInTemplateBook = CurrentBook.FindTemplateBook().GetPageByIndex(0).Id;
//				if (AddNewPageBasedOnTemplate(idOfFirstPageInTemplateBook))
//					return;
//			}
//			catch (Exception error)
//			{
//				Logger.WriteEvent(error.Message);
//				//this is not worth bothering the user about
//#if DEBUG
//				throw error;
//#endif
//			}
//			//there was some error figuring out a default page, let's just let the user choose what they want
//			if(this._view!=null)
//				this._view.ShowAddPageDialog();
//		}

		//invoked from TopicChooser.ts
		private void SetTopic(string englishTopicAsKey)
		{
			//make the change in the data div
			_currentlyDisplayedBook.SetTopic(englishTopicAsKey);
			_pageHasUnsavedDataDerivedChange = true;
			//reflect that change on this page
			RethinkPageAndReloadIt();
		}

		internal void RethinkPageAndReloadIt(ApiRequest request)
		{
			RethinkPageAndReloadIt();
			request.PostSucceeded();
		}

		internal void RethinkPageAndReloadIt()
		{
			if (CannotSavePage())
				return;
			FinishSavingPage();
			RefreshDisplayOfCurrentPage();
		}

		/// <summary>
		/// Called from a JavaScript event after it has done everything appropriate in JS land towards saving a page,
		/// in the process of wrapping up this page before moving to another.
		/// The main point is that any changes on this page get saved back to the main document.
		/// In case it is an origami page, there is some special stuff to do as commented below.
		/// (Argument is required for JS callback, not used).
		/// </summary>
		/// <returns>true if it was aborted (nothing to save or refresh)</returns>
		private void FinishSavingPage(string ignored = null)
		{
			if (CannotSavePage())
				return;

			var stopwatch = Stopwatch.StartNew();
			SaveNow();
			stopwatch.Stop();
			Debug.WriteLine("Save Now Elapsed Time: {0} ms", stopwatch.ElapsedMilliseconds);
		}

		private bool CannotSavePage()
		{
			var returnVal = _bookSelection == null || CurrentBook == null || _pageSelection.CurrentSelection == null ||
				_currentlyDisplayedBook == null;

			if (returnVal)
				_view.ChangingPages = false;

			return returnVal;
		}

		// We set this true for the interval between starting to navigate to a new
		// page and when it is loaded. This prevents trying to save when things are in an unstable state
		// (e.g., BL-2634, BL-6296). It may also prevent some wasted Saves and thus improve performance.
		public bool NavigatingSoSuspendSaving = false;

		public void SaveNow()
		{
			if (_domForCurrentPage != null && !_inProcessOfSaving && !NavigatingSoSuspendSaving)
			{
#if MEMORYCHECK
				// Check memory for the benefit of developers.
				SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "before EditingModel.SaveNow()", false);
#endif
				var watch = Stopwatch.StartNew();
				try
				{
					_webSocketServer.SendString("pageThumbnailList", "saving", "");

					// CleanHtml already requires that we are on UI thread. But it's worth asserting here too in case that changes.
					// If we weren't sure of that we would need locking for access to _tasksToDoAfterSaving and _inProcessOfSaving,
					// and would need to be careful about whether any delayed tasks needed to be on the UI thread.
					if (_view.InvokeRequired)
					{
						NonFatalProblem.Report(ModalIf.Beta, PassiveIf.Beta, "SaveNow called on wrong thread", null);
						_view.Invoke((Action)(SaveNow));
						watch.Stop();
						return;
					}
					CheckForBL2634("beginning SaveNow");
					_inProcessOfSaving = true;
					_tasksToDoAfterSaving.Clear();
					_view.CleanHtmlAndCopyToPageDom();
					SavePageFrameState();

					//BL-1064 (and several other reports) were about not being able to save a page. The problem appears to be that
					//this old code:
					//	CurrentBook.SavePage(_domForCurrentPage);
					//would some times ask book X to save a page from book Y.
					//We could never reproduce it at will, so this is to help with that...
					if(this._pageSelection.CurrentSelection.Book != _currentlyDisplayedBook)
					{
						Debug.Fail("This is the BL-1064 Situation");
						Logger.WriteEvent("Warning: SaveNow() with a page that is not the current book. That should be ok, but it is the BL-1064 situation (though we now work around it).");
					}
					//but meanwhile, the page knows its book, so we can see if it looks like a valid book and give a helpful
					//error if, for example, it was deleted:
					try
					{
						if (!_pageSelection.CurrentSelection.Book.CanUpdate)
						{
							Logger.WriteEvent("Error: SaveNow() found that this book had CanUpdate=='false'");
							Logger.WriteEvent("Book path was {0}",_pageSelection.CurrentSelection.Book.FolderPath);
							throw new ApplicationException("Bloom tried to save a page to a book that was not in a position to be updated.");
						}
					}
					catch (ObjectDisposedException err) // in case even calling CanUpdate gave an error
					{
						Logger.WriteEvent("Error: SaveNow() found that this book was disposed.");
						throw err;
					}
					catch(Exception err) // in case even calling CanUpdate gave an error
					{
						Logger.WriteEvent("Error: SaveNow():CanUpdate threw an exception");
						throw err;
					}
					CheckForBL2634("save");
					//OK, looks safe, time to save.
					var newPageData = GetPageData(_domForCurrentPage.RawDom);
					_pageSelection.CurrentSelection.Book.SavePage(_domForCurrentPage, NeedToDoFullSave(newPageData));
					_pageHasUnsavedDataDerivedChange = false;
					CheckForBL2634("finished save");
				}
				finally
				{
					_inProcessOfSaving = false;
				}
				while (_tasksToDoAfterSaving.Count > 0)
				{
					var task = _tasksToDoAfterSaving[0];
					_tasksToDoAfterSaving.RemoveAt(0);
					task();
				}
				watch.Stop();
				TroubleShooterDialog.Report($"Saving changes took {watch.ElapsedMilliseconds} milliseconds");
#if MEMORYCHECK
				// Check memory for the benefit of developers.
				SIL.Windows.Forms.Reporting.MemoryManagement.CheckMemory(false, "after EditingModel.SaveNow()", false);
#endif
			}
		}

		// If we return 'true', we need to do a complete book save, otherwise we'll just save this page.
		// The 'data-derived' nature of the license metadata means that the DataSet we were comparing was insufficient
		// to detect changes to it (BL-7518).
		// So far 'data-derived' divs are all in xmatter, so we could just always do a full save if we're on an xmatter
		// page. Unfortunately, that would take a lot of time on a large book so we need to know that something has
		// actually changed that needs saving. The hope is that if we ever add new 'data-derived' divs, changing them will
		// result in this flag being set.
		private bool NeedToDoFullSave(DataSet newPageData)
		{
			var newFeatureRequirements = BookStorage.GetRequiredVersionsString(CurrentBook.OurHtmlDom);
			return _pageHasUnsavedDataDerivedChange || !newPageData.SameAs(_pageDataBeforeEdits)
				|| _featureRequirementsBeforeEdits != newFeatureRequirements;
		}

		/// <summary>
		/// Save anything we want to persist from page to page but which is not part of the book from the page's current state.
		/// Currently there is nothing (once used to persist zoom level set with control wheel).
		/// </summary>
		void SavePageFrameState()
		{
		}

		// One more attempt to catch whatever is causing us to get errors indicating that the page we're trying
		// to save is not in the book we're trying to save it into.
		internal void CheckForBL2634(string when)
		{
			try
			{
				if (_pageSelection.CurrentSelection == null || _domForCurrentPage ==null)
					return;
				XmlElement divElement =
					_domForCurrentPage.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, 'bloom-page')]");
				string pageDivId = divElement.GetAttribute("id");
				if (pageDivId != _pageSelection.CurrentSelection.Id)
				{
					// Several reports indicate that this occasionally and unrepeatably happens with various call stacks.
					// This code is aimed at finding out a little more about the circumstances.
					try
					{
						Logger.WriteEvent("BL2634 failure: pageDiv is {0}", _domForCurrentPage.RawDom.OuterXml);
						Logger.WriteEvent("BL2634 failure: selection div is {0}", _pageSelection.CurrentSelection.GetDivNodeForThisPage().OuterXml);
					}
					catch (Exception)
					{
						Logger.WriteEvent("Bl2634: failed to write XML of DOM and selection");
					}
					throw new ApplicationException(
						String.Format(
							"Bl-2634: at {2}, id of _domForCurrentPage ({0}) is not the same as ID of _pageSelection.CurrentSelection ({1})",
							pageDivId, _pageSelection.CurrentSelection.Id, when));
				}
				// By comparing this with the stacks dumped when the check fails, we can hopefully tell whether the DOM or
				// the Current Selection ID somehow changed, which may help partition the space we need to look in to
				// solve the problem.
				Logger.WriteMinorEvent(String.Format("CheckForBl2634({0}: both ids are " + _pageSelection.CurrentSelection.Id, when));
			}
			catch (Exception err)
			{
				if (err.StackTrace.Contains("DeletePage"))
					Logger.WriteEvent("Trying to save a page while executing DeletePage");
				Logger.WriteEvent("Error: SaveNow(): a mixup occurred in page IDs");
				throw new ApplicationException("Check Inner Exception", err);//have to embed instead of just rethrow in order to preserve line number
			}
		}

		internal void RequestTranslationGroups(ApiRequest request)
		{
			string translationGroupHtml = TranslationGroupManager.GetDefaultTranslationGroupContent(_domForCurrentPage.RawDom, CurrentBook);
			request.ReplyWithHtml(translationGroupHtml);
		}
		
		public void ChangePicture(GeckoHtmlElement img, PalasoImage imageInfo, IProgress progress)
		{
			try
			{
				Logger.WriteMinorEvent("Starting ChangePicture {0}...", imageInfo.FileName);
				var editor = new PageEditingModel();
				editor.ChangePicture(CurrentBook.FolderPath, new ElementProxy(img), imageInfo, progress);

				// We need to save so that when asked by the thumbnailer, the book will give the proper image
				SaveNow();

				// BL-3717: if we cleanup unused image files whenever we change a picture then Cut can lose
				// all of an image's metadata (because the actual file is missing from the book folder when we go to
				// paste in the image that was copied onto the clipboard, which doesn't have metadata.)
				// Let's only do this on ExpensiveIntialization() when loading a book.
				//CurrentBook.Storage.CleanupUnusedImageFiles();

				// But after saving, we need the non-cleaned version back there
				_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

				_view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
				Logger.WriteMinorEvent("Finished ChangePicture {0} (except for async thumbnail) ...", imageInfo.FileName);
				Analytics.Track("Change Picture");
				Logger.WriteEvent("ChangePicture {0}...", imageInfo.FileName);

			}
			catch (Exception e)
			{
				var msg = LocalizationManager.GetString("Errors.ProblemImportingPicture","Bloom had a problem importing this picture.");
				ErrorReport.NotifyUserOfProblem(e, msg+Environment.NewLine+e.Message);
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

		/// <summary>
		/// Get the Browser object used for editing.
		/// </summary>
		/// <remarks>
		/// This is needed only on Linux to allow hooking up an OnBrowserClick used to work around a Mono bug.
		/// See https://issues.bloomlibrary.org/youtrack/issue/BL-6753.
		/// </remarks>
		internal Browser GetEditingBrowser()
		{
			return _view.Browser;
		}

		public IPage DeterminePageWhichWouldPrecedeNextInsertion()
		{
			if (_view != null)
			{
				var pagesStartingWithCurrentSelection =
					CurrentBook.GetPages().SkipWhile(p => p.Id != _pageSelection.CurrentSelection.Id);
				var candidates = pagesStartingWithCurrentSelection.ToArray();
				for (int i = 0; i < candidates.Length - 1; i++)
				{
					if (!candidates[i + 1].Required)
					{
						return candidates[i];
					}
				}
				var pages = CurrentBook.GetPages();
				// ReSharper disable PossibleMultipleEnumeration
				if (!pages.Any())
				{
					var exception = new ApplicationException(
						String.Format(
							@"CurrentBook.GetPages() gave no pages (BL-262 repro).
									  Book is '{0}'\r\nErrors known to book=[{1}]\r\n{2}\r\n{3}",
							CurrentBook.TitleBestForUserDisplay,
							CurrentBook.CheckForErrors(),
							CurrentBook.RawDom.OuterXml,
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
			// REVIEW: does this ToLower() do the right thing on Linux, where filenames are case sensitive?
			var name = _bookSelection.CurrentSelection.BookData.Language1.FontName.ToLowerInvariant();

			if (null == FontFamily.Families.FirstOrDefault(f => f.Name.ToLowerInvariant() == name))
			{
				var s = LocalizationManager.GetString("EditTab.FontMissing",
														   "The current selected " +
														   "font is '{0}', but it is not installed on this computer. Some other font will be used.");
				return String.Format(s, _bookSelection.CurrentSelection.BookData.Language1.FontName);
			}
			return null;
		}

		/*  Later I found a different explanation for why i wasn't getting the data back... the new classes were at the pag div
		 *  level, and the c# code was only looking at the innerhtml of that div when saving (still is).
		 *  /// <summary>
		  /// Although browsers are happy to let you manipulate the DOM, in most cases gecko/xulrunner does not expect that we,
		  /// the host process, are going to need access to those changes. For example, if we have a control that adds a class
		  /// to some element based on a user choice, the user will see the choice take effect, but then when they come back to the
		  /// page later, their choice will be lost. This is because that new class just isn't in the html that gets returned to us,
		  /// if we do, for example, _browser.Document.GetElementsByTagName("body").outerHtml. (Other things changes *are* returned, like
		  /// the new contents of an editable div).
		  ///
		  /// Anyhow this method, triggered by javascript that knows it did something that will be lost, is here in order to work
		  /// around this. The Javascript does something like:
		  /// var origin = window.location.protocol + '//' + window.location.host;
		  /// event.initMessageEvent ('PreserveClassAttributeOfElement', true, true, theHTML, origin, 1234, window, null);
		  /// document.dispatchEvent (event);
		  ///
		  /// The hard part here is knowing which element gets this html
		  /// </summary>
		  /// <param name="?"></param>
		  public void PreserveHtmlOfElement(string elementHtml)
		  {
			  try
			  {
				  var editor = new PageEditingModel();

				  //todo if anyone ever needs it: preserve more than just the class
				  editor.PreserveClassAttributeOfElement(_pageSelection.CurrentSelection.GetDivNodeForThisPage(), elementHtml);

				  //we have to save so that when asked by the thumbnailer, the book will give the proper image
	//              SaveNow();
				  //but then, we need the non-cleaned version back there
  //                _view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);

	//              _view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);

			  }
			  catch (Exception e)
			  {
				  ErrorReport.NotifyUserOfProblem(e, "Could not PreserveClassAttributeOfElement");
			  }
		  }
		 */

		public void ShowAddPageDialog()
		{
			SaveNow(); // At least in template mode, the current page shows in the Add Page dialog, and should be current.
			_view.ShowAddPageDialog();
		}

		internal void ChangePageLayout(IPage page)
		{
			PageChangingLayout = page;
			SaveNow();// need to preserve any typing they've done but not yet saved
			_view.ShowChangeLayoutDialog();
		}

		public void ChangeBookLicenseMetaData(Metadata metadata)
		{
			CurrentBook.SetMetadata(metadata);
			_pageHasUnsavedDataDerivedChange = true;
			RefreshDisplayOfCurrentPage(); //the cleanup() that is part of Save removes qtips, so let's redraw everything
		}

#if __MonoCS__
		/// <summary>
		/// Flag that a page selection is currently under way.
		/// </summary>
		internal void PageSelectionStarted()
		{
			_pageSelection.StartChangingPage();
		}

		/// <summary>
		/// Flag that the current (former) page selection has finished, so it's safe to select another page.
		/// </summary>
		internal void PageSelectionFinished()
		{
			_pageSelection.ChangingPageFinished();
		}
#endif

		public bool GetClipboardHasPage()
		{
			return _pageDivFromCopyPage != null;
		}

		public void CopyPage(IPage page)
		{
			SaveNow();	// need to preserve any typing they've done but not yet saved (BL-4512)
			// We have to clone this so that if the user changes the page after doing the copy,
			// when they paste they get the page as it was, not as it is now.
			_pageDivFromCopyPage = (XmlElement) page.GetDivNodeForThisPage().CloneNode(true);
			_bookPathFromCopyPage = page.Book.GetPathHtmlFile();
		}

		/// <summary>
		/// Paste the previously saved _pageDivFromCopyPage as a new page.
		/// </summary>
		/// <param name="pageToPasteAfter">This is NOT the page we are to paste!</param>
		public void PastePage(IPage pageToPasteAfter)
		{
			var templateBook = pageToPasteAfter.Book; // default is to assume it's from the same book
			bool fromAnotherBook = templateBook.GetPathHtmlFile() != _bookPathFromCopyPage;
			if (fromAnotherBook)
			{
				// Copying from some other book. We need an actual book object, just like when we insert a page from a template,
				// at least in order to properly copy any images and styles used on the page that are not in the
				// destination book.
				// If for some reason (since renamed?) we can't get it, just do the best we can...images and styles may
				// not be right, but we can still paste the content of the page.
				templateBook = _sourceCollectionsList.FindAndCreateTemplateBookByFullPath(_bookPathFromCopyPage) ?? templateBook;
			}
			var pageForPasting = new Page(templateBook, _pageDivFromCopyPage, "not used", "not used", x => _pageDivFromCopyPage);
			OnInsertPage(pageForPasting, new PageInsertEventArgs(false)); // false => don't need analytics on use of template pages
		}

		public void AdjustPageZoom(int delta)
		{
			_view.AdjustPageZoom(delta);
		}

		/// <summary>
		/// Make sure the book folder contains a current version of the video placeholder.
		/// We don't copy this to every book like placeHolder.png, since relatively few books need it,
		/// but if it's used it needs to be there so things look right when opened in a browser.
		/// I don't think our image deletion code is smart enough to detect that something a CSS
		/// file says is needed as a background should not be deleted, so I've just made this
		/// one of the image files that is never deleted once it gets added.
		/// </summary>
		public void RequestVideoPlaceHolder()
		{
			_bookSelection.CurrentSelection.Storage.Update("video-placeholder.svg");
		}
		public void RequestWidgetPlaceHolder()
		{
			_bookSelection.CurrentSelection.Storage.Update("widget-placeholder.svg");
		}

		public string MakeActivity(string fullWidgetPath)
		{
			return MakeActivity(CurrentBook.FolderPath, fullWidgetPath);
		}

		public static string MakeActivity(string bookFolderPath, string fullWidgetPath)
		{
			var widgetPath = fullWidgetPath.Replace("\\", "/");
			var widgetName = Path.GetFileNameWithoutExtension(widgetPath);
			var originalWidgetName = widgetName;
			var widgetDestinationFolder = bookFolderPath + "/" + "activities" + "/" + widgetName;
			var suffix = 1;
			// If widget destination folder already exists, come up with modified name
			while (Directory.Exists(widgetDestinationFolder))
			{
				suffix++;
				widgetName = Path.GetFileNameWithoutExtension(widgetPath) + suffix;
				widgetDestinationFolder = bookFolderPath + "/" + "activities" + "/" + widgetName;
			}

			ZipUtils.ExpandZip(fullWidgetPath, widgetDestinationFolder);
			if (suffix > 1)
			{
				// might be duplicate widget
				var originalWidgetFolderPath = bookFolderPath + "/" + "activities" + "/" + originalWidgetName;
				if (DirectoryUtils.SameContent(widgetDestinationFolder,
					originalWidgetFolderPath))
				{
					widgetName = originalWidgetName;
					SIL.IO.RobustIO.DeleteDirectoryAndContents(widgetDestinationFolder);
					widgetDestinationFolder = originalWidgetFolderPath;
				}
			}

			var rootFileName = "index.html";
			// Warning if unzipped folder does not contain index.html
			// Enhance: possibly (following wdgt format at https://support.apple.com/en-us/HT204433),
			// zip might contain Info.plist. Parsed as an XML file, this should have a root plist
			// element containing a dict element containing a sequence of key/string pairs, including
			// one like this:
			// <key>MainHTML</key>
			// <string>HelloWorld.html</string>
			// where the string element following the MainHTML key contains the name of the root
			// HTML file.
			if (!File.Exists(Path.Combine(widgetDestinationFolder, rootFileName)))
			{
				rootFileName = "index.htm";
				if (!File.Exists(Path.Combine(widgetDestinationFolder, rootFileName)))
					// Review: worth localizing?
					MessageBox.Show("Zip file contains no index.htm{l} file. It will not work as a Bloom book widget.",
						"Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}

			return "activities" + "/" + widgetName + "/" + rootFileName;
		}

		/// <summary>
		/// Create a .wdgt file from a folder that contains everything needed for a proper widget.
		/// </summary>
		/// <param name="fullWidgetPath">full path of the widget's "index.html" file, which may not actually be named "index.html"</param>
		/// <remarks>
		/// This has been tested on output from only Active Presenter and Book Widgets.
		/// </remarks>
		public static string CreateWidgetFromHtmlFolder(string fullWidgetPath)
		{
			var filename = Path.GetFileName(fullWidgetPath);
			var fullFolderName = Path.GetDirectoryName(fullWidgetPath);
			// First attempt: get the widget name from the name of the directory
			// containing the .html file.
			var widgetName = Path.GetFileName(fullFolderName);
			var fromActivePresenter = false;
			var activePresenterFiles = new string[] {"practice.html", "tutorial.html", "test.html"};
			if (widgetName == "HTML5")
			{
				// Active Presenter export HTML5 files to folder structure <ProjectName>/HTML5/<files>
				// where the files include possibly practice.html, tutorial.html, and/or test.html.
				// The user presumably picked one of these three files.  Get the widget name from
				// the directory name from the level above the HTML5 subfolder.
				widgetName = Path.GetFileName(Path.GetDirectoryName(fullFolderName));
				fromActivePresenter = activePresenterFiles.Contains(filename);
			}
			if (String.IsNullOrWhiteSpace(widgetName))
			{
				// The .html file must be at the top level of the filesystem??
				widgetName = "MYWIDGET";	// I doubt this ever happens, but it saves a compiler warning.
			}
			else if (widgetName.EndsWith(".wdgt"))
			{
				// Book Widgets creates a folder named <ProjectName>.wdgt in which to store the
				// widget files.  Why a folder and not a zip file, only the programmers/analysts
				// know...  So strip the extension off the folder name to get the widget name.
				widgetName = Path.GetFileNameWithoutExtension(widgetName);
			}
			var widgetPath = Path.Combine(Path.GetTempPath(), "Bloom", widgetName + ".wdgt");
			using (TemporaryFolder temp = new TemporaryFolder("CreatingWidgetForBloom"))
			{
				// Copy the relevant files and folders to a temporary location.
				DirectoryUtils.CopyFolder(fullFolderName, temp.FolderPath);
				// Remove excess html files (if any), and ensure desired html file is named "index.html".
				foreach (var filePath in Directory.GetFiles(temp.FolderPath))
				{
					var name = Path.GetFileName(filePath);
					if (name == filename)
					{
						if (filename != "index.html")
						{
							var destPath = Path.Combine(temp.FolderPath, "index.html");
							if (RobustFile.Exists(destPath))
								RobustFile.Delete(destPath);
							RobustFile.Move(filePath, destPath);
						}
					}
					else if (fromActivePresenter && (activePresenterFiles.Contains(name)))
					{
						RobustFile.Delete(filePath);
					}
				}
				// zip up the temporary folder contents into a widget file
				var zip = new BloomZipFile(widgetPath);
				foreach (var file in Directory.GetFiles(temp.FolderPath))
				{
					zip.AddTopLevelFile(file);
				}
				foreach (var dir in Directory.GetDirectories(temp.FolderPath))
					zip.AddDirectory(dir);
				zip.Save();
			}
			return widgetPath;
		}
	}
}
