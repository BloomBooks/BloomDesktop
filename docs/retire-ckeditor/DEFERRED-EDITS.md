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

**New code:** `src/BloomBrowserUI/bookEdit/undo/` — `undoTypes.ts`, `UndoStack.ts`,
`legacyUndoProviders.ts`, `runUndoable.ts`, `pageFrameUndoHooks.ts`, `redoKeyBinding.ts` and their
specs.

**Entries 1a–1e landed 2026-09-07** (commit `f92383031` on `BL-6681-stage1-undostack`). What they
did, briefly, so this file still explains the shape of `workspaceRoot.ts`:

- `registerLegacyUndoProviders()` is called once at module level in `workspaceRoot.ts`.
- `handleUndo()` and `canUndo()` are delegations to `theOneUndoStack`; the four-way if-chain, its
  order, and the BL-16558 markup-update calls now live in `undo/legacyUndoProviders.ts`. The stale
  "*See also Browser.Undo*" comment is gone.
- `switchContentPage()` calls `pageFrameNavigating()` before touching the old frame and
  `pageFrameLoaded()` in its load handler (`undo/pageFrameUndoHooks.ts`). One hook covers same-page
  reloads too, because every page-frame navigation C# makes goes through `switchContentPage`.
- `handleRedo()` / `canRedo()` are exported on the workspace bundle; Ctrl+Y is bound in the page frame
  by `undo/redoKeyBinding.ts`, installed from `editablePage.ts`'s ready handler, as the last resort
  behind origami's and the reader tools' handlers and CKEditor's own redo.

Three things differed from the entries as originally written — recorded in PROGRESS.md
(2026-09-07): master's BL-16558 had changed `handleUndo`; `data-page-id` is never set, so the page id
is `.bloom-page`'s `id`; and ctrl+wheel zoom no longer reloads the page.

### 1f. Expose the cross-frame push

`IWorkspaceExports` (`workspaceRoot.ts`) and the global exposure object at the bottom of the file
both need whatever Stage 2 pushes with. **Do not export `push(entry)` across frames** — that would
hand page-frame code the ability to put a page-frame closure on the stack, which is exactly the
failure `undoTypes.ts` documents at length. Export a function taking *data* and let the workspace
frame build the entry. Design it with Stage 2's first real caller, not before.

### Proof it worked

- [x] `pnpm test` green; `bookEdit/undo` specs green (52 tests, 21 of them added with the edits).
- [x] **The point of Stage 1 is that nothing changes**, so the verification is behavioural, in a
  running Bloom. Done 2026-09-07 with the harnesses in `liveChecks/` (see its README); results in
  PROGRESS.md under that date:
  - [x] Change Layout mode: a split, Ctrl+Z undoes it, Ctrl+Y redoes it, each exactly once (origami's
        own handler; ours declined). The Undo button reaches `origamiUndo` through the stack.
  - [x] Decodable Reader tool active: type, Undo button — the reader-tools undo runs (`tb=1`), not
        CKEditor's (`ck=0`), and the markup update follows (`markup=1`).
  - [x] Image: an undoable copyright change, Undo button — `imageOperationUndo` runs (`img=1`).
  - [x] Text box with no reader tool: type, Undo button — CKEditor's undo runs (`ck=1`); Ctrl+Y runs
        CKEditor's redo exactly once and ours declines.
  - [x] The Undo button's enabled state tracked `canUndo()` in every case.
