using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
//using Bloom.SendReceive;
using Bloom.ToPalaso.Experimental;
using Bloom.Api;
using DesktopAnalytics;
using Gecko;
using L10NSharp;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Reporting;

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
		//private readonly SendReceiver _sendReceiver;
		private HtmlDom _domForCurrentPage;
		// We dispose of this when we create a new one. It may hang around a little longer than needed, but memory
		// is the only resource being used, and there is only one instance of this object.
		private SimulatedPageFile _currentPage;
		public bool Visible;
		private Book.Book _currentlyDisplayedBook;
		private EditingView _view;
		private List<ContentLanguage> _contentLanguages;
		private IPage _previouslySelectedPage;
		private bool _inProcessOfDeleting;
		private bool _inProcessOfLoading;
		private string _toolboxFolder;
		private EnhancedImageServer _server;
		private readonly BloomWebSocketServer _webSocketServer;
		private Dictionary<string, IPage> _templatePagesDict;
		internal IPage PageChangingLayout; // used to save the page on which the choose different layout command was invoked while the dialog is active.

		// These variables are not thread-safe. Access only on UI thread.
		private bool _inProcessOfSaving;
		private List<Action> _tasksToDoAfterSaving = new List<Action>();

		readonly List<string> _activeStandardListeners = new List<string>();

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
			EnhancedImageServer server,
			BloomWebSocketServer webSocketServer)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_duplicatePageCommand = duplicatePageCommand;
			_deletePageCommand = deletePageCommand;
			_collectionSettings = collectionSettings;
			//_sendReceiver = sendReceiver;
			_server = server;
			_webSocketServer = webSocketServer;
			_templatePagesDict = null;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			pageSelection.SelectionChanging += OnPageSelectionChanging;
			templateInsertionCommand.InsertPage += new EventHandler(OnInsertTemplatePage);

			bookRefreshEvent.Subscribe((book) => OnBookSelectionChanged(null, null));
			pageRefreshEvent.Subscribe((PageRefreshEvent.SaveBehavior behavior) =>
			{
				switch (behavior)
				{
					case PageRefreshEvent.SaveBehavior.SaveBeforeRefresh:
						RethinkPageAndReloadIt(null);
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
			_server.CurrentCollectionSettings = _collectionSettings;
		}

		private Form _oldActiveForm;

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

		private void OnBookSelectionChanged(object sender, EventArgs e)
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

		private void OnDuplicatePage()
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
				_view.UpdatePageList(false);
				Logger.WriteEvent("Duplicate Page");
				Analytics.Track("Duplicate Page");
			}
			catch (Exception error)
			{
				ErrorReport.NotifyUserOfProblem(error,
					"Could not duplicate that page. Try quiting Bloom, run it again, and then attempt to duplicate the page again. And please click 'details' below and report this to us.");
			}
		}

		private void OnDeletePage()
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
				Analytics.Track("Relocate Page");
				Logger.WriteEvent("Relocate Page");
			}
		}

		private void OnInsertTemplatePage(object sender, EventArgs e)
		{
			CurrentBook.InsertPageAfter(DeterminePageWhichWouldPrecedeNextInsertion(), sender as Page);
			//_view.UpdatePageList(false);  InsertPageAfter calls this via pageListChangedEvent.  See BL-3632 for trouble this causes.
			//_pageSelection.SelectPage(newPage);
			try
			{
				Analytics.Track("Insert Template Page", new Dictionary<string, string>
					{
						{ "template-source", (sender as Page).Book.Title},
						{ "page", (sender as Page).Caption}
					});
			}
			catch (Exception)
			{
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
															  _collectionSettings.GetLanguage1Name("en"))
											{Locked = true, Selected = true, IsRtl = _collectionSettings.IsLanguage1Rtl});

					//NB: these won't *always* be tied to the national and regional languages, but they are for now. We would need more UI, without making for extra complexity
					var item2 = new ContentLanguage(_collectionSettings.Language2Iso639Code,
													_collectionSettings.GetLanguage2Name("en"))
									{
										IsRtl = _collectionSettings.IsLanguage1Rtl
//					            		Selected =
//					            			CurrentBook.MultilingualContentLanguage2 ==
//					            			_librarySettings.Language2Iso639Code
									};
					_contentLanguages.Add(item2);
					if (!String.IsNullOrEmpty(_collectionSettings.Language3Iso639Code))
					{
						//NB: this could be the 2nd language (when the national 1 language is not selected)
//						bool selected = CurrentBook.MultilingualContentLanguage2 ==
//						                _librarySettings.Language3Iso639Code ||
//						                CurrentBook.MultilingualContentLanguage3 ==
//						                _librarySettings.Language3Iso639Code;
						var item3 = new ContentLanguage(_collectionSettings.Language3Iso639Code,
														_collectionSettings.GetLanguage3Name("en"))
						{
							IsRtl = _collectionSettings.IsLanguage3Rtl
						};// {Selected = selected};
						_contentLanguages.Add(item3);
					}
				}
				//update the selections
				var lang2 = _contentLanguages.FirstOrDefault(l => l.Iso639Code == _collectionSettings.Language2Iso639Code);
				if (lang2 != null)
					lang2.Selected = CurrentBook.MultilingualContentLanguage2 == _collectionSettings.Language2Iso639Code;
				else
					Logger.WriteEvent("Found no Lang2 in ContentLanguages; count= " + _contentLanguages.Count);

				//the first language is always selected. This covers the common situation in shellbook collections where
				//we have English as both the 1st and national language. https://jira.sil.org/browse/BL-756
				var lang1 = _contentLanguages.FirstOrDefault(l => l.Iso639Code == _collectionSettings.Language1Iso639Code);
				if (lang1 != null)
					lang1.Selected = true;
				else
					Logger.WriteEvent("Hit BL-2780 condition in ContentLanguages; count= " + _contentLanguages.Count);

				var contentLanguageMatchingNatLan2 =
					_contentLanguages.Where(l => l.Iso639Code == _collectionSettings.Language3Iso639Code).FirstOrDefault();

				if(contentLanguageMatchingNatLan2!=null)
				{
					contentLanguageMatchingNatLan2.Selected =
					CurrentBook.MultilingualContentLanguage2 ==_collectionSettings.Language3Iso639Code
					|| CurrentBook.MultilingualContentLanguage3 == _collectionSettings.Language3Iso639Code;
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
			Analytics.Track("Change Content Languages");
		}

		public int NumberOfDisplayedLanguages
		{
			get { return ContentLanguages.Where(l => l.Selected).Count(); }
		}

		public bool CanEditCopyrightAndLicense
		{
			get { return CurrentBook.CanChangeLicense; }

		}

		public IEnumerable<string> LicenseDescriptionLanguagePriorities
		{
			get { return _collectionSettings.LicenseDescriptionLanguagePriorities; }
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
			public bool IsRtl;
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

			var errors = _currentlyDisplayedBook.GetErrorsIfNotCheckedBefore();
			if (!string.IsNullOrEmpty(errors))
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
			CheckForBL2364("start of page selection changing--should have old IDs");
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
			}

			GC.Collect();//i put this in while looking for memory leaks, feel free to remove it.
		}

		public void RefreshDisplayOfCurrentPage()
		{
			_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
		}

		public void SetupServerWithCurrentPageIframeContents()
		{
			_domForCurrentPage = CurrentBook.GetEditableHtmlDomForPage(_pageSelection.CurrentSelection);
			CheckForBL2364("setup");
			SetPageZoom();
			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(_domForCurrentPage.RawDom);
			CheckForBL2364("made tags safe");
			if (_currentPage != null)
				_currentPage.Dispose();
			_currentPage = EnhancedImageServer.MakeSimulatedPageFileInBookFolder(_domForCurrentPage, true);
			CheckForBL2364("made simulated page");

			// Enhance JohnT: Can we somehow have a much simpler toolbox content until the user displays it?
			//if (_currentlyDisplayedBook.BookInfo.ToolboxIsOpen)
				_server.ToolboxContent = ToolboxView.MakeToolboxContent(_currentlyDisplayedBook);
			//else
			//	_server.ToolboxContent = "<html><head><meta charset=\"UTF-8\"/></head><body></body></html>";

			_server.AuthorMode = CanAddPages;
		}

		/// <summary>
		/// Set a style on the body of the main content page that will zoom it to the extent the user currently prefers.
		/// </summary>
		private void SetPageZoom()
		{
			var body = _domForCurrentPage.Body;
			var pageZoom = Settings.Default.PageZoom ?? "1.0";
			body.SetAttribute("style", string.Format("transform: scale({0},{0})", pageZoom));
			CheckForBL2364("read page zoom");
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

		/// <summary>
		/// Return the top-level document that should be displayed in the browser for the current page.
		/// </summary>
		/// <returns></returns>
		public HtmlDom GetXmlDocumentForEditScreenWebPage()
		{
			var path = FileLocator.GetFileDistributedWithApplication(Path.Combine(BloomFileLocator.BrowserRoot, "bookEdit", "EditViewFrame.html"));
			// {simulatedPageFileInBookFolder} is placed in the template file where we want the source file for the 'page' iframe.
			// We don't really make a file for the page, the contents are just saved in our local server.
			// But we give it a url that makes it seem to be in the book folder so local urls work.
			// See EnhancedImageServer.MakeSimulatedPageFileInBookFolder() for more details.
			var frameText = RobustFile.ReadAllText(path, Encoding.UTF8).Replace("{simulatedPageFileInBookFolder}", _currentPage.Key);
			var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(frameText));


			if (_currentlyDisplayedBook.BookInfo.ToolboxIsOpen)
			{
				// Make the toolbox initially visible.
				// What we have to do to accomplish this is pretty non-intutive. It's a consequence of the way
				// the pure-drawer CSS achieves the open/close effect. This input is a check-box, so clicking it
				// changes the state of things in a way that all the other CSS can depend on.
				var toolboxCheckBox = dom.SelectSingleNode("//input[@id='pure-toggle-right']");
				if (toolboxCheckBox != null)
					toolboxCheckBox.SetAttribute("checked", "true");
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
			AddMessageEventListener("preparePageForEditingAfterOrigamiChangesEvent", RethinkPageAndReloadIt);
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
			//reflect that change on this page
			RethinkPageAndReloadIt(null);
		}

		private void RethinkPageAndReloadIt(string obj)
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


		public void SaveNow()
		{
			if (_domForCurrentPage != null)
			{
				try
				{
					_webSocketServer.Send("saving", "");

					// CleanHtml already requires that we are on UI thread. But it's worth asserting here too in case that changes.
					// If we weren't sure of that we would need locking for access to _tasksToDoAfterSaving and _inProcessOfSaving,
					// and would need to be careful about whether any delayed tasks needed to be on the UI thread.
					if (_view.InvokeRequired)
					{
						NonFatalProblem.Report(ModalIf.Beta, PassiveIf.Beta, "SaveNow called on wrong thread", null);
						_view.Invoke((Action)(SaveNow));
						return;
					}
					CheckForBL2364("beginning SaveNow");
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
					CheckForBL2364("save");
					//OK, looks safe, time to save.
					_pageSelection.CurrentSelection.Book.SavePage(_domForCurrentPage);
					CheckForBL2364("finished save");
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
			}
		}

		/// <summary>
		/// Save anything we want to persist from page to page but which is not part of the book from the page's current state.
		/// Currently this is just the zoom level.
		/// </summary>
		void SavePageFrameState()
		{
			var body = _view.GetPageBody();
			Debug.Assert(body!=null, "(Debug Only) no body when doing SavePageFrameState()" );

			// not worth crashing over a timing problem that means we don't save zoom state
			if (body == null)
				return; // BL-3075, not sure how this can happen but it has. Possibly the view is in some state like about:null which has no body.
			var styleAttr = body.Attributes["style"];

			if (styleAttr == null)
				return;
			var style = styleAttr.NodeValue;
			var match = Regex.Match(style, "scale\\(([^,]*),");
			if (!match.Success)
				return;
			var pageZoom = match.Groups[1].Value;
			if (pageZoom != Settings.Default.PageZoom)
			{
				Settings.Default.PageZoom = pageZoom;
				Settings.Default.Save();
			}
		}

		// One more attempt to catch whatever is causing us to get errors indicating that the page we're trying
		// to save is not in the book we're trying to save it into.
		internal void CheckForBL2364(string when)
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
						Logger.WriteEvent("BL2364 failure: pageDiv is {0}", _domForCurrentPage.RawDom.OuterXml);
						Logger.WriteEvent("BL2364 failure: selection div is {0}", _pageSelection.CurrentSelection.GetDivNodeForThisPage().OuterXml);
					}
					catch (Exception)
					{
						Logger.WriteEvent("Bl2364: failed to write XML of DOM and selection");
					}
					throw new ApplicationException(
						string.Format(
							"Bl-2634: at {2}, id of _domForCurrentPage ({0}) is not the same as ID of _pageSelection.CurrentSelection ({1})",
							pageDivId, _pageSelection.CurrentSelection.Id, when));
				}
				// By comparing this with the stacks dumped when the check fails, we can hopefully tell whether the DOM or
				// the Current Selection ID somehow changed, which may help partition the space we need to look in to
				// solve the problem.
				Logger.WriteMinorEvent(String.Format("CheckForBl2364({0}: both ids are " + _pageSelection.CurrentSelection.Id, when));
			}
			catch (Exception err)
			{
				if (err.StackTrace.Contains("DeletePage"))
					Logger.WriteEvent("Trying to save a page while executing DeletePage");
				Logger.WriteEvent("Error: SaveNow(): a mixup occurred in page IDs");
				throw new ApplicationException("Check Inner Exception", err);//have to embed instead of just rethrow in order to preserve line number
			}
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
						string.Format(
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
			var name = _collectionSettings.DefaultLanguage1FontName.ToLowerInvariant();

			if (null == FontFamily.Families.FirstOrDefault(f => f.Name.ToLowerInvariant() == name))
			{
				var s = LocalizationManager.GetString("EditTab.FontMissing",
														   "The current selected " +
														   "font is '{0}', but it is not installed on this computer. Some other font will be used.");
				return string.Format(s, _collectionSettings.DefaultLanguage1FontName);
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
			_view.ShowAddPageDialog();
		}

		internal void ChangePageLayout(IPage page)
		{
			PageChangingLayout = page;
			SaveNow();// need to preserve any typing they've done but not yet saved
			_view.ShowChangeLayoutDialog(page);
		}

		public void ChangeBookLicenseMetaData(Metadata metadata)
		{
			CurrentBook.SetMetadata(metadata);
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
	}
}
