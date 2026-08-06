# Review notes on the CKEditor-retirement plan

Round 1 review by Fable (Claude), 2026-08-04, against the first draft of [PLAN.md](PLAN.md).
Every finding below was independently verified against the source before being accepted or
rejected. This file exists so a later session knows *why* the plan says what it says, and
doesn't re-litigate settled points.

## Accepted — factual corrections to the draft

| Finding | Verification | Where it landed |
| --- | --- | --- |
| The draft said `Equation-style` / ArithmeticTemplate boxes get no editor. They **do**: `utils/shared.ts:16-19` includes `.Equation-style[contenteditable='true']` in `ckeditableSelector`, added for that template. The real no-editor case is `attachToCkEditor`'s `cursor: not-allowed` early return (`bloomEditing.ts:1952`). The comment at `toolbox.ts:1530-1537` claiming otherwise is stale. | Confirmed in `shared.ts` | §2 "dead or misleading code", Stage 0 inventory |
| The draft called the toolbox undo "reader-setup changes". It is really a **per-editable text-typing** undo: `{html, text, caretOffset}` seeded on focus (`readerToolsModel.ts:557-568`) and pushed inside `doMarkup` (:753-764), gated on `shouldHandleUndo()` (:570). | Confirmed by reading both ranges | §3 table |
| Consequently `handleUndo` consulting toolbox **before** CKEditor is deliberate, not arbitrary — when a reader tool is active it must shadow CKEditor's undo, which would restore stale markup. | Follows from the above | §3, Stage 1 rationale |
| Origami's undo is a jQuery `clone(true)` — DOM *plus handlers and data* — not an innerHTML snapshot, and it is safe partly because layout mode strips `contentEditable` (`origami.ts:132`). So it is not a drop-in precedent for innerHTML restore. | Confirmed at `origami.ts:262-294`, :132 | §3 table, Risk 2, Stage 4 |
| `deleteCanvasElement`'s background-image branch already records an image undo (`CanvasElementManager.ts:2755-2770`), so wrapping the delete would double-record. | Confirmed | §4.8, Stage 2b |
| Support-file cleanup runs only from `Book.BringBookUpToDate` (`Book.cs:1112`) and publish/upload paths (`BookStorage.cs:2623-2632`), **not** on page save — so the audio-file risk is much smaller than the draft assumed. | Confirmed by tracing `CleanupUnusedSupportFiles` call sites | Risk 7 (downgraded and scoped) |
| The cross-frame export is `getWorkspaceBundleExports`, not `getWorkspaceExports`. | Confirmed (`origami.ts:204`) | §4.2 |
| `ISelectionAnchor` keyed on `editableId` won't work: ordinary `.bloom-editable` divs have no `id` (only talking-book assigns them, `audioRecording.ts:1380, 3681`). | Confirmed | §4.3 — structural locator instead |
| The draft overstated the existing selection machinery. `makeSelectionIn` is the *consume* side; `getElementSelectionIndex` returns only an offset and in-tree callers pass `divBrCount = -1`. The capture side is new code. | Confirmed (`editableDivUtils.ts:30-46`, `readerToolsModel.ts:587-592`) | §4.3 |

## Accepted — design changes

1. **Undo entries must be data, not closures.** The page iframe's JS context dies on same-page
   reloads too (ctrl+wheel zoom `bloomEditing.ts:1268`, origami exit `origami.ts:193`), so
   page-id-scoped clearing alone leaves entries closing over a dead document. Snapshot entries
   are now pure data interpreted at undo time; closures are permitted only for workspace-owned
   operations; clearing also keys on page-frame unload/load. → §4.1
2. **Native browser undo must be actively fenced.** Plain typing feeds Chromium's undo stack and
   Ctrl+Z inside an editable triggers it; today CKEditor intercepts that. The new editor must
   `preventDefault()` on `beforeinput` with `inputType` `historyUndo`/`historyRedo`. This is
   also the replacement for `BloomField.PreventRemovalOfSomeElements`'s `execCommand("undo")`
   (`BloomField.ts:810-825`) — block the deletion rather than undoing it. → §4.4, Risk 3
3. **Re-cut Stages 1–2.** The draft spent Stage 1 converting three mechanisms that already work
   (delivering nothing the user asked for) while forcing in-place snapshot restore to mature on a
   page full of live CKEditor instances. The decisive detail: restoring innerHTML orphans
   `div.bloomCkEditor` (`BloomField.ts:419`), and because `doCkEditorCleanup` iterates that
   expando (`editableDivUtils.ts:350`) and `getBodyContentForSavePage` calls it
   (`bloomEditing.ts:1483`), the **save path would silently skip cleanup** for restored divs.
   So: Stage 1 now wraps all four mechanisms as legacy providers with no conversions; delete-page
   and delete-canvas-element move to Stage 2; `PageSnapshot` lands in Stage 3; conversions become
   optional Stage 4 cleanups.
