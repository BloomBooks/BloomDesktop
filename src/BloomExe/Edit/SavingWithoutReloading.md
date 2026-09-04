# Saving a page without reloading it — what it enables

## A note on shape: this document is part history

The sections up to "The page snapshot" were written *before* the snapshot existed, while the branch
was still converting callers one at a time and waiting on a round trip. They are kept because they
record why each caller was converted (or deliberately not), and what the round trip actually cost,
measured. Read them as the reasoning that led here, not as a description of the current code: what
they call `SaveThen` is now `EditingModel.MergeCurrentPageThenSave`, and the waiting states they
discuss no longer exist. Where an earlier section and a later one disagree, the later one is what
was built.

## What changed

Historically, gathering the current page's content for a save **wrecked the live page**. The
browser stripped the editing markup out of the real DOM (detached the toolbox tool, unmounted the
above-page controls, removed the origami layout mode and text-box labels, killed the niceScroll
bars, and rewrote every `bloom-editable`'s `innerHTML` with CKEditor's cleaned-up data). The page
that was left could be saved but not edited, which is exactly what the `SavedAndStripped` state in
`EditingStateMachine` records, and why **every** save had to end by navigating to some page
(BL-13502).

That is no longer true:

- `getBodyContentForSavePage()` (`bookEdit/js/bloomEditing.ts`) now **clones** the body and does all
  the stripping on the clone. **Nothing at all is done to the live page** — it is not touched, so
  there is nothing to put back.
- Canvas-element editing is no longer turned off and on around the save. `turnOffCanvasElementEditing()`
  did three things that affect what gets saved, and `CanvasElementManager.prepareCloneOfBodyForSave()`
  now does all three against the clone: Comical's bubble-tail `<svg>` (via
  `Comical.exportSvgToCopiesOfParents`, added in comicaljs 0.4.1 as the non-destructive counterpart of
  `stopEditing()`), the canvas element positions recorded as the current language's alternate (pure
  attribute manipulation, so a clone with no layout is fine), and the `bloom-focusedCanvasElement`
  class. The rest of what that method does is live-only: the control frame is `bloom-ui` so C#
  discards it anyway, `EnableAllImageEditing` only puts `bloom-ui` buttons back, and the listener
  removal has no bearing on the HTML.
- CKEditor's cleaned-up text is read from the live editors and written into the clone
  (`EditableDivUtils.copyCkEditorDataToClone`) rather than written back over the live editors.
- The scroll bars are cleaned off the clone by our own `removeNiceScrollArtifacts`
  (`bookEdit/js/niceScrollCleanup.ts`) instead of by asking the live niceScroll instances to remove
  themselves, so a save no longer disturbs the scroll bars the user is looking at. It handles the
  inserted rails/cursors, the alignment classes `addScrollbarsToPage()` moved aside (the part that
  would otherwise have been real data loss), and the three inline styles niceScroll sets without
  recording. The live page still uses bloom-player's `cleanupNiceScroll()` at page setup, since
  only that can tear down the instances themselves.
- `ITool` gained **one** new method, `removeToolMarkup(pageOrClone)`, which is used two ways rather
  than duplicated: the save path calls it on a clone of the `.bloom-page` div, and
  `ToolboxToolReactAdaptor.detachFromPage()` calls it on the live one. A tool with nothing
  live-only to clean up implements only `removeToolMarkup` and gets both behaviours; a tool that
  does (observers, React state, re-enabling image editing) overrides `detachFromPage` and calls
  `super.detachFromPage()` at the point where the markup should come off. `detachCurrentTool()`
  logs a console error if an override forgets that `super` call, because the symptom otherwise
  shows up much later as tool markup saved into the book.
- We no longer blur the active element while saving, so the user's cursor stays where it was.

On top of that:

- `EditingStateMachine.ToSavedInPlace(pageContentData, reportFailure)` — a save that begins and ends
  in `Editing`. No `SavePending` wait, no `SavedAndStripped`, no navigation.
