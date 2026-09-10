using System;
using System.Collections.Generic;
using Bloom.SafeXml;

namespace Bloom.Book
{
    /// <summary>
    /// Making the pages that the rest of a run of text needs.
    ///
    /// The last box of a chain holds the mark that says where its text stops fitting, and no box
    /// comes after it to take the rest. This adds a text-only page, links its box into the chain,
    /// moves the text that does not fit into it, asks where THAT box's text stops fitting, and
    /// goes round again until one page holds the whole of what is left.
    ///
    /// Everything only a browser can answer is a delegate the caller passes in: adding a page and
    /// the fit of one page. So the loop, which is the part that can go wrong, runs in a unit test
    /// with no browser at all (FlowTextCreatePagesTests), and FlowTextApi is the layer that binds
    /// those delegates to the real book, the Add Page code path, and FlowTextWalk's off-screen
    /// browser.
    ///
    /// The source box is on the page being edited, which the browser owns, so nothing here writes
    /// or saves that page: what the box keeps goes back in the reply for the browser to apply.
    /// </summary>
    public static class FlowTextCreatePages
    {
        /// <summary>
        /// The most pages one request will make.
        ///
        /// The loop ends on its own as soon as a page holds the whole of what is left, so this is
        /// reached only if the fit never says that. Without a cap such a run would go on adding
        /// pages until it had filled the book. The last page allowed is measured as the last box
        /// of the chain, so it keeps every remaining word, with the mark that says its text runs
        /// out; the run is then still whole and the button offers the rest of the work again.
        /// </summary>
        public const int kMaxPagesToCreate = 100;

        /// <summary>
        /// What making the pages left the book holding.
        /// </summary>
        public class Result
        {
            /// <summary>
            /// The chain every one of these boxes now carries, made here when the source box was
            /// in no chain before.
            /// </summary>
            public string ChainId;

            /// <summary>
            /// The source box's new content: what fits in it, the rest taken out. The browser
            /// puts this in place itself, because it owns the page it is editing.
            /// </summary>
            public string SourceHtml;

            /// <summary>
            /// The group added on each new page, in the order the text flows through them. The
            /// caller saves their pages: they are pages nobody is editing.
            /// </summary>
            public List<FlowTextChains.FlowGroup> CreatedGroups =
                new List<FlowTextChains.FlowGroup>();

            /// <summary>How many pages were made.</summary>
            public int PagesCreated => CreatedGroups.Count;

            /// <summary>
            /// True when the cap stopped the work rather than the text running out, so the last
            /// page made still holds more text than fits it.
            /// </summary>
            public bool HitCap;
        }

