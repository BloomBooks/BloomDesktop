using System.Linq;
using Bloom.Api;
using Bloom.Book;
using Bloom.Edit;

namespace Bloom.web.controllers
{
    /// <summary>
    /// The book-wide half of flow text: text that runs past the end of a box on one page and
    /// carries on in a box on a later page.
    ///
    /// The browser owns the page it is editing. It measures, moves text between the boxes of that
    /// page, and records with a span where the text of the last box stops fitting. Only the boxes
    /// on OTHER pages are out of its reach, and that is all these endpoints do: they read and write
    /// the text of a box on a page nobody is editing, and they save that page.
    ///
    /// Two rules hold everywhere here:
    ///  - Never touch the page the user is editing. The browser holds the live version of it, so
    ///    C# writing to it would either be overwritten or would clobber unsaved typing. Where an
    ///    operation has to change the current page, we hand the new content back and the browser
    ///    applies it.
    ///  - Save with Book.SaveForPageChanged, never EditingModel.SaveThen. SaveThen collects the
    ///    page the browser is showing, which is a round trip through the browser and is about the
    ///    current page, which we are not changing.
    /// </summary>
    public class FlowTextApi
    {
        public const string kApiUrlPart = "flowText/";

        private readonly BookSelection _bookSelection;
        private readonly EditingModel _editingModel;

        /// <summary>
        /// Where the caret should go when the next page loads, when the text the user was typing
        /// in moved onto that page. One slot: the caret is one place, and only the most recent
        /// handoff can be the one the user is following.
        /// </summary>
        private PendingCaret _pendingCaret;

        /// <summary>
        /// How many of these handlers are running. e2e/flowText/isIdle reports on it, so that a
        /// test can wait for the cross-page work it cannot see from the page. Handlers run on the
        /// UI thread one at a time, but a test may ask between two of them.
        /// </summary>
        private static int _busyCount;

        public static bool IsIdle => _busyCount == 0;

        public FlowTextApi(BookSelection bookSelection, EditingModel editingModel)
        {
            _bookSelection = bookSelection;
            _editingModel = editingModel;
            // A caret waiting for a page of one book means nothing in another.
            _bookSelection.SelectionChanged += (unused1, unused2) => _pendingCaret = null;
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
            apiHandler.RegisterEndpointHandler(
                kApiUrlPart + "pendingCaret",
                HandlePendingCaret,
                true
            );
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
        /// box's content becomes once text arrives from, or goes back to, the current page.
        /// Replies { pageId: null } when the chain ends on this page.
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

                    var editable = FlowTextChains.GetFlowEditable(next.Group, body.lang);
                    if (editable == null)
                    {
                        request.Failed("The next box of the chain has no box of that language.");
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
                    request.PostSucceeded();
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
        private static void RunHandler(ApiRequest request, System.Action handler)
        {
            _busyCount++;
            try
            {
                handler();
            }
            finally
            {
                _busyCount--;
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
