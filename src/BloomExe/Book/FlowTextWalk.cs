using System;
using System.Collections.Generic;
using System.Linq;
using Bloom.Api;
using Bloom.Edit;
using Bloom.MiscUI;
using Bloom.Publish;
using Bloom.SafeXml;
using Bloom.web;
using L10NSharp;
using Newtonsoft.Json;
using SIL.Reporting;

namespace Bloom.Book
{
    /// <summary>
    /// Refitting a whole chain of linked text boxes without the user visiting its pages.
    ///
    /// The browser edits one page and can measure only that page, so on its own a run of text
    /// settles only where the user has been: change the paper size or the style and every later
    /// page of a chain still holds what it held at the old size. This walks the chain instead.
    /// It gathers the run of text the chain carries, then for each page in turn loads that page
    /// into a throwaway off-screen browser with the whole of the remaining run in the box, asks
    /// the browser where that box's text stops fitting (captureFlowFit in
    /// bookEdit/flowText/flowCaptureFit.ts), keeps that much, and carries the rest to the next
    /// box. The last box keeps whatever is left, with the mark that says where its text ran out.
    ///
    /// It is the same division the browser makes for the page being edited
    /// (splitCombinedAcrossPages), made by the same code in a browser nobody is looking at, so a
    /// page refitted here holds what it would hold if the user had opened it.
    ///
    /// A walk never writes the page being edited, for the same reason nothing else in flow text
    /// does: the browser holds the live version of that page. So a walk covers the groups from
    /// its start page up to the page being edited, and the browser settles that page itself,
    /// asking for a walk of what follows whenever its own text crosses the boundary.
    ///
    /// One walk runs at a time, on a background thread, with the Edit tab's progress dialog up
    /// so that nothing is edited underneath it. A request that arrives while a walk is running
    /// waits, and a second request for a chain already waiting starts from the earlier of the
    /// two pages, because everything from there on has to be refitted anyway.
    /// </summary>
    public static class FlowTextWalk
    {
        /// <summary>What an empty box holds: one paragraph with nothing in it but a line break.</summary>
        public const string kPlaceholderParagraph = "<p><br /></p>";

        // The zero-width characters that are no part of what a reader sees: the mark's own
        // character (U+200C) and the filler the editor keeps at the end of a paragraph (U+200B).
        private static readonly char[] kInvisibleCharacters = { (char)0x200b, (char)0x200c };

        private const int kFitTimeoutMs = 30000;

        // Says of a page loaded only to be measured that its own passes must not run. The
        // browser reads it in bookEdit/flowText/flowConstants.ts, so the two must agree.
        private const string kMeasuringFlowFitAttrName = "data-bloom-measuring-flow-fit";

        // The classes the browser's overflow checker puts on a box that holds more text than
        // fits it, on a box pushed outside its container, and on a page that holds either.
        private const string kOverflowClass = "overflow";
        private const string kThisOverflowingParentClass = "thisOverflowingParent";
        private const string kPageOverflowsClass = "pageOverflows";

        /// <summary>
        /// The two halves of one box's text, as the browser divided them: what fits in the box,
        /// and what has to go on to the next box of the chain.
        /// </summary>
        public class FitResult
        {
            /// <summary>The new content of this box.</summary>
            public string head { get; set; }

            /// <summary>
            /// The content for the next box, its first paragraph carrying the continuation
            /// markers when this box's last paragraph was divided. Empty when all of the text
            /// fits in this box.
            /// </summary>
            public string tail { get; set; }
        }

        /// <summary>
        /// Where a box's text carries on a paragraph that began in the box before it. A walk
        /// that starts in the middle of a chain has to put these back on the box it starts at:
        /// the paragraph it continues is on a page the walk never touches.
        /// </summary>
        public class ContinuationMarkers
        {
            public bool IsContinuation;
            public bool HasSeamSpace;
        }

