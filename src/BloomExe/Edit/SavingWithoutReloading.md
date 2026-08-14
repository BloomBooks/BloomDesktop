# Saving a page without reloading it — what it enables

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
- `EditingModel.SavePageInPlace(pageContentData)` and `SavePageInPlaceThen(pageContentData,
  doBeforeSaveToDisk)` — the Javascript-initiated counterparts of `SaveThen`. They reuse
  `UpdateBookDomFromBrowserPageContent()` and `SaveBookToDisk()`, so they make exactly the same
  "just this page vs. full book save" decision as the old path. Each returns false, having done
  nothing at all (the action has NOT run), if we were not in a position to save, so the caller can
  fall back to `SaveThen`.
- API `editView/savePageInPlace`, called by `savePageWithoutReloading()` in `bloomEditing.ts`. The
  reply is not sent until the save has finished, so Javascript can `await` it.

### What has been converted so far

Everything the **page list frame** initiates. `collectCurrentPageContent()`
(`pageThumbnailList/currentPageContent.ts`) gathers the editable page's content — it can, because
`getEditablePageBundleExports()` reaches across frames — and every one of these sends it along with
its request:

| Command | Was | Is now |
| --- | --- | --- |
| clicking a page thumbnail | `SaveThen` round trip, then navigate | `SavePageInPlaceThenGoToPage` |
| Duplicate Page (button and context menu) | `SaveThen` round trip, then duplicate, then navigate | `SavePageInPlaceThen(duplicate)` |
| Delete Page (button and context menu) | ditto | `SavePageInPlaceThen(delete)` |
| Paste Page (context menu) | ditto | `SavePageInPlaceThen(insert)` |
| dragging a page to a new position | ditto | `SavePageInPlaceThen(relocate)` |
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
| `origami.ts:193`, `bloomVideo.ts:199`, `canvasControlTextMenuItems.ts:392`, `aiEditorLauncher.ts:158` — all four `postThatMightNavigate("common/saveChangesAndRethinkPageEvent")` | Save, then reload the page frame purely to get the DOM re-set-up | `await savePageWithoutReloading()`, then whatever local re-setup the change actually needs. Some of these want the reload for a genuine reason (a new stylesheet); most want it only because the save destroyed the page. |
| `EditingViewApi` `editView/setTopic` → `SavePageAndReloadIt()` | Save + full reload to show a changed data-div value | Save in place, then update the one element in the DOM |
| `EditingModel.SavePageAndReloadIt` generally (`PageRefreshEvent.SaveBeforeRefresh`) | Save + reload | Needs case-by-case review: some callers change the page HTML in C# and genuinely need the new HTML; those still need the reload. Others only needed it because of the stripping. |

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

## 2. The `requestPageContent` delay machinery

`addRequestPageContentDelay` / `removeRequestPageContentDelay` /
`wrapWithRequestPageContentDelay` (`bloomEditing.ts`) exist because **C# picks the moment to
capture the page**, so any asynchronous DOM work in flight has to register itself and hold the
capture off — with a 4-second timeout after which we capture anyway and hope. There are ~10 call
sites (image sizing, canvas background image fitting, clipboard paste, custom xmatter pages, the
image gallery dialog…), plus a rule in `src/BloomBrowserUI/AGENTS.md` telling reviewers to check for
it.

When Javascript initiates the save, it can simply `await` its own async work first and then call
`savePageWithoutReloading()`. The delay registry can't be deleted while C#-initiated saves exist,
but every converted call site is one fewer place that has to remember the rule, and one fewer
opportunity for the "proceeded anyway after 4s" warning to save a half-finished DOM.

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

If the browser sends the page content **with** the request that needs a save, the API handler can
be a plain synchronous method: save, do the thing, reply. Suggested shape for the handlers that
currently take no useful body:

```
// TS
postString("editView/duplicatePage", getPageContentForSave());
// C#
View.Model.SavePageInPlace(request.RequiredPostString(unescape: false));
DuplicatePage(...);
```

That removes the `doIfNotInRightStateToSave` callback (the handler can just check the return
value), the `doAfterSaveToDisk` callback, and the "don't touch the request afterwards" hazard.

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
- **A command that carries content must be a no-op when we decline to save.** `SavePageInPlace*`
  returns false *before running doBeforeSaveToDisk* when we are not in a state to save, precisely
  so the caller can fall back to `SaveThen` without the page being duplicated (or deleted!) twice.
  `EditingStateMachineTests` pins that, because it is the kind of thing a later refactor could
  quietly break with no visible symptom until a user loses a page.
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
