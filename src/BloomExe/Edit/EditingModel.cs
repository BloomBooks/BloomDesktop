using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using Bloom.SendReceive;
using Bloom.ToPalaso.Experimental;
using Bloom.web;
using BloomTemp;
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
#if __MonoCS__
#else
using SIL.Media.Naudio;
#endif

namespace Bloom.Edit
{
	public class EditingModel
	{
		private readonly BookSelection _bookSelection;
		private readonly PageSelection _pageSelection;
		private readonly LanguageSettings _languageSettings;
		private readonly DuplicatePageCommand _duplicatePageCommand;
		private readonly DeletePageCommand _deletePageCommand;
		private readonly LocalizationChangedEvent _localizationChangedEvent;
		private readonly CollectionSettings _collectionSettings;
		private readonly SendReceiver _sendReceiver;
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
		private string _accordionFolder;
		private EnhancedImageServer _server;
		private readonly TemplateInsertionCommand _templateInsertionCommand;
		private Dictionary<string, IPage> _templatePagesDict;
		private string _lastPageAdded;
		internal IPage PageChangingLayout; // used to save the page on which the choose different layout command was invoked while the dialog is active.

		// These variables are not thread-safe. Access only on UI thread.
		private bool _inProcessOfSaving;
		private List<Action> _tasksToDoAfterSaving = new List<Action>();

		readonly List<string> _activeStandardListeners = new List<string>();

		//public event EventHandler UpdatePageList;

		public delegate EditingModel Factory();//autofac uses this

		public EditingModel(BookSelection bookSelection, PageSelection pageSelection,
			LanguageSettings languageSettings,
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
			SendReceiver sendReceiver,
			EnhancedImageServer server)
		{
			_bookSelection = bookSelection;
			_pageSelection = pageSelection;
			_languageSettings = languageSettings;
			_duplicatePageCommand = duplicatePageCommand;
			_deletePageCommand = deletePageCommand;
			_collectionSettings = collectionSettings;
			_sendReceiver = sendReceiver;
			_server = server;
			_templatePagesDict = null;
			_lastPageAdded = String.Empty;

			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);
			pageSelection.SelectionChanged += new EventHandler(OnPageSelectionChanged);
			pageSelection.SelectionChanging += OnPageSelectionChanging;
			templateInsertionCommand.InsertPage += new EventHandler(OnInsertTemplatePage);

			bookRefreshEvent.Subscribe((book) => OnBookSelectionChanged(null, null));
			pageRefreshEvent.Subscribe((book) => RethinkPageAndReloadIt(null));
			selectedTabChangedEvent.Subscribe(OnTabChanged);
			selectedTabAboutToChangeEvent.Subscribe(OnTabAboutToChange);
			duplicatePageCommand.Implementer = OnDuplicatePage;
			deletePageCommand.Implementer = OnDeletePage;
			pageListChangedEvent.Subscribe(x => _view.UpdatePageList(false));
			relocatePageEvent.Subscribe(OnRelocatePage);
			libraryClosingEvent.Subscribe(o=>SaveNow());
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
			_server.CurrentBook = CurrentBook;
			_templateInsertionCommand = templateInsertionCommand;
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
				_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.disconnectForGarbageCollection(); }");
				// This bizarre behavior prevents BL-2313 and related problems.
				// For some reason I cannot discover, switching tabs when focus is in the Browser window
				// causes Bloom to get deactivated, which prevents various controls from working.
				// Moreover, it seems (BL-2329) that if the user types Alt-F4 while whatever-it-is is active,
				// things get into a very bad state indeed. So arrange to re-activate ourselves as soon as the dust settles.
				_oldActiveForm = Form.ActiveForm;
				Application.Idle += ReactivateFormOnIdle;
				//note: if they didn't actually change anything, Chorus is not going to actually do a checkin, so this
				//won't polute the history
				_sendReceiver.CheckInNow(string.Format("Edited '{0}'", CurrentBook.TitleBestForUserDisplay));

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
			_server.CurrentBook = CurrentBook;
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
				_view.UpdatePageList(false);
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
			_view.UpdatePageList(false);
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

			if (page != null)
				_pageSelection.SelectPage(page);

