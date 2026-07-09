# Item 7 scouting notes (progressive join) — 9 Jul 2026, read-only scout

Companion to DOGFOOD-BATCH-1.md item 7. Verified paths/lines as of commit e98cd809c.

## Where the join blocks today
`CloudJoinFlow.JoinCollection` (CloudJoinFlow.cs:163-235) is fully synchronous:
ClaimMemberships → DetermineScenario/folder → write TeamCollectionLink → HydrateFromServer
(line 231: populates CloudRepoCache with book list/titles/seqs, NO content) →
CopyRepoCollectionFilesToLocal (232: the .bloomCollection settings + collection files) →
**CopyAllBooksFromRepoToLocalFolder (233: the blocking bulk download to defer)**.
SharingApi.HandlePullDown (SharingApi.cs:498-566) replies with collectionPath only after all
of that returns; TS then posts workspace/openCollection → Program.SwitchToCollection
(Program.cs:1842-1847). Settings + titles are available WITHOUT any book download.
Note: cloud pull-down does NOT set NextMergeIsFirstTimeJoinCollection (folder TC does, at
FolderTeamCollection.cs:1445) — so reopen runs SyncAtStartup with firstTimeJoin:false.

## Resume path (already exists, verified)
SyncAtStartup (TeamCollection.cs:2226; repo-book loop 2507-2558): a repo book with no local
folder hits the "brand new book! Get it." branch → CopyBookFromRepoToLocalAndReport. Pinned
by CloudSyncAtStartupTests.SyncAtStartup_NewBookOnlyInRepo_IsFetchedToLocal (251-293).
CAVEAT: it runs synchronously inside the "Syncing Team Collection" progress dialog
(SynchronizeRepoAndLocal, TeamCollection.cs:2960-3032) — a half-joined collection would
block its next open until all missing books download, so item 7 must make this path skip
cloud missing-book fetches (hand them to the background queue instead).

## Book list / placeholder seam
CollectionApi.HandleBooksRequest (CollectionApi.cs:559-647, endpoint collections/books)
builds the JSON from BookCollection.GetBookInfos (BookCollection.cs:297-355), which is
LOCAL-DISK-ONLY — repo books with no local folder are simply absent. Cleanest seam: merge
CloudTeamCollection.GetBookList()/repo-cache titles into HandleBooksRequest's JSON with a
`notYetDownloaded: true` flag (keeps BookCollection disk-only). TS: BooksOfCollection.tsx
renders via LazyLoad with existing BookButtonPlaceHolder (BookButton.tsx:679-698, already
listens for a bookImage/reload websocket to swap in the real thumbnail); thumbnail API
falls back to a placeholder image for missing folders (CollectionApi.cs:755-789).

## Selection priority + "downloading" status
collections/selected-book POST (CollectionApi.cs:149-208) catches-and-logs failures for
missing folders (198-208) — natural place for the "prioritize this download" hint.
Status panel: clone the offlineDisabled pattern — server seam
TeamCollectionApi.AddCloudBookStatusFields (TeamCollectionApi.cs:912-943, populates
offlineDisabledReason when disconnected && no local seq); client union + branch + render in
TeamCollectionBookStatusPanel.tsx (union line 63, effect 104-160, render ~805).

## Queue reuse
RemoteBookAutoApplyQueue (single-consumer FIFO dedupe; TeamCollection.cs lazily constructs
at 117-128) is the right vehicle but has NO priority mechanism — selection-priority needs a
front-of-queue Enqueue variant. CloudBookTransfer.DownloadFiles (CloudBookTransfer.cs:
340-431) hash-skips already-present files → interrupted downloads resume for free; the
whole-folder atomic swap lives in FetchBookFromRepo (CloudTeamCollection.cs:735-821), so
the placeholder-only-or-fully-downloaded invariant already holds at folder level.

## Test landscape
No CloudJoinFlow test file exists. CloudSyncAtStartupTests uses a fake HTTP executor
(pattern to copy). No test covers HandleBooksRequest JSON shape (placeholder entries land
uncovered unless added). Queue seams: TestOnly_MakeAutoApplyQueueSynchronous /
TestOnly_ProcessAutoApplyRemoteChange (TeamCollection.cs:136-150).

## Sketch (for the implementation brief)
1. CloudJoinFlow: drop line 233; after settings download, enqueue all repo books.
2. HandleBooksRequest: synthetic notYetDownloaded entries from the repo cache.
3. Background queue drains; each completion broadcasts bookImage/reload + status refresh.
4. selected-book handler: front-of-queue bump + "Downloading…" panel state via
   AddCloudBookStatusFields; selection of a missing book must not crash (currently
   catch-and-log path).
5. SyncAtStartup for cloud: missing books → queue instead of synchronous fetch (keeps the
   startup progress dialog fast AND preserves resume semantics).
