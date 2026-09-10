using System.Collections.Generic;
using System.Linq;
using Bloom.SafeXml;

namespace Bloom.Book
{
    /// <summary>
    /// The book-wide part of flow text: which text boxes make up one chain, where the text of an
    /// earlier box stops fitting, and how to move the part that does not fit into another box.
    ///
    /// Everything here is pure DOM work on the book's own HtmlDom, so it is unit-testable and
    /// measures nothing: the browser decides where the text of a box runs out and records it with
    /// a span (see bookEdit/flowText/flowConstants.ts), and this code splits the paragraph there.
    /// FlowTextApi is the thin layer that calls this and saves the pages.
    /// </summary>
    public static class FlowTextChains
    {
        public const string kContinuationAttrName = "data-flow-continuation";

        /// <summary>
        /// Goes beside the continuation attribute when the text was cut at a space. See
        /// kSeamSpaceAttr in bookEdit/flowText/flowConstants.ts: no whitespace survives at the
        /// edge of a paragraph, so the space is carried as markup and put back when the editor
        /// joins the two halves.
        /// </summary>
        public const string kSeamSpaceAttrName = "data-flow-seam-space";
        public const string kNormalStyleClass = "normal-style";

        /// <summary>
        /// One translation group of a chain, and where it is in the book.
        /// </summary>
        public class FlowGroup
        {
            /// <summary>The id of the .bloom-page div the group is on.</summary>
            public string PageId;

            /// <summary>Where that page comes in the book, counting from zero.</summary>
            public int PageIndex;

            /// <summary>
            /// Where the group comes among the page's translation groups that are not inside a
            /// bloom-canvas, counting from zero. This is how the browser and C# name the same
            /// group without an id of its own.
            /// </summary>
            public int IndexInPage;

            public SafeXmlElement Group;
        }

        /// <summary>
        /// A box, earlier in the book, that holds more text than fits in it.
        /// </summary>
        public class PendingOverflow
        {
            public string PageId;

            /// <summary>
            /// What the reader calls that page: its page number, or its caption when it has no
            /// number. It goes straight into the button's label, so it is a string, not an int.
            /// </summary>
            public string PageNumberLabel;

            public int IndexInPage;

            /// <summary>The first few characters that do not fit, for the caller to show.</summary>
            public string PreviewText;
        }

        /// <summary>
        /// The result of splitting a paragraph at the point where its text stopped fitting.
        /// </summary>
        public class ParagraphSplit
        {
            /// <summary>
            /// The top-level nodes of the editable from the split to its end, taken out of it.
            /// </summary>
            public List<SafeXmlNode> TailNodes = new List<SafeXmlNode>();

            /// <summary>
            /// True when the split fell in the middle of a paragraph's text, so the first node of
            /// the tail carries on a paragraph that is still in the source box. When the split
            /// fell at the very start or the very end of a paragraph, no paragraph is divided:
            /// whole paragraphs move, and the first of them begins in the box it lands in.
            /// </summary>
            public bool IsContinuation;
        }

        /// <summary>
        /// How many characters of the text that does not fit we hand back for a preview.
        /// </summary>
        private const int kPreviewLength = 60;

        // The two zero-width characters that live in an edited box without being any part of what
        // the reader sees: the one the overflow marker holds (U+200C) and the filler the editor
        // keeps at the end of a paragraph it owns (U+200B).
        private static readonly string kZeroWidthNonJoiner = ((char)0x200c).ToString();
        private static readonly string kZeroWidthSpace = ((char)0x200b).ToString();

        /// <summary>
        /// The groups of one chain, in the order the text flows through them: book page order,
        /// then document order within a page. Groups inside a bloom-canvas are skipped, because a
        /// canvas element lays its own text out and can never be part of a chain.
        /// </summary>
        public static List<FlowGroup> GetChainGroups(HtmlDom dom, string chainId)
        {
            var result = new List<FlowGroup>();
            if (dom == null || string.IsNullOrEmpty(chainId))
                return result;

            foreach (var page in GetPageDivs(dom))
            {
                foreach (var group in GetFlowGroupsOfPage(page.Element, page.Id, page.Index))
                {
                    if (group.Group.GetAttribute(HtmlDom.kFlowChainAttrName) == chainId)
                        result.Add(group);
                }
            }

            return result;
        }