			if (_view != null)
			{
				_view.UpdatePageList(false);
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
			if (_view != null && !_inProcessOfDeleting)
			{
				_view.ChangingPages = true;
				_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.pageSelectionChanging();}");
				_view.RunJavaScript("if (calledByCSharp) { calledByCSharp.disconnectForGarbageCollection(); }");
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
			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(_domForCurrentPage.RawDom);
			if (_currentPage != null)
				_currentPage.Dispose();
			_currentPage = EnhancedImageServer.MakeSimulatedPageFileInBookFolder(_domForCurrentPage, true);

			if (_currentlyDisplayedBook.BookInfo.ReaderToolsAvailable)
				_server.AccordionContent = MakeAccordionContent();
			else
				_server.AccordionContent = "<html><head><meta charset=\"UTF-8\"/></head><body></body></html>";

			_server.CurrentBook = _currentlyDisplayedBook;
			_server.AuthorMode = CanAddPages;
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
			var path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI/bookEdit", "EditViewFrame.htm");
			// {simulatedPageFileInBookFolder} is placed in the template file where we want the source file for the 'page' iframe.
			// We don't really make a file for the page, the contents are just saved in our local server.
			// But we give it a url that makes it seem to be in the book folder so local urls work.
			// See EnhancedImageServer.MakeSimulatedPageFileInBookFolder() for more details.
			var frameText = File.ReadAllText(path, Encoding.UTF8).Replace("{simulatedPageFileInBookFolder}", _currentPage.Key);
			var dom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(frameText));

			// only show the accordion when the template enables it
			var css_class = dom.Body.GetAttributeNode("class");

			if (css_class == null)
			{
				css_class = dom.Body.OwnerDocument.CreateAttribute("class");
				dom.Body.Attributes.Append(css_class);
			}