- `EditingStateMachine.ToSavedInPlaceThenNavigating(pageContentData, doBeforeSaveToDisk,
  reportFailure)` — the same thing for a request that also has to *change* something and then show
  another page. `doBeforeSaveToDisk` plays exactly the role it plays in `ToSavePending`: it runs
  after the browser's content is in the book DOM and before the book is written to disk, and
  returns the page to go to. So the whole `SavePending → SavedAndStripped → Navigating` sequence
  collapses into one `Editing → Navigating` step.
- **`EditingModel.SaveThen(..., pageContentFromBrowser)`** — the way in. Given the content it does
  the whole save here and now (privately, via `SavePageInPlaceThen`); without it, or if we turn out
  not to be in a state to save, it asks the browser exactly as it always did. So a caller opts in
  by passing one more argument and needs to know nothing else: in particular it does not have to
  know that only a `Declined` outcome may fall back, which is the rule that, got wrong, deletes a
  page twice. Both routes reuse `UpdateBookDomFromBrowserPageContent()` and `SaveBookToDisk()`, so
  they make exactly the same "just this page vs. full book save" decision.
- `EditingModel.SavePageInPlace(pageContentData)` — save and stay put, for the one caller that
  wants no navigation at all (Copy Page).
- API `editView/savePageInPlace`, called by `savePageWithoutReloading()` in `bloomEditing.ts`. The
  reply is not sent until the save has finished, so Javascript can `await` it.

### What has been converted so far

Everything the **page list frame** initiates. `collectCurrentPageContent()`
(`pageThumbnailList/currentPageContent.ts`) gathers the editable page's content — it can, because
`getEditablePageBundleExports()` reaches across frames — and every one of these sends it along with
its request:

| Command | Was | Is now |
| --- | --- | --- |
| clicking a page thumbnail | `SaveThen` round trip, then navigate | same `SaveThen`, given the content |
| Duplicate Page (button and context menu) | `SaveThen` round trip, then duplicate, then navigate | ditto |
| Delete Page (button and context menu) | ditto | ditto |
| Paste Page (context menu) | ditto | ditto |
| dragging a page to a new position | ditto | ditto |
| Change Layout, import a video, convert a field to a derived one | `SaveThen` round trip, then reload the page | ditto — and they keep the reload, which is doing a second job for them (§1) |
| **Copy Page** (context menu) | `SaveThen` round trip **and a reload of the page being copied** | `SavePageInPlace` — no navigation at all |

Copy Page is the first of these to lose its reload entirely: copying doesn't change the page you
are looking at, so with the content in hand there is nothing left to navigate to. The others still
navigate, because they are *going somewhere* (the new page, the next page, the moved page); what
they lose is the round trip, and with it the `SavePending` window in which a second command is
silently dropped.

Not converted, because the request comes from a separate dialog window that cannot reach the page
frame: Add Page (`AddPageDialog`) and Duplicate Many Times (`duplicateManyDlgBundle`). Both still
use `SaveThen`, which is why it has to stay.

Everything below is the inventory of what else could be converted, and what that would let us
delete.

## Why the reload was expensive, not just ugly

A save-then-reload costs a full page teardown and rebuild: regenerate the page DOM in C#, navigate
the browser, re-run `SetupElements` over every element, re-attach CKEditor to every editable,
re-run the toolbox's `newPageReady` for the current tool, re-measure and re-fit images, re-add
scroll bars. It also throws away everything transient: the cursor position and selection, the
active canvas element and its control frame, scroll position, the Play/Start tab a game page was
on, an in-progress audio playback. Almost every "flicker" complaint about the Edit tab traces back
to a save.

---

## 1. Round trips that collapse into one call

These are places where Javascript wants "make sure the book on disk is current, then do X". Today
each is: JS posts to an API → C# calls `SaveThen` → C# asks the browser for the content → the
browser answers on a *different* API → the state machine runs the pending action → C# navigates →
the page reloads. Four hops and a reload, to do something the browser could have asked for
directly.