        /// <summary>
        /// The nearest page before beforePageId that holds a box of this language whose text
        /// stops fitting, or null when there is none. "A box" here means an ordinary text box of
        /// the page's own layout: normal-style, not inside a bloom-canvas, and not in the front or
        /// back matter, whose text carries the span that says where the fit ends.
        ///
        /// getPageLabel, when given, says what the reader calls a page; without it the label is
        /// the page's one-based position among the pages of the book.
        /// </summary>
        public static PendingOverflow FindPendingOverflowBefore(
            HtmlDom dom,
            string beforePageId,
            string lang,
            System.Func<string, string> getPageLabel = null
        )
        {
            if (dom == null || string.IsNullOrEmpty(beforePageId) || string.IsNullOrEmpty(lang))
                return null;

            PendingOverflow best = null;
            foreach (var page in GetPageDivs(dom))
            {
                if (page.Id == beforePageId)
                    break; // Everything from here on is the current page or later.
                if (IsXMatter(page.Element))
                    continue;

                foreach (var group in GetFlowGroupsOfPage(page.Element, page.Id, page.Index))
                {
                    var editable = GetFlowEditable(group.Group, lang);
                    var marker = editable == null ? null : FindOverflowMarker(editable);
                    if (marker == null)
                        continue;

                    best = new PendingOverflow
                    {
                        PageId = page.Id,
                        PageNumberLabel =
                            getPageLabel?.Invoke(page.Id) ?? (page.Index + 1).ToString(),
                        IndexInPage = group.IndexInPage,
                        PreviewText = GetPreviewAfterMarker(editable, marker),
                    };
                }
            }

            return best;
        }

        /// <summary>
        /// The box of this language whose text flows, in this translation group: a normal-style
        /// bloom-editable that is a child of the group. Null when the group has none.
        /// </summary>
        public static SafeXmlElement GetFlowEditable(SafeXmlElement group, string lang)
        {
            if (group == null)
                return null;

            return group
                .ChildNodes.OfType<SafeXmlElement>()
                .FirstOrDefault(child =>
                    child.HasClass("bloom-editable")
                    && child.HasClass(kNormalStyleClass)
                    && child.GetAttribute("lang") == lang
                );
        }

        /// <summary>
        /// Every language whose box in this group holds the span that says where its text stops
        /// fitting. Each language's text flows through its own boxes, so a handoff has to move all
        /// of them at once or the languages of one group would drift apart.
        /// </summary>
        public static List<string> GetLanguagesWithOverflow(SafeXmlElement group)
        {
            var result = new List<string>();
            if (group == null)
                return result;

            foreach (var child in group.ChildNodes.OfType<SafeXmlElement>())
            {
                if (!child.HasClass("bloom-editable") || !child.HasClass(kNormalStyleClass))
                    continue;
                var lang = child.GetAttribute("lang");
                if (string.IsNullOrEmpty(lang) || lang == "z" || lang == "*")
                    continue;
                if (FindOverflowMarker(child) != null)
                    result.Add(lang);
            }

            return result;
        }

        /// <summary>
        /// The span that says where the text of this box stops fitting, or null when its text
        /// fits. At most one is ever in a box.
        /// </summary>
        public static SafeXmlElement FindOverflowMarker(SafeXmlElement editable)
        {
            return editable
                ?.SafeSelectNodes($".//span[contains(@class,'{HtmlDom.kOverflowStartClass}')]")
                .OfType<SafeXmlElement>()
                .FirstOrDefault();
        }

        /// <summary>
        /// Divide the paragraph that holds the overflow marker at the marker, take the marker out,
        /// and take the nodes from the split to the end of the box out of the box. The open inline
        /// elements around the marker are cloned, so text that was bold before the split is still
        /// bold in both halves.
        ///
        /// Returns null when the box holds no marker.
        /// </summary>
        public static ParagraphSplit SplitParagraphAtMarker(SafeXmlElement editable)
        {
            var marker = FindOverflowMarker(editable);
            if (marker == null)
                return null;

            var topParagraph = GetTopLevelAncestor(marker, editable);
            if (topParagraph == null)
                return null;

            // Divide each open element between the marker and the paragraph, innermost first: a
            // shallow clone of the element takes everything that came after the marker, and the
            // clone of the level below goes in at the front of the clone above it.
            SafeXmlNode tailFromLevelBelow = null;
            var current = (SafeXmlNode)marker;
            SafeXmlElement tailParagraph = null;
            while (true)
            {
                var parent = current.ParentNode as SafeXmlElement;
                if (parent == null)
                    return null;

                var clone = (SafeXmlElement)parent.CloneNode(false);
                foreach (var following in GetFollowingSiblings(current))
                {
                    parent.RemoveChild(following);
                    clone.AppendChild(following);
                }

                if (tailFromLevelBelow != null)
                    clone.PrependChild(tailFromLevelBelow);

                if (parent == topParagraph)
                {
                    tailParagraph = clone;
                    break;
                }

                tailFromLevelBelow = clone;
                current = parent;
            }

            marker.ParentNode.RemoveChild(marker);

            var split = new ParagraphSplit();
            var headIsEmpty = !HasVisibleText(topParagraph);
            var tailIsEmpty = !HasVisibleText(tailParagraph);
            // Read the paragraphs after this one now: taking the divided paragraph out of the box
            // would leave it with no siblings to find.
            var laterParagraphs = GetFollowingSiblings(topParagraph);

            // A split at the very start or the very end of a paragraph divides no paragraph: the
            // empty half is not text, it is an artifact of where the split fell, so it goes.
            if (tailIsEmpty)
            {
                // Nothing of this paragraph moves. What follows it does.
            }
            else if (headIsEmpty)
            {
                editable.RemoveChild(topParagraph);
                split.TailNodes.Add(tailParagraph);
            }
            else
            {
                split.IsContinuation = true;
                RecordBoundarySpaceOnTail(topParagraph, tailParagraph);
                split.TailNodes.Add(tailParagraph);
            }

            foreach (var following in laterParagraphs)
            {
                editable.RemoveChild(following);
                split.TailNodes.Add(following);
            }

            return split;
        }

