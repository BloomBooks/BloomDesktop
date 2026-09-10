using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;
using Bloom.MiscUI;
using Bloom.Publish;
using L10NSharp;
using SIL.IO;
using SIL.Reporting;

namespace Bloom.web.controllers
{
    /// <summary>
    /// The book-wide half of flow text: text that runs past the end of a box on one page and
    /// carries on in a box on a later page.
    ///
    /// The browser owns the page it is editing. It measures, moves text between the boxes of that
    /// page, and records with a span where the text of the last box stops fitting. Only the boxes
    /// on OTHER pages are out of its reach, and that is what these endpoints are for: they read
    /// and write the text of a box on a page nobody is editing, and they save that page.
    /// createPagesAndContinue goes one step further and makes the pages a run of text needs.
    ///
    /// Two rules hold everywhere here:
    ///  - Never touch the page the user is editing. The browser holds the live version of it, so
    ///    C# writing to it would either be overwritten or would clobber unsaved typing. Where an
    ///    operation has to change the current page, we hand the new content back and the browser
    ///    applies it.
    ///  - Save a page with Book.SaveForPageChanged, not EditingModel.SaveThen: these handlers
    ///    change pages nobody is editing, and SaveThen is a round trip through the browser for
    ///    the content of the current page. The exception is reflowNow, which asks for exactly
    ///    that round trip, because a walk reads the book's copy of the page being edited.
    /// </summary>
    public class FlowTextApi
    {
        public const string kApiUrlPart = "flowText/";

        private readonly BookSelection _bookSelection;
        private readonly EditingModel _editingModel;
        private readonly PageTemplatesApi _pageTemplatesApi;
        private readonly ITemplateFinder _sourceCollectionsList;

        /// <summary>
        /// Where the caret should go when the next page loads, when the text the user was typing
        /// in moved onto that page. One slot: the caret is one place, and only the most recent
        /// handoff can be the one the user is following.
        /// </summary>
        private PendingCaret _pendingCaret;

        /// <summary>
        /// How many of these handlers are running. e2e/flowText/isIdle reports on it, so that a
        /// test can wait for the cross-page work it cannot see from the page. Most of these
        /// handlers run on the UI thread one at a time, but createPagesAndContinue does not, and
        /// a test may ask between any two of them.
        /// </summary>
        private static int _busyCount;

        /// <summary>
        /// Nothing anywhere in Bloom is moving text between the boxes of a chain: no handler is
        /// running, and no whole-chain walk is running. A walk merely waiting in the queue does
        /// not count, because nothing will run it until the user changes pages or asks for it.
        /// </summary>
        public static bool IsIdle => _busyCount == 0 && !Book.FlowTextWalk.IsBusy;

        public FlowTextApi(
            BookSelection bookSelection,
            EditingModel editingModel,
            PageTemplatesApi pageTemplatesApi,
            ITemplateFinder sourceCollectionsList
        )
        {
            _bookSelection = bookSelection;
            _editingModel = editingModel;
            _pageTemplatesApi = pageTemplatesApi;
            _sourceCollectionsList = sourceCollectionsList;
            // A caret waiting for a page of one book, or content a walk worked out for a page of
            // one book, means nothing in another.
            _bookSelection.SelectionChanged += (unused1, unused2) =>
            {
                _pendingCaret = null;
                FlowTextWalk.ClearRefitResults();
            };
        }

        public class PendingCaret
        {
            public string pageId { get; set; }
            public string chainId { get; set; }
            public string lang { get; set; }
            public int charOffset { get; set; }

            /// <summary>
            /// What the user typed after the caret left their page and before the next one was
            /// showing. The browser puts it in at the caret when it places it. Null or empty
            /// when nothing was typed in between.
            /// </summary>
            public string typedText { get; set; }
        }

        public class ContinueIntoRequest
        {
            public string sourcePageId { get; set; }
            public int sourceIndexInPage { get; set; }
            public string targetPageId { get; set; }
            public int targetIndexInPage { get; set; }
            public string lang { get; set; }
        }

        public class SetNextContentRequest
        {
            public string chainId { get; set; }
            public string afterPageId { get; set; }
            public string lang { get; set; }
            public string html { get; set; }

            /// <summary>
            /// What the browser read out of that box before it worked out this content, so that
            /// the write can be refused if the box has changed since. The content sent is this
            /// page's text and that box's text divided afresh, so writing it over a box that
            /// something else has since refitted would put text back that has moved on.
            /// </summary>
            public string expectedHtml { get; set; }
        }