        /// <summary>
        /// Divide a run of text among the boxes of a chain, from startIndex to the end.
        ///
        /// The fit is asked of the caller, once per box, because only a browser can answer it:
        /// it is given the group, the whole of the run that is left (which is already in the
        /// group's box, so the caller can lay the page out), and whether this is the last box.
        /// Returns the groups whose box changed, for the caller to save.
        /// </summary>
        public static List<FlowTextChains.FlowGroup> Distribute(
            List<FlowTextChains.FlowGroup> groups,
            int startIndex,
            string lang,
            Func<FlowTextChains.FlowGroup, bool, FitResult> fit
        )
        {
            var changed = new List<FlowTextChains.FlowGroup>();
            var boxes = Enumerable
                .Range(startIndex, groups.Count - startIndex)
                .Where(index => FlowTextChains.GetFlowEditable(groups[index].Group, lang) != null)
                .ToList();
            if (boxes.Count == 0)
                return changed;

            var startEditable = FlowTextChains.GetFlowEditable(groups[boxes[0]].Group, lang);
            var keep = ReadContinuationMarkers(startEditable);
            var run = CollectRun(groups, startIndex, lang);
            var wordsOfRun = ComparableWords(run.InnerText);
            var remaining = run.InnerXml;
            // Somewhere to put a piece of content when we need to ask a question about it
            // rather than write it into a box.
            var scratch = groups[startIndex].Group.OwnerDocument.CreateElement("div");

            for (var position = 0; position < boxes.Count; position++)
            {
                var group = groups[boxes[position]];
                var editable = FlowTextChains.GetFlowEditable(group.Group, lang);
                var before = editable.InnerXml;

                if (!HoldsAnyText(scratch, remaining))
                {
                    // The run ran out earlier in the chain, so this box and every box after it
                    // is empty. There is nothing to measure, so no browser is needed.
                    WritePlaceholder(editable);
                }
                else
                {
                    // The box holds the whole of what is left while the browser measures it:
                    // the fit is a question about this box's layout with this text in it.
                    HtmlDom.SetInnerHtmlFromFragment(editable, remaining);
                    var result = fit(group, position == boxes.Count - 1);
                    HtmlDom.SetInnerHtmlFromFragment(editable, result.head);
                    remaining = result.tail ?? "";
                }

                if (position == 0)
                    WriteContinuationMarkers(editable, keep);

                // Every box but the last of the run hands the text that does not fit it on to
                // the box after it, so it is not overfull however full it looks. The last box
                // keeps what is left, and the mark the fit left in it is what says its text ran
                // out. So the page's own overflow warning follows from the mark alone.
                var overfull =
                    position == boxes.Count - 1 && FindOverflowMarkers(editable).Count > 0;
                var markingChanged = !overfull && ClearOverflowMarking(editable);
                if (editable.InnerXml != before || markingChanged)
                    changed.Add(group);
            }

            // The run is the whole of what the chain carries from here on, and it has just been
            // written to several pages. A word dropped or repeated between two of them is a
            // word dropped or repeated in the book, so say so rather than saving it.
            var wordsNow = ComparableWords(CollectRun(groups, startIndex, lang).InnerText);
            if (wordsNow != wordsOfRun)
                throw new ApplicationException(
                    "flow text: refitting the chain changed its text. It carried "
                        + $"{wordsOfRun.Length} characters and now carries {wordsNow.Length}. "
                        + $"They first differ at {FirstDifference(wordsOfRun, wordsNow)}."
                );

            return changed;
        }

        /// <summary>
        /// The words of a run of text: what a reader sees, with the characters that only mark
        /// where text was divided left out, so that two ways of writing the same run compare
        /// equal.
        /// </summary>
        internal static string ComparableWords(string text)
        {
            var words = (text ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Select(word => word.Trim(kInvisibleCharacters))).Trim();
        }

        /// <summary>Where two runs of text first differ, with a little of each around it.</summary>
        private static string FirstDifference(string wanted, string got)
        {
            var at = 0;
            while (at < wanted.Length && at < got.Length && wanted[at] == got[at])
                at++;
            Func<string, string> around = text =>
                text.Substring(at, Math.Min(40, Math.Max(0, text.Length - at)));
            return $"character {at}: expected \"{around(wanted)}\", got \"{around(got)}\"";
        }

        /// <summary>
        /// Leave this box holding nothing but the placeholder paragraph an empty box holds. It
        /// is built as elements rather than parsed from kPlaceholderParagraph, because the HTML
        /// parser drops a line break that is all a paragraph holds.
        /// </summary>
        private static void WritePlaceholder(SafeXmlElement editable)
        {
            while (editable.FirstChild != null)
                editable.RemoveChild(editable.FirstChild);

            var paragraph = editable.OwnerDocument.CreateElement("p");
            paragraph.AppendChild(editable.OwnerDocument.CreateElement("br"));
            editable.AppendChild(paragraph);
        }

