//#define MEMORYCHECK
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ErrorReporter;
using Bloom.FontProcessing;
using Bloom.MiscUI;
using Bloom.ToPalaso.Experimental;
using Bloom.Utils;
using Bloom.web.controllers;
using DesktopAnalytics;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Miscellaneous;
using SIL.Xml;

namespace Bloom.Edit
{
    public class EditingModel
    {
        private readonly BookSelection _bookSelection;
        private readonly PageSelection _pageSelection;
        private readonly DuplicatePageCommand _duplicatePageCommand;
        private readonly DeletePageCommand _deletePageCommand;
        private readonly CollectionSettings _collectionSettings;
        private readonly ITemplateFinder _sourceCollectionsList;
        private bool _havePageToSave;

        public bool Visible;
        private Book.Book _currentlyDisplayedBook;
        private Book.Book _bookForToolboxContent;
        private EditingView _view;
        private List<ContentLanguage> _contentLanguages;
        private IPage _previouslySelectedPage;
        private bool _inProcessOfDeleting;
        private bool _inProcessOfLoading;
        private BloomServer _server;
        private readonly BloomWebSocketServer _webSocketServer;
        internal IPage PageChangingLayout; // used to save the page on which the choose different layout command was invoked while the dialog is active.

        // This event fires after the EditingModel has finished responding to a PageSelection change.
        internal event EventHandler PageSelectModelChangesComplete;

        // These variables are not thread-safe. Access only on UI thread.
        internal bool InProcessOfSaving;
        private List<Action> _tasksToDoAfterSaving = new List<Action>();

        // Perhaps a bit hack-ish, but this causes a full save to be done when our datadiv has been modified
        // but it's not obvious from the dataset changes. If we make new 'data-derived' divs someday, changing them
        // must set this flag to ensure the information gets saved properly.
        private bool _pageHasUnsavedDataDerivedChange;

        readonly List<string> _activeStandardListeners = new List<string>();

        internal const string PageScalingDivId = "page-scaling-container";

        /// <summary>
        /// Currently this is only valid in EditingView, since it depends on the Javascript code being
        /// configured to send appropriate messages to the editView/setIsSelectionRange API.
        /// </summary>
        public static bool IsTextSelected;

        // these 3 are used as part of automatically re-rerendering a page when a developer changes something in the supporting files
        private FileSystemWatcher _developerFileWatcher;
        private DateTime _lastTimeWeReloadedBecauseOfDeveloperChange;
        private bool _skipNextSaveBecauseDeveloperIsTweakingSupportingFiles;

        //public event EventHandler UpdatePageList;

        public delegate EditingModel Factory(); //autofac uses this

