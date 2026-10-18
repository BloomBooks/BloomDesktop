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

**New code:** `src/BloomBrowserUI/bookEdit/pageHistory/` and its specs.

**Entries 1a–1e landed 2026-09-07** (commit `f92383031` on `BL-6681-stage1-undostack`). The shape
of `workspaceRoot.ts` and the page frame's undo now:

- `handleUndo()` and `canUndo()` are delegations to the page frame's, in
  `pageHistory/pageHistorySetup.ts`, which answer from origami's undo in Change Layout mode and from
  the page history otherwise. The stale "*See also Browser.Undo*" comment is gone.
- The page history starts in the page frame when the page loads (`startPageHistoryForThisPage`,
  called from `editablePage.ts`) and ends on the frame's `pagehide`. That covers same-page reloads
  too.
- `handleRedo()` / `canRedo()` are exported on the page bundle; the undo and redo keys are bound in
  the page frame by `pageHistory/pageHistoryKeys.ts`.

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
  running Bloom. Done 2026-09-07 with the harnesses in `liveChecks/` (kept on branch
  `BL-6681-bug-repros`; the e2e spec `src/BloomE2E/tests/undo-routing.spec.ts` now does the
  same checks); results in PROGRESS.md under that date:
  - [x] Change Layout mode: a split, Ctrl+Z undoes it, Ctrl+Y redoes it, each exactly once (origami's
        own handler; ours declined). The Undo button reaches `origamiUndo` through the stack.
  - [x] Decodable Reader tool active: type, Undo button — the reader-tools undo runs (`tb=1`), not
        CKEditor's (`ck=0`), and the markup update follows (`markup=1`).
  - [x] Image: an undoable copyright change, Undo button — `imageOperationUndo` runs (`img=1`).
  - [x] Text box with no reader tool: type, Undo button — CKEditor's undo runs (`ck=1`); Ctrl+Y runs
        CKEditor's redo exactly once and ours declines.
  - [x] The Undo button's enabled state tracked `canUndo()` in every case.