        /// <summary>
        /// The run of one language's text through the chain from startIndex on, as the children
        /// of a detached element: every box's content in chain order, with each continuation
        /// paragraph joined back onto the paragraph it broke off from, and the marks that say
        /// where a box's text stopped fitting taken out. That is one run of text again, which is
        /// what the walk divides afresh.
        /// </summary>
        public static SafeXmlElement CollectRun(
            List<FlowTextChains.FlowGroup> groups,
            int startIndex,
            string lang
        )
        {
            var container = groups[startIndex].Group.OwnerDocument.CreateElement("div");
            for (var index = startIndex; index < groups.Count; index++)
            {
                var editable = FlowTextChains.GetFlowEditable(groups[index].Group, lang);
                if (editable != null)
                    AppendBoxContent(container, editable);
            }

            foreach (var mark in FindOverflowMarkers(container))
                mark.ParentNode.RemoveChild(mark);

            return container;
        }

        /// <summary>
        /// Does this box's first paragraph carry on a paragraph that began in the box before it,
        /// and was a space cut at the join?
        /// </summary>
        public static ContinuationMarkers ReadContinuationMarkers(SafeXmlElement editable)
        {
            var first = FlowTextChains.GetTopLevelParagraphs(editable).FirstOrDefault();
            return new ContinuationMarkers
            {
                IsContinuation =
                    first != null && first.HasAttribute(FlowTextChains.kContinuationAttrName),
                HasSeamSpace =
                    first != null && first.HasAttribute(FlowTextChains.kSeamSpaceAttrName),
            };
        }

        /// <summary>
        /// Put those markers back on the box's first paragraph, or take them off when the
        /// paragraph continues nothing.
        /// </summary>
        public static void WriteContinuationMarkers(
            SafeXmlElement editable,
            ContinuationMarkers markers
        )
        {
            var first = FlowTextChains.GetTopLevelParagraphs(editable).FirstOrDefault();
            if (first == null)
                return;

            if (markers.IsContinuation)
                first.SetAttribute(FlowTextChains.kContinuationAttrName, "true");
            else
                first.RemoveAttribute(FlowTextChains.kContinuationAttrName);

            if (markers.IsContinuation && markers.HasSeamSpace)
                first.SetAttribute(FlowTextChains.kSeamSpaceAttrName, "true");
            else
                first.RemoveAttribute(FlowTextChains.kSeamSpaceAttrName);
        }

        /// <summary>
        /// Add one box's content to the end of the run, joining its first paragraph onto the
        /// paragraph it broke off from when it is that paragraph's tail.
        /// </summary>
        private static void AppendBoxContent(SafeXmlElement container, SafeXmlElement editable)
        {
            if (FlowTextChains.HoldsOnlyPlaceholder(editable))
                return;

            var lastBefore = FlowTextChains.GetTopLevelParagraphs(container).LastOrDefault();
            var added = new List<SafeXmlNode>();
            foreach (var child in editable.ChildNodes)
            {
                var clone = container.OwnerDocument.ImportNode(child, true);
                container.AppendChild(clone);
                added.Add(clone);
            }

            var firstAdded = added
                .OfType<SafeXmlElement>()
                .FirstOrDefault(node => node.Name == "p");
            FlowTextChains.JoinContinuationOnto(lastBefore, firstAdded);
        }

        private static List<SafeXmlElement> FindOverflowMarkers(SafeXmlElement element)
        {
            return element
                .SafeSelectNodes($".//span[contains(@class,'{HtmlDom.kOverflowStartClass}')]")
                .OfType<SafeXmlElement>()
                .ToList();
        }

