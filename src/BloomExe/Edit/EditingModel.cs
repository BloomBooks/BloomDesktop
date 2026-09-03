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
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ErrorReporter;
using Bloom.FontProcessing;
using Bloom.MiscUI;
using Bloom.SafeXml;
using Bloom.ToPalaso.Experimental;
using Bloom.Utils;
using Bloom.web;
using Bloom.web.controllers;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Code;
using SIL.IO;
using SIL.Progress;
using SIL.Reporting;
using SIL.Windows.Forms.ClearShare;
using SIL.Windows.Forms.ImageToolbox;
using SIL.Windows.Forms.Miscellaneous;

namespace Bloom.Edit
{
    public class EditingModel
    {
        private readonly BookSelection _bookSelection;
        private readonly PageSelection _pageSelection;
        private readonly CollectionSettings _collectionSettings;
        private readonly ITemplateFinder _sourceCollectionsList;
        private bool _havePageToSave;

        // Set by ReloadCurrentBookDiscardingEdits when an external tool has overwritten the current
        // book on disk while the Edit tab is live. It tells the leaving-Edit-tab logic in
        // OnTabAboutToChange to reload the book from disk instead of saving, so the user's unsaved
        // page is discarded in favor of the new on-disk content rather than clobbering it.
        private bool _reloadFromDiskOnLeavingEditTab;

        /// <summary>
        /// What the browser last told us the edited page contains, volunteered rather than asked
        /// for. See PageSnapshot: this is what lets a save take the current page synchronously
        /// instead of asking the browser and waiting.
        /// </summary>
        private readonly PageSnapshot _pageSnapshot = new PageSnapshot();

        public bool Visible;
        private Book.Book _currentlyDisplayedBook;
        private Book.Book _bookForToolboxContent;
        private EditingView _view;
        private List<ContentLanguage> _contentLanguages;
        private IPage _previouslySelectedPage;
        private BloomServer _server;
        private readonly BloomWebSocketServer _webSocketServer;
        internal IPage PageChangingLayout; // used to save the page on which the choose different layout command was invoked while the dialog is active.

        // This event fires after the EditingModel has finished responding to a PageSelection change.
        internal event EventHandler PageSelectModelChangesComplete;

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

        // these 2 are used as part of automatically re-rerendering a page when a developer changes something in the supporting files
        private FileSystemWatcher _developerFileWatcher;
        private DateTime _lastTimeWeReloadedBecauseOfDeveloperChange;

        //public event EventHandler UpdatePageList;

        public delegate EditingModel Factory(); //autofac uses this

        private EditingStateMachine _stateMachine;

        public EditingStateMachine StateMachine => _stateMachine;