| Caller | Today | Could become |
| --- | --- | --- |
| ~~`origami.ts`, `bloomVideo.ts`, `canvasControlTextMenuItems.ts`~~ | — | **Done**: all three now call `saveChangesAndRethinkPage()` (`bloomEditing.ts`), which sends the content with the post. They **keep** the reload — see below; what went is the round trip. |
| `EditingViewApi` `editView/setTopic` → `SavePageAndReloadIt()` | Save + full reload to show a changed data-div value | Could carry the content too — the topic chooser runs in the workspace root, so it can reach the page frame. It would have to change from a plain post string to JSON, and it is also used from the Publish tab where there is no editable page at all (the collect just returns nothing and it falls back, which is fine). Small win, so not done yet. |
| `EditingModel.SavePageAndReloadIt` from `PageRefreshEvent` | Save + reload | Stays on `SaveThen`: these are raised inside C# (book settings, and `EditingModel` itself), so there is no browser request to carry the content. |

### The three converted ones keep their reload, and that is right

`common/saveChangesAndRethinkPageEvent` reads as "save this page, then show it again", and the
original reason for showing it again — restoring the UI markup the save stripped — is gone. But the
reload is doing a **second** job for these three callers, which is why they keep it: each has just
restructured the page into a state that has never been through `SetupElements` (a new origami
layout, an imported video, a translation group replaced by a derived field), and the reload is what
runs the page's setup over the result. This is the `customXmatterPage` lesson (below) applied
before making the mistake rather than after.

So what they lost is the four-hop round trip, not the reload. Verified by driving Change Layout
mode on and off in a real book: `editView/pageContent` never fires, the page reloads and comes back
fully alive (CKEditor attached, canvas elements present), and text typed but not saved before the
toggle is in the file on disk afterwards.

`postThatMightNavigate` itself exists (`utils/bloomApi.ts`) only because the post's own page is
about to be navigated out from under it, so the network error has to be swallowed. Calls that stop
navigating can use plain `post`/`postString` and get their errors reported again.

### Before converting any of these: the reload may be doing a second job

Check what the caller has just done to the live DOM, because a reload does not only recover from
the old destructive save — it also re-runs the page's whole setup (`SetupElements`, re-attaching
CKEditor to every editable, the toolbox's `newPageReady`, image sizing, scroll bars). A caller that
restructured the page may be relying on that without saying so.

This is not hypothetical. `customXmatterPage.tsx` posts `editView/jumpToPage` with its **own**
page id — asking to "jump" to where it already is, purely to get a save — right after
`convertXmatterPageToCustom()` rebuilds the cover into canvas elements. Converting it to
`savePageWithoutReloading()` looked ideal on paper (it even fixes a real bug: that handler replies
before the save has happened, so the `await` does not mean what it appears to). But driven live,
the converted cover came back with CKEditor attached to **0 of its 12** editables, where the
standard cover has 9: `convertXmatterPageToCustom()` never attaches editors to the elements it
creates, and the reload had been quietly covering for that. Reverted.

So: convert, then *drive the real UI* and check the page is still fully alive — editors attached,
tool markup present, images sized. Neither the unit tests nor the typecheck will tell you.

### What the round trip actually costs — measured, before changing anything

Do not do this work for speed. Measured on a running Bloom (7-page book, 25 KB page), driving real
thumbnail clicks and watching the API traffic from outside:
`.claude/skills/run-bloom/benchPageChange.mjs` and `benchSaveGather.mjs`.

| Phase of a page change | median ms from click |
| --- | --- |
| `pageList/pageClicked` acked | 19 |
| `editView/pageContent` complete (old page saved, navigation kicked off) | 167 |
| new page's DOM loaded | 748 |
| new page **editable** | 793 |

And separately: **gathering the page content in the browser takes 0.7 ms** (median of 15, on 25 KB
of HTML), while a *complete* direct save — `savePageWithoutReloading()`, i.e. gather + POST + merge
+ write to disk + reply — takes **92 ms**.