        /// <summary>
        /// Take the overflow warning off a box whose text fits it, and off its page when no box
        /// on the page holds more than fits any more. Returns true when a class changed, so that
        /// the caller saves the page.
        ///
        /// A page thumbnail shows its warning triangle for the pageOverflows class on the page,
        /// and the browser puts both classes on (OverflowChecker) while a page is being edited.
        /// A page refitted here is not the page being edited, so nothing else will correct what
        /// it says: a box that has just handed its extra text on to the next page would go on
        /// claiming to be overfull for as long as the reader stays away from its page.
        ///
        /// Only the removal belongs here. Adding the warning is the browser's, which alone
        /// measures the page and knows which page sizes scroll rather than report an overflow.
        /// </summary>
        public static bool ClearOverflowMarking(SafeXmlElement editable)
        {
            var boxWas = editable.HasClass(kOverflowClass);
            editable.RemoveClass(kOverflowClass);

            var page = editable.ParentWithClass("bloom-page");
            if (page == null || !page.HasClass(kPageOverflowsClass))
                return boxWas;

            // The same rule the browser uses (OverflowChecker.UpdatePageOverflow): the page
            // complains while any box on it holds more text than fits, or is pushed past its
            // container, and stops as soon as none does.
            var stillOverfull =
                SafeXmlElement.GetAllDivsWithClass(page, kOverflowClass).Length > 0
                || SafeXmlElement.GetAllDivsWithClass(page, kThisOverflowingParentClass).Length > 0;
            if (stillOverfull)
                return boxWas;

            page.RemoveClass(kPageOverflowsClass);
            return true;
        }

        /// <summary>
        /// Is there anything in this content a reader would see? An empty box's placeholder
        /// paragraph and the invisible characters the editor leaves behind are not text.
        /// </summary>
        private static bool HoldsAnyText(SafeXmlElement scratch, string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            HtmlDom.SetInnerHtmlFromFragment(scratch, html);
            return FlowTextChains.HasVisibleText(scratch);
        }

        #region Running a walk

        private class PendingWalk
        {
            public string ChainId;
            public string FromPageId;
            public int FromPageIndex;
            public string Lang;

            /// <summary>
            /// The style rules the browser has for the page being edited, or null when the
            /// request did not come from a browser. See the Styles argument of Request.
            /// </summary>
            public string Styles;
        }

        private static readonly object _lock = new object();

        // One entry per chain and language: a second request for the same box of work starts
        // from the earlier of the two pages rather than being queued twice.
        private static readonly Dictionary<string, PendingWalk> _pending =
            new Dictionary<string, PendingWalk>();
        private static bool _running;

        /// <summary>
        /// Is a walk running or waiting to run? e2e/flowText/isIdle reports on this through
        /// FlowTextApi.IsIdle, so that a test can wait for pages it cannot see.
        /// </summary>
        public static bool IsBusy
        {
            get
            {
                lock (_lock)
                    return _running || _pending.Count > 0;
            }
        }

        /// <summary>
        /// Take the sole right to move a chain's text between its boxes, or return false because
        /// something else holds it. While it is held no walk starts: a walk asked for in the
        /// meantime waits, IsBusy says Bloom is busy, and setNextContent refuses the browser's
        /// own move, exactly as during a walk.
        ///
        /// Making the pages a run of text needs (FlowTextCreatePages) holds this for the whole of
        /// its work. It fills the pages it makes one at a time, and a walk that gathered the run
        /// while some of them were still empty would divide a run with text missing and write the
        /// result over the pages, which is text lost.
        /// </summary>
        internal static bool TryClaim()
        {
            lock (_lock)
            {
                if (_running)
                    return false;
                _running = true;
                return true;
            }
        }

        /// <summary>
        /// Give the claim back, and run whatever walk was asked for while it was held.
        /// </summary>
        internal static void ReleaseClaim(EditingModel model)
        {
            lock (_lock)
                _running = false;
            StartIfIdle(model);
        }

        /// <summary>
        /// Refit this chain from this page on. Returns at once: the walk runs on a background
        /// thread, after any walk already running.
        ///
        /// styles is the content of the browser's userModifiedStyles element, which the walk
        /// puts into each page it lays out off-screen. The browser writes a style change into
        /// the page it is editing and the book learns of it only when that page is saved, so a
        /// walk asked for by the browser has to be told, or it would measure the pages with the
        /// rules the change replaced. Pass null to measure with the rules the book holds.
        /// </summary>
        public static void Request(
            EditingModel model,
            string chainId,
            string fromPageId,
            string lang,
            string styles
        )
        {
            var book = model?.CurrentBook;
            if (
                book == null
                || string.IsNullOrEmpty(chainId)
                || string.IsNullOrEmpty(fromPageId)
                || string.IsNullOrEmpty(lang)
            )
                return;

            var groups = FlowTextChains.GetChainGroups(book.OurHtmlDom, chainId);
            var start = groups.FindIndex(group => group.PageId == fromPageId);
            if (start < 0)
                return;

            Enqueue(chainId, fromPageId, groups[start].PageIndex, lang, styles);
            StartIfIdle(model);
        }

