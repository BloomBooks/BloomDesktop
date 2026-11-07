using System;
using System.Diagnostics;
using System.Threading;

namespace Bloom.Edit
{
    /// <summary>
    /// The most recent copy of the page being edited, volunteered by the browser so that a save
    /// can take it synchronously instead of asking for it and waiting. Gathering the page is cheap
    /// and does not touch the live page, so the browser posts it 25 ms after each change settles
    /// (see pageSnapshot.ts).
    ///
    /// A snapshot belongs to ONE page load: content for a page we are no longer on must never be
    /// written, so it is fetched by page id and refused from any load we have not accepted.
    ///
    /// Whether a snapshot changes anything is decided when it is merged, not here. The browser
    /// also sends each page once as soon as it has loaded, because loading can itself change the
    /// page (after a change of page size, images and canvas elements are laid out afresh, and
    /// those results belong in the book). Book.UpdateDomFromEditedPage compares what arrives with
    /// what the book holds, so a page the user merely looked at still writes nothing.
    /// </summary>
    public class PageSnapshot
    {
        private readonly object _lock = new object();
        private string _pageId;
        private string _content;

        // The page LOAD whose snapshots we are willing to believe, as the browser identified it
        // (getPageLoadId() in pageSnapshot.ts). Null between starting a navigation and the incoming
        // page reporting itself ready, when no snapshot should be believed.
        //
        // The page id alone is not enough: Change Layout, importing a video and changing the topic
        // all reload a page under its own id, and a snapshot posted moments before such a reload
        // must not be merged over what the reload built.
        private string _loadWeAccept;

        // What the browser says is still changing the page, or null when nothing is. The browser
        // keeps a register of asynchronous work whose results belong in the saved page (sizing an
        // image, settling a paste; see pageContentDelays.ts) and tells us when it goes from empty
        // to busy. The name is only a clue for the log: later work in the same busy spell is not
        // named, and what is named may already have finished. While busy, the snapshot we hold
        // predates that work; see WaitUntilIdle. A snapshot says the work is done, unless it says
        // the page is still busy (the browser gave up waiting for the work, or it began just after
        // the page was read). Busy notices and snapshots are sent one at a time, so they arrive in
        // order.
        private string _busyWith;

        /// <summary>
        /// Record what the browser says the page currently contains. stillBusyWith is null when the
        /// page is idle, which is the usual case; otherwise it names work still under way, as for
        /// SetBusy. Coming in the same message as the content, it leaves no moment in which a save
        /// would take this snapshot for the finished page. The API handler deliberately does not
        /// take the server's sync lock: this only stores a string, and the editor must not wait on
        /// a save to report its content.
        /// </summary>
        /// <returns>False if the snapshot is from a load we are not showing, which we ignore.</returns>
        public bool Set(string pageId, string loadId, string content, string stillBusyWith = null)
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
                _busyWith = stillBusyWith;
                return true;
            }
        }

        /// <summary>
        /// The browser says the page is busy with asynchronous work whose result belongs in the
        /// saved page; its next snapshot will say the work is done. busyWith is for the log only.
        /// Ignored, and answered false, unless it is about the load we are showing, as for Set.
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
        /// or maxMs has passed. Returns false, naming what the page was busy with, if we gave up
        /// waiting; the caller should then log that it is saving a page the browser still
        /// considers half-changed.
        ///
        /// Sleeping the UI thread is deliberate: it keeps saving synchronous for its callers. The
        /// snapshot that ends the wait arrives on a server thread, so the sleep does not stop it.
        ///
        /// What the sleep CAN stop is the work itself, when that work needs C#. Work that runs
        /// entirely in the browser (fitting a canvas element, waiting for an image to load)
        /// finishes and releases us. Work that calls an API which runs on the UI thread cannot
        /// finish while we sleep on it; and when the save is inside an API handler that holds the
        /// server's sync lock (leaving the tab from the tab bar is one), work that calls any
        /// synchronised API is blocked too. Then the wait runs out, the save goes ahead from the
        /// snapshot we hold, and the log names the work, so we can learn which kinds of work this
        /// matters for. The cap is kept short for that reason.
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
        /// Call this only for a "page is ready" notification we ACCEPTED, i.e. one for the page we
        /// are now editing. One from a page we have already left can turn up late; adopting its id
        /// would make us refuse every snapshot from the page the user is actually on, so leaving
        /// the tab or quitting would lose everything typed since that page loaded.
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
        /// The content the browser last volunteered for this page, or null if it has sent none for
        /// this load (or the snapshot belongs to a different page), meaning there is nothing to
        /// merge.
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
        /// going away, and the book DOM (which the save just wrote) is now the truth. Otherwise,
        /// coming back to the same page later could re-apply content from the previous visit.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _pageId = null;
                _content = null;
                _busyWith = null;
                // This makes the clearing stick: until the incoming page reports itself ready,
                // snapshots from the load we are leaving are refused rather than refilling it.
                _loadWeAccept = null;
            }
        }
    }
}