4. **Delete canvas element uses an inverse-op / narrow subtree, not a page snapshot**, reusing
   the existing `refreshCanvasElementEditing` path. Two things flagged to verify first: Comical
   bubble-family re-linking, and restoring a drag-activity target. → Stage 2b
5. **`runUndoable` needs nesting semantics from day one** (depth counter, outermost wins). → §4.8
6. **Delete-page capture must happen inside the `SaveThen` callback** (`EditingModel.cs:590-624`),
   and restore must re-raise `_pageListChangedEvent` / `InvokeContentsChanged` and navigate, not
   just renumber. The front-end entry is pushed by C#, since the page frame is being torn down.
   → Stage 2a
7. **The toolbox keystroke pipeline is the one place "one-line dispatch" fails**
   (`toolbox.ts:1509-1607`, ~100 lines of the app's most delicate code). Added a mechanical
   behaviour-preserving prep commit in Stage 0 that extracts the save/restore-selection bracket,
   so the eventual change swaps a function body. → §5.7.3
8. **The flag is a `localStorage` dev switch.** A URL parameter is not zero-touch: the page
   iframe’s `src` comes from C# via `switchContentPage`. → §5.7.4
9. **`canUndo()` must stay synchronous and O(1)** — C# polls it on a timer with a reentrancy
   guard (`WebView2Browser.cs:963-996`); expensive work there makes the button flicker. → §4.1

## Accepted — missed dependencies now in scope

- `WireToCKEditor` services the draft omitted: the BL-13779 `change`-event `data-user-deleted`
  tracking (`BloomField.ts:244-252`); BL-11745 qtip z-order juggling on focus/blur (:345-366);
  `selectionChange` → `EnsureCaretNotInsideLineBreakSpan` (:259-261); the SetupLink hyperlink
  command and button (:368-415). → §2 table rows 8–11, Stage 3 `BloomTextEditor.ts` /
  `FormatToolbar.tsx`
- **BL-12357 small caps cannot be ported unchanged.** `restoreHtmlMarkupIfNecessary`
  (`BloomField.ts:425-456`) detects CKEditor-internal copies via `dataTransfer.getData("cke/id")`
  and compensates for CKEditor's own span-stripping. With CKEditor gone, nothing stamps `cke/id`
  and nothing strips the spans, so the transform is meaningless as written and the problem may
  vanish. Verify against the repro. → Stage 3
- **The second paste path**: `pasteImpl`'s replace-whole-content branch for a canvas element that
  is selected but not being text-edited (`bloomEditing.ts:1792-1842`), plus the C#-initiated
  `pasteClipboard` entry point as distinct from the DOM `paste` event. → Stage 3 `pasteHandler.ts`
- **jsdom cannot test the `beforeinput` layer** — no native editing behaviour, no
  `getTargetRanges()`. `typingTransactions.ts` and `keyCommands.ts` need the live-WebView2 CDP
  harness. → §7
- `readerSetup.ui.ts:454`'s `execCommand` is in the reader-setup dialog, not the page frame —
  scoped out. → Stage 3 `keyCommands.ts`

## Accepted — cuts

- **Stage 1c (convert reader-tools undo) deleted.** It is a text-typing undo, so converting it
  would require typing transactions to exist first — out of order. It becomes a legacy provider
  and is deleted in Stage 4/5 when shared-stack snapshots subsume it.
- **Byte-budget accounting on the stack** — dropped; a count cap (~50) suffices.
- **`ISelectionAnchor.focusOffset` / range support** — deferred; every identified consumer needs
  only a caret. Added the reviewer's neat alternative for the snapshot case: inject a caret
  marker into the captured HTML *string*, which has none of the bookmark-span downsides.
- **Open question "delete-page depth"** — answered in the plan rather than asked: keep every
  deletion in the session, capped ~10.

## Confirmed as already correct

The Stage 0 behaviour inventory (which would itself have caught the `Equation-style` error), the
dev harness page, doing `inlineFormat` first, consolidating the C# scrubbers into
`LegacyCkEditorCleanup` rather than deleting them, and keeping Stage 6 as a separately-reviewed
pass.