        /// <summary>
        /// Take the space at the point of the split out of the head and record it on the tail.
        ///
        /// Neither half of a divided paragraph can hold that space. The editor keeps no real
        /// space at the end of a paragraph it owns: it writes a zero-width filler there instead.
        /// A space at the head of a paragraph does not come back from the saved page either. So
        /// the attribute says a space belongs at the seam, and the editor puts it back when it
        /// joins the halves (giveBackSeamSpace in flowDomMove.ts).
        /// </summary>
        private static void RecordBoundarySpaceOnTail(SafeXmlElement head, SafeXmlElement tail)
        {
            // The tail is a clone of the head, so it starts with whatever seam the head records.
            // Settle it either way: a cut inside a word must not be given a space.
            tail.RemoveAttribute(kSeamSpaceAttrName);

            var lastOfHead = FindLastTextNode(head);
            var text = lastOfHead?.Value;
            if (string.IsNullOrEmpty(text))
                return;

            // The editor's own filler can stand between the last word and the space, so take
            // both off; a filler is no part of what the reader sees.
            var kept = text.TrimEnd(' ', '\t', (char)0x200b, (char)0x200c);
            if (kept.Length == text.Length)
                return;

            lastOfHead.Value = kept;
            var removed = text.Substring(kept.Length);
            if (removed.IndexOf(' ') >= 0 || removed.IndexOf('\t') >= 0)
                tail.SetAttribute(kSeamSpaceAttrName, "true");
        }