        public class CreatePagesRequest
        {
            /// <summary>The page being edited, which holds the box the text runs out in.</summary>
            public string pageId { get; set; }
            public int indexInPage { get; set; }
            public string lang { get; set; }

            /// <summary>The chain that box carries, or empty when it is in none yet.</summary>
            public string chainId { get; set; }

            /// <summary>
            /// What that box holds, mark and all. It comes with the request because the browser
            /// owns the page being edited and the book's copy of it is older than what the user
            /// is looking at.
            /// </summary>
            public string html { get; set; }

            /// <summary>
            /// The rules of the browser's userModifiedStyles element, for the pages laid out
            /// off-screen. See the Styles argument of FlowTextWalk.Request.
            /// </summary>
            public string styles { get; set; }
        }

        public class WalkRequest
        {
            /// <summary>
            /// Which chain to refit. Empty means every chain in the book, each from its first
            /// page, which is what a change of paper size calls for.
            /// </summary>
            public string chainId { get; set; }
            public string fromPageId { get; set; }
            public string lang { get; set; }

            /// <summary>
            /// The rules of the browser's userModifiedStyles element. A style change lives in
            /// the page being edited until that page is saved, so the walk is told the rules
            /// rather than reading the older ones the book still holds.
            /// </summary>
            public string styles { get; set; }
        }

        public class ReflowOnPageChangeRequest
        {
            /// <summary>Whether changing pages should run the refitting that is waiting.</summary>
            public bool value { get; set; }
        }

        public class UnlinkFromRequest
        {
            public string chainId { get; set; }
            public string fromPageId { get; set; }
            public int fromIndexInPage { get; set; }
        }