So the round trip's whole purpose is to fetch something that costs 0.7 ms to produce. Its window
(19→167 ms) is at most ~56 ms more than doing the same save directly, and even that overstates it,
because the `editView/pageContent` handler also kicks off the navigation before it replies. The
genuinely removable part is the C#→WebView2 dispatch and scheduling: **30–50 ms out of ~790, i.e.
4–6%**. Roughly 80% of a page change is building and setting up the NEW page, which none of this
touches; deleting the save phase entirely would still cap the win at ~21%. The overhead is also
roughly constant while the disk write and page setup grow with the book, so it gets relatively
smaller on real books, not larger.

The reason to make these changes is the simplification below — fewer hops, fewer states, fewer
things that can interleave — with a small speed bonus, not the other way round.

Note the existing `PerformanceMeasurement.Measure("Select Page")` in `HandlePageClickedRequest`
cannot answer this: it wraps only the *initiation* (`SaveThen` returns as soon as the browser has
been asked), and it ignores nested measurements, so it cannot be subdivided either.

## 2. The delay register — now the one gate, not a `requestPageContent` detail

`addRequestPageContentDelay` / `removeRequestPageContentDelay` /
`wrapWithRequestPageContentDelay` exist because **C# picks the moment to capture the page**, so any
asynchronous DOM work in flight has to register itself and hold the capture off — with a 4-second
cap after which we capture anyway and warn. There are ~10 call sites (image sizing, canvas
background image fitting, clipboard paste, custom xmatter pages, the image gallery dialog…), plus a
rule in `src/BloomBrowserUI/AGENTS.md` telling reviewers to check for it.

None of that can go while C# still initiates saves. What has changed is that a
**browser**-initiated save is just as capable of catching the page mid-change, and the first
version of this work did exactly that: `collectCurrentPageContent()` gathered synchronously,
straight past the register. A page click landing while an image was still being sized would have
written the half-sized page into the book.

So the register moved out of `bloomEditing.ts` into its own module,
`bookEdit/js/pageContentDelays.ts`, and gained `whenNoActiveDelays()` — the single gate that
**every** route now waits on:

| Route | Used by |
| --- | --- |
| `requestPageContent()` | the C#-initiated save; the reason the register exists |
| `getPageContentForSaveWhenReady()` | `savePageWithoutReloading()`, and the page list's commands via `collectCurrentPageContent()` |
| `captureContentForExternalProcessing()` | the off-screen book processor |

The synchronous `getPageContentForSave()` is no longer exported from the module or across frames,
so there is no longer a way to gather the page without passing the gate. And because the page
list's commands await it, the *command* does not start either: C# is not asked to duplicate,
delete or reorder anything until the page has settled. `pageContentDelays.spec.ts` covers the
waiting, the release, the cap, and that a failed operation cannot leave the gate stuck shut.

The gate also stopped polling. It used to be two mechanisms — a timeout that `requestPageContent`
armed and `removeRequestPageContentDelay` fired early, plus a separate 50ms poll loop in the
off-screen path. Now removing the last delay releases the waiters directly.

Javascript-initiated saves could in principle just `await` their own async work instead of using
the register at all — but they cannot know about work someone *else* started, so they wait here
too. Each converted caller is still one fewer place that has to remember the rule.

## 3. `SaveThen`'s awkward shape

`SaveThen(doBeforeSaveToDisk, doIfNotInRightStateToSave, forceFullSave, skipSaveToDisk,
failureAction, doAfterSaveToDisk)` has six parameters, four of them callbacks, because the work has
to be chopped into pieces that run at different points of an asynchronous state machine. The
remark on it — *"if you are doing this in an API handler, remember that you must retrieve any data
in the request before calling SaveThen; the Request object can't be used inside
doBeforeSaveToDisk, since by then the request has been marked completed"* — is a direct symptom.

There are 20 call sites. Several are pure "save, then go to this page":