        public EditingModel(
            BookSelection bookSelection,
            PageSelection pageSelection,
            TemplateInsertionCommand templateInsertionCommand,
            PageListChangedEvent pageListChangedEvent,
            RelocatePageEvent relocatePageEvent,
            BookRefreshEvent bookRefreshEvent,
            PageRefreshEvent pageRefreshEvent,
            DuplicatePageCommand duplicatePageCommand,
            DeletePageCommand deletePageCommand,
            SelectedTabChangedEvent selectedTabChangedEvent,
            SelectedTabAboutToChangeEvent selectedTabAboutToChangeEvent,
            CollectionClosing collectionClosingEvent,
            LocalizationChangedEvent localizationChangedEvent,
            CollectionSettings collectionSettings,
            BloomServer server,
            BloomWebSocketServer webSocketServer,
            ITemplateFinder sourceCollectionsList
        )
        {
            _bookSelection = bookSelection;
            _pageSelection = pageSelection;
            _duplicatePageCommand = duplicatePageCommand;
            _deletePageCommand = deletePageCommand;
            _collectionSettings = collectionSettings;
            _server = server;
            _webSocketServer = webSocketServer;
            _sourceCollectionsList = sourceCollectionsList;

            bookSelection.SelectionChanged += OnBookSelectionChanged;
            pageSelection.SelectionChanged += OnPageSelectionChanged;
            pageSelection.SelectionChanging += OnPageSelectionChanging;
            templateInsertionCommand.InsertPage += OnInsertPage;

            bookRefreshEvent.Subscribe(
                (book) =>
                {
                    if (book == CurrentBook)
                    {
                        OnBookSelectionChanged(null, null);
                    }
                }
            );
            pageRefreshEvent.Subscribe(
                (PageRefreshEvent.SaveBehavior behavior) =>
                {
                    switch (behavior)
                    {
                        case PageRefreshEvent.SaveBehavior.SaveBeforeRefresh:
                            RethinkPageAndReloadIt();
                            break;

                        case PageRefreshEvent.SaveBehavior.SaveBeforeRefreshFullSave:
                            RethinkPageAndReloadIt(true);
                            break;

                        case PageRefreshEvent.SaveBehavior.JustRedisplay:
                            RefreshDisplayOfCurrentPage();
                            break;
                    }
                }
            );

            selectedTabChangedEvent.Subscribe(OnTabChanged);
            selectedTabAboutToChangeEvent.Subscribe(OnTabAboutToChange);
            duplicatePageCommand.Implementer = OnDuplicatePage;
            deletePageCommand.Implementer = OnDeletePage;
            pageListChangedEvent.Subscribe(needFullUpdate => _view.UpdatePageList(needFullUpdate));
            relocatePageEvent.Subscribe(OnRelocatePage);
            collectionClosingEvent.Subscribe(o =>
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
                    RefreshDisplayOfCurrentPage(true);
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

            if (Debugger.IsAttached)
            {
                StartWatchingDeveloperChanges();
            }
        }

        ~EditingModel()
        {
            // Note, as far as I can tell, EditingModels are never disposed of, so this is never called.
            // New ones are created each time you open a new collection.
            if (_developerFileWatcher != null)
            {
                _developerFileWatcher.Dispose();
                _developerFileWatcher = null;
            }
        }

        private Form _oldActiveForm;
        private XmlElement _pageDivFromCopyPage;
        private string _bookPathFromCopyPage;

        internal BloomWebSocketServer EditModelSocketServer
        {
            get { return _webSocketServer; }
        }

        /// <summary>
        /// we need to guarantee that we save *before* any other tabs try to update, hence this "about to change" event
        /// </summary>
        /// <param name="details"></param>
        private void OnTabAboutToChange(TabChangedDetails details)
        {
            if (details.From == _view)
            {
                if (!SaveAndReportIfItFails())
                {
                    // we might want to keep the user on the tab, but at the moment we're debugging
                    // the actual failure and don't want to pause to make it possible to prevent the
                    // tab from changing.
                }

                // This bizarre behavior prevents BL-2313 and related problems.
                // For some reason I cannot discover, switching tabs when focus is in the Browser window
                // causes Bloom to get deactivated, which prevents various controls from working.
                // Moreover, it seems (BL-2329) that if the user types Alt-F4 while whatever-it-is is active,
                // things get into a very bad state indeed. So arrange to re-activate ourselves as soon as the dust settles.
                _oldActiveForm = Form.ActiveForm;
                Application.Idle += ReactivateFormOnIdle;
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

        private void OnBookSelectionChanged(
            object sender,
            BookSelectionChangedEventArgs bookSelectionChangedEventArgs
        )
        {
            // Sometimes we raise this event just to update various status things in the collections tab.
            // This edit tab can ignore changes that don't actually involve selecting a different book.
            if (_bookSelection.CurrentSelection == _currentlyDisplayedBook)
                return;
            //prevent trying to save this page in whatever comes next
            var hadPageToSave = _havePageToSave;
            _havePageToSave = false;
            _currentlyDisplayedBook = null;
            if (Visible)
            {
                _view.ClearOutDisplay();
                if (hadPageToSave)
                    _view.UpdatePageList(false);
            }
        }

        internal void OnDuplicatePage()
        {
            DuplicatePage(_pageSelection.CurrentSelection);
        }

        internal void DuplicateManyPages(IPage page)
        {
            using (var dlg = new ReactDialog("duplicateManyDlgBundle"))
            {
                dlg.Width = 400;
                dlg.Height = 235;
                // This dialog is neater without a task bar. We don't need to be able to
                // drag it around. There's nothing left to give it one if we don't set a title
                // and remove the control box.
                dlg.ControlBox = false;
                dlg.ShowDialog();
            }
        }

        internal void DuplicatePage(IPage page)
        {
            DuplicatePageInternal(page);
        }

        /// <summary>
        /// Used by EditingViewApi when the user clicks OK in the ReactDialog that asks how many times to duplicate.
        /// </summary>
        /// <param name="numberOfTimes"></param>
        public void DuplicatePageManyTimes(int numberOfTimes)
        {
            var currentPage = _pageSelection?.CurrentSelection;
            if (currentPage == null || numberOfTimes > 999 || numberOfTimes < 1)
            {
                return; // Probably can't happen, but...
            }

            DuplicatePageInternal(_pageSelection.CurrentSelection, numberOfTimes);
        }

        private void DuplicatePageInternal(IPage page, int numberOfTimesToDuplicate = 1)
        {
            // NB: though there is an api call to do this, it isn't currently used, so we have to measure here.
            var countString = numberOfTimesToDuplicate.ToString();
            using (PerformanceMeasurement.Global.Measure("Duplicate page"))
            {
                try
                {
                    SaveNow(); //ensure current page is saved first
                    _havePageToSave = false; //prevent us trying to save it later, as the page selection changes
                    _currentlyDisplayedBook.DuplicatePage(page, numberOfTimesToDuplicate);
                    // Book.DuplicatePage() updates the page list so we don't need to do it here.
                    // (See http://issues.bloomlibrary.org/youtrack/issue/BL-3715.)
                    //_view.UpdatePageList(false);
                    Logger.WriteEvent(
                        "Duplicate Page"
                            + (numberOfTimesToDuplicate > 0 ? " " + countString + " times" : "")
                    );
                    Analytics.Track("Duplicate Page");
                }
                catch (Exception error)
                {
                    ErrorReport.NotifyUserOfProblem(
                        error,
                        "Could not duplicate that page. Try quiting Bloom, run it again, and then attempt to duplicate the page again. And please click 'details' below and report this to us."
                    );
                }
            }
        }

        internal void OnDeletePage()
        {
            DeletePage(_pageSelection.CurrentSelection);
        }

        internal void DeletePage(IPage page)
        {
            // This can only be called on the UI thread in response to a user button click.
            // If that ever changed we might need to arrange locking for access to InProcessOfSaving and _tasksToDoAfterSaving.
            Debug.Assert(!_view.InvokeRequired);
            if (InProcessOfSaving && page == _pageSelection.CurrentSelection)
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
                catch (Exception saveError)
                {
                    // we don't want to prevent deleting a problematic page, so just show a toast
                    NonFatalProblem.Report(
                        ModalIf.Alpha,
                        PassiveIf.All,
                        "Error during pre-delete save",
                        exception: saveError
                    );
                }

                _inProcessOfDeleting = true;
                _havePageToSave = false; //prevent us trying to save it later, as the page selection changes
                _currentlyDisplayedBook.DeletePage(page);
                //_view.UpdatePageList(false);  DeletePage calls this via pageListChangedEvent.  See BL-3632 for trouble this causes.
                Logger.WriteEvent("Delete Page");
                Analytics.Track("Delete Page");
            }
            catch (Exception error)
            {
                ErrorReport.NotifyUserOfProblem(
                    error,
                    "Could not delete that page. Try quiting Bloom, run it again, and then attempt to delete the page again. And please click 'details' below and report this to us."
                );
            }
            finally
            {
                _inProcessOfDeleting = false;
            }
        }

        private void OnRelocatePage(RelocatePageInfo info)
        {
            info.Cancel = !CurrentBook.RelocatePage(info.Page, info.IndexOfPageAfterMove);
            if (!info.Cancel)
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
            SaveNow(); // there might be unsaved changes in the current page from before we clicked Add Page
            CurrentBook.InsertPageAfter(
                DeterminePageWhichWouldPrecedeNextInsertion(),
                page as Page,
                e.NumberToAdd
            );
            //_view.UpdatePageList(false);  InsertPageAfter calls this via pageListChangedEvent.  See BL-3632 for trouble this causes.
            //_pageSelection.SelectPage(newPage);
            if (e.FromTemplate)
            {
                try
                {
                    Analytics.Track(
                        "Insert Template Page",
                        new Dictionary<string, string>
                        {
                            { "template-source", (page as IPage).Book.Title },
                            { "page", (page as IPage).Caption }
                        }
                    );
                }
                catch (Exception) { }
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

        public IPage CurrentPage => _pageSelection.CurrentSelection;

        public bool CanAddPages => !CurrentBook.IsCalendar;

        public bool CanDuplicatePage
        {
            get
            {
                return _pageSelection != null
                    && _pageSelection.CurrentSelection != null
                    && !_pageSelection.CurrentSelection.Required
                    && _currentlyDisplayedBook != null;
            }
        }

        public bool CanCopyPage
        {
            // Currently we don't want to allow copying xmatter pages. If we ever do, some research and non-trivial change
            // will probably be needed, not just removing the restriction. Xmatter pages have classes set on them which will cause
            // Bloom to delete them when the book is next opened. They also tend to be singletons, which may cause problems if
            // we let the user make multiple ones.
            // Note that we don't need the editability restrictions here, since copy doesn't modify this book.
            get
            {
                return _pageSelection != null
                    && _pageSelection.CurrentSelection != null
                    && !_pageSelection.CurrentSelection.IsXMatter;
            }
        }

        // For now, the same rules govern copying hyperlinks. Working hyperlinks to xmatter pages are difficult,
        // since each bring-book-up-to-date recreates the xmatter pages and gives them different IDs, so we'd need
        // a different strategy for identifying them. And then, choosing a different xmatter pack might cause a page
        // to cease to exist altogether.
        public bool CanCopyHyperlink => CanCopyPage;

        public bool CanDeletePage
        {
            get
            {
                return _pageSelection != null
                    && _pageSelection.CurrentSelection != null
                    && !_pageSelection.CurrentSelection.Required
                    && _currentlyDisplayedBook != null;
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
                    // TODO: use a method that gets all the collection languages when we have more than three.
                    // (We'll have to do something to stop the user choosing more than three.)
                    _contentLanguages.Add(new ContentLanguage(_collectionSettings.Language1));

                    //NB: these won't *always* be tied to the national and regional languages, but they are for now. We would need more UI, without making for extra complexity
                    if (_collectionSettings.Language2Tag != _collectionSettings.Language1Tag)
                    {
                        var item2 = new ContentLanguage(_collectionSettings.Language2);
                        _contentLanguages.Add(item2);
                    }

                    if (_collectionSettings.Language3 != null)
                    {
                        var item3 = new ContentLanguage(_collectionSettings.Language3);
                        _contentLanguages.Add(item3);
                    }
                }
                // update which ones are selected. Since there may be ones with Selected true from a previous book,
                // clear them first, so we end up with selections appropriate to this one.
                _contentLanguages.ForEach(l => l.Selected = false);
                var lang1 = _contentLanguages.FirstOrDefault(
                    l => l.LangTag == _bookSelection.CurrentSelection.Language1Tag
                );
                // We must have one language selected. If nothing matches, select the first.
                if (lang1 == null)
                    lang1 = _contentLanguages[0];
                lang1.Selected = true;

                var lang2 = _contentLanguages.FirstOrDefault(
                    l => l.LangTag == _bookSelection.CurrentSelection.Language2Tag
                );
                if (lang2 != null)
                    lang2.Selected = true;
                var lang3 = _contentLanguages.FirstOrDefault(
                    l => l.LangTag == _bookSelection.CurrentSelection.Language3Tag
                );
                if (lang3 != null)
                    lang3.Selected = true;

                return _contentLanguages;
            }
        }

        public IEnumerable<Layout> GetSizeAndOrientationChoices()
        {
            foreach (var sizeChoice in CurrentBook.GetSizeAndOrientationChoices())
            {
                yield return sizeChoice;
            }
        }

        public void SetLayout(Layout layout)
        {
            SaveNow();
            var changedOrientation =
                CurrentBook.GetLayout().SizeAndOrientation.IsLandScape
                != layout.SizeAndOrientation.IsLandScape;
            CurrentBook.SetLayout(layout);
            if (changedOrientation)
            {
                // We need to update the xmatter, since this process selects images to display based on orientation.
                // (Here we need to do it even if we already brought this book up to date when it was selected.)
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
            RefreshDisplayOfCurrentPage();

            _view.UpdatePageList(true); //counting on this to redo the thumbnails
        }

        /// <summary>
        /// user has selected or de-selected a content language
        /// </summary>
        public void ContentLanguagesSelectionChanged()
        {
            Logger.WriteEvent("Changing Content Languages");
            var contentLanguages = GetMultilingualContentLanguages();

            //Reload to display these changes
            CurrentBook.SetMultilingualContentLanguages(contentLanguages); // set langs before saving page
            // The language choice is saved in the data-div, so we must do a full save even if this
            // page doesn't contain anything else that has non-local effects.
            SaveNow(true);
            CurrentBook.PrepareForEditing();
            RefreshDisplayOfCurrentPage();
            _view.UpdatePageList(true); //counting on this to redo the thumbnails

            Logger.WriteEvent("ChangingContentLanguages");
            Analytics.Track("Change Content Languages");
        }

        // Get current MultilingualContentLanguage settings based on what's been recently checked/unchecked.
        // N.B. Unless we're calling this from a more general display update we do NOT want to update ContentLanguages
        // first, as that will change the 'checked' status back to what it was.
        private string[] GetMultilingualContentLanguages()
        {
            return _contentLanguages.Where(l => l.Selected).Select(l => l.LangTag).ToArray();
        }

        public int NumberOfDisplayedLanguages
        {
            get { return ContentLanguages.Where(l => l.Selected).Count(); }
        }

        public class ContentLanguage
        {
            public readonly string LangTag;
            public readonly string Name;
            private readonly WritingSystem _ws;

            public ContentLanguage(WritingSystem ws)
            {
                LangTag = ws.Tag;
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

        public void OnBecomeVisible()
        {
            _view.CheckFontAvailability();
            if (_currentlyDisplayedBook != CurrentBook)
            {
                // We must update the ContentLanguages. We've switched books, and it is supposed to reflect
                // which languages are selected in the current book. Note that this code makes sure that
                // the LIST of languages reflects the current collection settings, but which ones are
                // SELECTED reflects the current book.
                // I'm retaining the following comment because previously we did not call ContentLanguages
                // unless _contentLanguages.Count was zero. That was wrong (BL-11318) but the issue reference
                // just might be useful if there is some reason we did NOT want to do it, in which case we'll
                // need more hard thought how to prevent BL-11318.
                //		BL-5973 GetMultilingualContentLanguages() doesn't want to update _contentLanguages
                //		normally, but in this case we do.
                var dummy = ContentLanguages; // updates _contentLanguages based on CurrentBook and collection settings
                // Reset the book's languages in case the user changed the collection's languages.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-5444.
                // But (see above) this should NOT mess with which languages are selected for display in the book
                // (unless a previously selected language is no longer a valid collection language).
                var contentLanguages = GetMultilingualContentLanguages();
                CurrentBook.SetMultilingualContentLanguages(contentLanguages);
                CurrentBook.PrepareForEditing();
            }

            _currentlyDisplayedBook = CurrentBook;

            var errors = _currentlyDisplayedBook.CheckForErrors();
            if (!String.IsNullOrEmpty(errors))
            {
                ErrorReport.NotifyUserOfProblem(errors);
                return;
            }

            ErrorReportUtils.CheckForFakeTestErrorsIfNotRealUser(_currentlyDisplayedBook.Title);

            // BL-2339: try to choose the last edited page
            var page =
                _currentlyDisplayedBook.GetPageByIndex(
                    _currentlyDisplayedBook.UserPrefs.MostRecentPage
                ) ?? _currentlyDisplayedBook.FirstPage;
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
        // PageSelection.SelectPage [CS] raises PageSelection SelectionChanging event.
        //		OnPageSelectionChanging() [CS, here] responds to this event
        //			it calls pageSelectionChanging() [in JS]
        //				and then calls FinishSavingPage() [CS] (hopefully after the JS code has finished executing)
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
        int _selChangeCount = 0;
        object _selChangeLock = new object();

        private void OnPageSelectionChanging(object sender, EventArgs eventArgs)
        {
            try
            {
                lock (_selChangeLock)
                {
                    Debug.Assert(
                        _selChangeCount == 0,
                        $"Multiple active OnPageSelectionChanging calls, possible  empty marginBox cause? (count={_selChangeLock})"
                    );
                    _selChangeCount++;
                }
                OnPageSelectionChangingInternal(sender, eventArgs);
            }
            finally
            {
                lock (_selChangeLock)
                {
                    _selChangeCount--;
                }
            }
        }

        private void OnPageSelectionChangingInternal(object sender, EventArgs eventArgs)
        {
            if (
                _view != null
                && !_inProcessOfDeleting
                && !_inProcessOfLoading
                && !_skipNextSaveBecauseDeveloperIsTweakingSupportingFiles
            )
            {
                _view.HidePageAndShowWaitCursor(true);
                try
                {
                    SavePageWithTiming();
                }
                catch
                {
                    _view.HidePageAndShowWaitCursor(false);
                    throw;
                }
            }
            _skipNextSaveBecauseDeveloperIsTweakingSupportingFiles = false;
        }

        void OnPageSelectionChanged(object sender, EventArgs e)
        {
            Logger.WriteMinorEvent("changing page selection");
            Analytics.Track("Select Page"); //not "edit page" because at the moment we don't have the capability of detecting that.

            // Trace memory usage in case it may be useful
            // First see if we seem to have a problem without taking time (~100ms in a large book/fast computer) to force GC.
            // If we seem to have a problem do it again forcing the GC and possibly warning the user.
            if (MemoryManagement.CheckMemory(false, "switched page in edit", false, false))
                MemoryManagement.CheckMemory(false, "switched page in edit", true);

            if (_view != null)
            {
                if (_previouslySelectedPage != null && _havePageToSave)
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

                _pageSelection.CurrentSelection.Book.BringPageUpToDate(
                    _pageSelection.CurrentSelection.GetDivNodeForThisPage()
                );
                if (Visible)
                    RefreshDisplayOfCurrentPage();

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
                if (nodeList == null)
                {
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
                            var shortMsg =
                                "Corrupt Book - Duplicate audio ID. Please report this issue (and to receive help fixing the audio IDs in this book).\nAudio files in this book may become lost or overwritten.";
                            var longMsg =
                                $"Duplicate GUID {id} on recordable with text \"{node.InnerText}\". See BL-8852.";
                            NonFatalProblem.Report(ModalIf.All, PassiveIf.None, shortMsg, longMsg);

                            // Only it report it once per book (per time),
                            // No need to report multiple modals at the same time
                            return;
                        }
                    }
                }
            }
        }

        public void RefreshDisplayOfCurrentPage(bool changingUiLanguage = false)
        {
            _view.UpdateSingleDisplayedPage(_pageSelection.CurrentSelection, changingUiLanguage);
        }

        public void UpdateMetaData(string url)
        {
            var match = UrlPathString.CreateFromUnencodedString(url).UrlEncoded;
            var imgElt = _pageSelection.CurrentSelection
                .GetDivNodeForThisPage()
                .SafeSelectNodes($".//img[@src='{match}']")
                .Cast<XmlElement>()
                .FirstOrDefault();
            if (imgElt == null)
                return; // log? unexpected
            ImageUpdater.UpdateImgMetadataAttributesToMatchImage(
                CurrentBook.FolderPath,
                imgElt,
                new NullProgress()
            );
            RefreshDisplayOfCurrentPage();
        }

        private DataSet _pageDataBeforeEdits;
        private string _featureRequirementsBeforeEdits;

        private DataSet GetPageData(XmlNode page)
        {
            var data = new DataSet();
            CurrentBook.BookData.GatherDataItemsFromXElement(
                data,
                page,
                new HashSet<Tuple<string, string>>()
            );
            return data;
        }

        public static string GetEditPageIframeContents(Book.Book book, string pageId)
        {
            var page = book.GetPages().FirstOrDefault(page => page.Id == pageId);
            Guard.AgainstNull(page, "Could not find expected page");
            return GetEditPageIframeContents(book, page);
        }

        public static string GetEditPageIframeContents(Book.Book book, IPage page)
        {
            return GetEditPageIframeDom(book, page).getHtmlStringDisplayOnly();
        }

        public static HtmlDom GetEditPageIframeDom(Book.Book book, IPage page)
        {
            var dom = book.GetEditableHtmlDomForPage(page);
            AddMissingCopyrightNoticeIfNeeded(book, dom);
            SetupPageZoom(dom);
            book.InsertFullBleedMarkup(dom.Body);
            XmlHtmlConverter.MakeXmlishTagsSafeForInterpretationAsHtml(dom.RawDom);
            InsertLabelAndLayoutTogglePane(dom);
            // We might want something like this? I think just for debugging?
            // dom.Title = InMemoryHtmlFile.GetTitleForProcessExplorer(source) + " (InMemoryHtmlFile)"; // makes this show up in Windows Process Explorer WebView2 listing
            return dom;
        }

        public void SaveStateForFullSaveDecision()
        {
            _pageDataBeforeEdits = GetPageData(
                _pageSelection.CurrentSelection.GetDivNodeForThisPage()
            );
            _featureRequirementsBeforeEdits = CurrentBook.OurHtmlDom.GetMetaValue(
                "FeatureRequirement",
                ""
            );
            _havePageToSave = true;
        }

        private static void AddMissingCopyrightNoticeIfNeeded(Book.Book book, HtmlDom dom)
        {
            var licenseBlock = dom.SafeSelectNodes(".//div[@class='licenseBlock']")
                .Cast<XmlElement>()
                .FirstOrDefault();
            if (licenseBlock == null)
                return; // not the relevant page
            var metadata = book.GetLicenseMetadata();
            // BL-10360 says that we don't want this notice for CC0, even if metadata is not complete.
            // But that situation is not currently possible through our UI, and further thought
            // suggests we want to know who says it is CC0. So commenting that aspect out.
            var copyrightOk = metadata.IsMinimallyComplete; // || metadata.License?.Token == "cc0";
            var firstElementChild = licenseBlock.ChildNodes
                .Cast<XmlNode>()
                .FirstOrDefault(x => x is XmlElement);
            var haveMissingNotice =
                firstElementChild?.Attributes?["class"]?.Value == "ui-missingCopyrightNotice";
            if (haveMissingNotice && copyrightOk)
                licenseBlock.RemoveChild(firstElementChild);
            else if (!copyrightOk && !haveMissingNotice)
            {
                var div = licenseBlock.OwnerDocument.CreateElement("div");
                var anchor = licenseBlock.OwnerDocument.CreateElement("a");
                div.AppendChild(anchor);
                div.SetAttribute("class", "ui-missingCopyrightNotice"); // don't save this
                anchor.InnerText = LocalizationManager.GetString(
                    "Copyright.MissingCopyright",
                    "Needs Copyright"
                );
                anchor.SetAttribute(
                    "href",
                    "javascript:(window.parent || window).editTabBundle.showCopyrightAndLicenseDialog();"
                );
                licenseBlock.InsertBefore(div, licenseBlock.FirstChild);
            }
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
        /// And https://issues.bloomlibrary.org/youtrack/issue/BL-11640 and
        /// https://issues.bloomlibrary.org/youtrack/issue/BL-12253.
        /// </remarks>
        private static void SetupPageZoom(HtmlDom dom)
        {
            var pageZoom = EditingView.ZoomSetting / 100F;
            var body = dom.Body;
            var pageDiv =
                body.SelectSingleNode("//div[contains(concat(' ', @class, ' '), ' bloom-page ')]")
                as XmlElement;
            if (pageDiv != null)
            {
                var outerDiv = InsertContainingScalingDiv(body, pageDiv);
                // The HTML expects floating point values in the invariant culture, not the current culture.
                // See https://issues.bloomlibrary.org/youtrack/issue/BL-5579.
                var zoomString = pageZoom.ToString(CultureInfo.InvariantCulture);
                // If we don't set the width, any zoom will cause the page will be too wide and there will be an unnecessary
                // horizontal scrollbar (BL-11640). If we just say 'fit-content', the page will be too narrow and the
                // hint bubbles (especially; BL-12253) will be too constrained.
                // Subtracting 5px from 100% ensures that we don't have a horizontal scrollbar and leaves a small margin
                // between the main page and the toolbox.
                outerDiv.SetAttribute(
                    "style",
                    String.Format(
                        "transform: scale({0}); transform-origin: left top; width: calc((100% - 5px) / {0})",
                        zoomString
                    )
                );
            }
        }

        static XmlElement InsertContainingScalingDiv(XmlElement body, XmlElement pageDiv)
        {
            // Note: because this extra div is OUTSIDE the page div, we don't have to remove it later,
            // because only the page div and its contents are saved back to the permanent file.
            var newDiv = body.OwnerDocument.CreateElement("div");
            newDiv.SetAttribute("id", PageScalingDivId);
            body.PrependChild(newDiv);
            newDiv.AppendChild(pageDiv);
            return newDiv;
        }

        public string GetUrlForCurrentPage()
        {
            return BloomServer.UrlForCurrentBookPageEncodedForIframeSrc(
                _bookSelection.CurrentSelection.FolderPath,
                _pageSelection.CurrentSelection.Id
            );
        }

        /// <summary>
        /// Return the top-level document that should be displayed in the browser for the current page.
        /// </summary>
        /// <returns></returns>
        public HtmlDom GetXmlDocumentForEditScreenWebPage()
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                Path.Combine(BloomFileLocator.BrowserRoot, "bookEdit", "EditViewFrame.html")
            );
            // {simulatedPageFileInBookFolder} is placed in the template file where we want the source file for the 'page' iframe.
            // We don't really make a file for the page, the contents are just saved in our local server.
            // But we give it a url that makes it seem to be in the book folder so local urls work.
            // See BloomServer.MakeInMemoryHtmlFileInBookFolder() for more details.
            var frameText = RobustFile
                .ReadAllText(path, Encoding.UTF8)
                .Replace("{simulatedPageFileInBookFolder}", GetUrlForCurrentPage());
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
                    "Debug Only: BL-422 reproduction (currentlyDisplayedBook was null in OnIdleAfterDocumentSupposedlyCompleted)."
                );
                Logger.WriteEvent(
                    "BL-422 happened just now (currentlyDisplayedBook was null in OnIdleAfterDocumentSupposedlyCompleted)."
                );
                return;
            }
        }

        internal void SaveToolboxSettings(string data)
        {
            // ref BL-9859, BL-9912, BL-9978
            // If _currentlyDisplayedBook is null, it's because we got the API call to save
            // tool state too late. The book has already been saved and we're back on
            // the Collection Tab. In testing with the leveled and decodable readers,
            // I found that the important state, like what
            // level we are on, sort order, etc. has already been saved.
            if (_currentlyDisplayedBook != null)
            {
                ToolboxView.SaveToolboxSettings(_currentlyDisplayedBook, data);
                EnsureLevelAttrCorrect();
            }
        }

        private void EnsureLevelAttrCorrect()
        {
            var currentLevel = _currentlyDisplayedBook.OurHtmlDom.Body.Attributes[
                "data-leveledreaderlevel"
            ]?.Value;
            var correctLevel =
                _currentlyDisplayedBook.BookInfo.MetaData.LeveledReaderLevel.ToString();
            if (correctLevel != currentLevel)
            {
                SaveNow();
                _currentlyDisplayedBook.OurHtmlDom.Body.SetAttribute(
                    "data-leveledreaderlevel",
                    correctLevel
                );
                RethinkPageAndReloadIt();
            }
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
        //			if (!HaveCurrentEditableBook)
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

        //invoked from TopicChooserDialog.tsx via API
        internal void SetTopic(string englishTopicAsKey)
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

        internal void RethinkPageAndReloadItAndReportIfItFails(bool forceFullSave = false)
        {
            try
            {
                RethinkPageAndReloadIt(forceFullSave);
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(ModalIf.Beta, PassiveIf.Beta, e.Message, e.ToString());
            }
        }

        internal void RethinkPageAndReloadIt(bool forceFullSave = false)
        {
            if (CannotSavePage())
                return;
            SavePageWithTiming(forceFullSave);
            RefreshDisplayOfCurrentPage();
        }

        /// <summary>
        /// Called as a result of page selection changing, leaving the Edit tab, or other event that requests a save and reload.
        /// </summary>
        private void SavePageWithTiming(bool forceFullSave = false)
        {
            if (CannotSavePage())
                return;

            var stopwatch = Stopwatch.StartNew();
            SaveNow(forceFullSave);
            stopwatch.Stop();
            Debug.WriteLine("Save Now Elapsed Time: {0} ms", stopwatch.ElapsedMilliseconds);
        }

        private bool CannotSavePage()
        {
            var returnVal =
                _bookSelection == null
                || CurrentBook == null
                || _pageSelection.CurrentSelection == null
                || _currentlyDisplayedBook == null;

            if (returnVal)
                _view.HidePageAndShowWaitCursor(false);

            return returnVal;
        }

        // We set this true for the interval between starting to navigate to a new
        // page and when it is loaded. This prevents trying to save when things are in an unstable state
        // (e.g., BL-2634, BL-6296). It may also prevent some wasted Saves and thus improve performance.
        public bool NavigatingSoSuspendSaving = false;
        private System.Windows.Forms.Timer _developerFileWatcherQuietTimer;
        private bool _weHaveSeenAJsonChange;

        int _saveCount = 0;
        object _saveCountLock = new object();

        public bool SaveAndReportIfItFails(bool forceFullSave = false)
        {
            try
            {
                SaveNow(forceFullSave);
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(ModalIf.Beta, PassiveIf.Beta, e.Message, e.ToString());
                return false;
            }
            return true;
        }

        public void SaveNow(bool forceFullSave = false)
        {
            try
            {
                lock (_saveCountLock)
                {
                    Debug.Assert(
                        _saveCount == 0,
                        $"Trying to save while already saving: possible empty marginBox cause? (save count={_saveCount})"
                    );
                    _saveCount++;
                }
                SaveNowInternal(forceFullSave);
            }
            finally
            {
                lock (_saveCountLock)
                {
                    _saveCount--;
                }
            }
        }

        private void SaveNowInternal(bool forceFullSave = false)
        {
            if (_havePageToSave && !InProcessOfSaving && !NavigatingSoSuspendSaving)
            {
                Logger.WriteMinorEvent("EditingModel.SaveNow() starting");
#if MEMORYCHECK
                // Check memory for the benefit of developers.
                MemoryManagement.CheckMemory(false, "before EditingModel.SaveNow()", false);
#endif
                try
                {
                    _webSocketServer.SendString("pageThumbnailList", "saving", "");

                    // CleanHtml already requires that we are on UI thread. But it's worth asserting here too in case that changes.
                    // If we weren't sure of that we would need locking for access to _tasksToDoAfterSaving and InProcessOfSaving,
                    // and would need to be careful about whether any delayed tasks needed to be on the UI thread.
                    if (_view.InvokeRequired)
                    {
                        NonFatalProblem.Report(
                            ModalIf.Beta,
                            PassiveIf.Beta,
                            "SaveNow called on wrong thread",
                            null
                        );
                        _view.Invoke((Action)(() => SaveNowInternal(forceFullSave)));
                        Logger.WriteMinorEvent(
                            "EditingModel.SaveNow() finished after Invoke to get on UI thread"
                        );
                        return;
                    }
                    InProcessOfSaving = true;
                    _tasksToDoAfterSaving.Clear();
                    var docFromBrowser = _view.GetCleanCurrentPageFromBrowser();
                    if (docFromBrowser == null)
                    {
                        // nothing to save; since _havePageToSave is true, this is probably a bug
                        Logger.WriteMinorEvent("EditingModel.SaveNow() found no page data to save");
                        return;
                    }

                    //BL-1064 (and several other reports) were about not being able to save a page. The problem appears to be that
                    //this old code:
                    //	CurrentBook.SavePage(_domForCurrentPage);
                    //would some times ask book X to save a page from book Y.
                    //We could never reproduce it at will, so this is to help with that...
                    if (this._pageSelection.CurrentSelection.Book != _currentlyDisplayedBook)
                    {
                        Debug.Fail("This is the BL-1064 Situation");
                        Logger.WriteEvent(
                            "Warning: SaveNow() with a page that is not the current book. That should be ok, but it is the BL-1064 situation (though we now work around it)."
                        );
                    }
                    //but meanwhile, the page knows its book, so we can see if it looks like a valid book and give a helpful
                    //error if, for example, it was deleted:
                    try
                    {
                        if (!_pageSelection.CurrentSelection.Book.IsSaveable)
                        {
                            Logger.WriteEvent(
                                "Error: SaveNow() found that this book had IsSaveable=='false'"
                            );
                            Logger.WriteEvent(
                                "Book path was {0}",
                                _pageSelection.CurrentSelection.Book.FolderPath
                            );
                            throw new ApplicationException(
                                "Bloom tried to save a page to a book that was not in a position to be updated."
                            );
                        }
                    }
                    catch (ObjectDisposedException err) // in case even calling CanUpdate gave an error
                    {
                        Logger.WriteEvent("Error: SaveNow() found that this book was disposed.");
                        throw err;
                    }
                    catch (Exception err) // in case even calling CanUpdate gave an error
                    {
                        Logger.WriteEvent("Error: SaveNow():CanUpdate threw an exception");
                        throw err;
                    }
                    //OK, looks safe, time to save.
                    var newPageData = GetPageData(docFromBrowser);
                    _pageSelection.CurrentSelection.Book.SavePage(
                        HtmlDom.FromDoc(docFromBrowser),
                        forceFullSave || NeedToDoFullSave(newPageData)
                    );
                    _pageHasUnsavedDataDerivedChange = false;
                    while (_tasksToDoAfterSaving.Count > 0)
                    {
                        var task = _tasksToDoAfterSaving[0];
                        _tasksToDoAfterSaving.RemoveAt(0);
                        task();
                    }
                    PageTemplatesApi.LastSaveTime = DateTime.Now;
                }
                finally
                {
                    InProcessOfSaving = false;
                }
#if MEMORYCHECK
                // Check memory for the benefit of developers.
                MemoryManagement.CheckMemory(false, "after EditingModel.SaveNow()", false);
#endif
                Logger.WriteMinorEvent("EditingModel.SaveNow() finished");
            }
            else
            {
                Logger.WriteMinorEvent(
                    $"EditingModel.SaveNow() called when InProcessOfSaving={_inProcessOfDeleting}, NavigatingSoSuspendSaving={NavigatingSoSuspendSaving}"
                );
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
            var newFeatureRequirements = BookStorage.GetRequiredVersionsString(
                CurrentBook.OurHtmlDom
            );
            return _pageHasUnsavedDataDerivedChange
                || !newPageData.SameAs(_pageDataBeforeEdits)
                || _featureRequirementsBeforeEdits != newFeatureRequirements;
        }

        internal void RequestDefaultTranslationGroupContent(ApiRequest request)
        {
            string translationGroupHtml = TranslationGroupManager.GetDefaultTranslationGroupContent(
                CurrentBook
            );
            request.ReplyWithHtml(translationGroupHtml);
        }

        public void ChangePicture(
            string imageId,
            UrlPathString priorImageSrc,
            PalasoImage imageInfo
        )
        {
            try
            {
                Logger.WriteMinorEvent("Starting ChangePicture {0}...", (object)imageInfo.FileName);

                // REVIEW: This does a "fire and forget" call to JS. It is followed by a SaveNow() call for the sake of the thumbnail.
                var args = PageEditingModel.ChangePicture(
                    CurrentBook.FolderPath,
                    imageId,
                    priorImageSrc,
                    imageInfo
                );
                // we don't need to wait. Even if our caller kicks off a save, its call to RunJavascriptAsync() will come in after ours.
                GetEditingBrowser()
                    .RunJavascriptFireAndForget(
                        $"editTabBundle.getEditablePageBundleExports().changeImage({JsonConvert.SerializeObject(args)})"
                    );

                /* Right now, when we're trying to remove saves and simplify everytihng, we are
                   leaving the thumbnail out-of-date until you change pages, just like we do when the user adds text.
                   Enhance: Eventually when saving is less disruptive, we will bring it back.
                              
                    SaveNow();
                    _view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
                */

                Logger.WriteMinorEvent("Finished ChangePicture {0}", (object)imageInfo.FileName);
                Analytics.Track("Change Picture");
                Logger.WriteEvent("ChangePicture {0}...", (object)imageInfo.FileName);
            }
            catch (Exception e)
            {
                var msg = LocalizationManager.GetString(
                    "Errors.ProblemImportingPicture",
                    "Bloom had a problem importing this picture."
                );
                e.Data["ProblemImagePath"] = imageInfo.OriginalFilePath;
                ErrorReport.NotifyUserOfProblem(e, msg + Environment.NewLine + e.Message);
            }
        }

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
                var pagesStartingWithCurrentSelection = CurrentBook
                    .GetPages()
                    .SkipWhile(p => p.Id != _pageSelection.CurrentSelection.Id);
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
                            CurrentBook.NameBestForUserDisplay,
                            CurrentBook.CheckForErrors(),
                            CurrentBook.RawDom.OuterXml,
                            new StackTrace().ToString()
                        )
                    );

                    ErrorReport.NotifyUserOfProblem(
                        exception,
                        "There was a problem looking through the pages of this book. If you can send emails, please click 'details' and send this report to the developers."
                    );
                    return null;
                }
                IPage lastGuyWHoCanHaveAnInsertionAfterHim = pages.Last(p => !p.IsBackMatter);
                // ReSharper restore PossibleMultipleEnumeration
                return lastGuyWHoCanHaveAnInsertionAfterHim;
            }
            return null;
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
            using (var dlg = new ProgressDialogForeground()) //REVIEW: this foreground dialog has known problems in other contexts... it was used here because of its ability to handle exceptions well. TODO: make the background one handle exceptions well
            {
                dlg.ShowAndDoWork(
                    progress => CurrentBook.CopyImageMetadataToWholeBookAndSave(metadata, progress)
                );
            }
        }

