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
    /// A walk covers every group of the chain from its start page to the end, the page being
    /// edited included. The book's copy of that page is saved before the walk reads it, so the
    /// walk divides the text the user can see. A box the walk changes on that page is also kept
    /// for the browser to put in place (TakeRefitResults), because C# must not navigate the
    /// editor to reload a page that is being edited.
    ///
    /// Nothing starts a walk on its own. A change that calls for one only records it: Request and
    /// RequestEveryChain add to the queue and return, because a walk puts a progress dialog over
    /// the page and takes seconds, which is an interruption in the middle of typing. The queue is
    /// run by RunPending, which the Edit tab calls when the user changes pages (unless the book's
    /// "Reflow when you change pages" preference is off) and which the Reflow now button calls
    /// directly.
    ///
    /// One walk runs at a time, on a background thread, with the Edit tab's progress dialog up
    /// so that nothing is edited underneath it. A second request for a chain already waiting
    /// starts from the earlier of the two pages, because everything from there on has to be
    /// refitted anyway.
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

        // Names, on the body of the page loaded for one fit, the request that loaded it. The
        // off-screen browser starts a navigation and returns at once, and until the new document
        // takes over, a script runs in the document of the page fitted before it, which already
        // says it is ready. So the wait for readiness has to see this request's own mark as well.
        private const string kFitRequestAttrName = "data-bloom-flow-fit-request";

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

            /// <summary>
            /// The style attribute the page being measured left on this box's translation group.
            /// The editor writes the group's font size into it from the box's computed size
            /// (SetupThingsSensitiveToStyleChanges), and a page thumbnail is drawn in the page
            /// list's own document, where that inline size is the only word on how big the text
            /// is. Empty when the group carries no style attribute.
            /// </summary>
            public string groupStyle { get; set; }
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
        ///
        /// addPage, when it is given, adds one text-only page after the last page of the run and
        /// hands back the group on it, so that a run of text longer than the chain's boxes makes
        /// the pages it needs rather than piling up in the last box. Null leaves the last box
        /// holding whatever is left, with the mark that says where its text ran out. maxPagesToAdd
        /// is what stops a fit that never says the text is placed from filling the book; the last
        /// page it allows keeps the rest, so no word is lost and the work can be asked for again.
        /// The groups of the pages added come back in the changed list with the rest.
        ///
        /// addedGroups, when it is given, is filled with just the groups of the pages added, in
        /// the order the text flows through them. The caller cannot pick them out of the changed
        /// list, which says only that a box holds something new, and the last of them is the page
        /// the author is to be taken to.
        /// </summary>
        public static List<FlowTextChains.FlowGroup> Distribute(
            List<FlowTextChains.FlowGroup> groups,
            int startIndex,
            string lang,
            Func<FlowTextChains.FlowGroup, bool, FitResult> fit,
            Func<FlowTextChains.FlowGroup> addPage = null,
            int maxPagesToAdd = FlowTextCreatePages.kMaxPagesToCreate,
            List<FlowTextChains.FlowGroup> addedGroups = null
        )
        {
            var changed = new List<FlowTextChains.FlowGroup>();
            // The chain as this call sees it, which grows as pages are added. The caller's list
            // is left alone: the groups added come back in what is returned.
            var chain = new List<FlowTextChains.FlowGroup>(groups);
            var boxes = Enumerable
                .Range(startIndex, chain.Count - startIndex)
                .Where(index => FlowTextChains.GetFlowEditable(chain[index].Group, lang) != null)
                .ToList();
            if (boxes.Count == 0)
                return changed;

            var startEditable = FlowTextChains.GetFlowEditable(chain[boxes[0]].Group, lang);
            var keep = ReadContinuationMarkers(startEditable);
            var run = CollectRun(chain, startIndex, lang);
            var wordsOfRun = ComparableWords(run.InnerText);
            var remaining = run.InnerXml;
            // Somewhere to put a piece of content when we need to ask a question about it
            // rather than write it into a box.
            var scratch = chain[startIndex].Group.OwnerDocument.CreateElement("div");

            for (var position = 0; position < boxes.Count; position++)
            {
                var group = chain[boxes[position]];
                var editable = FlowTextChains.GetFlowEditable(group.Group, lang);
                var before = editable.InnerXml;
                // The fit brings back the inline font size the page being measured gives the
                // group, which is what a thumbnail of this page is drawn at, so a page whose
                // text is unchanged at a new size is still a page to save.
                var styleBefore = group.Group.GetAttribute("style");
                // Pages of its own are coming for whatever the boxes cannot hold, so the last
                // box of the chain as it stands is not the one that keeps the rest.
                var isLast = position == boxes.Count - 1 && addPage == null;

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
                    var result = fit(group, isLast);
                    HtmlDom.SetInnerHtmlFromFragment(editable, result.head);
                    remaining = result.tail ?? "";
                }

                if (position == 0)
                    WriteContinuationMarkers(editable, keep);

                // Every box but the last of the run hands the text that does not fit it on to
                // the box after it, so it is not overfull however full it looks. The last box
                // keeps what is left, and the mark the fit left in it is what says its text ran
                // out. So the page's own overflow warning follows from the mark alone.
                var overfull = isLast && FindOverflowMarkers(editable).Count > 0;
                var markingChanged = !overfull && ClearOverflowMarking(editable);
                if (
                    editable.InnerXml != before
                    || markingChanged
                    || group.Group.GetAttribute("style") != styleBefore
                )
                    changed.Add(group);
            }

            if (addPage != null)
            {
                var chainId = chain[startIndex].Group.GetAttribute(HtmlDom.kFlowChainAttrName);
                foreach (
                    var added in AddPagesForTheRest(
                        remaining,
                        lang,
                        chainId,
                        fit,
                        addPage,
                        maxPagesToAdd,
                        scratch
                    )
                )
                {
                    chain.Add(added);
                    changed.Add(added);
                    addedGroups?.Add(added);
                }
            }

            // The run is the whole of what the chain carries from here on, and it has just been
            // written to several pages. A word dropped or repeated between two of them is a
            // word dropped or repeated in the book, so say so rather than saving it.
            var wordsNow = ComparableWords(CollectRun(chain, startIndex, lang).InnerText);
            if (wordsNow != wordsOfRun)
                throw new ApplicationException(
                    "flow text: refitting the chain changed its text. It carried "
                        + $"{wordsOfRun.Length} characters and now carries {wordsNow.Length}. "
                        + $"They first differ at {FirstDifference(wordsOfRun, wordsNow)}."
                );

            return changed;
        }

        /// <summary>
        /// Make pages for the text the chain's boxes could not hold, and divide that text among
        /// them, until one page holds what is left or the cap is reached. Returns the groups of
        /// the pages made, in the order the text flows through them.
        ///
        /// The box on each page made holds the whole of what is left while its fit is measured,
        /// exactly as a box of the chain does, and carries the chain so that the text can find
        /// its way back when the author deletes some of it earlier on.
        /// </summary>
        private static List<FlowTextChains.FlowGroup> AddPagesForTheRest(
            string remaining,
            string lang,
            string chainId,
            Func<FlowTextChains.FlowGroup, bool, FitResult> fit,
            Func<FlowTextChains.FlowGroup> addPage,
            int maxPagesToAdd,
            SafeXmlElement scratch
        )
        {
            var added = new List<FlowTextChains.FlowGroup>();
            while (HoldsAnyText(scratch, remaining) && added.Count < maxPagesToAdd)
            {
                var isLast = added.Count + 1 >= maxPagesToAdd;
                var group = addPage();
                var editable = FlowTextChains.GetFlowEditable(group.Group, lang);
                if (editable == null)
                    throw new ApplicationException(
                        $"flow text: the page just made, {group.PageId}, has no {lang} box for "
                            + "the text to flow into."
                    );

                if (!string.IsNullOrEmpty(chainId))
                    group.Group.SetAttribute(HtmlDom.kFlowChainAttrName, chainId);
                HtmlDom.SetInnerHtmlFromFragment(editable, remaining);

                // The fit answers about the box's layout with this text in it and says nothing
                // about whether its first paragraph carries on a paragraph in the box before it,
                // so those markers are read here and put back afterwards.
                var markers = ReadContinuationMarkers(editable);
                var fitted = fit(group, isLast);
                HtmlDom.SetInnerHtmlFromFragment(editable, fitted.head);
                WriteContinuationMarkers(editable, markers);
                remaining = fitted.tail ?? "";
                added.Add(group);

                if (isLast && HoldsAnyText(scratch, remaining))
                {
                    // The cap has stopped the work with text still in hand. It goes into this
                    // box, however over-full that leaves it: text in no box at all is text the
                    // book has lost. The box then still holds more than fits, with the mark that
                    // says so, and the work can be asked for again.
                    FlowTextCreatePages.AppendKeepingSeam(editable, remaining);
                    remaining = "";
                }
            }

            return added;
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

        /// <summary>
        /// Why a walk was asked for, which is what the progress dialog tells the user: a change
        /// that alters every page of a flow at once (RequestEveryChain), or a change from one
        /// page on (Request).
        /// </summary>
        internal enum WalkKind
        {
            WholeFlow,
            FromPageForward,
        }

        internal class PendingWalk
        {
            public string ChainId;
            public string FromPageId;
            public int FromPageIndex;
            public string Lang;
            public WalkKind Kind;

            /// <summary>
            /// The style rules the browser has for the page being edited, or null when the
            /// request did not come from a browser. See the Styles argument of Request.
            /// </summary>
            public string Styles;
        }

        /// <summary>
        /// Adds one text-only page after the page whose id is given and hands back the group on
        /// it that the text flows into. FlowTextApi sets this in its constructor, because making
        /// a page is the Add Page code path and needs the template book the Add Page dialog would
        /// offer, which is the api layer's to find. Null where there is no api layer, such as a
        /// unit test, and then a walk makes no pages.
        /// </summary>
        internal static Func<Book, string, FlowTextChains.FlowGroup> AddTextOnlyPageAfter;

        private static readonly object _lock = new object();

        // One entry per chain and language: a second request for the same box of work starts
        // from the earlier of the two pages rather than being queued twice.
        private static readonly Dictionary<string, PendingWalk> _pending =
            new Dictionary<string, PendingWalk>();
        private static bool _running;

        /// <summary>
        /// One box on the page being edited that a walk gave new content, for the browser to put
        /// in place.
        /// </summary>
        public class RefitResult
        {
            /// <summary>The chain the box belongs to.</summary>
            public string chainId { get; set; }

            /// <summary>Which language's box of the group this is.</summary>
            public string lang { get; set; }

            /// <summary>Where the group comes on the page: see FlowGroup.IndexInPage.</summary>
            public int indexInPage { get; set; }

            /// <summary>What the box holds after the walk.</summary>
            public string html { get; set; }
        }

        // What a walk made of the boxes on the page being edited, by page id. The book's copy of
        // that page is saved like any other, but the browser is showing the older version and C#
        // must not navigate it to reload the page it is editing, so the new content waits here
        // for the browser to ask (flowText/refitResult).
        private static readonly Dictionary<string, List<RefitResult>> _refitResults =
            new Dictionary<string, List<RefitResult>>();

        /// <summary>
        /// What a walk made of the boxes on this page, and forget it. The browser applies each
        /// box once: a second ask, or an ask about any other page, gets nothing.
        /// </summary>
        public static List<RefitResult> TakeRefitResults(string pageId)
        {
            lock (_lock)
            {
                if (pageId == null || !_refitResults.TryGetValue(pageId, out var results))
                    return new List<RefitResult>();
                _refitResults.Remove(pageId);
                return results;
            }
        }

        /// <summary>
        /// Forget every box a walk is holding for the browser. Content worked out for a page of
        /// one book means nothing in another.
        /// </summary>
        public static void ClearRefitResults()
        {
            lock (_lock)
                _refitResults.Clear();
        }

        /// <summary>
        /// Keep this box's new content for the browser, because it is on the page being edited.
        /// </summary>
        internal static void HoldRefitResult(string pageId, RefitResult result)
        {
            lock (_lock)
            {
                if (!_refitResults.TryGetValue(pageId, out var results))
                {
                    results = new List<RefitResult>();
                    _refitResults[pageId] = results;
                }

                results.Add(result);
            }
        }

        /// <summary>
        /// Is a walk running, or is the right to move a chain's text held? e2e/flowText/isIdle
        /// reports on this through FlowTextApi.IsIdle, so that a test can wait for pages it cannot
        /// see.
        ///
        /// A walk merely waiting in the queue is NOT busy: nothing is going to run it until the
        /// user changes pages or asks for it, so anything that waited for it would wait for ever.
        /// </summary>
        public static bool IsBusy
        {
            get
            {
                lock (_lock)
                    return _running;
            }
        }

        /// <summary>Is there a walk waiting to be run?</summary>
        public static bool HasPending
        {
            get
            {
                lock (_lock)
                    return _pending.Count > 0;
            }
        }

        /// <summary>
        /// The chains that walks are waiting for, once each. A chain with a walk waiting for each
        /// of two languages is one chain here.
        /// </summary>
        public static List<string> PendingChainIds
        {
            get
            {
                lock (_lock)
                    return _pending.Values.Select(walk => walk.ChainId).Distinct().ToList();
            }
        }

        /// <summary>
        /// Take the sole right to move a chain's text between its boxes, or return false because
        /// something else holds it. While it is held no walk starts, IsBusy says Bloom is busy,
        /// and setNextContent refuses the browser's own move, exactly as during a walk.
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
        /// Give the claim back. The chain is free again, and the browser is told so. A walk asked
        /// for while the claim was held stays in the queue: nothing here runs it, because a walk
        /// runs only when the user changes pages or asks for one.
        /// </summary>
        internal static void ReleaseClaim()
        {
            lock (_lock)
                _running = false;
            NotifyFinished();
        }

        // The websocket the browser listens on for walkFinished. flowTrigger.ts uses the same
        // name, so the two must agree.
        private const string kWebSocketContext = "flowText";

        /// <summary>
        /// Tell the browser that nothing holds the chain any more. A move the browser could not
        /// make while a walk was running is made now: setNextContent refuses such a move, and
        /// nothing but this brings the browser back to it, so without it the page the user
        /// emptied would keep its text until the next keystroke.
        ///
        /// One call per transition to idle, sent once the pages a walk changed have been saved.
        /// There is no socket server in a unit test, and then nothing is sent.
        ///
        /// pageIdToShow, when it is given, is the page the run of text now ends on, after the
        /// walks added pages for it. pageIdToDelete, when it is given, is the page being edited
        /// that the walks left holding nothing but an empty box of a chain.
        ///
        /// Both are for the browser to act on, and neither is acted on here, because both save
        /// the page being edited and the browser has yet to put in what the walk made of that
        /// page's boxes. Saving it first would write the text the box held before the walk.
        /// </summary>
        private static void NotifyFinished(string pageIdToShow = null, string pageIdToDelete = null)
        {
            if (string.IsNullOrEmpty(pageIdToShow) && string.IsNullOrEmpty(pageIdToDelete))
            {
                BloomWebSocketServer.Instance?.SendEvent(kWebSocketContext, "walkFinished");
                return;
            }

            BloomWebSocketServer.Instance?.SendString(
                kWebSocketContext,
                "walkFinished",
                JsonConvert.SerializeObject(
                    new
                    {
                        pageIdToShow = string.IsNullOrEmpty(pageIdToShow) ? null : pageIdToShow,
                        pageIdToDelete = string.IsNullOrEmpty(pageIdToDelete)
                            ? null
                            : pageIdToDelete,
                    }
                )
            );
        }

        /// <summary>
        /// Tell the browser that a walk is waiting to be run, so that it can offer the user the
        /// Reflow now button. Nothing is going to run it otherwise until the user changes pages,
        /// so this is the only word the browser gets that some later page is now out of date.
        ///
        /// There is no socket server in a unit test, and then nothing is sent.
        /// </summary>
        private static void NotifyQueued()
        {
            BloomWebSocketServer.Instance?.SendEvent(kWebSocketContext, "walkQueued");
        }

        /// <summary>
        /// Ask for this chain to be refitted from this page on. Returns at once, and nothing is
        /// refitted yet: the walk waits in the queue until the user changes pages or asks for it
        /// with Reflow now (RunPending). A walk puts a dialog over the page for seconds, which is
        /// not something to do in the middle of the user's typing.
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
            if (book == null)
                return;

            if (QueueFromPageForward(book.OurHtmlDom, chainId, fromPageId, lang, styles))
                NotifyQueued();
        }

        /// <summary>
        /// Queue the walk that Request asks for. Returns whether there is now a walk waiting for
        /// that chain. Separate from Request so that what goes into the queue can be tested
        /// without an EditingModel.
        /// </summary>
        internal static bool QueueFromPageForward(
            HtmlDom dom,
            string chainId,
            string fromPageId,
            string lang,
            string styles
        )
        {
            if (
                string.IsNullOrEmpty(chainId)
                || string.IsNullOrEmpty(fromPageId)
                || string.IsNullOrEmpty(lang)
            )
                return false;

            var groups = FlowTextChains.GetChainGroups(dom, chainId);
            var start = groups.FindIndex(group => group.PageId == fromPageId);
            if (start < 0)
                return false;

            return Enqueue(
                chainId,
                fromPageId,
                groups[start].PageIndex,
                lang,
                styles,
                WalkKind.FromPageForward
            );
        }

        /// <summary>
        /// Ask for every chain in the book to be refitted, each from its first page, in the book's
        /// main language. This is for a change that alters where the text breaks on every page at
        /// once, such as a new paper size or a new style.
        ///
        /// styles carries the browser's own style rules, and means what it means on Request: a
        /// style change lives in the page being edited until that page is saved, so a walk asked
        /// for by the browser is told the rules rather than reading the older ones the book
        /// holds. Pass null to measure with the rules the book holds.
        ///
        /// Queuing runs nothing: the walks wait until something calls RunPending. That is what
        /// keeps this out of the browser's way, so the page being edited settles itself
        /// undisturbed after such a change. When a walk does run it covers the page being edited
        /// along with the rest of the chain.
        /// </summary>
        public static void RequestEveryChain(EditingModel model, string styles = null)
        {
            var book = model?.CurrentBook;
            if (book == null)
                return;

            var lang = book.Language1Tag;
            if (string.IsNullOrEmpty(lang))
                return;

            if (QueueEveryChain(book.OurHtmlDom, lang, styles))
                NotifyQueued();
        }

        /// <summary>
        /// Queue the walks that RequestEveryChain asks for, one per chain, each starting at the
        /// chain's first page, each carrying the style rules given. Returns whether any chain now
        /// has a walk waiting. Separate from RequestEveryChain so that what goes into the queue
        /// can be tested without an EditingModel.
        /// </summary>
        internal static bool QueueEveryChain(HtmlDom dom, string lang, string styles = null)
        {
            var any = false;
            foreach (var chainId in GetChainIds(dom))
            {
                var groups = FlowTextChains.GetChainGroups(dom, chainId);
                // One box on its own is not a chain: it has nowhere to send its extra text.
                if (groups.Count < 2)
                    continue;
                any |= Enqueue(
                    chainId,
                    groups[0].PageId,
                    groups[0].PageIndex,
                    lang,
                    styles,
                    WalkKind.WholeFlow
                );
            }

            return any;
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

        /// <summary>
        /// Put this walk in the queue, or fold it into the one already waiting for that chain and
        /// language. Returns true either way: there is a walk waiting for that chain when it
        /// returns.
        /// </summary>
        private static bool Enqueue(
            string chainId,
            string pageId,
            int pageIndex,
            string lang,
            string styles,
            WalkKind kind
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
                    // The wider reason for a walk is the one to report: a refit of the whole
                    // flow covers a refit from a page on as well.
                    if (kind == WalkKind.WholeFlow)
                        already.Kind = WalkKind.WholeFlow;
                    return true;
                }

                _pending[key] = new PendingWalk
                {
                    ChainId = chainId,
                    FromPageId = pageId,
                    FromPageIndex = pageIndex,
                    Lang = lang,
                    Styles = styles,
                    Kind = kind,
                };
                return true;
            }
        }

        /// <summary>The walks waiting to run, for tests to read.</summary>
        internal static List<PendingWalk> QueuedWalksForTests()
        {
            lock (_lock)
                return _pending.Values.ToList();
        }

        /// <summary>
        /// Forget the walks waiting to run. A waiting walk names its chain and its page by id
        /// and nothing else, and a copy of a book keeps those ids, so a walk queued in one book
        /// would match, and rewrite, the pages of another. The queue therefore belongs to the
        /// selected book: FlowTextApi empties it whenever the selection changes.
        /// </summary>
        public static void ClearPendingWalks()
        {
            lock (_lock)
                _pending.Clear();
        }

        /// <summary>Forget the walks waiting to run, so that one test cannot affect another.</summary>
        internal static void ClearQueueForTests()
        {
            ClearPendingWalks();
        }

        /// <summary>
        /// Run the walks that are waiting, if any. This is the one way a walk ever starts: the
        /// Edit tab calls it when the user changes pages (see EditingModel), and the Reflow now
        /// button calls it through flowText/reflowNow. Returns at once; the work runs on a
        /// background thread with the Edit tab's progress dialog up.
        ///
        /// Does nothing while something else holds the right to move a chain's text, or when
        /// nothing is queued.
        /// </summary>
        public static void RunPending(EditingModel model)
        {
            StartIfIdle(model);
        }

        /// <summary>
        /// Start the queue running, unless something already holds the chain or there is nothing
        /// queued. Returns whether a run was started.
        /// </summary>
        private static bool StartIfIdle(EditingModel model)
        {
            lock (_lock)
            {
                if (_running || _pending.Count == 0)
                    return false;
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
                return true;
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
            return true;
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

        /// <summary>
        /// What the progress dialog says a walk is doing, under its bar.
        /// </summary>
        private static string StageText(WalkKind kind)
        {
            if (kind == WalkKind.WholeFlow)
                return LocalizationManager.GetString(
                    "EditTab.FlowText.RefittingWholeFlow",
                    "Re-flowing the text of this flow"
                );
            return LocalizationManager.GetString(
                "EditTab.FlowText.RefittingFromPageForward",
                "Re-flowing from this page forward"
            );
        }

        private static void RunQueue(EditingModel model, IWebSocketProgress progress)
        {
            var queueProgress = new QueueProgress(progress);
            // Made when the first box has to be measured, not before: a browser is a WebView2 and
            // a thread of its own, and a queue whose walks all turn out to have nothing to fit
            // (every page of the chain is the page being edited, say) needs none.
            OffScreenBrowser browser = null;
            // The last page the queue's walks added to the chain the author is on, and a page the
            // queue emptied that the author is looking at. Both are for after every walk has run:
            // a page added by one walk can be filled again by the next, and the page being edited
            // cannot be taken away while a walk still has work to do on the book.
            string lastPageAdded = null;
            string emptiedPageBeingEdited = null;
            try
            {
                PendingWalk walk;
                while ((walk = TakeNext()) != null)
                {
                    var groups = GroupsToFit(model, walk, out var start);
                    if (groups == null)
                        continue;
                    queueProgress.AddBoxesToFit(groups.Count - start);
                    progress?.SendStage(StageText(walk.Kind));
                    browser = browser ?? new OffScreenBrowser();
                    RunOneWalk(
                        model,
                        browser,
                        walk,
                        groups,
                        start,
                        queueProgress,
                        out var addedByThisWalk,
                        out var emptiedByThisWalk
                    );
                    lastPageAdded = addedByThisWalk ?? lastPageAdded;
                    emptiedPageBeingEdited = emptiedByThisWalk ?? emptiedPageBeingEdited;
                }

                // TakeNext gave the claim back when it found nothing left, and RunOneWalk saved
                // each page it changed before returning, so the chain is free and the book holds
                // what the walk made of it.
                // Taking the page being edited away moves the Edit tab to the page beside it, so
                // there is no jump to ask the browser for as well.
                NotifyFinished(
                    emptiedPageBeingEdited == null ? lastPageAdded : null,
                    emptiedPageBeingEdited
                );
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

                NotifyFinished();
            }
            finally
            {
                browser?.Dispose();
            }
        }

        /// <summary>
        /// The run of groups this walk covers, and where in it the walk starts: from the walk's
        /// start page to the end of the chain, the page being edited included. Dividing the run
        /// afresh needs every box it can reach, because text moves backward as readily as
        /// forward: a bigger page pulls text back from later pages, and a page the walk stopped
        /// short of would keep text that belongs earlier.
        ///
        /// Null when the chain has no group on the walk's start page, which is the one case where
        /// there is nothing to fit.
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

            return GroupsToFit(
                FlowTextChains.GetChainGroups(book.OurHtmlDom, walk.ChainId),
                walk.FromPageId,
                out startIndex
            );
        }

        /// <summary>
        /// Which of a chain's groups a walk starting at this page covers, and where in them it
        /// starts: all of them, from the group on that page. Null, with startIndex zero, when the
        /// chain has no group on that page.
        ///
        /// The groups before the start page come back with the rest because Distribute is given
        /// the whole chain and the place in it to begin: what precedes the start is read for the
        /// markers that say a paragraph was divided, and never written.
        /// </summary>
        internal static List<FlowTextChains.FlowGroup> GroupsToFit(
            List<FlowTextChains.FlowGroup> groups,
            string fromPageId,
            out int startIndex
        )
        {
            startIndex = groups.FindIndex(group => group.PageId == fromPageId);
            if (startIndex < 0)
            {
                startIndex = 0;
                return null;
            }

            return groups;
        }

        /// <summary>
        /// Refit one chain, save every page the refit changed, and take away the pages it
        /// emptied.
        ///
        /// lastPageAdded comes back as the id of the last page this walk made, and only when the
        /// author is on a page of the chain it walked: that is where their own text ended up.
        /// Null otherwise. emptiedPageBeingEdited comes back as the id of the page the author is
        /// looking at when this walk emptied that page, or null. Neither is acted on here: both
        /// are for RunQueue, once every walk has run.
        /// </summary>
        private static void RunOneWalk(
            EditingModel model,
            OffScreenBrowser browser,
            PendingWalk walk,
            List<FlowTextChains.FlowGroup> groups,
            int start,
            QueueProgress queueProgress,
            out string lastPageAdded,
            out string emptiedPageBeingEdited
        )
        {
            lastPageAdded = null;
            emptiedPageBeingEdited = null;
            var book = model.CurrentBook;
            var pagesLoaded = 0;
            // The author has asked for pages once in this book, so the flow may make the pages
            // its text needs and take away the ones it has emptied.
            var autoPages =
                book.UserPrefs?.FlowTextAutoPages == true && AddTextOnlyPageAfter != null;
            // Where a page made goes: after the last page of the chain, and then after the last
            // page made, so that the pages come in the order the text flows through them.
            var afterPageId = groups[groups.Count - 1].PageId;
            Func<FlowTextChains.FlowGroup> addPage = null;
            if (autoPages)
                addPage = () =>
                {
                    var added = AddTextOnlyPageAfter(book, afterPageId);
                    afterPageId = added.PageId;
                    // Its box is one more box for the browser to measure, so the bar has one
                    // more box to go.
                    queueProgress.AddBoxesToFit(1);
                    return added;
                };

            var addedGroups = new List<FlowTextChains.FlowGroup>();
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
                },
                addPage,
                addedGroups: addedGroups
            );

            // A page the run of text has emptied, and that holds nothing else, goes with the
            // text: the page was there to carry that text and now carries nothing. The chain's
            // first page stays whatever it holds, because a chain has to begin somewhere.
            //
            // The page the author is looking at goes the same way, but not here: the browser is
            // showing it, so it is taken out once every walk has run (RunQueue), by the route the
            // Delete Page command uses.
            var emptyPageIds = autoPages
                ? groups
                    .Skip(start)
                    .Where(group =>
                        group.PageId != groups[0].PageId
                        && FlowTextChains.PageHoldsNothingBut(
                            FlowTextChains.FindPage(book, group.PageId)?.GetDivNodeForThisPage(),
                            group.Group
                        )
                    )
                    .Select(group => group.PageId)
                    .Distinct()
                    .ToList()
                : new List<string>();

            if (changed.Count == 0 && emptyPageIds.Count == 0)
                return;

            var changedPageIds = changed.Select(group => group.PageId).Distinct().ToList();
            // Assigned on the UI thread below, because an out parameter cannot be.
            string emptiedCurrentPage = null;
            string lastAddedForThisAuthor = null;
            InvokeOnUiThread(() =>
            {
                // The browser is showing the page being edited and will not be navigated to
                // reload it, so a box the walk changed there is kept for the browser to apply.
                // Read here rather than earlier, because which page is being edited is a
                // question about the moment the walk's work lands.
                var currentPageId = model.CurrentPage?.Id;
                // The page the user is looking at is not taken away here: the browser is showing
                // it and has yet to be given what the walk made of its boxes, so it asks for the
                // page to go once it has put that in (flowText/deleteEmptiedPage).
                var pageIdsToDelete = emptyPageIds
                    .Where(pageId => pageId != currentPageId)
                    .ToList();
                emptiedCurrentPage = PageBeingEditedToDelete(
                    emptyPageIds,
                    currentPageId,
                    groups[0].PageId
                );

                // Where the author is taken when this walk made pages: the last of them. Only a
                // walk of the chain the author is editing offers one. A book can hold several
                // chains, and a change of font size or paper size refits them all, so a walk of
                // some other chain grew a run the author was not looking at.
                if (addedGroups.Count > 0 && groups.Any(group => group.PageId == currentPageId))
                    lastAddedForThisAuthor = addedGroups[addedGroups.Count - 1].PageId;
                foreach (var group in changed.Where(group => group.PageId == currentPageId))
                {
                    var editable = FlowTextChains.GetFlowEditable(group.Group, walk.Lang);
                    if (editable == null)
                        continue;
                    HoldRefitResult(
                        currentPageId,
                        new RefitResult
                        {
                            chainId = walk.ChainId,
                            lang = walk.Lang,
                            indexInPage = group.IndexInPage,
                            html = editable.InnerXml,
                        }
                    );
                }

                foreach (var pageId in changedPageIds.Where(id => !pageIdsToDelete.Contains(id)))
                {
                    var page = FlowTextChains.FindPage(book, pageId);
                    if (page == null)
                        continue;
                    book.SaveForPageChanged(page.Id, page.GetDivNodeForThisPage());
                    model.RefreshThumbnail(page);
                }

                if (pageIdsToDelete.Count == 0)
                    return;

                foreach (var pageId in pageIdsToDelete)
                {
                    var page = FlowTextChains.FindPage(book, pageId);
                    if (page != null)
                        model.RemovePageFromBook(page);
                }

                // Book.DeletePage takes the page out of the book's DOM and tells the page list,
                // but nothing there writes the book to disk: the Delete Page command relies on
                // the save it is wrapped in. A walk is in no such save, so the whole book is
                // written here, which is also what the renumbering of the pages that are left
                // needs.
                book.Save();
            });

            emptiedPageBeingEdited = emptiedCurrentPage;
            lastPageAdded = lastAddedForThisAuthor;
        }

        /// <summary>
        /// Which page a walk emptied is the page the author is looking at, and so is the one page
        /// the walk itself cannot take away. Null when none of them is.
        ///
        /// A page the author is looking at is taken away like any other page the run of text has
        /// left empty, because a page that was only ever there to carry text the flow has moved
        /// elsewhere is a page in the author's way. The chain's first page is the exception: a
        /// chain has to begin somewhere.
        /// </summary>
        internal static string PageBeingEditedToDelete(
            List<string> emptyPageIds,
            string currentPageId,
            string firstPageIdOfChain
        )
        {
            if (string.IsNullOrEmpty(currentPageId) || currentPageId == firstPageIdOfChain)
                return null;
            return emptyPageIds.Contains(currentPageId) ? currentPageId : null;
        }

        /// <summary>
        /// Take away the page the author is looking at, which a refit has left holding nothing
        /// but an empty box of a chain. Returns whether the page went.
        ///
        /// This is the Delete Page route (EditingModel.DeletePage), because the browser is
        /// showing the page: that saves what the browser holds, takes the page out, moves the
        /// Edit tab to the page beside it and redraws the page list. So it must be called on the
        /// UI thread, and only once the browser has put the refit's own content into the boxes
        /// of this page, which is why the browser asks for it rather than the refit doing it.
        ///
        /// Everything the walk read is checked again, because the book has changed hands since:
        /// the author may have turned the page, the setting may be off, and the box may have
        /// been filled again.
        /// </summary>
        internal static bool DeleteEmptiedPageBeingEdited(EditingModel model, string pageId)
        {
            var book = model?.CurrentBook;
            if (book == null || string.IsNullOrEmpty(pageId))
                return false;
            if (book.UserPrefs?.FlowTextAutoPages != true)
                return false;
            if (model.CurrentPage?.Id != pageId)
                return false;

            var page = FlowTextChains.FindPage(book, pageId);
            var pageElement = page?.GetDivNodeForThisPage();
            if (pageElement == null)
                return false;

            // The box the refit emptied, found again by the chain it belongs to: that is what
            // makes this a page the flow put there rather than one the author made.
            var group = FlowTextChains
                .GetFlowGroupsOfPage(pageElement, pageId, 0)
                .Select(candidate => candidate.Group)
                .FirstOrDefault(candidate => candidate.HasAttribute(HtmlDom.kFlowChainAttrName));
            if (group == null)
                return false;

            var chainId = group.GetAttribute(HtmlDom.kFlowChainAttrName);
            var chainGroups = FlowTextChains.GetChainGroups(book.OurHtmlDom, chainId);
            if (chainGroups.Count > 0 && chainGroups[0].PageId == pageId)
                return false;

            if (!FlowTextChains.PageHoldsNothingBut(pageElement, group))
                return false;

            if (model.InProcessOfSaving)
            {
                Logger.WriteEvent(
                    "flow text: the page being edited was left empty by a refit, but a save "
                        + "was in progress, so it was not taken out."
                );
                return false;
            }

            model.DeletePage(page);
            return true;
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
            var requestId = Guid.NewGuid().ToString("N");
            dom.Body.SetAttribute(kFitRequestAttrName, requestId);
            // Off-screen measuring never types into the page, so CKEditor is dead weight: a
            // third of a megabyte of script to load into each fresh renderer, and none of it
            // changes how the text is laid out.
            foreach (var script in dom.SafeSelectNodes("//script[contains(@src,'ckeditor')]"))
                script.ParentNode?.RemoveChild(script);

            // A classic script in the head runs before any module script, so this is listening
            // by the time the page's own bundles load: an error that stops one of them loading
            // is the reason the wait below can time out, and nothing else records it.
            HtmlDom.AddInlineScript(dom.RawDom, kRecordLoadErrorsScript, false);

            browser.NavigateWithoutWaitingForLoad(dom, InMemoryHtmlFileSource.Frame);
            try
            {
                BookProcessor.WaitForJavascriptResult(
                    browser,
                    "(document.body && document.body.getAttribute("
                        + $"'{kFitRequestAttrName}') === '{requestId}' "
                        + "&& window.__bloomEditablePageReady && window.editablePageBundle) "
                        + "? 'ready' : ''",
                    "the editing bundle to initialize",
                    group.PageId,
                    kFitTimeoutMs
                );
            }
            catch (ApplicationException timeout)
            {
                throw new ApplicationException(
                    timeout.Message + " Page state: " + DescribePageState(browser),
                    timeout
                );
            }
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

            var result = JsonConvert.DeserializeObject<FitResult>(answer);
            WriteGroupStyle(group.Group, result.groupStyle);
            return result;
        }

        /// <summary>
        /// Give the group the style attribute the page laid out off-screen settled on. The editor
        /// writes the group's font size into that attribute from the box's computed size, and a
        /// page thumbnail is drawn in the page list's own document, whose stylesheet is not the
        /// book's: the inline size is all the thumbnail has to go on. So a page a walk writes
        /// keeps the size it was drawn at before unless it is copied back here.
        /// </summary>
        internal static void WriteGroupStyle(SafeXmlElement group, string style)
        {
            if (!string.IsNullOrEmpty(style))
                group.SetAttribute("style", style);
            else if (group.HasAttribute("style"))
                group.RemoveAttribute("style");
        }

        // Keeps every error the page reports while it loads, for DescribePageState to hand back
        // when the page never becomes ready.
        private const string kRecordLoadErrorsScript =
            @"
                window.__bloomFlowLoadErrors = [];
                window.addEventListener('error', function (event) {
                    window.__bloomFlowLoadErrors.push({
                        message: event.message,
                        filename: event.filename,
                        lineno: event.lineno
                    });
                });
                window.addEventListener('unhandledrejection', function (event) {
                    window.__bloomFlowLoadErrors.push({ reason: String(event.reason) });
                });";

        // Everything that says why the page has not become ready, as a JSON string. It must
        // answer even when the page is broken, so each part that can throw is guarded.
        private const string kPageStateScript =
            @"
                (function () {
                    try {
                        var sources = [];
                        var scripts = document.querySelectorAll('script');
                        scripts.forEach(function (script) {
                            if (script.src) sources.push(script.src);
                        });
                        var loading = [];
                        try {
                            performance.getEntriesByType('resource').forEach(function (entry) {
                                if (entry.responseEnd === 0) loading.push(entry.name);
                            });
                        } catch (error) {
                            loading.push('no performance entries: ' + String(error));
                        }
                        return JSON.stringify({
                            readyState: document.readyState,
                            href: location.href,
                            scriptCount: scripts.length,
                            scriptSources: sources,
                            pageReady: !!window.__bloomEditablePageReady,
                            bundleType: typeof window.editablePageBundle,
                            loadErrors: window.__bloomFlowLoadErrors || null,
                            stillLoadingCount: loading.length,
                            stillLoading: loading.slice(0, 10)
                        });
                    } catch (error) {
                        return 'the diagnostic script failed: ' + String(error);
                    }
                })()";

        /// <summary>
        /// What the off-screen page has got to: what it has loaded, what it has not, and what it
        /// reported going wrong. This is all there is to say why a page never became ready, so
        /// it never throws in place of the failure it is describing.
        /// </summary>
        private static string DescribePageState(OffScreenBrowser browser)
        {
            try
            {
                return browser.RunJavascript(kPageStateScript);
            }
            catch (Exception e)
            {
                return "the page could not be asked: " + e.Message;
            }
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