        /// <summary>
        /// Refit every chain the browser cannot reach, from its first page, in the book's main
        /// language. This is for a change that alters where the text breaks on every page at
        /// once, such as a new paper size: a chain the user is nowhere near still has to be
        /// refitted.
        ///
        /// A chain with a box on the page being edited is left out. The browser holds that page
        /// and settles it as soon as it is rebuilt at the new size, and asks for a walk of what
        /// follows it when its own text crosses the boundary (settleCrossPageBoundary). Walking
        /// it from here would run at the same time as that, and a walk in progress refuses the
        /// browser's move, which would leave the page being edited holding text that no longer
        /// fits it.
        /// </summary>
        public static void RequestEveryChain(EditingModel model)
        {
            var book = model?.CurrentBook;
            if (book == null)
                return;

            var lang = book.Language1Tag;
            if (string.IsNullOrEmpty(lang))
                return;

            var currentPageId = model.CurrentPage?.Id;
            foreach (var chainId in GetChainIds(book.OurHtmlDom))
            {
                var groups = FlowTextChains.GetChainGroups(book.OurHtmlDom, chainId);
                // One box on its own is not a chain: it has nowhere to send its extra text.
                if (groups.Count < 2)
                    continue;
                if (groups.Any(group => group.PageId == currentPageId))
                    continue;
                Enqueue(chainId, groups[0].PageId, groups[0].PageIndex, lang, null);
            }

            StartIfIdle(model);
        }

        /// <summary>Every chain id carried by a group of this book, once each.</summary>
        public static List<string> GetChainIds(HtmlDom dom)
        {
            return dom
                .RawDom.SafeSelectNodes(
                    $"//div[contains(@class,'bloom-translationGroup')][@{HtmlDom.kFlowChainAttrName}]"
                )
                .OfType<SafeXmlElement>()
                .Select(group => group.GetAttribute(HtmlDom.kFlowChainAttrName))
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
        }

        private static void Enqueue(
            string chainId,
            string pageId,
            int pageIndex,
            string lang,
            string styles
        )
        {
            lock (_lock)
            {
                var key = chainId + "|" + lang;
                if (
                    _pending.TryGetValue(key, out var already)
                    && already.FromPageIndex <= pageIndex
                )
                {
                    // Already going to start at that page or an earlier one. The rules are
                    // still worth having: they are the newest word on how the text is drawn.
                    already.Styles = styles ?? already.Styles;
                    return;
                }

                _pending[key] = new PendingWalk
                {
                    ChainId = chainId,
                    FromPageId = pageId,
                    FromPageIndex = pageIndex,
                    Lang = lang,
                    Styles = styles,
                };
            }
        }

        private static void StartIfIdle(EditingModel model)
        {
            lock (_lock)
            {
                if (_running || _pending.Count == 0)
                    return;
                _running = true;
            }

            var socketServer = BloomWebSocketServer.Instance;
            var shell = Shell.GetShellOrNull();
            if (socketServer == null || shell == null || shell.IsDisposed)
            {
                // There is no browser to show the dialog in: Bloom is going away, or this is a
                // unit test driving the queue with no UI at all. The walks still have to run,
                // or IsBusy would say Bloom was busy for ever.
                System.Threading.Tasks.Task.Run(() => RunQueue(model, null));
                return;
            }

            // Opening the dialog is a UI-thread job; the work then runs on the dialog's own
            // background thread, which is what leaves the UI free to paint the bar. The walk
            // drives its WebView2 on the OffScreenBrowser's own thread and just blocks on it, so
            // it must not be the UI thread.
            InvokeOnUiThread(() =>
            {
                // The returned task completes when the work has been started, not when it is
                // done, so there is nothing here to wait for.
                _ = BrowserProgressDialog.DoWorkWithDeterminateProgressDialogAsync(
                    socketServer,
                    BrowserProgressDialog.kEditViewProgressDialogId,
                    LocalizationManager.GetString(
                        "EditTab.FlowText.RefittingProgressTitle",
                        "Flowing text across pages"
                    ),
                    progress =>
                    {
                        RunQueue(model, progress);
                        return System.Threading.Tasks.Task.CompletedTask;
                    }
                );
            });
        }