- `PageListController.cs:48` — `SaveThen(() => page.Id, () => { })`
- `EditingViewApi.cs:299` — `SaveThen(() => pageId, () => { })`
- `EditingModel.SavePageAndReloadIt` — `SaveThen(() => CurrentSelection.Id, () => { })`

If the browser sends the page content **with** the request that needs a save, the handler no longer
has to be chopped up around an asynchronous wait. The shape the converted ones use:

```
// TS
postThatMightNavigate("edit/pageControls/duplicatePage",
                      await collectCurrentPageContent("the duplicate command"));
// C#
_editingModel.OnDuplicatePage(request.GetPageContentFromBrowserOrNull());
// ...which ends up at SaveThen(..., pageContentFromBrowser: content)
```

For a handler that has no reason to navigate at all, `SavePageInPlace` is even plainer — save,
do the thing, reply — which is what removes the `doIfNotInRightStateToSave` callback (the handler
can just check the return value), the `doAfterSaveToDisk` callback, and the "don't touch the
request afterwards" hazard.

Two of the six parameters are there for one caller each and would go away with them:
`skipSaveToDisk` (`collectionClosingEvent` and `OnTabAboutToChange`, which both want to do their own
`CurrentBook.Save()` before some postponed work) and `doAfterSaveToDisk`
(`WorkspaceView.cs:1742` and `CopyrightAndLicenseApi.cs`, which need up-to-date files on disk
before showing a blocking dialog).

## 4. The websocket dance in the copyright dialog

`EditingModel.NotifyCopyrightPushedToAllImages` exists, with an explanatory comment, purely because
*"we can't signal completion from the POST response itself, which returns as soon as the save is
initiated, well before the asynchronous post-save action runs."* With `editView/savePageInPlace`
the POST response **is** the completion signal, so this whole websocket event
(`kCopyrightWebSocketEventId_PushedToAllImages`, its sender, and its listener in
`CopyrightAndLicenseDialog.tsx`) can go once that flow is converted.

## 5. State-machine surface

If the C#-initiated save ever disappears entirely, these go with it:

- the `SavePending` and `SavedAndStripped` states, and their `ToSavePending` /
  `ToSavedAndStripped` transitions (two overloads);
- `DiscardInFlightSave()` and `_discardInFlightSave`, which exist only because a save can be in
  flight for an unbounded time;
- `RequestBrowserToSave()` and the `editView/pageContent` API;
- `enableStateTransitions` — the tab strip is disabled during `SavePending`/`SavedAndStripped`
  precisely because the page is unusable during them. An in-place save is synchronous; there is no
  window to disable anything in.
- `NavigatingSoSuspendSaving`, and the various "a Save is still in progress, abort" guards such as
  `EditingModel.cs:601`.

That is a long way off — `OnTabAboutToChange` and `collectionClosingEvent` legitimately have to
start a save from C# — but each converted caller shrinks the surface.

## 6. Smaller things

