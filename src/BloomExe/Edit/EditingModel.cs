//#define MEMORYCHECK
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
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
using Sentry.Protocol;
using SIL.Code;
using SIL.Extensions;
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
        private BloomServer _server;
        private readonly BloomWebSocketServer _webSocketServer;
        internal IPage PageChangingLayout; // used to save the page on which the choose different layout command was invoked while the dialog is active.

        // This event fires after the EditingModel has finished responding to a PageSelection change.
        internal event EventHandler PageSelectModelChangesComplete;

        // These variables are not thread-safe. Access only on UI thread.
        internal bool InProcessOfSaving => _stateMachine.SavePending;

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

        /// <summary>
        /// If this is set, the model may call it (passing false) to prevent switching to and from the Edit tab.
        /// It should then be called (passing true) when it's OK to switch tabs again.
        /// We use this so that
        /// (a) when we are in the process of completing a Save for some command in the edit tab, we can't leave that
        /// tab (until the save is complete), and
        /// (b) when are doing the Save that is part of leaving the Edit tab, we can't switch back to Edit tab
        /// until the save is complete and we're in the expected state for entering the tab.
        /// Without these restrictions, it is hard to reason about the possible states of the system
        /// as we execute commands and then rapidly switch tabs.
        /// Probably only testers will switch tabs so frequently as to run into such problems, and even
        /// they may not notice the brief disabling of the tabs.
        /// </summary>
        public Action<bool> EnableSwitchingTabs;

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

            _stateMachine = new EditingStateMachine(
                // navigate,
                (string pageId) =>
                {
                    StartNavigationToEditPage(CurrentBook.GetPage(pageId));
                },
                //requestPageSave,
                (string pageId) =>
                {
                    RequestBrowserToSave();
                },
                // updateBookWithPageContent
                (string pageId, string pageContentData) =>
                    UpdateBookDomFromBrowserPageContent(pageContentData),
                // saveBook
                () =>
                {
                    if (_modifiedPageElement == null)
                        return;

                    CurrentBook.SavePageToDisk(_modifiedPageElement, _nextSaveMustBeFull);
                    _nextSaveMustBeFull = false;
                    _pageHasUnsavedDataDerivedChange = false;
                    PageTemplatesApi.LastSaveTime = DateTime.Now;
                },
                // hidePage
                () =>
                {
                    if (_view != null)
                    {
                        _view.OnHideEditTab();
                    }
                },
                enableStateTransitions: (enabled) => EnableSwitchingTabs?.Invoke(enabled)
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
            duplicatePageCommand.Implementer = OnDuplicatePage;
            deletePageCommand.Implementer = OnDeletePage;
            pageListChangedEvent.Subscribe(needFullUpdate => _view.UpdatePageList(needFullUpdate));
            relocatePageEvent.Subscribe(OnRelocatePage);
            collectionClosingEvent.Subscribe(args =>
            {
                if (Visible)
                {
                    // We want to save any changes, and they ought to be fully saved before we shut down the program.
                    // To that end we normally set Delayed to indicate that we take responsibility for doing the caller-supplied
                    // action that continues the shutdown process (after the Save completes).
                    // If we can't initiate a Save, we'll just let the shutdown proceed (leave Delayed false).
                    // Review: should we warn the user? Displaying UI while the user is tying to close the program is
                    // generally a bad idea, but we may have failed to save some changes. On the other hand,
                    // the only likely reason for this is that the program is in a bad state, probably from a previously
                    // reported error.
                    args.Delayed = true;
                    SaveThen(
                        () =>
                        {
                            // We are setting skipSaveToDisk true so that we can do it ourselves here BEFORE
                            // the postponed work, which is going to shut everything down and would prevent
                            // the normal automatic save-to-disk from working.
                            // If the save failed before this action gets called, Delayed is true, and PostponedWork
                            // doesn't get done at all. This typically means the collection will not close.
                            // However, FailureAction should be called in this case which allows closing the collection
                            // to try again. If we do try again and the same page fails again, the state machine will
                            // call this action anyway. So, finally PostponedWork will get called and we can close the collection.
                            CurrentBook.Save();
                            args.PostponedWork();
                            return null;
                        },
                        doIfNotInRightStateToSave: () => args.Delayed = false, // go ahead and quit now
                        skipSaveToDisk: true,
                        failureAction: args.FailureAction
                    );
                }
            });
            localizationChangedEvent.Subscribe(o =>
            {
                if (_view != null)
                {
                    _view.NextReloadChangesUiLanguage();
                    _view.UpdateButtonLocalizations();
                }

                //this is visible was added for https://jira.sil.org/browse/BL-267, where the edit tab has never been
                //shown so the view has never been full constructed, so we're not in a good state to do a refresh
                if (Visible)
                {
                    SaveThen(
                        () =>
                        {
                            _view.UpdatePageList(false);
                            return _pageSelection.CurrentSelection.Id;
                        },
                        () => { } // wrong state, I think there's nothing we can safely do.
                    );
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
        /// Enhance: ideally we would use a mutation observer so the browser knows whether anything needs saving,
        /// and this method would get something indicating it doesn't need to save if that's so.
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
        /// editable page browser), this method creates a new XmlDocument that contains the same state.
        /// It does some additional cleanup of things that get added to the page as UI controls
        /// to support editing. (Enhance: it would be nice if ALL the cleanup happened in one place,
        /// probably the Javascript method that retrieves the page content).
        /// (Nicer still if cleanup didn't leave the page in an invalid state, see BL-13502.)
        /// </summary>
        private XmlDocument GetCleanCurrentPageFromBodyAndCss(
            string bodyHtml,
            string userCssContent
        )
        {
            // If anything goes badly wrong here, we want to throw rather then just bringing up a dialog.
            // The process of saving the page content to the DOM should either succeed or throw, so that
            // we don't get stuck in an invalid state that locks up the UI.

            if (string.IsNullOrEmpty(bodyHtml))
                throw new ApplicationException("Got an empty body while trying to save page");

            var content = bodyHtml;
            XmlDocument dom;

            //todo: deal with exception that can come out of this
            dom = XmlHtmlConverter.GetXmlDomFromHtml(content, false);
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
            if (BookStorage.CheckForEmptyMarginBoxOnPage(browserDomPage as XmlElement))
            {
                //We don't want to save the empty page.
                // This has been logged and reported to the user; we would prefer not to report it again, but we need the exception
                // handling inside the state machine to run so we can maintain a valid state, so we throw anyway.
                // Enhance: make that reporter not report again when we know we have already reported.
                throw new ApplicationException("Check for valid margin box failed");
            }

            SaveCustomizedCssRules(dom, userCssContent);
            XmlHtmlConverter.ThrowIfHtmlHasErrors(dom.OuterXml);
            return dom;
        }

        private void SaveCustomizedCssRules(XmlDocument dom, string userCssContent)
        {
            // Yes, this wipes out everything else in the head. At this point, the only things
            // we need in _pageEditDom are the user defined style sheet and the bloom-page element in the body.
            dom.GetElementsByTagName("head")[0].InnerXml = HtmlDom.CreateUserModifiedStyles(
                userCssContent
            );
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
                SaveThen(
                    () =>
                    {
                        // We are setting skipSaveToDisk true so that we can do it ourselves here BEFORE
                        // the postponed work, which is going to shut everything down and would prevent
                        // the normal automatic save-to-disk from working.
                        CurrentBook?.Save(); // we need it all the way saved before doing the PostponedWork
                        // This bizarre behavior prevents BL-2313 and related problems.
                        // For some reason I cannot discover, switching tabs when focus is in the Browser window
                        // causes Bloom to get deactivated, which prevents various controls from working.
                        // Moreover, it seems (BL-2329) that if the user types Alt-F4 while whatever-it-is is active,
                        // things get into a very bad state indeed. So arrange to re-activate ourselves as soon as the dust settles.
                        _oldActiveForm = Form.ActiveForm;
                        Application.Idle += ReactivateFormOnIdle;
                        details.PostponedWork?.Invoke();
                        return null; // leaving this tab, show blank page
                    },
                    () =>
                    {
                        // We disable the tab control while we're in SavePending or SavedAndStripped.
                        // We shouldn't be in NoPage while in the edit tab, but if we somehow are, we take the branch above.
                        // If we're Editing, we will take the branch above.
                        // So this is just the case where we're Navigating, either because we clicked on the Edit tab
                        // and then immediately something else, or clicked another tab during the fraction of a second
                        // while Bloom is navigating to a new page after doing some command. Abort the navigate, then go ahead.
                        Guard.AssertThat(
                            StateMachine.Navigating,
                            "This branch should only be taken when navigating"
                        );
                        StateMachine.ToNoPage();
                        _oldActiveForm = Form.ActiveForm;
                        Application.Idle += ReactivateFormOnIdle;
                        details.PostponedWork?.Invoke();
                    },
                    skipSaveToDisk: true
                );
            }
            else
            {
                // If the old tab is not Edit, we don't need to save anything, so just do the postponed work.
                details.PostponedWork?.Invoke();
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
            var newPageId = page.Id; // error fallback
            SaveThen(
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
                    return newPageId;
                },
                () => { }, // wrong state, do nothing
                forceFullSave: true
            );
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
            if (InProcessOfSaving)
            {
                // Somehow (BL-431) it's possible that a Save is still in progress when we start executing a delete page.
                // If this happens, just abort the delete.
                return;
            }
            _inProcessOfDeleting = true;
            SaveThen(
                () =>
                {
                    try
                    {
                        var pageToShowNext = GetPageToShowAfterDeletion(page);
                        _currentlyDisplayedBook.DeletePage(page);
                        //_view.UpdatePageList(false);  DeletePage calls this via pageListChangedEvent.  See BL-3632 for trouble this causes.
                        Logger.WriteEvent("Delete Page");
                        Analytics.Track("Delete Page");
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
                    finally
                    {
                        _inProcessOfDeleting = false;
                    }
                },
                () => { }, // wrong state, do nothing
                forceFullSave: true
            );
        }

        private IPage GetPageToShowAfterDeletion(IPage page)
        {
            var pages = CurrentBook.GetPages().ToList();
            var index = pages.IndexOf(page);
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

                Analytics.Track("Relocate Page");
                Logger.WriteEvent("Relocate Page");
            }
        }

        /// <summary>
        /// This is used both to insert pages from the AddPageDialog, and also "paste page"
        /// </summary>
        private void OnInsertPage(object page, PageInsertEventArgs e)
        {
            SaveThen(
                () =>
                { // there might be unsaved changes in the current page from before we clicked Add Page
                    var newPageId = CurrentBook.InsertPageAfter(
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
                    return newPageId;
                },
                () => { }, // wrong state, do nothing
                forceFullSave: true
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
            SaveThen(
                () =>
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
                },
                () => { } // wrong state, do nothing
            );
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
            SaveThen(
                () =>
                {
                    CurrentBook.PrepareForEditing();
                    _view.UpdatePageList(true); //counting on this to redo the thumbnails

                    Logger.WriteEvent("ChangingContentLanguages");
                    Analytics.Track("Change Content Languages");
                    return _pageSelection.CurrentSelection.Id;
                },
                () => { } // wrong state, do nothing
            );
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
            _skipNextSaveBecauseDeveloperIsTweakingSupportingFiles = false;
            if (_view != null)
            {
                _view.UpdatePageList(false);
            }
        }

        /// <summary>
        /// The code invoked by the state machine to actually start the editable page browser navigating
        /// to a particular page. Anything that needs saving on the current page should already have been saved.
        /// </summary>
        void StartNavigationToEditPage(IPage page)
        {
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

                    CurrentBook.BringPageUpToDate(page.GetDivNodeForThisPage());
                    if (Visible)
                        _view.StartNavigationToEditPage(page);

                    _duplicatePageCommand.Enabled = !_pageSelection.CurrentSelection.Required;
                    _deletePageCommand.Enabled = !_pageSelection.CurrentSelection.Required;

                    CheckForBL8852();

                    PageSelectModelChangesComplete?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception e)
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
        /// Also see comments in EditingView.StartNavigationToEditPage.
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
                SaveThen(
                    () =>
                    {
                        _currentlyDisplayedBook.OurHtmlDom.Body.SetAttribute(
                            "data-leveledreaderlevel",
                            correctLevel
                        );
                        return _pageSelection.CurrentSelection.Id;
                    },
                    () => { } // wrong state, do nothing
                );
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
            SavePageAndReloadIt();
        }

        internal void SavePageAndReloadIt(ApiRequest request)
        {
            SavePageAndReloadIt();
            request.PostSucceeded();
        }

        internal void RethinkPageAndReloadItAndReportIfItFails(bool forceFullSave = false)
        {
            try
            {
                SavePageAndReloadIt(forceFullSave);
            }
            catch (Exception e)
            {
                NonFatalProblem.Report(ModalIf.Beta, PassiveIf.Beta, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Save all the changes to the current page, then reload it (thus restoring any UI stuff that
        /// was stripped out by the Save).
        /// </summary>
        internal void SavePageAndReloadIt(bool forceFullSave = false)
        {
            if (CannotSavePage())
                return;
            _nextSaveMustBeFull |= forceFullSave;
            SaveThen(() => _pageSelection.CurrentSelection.Id, () => { });
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
        public bool NavigatingSoSuspendSaving => _stateMachine.Navigating;
        private System.Windows.Forms.Timer _developerFileWatcherQuietTimer;
        private bool _weHaveSeenAJsonChange;

        private bool _nextSaveMustBeFull; // review: store in state machine?

        /// <summary>
        /// Request the needed data to do a save, then when the contents of the current page have been saved,
        /// do the given action. The result of the action is a page ID which we will then navigate to, or null
        /// to show a blank screen if we are leaving the edit tab.
        /// If we are not in the right state to save, doAfterSaving() will not be called at all,
        /// but instead doIfNotInRightStateToSave() will be called. Usually the latter does nothing,
        /// but we are deliberately not making it optional to make sure it gets thought about.
        /// Usually, the book will be saved to disk after executing the action, but before navigating to
        /// the new page. Sometimes the action needs to save the book itself, in which case it can prevent
        /// a further Save() by setting skipSaveToDisk to true. (Or just possibly, we might not want the Save
        /// to go all the way to disk, especially if we aren't going to switch pages.)
        /// Returns true if we're in a valid state to save, false if we're not. In the latter case, doAfterSaving
        /// will not be called (even later).
        /// </summary>
        /// <remarks>If you are doing this in an API handler, remember that you must retrieve any data in
        /// the request before calling SaveThen. The Request object can't be used in side the doAfterSaving function,
        /// since by then the request has been marked completed.</remarks>
        public void SaveThen(
            Func<string> doAfterSaving,
            Action doIfNotInRightStateToSave,
            bool forceFullSave = false,
            bool skipSaveToDisk = false,
            Action failureAction = null
        )
        {
            _nextSaveMustBeFull |= forceFullSave;
            if (
                !_stateMachine.ToSavePending(
                    doAfterSaving,
                    saveActionHandlesSaveBook: skipSaveToDisk,
                    failureAction
                )
            )
                doIfNotInRightStateToSave();
        }

        // Send a request to the browser to send us the page content so we can save it.
        private void RequestBrowserToSave()
        {
            Logger.WriteMinorEvent("EditingModel.RequestSave() starting");
            // show the saving message to the user
            _webSocketServer.SendString("pageThumbnailList", "saving", "");
            // review do we really need to be checking to see if things are loaded? If they are not, then there is nothing to save, and this doesn't thow.
            var script = $"editTabBundle.getEditablePageBundleExports().requestPageContent()";
            _view.Browser.RunJavascriptAsync(script);
        }

        /// <summary>
        /// Called by an API from JavaScript code invoked by RequestBrowserToSave, this receives the body and user-defined
        /// styles of the current page and saves them to the book DOM.
        /// </summary>
        public void ReceivePageContent(string pageContentData)
        {
            _stateMachine.ToSavedAndStripped(pageContentData);
        }

        private XmlElement _modifiedPageElement;

        /// <summary>
        /// Receives a DOM (derived the browser) that combines the body of the document of the page
        /// being edited with the CSS that defines the user-defined styles. It updates the current book DOM
        /// to match whatever the browser has.
        /// </summary>
        public void UpdateBookDomFromBrowserPageContent(XmlDocument docFromBrowser)
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
            _nextSaveMustBeFull = CurrentBook.UpdateDomFromEditedPage(
                HtmlDom.FromDoc(docFromBrowser),
                out _modifiedPageElement,
                _nextSaveMustBeFull || NeedToDoFullSave(newPageData)
            );
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

                /* We're Saving to the DOM here:
                 * 1) Makes it transparent if it should be:
                 *        Cause: Until we have Saved the page, the in-memory DOM doesn't have this as the cover image,
                 *        so the check to see if we need to make it tranparent says "no".
                 *        This could probably be done in a smarter way that isn't occuring to me at the moment.
                 * 2) It is needed if we're going to update the thumbnail (we could live without this)
                 */
                SaveThen(
                    doAfterSaving: () =>
                    {
                        _view.UpdateThumbnailAsync(_pageSelection.CurrentSelection);

                        Logger.WriteMinorEvent(
                            "Finished ChangePicture {0}",
                            (object)imageInfo.FileName
                        );
                        Analytics.Track("Change Picture");
                        Logger.WriteEvent("ChangePicture {0}...", (object)imageInfo.FileName);
                        return _pageSelection.CurrentSelection.Id; // we're not changing pages
                    },
                    doIfNotInRightStateToSave: () => { },
                    forceFullSave: false,
                    skipSaveToDisk: false // we can wait for the normal save to disk
                );
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
                SaveThen(
                    () =>
                    {
                        CurrentBook.SetMetadata(metadata);
                        _pageHasUnsavedDataDerivedChange = true;
                        return _pageSelection.CurrentSelection.Id;
                    },
                    () => { } // wrong state, do nothing
                );
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

        public void CopyPage(IPage page)
        {
            // need to preserve any typing they've done but not yet saved (BL-4512)
            SaveThen(
                () =>
                {
                    // We have to clone this so that if the user changes the page after doing the copy,
                    // when they paste they get the page as it was, not as it is now.
                    _pageDivFromCopyPage = (XmlElement)page.GetDivNodeForThisPage().CloneNode(true);
                    _bookPathFromCopyPage = page.Book.GetPathHtmlFile();
                    return page.Id;
                },
                () => { }, // wrong state, do nothing
                forceFullSave: true
            );
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

        public void HandlePageDomLoadedEvent(string pageId)
        {
            _stateMachine.ToEditing(pageId);
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
                                _currentlyDisplayedBook.GetPageByIndex(pageIndex)
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
                                SavePageAndReloadIt();
                            }
                    );
                }
            }
            _weHaveSeenAJsonChange = false;
        }
    }
}