        /// <summary>
        /// Make pages for the text that does not fit in this box, and divide that text among
        /// them. Returns null, changing nothing, when the box's text all fits: there is then
        /// nothing to make pages for.
        ///
        /// addPage adds one text-only page after the last page of the chain and returns the group
        /// on it that the text flows into. fit is asked, once per page made, where that box's
        /// text stops fitting: it is given the group, whose box already holds the whole of what is
        /// left, and whether this is the last box the text may use.
        ///
        /// The pages are left in the book's DOM for the caller to save. Nothing is saved here,
        /// because a run of text divided among pages is right or wrong as a whole: the check that
        /// no word was lost or repeated can only be made once every page has its share.
        /// </summary>
        public static Result Run(
            FlowTextChains.FlowGroup sourceGroup,
            string lang,
            Func<FlowTextChains.FlowGroup> addPage,
            Func<FlowTextChains.FlowGroup, bool, FlowTextWalk.FitResult> fit,
            int maxPages = kMaxPagesToCreate
        )
        {
            var sourceEditable = FlowTextChains.GetFlowEditable(sourceGroup.Group, lang);
            if (sourceEditable == null)
                throw new ApplicationException(
                    $"flow text: the group on page {sourceGroup.PageId} has no {lang} box, so "
                        + "there is no text to make pages for."
                );

            // The browser put the mark where this box's text stops fitting, so no measuring is
            // needed to divide it: what is before the mark fits the box as it stands.
            if (FlowTextChains.FindOverflowMarker(sourceEditable) == null)
                return null;

            var sourceOnly = new List<FlowTextChains.FlowGroup> { sourceGroup };
            var wordsBefore = FlowTextWalk.ComparableWords(
                FlowTextWalk.CollectRun(sourceOnly, 0, lang).InnerText
            );

            // A chain is what makes these pages one run of text: it is how the text finds its way
            // back to this box when the user deletes some of it further on. A box that was in no
            // chain starts one here.
            var chainId = sourceGroup.Group.GetAttribute(HtmlDom.kFlowChainAttrName);
            if (string.IsNullOrEmpty(chainId))
                chainId = Guid.NewGuid().ToString();
            sourceGroup.Group.SetAttribute(HtmlDom.kFlowChainAttrName, chainId);

            var result = new Result { ChainId = chainId };
            // Somewhere to put a piece of content when we need to ask a question about it rather
            // than write it into a box.
            var scratch = sourceGroup.Group.OwnerDocument.CreateElement("div");
            // What the page being made has to hold. Null on the first time round, where the text
            // comes out of the source box instead, at its mark.
            string remaining = null;

            while (remaining == null || HoldsAnyText(scratch, remaining))
            {
                var isLast = result.PagesCreated + 1 >= maxPages;
                var group = addPage();
                var editable = FlowTextChains.GetFlowEditable(group.Group, lang);
                if (editable == null)
                    throw new ApplicationException(
                        $"flow text: the page just made, {group.PageId}, has no {lang} box for "
                            + "the text to flow into."
                    );

                group.Group.SetAttribute(HtmlDom.kFlowChainAttrName, chainId);
                if (remaining == null)
                    FlowTextChains.MoveTailInto(sourceEditable, editable);
                else
                    HtmlDom.SetInnerHtmlFromFragment(editable, remaining);

                // The box holds the whole of what is left while the fit is measured: where its
                // text stops fitting is a question about this box's layout with this text in it.
                // The answer says nothing about the markers that record that the box's first
                // paragraph carries on a paragraph in the box before it, so they are read here
                // and put back afterwards.
                var markers = FlowTextWalk.ReadContinuationMarkers(editable);
                var fitted = fit(group, isLast);
                HtmlDom.SetInnerHtmlFromFragment(editable, fitted.head);
                FlowTextWalk.WriteContinuationMarkers(editable, markers);
                remaining = fitted.tail ?? "";

                result.CreatedGroups.Add(group);

                if (isLast && HoldsAnyText(scratch, remaining))
                {
                    // The cap has stopped the work with text still in hand. It goes into this
                    // box, however over-full that leaves it: text in no box at all is text the
                    // book has lost. The box then still holds more than fits, so the button
                    // offers the rest of the work again.
                    result.HitCap = true;
                    AppendKeepingSeam(editable, remaining);
                    remaining = "";
                }
            }

            result.SourceHtml = sourceEditable.InnerXml;

            // The run is the whole of what this box carried, and it has just been divided among
            // pages. A word dropped or repeated between two of them is a word dropped or repeated
            // in the book, so say so rather than letting the caller save it.
            var everyGroup = new List<FlowTextChains.FlowGroup> { sourceGroup };
            everyGroup.AddRange(result.CreatedGroups);
            var wordsAfter = FlowTextWalk.ComparableWords(
                FlowTextWalk.CollectRun(everyGroup, 0, lang).InnerText
            );
            if (wordsAfter != wordsBefore)
                throw new ApplicationException(
                    "flow text: making pages for the rest of the text changed the text. It was "
                        + $"{wordsBefore.Length} characters and is now {wordsAfter.Length}, over "
                        + $"{result.PagesCreated} new page(s)."
                );

            return result;
        }

        /// <summary>
        /// Add this content to the end of what the box holds, joining its first paragraph back
        /// onto the paragraph it broke off from when it is that paragraph's tail. Otherwise the
        /// two halves of one paragraph would sit in the box as two paragraphs.
        /// </summary>
        private static void AppendKeepingSeam(SafeXmlElement editable, string html)
        {
            var lastBefore = FlowTextChains.GetTopLevelParagraphs(editable).Count;
            var scratch = editable.OwnerDocument.CreateElement("div");
            HtmlDom.SetInnerHtmlFromFragment(scratch, html);

            foreach (var node in scratch.ChildNodes)
            {
                scratch.RemoveChild(node);
                editable.AppendChild(node);
            }

            var paragraphs = FlowTextChains.GetTopLevelParagraphs(editable);
            if (lastBefore > 0 && paragraphs.Count > lastBefore)
                FlowTextChains.JoinContinuationOnto(
                    paragraphs[lastBefore - 1],
                    paragraphs[lastBefore]
                );
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
    }
}