        private static PendingWalk TakeNext()
        {
            lock (_lock)
            {
                var next = _pending.Values.OrderBy(walk => walk.FromPageIndex).FirstOrDefault();
                if (next == null)
                {
                    _running = false;
                    return null;
                }

                _pending.Remove(next.ChainId + "|" + next.Lang);
                return next;
            }
        }

        /// <summary>
        /// How far the queue has got, as the fraction of the boxes its walks have to fit. More
        /// walks can be queued while one is running, so the total grows as they are taken up.
        ///
        /// A walk whose run of text ends before its last box asks the browser about fewer boxes
        /// than were counted for it, so the bar can stop short of the end. The dialog closes
        /// when the work returns, not when the bar fills.
        /// </summary>
        private class QueueProgress
        {
            private readonly IWebSocketProgress _progress;
            private int _toFit;
            private int _fitted;

            public QueueProgress(IWebSocketProgress progress)
            {
                _progress = progress;
            }

            /// <summary>Another walk's boxes have joined the work.</summary>
            public void AddBoxesToFit(int count)
            {
                _toFit += count;
                Report();
            }

            /// <summary>One more box has been fitted.</summary>
            public void BoxFitted()
            {
                _fitted++;
                Report();
            }

            private void Report()
            {
                if (_progress == null || _toFit == 0)
                    return;
                _progress.SendPercent(Math.Min(100, _fitted * 100 / _toFit));
            }
        }