        public string GetFontAvailabilityMessage()
        {
            // REVIEW: does this ToLower() do the right thing on Linux, where filenames are case sensitive?
            var bookData = _bookSelection.CurrentSelection.BookData;
            var language1FontName = bookData.Language1.FontName;
            var name = language1FontName.ToLowerInvariant();

            if (null == FontFamily.Families.FirstOrDefault(f => f.Name.ToLowerInvariant() == name))
            {
                var serve = FontServe.GetInstance();
                if (serve.HasFamily(language1FontName))
                    return null;
                if (serve.HasFamily("Andika") && language1FontName == "Andika New Basic")
                    return null; // Andika subsumes Andika New Basic and is served for it
                var s = LocalizationManager.GetString(
                    "EditTab.FontMissing",
                    "The current selected "
                        + "font is '{0}', but it is not installed on this computer. Some other font will be used."
                );
                return String.Format(s, language1FontName);
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
            // We would like to save here, but that leaves the page in a bad state in case the user cancels.
            // Usually if we want to save but not go to another page, we we call SaveNow() and then RefreshDisplayOfCurrentPage().
            // If we do that here, ShowAddPageDialog() does not bring up the dialog. So we decided to just not save here.
            // If they actually add a page, we'll save then.
            // The worst consequence is that if they add a page in a template, and then Add Page again, the thumbnail might not
            // accurately reflect the new page.
            // Usually, relevant changes will have been saved when Change Layout was turned off.
            //SaveNow();
            _view.ShowAddPageDialog();
        }

        internal void ChangePageLayout(IPage page)
        {
            PageChangingLayout = page;
            SaveNow(); // need to preserve any typing they've done but not yet saved
            _view.ShowChangeLayoutDialog();
        }

        public void ChangeBookLicenseMetaData(Metadata metadata)
        {
            CurrentBook.SetMetadata(metadata);

            // This is awkward.
            // Originally, one could only open the CopyrightAndLicenseDialog from the Edit tab. Now, one can open it from the Publish tab.
            // I wanted to introduce an event or other mechanism such that Edit and Publish could each do what they need to when
            // the dialog is closed, but CopyrightAndLicenseApi is already so entangled with EditModel, it wasn't going to be clean
            // no matter what I did. And this is simpler.

            // For Edit tab:
            if (Visible)
            {
                _pageHasUnsavedDataDerivedChange = true;
                RefreshDisplayOfCurrentPage(); //the cleanup() that is part of Save removes qtips, so let's redraw everything
            }

            // Apparently, there are two sources of truth for the book's metadata: the BookInfo object, and the dom. Sigh.
            CurrentBook.BookInfo.Save(); // Save copyright/license in meta.json; believe it or not, this doesn't happen as part of Book.Save().
            CurrentBook.Save(); // Save copyright/license in the dom.

            // Used by the Publish tab to reload the UI when the data is saved.
            _webSocketServer.SendString(
                "bookCopyrightAndLicense",
                "saved",
                CurrentBook.BookInfo.Copyright
            );
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
            SaveNow(); // need to preserve any typing they've done but not yet saved (BL-4512)
            // We have to clone this so that if the user changes the page after doing the copy,
            // when they paste they get the page as it was, not as it is now.
            _pageDivFromCopyPage = (XmlElement)page.GetDivNodeForThisPage().CloneNode(true);
            _bookPathFromCopyPage = page.Book.GetPathHtmlFile();
        }

        public void CopyHyperlink(IPage page)
        {
            var id = page.GetDivNodeForThisPage().GetAttribute("id");
            var hyperlink = "#" + id;
            PortableClipboard.SetText(hyperlink);
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
                templateBook =
                    _sourceCollectionsList.FindAndCreateTemplateBookByFullPath(
                        _bookPathFromCopyPage
                    ) ?? templateBook;
            }
            var pageForPasting = new Page(
                templateBook,
                _pageDivFromCopyPage,
                "not used",
                "not used",
                x => _pageDivFromCopyPage
            );
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
            Book.Book.EnsureVideoPlaceholderFile(_bookSelection.CurrentSelection);
        }