        public void RegisterWithApiHandler(BloomApiHandler apiHandler)
        {
            // Every one of these reads or writes the book's DOM and may save a page, so they all
            // run on the UI thread.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "pendingOverflow",
                HandleGetPendingOverflow,
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "continueInto",
                HandleContinueInto,
                true
            );
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "previous", HandleGetPrevious, true);
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "peekNext", HandlePeekNext, true);
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "setNextContent",
                HandleSetNextContent,
                true
            );
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "unlinkFrom", HandleUnlinkFrom, true);
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "walk", HandleWalk, true);
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "pendingWalks",
                HandleGetPendingWalks,
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "refitResult",
                HandleGetRefitResult,
                true
            );
            apiHandler.RegisterEndpointHandler(kApiUrlPart + "reflowNow", HandleReflowNow, true);
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "reflowOnPageChange",
                HandleReflowOnPageChange,
                true
            );
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "pendingCaret",
                HandlePendingCaret,
                true
            );

            // The one endpoint here that does NOT run on the UI thread, and the one that does not
            // hold the api handler's lock. It makes a page, lays it out off-screen to measure it,
            // and goes round again, which is seconds of work: on the UI thread nothing would
            // paint, and holding the lock would shut out the api requests each off-screen page
            // makes for itself. So it works on the server's own worker thread, which
            // OffScreenBrowser tells the server about (ReportThreadBlocking) so that a blocked
            // worker cannot starve the pool.
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "createPagesAndContinue",
                HandleCreatePagesAndContinue,
                false,
                false
            );
        }

        /// <summary>
        /// POST flowText/createPagesAndContinue: make as many text-only pages as the rest of this
        /// box's text needs, link them into its chain, and divide the text among them. Replies
        /// { chainId, sourceHtml, pagesCreated, lastPageId } once every page is made.
        ///
        /// The box is on the page being edited, so its new content goes back for the browser to
        /// apply; only the pages made here are saved. The page list refreshes itself, because
        /// Book.InsertPageAfter raises pageListChangedEvent once the request is over.
        /// </summary>
        private void HandleCreatePagesAndContinue(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var body = request.RequiredPostObject<CreatePagesRequest>();
                    var book = _bookSelection.CurrentSelection;
                    if (book == null)
                    {
                        request.Failed("No book is selected.");
                        return;
                    }

                    // Nothing else may move this run of text about while the pages are made and
                    // filled one at a time. A walk that gathered the run while some of the new
                    // pages were still empty would divide a run with text missing and write that
                    // over them. The browser leaves the button up when we refuse, so the user can
                    // ask again once the walk has finished.
                    if (!FlowTextWalk.TryClaim())
                    {
                        request.Failed("A whole-chain refit is in progress.");
                        return;
                    }

                    try
                    {
                        MakeThePagesAndReply(request, book, body);
                    }
                    finally
                    {
                        FlowTextWalk.ReleaseClaim();
                    }
                }
            );
        }

        /// <summary>
        /// Make the pages and reply, with the sole right to move this run of text already in hand.
        /// </summary>
        private void MakeThePagesAndReply(
            ApiRequest request,
            Book.Book book,
            CreatePagesRequest body
        )
        {
            var sourceGroup = FindGroupOnPage(book, body.pageId, body.indexInPage);
            if (sourceGroup == null)
            {
                request.Failed("That text box is no longer there.");
                return;
            }

            var sourceEditable = FlowTextChains.GetFlowEditable(sourceGroup.Group, body.lang);
            if (sourceEditable == null)
            {
                request.Failed("That text box has no box of that language.");
                return;
            }

            // The browser owns the page being edited, so what the box holds arrives with
            // the request. The book's own copy is older: it is what was last saved.
            HtmlDom.SetInnerHtmlFromFragment(sourceEditable, body.html);
            if (!string.IsNullOrEmpty(body.chainId))
                sourceGroup.Group.SetAttribute(HtmlDom.kFlowChainAttrName, body.chainId);

            var templatePage = FindJustTextTemplatePage();
            if (templatePage == null)
            {
                request.Failed("Bloom cannot find the Just Text page template.");
                return;
            }

            var result = MakeThePages(book, sourceGroup, templatePage, body);
            if (result == null)
            {
                request.Failed("That text box's text all fits, so it needs no pages.");
                return;
            }

            request.ReplyWithJson(
                new
                {
                    chainId = result.ChainId,
                    sourceHtml = result.SourceHtml,
                    pagesCreated = result.PagesCreated,
                    // Where the run of text now ends. The browser goes there once it has put
                    // the source box's own content in place, so that the author sees it.
                    lastPageId = result.CreatedGroups[result.PagesCreated - 1].PageId,
                }
            );
        }

        /// <summary>
        /// Lay one page out in a browser nobody is looking at and ask where its box's text stops
        /// fitting. This runs exactly once for each page made, which makes it the one place that
        /// knows how far the work has got.
        ///
        /// Each page gets a browser of its own: a renderer that has laid out a page once holds
        /// its scripts and its styles.
        /// </summary>
        private FlowTextWalk.FitResult LayOutOnePage(
            Book.Book book,
            OffScreenBrowser browser,
            FlowTextChains.FlowGroup group,
            CreatePagesRequest body,
            bool isLast,
            int pagesLaidOutBefore
        )
        {
            if (pagesLaidOutBefore > 0)
                browser.StartFreshBrowser();
            return FlowTextWalk.AskBrowserForFit(
                book,
                browser,
                group,
                body.lang,
                body.styles,
                isLast
            );
        }

        /// <summary>
        /// Make the pages, with the Edit tab's progress dialog up: this is seconds of work, and
        /// nothing else must be edited while it runs.
        ///
        /// The dialog runs the work on its own background thread and returns as soon as it has
        /// started, but this request has to reply with the pages once they are made, so it waits
        /// here for the work to finish.
        /// </summary>
        private FlowTextCreatePages.Result MakeThePages(
            Book.Book book,
            FlowTextChains.FlowGroup sourceGroup,
            IPage templatePage,
            CreatePagesRequest body
        )
        {
            var socketServer = BloomWebSocketServer.Instance;
            var shell = Shell.GetShellOrNull();
            if (socketServer == null || shell == null || shell.IsDisposed)
            {
                // There is no browser to show the dialog in, so just do the work.
                return RunTheLoop(book, sourceGroup, templatePage, body, null);
            }

            FlowTextCreatePages.Result result = null;
            Exception failure = null;
            var opened = false;
            using (var finished = new ManualResetEventSlim(false))
            {
                // Opening the dialog is a UI-thread job. The delegate below runs on the dialog's
                // own background thread, not this one.
                FlowTextWalk.InvokeOnUiThread(() =>
                {
                    opened = true;
                    _ = BrowserProgressDialog.DoWorkWithDeterminateProgressDialogAsync(
                        socketServer,
                        BrowserProgressDialog.kEditViewProgressDialogId,
                        LocalizationManager.GetString(
                            "EditTab.FlowText.CreatingPagesProgressTitle",
                            "Creating pages and flowing text"
                        ),
                        progress =>
                        {
                            try
                            {
                                result = RunTheLoop(
                                    book,
                                    sourceGroup,
                                    templatePage,
                                    body,
                                    progress
                                );
                            }
                            catch (Exception e)
                            {
                                failure = e;
                            }
                            finally
                            {
                                finished.Set();
                            }
                            return Task.CompletedTask;
                        }
                    );
                });
                if (!opened)
                {
                    // Bloom's window went away between the check above and the call, so nothing
                    // is going to run the work; do it here instead.
                    return RunTheLoop(book, sourceGroup, templatePage, body, null);
                }
                finished.Wait();
            }

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }

        /// <summary>
        /// Add a page, fill it, and go round again until one page holds what is left, then save
        /// the pages made. Each page made is saved once every page has its share of the text,
        /// because a run divided among pages is right or wrong as a whole.
        /// </summary>
        private FlowTextCreatePages.Result RunTheLoop(
            Book.Book book,
            FlowTextChains.FlowGroup sourceGroup,
            IPage templatePage,
            CreatePagesRequest body,
            IWebSocketProgress progress
        )
        {
            using (var browser = new OffScreenBrowser())
            {
                var afterPageId = sourceGroup.PageId;
                var pagesLaidOut = 0;
                // How much text the first page made held. Nothing knows how many pages the text
                // needs until the last of them holds what is left, so this stands for how much
                // each page holds, and it is that estimate that makes the bar move.
                var heldByFirstPage = 0;
                var result = FlowTextCreatePages.Run(
                    sourceGroup,
                    body.lang,
                    () =>
                    {
                        var added = AddTextOnlyPageAfter(book, afterPageId, templatePage);
                        afterPageId = added.PageId;
                        return added;
                    },
                    (group, isLast) =>
                    {
                        var fitted = LayOutOnePage(
                            book,
                            browser,
                            group,
                            body,
                            isLast,
                            pagesLaidOut++
                        );
                        if (heldByFirstPage == 0)
                            heldByFirstPage = (fitted.head ?? "").Length;
                        ReportPagesMade(
                            progress,
                            pagesLaidOut,
                            (fitted.tail ?? "").Length,
                            heldByFirstPage
                        );
                        return fitted;
                    }
                );
                if (result == null)
                    return null;

                SaveCreatedPages(book, result);
                return result;
            }
        }

        /// <summary>
        /// Say how far the making of pages has got: the pages made against those made plus the
        /// ones the text still in hand is estimated to need. Capped below the end, because the
        /// work is done when the loop ends, not when the estimate is reached.
        /// </summary>
        private static void ReportPagesMade(
            IWebSocketProgress progress,
            int pagesMade,
            int charactersLeft,
            int charactersPerPage
        )
        {
            if (progress == null)
                return;
            var pagesLeft =
                charactersPerPage > 0
                    ? (charactersLeft + charactersPerPage - 1) / charactersPerPage
                    : 0;
            progress.SendPercent(Math.Min(99, pagesMade * 100 / (pagesMade + pagesLeft)));
        }

        /// <summary>
        /// Add one text-only page after this one and hand back the group on it that the text
        /// flows into. This is the Add Page code path: the same InsertPageAfter that the Add Page
        /// dialog uses, with the same template page, so a page made here is a page the author
        /// could have added themselves.
        /// </summary>
        private static FlowTextChains.FlowGroup AddTextOnlyPageAfter(
            Book.Book book,
            string afterPageId,
            IPage templatePage
        )
        {
            FlowTextChains.FlowGroup added = null;
            // Inserting a page rebuilds the book's page cache and copies the template's files, so
            // it belongs on the UI thread with everything else that changes the book's structure.
            FlowTextWalk.InvokeOnUiThread(() =>
            {
                var newPageId = book.InsertPageAfter(
                    FlowTextChains.FindPage(book, afterPageId),
                    templatePage
                );
                added = FindGroupOnPage(book, newPageId, 0);
            });

            if (added == null)
                throw new ApplicationException(
                    "flow text: the text-only page Bloom just made has no text box on it."
                );

            return added;
        }

        /// <summary>
        /// Save each page made and redraw its thumbnail. The pages are ones nobody is editing, so
        /// SaveForPageChanged writes them from the book's own DOM, which is what we changed.
        /// </summary>
        private void SaveCreatedPages(Book.Book book, FlowTextCreatePages.Result result)
        {
            var pageIds = result.CreatedGroups.Select(group => group.PageId).Distinct().ToList();
            FlowTextWalk.InvokeOnUiThread(() =>
            {
                foreach (var pageId in pageIds)
                {
                    var page = FlowTextChains.FindPage(book, pageId);
                    if (page == null)
                        continue;
                    SavePage(book, page);
                    _editingModel.RefreshThumbnail(page);
                }
            });
        }

        /// <summary>
        /// The template page the Add Page dialog calls "Just Text", from the first template book
        /// the dialog would offer this book that holds it. Null when no template book on this
        /// machine has it.
        /// </summary>
        private IPage FindJustTextTemplatePage()
        {
            IPage found = null;
            // Finding a template book reads it off the disk and adds it to the collection's list
            // of source books, so it goes on the UI thread with the rest of that work.
            FlowTextWalk.InvokeOnUiThread(() =>
            {
                foreach (var path in _pageTemplatesApi.GetTemplateBookPathsForAddPage())
                {
                    if (!RobustFile.Exists(path))
                        continue;
                    var templateBook = _sourceCollectionsList.FindAndCreateTemplateBookByFullPath(
                        path
                    );
                    if (templateBook == null)
                        continue;
                    if (
                        templateBook
                            .GetTemplatePagesIdDictionary()
                            .TryGetValue(Book.Book.JustTextGuid, out var page)
                    )
                    {
                        found = page;
                        return;
                    }
                }
            });

            return found;
        }

        /// <summary>
        /// The group at this place on this page of this book, with where it is, or null when the
        /// page or the group is not there.
        /// </summary>
        private static FlowTextChains.FlowGroup FindGroupOnPage(
            Book.Book book,
            string pageId,
            int indexInPage
        )
        {
            var pages = book.GetPages().ToList();
            var pageIndex = pages.FindIndex(page => page.Id == pageId);
            if (pageIndex < 0)
                return null;

            return FlowTextChains
                .GetFlowGroupsOfPage(pages[pageIndex].GetDivNodeForThisPage(), pageId, pageIndex)
                .FirstOrDefault(group => group.IndexInPage == indexInPage);
        }

        /// <summary>
        /// GET flowText/pendingOverflow?beforePageId=&amp;lang=: the nearest box before this page
        /// whose text does not all fit, which is what an empty box on this page offers to
        /// continue. Replies with { pageId: null } when there is none.
        /// </summary>
        private void HandleGetPendingOverflow(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var book = _bookSelection.CurrentSelection;
                    var beforePageId = request.RequiredParam("beforePageId");
                    var lang = request.RequiredParam("lang");
                    var found =
                        book == null
                            ? null
                            : FlowTextChains.FindPendingOverflowBefore(
                                book.OurHtmlDom,
                                beforePageId,
                                lang,
                                pageId => GetPageLabel(book, pageId)
                            );
                    if (found == null)
                    {
                        request.ReplyWithJson(new { pageId = (string)null });
                        return;
                    }

                    request.ReplyWithJson(
                        new
                        {
                            pageId = found.PageId,
                            pageNumber = found.PageNumberLabel,
                            indexInPage = found.IndexInPage,
                            previewText = found.PreviewText,
                        }
                    );
                }
            );
        }

        /// <summary>
        /// GET flowText/previous?chainId=&amp;beforePageId=: the box of this chain that comes just
        /// before the chain's first box on that page, which is where that page's text flows in
        /// from. Replies { pageId, pageNumber }, pageNumber being what the reader calls that page
        /// and possibly empty; or { pageId: null } when the chain starts on that page or is
        /// unknown.
        /// </summary>
        private void HandleGetPrevious(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var chainId = request.RequiredParam("chainId");
                    var beforePageId = request.RequiredParam("beforePageId");
                    var book = _bookSelection.CurrentSelection;
                    var previous = FindPreviousGroup(chainId, beforePageId);
                    if (previous == null)
                    {
                        request.ReplyWithJson(new { pageId = (string)null });
                        return;
                    }

                    request.ReplyWithJson(
                        new
                        {
                            pageId = previous.PageId,
                            pageNumber = GetPageLabel(book, previous.PageId),
                        }
                    );
                }
            );
        }

        /// <summary>
        /// POST flowText/continueInto: join an empty box on the page being edited to an
        /// overflowing box on an earlier page, and move the text that does not fit into it.
        ///
        /// The source page is saved here. The target is the page the user is looking at, so its
        /// new content goes back in the reply for the browser to put in place; C# must not write
        /// the current page under the user. Replies { chainId, targetHtmlByLang }.
        /// </summary>
        private void HandleContinueInto(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var body = request.RequiredPostObject<ContinueIntoRequest>();
                    var book = _bookSelection.CurrentSelection;
                    if (book == null)
                    {
                        request.Failed("No book is selected.");
                        return;
                    }

                    var result = FlowTextChains.ContinueInto(
                        book,
                        body.sourcePageId,
                        body.sourceIndexInPage,
                        body.targetPageId,
                        body.targetIndexInPage,
                        body.lang
                    );
                    if (result == null)
                    {
                        request.Failed("The source or target text box is no longer there.");
                        return;
                    }

                    if (result.MovedAny)
                        _editingModel.RefreshThumbnail(result.SourcePage);

                    request.ReplyWithJson(
                        new { chainId = result.ChainId, targetHtmlByLang = result.TargetHtmlByLang }
                    );
                }
            );
        }

        /// <summary>
        /// GET flowText/peekNext?chainId=&amp;afterPageId=&amp;lang=: what the next box of the
        /// chain, on a later page, holds at the moment. The browser needs it to work out what that
        /// box's content becomes once text arrives from, or goes back to, the current page, and
        /// pageNumber, what the reader calls that page, is what the browser's label says. That can
        /// be empty: not every page has a number. Replies { pageId: null } when the chain ends on
        /// this page.
        /// </summary>
        private void HandlePeekNext(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var chainId = request.RequiredParam("chainId");
                    var afterPageId = request.RequiredParam("afterPageId");
                    var lang = request.RequiredParam("lang");
                    var book = _bookSelection.CurrentSelection;
                    var next = FindNextGroup(chainId, afterPageId);
                    var editable =
                        next == null ? null : FlowTextChains.GetFlowEditable(next.Group, lang);
                    if (editable == null)
                    {
                        request.ReplyWithJson(new { pageId = (string)null });
                        return;
                    }

                    request.ReplyWithJson(
                        new
                        {
                            pageId = next.PageId,
                            pageNumber = GetPageLabel(book, next.PageId),
                            indexInPage = next.IndexInPage,
                            html = editable.InnerXml,
                        }
                    );
                }
            );
        }

        /// <summary>
        /// POST flowText/setNextContent: put this content in the next box of the chain, on the
        /// page after the current one, and save that page. Refuses when the next box is on the
        /// page being edited: the browser owns that page and settles it itself.
        ///
        /// Answers { accepted, walkInProgress }. A refusal that only has to be waited out — a
        /// walk holds the chain, or has moved this box's text on since the browser read it — is
        /// an answer rather than a failure, because the browser retries such a move once the
        /// walk reports it has finished.
        /// </summary>
        private void HandleSetNextContent(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var body = request.RequiredPostObject<SetNextContentRequest>();
                    var book = _bookSelection.CurrentSelection;
                    var next = FindNextGroup(body.chainId, body.afterPageId);
                    if (book == null || next == null)
                    {
                        request.Failed("The chain has no box after that page.");
                        return;
                    }

                    if (next.PageId == _editingModel.CurrentPage?.Id)
                    {
                        request.Failed(
                            "The next box of the chain is on the page being edited, which the browser owns."
                        );
                        return;
                    }

                    if (FlowTextWalk.IsBusy)
                    {
                        // A walk is dividing this very run of text among its boxes, off-screen.
                        // Letting the browser write one of those boxes in the middle of that
                        // would put the same text in two places. The browser keeps the text
                        // where it is, and settles the boundary again when the walk reports it
                        // has finished, so walkInProgress says to wait for that word.
                        request.ReplyWithJson(new { accepted = false, walkInProgress = true });
                        return;
                    }

                    var editable = FlowTextChains.GetFlowEditable(next.Group, body.lang);
                    if (editable == null)
                    {
                        request.Failed("The next box of the chain has no box of that language.");
                        return;
                    }

                    if (body.expectedHtml != null && editable.InnerXml != body.expectedHtml)
                    {
                        // The box holds something else now: a walk has refitted it since the
                        // browser read it. The content offered was worked out from what it held
                        // then, so writing it would undo the refit and put text in two places.
                        // The browser reads the box afresh and tries again.
                        request.ReplyWithJson(
                            new { accepted = false, walkInProgress = FlowTextWalk.IsBusy }
                        );
                        return;
                    }

                    // The browser has already taken this text out of its own box, and keeps it
                    // out only if this call succeeds. So the box here must hold either the new
                    // content saved, or exactly what it held before: a save that fails must not
                    // leave the new content in the book's DOM, or the text would be in two places.
                    var page = FlowTextChains.FindPage(book, next.PageId);
                    var previousContent = editable.InnerXml;
                    HtmlDom.SetInnerHtmlFromFragment(editable, body.html);
                    try
                    {
                        SavePage(book, page);
                    }
                    catch
                    {
                        editable.InnerXml = previousContent;
                        throw;
                    }

                    _editingModel.RefreshThumbnail(page);
                    request.ReplyWithJson(new { accepted = true });
                }
            );
        }

        /// <summary>
        /// POST flowText/unlinkFrom: take this box, and every box of the chain after it, out of
        /// the chain. The text stays where it is. The browser has already done this for the page
        /// it is editing, so only the later pages are changed and saved here.
        /// </summary>
        private void HandleUnlinkFrom(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var body = request.RequiredPostObject<UnlinkFromRequest>();
                    var book = _bookSelection.CurrentSelection;
                    if (book == null)
                    {
                        request.Failed("No book is selected.");
                        return;
                    }

                    var currentPageId = _editingModel.CurrentPage?.Id;
                    var changed = FlowTextChains.UnlinkFrom(
                        book.OurHtmlDom,
                        body.chainId,
                        body.fromPageId,
                        body.fromIndexInPage
                    );
                    foreach (var pageId in changed.Select(group => group.PageId).Distinct())
                    {
                        if (pageId == currentPageId)
                            continue; // The browser has already done this page.
                        var page = FlowTextChains.FindPage(book, pageId);
                        SavePage(book, page);
                        _editingModel.RefreshThumbnail(page);
                    }

                    request.PostSucceeded();
                }
            );
        }

        /// <summary>
        /// POST flowText/walk: ask for a whole chain to be refitted from this page on, so that
        /// the later pages of the chain hold what they would hold if the user had opened each of
        /// them. With no chainId, ask for every chain in the book, each from its first page.
        ///
        /// Nothing is refitted yet. The walk waits in the queue until the user changes pages or
        /// asks for it with Reflow now (flowText/reflowNow), because a walk puts a progress
        /// dialog over the page for seconds.
        /// </summary>
        private void HandleWalk(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var body = request.RequiredPostObject<WalkRequest>();
                    if (string.IsNullOrEmpty(body.chainId))
                        FlowTextWalk.RequestEveryChain(_editingModel);
                    else
                        FlowTextWalk.Request(
                            _editingModel,
                            body.chainId,
                            body.fromPageId,
                            body.lang,
                            body.styles
                        );
                    request.PostSucceeded();
                }
            );
        }

        /// <summary>
        /// GET flowText/pendingWalks: what refitting is waiting to be done, which is what the
        /// Reflow now button is offered for. Replies { pending, chainIds, reflowOnPageChange },
        /// reflowOnPageChange being whether changing pages will run it (the book's own setting).
        /// </summary>
        private void HandleGetPendingWalks(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                    request.ReplyWithJson(
                        new
                        {
                            pending = FlowTextWalk.HasPending,
                            chainIds = FlowTextWalk.PendingChainIds,
                            reflowOnPageChange = CurrentUserPrefs?.FlowTextReflowOnPageChange
                                ?? true,
                        }
                    )
            );
        }

        /// <summary>
        /// POST flowText/reflowNow: run the refitting that is waiting, now, rather than at the
        /// next page change. Returns as soon as it has started; the work runs on a background
        /// thread behind the Edit tab's progress dialog.
        ///
        /// The page being edited is saved first, because a walk covers that page and reads the
        /// book's copy of it, which is otherwise older than what the user is looking at.
        /// </summary>
        private void HandleReflowNow(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    _editingModel.SaveThen(
                        () => _editingModel.CurrentPage.Id,
                        () =>
                            // A save or a navigation is already in flight, so the page cannot be
                            // collected from the browser at this moment. The button stays up for
                            // the user to press again.
                            Logger.WriteEvent(
                                "flow text: reflowNow came while Bloom was not in a state to save the page."
                            ),
                        doAfterSaveToDisk: () => FlowTextWalk.RunPending(_editingModel)
                    );
                    request.PostSucceeded();
                }
            );
        }

        /// <summary>
        /// GET flowText/refitResult?pageId=: what a walk made of the boxes on this page, which is
        /// the page being edited, and forget it. C# saved the page but cannot reload the browser's
        /// copy of it, so the browser puts this content in place itself when it hears a walk has
        /// finished. Replies { boxes: [ { chainId, lang, indexInPage, html } ] }, the list empty
        /// when the walk changed nothing on this page.
        /// </summary>
        private void HandleGetRefitResult(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var pageId = request.RequiredParam("pageId");
                    request.ReplyWithJson(new { boxes = FlowTextWalk.TakeRefitResults(pageId) });
                }
            );
        }

        /// <summary>
        /// GET flowText/reflowOnPageChange: whether changing pages runs the refitting that is
        /// waiting, which is this book's own setting. Replies { value }.
        /// POST flowText/reflowOnPageChange with { value }: set it.
        /// </summary>
        private void HandleReflowOnPageChange(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    var prefs = CurrentUserPrefs;
                    if (request.HttpMethod == HttpMethods.Post)
                    {
                        var body = request.RequiredPostObject<ReflowOnPageChangeRequest>();
                        if (prefs != null)
                            prefs.FlowTextReflowOnPageChange = body.value;
                        request.PostSucceeded();
                        return;
                    }

                    request.ReplyWithJson(
                        new { value = prefs?.FlowTextReflowOnPageChange ?? true }
                    );
                }
            );
        }

        /// <summary>
        /// The selected book's preferences, or null when no book is selected.
        /// </summary>
        private UserPrefs CurrentUserPrefs => _bookSelection.CurrentSelection?.UserPrefs;

        /// <summary>
        /// POST flowText/pendingCaret: remember where the caret should go when a page loads,
        /// because the text the user was typing in has just moved onto it.
        /// GET flowText/pendingCaret?pageId=: hand back what was remembered for this page and
        /// forget it, or { pageId: null }. Reading it clears it, so a caret is placed once.
        /// </summary>
        private void HandlePendingCaret(ApiRequest request)
        {
            RunHandler(
                request,
                () =>
                {
                    if (request.HttpMethod == HttpMethods.Post)
                    {
                        _pendingCaret = request.RequiredPostObject<PendingCaret>();
                        request.PostSucceeded();
                        return;
                    }

                    var pageId = request.RequiredParam("pageId");
                    var waiting = _pendingCaret;
                    if (waiting == null || waiting.pageId != pageId)
                    {
                        request.ReplyWithJson(new { pageId = (string)null });
                        return;
                    }

                    _pendingCaret = null;
                    request.ReplyWithJson(waiting);
                }
            );
        }

        /// <summary>
        /// Run one handler, counting it as work in progress so that a test can wait for it.
        /// </summary>
        private static void RunHandler(ApiRequest request, Action handler)
        {
            // createPagesAndContinue runs on a server worker thread rather than the UI thread,
            // so two of these can be counted at once.
            Interlocked.Increment(ref _busyCount);
            try
            {
                handler();
            }
            finally
            {
                Interlocked.Decrement(ref _busyCount);
            }
        }

        /// <summary>
        /// The group of the chain that comes after the one on this page, wherever it is. The order
        /// of a chain is book page order, so this is the box the text of that page runs on into.
        /// </summary>
        private FlowTextChains.FlowGroup FindNextGroup(string chainId, string afterPageId)
        {
            var book = _bookSelection.CurrentSelection;
            if (book == null)
                return null;

            var groups = FlowTextChains.GetChainGroups(book.OurHtmlDom, chainId);
            var lastOnThatPage = groups.FindLastIndex(group => group.PageId == afterPageId);
            if (lastOnThatPage < 0 || lastOnThatPage + 1 >= groups.Count)
                return null;

            return groups[lastOnThatPage + 1];
        }

        /// <summary>
        /// The group of the chain that comes just before the chain's FIRST group on this page.
        /// That is the box whose text runs on into this page, so it is the one the label names.
        /// Null when the chain begins on this page, or when nothing carries that chain id.
        /// </summary>
        private FlowTextChains.FlowGroup FindPreviousGroup(string chainId, string beforePageId)
        {
            var book = _bookSelection.CurrentSelection;
            if (book == null)
                return null;

            var groups = FlowTextChains.GetChainGroups(book.OurHtmlDom, chainId);
            var firstOnThatPage = groups.FindIndex(group => group.PageId == beforePageId);
            if (firstOnThatPage <= 0)
                return null;

            return groups[firstOnThatPage - 1];
        }

        private static void SavePage(Book.Book book, IPage page)
        {
            if (page == null)
                return;

            book.SaveForPageChanged(page.Id, page.GetDivNodeForThisPage());
        }

        /// <summary>
        /// What the reader calls this page: its page number, or its caption when it has none.
        /// </summary>
        private static string GetPageLabel(Book.Book book, string pageId)
        {
            var pageNumber = 0;
            foreach (var page in book.GetPages())
            {
                var caption = page.GetCaptionOrPageNumber(ref pageNumber, out var captionI18nId);
                if (page.Id == pageId)
                    return caption;
            }

            return "";
        }
    }
}