        /// <summary>The last text node inside this element, in document order, if it has one.</summary>
        private static SafeXmlNode FindLastTextNode(SafeXmlNode node)
        {
            if (node.NodeType == System.Xml.XmlNodeType.Text)
                return node;

            var children = node.ChildNodes;
            for (var index = children.Length - 1; index >= 0; index--)
            {
                var found = FindLastTextNode(children[index]);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Move the text that does not fit in the source box into the target box, ahead of
        /// whatever the target box already holds. The paragraph that was divided carries on in the
        /// target as a continuation paragraph, which the stylesheet gives no first-line indent and
        /// no top margin. A target that holds nothing but an empty placeholder paragraph loses it.
        ///
        /// Returns false, changing nothing, when the source box holds no overflow marker.
        /// </summary>
        public static bool MoveTailInto(
            SafeXmlElement sourceEditable,
            SafeXmlElement targetEditable
        )
        {
            if (sourceEditable == null || targetEditable == null)
                return false;

            var split = SplitParagraphAtMarker(sourceEditable);
            if (split == null || split.TailNodes.Count == 0)
                return false;

            RemovePlaceholderParagraph(targetEditable);

            SafeXmlNode after = null;
            foreach (var node in split.TailNodes)
            {
                var imported =
                    targetEditable.OwnerDocument == node.OwnerDocument
                        ? node
                        : targetEditable.OwnerDocument.ImportNode(node, true);
                if (after == null)
                    targetEditable.PrependChild(imported);
                else
                    targetEditable.InsertAfter(imported, after);
                after = imported;
            }

            var firstParagraph = targetEditable
                .ChildNodes.OfType<SafeXmlElement>()
                .FirstOrDefault(child => child.Name == "p");
            if (firstParagraph != null)
            {
                if (split.IsContinuation)
                {
                    firstParagraph.SetAttribute(kContinuationAttrName, "true");
                }
                else
                {
                    // A whole paragraph moved, so it continues nothing and no space was cut.
                    firstParagraph.RemoveAttribute(kContinuationAttrName);
                    firstParagraph.RemoveAttribute(kSeamSpaceAttrName);
                }
            }

            // The marker sat where the source box's text stopped fitting, so what is left in
            // the box fits it. The browser's warning that it overflows, saved with the page,
            // would go on showing on the page's thumbnail until someone opened the page.
            FlowTextWalk.ClearOverflowMarking(sourceEditable);

            return true;
        }

        /// <summary>
        /// What deleting one page did to one chain that ran through it.
        /// </summary>
        public class PageDeletionMove
        {
            public string ChainId;

            /// <summary>
            /// The page holding the box the text moved into, which is where a walk of the chain
            /// starts. Null when the chain has no box left on another page, or when only one box
            /// is left and so the chain is no longer a chain: there is nothing to refit.
            /// </summary>
            public string WalkFromPageId;

            /// <summary>
            /// The languages whose text moved. Each language's text flows through its own boxes,
            /// so each one needs its own walk.
            /// </summary>
            public List<string> Langs = new List<string>();
        }

        /// <summary>
        /// Move the text of every chained box on this page into the box beside it in its chain,
        /// so that deleting the page loses none of the text the chain carries. Call this while
        /// the page is still in the DOM; the caller removes the page afterwards and asks for a
        /// walk of each chain reported here.
        ///
        /// The text goes to the next box of the chain, ahead of what that box already holds.
        /// When the page holds the last box of the chain it goes instead to the end of the
        /// previous box. Either way the two boxes' text meets at one point, and the paragraph on
        /// the later side of that point is joined back onto the paragraph it broke off from when
        /// it carries the continuation attribute.
        ///
        /// A chain with only one box left afterwards stops being a chain, for the same reason
        /// UnlinkFrom applies: one box on its own has nowhere to send its extra text.
        ///
        /// A page holding the only box of a chain has nowhere to put its text, so nothing moves
        /// and the text goes with the page.
        /// </summary>
        public static List<PageDeletionMove> MoveChainedTextOffPage(
            HtmlDom dom,
            SafeXmlElement pageElement
        )
        {
            var moves = new List<PageDeletionMove>();
            if (dom == null || pageElement == null)
                return moves;

            var pageId = pageElement.GetAttribute("id");
            if (string.IsNullOrEmpty(pageId))
                return moves;

            var chainIds = GetFlowGroupsOfPage(pageElement, pageId, 0)
                .Select(group => group.Group.GetAttribute(HtmlDom.kFlowChainAttrName))
                .Where(chainId => !string.IsNullOrEmpty(chainId))
                .Distinct()
                .ToList();

            foreach (var chainId in chainIds)
            {
                var groups = GetChainGroups(dom, chainId);
                // The groups of a chain are in page order, so the ones on this page are together.
                var firstOnPage = groups.FindIndex(group => group.PageId == pageId);
                var lastOnPage = groups.FindLastIndex(group => group.PageId == pageId);
                if (firstOnPage < 0)
                    continue;

                var move = new PageDeletionMove { ChainId = chainId };
                var goingOut = Enumerable
                    .Range(firstOnPage, lastOnPage - firstOnPage + 1)
                    .Select(index => groups[index])
                    .ToList();

                FlowGroup target = null;
                if (lastOnPage + 1 < groups.Count)
                {
                    // The text goes to the next box, so the box furthest down the chain moves
                    // first: each box then lands ahead of the one that followed it.
                    target = groups[lastOnPage + 1];
                    goingOut.Reverse();
                    MoveGroupsInto(goingOut, target, atEnd: false, langs: move.Langs);
                }
                else if (firstOnPage - 1 >= 0)
                {
                    target = groups[firstOnPage - 1];
                    MoveGroupsInto(goingOut, target, atEnd: true, langs: move.Langs);
                }

                var stillLinked = groups.Where(group => group.PageId != pageId).ToList();
                if (stillLinked.Count == 1)
                    stillLinked[0].Group.RemoveAttribute(HtmlDom.kFlowChainAttrName);
                else if (target != null)
                    move.WalkFromPageId = target.PageId;

                moves.Add(move);
            }

            return moves;
        }

        /// <summary>
        /// Move the content of each of these groups into the target group, box by box for every
        /// language the group has. Adds each language it moved to langs.
        /// </summary>
        private static void MoveGroupsInto(
            List<FlowGroup> sourceGroups,
            FlowGroup target,
            bool atEnd,
            List<string> langs
        )
        {
            foreach (var source in sourceGroups)
            {
                foreach (var lang in GetFlowLanguages(source.Group))
                {
                    var sourceEditable = GetFlowEditable(source.Group, lang);
                    var targetEditable = GetFlowEditable(target.Group, lang);
                    if (targetEditable == null)
                    {
                        // The groups of one chain hold the same languages, so a box with nowhere
                        // to move to means the book is not what this code can work on. Say so
                        // rather than dropping the text on the floor.
                        if (HoldsOnlyPlaceholder(sourceEditable))
                            continue;
                        throw new System.ApplicationException(
                            $"flow text: the chain has no {lang} box on page {target.PageId} to keep the text of page {source.PageId}."
                        );
                    }

                    if (
                        MoveBoxContentInto(sourceEditable, targetEditable, atEnd)
                        && !langs.Contains(lang)
                    )
                        langs.Add(lang);
                }
            }
        }

        /// <summary>
        /// Every language whose box in this group is one that text flows through: a normal-style
        /// bloom-editable child with a language of its own.
        /// </summary>
        public static List<string> GetFlowLanguages(SafeXmlElement group)
        {
            var result = new List<string>();
            if (group == null)
                return result;

            foreach (var child in group.ChildNodes.OfType<SafeXmlElement>())
            {
                if (!child.HasClass("bloom-editable") || !child.HasClass(kNormalStyleClass))
                    continue;
                var lang = child.GetAttribute("lang");
                if (string.IsNullOrEmpty(lang) || lang == "z" || lang == "*")
                    continue;
                result.Add(lang);
            }

            return result;
        }

        /// <summary>
        /// Move everything one box holds into another box of the same chain: ahead of what the
        /// target already holds, or after it when atEnd. Where the two boxes' text meets, the
        /// paragraph on the later side is joined onto the paragraph it broke off from if it
        /// carries the continuation attribute. The marks that say where a box's text stopped
        /// fitting do not survive the move: they described a fit of text that has changed, and a
        /// walk puts them back where the text now runs out.
        ///
        /// Returns false, changing nothing, when the source box holds nothing a reader sees.
        /// </summary>
        public static bool MoveBoxContentInto(
            SafeXmlElement sourceEditable,
            SafeXmlElement targetEditable,
            bool atEnd
        )
        {
            if (sourceEditable == null || targetEditable == null)
                return false;
            if (HoldsOnlyPlaceholder(sourceEditable))
                return false;

            RemovePlaceholderParagraph(targetEditable);
            var targetFirstParagraph = GetTopLevelParagraphs(targetEditable).FirstOrDefault();
            var targetLastParagraph = GetTopLevelParagraphs(targetEditable).LastOrDefault();

            var moved = new List<SafeXmlNode>();
            foreach (var child in sourceEditable.ChildNodes)
            {
                moved.Add(
                    targetEditable.OwnerDocument == child.OwnerDocument
                        ? child
                        : targetEditable.OwnerDocument.ImportNode(child, true)
                );
            }

            if (atEnd)
            {
                foreach (var node in moved)
                    targetEditable.AppendChild(node);
            }
            else
            {
                SafeXmlNode after = null;
                foreach (var node in moved)
                {
                    if (after == null)
                        targetEditable.PrependChild(node);
                    else
                        targetEditable.InsertAfter(node, after);
                    after = node;
                }
            }

            var movedParagraphs = moved
                .OfType<SafeXmlElement>()
                .Where(node => node.Name == "p")
                .ToList();
            if (atEnd)
                JoinContinuationOnto(targetLastParagraph, movedParagraphs.FirstOrDefault());
            else
                JoinContinuationOnto(movedParagraphs.LastOrDefault(), targetFirstParagraph);

            RemoveOverflowMarkers(targetEditable);
            return true;
        }

        /// <summary>
        /// Put the text of a paragraph that carries on an earlier one back onto that earlier
        /// paragraph, and take the paragraph itself out. Does nothing when the later paragraph
        /// begins a paragraph of its own, which is what no continuation attribute means.
        ///
        /// The space that was cut at the join comes back from the seam attribute, the same way
        /// giveBackSeamSpace does it in the browser.
        /// </summary>
        public static void JoinContinuationOnto(
            SafeXmlElement earlierParagraph,
            SafeXmlElement laterParagraph
        )
        {
            if (earlierParagraph == null || laterParagraph == null)
                return;
            if (!laterParagraph.HasAttribute(kContinuationAttrName))
                return;

            if (laterParagraph.HasAttribute(kSeamSpaceAttrName))
                PrependSpace(laterParagraph);
            TrimInvisibleCharactersAtEnd(earlierParagraph);

            foreach (var node in laterParagraph.ChildNodes)
            {
                laterParagraph.RemoveChild(node);
                earlierParagraph.AppendChild(node);
            }

            laterParagraph.ParentNode.RemoveChild(laterParagraph);
        }

        /// <summary>Put the space of a seam back at the head of the paragraph that lost it.</summary>
        private static void PrependSpace(SafeXmlElement paragraph)
        {
            var firstText = FindFirstTextNode(paragraph);
            if (firstText != null)
                firstText.Value = " " + firstText.Value;
            else
                paragraph.PrependChild(paragraph.OwnerDocument.CreateTextNode(" "));
        }

        /// <summary>The first text node inside this element, in document order, if it has one.</summary>
        private static SafeXmlNode FindFirstTextNode(SafeXmlNode node)
        {
            if (node.NodeType == System.Xml.XmlNodeType.Text)
                return node;

            foreach (var child in node.ChildNodes)
            {
                var found = FindFirstTextNode(child);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Take the editor's own filler off the end of the paragraph another paragraph is joined
        /// onto. It is no part of what the reader sees, and it would sit between the last word of
        /// one half and the first word of the other.
        /// </summary>
        private static void TrimInvisibleCharactersAtEnd(SafeXmlElement paragraph)
        {
            var last = FindLastTextNode(paragraph);
            if (last?.Value == null)
                return;

            last.Value = last.Value.TrimEnd((char)0x200b, (char)0x200c);
        }

        /// <summary>
        /// Take out every mark that says where a box's text stopped fitting. The text of the box
        /// has changed, so the place the mark named is no longer where the text runs out.
        /// </summary>
        private static void RemoveOverflowMarkers(SafeXmlElement element)
        {
            foreach (
                var marker in element
                    .SafeSelectNodes($".//span[contains(@class,'{HtmlDom.kOverflowStartClass}')]")
                    .OfType<SafeXmlElement>()
            )
            {
                marker.ParentNode.RemoveChild(marker);
            }
        }

        /// <summary>
        /// What a join of two boxes did, so the caller can tell the browser.
        /// </summary>
        public class ContinueIntoResult
        {
            /// <summary>The chain both groups now carry.</summary>
            public string ChainId;

            /// <summary>
            /// The new content of the target box, per language. The target is the page the user is
            /// looking at, and the browser owns that page, so the browser puts this in place
            /// rather than C# writing the page under the user.
            /// </summary>
            public Dictionary<string, string> TargetHtmlByLang = new Dictionary<string, string>();

            /// <summary>Did any text actually move? A box whose text fits hands nothing over.</summary>
            public bool MovedAny;

            public IPage SourcePage;
        }

        /// <summary>
        /// Join an empty box on one page to an overflowing box on an earlier page, and move the
        /// text that does not fit into it. The source page is saved here; the target's new content
        /// comes back for the browser to apply.
        ///
        /// Every language whose box in the source group has text that does not fit moves in the
        /// same operation: each language's text flows through its own boxes, so moving one and not
        /// another would let the languages of one group drift apart.
        ///
        /// Returns null, changing nothing, when either box is no longer there.
        /// </summary>
        public static ContinueIntoResult ContinueInto(
            Book book,
            string sourcePageId,
            int sourceIndexInPage,
            string targetPageId,
            int targetIndexInPage,
            string lang
        )
        {
            var sourcePage = FindPage(book, sourcePageId);
            var targetPage = FindPage(book, targetPageId);
            var sourceGroup =
                sourcePage == null
                    ? null
                    : GetGroupAt(sourcePage.GetDivNodeForThisPage(), sourceIndexInPage);
            var targetGroup =
                targetPage == null
                    ? null
                    : GetGroupAt(targetPage.GetDivNodeForThisPage(), targetIndexInPage);
            if (sourceGroup == null || targetGroup == null)
                return null;

            var result = new ContinueIntoResult { SourcePage = sourcePage };
            var languages = GetLanguagesWithOverflow(sourceGroup);
            if (!string.IsNullOrEmpty(lang) && !languages.Contains(lang))
                languages.Add(lang);

            foreach (var language in languages)
            {
                var sourceEditable = GetFlowEditable(sourceGroup, language);
                var targetEditable = GetFlowEditable(targetGroup, language);
                if (sourceEditable == null || targetEditable == null)
                    continue;
                if (MoveTailInto(sourceEditable, targetEditable))
                    result.MovedAny = true;
                result.TargetHtmlByLang[language] = targetEditable.InnerXml;
            }

            // The chain is what makes the join permanent, so that text flows back to the earlier
            // page when the user deletes there. The source keeps the id it already has, so joining
            // a third box to a chain does not break the first two apart.
            var chainId = sourceGroup.GetAttribute(HtmlDom.kFlowChainAttrName);
            if (string.IsNullOrEmpty(chainId))
                chainId = System.Guid.NewGuid().ToString();
            sourceGroup.SetAttribute(HtmlDom.kFlowChainAttrName, chainId);
            targetGroup.SetAttribute(HtmlDom.kFlowChainAttrName, chainId);
            result.ChainId = chainId;

            // Only the source page is written. SaveForPageChanged writes the one page from the
            // book's own DOM, which is what we changed; Save would gather the page the browser is
            // showing, and that is the target page, which the browser is about to change itself.
            if (result.MovedAny)
                book.SaveForPageChanged(sourcePage.Id, sourcePage.GetDivNodeForThisPage());

            return result;
        }

        /// <summary>The page of this book with this id, or null.</summary>
        public static IPage FindPage(Book book, string pageId)
        {
            return book?.GetPages().FirstOrDefault(page => page.Id == pageId);
        }

        /// <summary>
        /// Take the chain attribute off this group and off every group of the chain that comes
        /// after it, and make the paragraph each of them began with an ordinary paragraph again.
        /// The text stays where it is. Returns the groups it changed, so the caller can save their
        /// pages.
        /// </summary>
        public static List<FlowGroup> UnlinkFrom(
            HtmlDom dom,
            string chainId,
            string fromPageId,
            int fromIndexInPage
        )
        {
            var changed = new List<FlowGroup>();
            var groups = GetChainGroups(dom, chainId);
            var startAt = groups.FindIndex(group =>
                group.PageId == fromPageId && group.IndexInPage == fromIndexInPage
            );
            if (startAt < 0)
                return changed;

            for (var i = startAt; i < groups.Count; i++)
            {
                groups[i].Group.RemoveAttribute(HtmlDom.kFlowChainAttrName);
                foreach (
                    var editable in groups[i]
                        .Group.ChildNodes.OfType<SafeXmlElement>()
                        .Where(child => child.HasClass("bloom-editable"))
                )
                {
                    foreach (
                        var paragraph in editable
                            .ChildNodes.OfType<SafeXmlElement>()
                            .Where(child => child.HasAttribute(kContinuationAttrName))
                    )
                    {
                        paragraph.RemoveAttribute(kContinuationAttrName);
                        paragraph.RemoveAttribute(kSeamSpaceAttrName);
                    }
                }

                changed.Add(groups[i]);
            }

            // A chain needs two groups to be a chain: one box on its own has nowhere to send its
            // extra text and nowhere to get any from. So a single group left in the chain leaves
            // it too.
            var stillLinked = groups.Take(startAt).ToList();
            if (stillLinked.Count == 1)
            {
                stillLinked[0].Group.RemoveAttribute(HtmlDom.kFlowChainAttrName);
                changed.Add(stillLinked[0]);
            }

            return changed;
        }

        /// <summary>
        /// The translation groups of one page that flow text can use, in document order: the ones
        /// that are not inside a bloom-canvas. The index of a group in this list is what the
        /// browser and C# use to name the same group.
        /// </summary>
        public static List<FlowGroup> GetFlowGroupsOfPage(
            SafeXmlElement pageElement,
            string pageId,
            int pageIndex
        )
        {
            var result = new List<FlowGroup>();
            if (pageElement == null)
                return result;

            var groups = HtmlDom.GetEltsWithClassNotInBloomCanvas(
                pageElement,
                "bloom-translationGroup"
            );
            for (var index = 0; index < groups.Count; index++)
            {
                result.Add(
                    new FlowGroup
                    {
                        PageId = pageId,
                        PageIndex = pageIndex,
                        IndexInPage = index,
                        Group = groups[index],
                    }
                );
            }

            return result;
        }

        /// <summary>
        /// The group at this place in this page, or null when the page has no such group.
        /// </summary>
        public static SafeXmlElement GetGroupAt(SafeXmlElement pageElement, int indexInPage)
        {
            var groups = HtmlDom.GetEltsWithClassNotInBloomCanvas(
                pageElement,
                "bloom-translationGroup"
            );
            return indexInPage >= 0 && indexInPage < groups.Count ? groups[indexInPage] : null;
        }

        public static bool IsXMatter(SafeXmlElement pageElement)
        {
            return pageElement != null
                && (
                    pageElement.HasClass("bloom-frontMatter")
                    || pageElement.HasClass("bloom-backMatter")
                );
        }

        /// <summary>
        /// The .bloom-page divs of a book DOM, in document order, with their ids and positions.
        /// </summary>
        private static List<PageDiv> GetPageDivs(HtmlDom dom)
        {
            var result = new List<PageDiv>();
            var pages = dom
                .RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]")
                .OfType<SafeXmlElement>()
                .ToArray();
            for (var index = 0; index < pages.Length; index++)
            {
                result.Add(
                    new PageDiv
                    {
                        Element = pages[index],
                        Id = pages[index].GetAttribute("id"),
                        Index = index,
                    }
                );
            }

            return result;
        }

        private class PageDiv
        {
            public SafeXmlElement Element;
            public string Id;
            public int Index;
        }

        /// <summary>
        /// The first characters of the text that does not fit, so that the offer to continue can
        /// show what would arrive.
        /// </summary>
        private static string GetPreviewAfterMarker(SafeXmlElement editable, SafeXmlElement marker)
        {
            var all = editable.InnerText ?? "";
            var head = TextBefore(editable, marker);
            var tail = all.Length > head.Length ? all.Substring(head.Length) : "";
            tail = tail.Replace(kZeroWidthNonJoiner, "").Replace(kZeroWidthSpace, "").TrimStart();
            return tail.Length > kPreviewLength ? tail.Substring(0, kPreviewLength) : tail;
        }

        /// <summary>
        /// The text of the box up to, but not including, this node.
        /// </summary>
        private static string TextBefore(SafeXmlElement editable, SafeXmlNode stopAt)
        {
            var text = new System.Text.StringBuilder();
            CollectTextBefore(editable, stopAt, text);
            return text.ToString();
        }

        private static bool CollectTextBefore(
            SafeXmlNode node,
            SafeXmlNode stopAt,
            System.Text.StringBuilder text
        )
        {
            foreach (var child in node.ChildNodes)
            {
                if (child == stopAt)
                    return true;
                if (child.NodeType == System.Xml.XmlNodeType.Text)
                    text.Append(child.Value);
                else if (CollectTextBefore(child, stopAt, text))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The child of the editable that this node is inside, which for a marker is the
        /// top-level paragraph its text belongs to.
        /// </summary>
        private static SafeXmlElement GetTopLevelAncestor(SafeXmlNode node, SafeXmlElement editable)
        {
            var current = node;
            while (current?.ParentNode != null && current.ParentNode != editable)
            {
                current = current.ParentNode;
            }

            return current?.ParentNode == editable ? current as SafeXmlElement : null;
        }

        /// <summary>
        /// The siblings that follow this node, read off the parent's children rather than by
        /// walking sibling links, because only an element knows its next sibling here.
        /// </summary>
        private static List<SafeXmlNode> GetFollowingSiblings(SafeXmlNode node)
        {
            var siblings = node.ParentNode?.ChildNodes ?? new SafeXmlNode[0];
            var at = System.Array.IndexOf(siblings, node);
            return at < 0 ? new List<SafeXmlNode>() : siblings.Skip(at + 1).ToList();
        }

        /// <summary>
        /// Is there anything in here a reader would see? A line break, the zero-width characters
        /// the editor leaves behind, and whitespace are not text.
        /// </summary>
        public static bool HasVisibleText(SafeXmlElement element)
        {
            if (element == null)
                return false;

            var text = (element.InnerText ?? "")
                .Replace(kZeroWidthNonJoiner, "")
                .Replace(kZeroWidthSpace, "");
            return !string.IsNullOrWhiteSpace(text);
        }

        /// <summary>
        /// An empty text box holds one paragraph with nothing in it but a line break. That is a
        /// placeholder, not content, so text arriving in the box replaces it.
        /// </summary>
        public static void RemovePlaceholderParagraph(SafeXmlElement editable)
        {
            if (!HoldsOnlyPlaceholder(editable))
                return;

            editable.RemoveChild(GetTopLevelParagraphs(editable)[0]);
        }

        /// <summary>
        /// Does this box hold nothing but its placeholder paragraph, so that it contributes no
        /// text at all? An empty box holds one paragraph with nothing in it a reader would see.
        /// </summary>
        public static bool HoldsOnlyPlaceholder(SafeXmlElement editable)
        {
            if (editable == null)
                return false;

            var paragraphs = GetTopLevelParagraphs(editable);
            return paragraphs.Count == 1 && !HasVisibleText(paragraphs[0]);
        }

        /// <summary>
        /// The paragraphs that are children of this element, in document order. A chained box
        /// holds top-level paragraphs and nothing else, so these are its content.
        /// </summary>
        public static List<SafeXmlElement> GetTopLevelParagraphs(SafeXmlElement element)
        {
            return element
                .ChildNodes.OfType<SafeXmlElement>()
                .Where(child => child.Name == "p")
                .ToList();
        }
    }
}