        public void RequestWidgetPlaceHolder()
        {
            Book.Book.EnsureWidgetPlaceholderFile(_bookSelection.CurrentSelection);
        }

        // "Widgets" are HTML Activities that the user creates outside of Bloom, as distinct from our built-in activities.
        public UrlPathString AddWidgetFilesToBookFolder(string fullWidgetPath)
        {
            return WidgetHelper.AddWidgetFilesToBookFolder(CurrentBook.FolderPath, fullWidgetPath);
        }

        public void HandlePageDomLoadedEvent(object sender, EventArgs args)
        {
            NavigatingSoSuspendSaving = false;
        }

        // This speeds up developing brandings. It may speed up other things, but I haven't tested those.
        // Currently, branding.json changes won't be visible until you change pages (or click on the current page thumbnail)
        private void StartWatchingDeveloperChanges()
        {
            // This speeds up the process of tweaking branding files
            if (Debugger.IsAttached)
            {
                _developerFileWatcher = new FileSystemWatcher { IncludeSubdirectories = true, };
                _developerFileWatcher.Path =
                    FileLocationUtilities.GetDirectoryDistributedWithApplication(
                        BloomFileLocator.BrowserRoot
                    );
                // we don't want _developerFileWatcher to fire initially, onlye when there's a change
                _developerFileWatcher.NotifyFilter = NotifyFilters.LastWrite;

                var waitingForInitialLoad = true;
                _developerFileWatcher.Changed += async (sender, args) =>
                {
                    // oddly, there is no way to tell the file watcher that we don't want to consider the original state of the files as "changes"
                    // so we ignore events for the first 5 seconds
                    if (waitingForInitialLoad)
                    {
                        return;
                    }
                    _weHaveSeenAJsonChange |= args.Name.ToLowerInvariant().EndsWith(".json");
                    if (CurrentBook == null)
                        return;
                    // if we've been called already in the past 5 seconds, don't do it again
                    if (
                        DateTime.Now
                            .Subtract(_lastTimeWeReloadedBecauseOfDeveloperChange)
                            .TotalSeconds >= 2
                    )
                    {
                        // so it's been a long time, it's safe to do a reload as fast as possible, even if more  changes come in momentarily
                        doReload();
                        return;
                    }
                    else
                    {
                        // it's been too soon, let's reload after a bit of quiet. use a timer to call doReload()
                        // after a bit of quiet.
                        if (_developerFileWatcherQuietTimer == null)
                        {
                            _developerFileWatcherQuietTimer = new Timer();
                            _developerFileWatcherQuietTimer.Interval = 2000;
                            _developerFileWatcherQuietTimer.Tick += (sender, timerArgs) =>
                            {
                                _developerFileWatcherQuietTimer.Stop();
                                _developerFileWatcherQuietTimer = null;
                                doReload();
                            };
                            _developerFileWatcherQuietTimer.Start();
                        }
                        else
                        {
                            // we're already waiting for a quiet period, so just keep waiting. We've updated _weHaveSeenAJsonChange as needed with this latest change.
                        }
                    }
                };
                _developerFileWatcher.EnableRaisingEvents = true;
                Task.Delay(5000)
                    .ContinueWith(_ =>
                    {
                        waitingForInitialLoad = false;
                    });
            }
        }