        private static void RunQueue(EditingModel model, IWebSocketProgress progress)
        {
            var queueProgress = new QueueProgress(progress);
            try
            {
                using (var browser = new OffScreenBrowser())
                {
                    PendingWalk walk;
                    while ((walk = TakeNext()) != null)
                    {
                        var groups = GroupsToFit(model, walk, out var start);
                        if (groups == null)
                            continue;
                        queueProgress.AddBoxesToFit(groups.Count - start);
                        RunOneWalk(model, browser, walk, groups, start, queueProgress);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.WriteError("flow text: refitting a chain failed", e);
                lock (_lock)
                {
                    // Nothing is going to run these now, and leaving them queued would make
                    // Bloom look busy for ever to anything watching IsBusy.
                    _pending.Clear();
                    _running = false;
                }
            }
        }

        /// <summary>
        /// The run of groups this walk covers, and where in it the walk starts. Null when there
        /// is nothing for the walk to fit.
        ///
        /// A walk never writes the page being edited: the browser holds the live version of that
        /// page, so writing it here would either be overwritten or would clobber unsaved typing.
        /// So the walk covers the run of groups from its start page up to the page being edited,
        /// and the browser settles that page itself, asking for a walk of what follows it when
        /// its own text moves (settleCrossPageBoundary).
        /// </summary>
        private static List<FlowTextChains.FlowGroup> GroupsToFit(
            EditingModel model,
            PendingWalk walk,
            out int startIndex
        )
        {
            startIndex = 0;
            var book = model?.CurrentBook;
            if (book == null)
                return null;

            var groups = FlowTextChains.GetChainGroups(book.OurHtmlDom, walk.ChainId);
            var start = groups.FindIndex(group => group.PageId == walk.FromPageId);
            if (start < 0)
                return null;

            var currentPageId = model.CurrentPage?.Id;
            while (start < groups.Count && groups[start].PageId == currentPageId)
                start++;
            var end = start;
            while (end < groups.Count && groups[end].PageId != currentPageId)
                end++;
            if (end <= start)
                return null;

            startIndex = start;
            return groups.GetRange(0, end);
        }

        private static void RunOneWalk(
            EditingModel model,
            OffScreenBrowser browser,
            PendingWalk walk,
            List<FlowTextChains.FlowGroup> groups,
            int start,
            QueueProgress queueProgress
        )
        {
            var book = model.CurrentBook;
            var pagesLoaded = 0;
            var changed = Distribute(
                groups,
                start,
                walk.Lang,
                (group, isLast) =>
                {
                    if (pagesLoaded++ > 0)
                        browser.StartFreshBrowser();
                    var fitted = AskBrowserForFit(
                        book,
                        browser,
                        group,
                        walk.Lang,
                        walk.Styles,
                        isLast
                    );
                    queueProgress.BoxFitted();
                    return fitted;
                }
            );
            if (changed.Count == 0)
                return;

            var changedPageIds = changed.Select(group => group.PageId).Distinct().ToList();
            InvokeOnUiThread(() =>
            {
                foreach (var pageId in changedPageIds)
                {
                    var page = FlowTextChains.FindPage(book, pageId);
                    if (page == null)
                        continue;
                    book.SaveForPageChanged(page.Id, page.GetDivNodeForThisPage());
                    model.RefreshThumbnail(page);
                }
            });
        }

        /// <summary>
        /// Load this page into a throwaway off-screen browser and ask it where the box's text
        /// stops fitting. The box already holds the whole of the run that is left.
        /// </summary>
        internal static FitResult AskBrowserForFit(
            Book book,
            OffScreenBrowser browser,
            FlowTextChains.FlowGroup group,
            string lang,
            string styles,
            bool isLast
        )
        {
            var page = FlowTextChains.FindPage(book, group.PageId);
            if (page == null)
                throw new ApplicationException(
                    $"flow text: the book has no page {group.PageId} to refit."
                );

            var dom = book.GetEditableHtmlDomForPage(page);
            dom.BaseForRelativePaths = book.FolderPath;
            ApplyStyles(dom, styles);
            // The box is about to be given the whole of the run that is left, which is more text
            // than fits it on purpose. So the page's own passes must leave it alone: they settle
            // a page by moving text into the box after it, and here that box is on a page this
            // document does not hold (flowTrigger.setupFlowText).
            dom.Body.SetAttribute(kMeasuringFlowFitAttrName, "true");
            // Off-screen measuring never types into the page, so CKEditor is dead weight: a
            // third of a megabyte of script to load into each fresh renderer, and none of it
            // changes how the text is laid out.
            foreach (var script in dom.SafeSelectNodes("//script[contains(@src,'ckeditor')]"))
                script.ParentNode?.RemoveChild(script);

            browser.NavigateWithoutWaitingForLoad(dom, InMemoryHtmlFileSource.Frame);
            BookProcessor.WaitForJavascriptResult(
                browser,
                "(window.__bloomEditablePageReady && window.editablePageBundle) ? 'ready' : ''",
                "the editing bundle to initialize",
                group.PageId,
                kFitTimeoutMs
            );
            browser.RunJavascriptFireAndForget(
                "window.editablePageBundle.captureFlowFit("
                    + $"{group.IndexInPage}, {JsonConvert.ToString(lang)}, "
                    + $"{(isLast ? "true" : "false")})"
            );
            var answer = BookProcessor.WaitForJavascriptResult(
                browser,
                "window.__bloomFlowFit || ''",
                "the text of a box to be refitted",
                group.PageId,
                kFitTimeoutMs
            );
            if (answer.StartsWith("ERROR:", StringComparison.Ordinal))
                throw new ApplicationException(
                    $"flow text: refitting the box on page {group.PageId} failed: {answer}"
                );

            return JsonConvert.DeserializeObject<FitResult>(answer);
        }

        /// <summary>
        /// Lay this page out with the browser's style rules rather than the book's. They differ
        /// while a style change the user has just made is still only in the page being edited.
        /// </summary>
        private static void ApplyStyles(HtmlDom dom, string styles)
        {
            if (styles == null)
                return;
            var element =
                HtmlDom.GetUserModifiedStyleElement(dom.Head)
                ?? HtmlDom.AddEmptyUserModifiedStylesNode(dom.Head);
            element.InnerText = styles;
        }

        internal static void InvokeOnUiThread(Action action)
        {
            var shell = Shell.GetShellOrNull();
            if (shell == null || shell.IsDisposed)
                return; // Bloom is going away; there is nothing left to refresh.
            if (shell.InvokeRequired)
                shell.Invoke(action);
            else
                action();
        }

        #endregion
    }
}
