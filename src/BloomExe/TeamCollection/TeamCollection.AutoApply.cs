using System;
using System.IO;
using SIL.Reporting;

namespace Bloom.TeamCollection
{
    public abstract partial class TeamCollection
    {
        /// <summary>
        /// True for backends where a remote change to a book that is safe to apply (not checked
        /// out here, no local edits that would be clobbered) should be downloaded and swapped in
        /// automatically, rather than merely reported via a "click Reload/Sync" message. False (the
        /// default) preserves every folder-TC behavior exactly. See
        /// <see cref="Cloud.CloudTeamCollection"/>'s override and <see cref="HandleModifiedFile"/>'s
        /// use of this flag.
        /// </summary>
        protected virtual bool CanAutoApplyRemoteChanges => false;

        private RemoteBookAutoApplyQueue _autoApplyQueue;
        private bool _autoApplyQueueSynchronousForTests;

        /// <summary>
        /// Lazily-created so a backend that never sets <see cref="CanAutoApplyRemoteChanges"/> true
        /// (every folder TC) never spins up any queueing machinery at all.
        /// </summary>
        private RemoteBookAutoApplyQueue AutoApplyQueue =>
            _autoApplyQueue ??= new RemoteBookAutoApplyQueue(
                ProcessAutoApplyRemoteChange,
                _autoApplyQueueSynchronousForTests ? (Action<Action>)(action => action()) : null
            );

        /// <summary>
        /// For testing only. Makes the auto-apply queue's worker run synchronously (on the calling
        /// thread, immediately, inside Enqueue) instead of via a background task, so a test can
        /// assert on the outcome of HandleModifiedFile's auto-apply path without needing to wait
        /// for a real background thread. Must be called before the first book is queued.
        /// </summary>
        internal void TestOnly_MakeAutoApplyQueueSynchronous()
        {
            _autoApplyQueueSynchronousForTests = true;
        }

        /// <summary>
        /// For testing only. Directly invokes the auto-apply worker's eligibility re-verification
        /// and copy logic for one book, bypassing the queue entirely -- lets a test exercise
        /// ProcessAutoApplyRemoteChange's behavior for a specific state without needing to win a
        /// real race between queueing and background processing.
        /// </summary>
        internal void TestOnly_ProcessAutoApplyRemoteChange(string bookBaseName)
        {
            ProcessAutoApplyRemoteChange(bookBaseName);
        }

        /// <summary>
        /// Batch item 7 (progressive join): queues a repo book (by folder name) for background
        /// download via the same single-consumer queue <see cref="CanAutoApplyRemoteChanges"/>
        /// backends use to auto-apply remote changes. Used by <see cref="Cloud.CloudJoinFlow"/>
        /// right after joining (instead of blocking the join on every book's full download) and by
        /// <see cref="SyncAtStartup"/>'s cloud rerouting for books that are still missing locally.
        /// A no-op for backends that don't set <see cref="CanAutoApplyRemoteChanges"/> true (every
        /// folder TC): a folder TC has no use for background-downloading a book with no local
        /// folder at all, so this simply does nothing rather than spinning up queueing machinery it
        /// will never need.
        /// </summary>
        internal void QueueBookForBackgroundDownload(string bookName)
        {
            if (!CanAutoApplyRemoteChanges)
                return;
            AutoApplyQueue.Enqueue(bookName);
        }

        /// <summary>
        /// Like <see cref="QueueBookForBackgroundDownload"/>, but jumps the book to the front of
        /// the queue (see <see cref="RemoteBookAutoApplyQueue.EnqueueFront"/>) -- used when the
        /// user explicitly selects a not-yet-downloaded book, so it arrives before books that were
        /// only queued in the background.
        /// </summary>
        internal void PrioritizeBackgroundDownload(string bookName)
        {
            if (!CanAutoApplyRemoteChanges)
                return;
            AutoApplyQueue.EnqueueFront(bookName);
        }

