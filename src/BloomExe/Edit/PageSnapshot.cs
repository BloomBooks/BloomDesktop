using System;

namespace Bloom.Edit
{
    /// <summary>
    /// The most recent copy of the page being edited that the BROWSER volunteered, rather than one
    /// C# asked for and waited on.
    ///
    /// This exists to remove the round trip at the heart of saving. Historically, when C# wanted
    /// the current page it had to ask the browser (RequestBrowserToSave) and wait for the answer to
    /// arrive on a separate API call (editView/pageContent) — which is why saving needed states to
    /// wait in, and why anything that had to save first (leaving the Edit tab, closing the
    /// collection, a page-list command) had to be chopped into "before" and "after" halves around
    /// an asynchronous gap.
    ///
    /// Gathering the page is now cheap (~0.7 ms) and, since BL-13502, has no effect on the live
    /// page at all. So the browser can simply keep C# supplied: an idle task in the editing page
    /// posts the current content whenever the page has settled after a change. C# then already has
    /// what a save needs, and can take it synchronously.
    ///
    /// Two properties matter and are the reason this is a class rather than two fields:
    ///
    /// 1. A snapshot belongs to ONE page. Content for a page we are no longer on must never be
    ///    written; ask for it by page id and you cannot get someone else's.
    /// 2. NO snapshot means NO unsaved changes, not "we do not know". The browser posts only after
    ///    something has actually changed the page, so a page the user merely looked at never
    ///    produces one — and there is then genuinely nothing to save. Navigation clears it, so a
    ///    page revisited later starts empty again rather than re-applying what it had last time.
    /// </summary>
    public class PageSnapshot
    {
        private readonly object _lock = new object();
        private string _pageId;
        private string _content;

        /// <summary>
        /// Record what the browser says the page currently contains. Called from the API handler,
        /// which deliberately does not take the server's sync lock — this only stores a string, and
        /// making the editor wait on a save in order to report its own content would defeat the
        /// point.
        /// </summary>
        public void Set(string pageId, string content)
        {
            if (string.IsNullOrEmpty(pageId))
                throw new ArgumentException(
                    "A snapshot must say which page it is for",
                    nameof(pageId)
                );
            lock (_lock)
            {
                _pageId = pageId;
                _content = content;
            }
        }

        /// <summary>
        /// The content the browser last volunteered for this page, or null if it has not changed
        /// since it was loaded (or the snapshot belongs to a different page). Null means "nothing
        /// to save", NOT "go and ask the browser".
        /// </summary>
        public string GetFor(string pageId)
        {
            if (string.IsNullOrEmpty(pageId))
                return null;
            lock (_lock)
            {
                return _pageId == pageId ? _content : null;
            }
        }

        /// <summary>
        /// Forget everything. Called when we start navigating: the page we had a snapshot of is
        /// going away, and the copy in the book DOM (which the save just wrote) is now the truth.
        /// Without this, coming back to the same page later could re-apply content from the
        /// previous visit over what is actually in the book.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _pageId = null;
                _content = null;
            }
        }
    }
}
