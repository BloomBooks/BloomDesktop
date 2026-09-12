using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bloom.SafeXml;

namespace Bloom.Book
{
    /// <summary>
    /// What Bloom reports about the use of flow text, which is three questions and nothing else:
    /// how many people use it, how many distinct books they use it on, and over what range of
    /// pages a run of text is spread.
    ///
    /// One event answers all three. "Flow Text" carries the book's id, which makes the books
    /// countable, and the number of pages spanned by the longest run in that book, which is the
    /// figure the range is read from. Everything else a reader might want is already on every
    /// event: the person is Segment's own identity, and the country, branding and collection
    /// languages are application properties set in CollectionSettings.
    ///
    /// The event is sent only when a book's longest run reaches a length not yet reported for
    /// that book in this Bloom run. Typing sends nothing, and a refit that merely changes where
    /// the lines break sends nothing; a run growing from two pages to nine sends a handful of
    /// events over a session rather than one per keystroke.
    ///
    /// Reporting growth, rather than waiting for the author to be finished with a book, is
    /// deliberate: there is no moment that reliably means finished. The author can close Bloom,
    /// change collections, or crash, and the edit view has already lost the outgoing book by the
    /// time its selection handler runs. Reporting each new maximum means the longest run the book
    /// ever reached is in the data whatever happens next.
    /// </summary>
    public static class FlowTextAnalytics
    {
        private const string kEventName = "Flow Text";

        private static readonly object _lock = new object();

        /// <summary>
        /// The longest run, in pages, already reported for each book in this Bloom run. Held for
        /// the life of the process, so a book closed and opened again reports nothing further
        /// unless its run grows: one book that reached nine pages belongs in the data once at
        /// nine, not once per visit.
        /// </summary>
        private static readonly Dictionary<string, int> _largestReported =
            new Dictionary<string, int>();

        /// <summary>
        /// Report this book's longest run of flowing text, if it is longer than anything already
        /// reported for the book.
        ///
        /// pageJustSaved, when it is given, is the page whose save brought us here, and is only a
        /// short cut: a page with no box of a chain on it cannot have lengthened a run, so there
        /// is nothing to look at. Pass nothing to have the whole book read.
        /// </summary>
        public static void ReportIfRunGrew(Book book, SafeXmlElement pageJustSaved = null)
        {
            if (book == null)
                return;
            if (pageJustSaved != null && !HoldsAnyChain(pageJustSaved))
                return;

            var bookId = book.ID;
            if (string.IsNullOrEmpty(bookId))
                return;

            var pages = LongestRunInPages(book.OurHtmlDom);
            if (!ShouldReport(bookId, pages))
                return;

            BloomAnalytics.Track(
                kEventName,
                new Dictionary<string, string>
                {
                    { "BookId", bookId },
                    { "pages", pages.ToString(CultureInfo.InvariantCulture) },
                }
            );
        }

        /// <summary>Does this page hold any box that belongs to a chain?</summary>
        private static bool HoldsAnyChain(SafeXmlElement page)
        {
            return page.SafeSelectNodes($".//*[@{HtmlDom.kFlowChainAttrName}]")
                .OfType<SafeXmlElement>()
                .Any();
        }

        /// <summary>
        /// Whether this length is worth reporting for this book, and if it is, remember it. The
        /// comparison and the remembering are one locked operation because two threads ask: the
        /// UI thread as a page is saved, and the walk's own thread as a refit ends.
        /// </summary>
        internal static bool ShouldReport(string bookId, int pages)
        {
            if (pages <= 0)
                return false;
            lock (_lock)
            {
                if (_largestReported.TryGetValue(bookId, out var already) && already >= pages)
                    return false;
                _largestReported[bookId] = pages;
                return true;
            }
        }

        /// <summary>
        /// How many pages the longest run of flowing text in this book is spread over. Zero when
        /// the book has no run at all.
        ///
        /// A chain of two boxes on one page counts as one page. That is real use of the feature
        /// and worth telling apart from no use. A lone box carrying a chain attribute is not a
        /// chain and counts for nothing: it has nowhere to send its text, which is the same rule
        /// FlowTextWalk.QueueEveryChain applies when it decides what is worth refitting.
        /// </summary>
        internal static int LongestRunInPages(HtmlDom dom)
        {
            if (dom == null)
                return 0;

            var longest = 0;
            foreach (var chainId in FlowTextWalk.GetChainIds(dom))
            {
                var groups = FlowTextChains.GetChainGroups(dom, chainId);
                if (groups.Count < 2)
                    continue;
                var pages = groups.Select(group => group.PageId).Distinct().Count();
                if (pages > longest)
                    longest = pages;
            }

            return longest;
        }

        /// <summary>Forget what has been reported, so that one test cannot affect another.</summary>
        internal static void ForgetForTests()
        {
            lock (_lock)
                _largestReported.Clear();
        }
    }
}