        /// <summary>
        /// Queues a background download for every repo book that has no local folder and is not
        /// checked out by the current user. This is the self-healing safety net for the
        /// progressive-join pipeline (batch item 7): the in-memory download queue does not survive
        /// a Bloom restart (the join flow's pullDown-then-relaunch pattern, or a crash mid-join),
        /// and a book the queue somehow dropped would otherwise never be retried, because the poll
        /// only raises change events for books whose repo state CHANGED since the last poll.
        /// Called when monitoring starts (right after the startup sync) and again after every
        /// poll, so any locally-missing repo book is re-queued within one poll interval no matter
        /// how it was missed. Enqueue's dedupe makes repeat calls cheap and safe.
        /// Books locked by the CURRENT USER ON THIS MACHINE are skipped: that is the
        /// local-rename-mid-checkin edge, where the OLD repo name intentionally has no local
        /// folder and downloading it would resurrect the pre-rename book. Any other lock must NOT
        /// suppress the download -- a teammate's lock (e2e-4, 10 Jul 2026: an any-lock skip
        /// turned one transient download failure into "book missing for as long as the teammate
        /// held the lock") and even the current user's own lock taken on a DIFFERENT machine
        /// (preflight review finding, same day: the rename edge is machine-local, "checked out
        /// here" everywhere else in this file means lockedBy AND lockedWhere match, and
        /// SyncAtStartup happily fetches such a book on restart -- the retry pass must agree
        /// with it) both describe committed content that is exactly what Receive would fetch.
        /// </summary>
        internal void QueueMissingRepoBooksForBackgroundDownload()
        {
            if (!CanAutoApplyRemoteChanges)
                return;
            // Caller-owned scan state for the rename check: a backend that would otherwise redo
            // per-book work across this loop builds an index into it the first time it's needed
            // and reuses it for the rest of this pass (see NewBookRenamedFrom(name, ref scanState)).
            // A local, so it's fresh every poll and confined to this thread.
            object renameScanState = null;
            foreach (var bookName in GetBookList())
            {
                if (Directory.Exists(Path.Combine(_localCollectionFolder, bookName)))
                    continue;
                // A repo book with no folder of ITS name but which is a rename of an existing
                // local book is NOT missing -- downloading it would create a duplicate of that
                // book under the new name (bug #18). The rename itself is applied by the next
                // sync's rename-from-remote pass; HandleModifiedFile/HandleNewBook report it.
                var renamedFrom = NewBookRenamedFrom(bookName, ref renameScanState);
                if (renamedFrom != null)
                {
                    Logger.WriteEvent(
                        $"TeamCollection: repo book '{bookName}' is a rename of local book '{renamedFrom}'; not queueing a download."
                    );
                    continue;
                }
                var lockedBy = WhoHasBookLocked(bookName);
                if (
                    !string.IsNullOrEmpty(lockedBy)
                    && string.Equals(
                        lockedBy,
                        CurrentUserIdentity,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && WhatComputerHasBookLocked(bookName) == TeamCollectionManager.CurrentMachine
                )
                    continue;
                Logger.WriteEvent(
                    $"TeamCollection: repo book '{bookName}' has no local folder; queueing background download."
                );
                QueueBookForBackgroundDownload(bookName);
            }
        }

        /// <summary>
        /// Runs on a background thread (see <see cref="AutoApplyQueue"/> and
        /// <see cref="CanAutoApplyRemoteChanges"/>): re-verifies that it is still safe to apply this
        /// book's remote change -- the state at the time this runs may differ from the state at the
        /// time HandleModifiedFile queued it, since queueing and processing happen at different
        /// times -- and if so, downloads and atomically swaps in the new content
        /// (CopyBookFromRepoToLocal already stages to a temp folder and swaps via directory
        /// renames, so there is no user-visible half-written state). On success, updates the book's
        /// status icon and, if this book is the one currently selected, tells the preview to
        /// refresh so the user sees the new content without reselecting. On failure, or if the book
        /// is no longer eligible, falls back to exactly the same NewStuff message an
        /// auto-apply-incapable backend (e.g. a folder TC) would have written instead.
        ///
        /// Batch item 7 (progressive join): this same queue also carries books that have NO local
        /// folder at all yet (queued by <see cref="QueueBookForBackgroundDownload"/> from
        /// <see cref="Cloud.CloudJoinFlow"/> or <see cref="SyncAtStartup"/>'s cloud rerouting).
        /// That case is simpler -- there's no existing local content to re-verify eligibility
        /// against or protect -- so it's handled by <see cref="DownloadMissingBookInBackground"/>
        /// instead of the auto-apply re-verification below.
        /// </summary>
        private void ProcessAutoApplyRemoteChange(string bookBaseName)
        {
            if (!Directory.Exists(Path.Combine(_localCollectionFolder, bookBaseName)))
            {
                DownloadMissingBookInBackground(bookBaseName);
                return;
            }

            // Re-verify eligibility: none of these should be true, or auto-applying now would be
            // wrong -- e.g. the user might have checked the book out here, or a conflicting local
            // edit might have appeared, since this book was queued.
            if (
                !HasBeenChangedRemotely(bookBaseName)
                || IsCheckedOutHereBy(GetLocalStatus(bookBaseName))
                || HasLocalChangesThatMustBeClobbered(bookBaseName)
                || HasCheckoutConflict(bookBaseName)
            )
                return; // no longer (or not yet) safe/needed; leave it for the normal handling

            // Batch item 8: if the local copy somehow changed since the last sync (e.g. a
            // force-stolen checkout that had local edits -- a case the eligibility gates above
            // cannot see for cloud, since cloud checkouts don't write the local status file),
            // preserve it before the swap below discards it.
            PreserveLocalCopyIfModifiedSinceLastSync(bookBaseName);

            var error = CopyBookFromRepoToLocal(bookBaseName);
            if (error != null)
            {
                // Fall back to exactly the message-only path so the user at least knows to Sync/Reload.
                _tcLog.WriteMessage(
                    MessageAndMilestoneType.NewStuff,
                    "TeamCollection.BookModifiedRemotely",
                    "One of your teammates has made changes to the book '{0}'",
                    bookBaseName,
                    null
                );
                UpdateBookStatus(bookBaseName, true);
                return;
            }

            UpdateBookStatus(bookBaseName, true);

            // If the book we just updated is the one currently selected, refresh the preview so the
            // user sees the new content without having to reselect the book.
            var selectedFolder = _tcManager?.BookSelection?.CurrentSelection?.FolderPath;
            if (
                !string.IsNullOrEmpty(selectedFolder)
                && string.Equals(
                    Path.GetFileName(selectedFolder),
                    bookBaseName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                _tcManager.SendBookContentReload();
            }
        }

        /// <summary>
        /// Batch item 7 (progressive join): downloads a repo book that has no local folder at all
        /// yet (queued by <see cref="QueueBookForBackgroundDownload"/>/<see cref="PrioritizeBackgroundDownload"/>,
        /// e.g. right after joining a cloud collection, or by <see cref="SyncAtStartup"/>'s cloud
        /// rerouting for a half-joined collection's next open). Unlike
        /// <see cref="ProcessAutoApplyRemoteChange"/>'s re-verification, there is no existing local
        /// content to check eligibility against or protect -- the only thing that could have
        /// changed since queueing is that the book itself vanished from the repo (e.g. deleted
        /// before its background download ran), which <see cref="IsBookPresentInRepo"/> catches.
        /// On success, invalidates the cached book list and tells the collection-tab UI to reload
        /// it so the not-yet-downloaded placeholder (see CollectionApi.HandleBooksRequest) swaps
        /// for the real book button.
        /// </summary>
        private void DownloadMissingBookInBackground(string bookBaseName)
        {
            if (!IsBookPresentInRepo(bookBaseName))
            {
                // Usually the book was deleted/renamed remotely between queueing and now; but a
                // cache problem would look identical, so never skip SILENTLY (the first post-batch
                // full matrix, 10 Jul 2026, lost a book to an undiagnosable silent drop here --
                // this log line plus the QueueMissingRepoBooksForBackgroundDownload retry pass are
                // the fix).
                Logger.WriteEvent(
                    $"Background download of '{bookBaseName}' skipped: the current repo cache does not list it (deleted remotely, renamed, or a cache problem)."
                );
                return;
            }

            // Guard against the queued-before-the-rename-landed race (bug #18): by the time this
            // runs, the "missing" repo book may have turned out to be a rename of an existing
            // local book -- downloading it would duplicate that book under its new name. (After
            // the presence check on purpose: rename detection reads the repo book's meta.json,
            // which a repo-deleted book no longer has.)
            var renamedFrom = NewBookRenamedFrom(bookBaseName);
            if (renamedFrom != null)
            {
                Logger.WriteEvent(
                    $"Background download of '{bookBaseName}' skipped: it is a rename of the local book '{renamedFrom}' (applied at the next sync)."
                );
                return;
            }

            var error = CopyBookFromRepoToLocal(bookBaseName);
            if (error != null)
            {
                Logger.WriteEvent(
                    $"Background download of new book '{bookBaseName}' failed: {error}"
                );
                return;
            }
            Logger.WriteEvent($"Background download of new book '{bookBaseName}' completed.");
            UpdateBookStatus(bookBaseName, true);

            // Swap the placeholder for the real book button: the JSON collections/books merge
            // (CollectionApi.HandleBooksRequest) only shows a placeholder while GetBookInfos()
            // finds no matching local folder, so the cached book list must be invalidated before
            // the client re-fetches it.
            _bookCollectionHolder?.TheOneEditableCollection?.InvalidateBookList();
            SocketServer?.SendEvent("editableCollectionList", "reload:" + _localCollectionFolder);
        }
    }
}
