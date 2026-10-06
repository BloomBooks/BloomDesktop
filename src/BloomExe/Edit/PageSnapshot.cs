using System;
using System.Diagnostics;
using System.Threading;

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
    /// page at all. So the browser can simply keep C# supplied: a MutationObserver in the editing
    /// page notices every change, and the content is posted 25 ms after the last one (see
    /// pageSnapshot.ts). C# then already has what a save needs, and can take it synchronously.
    ///
    /// Two properties matter and are the reason this is a class rather than two fields:
    ///
    /// 1. A snapshot belongs to ONE page. Content for a page we are no longer on must never be
    ///    written; ask for it by page id and you cannot get someone else's.
    /// 2. Whether a snapshot changes anything is decided when it is merged, not here. The browser
    ///    sends each page once as soon as it has loaded, because loading can itself change the
    ///    page (after a change of page size, images and canvas elements are laid out afresh, and
    ///    those results belong in the book), and again after every change that settles.
    ///    Book.UpdateDomFromEditedPage compares what arrives with what the book holds, so a page
    ///    the user merely looked at still writes nothing. Navigation clears the snapshot, so a
    ///    page revisited later starts empty again rather than re-applying what it had last time.
    /// </summary>
    public class PageSnapshot
    {
        private readonly object _lock = new object();
        private string _pageId;
        private string _content;

        // The page LOAD whose snapshots we are willing to believe, as the browser identified it
        // (getPageLoadId() in pageSnapshot.ts). Null between starting a navigation and the incoming
        // page reporting itself ready, which is exactly the window in which no snapshot should be
        // believed.
        //
        // The page id alone is not enough. Reloading the SAME page keeps it -- Change Layout,
        // importing a video and changing the topic all rebuild a page under its own id -- so
        // without this a snapshot posted moments before such a reload could be merged over what the
        // reload built.
        private string _loadWeAccept;

        // What the browser says is still changing the page, or null when nothing is. The browser
        // keeps a register of asynchronous work whose results belong in the saved page (sizing an
        // image, settling a paste; see pageContentDelays.ts) and tells us when that register goes
        // from empty to busy, naming the work that was registered when it became busy (only a
        // clue for the log: work added later in the same busy spell is not named, and what is
        // named may already have finished). While it is busy, the snapshot we hold predates that
        // work, so a save from it would miss whatever the work is doing; see WaitUntilIdle. The
        // browser only ever gathers an idle page, so the next snapshot is also what says the
        // work is done.
        //
        // The browser sends its busy notices and snapshots one at a time, each after the last was
        // answered, so they arrive in the order they were sent.
        private string _busyWith;

        /// <summary>
        /// Record what the browser says the page currently contains, which also means it is no
        /// longer busy. Called from the API handler, which deliberately does not take the server's
        /// sync lock — this only stores a string, and making the editor wait on a save in order to
        /// report its own content would defeat the point.
        /// </summary>
        /// <returns>False if the snapshot is from a load we are not showing, which we ignore. The
        /// browser starts sending only once we have accepted its load, so this means we have moved
        /// on from it.</returns>
        public bool Set(string pageId, string loadId, string content)
        {
            if (string.IsNullOrEmpty(pageId))
                throw new ArgumentException(
                    "A snapshot must say which page it is for",
                    nameof(pageId)
                );
            lock (_lock)
            {
                if (_loadWeAccept == null || loadId != _loadWeAccept)
                    return false;
                _pageId = pageId;
                _content = content;
                _busyWith = null;
                return true;
            }
        }

        /// <summary>
        /// The browser says the page is busy with some asynchronous work whose result belongs in
        /// the saved page; its next snapshot will say the work is done. busyWith names the work
        /// registered when it became busy; it is for the log only and need not be complete or
        /// current. Ignored, and answered false, unless it is about the load we are showing -- the
        /// same rule as Set.
        /// </summary>
        public bool SetBusy(string loadId, string busyWith)
        {
            lock (_lock)
            {
                if (_loadWeAccept == null || loadId != _loadWeAccept)
                    return false;
                _busyWith = busyWith;
                return true;
            }
        }

        /// <summary>
        /// Block the calling thread until the browser sends the snapshot that ends a busy spell,
        /// or maxMs has passed.
        /// Returns false, naming what the page was busy with, if we gave up waiting; the caller
        /// should then log that it is saving a page the browser still considers half-changed.
        ///
        /// Sleeping the UI thread is deliberate: the alternative is another asynchronous protocol
        /// for the callers that used to have one and were glad to lose it. The snapshot that ends
        /// the wait arrives on a server thread, so the sleep does not stop it.
        ///
        /// What the sleep CAN stop is the work itself, when that work needs C#. Work that runs
        /// entirely in the browser (fitting a canvas element, waiting for an image to load)
        /// finishes and releases us. Work that calls an API which runs on the UI thread cannot
        /// finish while we sleep on it; and when the save itself is inside an API handler that
        /// holds the server's sync lock (leaving the tab from the tab bar is one), work that calls
        /// any synchronised API is blocked too. In those cases the wait runs out, the save goes
        /// ahead exactly as it would have without the wait, and the log names the work -- which
        /// is how we will find out which kinds of work this matters for. The cap is kept short
        /// for that reason.
        /// </summary>
        public bool WaitUntilIdle(int maxMs, out string busyWith)
        {
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                lock (_lock)
                {
                    if (_busyWith == null)
                    {
                        busyWith = null;
                        return true;
                    }
                    if (stopwatch.ElapsedMilliseconds >= maxMs)
                    {
                        busyWith = _busyWith;
                        return false;
                    }
                }
                Thread.Sleep(20);
            }
        }

        /// <summary>
        /// Believe snapshots from this page load, and no other, until the next navigation or the
        /// next call here.
        ///
        /// Only ever called for a "page is ready" notification we ACCEPTED, i.e. one for the page
        /// we are now editing. Those notifications arrive asynchronously, so one from a page we
        /// have already left can turn up late; adopting its id would make us refuse every snapshot
        /// the page the user is actually on sends. We would then hold nothing for that page, and
        /// leaving the tab or quitting would write nothing -- losing not the last keystroke but
        /// everything since the page loaded.
        /// </summary>
        public void AcceptSnapshotsFromLoad(string loadId)
        {
            lock (_lock)
            {
                _loadWeAccept = loadId;
                _busyWith = null;
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
                _busyWith = null;
                // Forgetting which load we believe is what makes the clearing stick: until the
                // incoming page reports itself ready, every snapshot that arrives belongs to the
                // load we are leaving, and is refused rather than quietly refilling what we just
                // cleared.
                _loadWeAccept = null;
            }
        }
    }
}