        public EditingModel(
            BookSelection bookSelection,
            PageSelection pageSelection,
            TemplateInsertionCommand templateInsertionCommand,
            PageListChangedEvent pageListChangedEvent,
            RelocatePageEvent relocatePageEvent,
            BookRefreshEvent bookRefreshEvent,
            PageRefreshEvent pageRefreshEvent,
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
            _collectionSettings = collectionSettings;
            _server = server;
            _webSocketServer = webSocketServer;
            _sourceCollectionsList = sourceCollectionsList;

            _stateMachine = new EditingStateMachine(
                // navigate,
                (string pageId) =>
                {
                    StartNavigationToEditPage(CurrentBook.GetPage(pageId));
                },
                // updateBookWithPageContent
                (string pageId, string pageContentData) =>
                    UpdateBookDomFromBrowserPageContent(pageContentData),
                // saveBook
                SaveBookToDisk,
                // hidePage
                () =>
                {
                    if (_view != null)
                    {
                        _view.OnHideEditTab();
                    }
                }
            );

            bookSelection.SelectionChanged += OnBookSelectionChanged;
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
                            SavePageAndReloadIt();
                            break;

                        case PageRefreshEvent.SaveBehavior.SaveBeforeRefreshFullSave:
                            SavePageAndReloadIt(true);
                            break;

                        case PageRefreshEvent.SaveBehavior.JustRedisplay:
                            RefreshDisplayOfCurrentPage();
                            break;
                    }
                }
            );

            selectedTabChangedEvent.Subscribe(OnTabChanged);
            selectedTabAboutToChangeEvent.Subscribe(OnTabAboutToChange);
            pageListChangedEvent.Subscribe(needFullUpdate => _view.UpdatePageList(needFullUpdate));
            relocatePageEvent.Subscribe(OnRelocatePage);
            collectionClosingEvent.Subscribe(args =>
            {
                if (Visible)
                {
                    // Synchronous: the browser has already given us the page, so shutting down has
                    // nothing to wait for. See SaveEverythingBeforeClosing, which is also where the
                    // reasons this used to be asynchronous are written down.
                    SaveEverythingBeforeClosing();
                }
            });
            localizationChangedEvent.Subscribe(o =>
            {
                if (_view != null)
                {
                    _view.NextReloadChangesUiLanguage();
                }

                //this is visible was added for https://jira.sil.org/browse/BL-267, where the edit tab has never been
                //shown so the view has never been full constructed, so we're not in a good state to do a refresh
                if (Visible)
                {
                    MergeCurrentPageThenSave(() =>
                    {
                        _view.UpdatePageList(false);
                        return _pageSelection.CurrentSelection.Id;
                    });
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

        /// <summary>
        /// Receives a string (which comes from the browser) that combines the body of the document of the page
        /// being edited with the CSS that defines the user-defined styles. It updates the current book DOM
        /// to match whatever the browser has.
        ///
        /// The browser now does watch the page with a MutationObserver (see pageSnapshot.ts), so a null
        /// here already means "the user has changed nothing on this page". But that is only ever an
        /// optimisation, because the browser cannot know what our own processing will make of what it
        /// sends -- it would have to predict ProcessPageAfterEditing. Whether the book really changed is
        /// decided here, by Book.UpdateDomFromEditedPage's anythingChanged. See BL-13502.
        /// </summary>
        public void UpdateBookDomFromBrowserPageContent(string pageContentData)
        {
            if (pageContentData != null)
            {
                var endHtml = pageContentData.IndexOf("<SPLIT-DATA>", StringComparison.Ordinal);
                if (endHtml > 0)
                {
                    var bodyHtml = pageContentData.Substring(0, endHtml);
                    var userCssContent = pageContentData.Substring(endHtml + "<SPLIT-DATA>".Length);
                    var docFromBrowser = GetCleanCurrentPageFromBodyAndCss(
                        bodyHtml,
                        userCssContent
                    );
                    UpdateBookDomFromBrowserPageContent(docFromBrowser);
                }
            }
        }

        /// <summary>
        /// Given the body of the editable page and the CSS for any user-defined styles (from the
        /// editable page browser), this method creates a new SafeXmlDocument that contains the same state.
        /// It does some additional cleanup of things that get added to the page as UI controls
        /// to support editing. (Enhance: it would be nice if ALL the cleanup happened in one place,
        /// probably the Javascript method that retrieves the page content).
        /// (Nicer still if cleanup didn't leave the page in an invalid state, see BL-13502.)
        /// </summary>
        internal static SafeXmlDocument GetCleanCurrentPageFromBodyAndCss(
            string bodyHtml,
            string userCssContent
        )
        {
            // If anything goes badly wrong here, we want to throw rather then just bringing up a dialog.
            // The process of saving the page content to the DOM should either succeed or throw, so that
            // we don't get stuck in an invalid state that locks up the UI.

            if (string.IsNullOrEmpty(bodyHtml))
                throw new ApplicationException("Got an empty body while trying to save page");

            SafeXmlDocument dom;

            var htmlDoc = XmlHtmlConverter.CreateHtmlString(bodyHtml);
            dom = XmlHtmlConverter.GetXmlDomFromHtml(htmlDoc, false);
            var bodyDom = dom.SelectSingleNode("//body");

            var browserDomPage = bodyDom.SelectSingleNode(
                "//body//div[contains(@class,'bloom-page')]"
            );
            if (browserDomPage == null)
                throw new ApplicationException(
                    "Got a null browserDomPage while trying to save page"
                ); //why? but I've seen it happen

            // We've seen pages get emptied out, and we don't know why. This is a safety check.
            // See BL-13078, BL-13120, BL-13123, and BL-13143 for examples.
            if (BookStorage.CheckForEmptyMarginBoxOnPage(browserDomPage as SafeXmlElement))
            {
                //We don't want to save the empty page.
                // This has been logged and reported to the user; we would prefer not to report it again, but we need the exception
                // handling inside the state machine to run so we can maintain a valid state, so we throw anyway.
                // Enhance: make that reporter not report again when we know we have already reported.
                throw new ApplicationException("Check for valid margin box failed");
            }

            SaveCustomizedCssRules(dom, userCssContent);
            return dom;
        }

        /// <summary>
        /// Given the combined "body &lt;SPLIT-DATA&gt; userCss" string that the editable-page bundle
        /// produces (see captureContentForExternalProcessing / requestPageContent in bloomEditing.ts),
        /// build the edited-page HtmlDom ready to hand to Book.SavePage / Book.UpdateDomFromEditedPage.
        /// This is the same parsing the live editor does in UpdateBookDomFromBrowserPageContent(string),
        /// factored out so the off-screen book processor (external/process-book) can reuse it without
        /// going through the live EditingModel/state machine.
        /// </summary>
        public static HtmlDom GetEditedPageDomFromBrowserContent(string pageContentData)
        {
            if (pageContentData == null)
                throw new ApplicationException("page content was null");
            var endHtml = pageContentData.IndexOf("<SPLIT-DATA>", StringComparison.Ordinal);
            if (endHtml < 0)
                throw new ApplicationException(
                    "page content was missing the <SPLIT-DATA> delimiter"
                );
            var bodyHtml = pageContentData.Substring(0, endHtml);
            var userCssContent = pageContentData.Substring(endHtml + "<SPLIT-DATA>".Length);
            return new HtmlDom(GetCleanCurrentPageFromBodyAndCss(bodyHtml, userCssContent));
        }

        private static void SaveCustomizedCssRules(SafeXmlDocument dom, string userCssContent)
        {
            // Yes, this wipes out everything else in the head. At this point, the only things
            // we need in _pageEditDom are the user defined style sheet and the bloom-page element in the body.
            dom.GetElementsByTagName("head")[0].InnerXml = HtmlDom.CreateUserModifiedStyles(
                userCssContent
            );
        }

        private Form _oldActiveForm;
        private SafeXmlElement _pageDivFromCopyPage;
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
            if (details.FromTab == Workspace.WorkspaceTab.edit)
            {
                // Leaving the tab means no page will load to run whatever was queued for the next
                // page load (see RunAfterNextPageLoad) - and it was queued for the page we are
                // leaving, so it must not spring to life if the user comes back to that page later.
                _doAfterNextPageLoad = null;

                // When an external tool has overwritten the current book on disk (see
                // ReloadCurrentBookDiscardingEdits), we are leaving the Edit tab specifically to
                // discard the unsaved page. In that case reload from disk instead of saving, so the
                // editor's normal save-on-leave doesn't clobber what the external tool just wrote.
                var reloadFromDiskInsteadOfSaving = _reloadFromDiskOnLeavingEditTab;
                _reloadFromDiskOnLeavingEditTab = false;

                // All of this is synchronous now. It used to be a save-then-do-this with two branches -- one
                // for the save it started, one for "we were in the wrong state to save" -- because
                // the save had to ask the browser for the page and wait. The browser volunteers the
                // page as it is edited (see PageSnapshot), so we simply write it and go.
                if (reloadFromDiskInsteadOfSaving)
                {
                    // Discard the page the user was editing; what the external process wrote wins.
                    CurrentBook?.ReloadFromDisk(null);
                    // Force OnBecomeVisible to re-display from the freshly-loaded book if the user
                    // returns to the Edit tab.
                    _currentlyDisplayedBook = null;
                }
                else
                {
                    SaveCurrentPageAndBook();
                }

                // Show nothing in the editor. We have just saved, so there is nothing to lose.
                _stateMachine.ToNoPageHavingSaved();

                // This bizarre behavior prevents BL-2313 and related problems.
                // For some reason I cannot discover, switching tabs when focus is in the Browser window
                // causes Bloom to get deactivated, which prevents various controls from working.
                // Moreover, it seems (BL-2329) that if the user types Alt-F4 while whatever-it-is is active,
                // things get into a very bad state indeed. So arrange to re-activate ourselves as soon as the dust settles.
                _oldActiveForm = Form.ActiveForm;
                Application.Idle += ReactivateFormOnIdle;
                details.CompleteTheChange?.Invoke();
            }
            else
            {
                // If the old tab is not Edit, we don't need to save anything, so just do the postponed work.
                details.CompleteTheChange?.Invoke();
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
            Visible = details.ToTab == Workspace.WorkspaceTab.edit;
            // If an "Update Book" per-page pass is somehow still active as we leave the Edit tab
            // (e.g. the chain stalled, or the user navigated away mid-pass), abandon it so its
            // leftover state can't drive later page loads. In the normal case FinishUpdatingAllPages()
            // has already cleared this before it switches back to the Collection tab, so this is just
            // a safety net.
            if (!Visible)
                _updatingAllPages = false;
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

        internal void OnDuplicatePage(string pageContentFromBrowser = null)
        {
            DuplicatePage(_pageSelection.CurrentSelection, pageContentFromBrowser);
        }

        internal void DuplicateManyPages(IPage page)
        {
            using (var dlg = new ReactDialog("duplicateManyDlgBundle"))
            {
                dlg.SetScaledSize(400, 235);
                // This dialog is neater without a task bar. We don't need to be able to
                // drag it around. There's nothing left to give it one if we don't set a title
                // and remove the control box.
                dlg.ControlBox = false;
                dlg.ShowDialog();
            }
        }

        internal void DuplicatePage(IPage page, string pageContentFromBrowser = null)
        {
            DuplicatePageInternal(page, 1, pageContentFromBrowser);
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

        private void DuplicatePageInternal(
            IPage page,
            int numberOfTimesToDuplicate = 1,
            string pageContentFromBrowser = null
        )
        {
            // NB: though there is an api call to do this, it isn't currently used, so we have to measure here.
            var countString = numberOfTimesToDuplicate.ToString();
            var newPageId = page.Id; // error fallback
            MergeCurrentPageThenSave(
                () =>
                {
                    using (PerformanceMeasurement.Global.Measure("Duplicate page"))
                    {
                        try
                        {
                            newPageId = _currentlyDisplayedBook.DuplicatePage(
                                page,
                                numberOfTimesToDuplicate
                            );
                            // Book.DuplicatePage() updates the page list so we don't need to do it here.
                            // (See http://issues.bloomlibrary.org/youtrack/issue/BL-3715.)
                            //_view.UpdatePageList(false);
                            Logger.WriteEvent(
                                "Duplicate Page"
                                    + (
                                        numberOfTimesToDuplicate > 0
                                            ? " " + countString + " times"
                                            : ""
                                    )
                            );
                            BloomAnalytics.Track("Duplicate Page");
                        }
                        catch (Exception error)
                        {
                            ErrorReport.NotifyUserOfProblem(
                                error,
                                "Could not duplicate that page. Try quiting Bloom, run it again, and then attempt to duplicate the page again. And please click 'details' below and report this to us."
                            );
                        }
                    }
                    return newPageId;
                },
                forceFullSave: true,
                pageContentFromBrowser: pageContentFromBrowser
            );
        }

        internal void OnDeletePage(string pageContentFromBrowser = null)
        {
            DeletePage(_pageSelection.CurrentSelection, pageContentFromBrowser);
        }

        internal void DeletePage(IPage page, string pageContentFromBrowser = null)
        {
            // This can only be called on the UI thread in response to a user button click.
            Debug.Assert(!_view.InvokeRequired);
            // There used to be a guard here against a save still being in progress (BL-431). A save
            // now finishes inside the call that asks for it, so there is no such window.
            MergeCurrentPageThenSave(
                () =>
                {
                    try
                    {
                        var pageToShowNext = GetPageToShowAfterDeletion(page);
                        _currentlyDisplayedBook.DeletePage(page);
                        //_view.UpdatePageList(false);  DeletePage calls this via pageListChangedEvent.  See BL-3632 for trouble this causes.
                        Logger.WriteEvent("Delete Page");
                        BloomAnalytics.Track("Delete Page");
                        return pageToShowNext.Id;
                    }
                    catch (Exception error)
                    {
                        ErrorReport.NotifyUserOfProblem(
                            error,
                            "Could not delete that page. Try quiting Bloom, run it again, and then attempt to delete the page again. And please click 'details' below and report this to us."
                        );
                        return page.Id; // stay on this page.
                    }
                },
                forceFullSave: true,
                pageContentFromBrowser: pageContentFromBrowser
            );
        }

        private IPage GetPageToShowAfterDeletion(IPage page)
        {
            var pages = CurrentBook.GetPages().ToList();
            // pages.IndexOf(page) can fail here when a new page is removed after being relocated.
            // Apparently the page object is modified when it is relocated, but the page chooser's
            // copy of the page object is not updated.  IndexOf requires matching actual objects,
            // not just objects with the same Id.  So we find the page index by Id.  See BL-14133.
            var index = pages.FindIndex(p => p.Id == page.Id);
            Guard.Against(index < 0, "Couldn't find page in cache");

            if (index == pages.Count - 1) //if it's the last page
            {
                if (index < 1) //if it's the only page
                    throw new ApplicationException(
                        "Bloom should not have allowed you to delete the last remaining page."
                    );
                return pages[index - 1]; //give the preceding page
            }

            return pages[index + 1]; //give the following page
        }

        private void OnRelocatePage(RelocatePageInfo info)
        {
            info.Cancel = !CurrentBook.RelocatePage(info.Page, info.IndexOfPageAfterMove);
            if (!info.Cancel)
            {
                // Moving a page actually changes its html to have the new left/right side and page number,
                // The Book takes care of that, but now we need to actually reload it from the dom.
                RefreshDisplayOfCurrentPage();
                _view.UpdatePageList(false);

                BloomAnalytics.Track("Relocate Page");
                Logger.WriteEvent("Relocate Page");
            }
        }

        /// <summary>
        /// The event handler form of InsertPage, for the AddPageDialog's InsertPage event. The
        /// dialog is a separate window, so it has no way to hand us the current page's content;
        /// "paste page" calls InsertPage directly and can.
        /// </summary>
        private void OnInsertPage(object page, PageInsertEventArgs e)
        {
            InsertPage(page, e, null);
        }

        /// <summary>
        /// This is used both to insert pages from the AddPageDialog, and also "paste page"
        /// </summary>
        private void InsertPage(object page, PageInsertEventArgs e, string pageContentFromBrowser)
        {
            MergeCurrentPageThenSave(
                () =>
                { // there might be unsaved changes in the current page from before we clicked Add Page
                    var newPageId = CurrentBook.InsertPageAfter(
                        DeterminePageWhichWouldPrecedeNextInsertion(),
                        page as Page,
                        e.NumberToAdd
                    );
                    // We deliberately do NOT force the page-list iframe to reload here.
                    // InsertPageAfter raises pageListChangedEvent (deferred until idle), which
                    // leads to UpdatePageList(); that either sends pageListNeedsRefresh over the
                    // websocket (a cheap, incremental update in the React page list) or, if the
                    // new page brought new stylesheets, regenerates the page-list document and
                    // navigates the iframe to it. We used to also do a hard location.reload of
                    // the iframe here, but that repainted the entire thumbnail list on every
                    // insert and raced with those deferred notifications: a websocket message
                    // arriving while the iframe was mid-reload was silently dropped, leaving the
                    // list permanently stale.
                    //
                    // The stylesheet-change path still navigates the iframe, which does repaint
                    // the whole list and does have a brief window during load where websocket
                    // messages are ignored. The difference is that this navigation is no longer a
                    // blind reload racing a separate notification: it is triggered by the deferred
                    // event itself and loads a freshly regenerated document that already contains
                    // the new page (and its stylesheet), so it is correct on its own. And when the
                    // reloaded iframe's socket opens, the React code re-fetches the page list (see
                    // the websocket/open handler in pageThumbnailList.tsx), recovering anything
                    // missed during the load. So no stale-list race remains.
                    //_view.UpdatePageList(false);  InsertPageAfter calls this via pageListChangedEvent.  See BL-3632 for trouble this causes.
                    //_pageSelection.SelectPage(newPage);
                    if (e.FromTemplate)
                    {
                        try
                        {
                            BloomAnalytics.Track(
                                "Insert Template Page",
                                new Dictionary<string, string>
                                {
                                    { "template-source", (page as IPage).Book.Title },
                                    { "page", (page as IPage).Caption },
                                }
                            );
                        }
                        catch (Exception) { }
                    }
                    Logger.WriteEvent("InsertTemplatePage");
                    return newPageId;
                },
                forceFullSave: true,
                pageContentFromBrowser: pageContentFromBrowser
            );
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
                var lang1 = _contentLanguages.FirstOrDefault(l =>
                    l.LangTag == _bookSelection.CurrentSelection.Language1Tag
                );
                // We must have one language selected. If nothing matches, select the first.
                if (lang1 == null)
                    lang1 = _contentLanguages[0];
                lang1.Selected = true;

                var lang2 = _contentLanguages.FirstOrDefault(l =>
                    l.LangTag == _bookSelection.CurrentSelection.Language2Tag
                );
                if (lang2 != null)
                    lang2.Selected = true;
                var lang3 = _contentLanguages.FirstOrDefault(l =>
                    l.LangTag == _bookSelection.CurrentSelection.Language3Tag
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
            MergeCurrentPageThenSave(() =>
            {
                var pageId = _pageSelection.CurrentSelection.Id;
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
                }
                CurrentBook.PrepareForEditing();
                _view.UpdatePageList(true); //counting on this to redo the thumbnails
                return pageId;
            });
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
            _nextSaveMustBeFull = true;
            MergeCurrentPageThenSave(() =>
            {
                CurrentBook.PrepareForEditing();
                _view.UpdatePageList(true); //counting on this to redo the thumbnails

                Logger.WriteEvent("ChangingContentLanguages");
                BloomAnalytics.Track("Change Content Languages");
                return _pageSelection.CurrentSelection.Id;
            });
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

            if (page != null)
                _view.GoToPage(page);
            if (_view != null)
            {
                _view.UpdatePageList(false);
            }
        }

        /// <summary>
        /// Reload the currently-selected book from disk, deliberately throwing away any unsaved edits
        /// to the page the user might be working on. This is used when an external process (e.g.
        /// BloomBridge) has just re-imported/overwritten the book on disk and we want the
        /// running Bloom to show the new version. The caller is responsible for making sure this really
        /// is the book that was changed; we only ever discard edits for the current selection.
        /// If the Edit tab is live, rather than risk reloading the book under the editor mid-edit, we
        /// kick the user back to the Collection tab (discarding edits and reloading from disk on the
        /// way out); the fresh book is shown if/when they return to the Edit tab.
        /// </summary>
        public void ReloadCurrentBookDiscardingEdits()
        {
            var book = CurrentBook;
            if (book == null)
                return;

            // Make sure we do NOT save the page the user might be editing; we are intentionally
            // discarding those edits in favor of what is now on disk. This is the same flag the
            // normal book-switch path clears to avoid saving the outgoing page (see OnBookSelectionChanged).
            _havePageToSave = false;

            if (!Visible)
            {
                // The Edit tab isn't showing, so the book isn't live in the browser and there's no
                // editing state to unwind. Just reload from disk; OnBecomeVisible will display the
                // fresh version when the user next switches to the Edit tab.
                book.ReloadFromDisk(null);
                _currentlyDisplayedBook = null;
                return;
            }

            // The Edit tab is showing and the page is live in the browser, very possibly mid-edit.
            // Trying to reload-and-renavigate the book in place while the editor is live proved
            // fragile (the state machine forbids a direct re-navigation, and unwinding it under the
            // user mid-edit could leave the editor in a bad state). The safe, predictable thing is to
            // kick the user back to the Collection tab. We set _reloadFromDiskOnLeavingEditTab so the
            // leaving-Edit-tab logic in OnTabAboutToChange reloads from disk instead of saving (which
            // would clobber the external tool's content). When the user returns to the Edit tab,
            // OnBecomeVisible will display the freshly-loaded book.
            _reloadFromDiskOnLeavingEditTab = true;
            _view.WorkspaceView.ChangeTab(Workspace.WorkspaceTab.collection);
        }

        /// <summary>
        /// The code invoked by the state machine to actually start the editable page browser navigating
        /// to a particular page. Anything that needs saving on the current page should already have been saved.
        /// </summary>
        void StartNavigationToEditPage(IPage page)
        {
            // The page we may have a snapshot of is going away, and whatever the save just wrote
            // into the book DOM is now the truth. Holding on to it would let a later visit to the
            // same page re-apply content from the previous visit. See PageSnapshot.Clear.
            _pageSnapshot.Clear();
            try
            {
                if (page == null)
                {
                    // bizarre, but in some error recovery situations the page we were on before the crash might
                    // no longer exist. In that case, just go to the first page.
                    page = CurrentBook.FirstPage;
                }

                _pageSelection.SelectPage(page);
                Logger.WriteMinorEvent("changing page selection");
                BloomAnalytics.Track("Select Page"); //not "edit page" because at the moment we don't have the capability of detecting that.

                // Trace memory usage in case it may be useful
                // First see if we seem to have a problem without taking time (~100ms in a large book/fast computer) to force GC.
                // If we seem to have a problem do it again forcing the GC and possibly warning the user.
                //if (MemoryManagement.CheckMemory(false, "switched page in edit", false, false))
                //    MemoryManagement.CheckMemory(false, "switched page in edit", true);

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

                    CurrentBook.ConvertPreOrigamiPages(page.GetDivNodeForThisPage());
                    if (Visible)
                        _view.StartNavigationToEditPage(page);

                    CheckForBL8852();

                    PageSelectModelChangesComplete?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception)
            {
                // It's very important that we succeed in navigating to SOME page; otherwise, we may well be left
                // in a state where the page UI isn't fully set up, and the state machine is in the SavedAndStripped
                // state, which will prevent saving any future changes. So if something went wrong here, see if
                // we can navigate to some other page. Arbitrarily, we'll try the first page, but only if that isn't
                // what we were already doing...that could lead to an infinite recursion. I can't think of anything
                // that feels useful to try if we can't navigate to the first page that is really in the current book.
                // (Conceivably we could try to report it, but we already have a navigation error we're about to throw,
                // and in that case it's presumably a report of something that went wrong while trying to navigate
                // to the first page.)
                // Review: in some ways it would be better to do this AFTER reporting the problem...but how can we reliably
                // detect that we're done handling the exception? It MIGHT not even end up being reported, depending
                // on what exception handlers may be up the stack.
                try
                {
                    var page1 = CurrentBook.GetPages().FirstOrDefault();
                    if (page1 != null && page1.Id != page?.Id)
                    {
                        // Not just a recursive call to StartNavigationToEditPage, though that will happen,
                        // because the state machine needs to know about the different page ID.
                        StateMachine.ToNavigating(page1.Id);
                    }
                }
                catch (Exception e2)
                {
                    // If we can't even navigate to the first page, we're in trouble. But better to throw the original error.
                    Logger.WriteEvent("Error navigating to page1: " + e2.Message);
                    // Try to ensure the user can at least try to recover by choosing another page.
                    // (This may not be sufficient, if the state machine is left in a state where we can't Save.
                    // With no way to know just what went wrong, I can't be sure this fall-back to the fall-back
                    // will work, but it may help in some cases.)
                    _view.SetModalState(false);
                }

                throw;
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

                for (int i = 0; i < nodeList.Length; ++i)
                {
                    var node = nodeList[i];

                    // GetOptionalStringAttribute needs this to be non-null, or else an exception will happen
                    if (node.AttributeNames == null)
                    {
                        continue;
                    }

                    if (HtmlDom.DoesNodeGetCopiedToDataDiv(node))
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

        /// <summary>
        /// Make what the editable page browser is showing match what's currently in the DOM.
        /// Assumes that anything that needs saving was saved before whatever changes
        /// made this reload necessary (or perhaps we just need to reload because saving
        /// currently strips out some UI stuff we need for editing).
        /// </summary>
        public void RefreshDisplayOfCurrentPage(bool changingUiLanguage = false)
        {
            _view.GoToPage(_pageSelection.CurrentSelection, changingUiLanguage);
        }

        /// <summary>
        /// XPath for the img on a page whose src is the given (URL-encoded) file name.
        /// </summary>
        /// <remarks>
        /// A src often carries a query string as well as the file name -- "?transparent=yes" from
        /// the transparency handling, "?thumbnail=1" from the page list, or the old cache-busting
        /// "?12345". Matching the src exactly therefore found nothing on exactly the pages that
        /// use those, and the caller's only response to finding nothing is to give up silently.
        /// So we accept either the bare name or the name followed by '?'. Requiring the '?' is
        /// what keeps this from also matching a different file that merely starts with the same
        /// characters ("cat.png" must not match "cat2.png"). (BL-16669)
        ///
        /// The name is safe to embed in the XPath string literal: it is URL-encoded, and
        /// UrlEncoded escapes an apostrophe as %27, so it cannot terminate the literal.
        /// </remarks>
        internal static string MakeImgWithSrcXPath(string urlEncodedFileName)
        {
            return $".//img[@src='{urlEncodedFileName}' or starts-with(@src, '{urlEncodedFileName}?')]";
        }

        public void UpdateMetaData(string url)
        {
            // url is a file name (EditingView._fileNameOfImageBeingModified), which we re-encode
            // here so it matches what is in the src attribute.
            var match = UrlPathString.CreateFromUnencodedString(url).UrlEncoded;
            var imgElt = _pageSelection
                .CurrentSelection.GetDivNodeForThisPage()
                .SafeSelectNodes(MakeImgWithSrcXPath(match))
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            if (imgElt == null)
                return; // log? unexpected
            ImageUpdater.UpdateImgMetadataAttributesToMatchImage(
                CurrentBook.FolderPath,
                imgElt,
                new NullProgress()
            );
            if (_nextSaveMustBeFull)
            {
                // We've changed the metadata on the current page, but a full save will
                // try to sync everything using the data-div, which has not yet been updated.
                // It comes before the page, so the out-of-date copy there will overwrite the
                // changes we just made. The simplest way to prevent this is to update the
                // data-div to match the current page before we do the full save.
                UpdateDataDivFromCurrentPage();
            }
            RefreshDisplayOfCurrentPage();
        }

        private void UpdateDataDivFromCurrentPage()
        {
            CurrentBook.BookData.SuckInDataFromEditedDom(
                _pageSelection.CurrentSelection.GetDivNodeForThisPage(),
                CurrentBook.BookInfo
            );
        }

        private DataSet _pageDataBeforeEdits;
        private string _featureRequirementsBeforeEdits;

        private DataSet GetPageData(SafeXmlNode page)
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
            var dom = GetEditPageIframeDom(book, page);
            var transparencyModifications = HtmlDom.AddTransparencyParamToImages(dom);
            try
            {
                return dom.getHtmlStringDisplayOnly();
            }
            finally
            {
                HtmlDom.RestoreImageSrcs(transparencyModifications);
            }
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
                .Cast<SafeXmlElement>()
                .FirstOrDefault();
            if (licenseBlock == null)
                return; // not the relevant page
            var metadata = book.GetLicenseMetadata();
            // BL-10360 says that we don't want this notice for CC0, even if metadata is not complete.
            // But that situation is not currently possible through our UI, and further thought
            // suggests we want to know who says it is CC0. So commenting that aspect out.
            var copyrightOk = metadata.IsMinimallyComplete; // || metadata.License?.Token == "cc0";
            var firstElementChild = licenseBlock
                .ChildNodes.Cast<SafeXmlNode>()
                .FirstOrDefault(x => x is SafeXmlElement);
            var haveMissingNotice =
                firstElementChild?.GetAttribute("class") == "ui-missingCopyrightNotice";
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
                    "javascript:(window.parent || window).workspaceBundle.showCopyrightAndLicenseDialog();"
                );
                licenseBlock.InsertBefore(div, licenseBlock.FirstChild);
            }
        }

        private static void InsertLabelAndLayoutTogglePane(HtmlDom dom)
        {
            // Add an empty div that will provide space for the page label and origami toggle above the displayed page.
            var node = dom.RawDom.CreateElement("div");
            node.SetAttribute("id", "labelAndLayoutPane");
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
                as SafeXmlElement;
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
                // If this changes, adjust similar code in the TS SetZoom method, currently in workspaceRoot.ts.
                outerDiv.SetAttribute(
                    "style",
                    String.Format(
                        "transform: scale({0}); transform-origin: left top; width: calc((100% - 5px) / {0})",
                        zoomString
                    )
                );
            }
        }

        static SafeXmlElement InsertContainingScalingDiv(
            SafeXmlElement body,
            SafeXmlElement pageDiv
        )
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
            var url = BloomServer.UrlForCurrentBookPageEncodedForIframeSrc(
                _bookSelection.CurrentSelection.FolderPath,
                _pageSelection.CurrentSelection.Id
            );
            BloomServer.SetCurrentEditPageUrlForDebugging(url);
            return url;
        }

        /// <summary>
        /// The returned url is to a simulated file that contains the page list HTML.  The file
        /// is created in memory and served by our local server, but it has a url that makes it
        /// seem to be in the book folder so local urls work.  The returned URL is HTTP encoded
        /// for use in an iframe src.
        /// </summary>
        internal string GetUrlForPageListFile()
        {
            var useViteDev = ReactControl.ShouldUseViteDev();
            var frame = BloomFileLocator.GetBrowserFile(
                false,
                "bookEdit",
                "pageThumbnailList",
                useViteDev ? "pageThumbnailList.vite-dev.html" : "pageThumbnailList.html"
            );
            var backColor = MiscUtils.ColorToHtmlCode(Palette.SidePanelBackgroundColor);
            var _baseHtml = RobustFile
                .ReadAllText(frame, Encoding.UTF8)
                .Replace("DarkGray", backColor);
            if (useViteDev)
                _baseHtml = ReactControl.ReplaceViteDevOrigin(_baseHtml);
            var pages = CurrentBook.GetPages().ToList();
            var sizeClass =
                pages.Count > 1
                    ? Book
                        .Layout.FromPage(pages[1].GetDivNodeForThisPage(), Book.Layout.A5Portrait)
                        .SizeAndOrientation.ClassName
                    : "A5Portrait";
            // Somehow, the React code needs to know the page size, mainly so it can put the right class on
            // the pageContainer element in pageThumbnail.tsx.
            // - It could get it by parsing the HTML page content, but that'movedPageIdAndNewIndex clumsy and also really too late:
            //   the pages are drawn empty before the page content is ever retrieved.
            // - we can't use the class on the page element because it is inside the pageContainer we need to affect
            // - we could put a sizeClass on the body or some other higher-level element, and rewrite the CSS
            //   rules to look for pageContainer INSIDE a certain page class. But this seems risky.
            //   Our expectation is that this class is applied to a page-level element. We don't want to
            //   accidentally invoke some rule that makes the whole preview pane A5Portrait-shaped.
            //   It also violates all our expectations, and forces us to do counter-intuitive things
            //   like making pageContainer a certain size if it is 'inside' something that is A5Portrait.
            // So, I ended up putting a data-pageSize attribute on the body element, and having the
            // code that initializes React look for it and pass pageSize to the root React element
            // as it should be, a property.
            var htmlText = _baseHtml.Replace(
                "data-pageSize=\"A5Portrait\"",
                $"data-pageSize=\"{sizeClass}\""
            );
            var pageListDom = new HtmlDom(XmlHtmlConverter.GetXmlDomFromHtml(htmlText));

            if (SIL.PlatformUtilities.Platform.IsLinux)
                OptimizeForLinux(pageListDom);

            pageListDom = CurrentBook.GetHtmlDomForPageList(pageListDom);
            var url = _view.Browser.CreateSimulatedFile(
                pageListDom,
                false,
                InMemoryHtmlFileSource.Pagelist
            );
            // PossiblyEncoded because CreateSimulatedFile returns a localhost url whose path
            // components are already escaped; see the note on CreateFromPossiblyEncodedString.
            var urlPath = UrlPathString.CreateFromPossiblyEncodedString(url);
            var encodedUrl = urlPath.UrlEncodedForHttpPath;
            BloomServer.SetCurrentPageListUrlForDebugging(encodedUrl);
            return encodedUrl;
        }

        private static void OptimizeForLinux(HtmlDom pageListDom)
        {
            // BL-987: Add styles to optimize performance on Linux
            var style = pageListDom.RawDom.CreateElement("style");
            style.InnerXml =
                "img { image-rendering: optimizeSpeed; image-rendering: crisp-edges; }";
            pageListDom.RawDom.GetElementsByTagName("head")[0].AppendChild(style);
        }

        internal HtmlDom GetXmlDocumentForEditScreenWebPage(string pageUrl, string pageListUrl)
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                Path.Combine(
                    BloomFileLocator.BrowserRoot,
                    "bookEdit",
                    ReactControl.ShouldUseViteDev()
                        ? "WorkspaceRoot.vite-dev.html"
                        : "WorkspaceRoot.html"
                )
            );
            // {simulatedPageFileInBookFolder} is placed in the template file where we want the source file for the 'page' iframe.
            // We don't really make a file for the page, the contents are just saved in our local server.
            // But we give it a url that makes it seem to be in the book folder so local urls work.
            // See BloomServer.MakeInMemoryHtmlFileInBookFolder() for more details.
            var frameText = RobustFile
                .ReadAllText(path, Encoding.UTF8)
                .Replace("{simulatedPageFileInBookFolder}", pageUrl)
                .Replace("{simulatedPageListFile}", pageListUrl);
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
        /// Return the top-level document that should be displayed in the browser for the current page.
        /// </summary>
        /// <returns></returns>
        public HtmlDom GetXmlDocumentForEditScreenWebPage()
        {
            var path = FileLocationUtilities.GetFileDistributedWithApplication(
                Path.Combine(
                    BloomFileLocator.BrowserRoot,
                    "bookEdit",
                    ReactControl.ShouldUseViteDev()
                        ? "WorkspaceRoot.vite-dev.html"
                        : "WorkspaceRoot.html"
                )
            );
            // {simulatedPageFileInBookFolder} is placed in the template file where we want the source file for the 'page' iframe.
            // We don't really make a file for the page, the contents are just saved in our local server.
            // But we give it a url that makes it seem to be in the book folder so local urls work.
            // See BloomServer.MakeInMemoryHtmlFileInBookFolder() for more details.
            var frameText = RobustFile
                .ReadAllText(path, Encoding.UTF8)
                .Replace("{simulatedPageFileInBookFolder}", GetUrlForCurrentPage())
                .Replace("{simulatedPageListFile}", GetUrlForPageListFile());
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
            var currentLevel = _currentlyDisplayedBook.OurHtmlDom.Body.GetAttribute(
                "data-leveledreaderlevel"
            );
            var correctLevel =
                _currentlyDisplayedBook.BookInfo.MetaData.LeveledReaderLevel.ToString();
            if (correctLevel != currentLevel)
            {
                MergeCurrentPageThenSave(() =>
                {
                    _currentlyDisplayedBook.OurHtmlDom.Body.SetAttribute(
                        "data-leveledreaderlevel",
                        correctLevel
                    );
                    return _pageSelection.CurrentSelection.Id;
                });
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
        /// <summary>
        /// Save all the changes to the current page, then reload it.
        ///
        /// The reload used to be needed just to restore the UI markup the Save stripped out. That
        /// is no longer true (BL-13502), but it is still doing a second job for these callers, and
        /// that is why it stays: the page has to be rebuilt from the book DOM either because C#
        /// just changed it (a new topic in the data div, new book settings) or because the browser
        /// created elements that have never been through SetupElements (a new origami layout, an
        /// imported video, a translation group replaced by a derived field).
        ///
        /// pageContentFromBrowser, when the caller was able to send it, removes the round trip:
        /// we save and navigate in one step rather than asking the browser for the content and
        /// waiting for it on another API. Callers that have no browser request to carry it (the
        /// PageRefreshEvent handlers) leave it null and get the old path.
        /// </summary>
        internal void SavePageAndReloadIt(
            bool forceFullSave = false,
            string pageContentFromBrowser = null
        )
        {
            if (CannotSavePage())
                return;
            _nextSaveMustBeFull |= forceFullSave;
            MergeCurrentPageThenSave(
                () => _pageSelection.CurrentSelection.Id,
                pageContentFromBrowser: pageContentFromBrowser
            );
        }

        //invoked from TopicChooserDialog.tsx via API
        internal void SetTopic(string englishTopicAsKey)
        {
            //make the change in the data div
            _currentlyDisplayedBook.SetTopic(englishTopicAsKey);
            _pageHasUnsavedDataDerivedChange = true;
            //reflect that change on this page
            SavePageAndReloadIt();
        }

        internal void SavePageAndReloadIt(ApiRequest request)
        {
            // The browser sends the current page's content with this request when it can; see
            // saveChangesAndRethinkPage() in bloomEditing.ts.
            SavePageAndReloadIt(pageContentFromBrowser: request.GetPageContentFromBrowserOrNull());
            request.PostSucceeded();
        }

        private bool CannotSavePage()
        {
            return _bookSelection == null
                || CurrentBook == null
                || _pageSelection.CurrentSelection == null
                || _currentlyDisplayedBook == null;
        }

        // We set this true for the interval between starting to navigate to a new
        // page and when it is loaded. This prevents trying to save when things are in an unstable state
        // (e.g., BL-2634, BL-6296). It may also prevent some wasted Saves and thus improve performance.
        public bool NavigatingSoSuspendSaving => _stateMachine.Navigating;
        private System.Windows.Forms.Timer _developerFileWatcherQuietTimer;
        private bool _weHaveSeenAJsonChange;

        private bool _nextSaveMustBeFull; // review: store in state machine?

        /// <summary>
        /// Fold the current page's edits into the book, let the caller change the book, then write
        /// it once and go to the page the caller names. All synchronous.
        ///
        /// The three steps happen in that order, and the order is the point:
        ///
        ///   1. the page the user was editing is merged into the book DOM, so that
        ///   2. changeBookBeforeWriting sees those edits (duplicating a page has to copy what the
        ///      user just typed, not what was on disk), and it returns the id of the page to show
        ///      next -- or null to leave the editor blank, which is how leaving the Edit tab works;
        ///   3. the book is written to disk ONCE, covering both the merge and the change, and we
        ///      navigate to that page.
        ///
        /// That middle slot is why this takes an action rather than simply returning: the caller's
        /// work belongs between the merge and the write, not after the save.
        ///
        /// This was called SaveThen, from when it meant "ask the browser for the page, and when it
        /// eventually answers, do this". The browser now volunteers the page as it is edited (see
        /// PageSnapshot), so there is nothing to wait for and nothing happens "then".
        /// </summary>
        /// <param name="changeBookBeforeWriting">Runs between the merge and the write. Returns the
        /// page to show next, or null for a blank editor.</param>
        /// <param name="ifNotInAStateToSave">Called INSTEAD of everything above when we could not
        /// start at all -- the user may have begun changing pages, or this may be a nested request
        /// arriving from inside another one's changeBookBeforeWriting. Most callers have nothing
        /// useful to do then and omit it; the ones that do are finishing something they had already
        /// started, like clearing a dialog's spinner.</param>
        /// <param name="pageContentFromBrowser">The current page's content, when the request that
        /// got us here brought it along (see getPageContentForSaveWhenReady() in the browser).
        /// Otherwise we use whatever the browser last volunteered; a null snapshot is a positive
        /// statement that the page has not changed since it loaded, not "we do not know".</param>
        public void MergeCurrentPageThenSave(
            Func<string> changeBookBeforeWriting,
            Action ifNotInAStateToSave = null,
            bool forceFullSave = false,
            string pageContentFromBrowser = null
        )
        {
            var outcome = SavePageInPlaceThen(
                pageContentFromBrowser ?? CurrentPageSnapshotOrNull,
                changeBookBeforeWriting,
                forceFullSave
            );
            // Declined is the only outcome where nothing at all happened -- changeBookBeforeWriting
            // has NOT run -- so it is the only one where the caller's fallback is the right
            // response. Failed means the action may already have run (running it again would
            // duplicate or delete a second page), and Refused means an external process has
            // replaced the book and this page must not be written at all. See InPlaceSaveOutcome.
            if (outcome == InPlaceSaveOutcome.Declined)
                ifNotInAStateToSave?.Invoke();
        }

        /// <summary>
        /// Called by the editView/pageSnapshot API when the browser's idle task volunteers the
        /// current content of the page. All we do is remember it; see PageSnapshot for why.
        /// </summary>
        public void ReceivePageSnapshot(string pageId, string pageContentData)
        {
            _pageSnapshot.Set(pageId, pageContentData);
        }

        /// <summary>
        /// The current page's content as the browser last reported it, or null if the page has not
        /// been changed since it loaded.
        ///
        /// Null genuinely means "nothing to save" rather than "ask the browser": the editing page
        /// posts a snapshot after any change that settles, so a page with no snapshot has had no
        /// change to record. That is what lets a caller which used to start an asynchronous save
        /// just take the content and get on with it.
        /// </summary>
        public string CurrentPageSnapshotOrNull =>
            _pageSnapshot.GetFor(_pageSelection?.CurrentSelection?.Id);

        /// <summary>
        /// Get the edited page and the book onto disk, synchronously, on the way out of the
        /// program or the collection.
        ///
        /// This exists so that shutting down does not have to wait for anything. It used to: the
        /// save had to ask the browser for the page and wait for the answer on another API call,
        /// so the collection-closing event grew a whole "I'll finish this later, you carry on"
        /// protocol (Delayed / PostponedWork / FailureAction), and Shell.OnClosing had to cancel
        /// the close and re-issue it once the save finished. The browser now volunteers the page
        /// as it goes (see PageSnapshot), so there is nothing left to wait for.
        ///
        /// A null snapshot means the page has not been changed since it loaded, so there is
        /// nothing to merge -- but we still write the book, because other things (a page added,
        /// a page deleted) may be sitting in the DOM unwritten.
        ///
        /// The one thing this cannot do is save a change made in the last few tens of
        /// milliseconds, which the browser has not posted yet. See "The freshness window" in
        /// SavingWithoutReloading.md for why that is the accepted trade.
        /// </summary>
        public bool SaveCurrentPageAndBook()
        {
            if (CannotSavePage())
                return false;
            if (_reloadFromDiskOnLeavingEditTab)
            {
                // An external process replaced the book on disk and we are discarding the user's
                // page in favour of what it wrote. Writing now would clobber exactly what that
                // guard exists to protect. Same rule as SavePageInPlaceThen's Refused outcome.
                // (Leaving the Edit tab clears the flag and handles that case itself, so this is
                // for the other callers.)
                return false;
            }
            UpdateBookDomFromBrowserPageContent(CurrentPageSnapshotOrNull);
            CurrentBook.Save();
            return true;
        }

        /// <summary>
        /// As SaveCurrentPageAndBook, plus the book-created history entry that only belongs at the
        /// end of a session.
        /// </summary>
        public void SaveEverythingBeforeClosing()
        {
            if (SaveCurrentPageAndBook())
                CurrentBook.RecordPendingCreatedHistoryEvent();
        }

        /// <summary>
        /// Write out whatever UpdateBookDomFromBrowserPageContent() put into the book DOM: either just
        /// the one page that changed, or the whole book if something shared changed.
        /// This is the state machine's saveBook action, and also the second half of SavePageInPlace,
        /// so both routes make exactly the same decisions.
        /// </summary>
        private void SaveBookToDisk()
        {
            if (_modifiedPageElement == null)
                return;

            CurrentBook.SavePageToDisk(_modifiedPageElement, _nextSaveMustBeFull);
            _nextSaveMustBeFull = false;
            _pageHasUnsavedDataDerivedChange = false;
            PageTemplatesApi.LastSaveTime = DateTime.Now;
        }

        /// <summary>
        /// Save the current page from content the browser has ALREADY gathered — the combined
        /// "body &lt;SPLIT-DATA&gt; userCss" string that getPageContentForSave() produces — and leave the
        /// browser showing that same page, still editable.
        ///
        /// This is the Javascript-initiated counterpart of MergeCurrentPageThenSave(). The
        /// difference is only in what happens afterwards: that one navigates to a page the caller
        /// names, because its callers are changing which pages exist; this one leaves the browser
        /// showing the same page, still editable, which is possible at all because the gather no
        /// longer strips the live page of the markup that makes it editable (BL-13502).
        ///
        /// It deliberately goes through the same two steps as MergeCurrentPageThenSave — first
        /// UpdateBookDomFromBrowserPageContent(), then SaveBookToDisk() — so the same logic decides
        /// whether the change is confined to this page or has to be propagated across the book
        /// (see NeedToDoFullSave and Book.UpdateDomFromEditedPage).
        ///
        /// Returns false, having done nothing, if we are not in a position to save. That is a normal
        /// outcome, not an error: the user may have started changing pages, or an external process may
        /// have replaced the book on disk.
        /// </summary>
        public bool SavePageInPlace(string pageContentData, bool forceFullSave = false)
        {
            if (CannotSavePage() || !_havePageToSave)
                return false;
            // An external process has overwritten the book on disk and we are about to discard this
            // page in favor of what it wrote; saving now would clobber that. (SavePageInPlaceThen
            // refuses for the same reason, which covers the MergeCurrentPageThenSave path.)
            if (_reloadFromDiskOnLeavingEditTab)
                return false;

            _nextSaveMustBeFull |= forceFullSave;
            if (
                !_stateMachine.ToSavedInPlace(
                    pageContentData,
                    e =>
                        ErrorReport.NotifyUserOfProblem(
                            e,
                            LocalizationManager.GetString(
                                "Errors.CouldNotSavePage",
                                "Bloom had trouble saving a page. Please report the problem to us. Then quit Bloom, run it again, and check to see if the page you just edited is missing anything. Sorry!"
                            )
                        )
                )
            )
                return false;

            // What we just saved is the new baseline for deciding whether the NEXT save has changed
            // anything the rest of the book shares. (For the MergeCurrentPageThenSave path, the navigation that
            // follows a save does this, in EditingView.StartNavigationToEditPage.)
            SaveStateForFullSaveDecision();
            // Likewise, the page list would normally be refreshed as part of navigating.
            _view?.UpdateThumbnailAsync(_pageSelection.CurrentSelection);
            return true;
        }

        /// <summary>
        /// The body of MergeCurrentPageThenSave: merge pageContentData into the book, run
        /// changeBookBeforeWriting (which may change the book, and returns the id of the page to
        /// show next), write the book to disk, and navigate there — all synchronously, before we
        /// return.
        ///
        /// Returns Declined, having done nothing at all, if we were not in a position to save --
        /// which is the only outcome where the caller's fallback is the right response. If it
        /// returns Failed, changeBookBeforeWriting may already have run and changed the book, so
        /// treating it as "nothing happened" would run it a second time. See InPlaceSaveOutcome.
        ///
        /// Private because MergeCurrentPageThenSave is the way in: it supplies the page content,
        /// falling back to the snapshot when the caller had none, so no caller has to get that
        /// right.
        /// </summary>
        private InPlaceSaveOutcome SavePageInPlaceThen(
            string pageContentData,
            Func<string> changeBookBeforeWriting,
            bool forceFullSave = false
        )
        {
            if (CannotSavePage() || !_havePageToSave)
                return InPlaceSaveOutcome.Declined;
            // See SavePageInPlace: an external process has replaced the book on disk, so this
            // page's content must not be written over what it wrote. Refused, NOT Declined --
            // Declined would send the caller to the ask-the-browser path, which has no such guard
            // and would write the page anyway.
            if (_reloadFromDiskOnLeavingEditTab)
                return InPlaceSaveOutcome.Refused;

            _nextSaveMustBeFull |= forceFullSave;
            // Unlike SavePageInPlace there is nothing to do afterwards on success: we do NOT
            // refresh the full-save baseline or the thumbnail, because navigating does both for
            // us, in EditingView.StartNavigationToEditPage, which this has already started.
            return _stateMachine.ToSavedInPlaceThenNavigating(
                pageContentData,
                changeBookBeforeWriting,
                e =>
                    ErrorReport.NotifyUserOfProblem(
                        e,
                        LocalizationManager.GetString(
                            "Errors.CouldNotSavePage",
                            "Bloom had trouble saving a page. Please report the problem to us. Then quit Bloom, run it again, and check to see if the page you just edited is missing anything. Sorry!"
                        )
                    )
            );
        }

        private SafeXmlElement _modifiedPageElement;

        /// <summary>
        /// Receives a DOM (derived the browser) that combines the body of the document of the page
        /// being edited with the CSS that defines the user-defined styles. It updates the current book DOM
        /// to match whatever the browser has.
        /// </summary>
        public void UpdateBookDomFromBrowserPageContent(SafeXmlDocument docFromBrowser)
        {
            //BL-1064 (and several other reports) were about not being able to save a page. The problem appears to be that
            //this old code:
            //	CurrentBook.SavePage(_domForCurrentPage);
            //would some times ask book X to save a page from book Y.
            //We could never reproduce it at will, so this is to help with that...
            if (CurrentBook != _currentlyDisplayedBook)
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
                if (!CurrentBook.IsSaveable)
                {
                    Logger.WriteEvent(
                        "Error: SaveNow() found that this book had IsSaveable=='false'"
                    );
                    Logger.WriteEvent("Book path was {0}", CurrentBook.FolderPath);
                    throw new ApplicationException(
                        "Bloom tried to save a page to a book that was not in a position to be updated."
                    );
                }
            }
            catch (ObjectDisposedException) // in case even calling CanUpdate gave an error
            {
                Logger.WriteEvent("Error: SaveNow() found that this book was disposed.");
                throw;
            }
            catch (Exception) // in case even calling CanUpdate gave an error
            {
                Logger.WriteEvent("Error: SaveNow():CanUpdate threw an exception");
                throw;
            }
            //OK, looks safe, time to save.
            var editedDom = new HtmlDom(docFromBrowser);
            var newPageData = GetPageData(editedDom.RawDom);
            // True when something OUTSIDE the page HTML we are about to hand over wants saving: a
            // data-derived value some dialog changed, altered feature requirements, or a caller
            // that explicitly forced a full save. The page content test below cannot see any of
            // those, so they have to be kept separately.
            var somethingElseNeedsSaving = _nextSaveMustBeFull || NeedToDoFullSave(newPageData);

            _nextSaveMustBeFull = CurrentBook.UpdateDomFromEditedPage(
                editedDom,
                out _modifiedPageElement,
                somethingElseNeedsSaving,
                out var anythingChanged
            );

            // The page says exactly what the book already said and nothing else is outstanding, so
            // there is nothing to write. A null _modifiedPageElement is how SaveBookToDisk is
            // already told there is nothing to save.
            if (!anythingChanged && !somethingElseNeedsSaving)
                _modifiedPageElement = null;
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

        /// <param name="source">For analytics; passed on to UpdateImageInBrowser.</param>
        public void ChangePicture(
            string imageId,
            UrlPathString priorImageSrc,
            PalasoImage imageInfo,
            string source,
            string pageBackgroundColor = null
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
                    imageInfo,
                    pageBackgroundColor,
                    undoable: true // All image changes made here are undoable.
                );
                UpdateImageInBrowser(args, source);
            }
            catch (Exception e)
            {
                var msg = LocalizationManager.GetString(
                    "Errors.ProblemImportingPicture",
                    "Bloom had a problem importing this image."
                );
                e.Data["ProblemImagePath"] = imageInfo.OriginalFilePath;
                ErrorReport.NotifyUserOfProblem(e, msg + Environment.NewLine + e.Message);
            }
        }

        /// <param name="source">Where this picture came from, for analytics: see
        /// AnalyticsApi.TrackChangePicture. Every caller here is some form of paste; the image
        /// chooser and the AI image editor report their own.</param>
        public void UpdateImageInBrowser(
            PageEditingModel.ImageInfoForJavascript args,
            string source
        )
        {
            // We generally don't need to wait since we don't need to save as part of this operation.
            // If a cover image needs to be made transparent, code in version 6.5 and later takes care of that elsewhere.
            // Not saving here greatly simplifies Undo image changes for cover pages.  (BL-16330)
            GetEditingBrowser()
                .RunJavascriptFireAndForget(
                    $"workspaceBundle.getEditablePageBundleExports().changeImage({JsonConvert.SerializeObject(args)})"
                );
            // not saving, but we still want to log etc.
            AnalyticsApi.TrackChangePicture(source, CurrentBook?.ID);
            Logger.WriteEvent("ChangePicture {0}...", (object)args.src);
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

        // Client context and event id used to tell the open Copyright & License dialog that an
        // "Add this info to all images" operation has finished. These must match the constants
        // used by the React dialog (CopyrightAndLicenseDialog.tsx).
        public const string kCopyrightWebSocketContext = "copyrightAndLicense";
        public const string kCopyrightWebSocketEventId_PushedToAllImages = "pushedToAllImages";

        /// <summary>
        /// Tell the (still-open) Copyright &amp; License dialog that an "Add this info to all
        /// images" request has finished, so it can replace its "Working…" spinner with a "done"
        /// confirmation. The image-metadata POST handler calls this for every such request,
        /// whether or not the copy actually ran (e.g. the save failed or the image is not a
        /// normal image), so the dialog never waits forever. We can't signal completion from the
        /// POST response itself, which returns as soon as the save is initiated, well before the
        /// asynchronous post-save action runs.
        /// </summary>
        public void NotifyCopyrightPushedToAllImages()
        {
            _webSocketServer.SendEvent(
                kCopyrightWebSocketContext,
                kCopyrightWebSocketEventId_PushedToAllImages
            );
        }

        public void CopyImageMetadataToWholeBook(Metadata metadata)
        {
            using (var dlg = new ProgressDialogForeground()) //REVIEW: this foreground dialog has known problems in other contexts... it was used here because of its ability to handle exceptions well. TODO: make the background one handle exceptions well
            {
                dlg.ShowAndDoWork(progress =>
                    CurrentBook.CopyImageMetadataToWholeBookAndSave(metadata, progress)
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
            _view.ShowChangeLayoutDialog();
        }

        public void ChangeBookLicenseMetaData(Metadata metadata)
        {
            // This is awkward.
            // Originally, one could only open the CopyrightAndLicenseDialog from the Edit tab. Now, one can open it from the Publish tab.
            // I wanted to introduce an event or other mechanism such that Edit and Publish could each do what they need to when
            // the dialog is closed, but CopyrightAndLicenseApi is already so entangled with EditModel, it wasn't going to be clean
            // no matter what I did. And this is simpler.

            // For Edit tab:
            if (Visible)
            {
                MergeCurrentPageThenSave(() =>
                {
                    CurrentBook.SetMetadata(metadata);
                    _pageHasUnsavedDataDerivedChange = true;
                    return _pageSelection.CurrentSelection.Id;
                });
            }
            else
            {
                CurrentBook.SetMetadata(metadata);
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

        public void CopyPage(IPage page, string pageContentFromBrowser = null)
        {
            // We have to clone the page div so that if the user changes the page after doing the
            // copy, when they paste they get the page as it was, not as it is now. And we have to
            // save first, or the clone would miss any typing they have done but not yet saved
            // (BL-4512).
            Action takeTheSnapshot = () =>
            {
                _pageDivFromCopyPage = (SafeXmlElement)page.GetDivNodeForThisPage().CloneNode(true);
                _bookPathFromCopyPage = page.Book.GetPathHtmlFile();
            };

            // In practice the page being copied is ALWAYS the selected one: the page list only
            // opens its context menu on the selected page (see openContextMenu in
            // pageThumbnailList.tsx, which bails unless pageId === selectedPageId), and the menu
            // button is only rendered there. So we take the in-place branch and copying a page
            // does not reload it -- which is the win here.
            //
            // The navigating branch is kept as a safety net rather than dead weight, because the
            // copied page MUST end up selected: a later Paste inserts after the current selection
            // (see DeterminePageWhichWouldPrecedeNextInsertion), so were that guarantee ever
            // relaxed, copying without selecting would drop the pasted copy somewhere the user
            // did not ask for.
            var copyingTheSelectedPage = _pageSelection.CurrentSelection?.Id == page.Id;
            if (
                copyingTheSelectedPage
                && pageContentFromBrowser != null
                && SavePageInPlace(pageContentFromBrowser, forceFullSave: true)
            )
            {
                takeTheSnapshot();
                return;
            }
            MergeCurrentPageThenSave(
                () =>
                {
                    takeTheSnapshot();
                    return page.Id;
                },
                forceFullSave: true
            );
        }

        /// <summary>
        /// Paste the previously saved _pageDivFromCopyPage as a new page.
        /// </summary>
        /// <param name="pageToPasteAfter">This is NOT the page we are to paste!</param>
        public void PastePage(IPage pageToPasteAfter, string pageContentFromBrowser = null)
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
            // false => don't need analytics on use of template pages
            InsertPage(pageForPasting, new PageInsertEventArgs(false), pageContentFromBrowser);
        }

        public void AdjustPageZoom(int delta)
        {
            _view.AdjustPageZoom(delta);
        }

        /// <summary>
        /// Make sure the book folder contains a current version of the video placeholder.
        /// We don't copy this to every book, since relatively few books need it,
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

        public void HandlePageDomLoadedEvent(string pageId)
        {
            var nowEditing = _stateMachine.ToEditing(pageId);
            if (nowEditing)
            {
                // Run whatever was queued for "the browser has a page again" (see
                // RunAfterNextPageLoad). Taken and cleared before invoking, so it fires at most
                // once even if it throws, and so an action that queues another one works.
                // Before AdvanceUpdatingAllPages, which may navigate straight off this page.
                var afterPageLoad = _doAfterNextPageLoad;
                _doAfterNextPageLoad = null;
                afterPageLoad?.Invoke(pageId);
            }
            // If we are in the middle of the "Update Book" per-page pass, a page finishing loading
            // (which means the edit-tab page setup code has run on it) is our cue to save it and
            // move on to the next page. See StartUpdatingAllPages().
            if (nowEditing && _updatingAllPages)
                AdvanceUpdatingAllPages(pageId);
        }

        // The one action queued by RunAfterNextPageLoad, or null.
        private Action<string> _doAfterNextPageLoad;

        /// <summary>
        /// Arrange for <paramref name="action"/> to run the next time a page finishes loading in
        /// the browser, passing it that page's id.
        ///
        /// This exists for callers that must save the current page before doing something in the
        /// browser that needs the saved book DOM to be up to date. Saving strips the live page, so
        /// it always ends by re-navigating to it (see EditingStateMachine) — which means
        /// MergeCurrentPageThenSave is too early for such a caller: it returns before that
        /// navigation, so the browser code it started would be torn down. Waiting for the page to
        /// come back is the only safe point. AiImageEditorApi.HandleSaveThenLaunch is the caller
        /// this was written for (BL-16682).
        ///
        /// Note that "torn down" is not limited to the page iframe, which is why this cannot be
        /// worked around by putting the browser code somewhere higher up.
        /// EditingView.StartNavigationToEditPage picks one of three routes, and the third reloads
        /// the whole workspace root document. In practice that route is reached when
        /// MemoryUtils.SystemIsShortOfMemory() — which is Bloom's OWN private bytes past ~2GB, so
        /// the ordinary state of a long editing session on a big book, and exactly what the full
        /// reload exists to recover from. (Its other trigger, _changingUiLanguage, appears
        /// unreachable from the edit tab today: everything that sets it — choosing a UI language,
        /// toggling unapproved translations — reopens the project or restarts Bloom first. Don't
        /// rely on that; the memory condition alone is enough.) So no browser-side state at all is
        /// guaranteed to survive the navigation that ends a save; only C#-side state like this is.
        ///
        /// Only one action is held; queueing a second replaces the first, and passing null cancels.
        /// The page that loads next is not necessarily the one the caller was on (the user may have
        /// navigated, or the save may have failed), so callers that care must check the id they are
        /// given. Leaving the Edit tab drops it (see OnTabAboutToChange), since no page would load
        /// to run it and the caller's page is no longer on screen.
        /// </summary>
        public void RunAfterNextPageLoad(Action<string> action)
        {
            _doAfterNextPageLoad = action;
        }

        // Fields supporting the "Update Book" per-page pass (see StartUpdatingAllPages()).
        // _updatingAllPages is true while we are visiting and saving every page in turn.
        // _pageUpdateOrder is the list of page IDs to visit (in book order); _pagesRemainingToUpdate
        // is the subset we have not yet visited on this pass.
        private bool _updatingAllPages;
        private List<string> _pageUpdateOrder;
        private HashSet<string> _pagesRemainingToUpdate;

        /// <summary>
        /// Visit every page in the current book as if the user had clicked on each one in the Edit
        /// tab, saving each page as we leave it. Visiting a page runs the normal edit-tab page setup
        /// code (both the server-side edit DOM construction and the browser's SetupElements), and
        /// saving it persists whatever that setup changed. This gives the "Update Book" command the
        /// same effect as manually going to the Edit tab and clicking each page, which is what we
        /// used to have to tell users to do (BL-16595).
        ///
        /// The pass is inherently asynchronous: each navigation waits for the browser to load the
        /// page and report back (editView/pageDomLoaded -> HandlePageDomLoadedEvent), which then
        /// drives us on to the next page via the normal save-then-navigate state machine. So this
        /// method just sets things up and starts the first navigation; the chain continues in
        /// AdvanceUpdatingAllPages() and ends in FinishUpdatingAllPages().
        /// </summary>
        public void StartUpdatingAllPages()
        {
            var book = CurrentBook;
            if (book == null)
                return;
            var pageIds = book.GetPages().Select(p => p.Id).ToList();
            if (pageIds.Count == 0)
                return;

            // If the book has structural errors, showing it in the Edit tab displays an error page
            // instead of navigating to a real page (see the early return in OnBecomeVisible). That
            // means no pageDomLoaded event would arrive to start the chain, and we would be left
            // stuck with _updatingAllPages set. Skip the per-page pass in that case; the whole-book
            // update (BringBookUpToDate) has already run.
            if (!string.IsNullOrEmpty(book.CheckForErrors()))
            {
                Logger.WriteEvent(
                    "Update Book: skipping the per-page pass because the book has errors."
                );
                return;
            }

            _pageUpdateOrder = pageIds;
            _pagesRemainingToUpdate = new HashSet<string>(pageIds);
            _updatingAllPages = true;
            Logger.WriteEvent(
                $"Update Book: visiting {pageIds.Count} page(s) to apply edit-tab updates to each."
            );

            if (Visible)
            {
                // We are already in the Edit tab. Kick off the chain by navigating to the first page.
                // (MergeCurrentPageThenSave saves whatever page is showing, then navigates.)
                var firstPageId = _pageUpdateOrder[0];
                MergeCurrentPageThenSave(() => firstPageId, () => FinishUpdatingAllPages());
            }
            else
            {
                // Switch to the Edit tab. Becoming visible navigates to a page (see OnBecomeVisible),
                // and that page's pageDomLoaded event starts the chain.
                _view.WorkspaceView.ChangeTab(Workspace.WorkspaceTab.edit);
            }
        }

        /// <summary>
        /// Called (via HandlePageDomLoadedEvent) each time a page finishes loading during the
        /// "Update Book" per-page pass. Saves the page we just visited and navigates to the next
        /// page still needing a visit, or finishes the pass if this was the last one.
        /// </summary>
        private void AdvanceUpdatingAllPages(string loadedPageId)
        {
            _pagesRemainingToUpdate.Remove(loadedPageId);
            var nextPageId = _pageUpdateOrder.FirstOrDefault(id =>
                _pagesRemainingToUpdate.Contains(id)
            );
            if (nextPageId != null)
            {
                // Save the page we just visited (persisting the edit-tab setup that ran on it) and
                // move on. Reusing the normal save-then-navigate cycle means each page gets exactly
                // the treatment it would if the user clicked it in the Edit tab.
                MergeCurrentPageThenSave(() => nextPageId, () => FinishUpdatingAllPages());
            }
            else
            {
                // We just visited the last page. Save it, then return to the Collection tab. We
                // show a blank page because we are about to leave the Edit tab anyway. Switching
                // tabs is still deferred to the next message, so we don't re-enter the state
                // machine from inside this call.
                SaveCurrentPageAndBook();
                _stateMachine.ToNoPageHavingSaved();
                _view.BeginInvoke((Action)FinishUpdatingAllPages);
            }
        }

        /// <summary>
        /// Ends the "Update Book" per-page pass and returns to the Collection tab, where the command
        /// was invoked. Guarded so it is harmless to call more than once.
        /// </summary>
        private void FinishUpdatingAllPages()
        {
            if (!_updatingAllPages)
                return;
            _updatingAllPages = false;
            _pagesRemainingToUpdate = null;
            _pageUpdateOrder = null;
            Logger.WriteEvent("Update Book: finished visiting all pages.");
            _view?.WorkspaceView?.ChangeTab(Workspace.WorkspaceTab.collection);
        }

        // This speeds up developing brandings. It may speed up other things, but I haven't tested those.
        // Currently, branding.json changes won't be visible until you change pages (or click on the current page thumbnail)
        private void StartWatchingDeveloperChanges()
        {
            // This speeds up the process of tweaking branding files
            if (Debugger.IsAttached)
            {
                _developerFileWatcher = new FileSystemWatcher { IncludeSubdirectories = true };
                _developerFileWatcher.Path =
                    FileLocationUtilities.GetDirectoryDistributedWithApplication(
                        BloomFileLocator.BrowserRoot
                    );
                // we don't want _developerFileWatcher to fire initially, onlye when there's a change
                _developerFileWatcher.NotifyFilter = NotifyFilters.LastWrite;

                var waitingForInitialLoad = true;
                // Not async: nothing in this handler awaits anything (it delegates timing to a
                // WinForms Timer), so marking it async would just create an async-void event handler
                // whose exceptions we couldn't observe.
                _developerFileWatcher.Changed += (sender, args) =>
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
                        DateTime
                            .Now.Subtract(_lastTimeWeReloadedBecauseOfDeveloperChange)
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
                // Deliberately fire-and-forget: this is just a one-shot timer that stops us treating
                // the initial burst of file-watcher events as real changes. Nothing depends on when
                // it completes, so there is no reason to await the resulting Task.
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
            if (_weHaveSeenAJsonChange && _currentlyDisplayedBook != null)
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
                            _view.Refresh();

                            _pageSelection.SelectPage(
                                _currentlyDisplayedBook.GetPageByIndex(pageIndex)
                            );
                        }
                    )
                );
            }
            else // css, png, svg, js, etc.
            {
                CurrentBook.UpdateSupportFiles();
                if (!_view.IsDisposed && _view.IsHandleCreated)
                {
                    _view.Invoke(
                        (MethodInvoker)
                            delegate
                            {
                                SavePageAndReloadIt();
                            }
                    );
                }
            }
            _weHaveSeenAJsonChange = false;
        }
    }
}