			if (_currentlyDisplayedBook.BookInfo.ReaderToolsAvailable)
				css_class.Value = "accordion";
			else
				css_class.Value = "no-accordion";

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
		/// Otherwise, sometimes the calledByCSharp object doesn't exist; then we don't call restoreAccordionSettings.
		/// If we don't call restoreAccordionSettings, then the more panel stays open without the checkboxes checked.
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
#if __MonoCS__
#else
			_audioRecording.PeakLevelChanged += (s, args) => _view.SetPeakLevel(args.Level.ToString(CultureInfo.InvariantCulture));
#endif
		}

		/// <summary>
		/// listen for these events raised by javascript.
		/// </summary>
		internal void AddStandardEventListeners()
		{
			AddMessageEventListener("saveAccordionSettingsEvent", SaveAccordionSettings);
			AddMessageEventListener("preparePageForEditingAfterOrigamiChangesEvent", RethinkPageAndReloadIt);
			AddMessageEventListener("setTopic", SetTopic);
			AddMessageEventListener("finishSavingPage", FinishSavingPage);
			AddMessageEventListener("handleAddNewPageKeystroke", HandleAddNewPageKeystroke);
			AddMessageEventListener("addPage", (id) => AddNewPageBasedOnTemplate(id));
			AddMessageEventListener("chooseLayout", (id) => ChangePageLayoutBasedOnTemplate(id));
			AddMessageEventListener("startRecordAudio", StartRecordAudio);
			AddMessageEventListener("endRecordAudio", EndRecordAudio);
			AddMessageEventListener("changeRecordingDevice", ChangeRecordingDevice);
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
		public void HandleAddNewPageKeystroke(string unused)
		{
			if (!HaveCurrentEditableBook || _currentlyDisplayedBook.LockedDown)
				return;

			try
			{
				if (CanDuplicatePage)
				{
					if (AddNewPageBasedOnTemplate(this._pageSelection.CurrentSelection.IdOfFirstAncestor))
						return;
				}
				var idOfFirstPageInTemplateBook = CurrentBook.FindTemplateBook().GetPageByIndex(0).Id;
				if (AddNewPageBasedOnTemplate(idOfFirstPageInTemplateBook))
					return;
			}
			catch (Exception error)
			{
				Logger.WriteEvent(error.Message);
				//this is not worth bothering the user about
#if DEBUG
				throw error;
#endif
			}
			//there was some error figuring out a default page, let's just let the user choose what they want
			if(this._view!=null)
				this._view.ShowAddPageDialog();
		}

		AudioRecording _audioRecording = new AudioRecording();
		/// <summary>
		/// Start recording audio for the current segment (whose ID is the argument)
		/// </summary>
		/// <param name="segmentId"></param>
		private void StartRecordAudio(string segmentId)
		{
			_audioRecording.Path = Path.Combine(_currentlyDisplayedBook.FolderPath, "audio", segmentId + ".wav");
			_audioRecording.StartRecording();
		}
		/// <summary>
		/// Stop recording and save the result.
		/// </summary>
		/// <param name="dummy"></param>
		private void EndRecordAudio(string dummy)
		{
			_audioRecording.StopRecording();
		}

		private Dictionary<string, IPage> GetTemplatePagesForThisBook()
		{
			if (_templatePagesDict != null)
				return _templatePagesDict;

			var templateBook = CurrentBook.FindTemplateBook();
			if (templateBook == null)
				return null;
			_templatePagesDict = templateBook.GetTemplatePagesIdDictionary();
			return _templatePagesDict;
		}

		private bool AddNewPageBasedOnTemplate(string pageId)
		{
			IPage page;
			var dict = GetTemplatePagesForThisBook();
			if (dict != null && dict.TryGetValue(pageId, out page))
			{
				_templateInsertionCommand.Insert(page as Page);
				_lastPageAdded = pageId;
				return true;
			}
			return false;
		}

		private void ChangePageLayoutBasedOnTemplate(string layoutId)
		{
			SaveNow();

			IPage page;
			var dict = GetTemplatePagesForThisBook();
			if (dict != null && dict.TryGetValue(layoutId, out page))
			{
				var templatePage = page.GetDivNodeForThisPage();
				var book = _pageSelection.CurrentSelection.Book;
				var pageToChange = PageChangingLayout ?? _pageSelection.CurrentSelection;
				book.UpdatePageToTemplate(book.OurHtmlDom, templatePage, pageToChange.Id);
				_lastPageAdded = layoutId; // Review
				// The Page objects are cached in the page list and may be used if we issue another
				// change layout command. We must update their lineage so the right "current layout"
				// will be shown if the user changes the layout of the same page again.
				var pageChanged = pageToChange as Page;
				if (pageChanged != null)
					pageChanged.UpdateLineage(new[] {layoutId});
				if (pageToChange.Id == _pageSelection.CurrentSelection.Id)
					_view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection);
				else
					_pageSelection.SelectPage(pageToChange);
			}
		}

		private void ChangeRecordingDevice(string deviceName)
		{
			_audioRecording.ChangeRecordingDevice(deviceName);
		}

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

			SaveNow();

			// "Origami" is the javascript system that lets the user introduce new elements to the page.
			// It can insert .bloom-translationGroup's, but it can't populate them with .bloom-editables
			// or set the proper classes on those editables to match the current multilingual settings.
			// So after a change, this eventually gets called. We then ask the page's book to fix things
			// up so that those boxes are ready to edit
			_domForCurrentPage = CurrentBook.GetEditableHtmlDomForPage(_pageSelection.CurrentSelection);
			_currentlyDisplayedBook.UpdateEditableAreasOfElement(_domForCurrentPage);

			//Enhance: Probably we could avoid having two saves, by determing what it is that they entail that is required.
			//But at the moment both of them are required
			SaveNow();
		}

		private bool CannotSavePage()
		{
			var returnVal = _bookSelection == null || CurrentBook == null || _pageSelection.CurrentSelection == null ||
				_currentlyDisplayedBook == null;

			if (returnVal)
				_view.ChangingPages = false;

			return returnVal;
		}

		private void SaveAccordionSettings(string data)
		{
			var args = data.Split(new[] { '\t' });

			switch (args[0])
			{
				case "showPE":
					UpdateActiveToolSetting("pageElements", args[1] == "1");
					return;

				case "showDRT":
					UpdateActiveToolSetting("decodableReader", args[1] == "1");
					return;

				case "showLRT":
					UpdateActiveToolSetting("leveledReader", args[1] == "1");
					return;

				case "current":
					_currentlyDisplayedBook.BookInfo.CurrentTool = AccordionDirectoryNameToToolName(args[1]);
					return;

				case "state":
					UpdateToolState(args[1], args[2]);
					return;
			}
		}

		private void UpdateToolState(string toolName, string state)
		{
			var tools = _currentlyDisplayedBook.BookInfo.Tools;
			var item = tools.FirstOrDefault(t => t.Name == toolName);

			if (item != null)
				item.State = state;
		}

		private void UpdateActiveToolSetting(string toolName, bool enabled)
		{
			var tools = _currentlyDisplayedBook.BookInfo.Tools;
			var item = tools.FirstOrDefault(t => t.Name == toolName);

			if (item == null)
				tools.Add(new AccordionTool() { Name = toolName, Enabled = enabled });
			else
				item.Enabled = enabled;
		}

		private static string AccordionToolNameToDirectoryName(string toolName)
		{
			switch (toolName)
			{
				case "pageElements":
					return "PageElements";

				case "decodableReader":
					return "DecodableRT";

				case "leveledReader":
					return "LeveledRT";
			}

			return string.Empty;
		}

		private static string AccordionDirectoryNameToToolName(string directoryName)
		{
			switch (directoryName)
			{
				case "PageElements":
					return "pageElements";

				case "DecodableRT":
					return "decodableReader";

				case "LeveledRT":
					return "leveledReader";
			}

			return string.Empty;
		}


		private string MakeAccordionContent()
		{
			var path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI/bookEdit/accordion", "Accordion.htm");
			_accordionFolder = Path.GetDirectoryName(path);

			var domForAccordion = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(path));

			// embed settings on the page
			var tools = _currentlyDisplayedBook.BookInfo.Tools.Where(t => t.Enabled == true).ToList();

			var settings = new Dictionary<string, object>
			{
				{"current", AccordionToolNameToDirectoryName(_currentlyDisplayedBook.BookInfo.CurrentTool)}
			};

			var decodableTool = tools.FirstOrDefault(t => t.Name == "decodableReader");
			if (decodableTool != null && !string.IsNullOrEmpty(decodableTool.State))
				settings.Add("decodableState", decodableTool.State);
			var leveledTool = tools.FirstOrDefault(t => t.Name == "leveledReader");
			if (leveledTool != null && !string.IsNullOrEmpty(leveledTool.State))
				settings.Add("leveledState", leveledTool.State);

			var settingsStr = JsonConvert.SerializeObject(settings);
			settingsStr = String.Format("function GetAccordionSettings() {{ return {0};}}", settingsStr) +
				"\n$(document).ready(function() { restoreAccordionSettings(GetAccordionSettings()); });";

			var scriptElement = domForAccordion.RawDom.CreateElement("script");
			scriptElement.SetAttribute("type", "text/javascript");
			scriptElement.SetAttribute("id", "ui-accordionSettings");
			scriptElement.InnerText = settingsStr;

			domForAccordion.Head.InsertAfter(scriptElement, domForAccordion.Head.LastChild);

			// get additional tabs to load
			var checkedBoxes = new List<string>();

			if (tools.Any(t => t.Name == "decodableReader"))
			{
				AppendAccordionPanel(domForAccordion, FileLocator.GetFileDistributedWithApplication(Path.Combine(_accordionFolder, "DecodableRT", "DecodableRT.htm")));
				checkedBoxes.Add("showDRT");
			}

			if (tools.Any(t => t.Name == "leveledReader"))
			{
				AppendAccordionPanel(domForAccordion, FileLocator.GetFileDistributedWithApplication(Path.Combine(_accordionFolder, "LeveledRT", "LeveledRT.htm")));
				checkedBoxes.Add("showLRT");
			}

			// Load settings into the accordion panel
			AppendAccordionPanel(domForAccordion, FileLocator.GetFileDistributedWithApplication(Path.Combine(_accordionFolder, "settings", "Settings.htm")));

			// check the appropriate boxes
			foreach (var checkBoxId in checkedBoxes)
			{
				domForAccordion.Body.SelectSingleNode("//div[@id='" + checkBoxId + "']").InnerXml = "&#10004;";
			}

			XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(domForAccordion.RawDom);
			return TempFileUtils.CreateHtml5StringFromXml(domForAccordion.RawDom);
		}

		/// <summary>Loads the requested panel into the accordion</summary>
		private void AppendAccordionPanel(HtmlDom domForAccordion, string fileName)
		{
			var accordion = domForAccordion.Body.SelectSingleNode("//div[@id='accordion']");
			var subPanelDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtmlFile(fileName));
			AppendAllChildren(subPanelDom.Body, accordion);
		}

		void AppendAllChildren(XmlNode source, XmlNode dest)
		{
			// Not sure, but the ToArray MIGHT be needed because AppendChild MIGHT remove the node from the source
			// which MIGHT interfere with iterating over them.
			foreach (var node in source.ChildNodes.Cast<XmlNode>().ToArray())
			{
				// It's nice if the independent HMTL file we are copying can have its own title, but we don't want to duplicate that into
				// our page document, which already has its own.
				if (node.Name == "title")
					continue;
				// It's no good copying file references; they may be useful for independent testing of the control source,
				// but the relative paths won't work. Any needed scripts must be re-included.
				if (node.Name == "script" && node.Attributes != null && node.Attributes["src"] != null)
					continue;
				if (node.Name == "link" && node.Attributes != null && node.Attributes["rel"] != null)
					continue; // likewise stylesheets must be inserted
				dest.AppendChild(dest.OwnerDocument.ImportNode(node,true));
			}
		}

		public void SaveNow()
		{
			if (_domForCurrentPage != null)
			{
				try
				{
					// CleanHtml already requires that we are on UI thread. But it's worth asserting here too in case that changes.
					// If we weren't sure of that we would need locking for access to _tasksToDoAfterSaving and _inProcessOfSaving,
					// and would need to be careful about whether any delayed tasks needed to be on the UI thread.
					Debug.Assert(!_view.InvokeRequired);
					_inProcessOfSaving = true;
					_tasksToDoAfterSaving.Clear();
					_view.CleanHtmlAndCopyToPageDom();

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
					CheckForBL2364();
					//OK, looks safe, time to save.
					_pageSelection.CurrentSelection.Book.SavePage(_domForCurrentPage);
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

		// One more attempt to catch whatever is causing us to get errors indicating that the page we're trying
		// to save is not in the book we're trying to save it into.
		private void CheckForBL2364()
		{
			try
			{
				XmlElement divElement =
					_domForCurrentPage.SelectSingleNodeHonoringDefaultNS("//div[contains(@class, 'bloom-page')]");
				string pageDivId = divElement.GetAttribute("id");
				if (pageDivId != _pageSelection.CurrentSelection.Id)
					throw new ApplicationException(
						"Bl-2634: id of _domForCurrentPage is not the same as ID of _pageSelection.CurrentSelection");
			}
			catch (Exception err)
			{
				if (err.StackTrace.Contains("DeletePage"))
					Logger.WriteEvent("Trying to save a page while executing DeletePage");
				Logger.WriteEvent("Error: SaveNow(): a mixup occurred in page IDs");
				throw;
			}
		}

		public void ChangePicture(GeckoHtmlElement img, PalasoImage imageInfo, IProgress progress)
		{
			try
			{
				Logger.WriteMinorEvent("Starting ChangePicture {0}...", imageInfo.FileName);
				var editor = new PageEditingModel();
				editor.ChangePicture(CurrentBook.FolderPath, new ElementProxy(img), imageInfo, progress);

				//we have to save so that when asked by the thumbnailer, the book will give the proper image
				SaveNow();
				CurrentBook.Storage.CleanupUnusedImageFiles();
				//but then, we need the non-cleaned version back there
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

		private string GetPathToCurrentTemplateHtml
		{
			get
			{
				var templateBook = CurrentBook.FindTemplateBook();
				if (templateBook == null)
					return null;

				return templateBook.GetPathHtmlFile();
			}
		}

		/// <summary>
		/// Returns a json string for initializing the AddPage dialog. It gives paths to our current TemplateBook
		/// and specifies whether the dialog is to be used for adding pages or choosing a different layout.
		/// </summary>
		/// <remarks>If forChooseLayout is true, page argument is required.</remarks>
		public string GetAddPageArguments(bool forChooseLayout, IPage page = null)
		{
			dynamic addPageSettings = new ExpandoObject();
			addPageSettings.lastPageAdded = _lastPageAdded;
			addPageSettings.orientation = CurrentBook.GetLayout().SizeAndOrientation.IsLandScape ? "landscape" : "portrait";
			dynamic collection1 = new ExpandoObject();
			collection1.templateBookFolderUrl = MassageUrlForJavascript(Path.GetDirectoryName(GetPathToCurrentTemplateHtml));
			collection1.templateBookUrl = MassageUrlForJavascript(GetPathToCurrentTemplateHtml);
			addPageSettings.collections = new[] {collection1};
			addPageSettings.chooseLayout = forChooseLayout;
			if (forChooseLayout)
				addPageSettings.currentLayout = page.IdOfFirstAncestor;
			var settingsString = JsonConvert.SerializeObject(addPageSettings);
			return settingsString;
		}

		public void ShowAddPageDialog()
		{
			_view.ShowAddPageDialog();
		}

		private const string URL_PREFIX = "/bloom/localhost/";

		private static string MassageUrlForJavascript(string url)
		{
			var newUrl = URL_PREFIX + url;
			return newUrl.Replace(':', '$').Replace('\\', '/');
		}

		internal void ChangePageLayout(IPage page)
		{
			PageChangingLayout = page;
			_view.ShowChangeLayoutDialog(page);
		}

		public void ChangeBookLicenseMetaData(Metadata metadata)
		{
			CurrentBook.SetMetadata(metadata);
			RefreshDisplayOfCurrentPage(); //the cleanup() that is part of Save removes qtips, so let's redraw everything
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
