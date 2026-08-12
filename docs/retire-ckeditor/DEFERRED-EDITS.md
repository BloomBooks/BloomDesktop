# Deferred edits to existing files

The project's defence against rebase pain is that new code goes in new files and edits to existing
files land as late as possible (PLAN.md §5). This file is the ledger of edits that a completed stage
of *new* code is waiting on — written down at the moment the new code was designed, while the
reasoning is fresh, so that landing them later is mechanical rather than a re-derivation.

**Every entry states what the edit is, why it is safe, and what proves it worked.** Delete an entry
when its edit lands.

Line citations are as of the commit that added the entry. They drift; the surrounding code is quoted
so the right place is still findable.

---

## Stage 1 — activate the one undo stack

**New code (already landed, inert):** `src/BloomBrowserUI/bookEdit/undo/` — `undoTypes.ts`,
`UndoStack.ts`, `legacyUndoProviders.ts`, `runUndoable.ts` and their specs. Nothing imports them
yet, so the bundle is unchanged in behaviour and very nearly unchanged in size.

**Why they were deferred:** these edits are all in `workspaceRoot.ts`, which the Stage 0 PR (#8153)
does not touch but which sits next to code that PR does touch, so waiting until Stage 0 merged kept
the two reviews independent.

**They are no longer blocked (2026-08-06).** Under the no-merging constraint (PLAN.md §5) Stage 0
will not reach `master` for months — deferring until then would leave Stage 1 unverifiable for the
whole period, which is much worse than the review-independence it was buying. And the reason has
gone anyway: Stage 0's commits are the base of the integration branch `BL-6681-ckeditor`, and Stage
1's PR targets that branch, so its diff shows only Stage 1's own changes. **Apply these on the Stage
1 branch.** Every file they touch had zero commits on `master` in the 30 days measured in §5.1, so
the integration risk is as low as it gets.

### 1a. Register the legacy providers, once

In `workspaceRoot.ts`, alongside the other module-level imports:

```ts
import { registerLegacyUndoProviders } from "./undo/legacyUndoProviders";
import { theOneUndoStack } from "./undo/UndoStack";

registerLegacyUndoProviders();
```

Module-level is right: `workspaceRoot` is loaded once per edit-tab session, and the providers only
reach across frames when consulted, so nothing needs the frames to exist yet.

*Safe because:* registration does no work. **Do not call it twice** — each mechanism would be
consulted twice, harmless but confusing.

### 1b. `handleUndo()` becomes a delegation

**Scope, so the verification below is not over-claimed:** `handleUndo` has exactly one caller —
`topBarButtonClick` (`bloomEditing.ts:1633-1648`), i.e. the toolbar Undo button. There is no Ctrl+Z
handler in the workspace frame, and C#'s `UndoCommand.Implementer` is an empty lambda
(`WebView2Browser.cs:890`) existing only to make the button's `Enabled` settable. Ctrl+Z is claimed in
the *page* frame by origami, by the reader tools, or by CKEditor. So these edits change the **button**
path only; the keyboard path is untouched, which is both why they are safe and why "one consistent
Undo" is not yet true for the keystroke. See PLAN.md §3's correction.


Replace the body of `handleUndo()` (`workspaceRoot.ts:97-126`) with:

```ts
export function handleUndo(): void {
    theOneUndoStack.undo();
}
```

The four-way if-chain being deleted is reproduced exactly by the providers, in the same order, in
`legacyUndoProviders.registerLegacyUndoProviders()`. Two comments in the deleted body should move
rather than die, because they record *why* the order is what it is — they are already carried in
`legacyUndoProviders.ts`, so check them across before deleting.

**One comment must not move: it is wrong.** `workspaceRoot.ts:125` says "*See also Browser.Undo; if
all else fails we ask the C# browser object to Undo*". There is no such fallback in the WebView2
code — the Undo button's enabled state comes purely from `canUndo()` returning `"yes"`. Delete it.

### 1c. `canUndo()` becomes a delegation

Replace the body of `canUndo()` (`workspaceRoot.ts:248-266`) with:

```ts
//Called by c# using workspaceBundle.canUndo()
export function canUndo(): string {
    return theOneUndoStack.canUndo() ? "yes" : "fail";
}
```

Keep the `"yes"`/`"fail"` strings: that is the contract with `WebView2Browser.CanUndoAsync`, which
polls it on a timer. Changing it is a separate, C#-touching change and not worth bundling in.

*Watch for:* the old `canUndo` guarded the toolbox call as `toolboxWindow.canUndo &&
toolboxWindow.canUndo()` while `handleUndo` did not. `toolboxUndoProvider` keeps that asymmetry
deliberately (a throw in a timer-polled function fires repeatedly), and says so.

### 1d. Tell the stack when the page changes

`UndoStack.setCurrentPageId()` exists but nothing calls it, so page-scoped entries are never
discarded. The hook point is the `load` handler already inside `switchContentPage`
(`workspaceRoot.ts:163-172`), which is where the new page's DOM first exists:

```ts
const handler = () => {
    handlerCalled = true;
    iframe.removeEventListener("load", handler);
    theOneUndoStack.setCurrentPageId(getCurrentPageIdFromPageFrame());
    doWhenToolboxLoaded(...);
};
```

The page id lives on the current page element as `data-page-id` — the same source
`ImageUndoManager` uses for exactly this purpose (`ImageUndoManager.ts:154-161`,
`clearImageOperationUndoOnPageChange`). Reuse that, don't invent a second notion of page identity.

Note the 1500 ms fallback below it: the `load` event sometimes never fires, and `handler` is called
on a timer instead. `setCurrentPageId` is idempotent for an unchanged id, so being called twice or
late is harmless — but it means an entry pushed in that window could be attributed to the previous
page. Nothing pushes automatically in Stage 1, so this cannot bite yet; it must be re-examined when
Stage 3 starts recording typing.

**Also needed, and not covered by the above:** `clearPageScopedEntries()` on a page-frame reload
that keeps the *same* page — ctrl+wheel zoom (`bloomEditing.ts:1259-1276`, whose own comment says
"Zooming re-loads the page") and leaving Change Layout mode (`origami.ts:193`). `switchContentPage` is not involved in either, so this needs its own hook.
`pageUnloading()` (already called at `workspaceRoot.ts:138`) is the candidate; confirm it runs on
same-page reloads before relying on it.

### 1e. Expose Redo (Ctrl+Y only) — and it cannot live in the workspace frame

There is **no Redo plumbing in C# at all** — no `RedoCommand`, no `SetEditingCommands` parameter,
nothing in the `updateEditButtons` payload, no icon, no XLF entry. So a Redo *button* is where the
real cost is, and it is deliberately out of scope. Ctrl+Y is JS-only and needs none of it.

Add to `workspaceRoot.ts`:

```ts
export function handleRedo(): void {
    theOneUndoStack.redo();
}
```

**But do not bind Ctrl+Y in the workspace frame.** Keyboard events inside the page iframe are
delivered to that iframe's document and never reach the parent, so a workspace-frame handler would
fire only when focus is outside the page — which is the opposite of when Redo is wanted. This is why
*both* existing Ctrl+Y handlers are in the page frame: origami's on `html` (`origami.ts:137`) and the
reader tools' on each editable (`decodableReaderTool.tsx:158-178`). So the new binding goes in the
page frame too, and calls `getWorkspaceBundleExports().handleRedo()`.

**Both existing handlers `preventDefault()` and win where they apply**, so the new one must be the
last resort, not the first:

- In Change Layout mode, origami's handler claims Ctrl+Y. Leave it — it is the only Redo for layout
  changes until Stage 4 converts it, and that conversion must retire the handler in the *same*
  commit or its Redo breaks in between.
- In any editable while a reader tool is active (`currentMarkupType !== None`), the reader tools'
  handler claims Ctrl+Z *and* Ctrl+Y and returns false. Also leave it.

*Verify:* press Ctrl+Y in Change Layout mode, in a reader-tool text box, and in an ordinary text box,
and confirm exactly one redo happens in each — not two, and not none.

### 1f. Expose the cross-frame push

`IWorkspaceExports` (`workspaceRoot.ts:14-55`) and the global exposure object at the bottom of the
file both need whatever Stage 2 pushes with. **Do not export `push(entry)` across frames** — that
would hand page-frame code the ability to put a page-frame closure on the stack, which is exactly
the failure `undoTypes.ts` documents at length. Export a function taking *data* and let the
workspace frame build the entry. Design it with Stage 2's first real caller, not before.

### Proof it worked

- `pnpm test` green; `bookEdit/undo` specs green (31 tests).
- **The point of Stage 1 is that nothing changes**, so the verification is behavioural, in a
  running Bloom, comparing against the same gestures before the edits:
  - In Change Layout mode: make a layout change, Ctrl+Z undoes it; Ctrl+Y redoes it.
  - With the Decodable Reader tool open: type, then Undo — the reader-tools undo runs, not
    CKEditor's (this is the deliberate precedence that would be easiest to lose).
  - On an image: change its copyright, then Undo.
  - In a text box with no tool active: type, then Undo — CKEditor's undo runs.
  - The Undo button's enabled state tracks all four, since C# polls `canUndo()` on a timer.