        private void doReload()
        {
            _lastTimeWeReloadedBecauseOfDeveloperChange = DateTime.Now;

            // About this doing one thing for json and another for css; at the moment, I can't only
            // figure out how to do EITHER a BringBookUpToDate (make use of new json presets from branding)
            // OR actually refresh the page (make use of new css).
            //
            // Enhance: I suspect all the problems here are related to us changing the page id's each time we load, which I don't understand.
            // It may just be a mistake.
            if (_weHaveSeenAJsonChange)
            {
                var pageIndex = _pageSelection.CurrentSelection.GetIndex();
                CurrentBook.BringBookUpToDate(new NullProgress());
                _view.Invoke(
                    (MethodInvoker)(
                        () =>
                        {
                            // Because BringBookUpToDate will have changed page id's, we need to rebuild the page
                            // list else the next time you click on one, that page won't be found.
                            _view.UpdatePageList(true);
                            // And also, when you click on another page, if we try to save the current page, it won't be found.
                            _skipNextSaveBecauseDeveloperIsTweakingSupportingFiles = true;
                            _view.Refresh();

                            _pageSelection.SelectPage(
                                _currentlyDisplayedBook.GetPageByIndex(pageIndex),
                                true /* don't do anything else */
                            );
                        }
                    )
                );
            }
            else // css, png, svg, js, etc.
            {
                CurrentBook.Storage.UpdateSupportFiles();
                if (!_view.IsDisposed && _view.IsHandleCreated)
                {
                    _view.Invoke(
                        (MethodInvoker)
                            delegate
                            {
                                RefreshDisplayOfCurrentPage(false);
                            }
                    );
                }
            }
            _weHaveSeenAJsonChange = false;
        }
    }
}