- ~~**`getBodyContentForSavePage` is exported cross-frame but nothing calls it.**~~ Done: both it
  and `userStylesheetContent` (whose comment still claimed it was *"Called from C# by a
  RunJavaScript() in EditingView.CleanHtmlAndCopyToPageDom"*, a method that no longer exists) are
  now private to `bloomEditing.ts`.
- **The off-screen book processor** (`BookProcessor.cs`) polls `window.__bloomExternalPageContent`
  because there is no live `EditingModel` for the callback API. Now that content-gathering is
  side-effect-free, `getPageContentForSave()` can be called directly and its value returned by
  `RunJavascriptWithStringResult`, once the caller can wait for the `activeDelays` loop. (Not
  urgent; the polling works.)
- **Thumbnail updates.** `SavePageInPlace` refreshes the current page's thumbnail because the
  navigation that used to follow a save did it (`EditingView.StartNavigationToEditPage`). If saves
  become frequent, that wants debouncing.

---

## Risks to watch when converting callers

- **niceScroll cleanup is our own code now.** `removeNiceScrollArtifacts` knows what niceScroll and
  bloom-player's `addScrollbarsToPage()` leave behind rather than asking them to undo it, so a
  change at either end could leave something in the saved page. `niceScrollCleanup.spec.ts` pins
  the current expectations, and the module comment records where each item comes from.
- **comicaljs 0.4.1 is required**, for `Comical.exportSvgToCopiesOfParents`. Note that moving from
  0.3.106 to 0.4.x also surfaces four pre-existing type errors in `canvasElementManager/`: 0.3.106's
  declarations import `from "bubbleSpec"` (a bare specifier TypeScript cannot resolve), so
  `BubbleSpec` silently degraded to `any` in Bloom; 0.4.x emits correct relative imports and the real
  types finally apply. They are unrelated to this work but must be fixed to pin 0.4.1. The most
  interesting is `CanvasElementResizeAdjustments.ts:161`, `bubbleSpec.spec !== "none"` — `BubbleSpec`
  has no `spec` member, so that comparison is always true and a Comical update is forced every time.
- **"We didn't save" and "we tried and failed" are different answers, and the difference is a
  page.** `SavePageInPlaceThen` returns `InPlaceSaveOutcome`, and only `Declined` — which
  guarantees `doBeforeSaveToDisk` never ran — permits falling back to asking the browser. This is
  not hypothetical: the first version returned a plain bool, and when relocating a page threw part
  way through, the caller read it as "not saved" and relocated the page a **second** time.
  `EditingStateMachineTests` pins all three outcomes. That rule now lives in exactly one place,
  inside `SaveThen`, which is the main reason `SavePageInPlaceThen` is private: no caller can get
  it wrong because no caller has to know about it.
- **The action is allowed to navigate.** Under `SaveThen` it ran in `SavedAndStripped`, where
  `ToNavigating` is legal; it now runs in `Editing`, where `ToNavigating` throws. Relocating a page
  does navigate (`OnRelocatePage` refreshes the page whose side and number just changed), so
  `_runningSaveInPlaceAction` relaxes that guard for the duration of the action — safely, because
  by then the browser's content is already in the book DOM and there is nothing left to lose. Our
  own navigation afterwards supersedes the action's, or is ignored when it is to the same page.
- **The context menu runs its command ~100ms after the click** (`HandleContextMenuItemClickedRequest`
  defers it so the menu can close). The content we save is therefore gathered slightly *earlier*
  than the old path gathered it — at click time rather than 100ms later. Nothing a user can type
  into fits in that window, but it is a real difference.
- **Not blurring.** The old code blurred the active element before capturing. If any code relies on
  a blur handler to normalize text before it is saved, that normalization no longer happens on save.
  CKEditor's `getData()` gives us current text either way, so this is about side effects, not text.
- **`ui-audioCurrent`.** The Talking Book tool deliberately leaves its highlight class on the live
  page (BL-15300), so it can reach the saved HTML; `BookData.cs:2091` already defends against that.
  Unchanged by this work, but worth knowing when reading the clone-cleanup code.

---

# The page snapshot: removing the round trip altogether

Branch `BL-13502-page-snapshot`, exploratory. The idea: instead of C# asking the browser for the
page and waiting, the **browser volunteers** it. An idle task in the editing page posts the current
content whenever the page has settled after a change (`pageSnapshot.ts`); C# stores the string
(`PageSnapshot.cs`, one new API `editView/pageSnapshot`); and a save then takes it synchronously.

## It works

Driven against a real book: typing in a text box, then clicking another page, saves the typing to
disk and the traffic is **only `pageList/pageClicked`** — `editView/pageContent` never fires. That
is the round trip gone for the path that matters most.

`SaveThen` now falls back to the snapshot whenever a caller did not bring content of its own, so
every caller that used to go the long way gets the short one for free.

## Two things that are not what we hoped

**1. A page nobody touched still posts snapshots — three of them, in the first ~6 seconds.**

The first version posted one for *every* page opened, which would have made "no snapshot" mean
nothing at all. The cause is that loading is not finished when `bootstrap()` returns: image sizing
and canvas-element layout complete asynchronously and mutate the page, and a `MutationObserver`
cannot tell those from the user. Taking a **baseline** once the page has settled fixes most of it,
and is why `startWatchingPageForSnapshots` gathers once before it starts posting.

Chased down, because the three turned out to be two different things.

**Two of them were a real bug, and not one this branch introduced.** Capturing the actual bodies
showed the first and third were byte-identical and the middle one 235 characters longer; the extra
was `<div id="measureTextDiv">`, the hidden scratch element `utils/measureText.ts` appends to the
body to measure text with. It is transient (a timer removes it) and it is not part of the page —
but the gather clones the whole body, and `removeEditingDebrisFromClone` did not strip it. **So a
save landing while it exists writes it into the book.** That window is not exotic: the div is
created while text is being fitted, i.e. while the user is typing, and a save right after typing is
the commonest save there is. Now stripped in the clone cleanup. The snapshot only found it because
it gathers far more often than a save does.

**The third is benign, and deliberately left alone.** With that fixed, an untouched page posts
exactly one snapshot, and it is byte-identical to the settled page. The cause is that the baseline
is taken before the page has finished settling: at that moment the asynchronous fix-ups have not
registered their delays yet, so `whenNoActiveDelays()` returns at once.

Delaying the baseline until the page is quiet would remove it, and would be a bad trade. The
baseline would then include any edit the user managed in the meantime, and because load-time
settling is indistinguishable from typing, we would have no way to know we still owed C# a snapshot
of it — swapping a harmless duplicate for a lost edit. One post per page visit, carrying exactly
what a save would have written, is the better end of that trade.

So the residual cost is one redundant store per page visit. Nothing extra reaches the disk: C#
only writes when a save actually happens.

## The freshness window, measured

The debounce decides how far behind the live page C# can be, and therefore how much typing an exit
could lose. It started at 400 ms, which was picked without measuring. Measured on a real page
(26 KB of HTML):

| | |
| --- | --- |
| One gather | **0.4 ms** median (0.2–1.6) |
| MutationObserver batches produced by ONE keystroke | **~8.9** |
| Keystroke → C# has the content, at 25 ms debounce | **~49 ms** |
| Snapshot posts while typing, at 25 ms | one per keystroke |
| Snapshot posts per visit to an untouched page, at 25 ms | 2 (was 1 at 400 ms) |

The nine batches per keystroke are why a debounce is still wanted at all — CKEditor does a lot of
DOM work per key, and without one we would gather nine times per character. 25 ms collapses them
into a single gather. Going lower buys almost nothing: below ~25 ms the lag is dominated by the
POST, not by us.

So the exposure is **~50 ms, not 400 ms**, and the residual risk is at most the last character —
and only if the exit arrived within 50 ms of a keystroke, which is shorter than the hand movement
that triggers an exit. The cost is one POST per keystroke rather than one per typing pause (26 KB
to localhost; C# stores the string, replacing the previous one) and one extra snapshot per page
visit, because a short debounce catches the page mid-settle as well as settled.

### On a slower machine

This machine is faster than many Bloom runs on, so the numbers above are the optimistic end.
Measured again under CDP CPU throttling, same page:

| CPU | gather, median | gather, worst | snapshot posts per keystroke |
| --- | --- | --- | --- |
| 1× | 0.5 ms | 1.0 ms | 1.00 |
| 4× | 1.8 ms | 3.2 ms | 0.91 |
| 8× | 3.9 ms | 7.3 ms | 0.91 |

The gather scales about linearly with CPU, as expected. The interesting column is the last one:
**the number of snapshots per keystroke does not grow as the machine slows — it falls slightly.**
Slower processing spreads a keystroke's DOM work out, so more of it lands inside one debounce
window, and the serialization in `takeSnapshot` (only one gather-and-post at a time) coalesces the
rest. The design degrades by taking *fewer, later* snapshots rather than by piling up.

So at 8× slower the cost is about 0.9 × 3.9 ms ≈ 3.5 ms of main-thread work per keystroke, against
a keystroke interval of at least ~110 ms. A single gather stays inside one 16.7 ms frame even at 8×.

Two limits worth stating rather than discovering later. The gather also scales with **page size**,
and only a 26 KB page was measured; a much heavier page (many canvas elements) costs proportionally
more, and 8× slow together with a 100 KB page would put a gather near a frame. And these are
throttled-CPU figures, not a real slow machine — throttling does not reproduce slow disk or memory
pressure.

None of that argues for a longer debounce, which would cost every user a bigger loss window to buy
something the coalescing already provides.

### Why that mattered for exit

`Shell.OnClosing` used to cancel the close (`e.Cancel = true`), start a save, and call `Close()`
again once the save came back — with `_startedClosingEvent` / `_finishedClosingEvent` guarding the
re-entry. All of that existed for one reason: the save could not complete synchronously, because it
had to ask the browser and wait for the answer on another API call.

A save that takes the snapshot **is** synchronous, so the dance is gone: `OnClosing` saves and lets
the close proceed. Nothing waits on the browser at shutdown, and so nothing can hang there.

What is accepted in exchange is stated plainly, because it is the one guarantee this design gives
up: the content written at exit is the last snapshot the browser posted, which is up to ~50 ms
behind the live page. Quitting within 50 ms of a keystroke can therefore miss that keystroke. It is
not "the page is re-read at close time" — there is no mechanism left that could re-read it. The
window is shorter than the hand movement that reaches for the X, and testing has not managed to hit
it, but it is a trade rather than an oversight.

### Would observing `.bloom-page` instead of the body be better?

It would have hidden the `measureTextDiv` bug rather than exposing it, and it would be unsound:
the gather clones the whole `document.body`, so a change outside `.bloom-page` can still alter what
gets saved. An observer narrower than the thing being gathered can miss a real change. If the two
are ever narrowed, they must be narrowed together.

**2. The two saves that can follow a keystroke were expected to keep the round trip. They did not.**

A snapshot is up to `kQuietMs` plus a gather behind the live page. That is fine for a page click,
which cannot happen within a few hundred milliseconds of a keystroke. The doubt was about the two
saves that can:

- **leaving the Edit tab** — a tab click, possibly right after typing;
- **closing the collection** — `Shell.OnClosing`, i.e. the window's X button, Alt+F4, or the OS
  shutting Bloom down.

The plan was to let those two keep asking the browser, which would have kept `SavePending`,
`SavedAndStripped`, `RequestBrowserToSave` and the `editView/pageContent` API alive to serve them,
and left the state machine at "one waiting state, one caller" rather than none.

Measuring the debounce is what changed the answer. At 400 ms the round trip was clearly worth
keeping for those two; at 25 ms the freshness window is ~50 ms, which is below the time it takes to
move a hand from the keyboard to a tab or a close button. So both were converted after all: the
waiting states, `RequestBrowserToSave` and `editView/pageContent` are gone, and both paths are now
straight-line code. The state machine keeps only the states it needs to know *which page is being
edited and whether it is safe to act on it* — it no longer arbitrates a wait.

The C#-side alternative was never available in any case: the only synchronous way to read Javascript
is `RunJavascriptWithStringResult_Sync_Dangerous`, which pumps the message loop, and Bloom has been
deliberately retreating from it (see `OffScreenBrowser`, `PublishHelper`).

## The verdict

The round trip is gone from every save, including the two that were expected to keep it, and with it
the asynchronous machinery that existed only to wait: two state-machine states, the ask-the-browser
API and its browser half, and the shutdown kludge that cancelled the user's quit and re-issued it.
The price is a single, bounded one — the last ~50 ms of typing at exit — and it is paid only by the
exit path, not by the ordinary saves that happen all day.
