# Our page history compared with the retire-CKEditor plan

Status: comparison, 2026-10-15. Our design is in [APPROACHES.md](APPROACHES.md),
[MINIMUM.md](MINIMUM.md) and [OPEN-ISSUES.md](OPEN-ISSUES.md); "goal N" means OUTLINE.md §1 goal N.
The other plan is `docs/retire-ckeditor/`: [PLAN.md](../retire-ckeditor/PLAN.md),
[PROGRESS.md](../retire-ckeditor/PROGRESS.md),
[BEHAVIOR-INVENTORY.md](../retire-ckeditor/BEHAVIOR-INVENTORY.md),
[DEFERRED-EDITS.md](../retire-ckeditor/DEFERRED-EDITS.md) and
[PASTE-DROP-BASELINE.md](../retire-ckeditor/PASTE-DROP-BASELINE.md), all read in full. "PLAN §"
numbers refer to PLAN.md. Code paths start at `src/BloomBrowserUI/` unless they start with `src/`.

What was checked in the code: every claim below about today's code was read on this branch, except
where it says "observed by them" (a result that PROGRESS.md reports from a running Bloom, not
repeated here) or "inferred".

---

## 1. What the retire-CKEditor plan proposes for undo

### Goals and stages

Its goal 2 is "one consistent Undo stack" over the page as currently loaded, with priority on
operations that are hard to reverse by hand (PLAN.md:10-14). Undo is one part of a larger project
whose main aim is removing CKEditor 4 (PLAN.md:8-9). The stages (PLAN §6, PLAN.md:1035-1283):

| Stage | What it does for undo | State (PROGRESS.md) |
|---|---|---|
| 0 | Behaviour inventory (section H is undo, BEHAVIOR-INVENTORY.md:182-208), a selection prep commit in the toolbox, bug repros | Merged 2026-09-21 (PROGRESS.md:28) |
| 1 | `bookEdit/undo/`: one `UndoStack` in the page frame, the four old mechanisms wrapped as legacy providers in their old order, `runUndoable`, Ctrl+Y bound in the page frame; no conversions (PLAN.md:1079-1112) | Merged to master 2026-10-13 as PR 8387 (git: `ae4a6fa366`, `45505dcb27`). PROGRESS.md's last entry (:1252-1264) still says it waits for review. |
| 2 | 2a undo of page deletion: dropped (decision 7). 2b undo of deleting a canvas element as a neighbour-based entry: parked on draft PR 8451, BL-17003 (PLAN.md:1119). 2c style changes: deferred (PLAN.md:1153-1154) | Parked 2026-10-13 (PROGRESS.md:1254-1257) |
| 3 | A new text editor behind an experimental flag, with CKEditor not loaded when the flag is on; `typingTransactions.ts` puts typing on the stack; snapshot entries and `reinitializePageAfterRestore()` (PLAN.md:1159-1216) | Next (PROGRESS.md:1252) |
| 4 | Flag on by default; delete the reader-tools and CKEditor providers; convert origami's undo to a custom entry in the same commit that retires its `keydown.origami` handler; optionally convert `ImageUndoManager` (PLAN.md:1218-1231) | Not started |
| 5, 6 | Delete CKEditor and its workarounds; simplify page load | Not started |

### Entry kinds

`IUndoEntry` has a `label`, a `kind` of `"pageSnapshot" | "subtreeSnapshot" | "custom"`, `undo()`,
an optional `redo()`, an optional `prepareRedo()` that captures the redo state just before the undo,
and an optional `isAvailable()` (undo/undoTypes.ts:17, :38-74; PLAN.md:167-175). Snapshots are the
intended default and custom entries are for cases where a snapshot is too blunt (PLAN.md:162-165).
Today only custom entries exist: table operations (undo/tableUndo.ts:76-117) and, unused, the
compound entry (undo/compoundUndoEntry.ts). Snapshot entries are planned for Stage 3 and would
restore in three tiers (PLAN §4.11, PLAN.md:577-581): Tier 1 writes one editable's `innerHTML` back
and restores the selection; Tier 2 writes a `.bloom-canvas` subtree's HTML back and calls
`refreshCanvasElementEditing`; Tier 3, a whole page, is not to be built, because nothing needs it
(PLAN.md:632-650). Origami keeps its jQuery `clone(true)` copies (PLAN.md:636-644).

Two rules for writing an entry (PLAN.md:177-194, repeated at undo/undoTypes.ts:26-36): check before
undoing that what the entry changed is still as it left it, and throw if not; prefer state held as
data that would survive a reload, without insisting.

### How it decides steps

- One `push` is one entry. `runUndoable(label, fn)` makes everything pushed inside it one compound
  entry with the outermost label; undo runs the parts last first, redo runs them in order, and the
  compound can redo only if every part can (PLAN §4.13, PLAN.md:748-768; undo/UndoStack.ts:266-281;
  undo/compoundUndoEntry.ts:25-51). Nothing calls `runUndoable` today (searched: only specs).
- An operation that pushes nothing is not undoable. PLAN.md:179-185 names the unrecorded ones:
  moving and resizing canvas elements, Format dialog changes, Talking Book's sentence splitting, game
  settings, and until Stage 3 all typing.
- Typing, from Stage 3: one transaction per run of typing, closed on a word boundary (space,
  punctuation, Enter), a switch between inserting and deleting, a caret move or focus change, about
  1 s idle, or any other command; a transaction that changes nothing records nothing; each entry
  restores the selection, ranges included (PLAN §4.6, PLAN.md:333-340; §4.14 items 20-24,
  PLAN.md:876-889). It says outright that this is finer than CKEditor's grouping and that this is
  intended (PLAN.md:886-888).
- Until Stage 3, typing stays in CKEditor's history for each box. A counter decides whether the
  stack's newest entry or CKEditor's last change came later (undo/changeOrder.ts:1-35;
  undo/UndoStack.ts:128-136).
- 50 entries, counted, not measured in bytes (undo/undoTypes.ts:120; PLAN.md:196-197).

### Where the history lives

In the page frame, created on each page load (`setUpPageUndo`, undo/pageUndo.ts:23-31, called at
editablePage.ts:438). A page change or any reload of the same page starts it empty (PLAN §4.2,
PLAN.md:216-244). The Undo button reaches it through C# running `topBarButtonClick` in the page
frame (src/BloomExe/web/controllers/EditingViewApi.cs:470; bookEdit/js/bloomEditing.ts:1706-1717);
C# polls `workspaceBundle.canUndo()` on a timer, and the poll answers `true` when a previous call has
not finished (src/BloomExe/WebView2Browser.cs:1313-1329). PLAN.md:212-214 requires `canUndo` to stay
synchronous and cheap for that reason. Decision 7 (PLAN.md:1408-1417) moved the stack there from the
workspace frame on 2026-10-06 and dropped undo of page deletion.

### Async work

`runUndoable` keeps its scope open until an async operation settles, so everything pushed meanwhile
joins the gesture; the scope depth is global, so two independent async operations must not overlap
(undo/runUndoable.ts:20-24, :45-47). An undo or redo may itself be async; a re-entrant one is ignored
while one runs (undo/UndoStack.ts:174-177, :299-323). One async hazard is named: an undo while the
C# state machine is in `SavePending` must not let the save in flight merge content that the undo
discards (PLAN.md:657-663, risk 5 at :1335-1340). PROGRESS.md:904-908 reports that PR 8209
(BL-13502) removes `SavePending` and `DiscardInFlightSave` entirely.

### C# state and files

The plan records no C# state in entries. Redo has no C# side (decision 1, PLAN.md:1376-1386). Undo
of page deletion, the one case that needed C#, is dropped. For files, risk 8 (PLAN.md:1348-1353)
finds that support-file cleanup runs only from `Book.BringBookUpToDate` and the publish and upload
paths, so files that restored audio spans name still exist within a session; the Talking Book tool's
explicit delete and re-record are left to investigate. It rejects undo by navigation, because the
book DOM could only receive the undone state through the save's merge (PLAN.md:605-630).

### Boundaries

The only boundaries are a page change and a reload of the current page. PLAN.md:228-236 lists the
actions that reload the current page, from leaving Change Layout mode to Report a Problem, and says
that each one "discards the undo history of the page before it, as it always has". Nothing clears
the stack on a request to C#. A failed undo or redo clears the whole stack (below).

### Failures

A failed undo or redo, including an entry whose check before undoing finds the page changed,
discards the whole stack and rethrows (PLAN.md:763-768, decision 6 at :1405-1406;
undo/UndoStack.ts:299-323). An operation that throws inside `runUndoable` still records what it
pushed before the throw (undo/runUndoable.ts:37-38 closes the scope, and closing records the held
pushes); the page is not rolled back.

### CKEditor

The plan replaces it. Until Stage 3, CKEditor's history for each box is a legacy provider
(undo/legacyUndoProviders.ts:118-139), consulted after origami, the reader tools and the image undo
(:149-156), and standing aside when a table entry is newer. Ctrl+Z goes to whichever page-frame
handler claims it first: origami's in Change Layout mode, the reader tools' while a reader tool is
active, otherwise CKEditor's (PLAN.md:126-145). Stage 1 changed only the button path. In the new
editor, `beforeinput` with `historyUndo` or `historyRedo` is cancelled and routed to the stack, so
the browser's own history cannot diverge (PLAN §4.4, PLAN.md:309-317). §4.14 (PLAN.md:770-906) lists
25 things CKEditor 4.5.1 does without being asked that the new editor must reproduce, five of them
about undo (items 20-24).

### How far PROGRESS.md says it got

Stage 0 merged; Stage 1 merged (by git, not by PROGRESS.md); 66 undo tests; the routing checks are
the e2e spec `src/BloomE2E/tests/undo-routing.spec.ts` (PROGRESS.md:1156-1162). Stage 2 parked, its
comicaljs fix split out as PR 8466 (comicaljs 0.4.2, branch `origin/comicaljs-0.4.2`, commit
`52e8168a68`), Stage 3 next. Still open from Stage 0: inventory rows G2 (the async markup path),
G6 and G7 (highlights after a restore) (PROGRESS.md:72-75), and five reproduced bugs that are not
filed (PROGRESS.md:1038-1044).

---

## 2. Where the two designs agree

- **The history is per page load, in the page frame.** A page change or a reload of the page starts
  it empty, and the workspace frame only asks the page frame (PLAN §4.2; our §3.3). Both reached
  this independently, and both drop undo of page deletion (PLAN decision 7; OPEN-ISSUES.md, "Product
  decisions").
- **A refused undo loses history rather than damaging the page** (PLAN.md:183-185; our rule 4 of
  §3.0.2).
- **Two histories over the same nodes cannot keep one order, so typing has to join the one
  history** (PLAN.md:1211-1213 calls it "the payoff of the whole project"; our §1).
- **The browser's own undo has to be fenced** by cancelling `beforeinput` with `historyUndo` and
  `historyRedo` (PLAN §4.4; our case 7.7 and §3.3).
- **Undo restores the selection, ranges included** (PLAN §4.14 item 20; our §6.2 keeps a
  `StaticRange` before and after).
- **A step that changes nothing is not recorded** (PLAN §4.14 item 22; our empty transaction is
  dropped, §6.1, and MINIMUM item 1 counts only a net change).
- **One gesture is one step, however many layers record inside it; the outermost wrapping names it**
  (PLAN §4.13; our §11.8.1, "Transactions inside transactions").
- **A colour picker drag is one step, and Cancel restores what was there** (PLAN §4.14 item 9; our
  §3.2, `beginTransaction` for `ColorPickerDialog`).
- **Highlights painted over live Ranges must be repainted after any restore** (PLAN.md:583-592; our
  case 11.1 step 4 and §11.8.3, "Display").
- **Redo comes from keys only, Ctrl+Y and Ctrl+Shift+Z, with no button** (PLAN decision 1; our goal
  8 and MINIMUM item 10).
- **Origami keeps its own copies for now** (PLAN.md:636-644; our §11.6 and MINIMUM item 9).
- **Putting back the same element object keeps its CKEditor instance.** The BL-17003 commit
  `ab5b056331` reuses the deleted element "so its editables keep their CKEditor instances"; our
  EXPERIMENTS §2.2 observed the same.
- **comicaljs bubbles that Comical has replaced keep reacting to changes.** Our §11.8.3 (row 2)
  found it by reading; the other plan found it in BL-17003 and fixed it upstream (comicaljs 0.4.2).
- **Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z reach the page's JavaScript; C# takes none of them**
  (PROGRESS.md:1176-1182; our EXPERIMENTS §4).
- **CKEditor keeps 20 steps per box**, because `config.undoStackSize = 0` (lib/ckeditor/config.js:82)
  is read as `undoStackSize||20` (lib/ckeditor/ckeditor.js:679) (PLAN.md:152-153; MINIMUM.md:24).
- **No file is deleted while editing**: cleanup runs only outside an editing session (PLAN risk 8;
  our §3.4 and §11.8.5).
- **Small PRs, Bloom shippable after each** (PLAN §5.4; goal 8).

---

## 3. Where they differ

Each item states what each plan does and the OUTLINE §1 goal that the difference bears on.

1. **What an entry is.** PLAN: code builds an entry and pushes it; the default is a snapshot of HTML,
   with custom entries holding their own `undo()` (PLAN §4.1). Ours: a `MutationObserver` records
   every change to the page, and code only says where a transaction starts and ends (§6.1). Goals 2
   and 4.
2. **An action that no code has wrapped.** PLAN: it is not undoable; its changes can leave an older
   entry's check failing, which discards the whole stack (PLAN.md:179-185, risk 10 at
   :1356-1359). Ours: a real click or key press opens an "Undo edit" transaction (rule 2), and a
   change made outside any input joins the latest transaction (rule 1). Goals 3, 4 and 8.
3. **The check before undoing.** PLAN: each entry's author writes it for that entry (the table entry
   compares the library's newest label, undo/tableUndo.ts:81-91). Ours: one generic check of every
   recorded value and every removed subtree (rule 4). Goals 2 and 4.
4. **What a failed undo discards.** PLAN: the whole stack, redo included (undo/UndoStack.ts:299-323).
   Ours: that transaction and every older one; the redo stack stays (rule 4, §3.6). Goal 3.
5. **An action that throws partway.** PLAN: what it pushed before the throw is recorded and the page
   keeps the partial change (undo/runUndoable.ts:37-38). Ours: its changes are put back at once and
   the error is rethrown (rule 9, §3.6). Goal 4.
6. **Redo.** PLAN: optional for each entry; an entry without `redo` stops redo below it (its "redo
   floor", undo/UndoStack.ts:154-157); table entries have none; the redo state is captured just
   before the undo (`prepareRedo`). Ours: redo is the same mechanism with after values and the same
   check, so every transaction can redo (§10.5). Goals 3 and 8.
7. **Typing steps.** PLAN: word-level transactions, deliberately finer than CKEditor's (PLAN §4.6,
   PLAN.md:886-888). Ours: the steps that users know from the editors they use every day (OUTLINE
   §3); MINIMUM item 3 copies CKEditor's breaks (every 25 keystrokes, a change in the kind of input,
   around commands). Goals 3 and 7.
8. **How a restore writes the page.** PLAN: HTML strings written back (Tier 1 one editable's
   `innerHTML`, Tier 2 a `.bloom-canvas` subtree), which builds new nodes; then repaint highlights
   and, for Tier 2, `refreshCanvasElementEditing`. Ours: the same node objects go back with their
   recorded attributes, text and children, so wiring on those nodes stays (EXPERIMENTS §2.2; §4.4).
   Goals 2 and 4.
9. **Selection.** PLAN: a new anchor of character offset, line-break count and a structural locator
   for the editable, because restored nodes are new (PLAN §4.3, PLAN.md:255-266). Ours: a
   `StaticRange` on the kept nodes (§6.2). Goal 3.
10. **CKEditor.** PLAN: retire it; text joins the stack only with the new editor (Stage 3), and the
    CKEditor path keeps its own history until then. Ours: the design does not depend on it (goal 5);
    CKEditor's undo plugin is turned off or driven, and the keys are taken in the capture phase
    (case 7.7, §8.2). Goals 1 and 5.
11. **The keys.** PLAN: today Ctrl+Z goes to origami, the reader tools or CKEditor, and the stack's
    Ctrl+Y binding acts last, in the bubble phase, only when nothing else claimed the key
    (undo/redoKeyBinding.ts:52-77); unifying the keys waits for Stages 3 and 4. Ours: the page takes
    all three keys first, in the capture phase, from the first step, and the other frames forward
    them (MINIMUM item 10; case 7.7). Goals 1 and 3.
12. **The old histories during the move.** PLAN: wrap them as legacy providers and delete them stage
    by stage; ordering between them rests on context ("contextually exclusive",
    undo/legacyUndoProviders.ts:14-18) and on the change counter for CKEditor. Ours: switch them all
    off in the first step (§10.1, MINIMUM item 9). Goals 1, 3 and 8.
13. **Tables.** PLAN: each operation that bloom-table announces is an entry that asks the library to
    undo it, with no redo (decision 8; undo/tableUndo.ts). Ours: bloom-table's history is switched
    off and the observer records its changes (§17.3, MINIMUM item 9). Goals 1 and 6.
14. **Reloads of the current page.** PLAN: they stay and empty the stack (PLAN.md:228-236); undo
    across a reload is a later option that would need data-based entries (PLAN.md:238-244). Ours:
    APPROACHES §3.3 decides to remove them, since recorded mutations hold node objects; MINIMUM item 8
    keeps them for the first version. Goals 3 and 8.
15. **Async actions.** PLAN: an async `runUndoable` gathers what is pushed meanwhile, and typing is
    not on the stack until Stage 3. Ours: everything that changes while a transaction is open joins
    it, the user's typing included (rule 7). Goals 3 and 4.
16. **Nested wrapping that fails.** PLAN: no rollback; the parts are kept as one compound entry.
    Ours: an inner transaction that fails puts back only its own changes, and the outer one carries
    on (§11.8.1). Goal 4.
17. **C# state, files and requests to C#.** PLAN: no C# state in entries; files only for Talking
    Book's explicit delete and re-record, still to investigate (risk 8). Ours: `addChangeOutsidePage`
    for state outside the page, the file rules of §11.8.5, and a request interceptor that ends the
    history (§11.8.7; MINIMUM item 7 inverts it to a list of harmless requests). Goals 4 and 7.
18. **Boundaries.** PLAN: page change and reload only. Ours: also `clearUndoAndRedo()` from the
    request interceptor and from components (§11.8.7), and, in MINIMUM item 4, input outside the
    page frame that changes the page. Goal 4.
19. **Components that hold copies of page content.** PLAN: a registry that sets up and tears down
    handlers for each page (`pageScope.ts`, PLAN §4.10), `applyToolboxStateToPage()` for the toolbox
    frame's observers (PLAN.md:542-551), and a repaint of highlights. Ours: `onBeforeUndoOrRedo` and
    `onAfterUndoOrRedo`, with the component list of §11.8.3. Goals 2 and 4.
20. **Cleanup of captured content.** PLAN: once CKEditor is gone an editable's clean HTML is
    `innerHTML` plus a small normalizer (`getCleanHtml`, PLAN §4.7). Ours: one filter module shared
    with the save path's cleanup (rule 6, §3.7). Goal 4.
21. **Descriptions.** PLAN: an English `label` for tooltips and logging (undo/undoTypes.ts:39-40);
    labels in the UI are a follow-up (PLAN §9). Ours: an l10n key for every transaction, with "Undo
    edit" for unwrapped actions (§6.1). Goal 3.
22. **Size.** PLAN: 50 entries. Ours: not decided (OUTLINE §12). No goal in §1.

---

## 4. What the retire-CKEditor docs found that ours missed or got wrong

1. **The paste filter is bypassed whenever the clipboard HTML holds a styled span.**
   `BloomField.restoreHtmlMarkupIfNecessary` (bookEdit/bloomField/BloomField.ts:462-493) treats
   `getData("cke/id")` as "pasted from inside CKEditor", but CKEditor's `dataTransfer` constructor
   gives every transfer an id (`"cke-"+CKEDITOR.tools.getUniqueId()`, lib/ckeditor/ckeditor.js:635,
   read here), so the full clipboard HTML replaces the filtered HTML. Observed by them: a table, an
   iframe, an image and a `div` with an id all reached the box when one styled span came with them
   (PASTE-DROP-BASELINE.md:158-185; PROGRESS.md:956-969). Not filed (PROGRESS.md:1038-1040).
   *Changes:* APPROACHES case 11.1 (APPROACHES.md:3283), which says the `pasteFilter` keeps only a
   few elements.
2. **With a reader tool active, Ctrl+Z runs two undos.** The reader tools' handler is a jQuery
   `keydown` on each `.bloom-editable` that returns `false` (toolbox/readers/decodableReader/
   decodableReaderTool.tsx:163-187); that stops propagation but not CKEditor's own listener on the
   same element, which matches what they observed: both histories undo, and Ctrl+Y redoes both and
   loses typing (PROGRESS.md:894-899). *Changes:* MINIMUM "What today's undo covers" (MINIMUM.md:25,
   :36-38), which says the reader tools' history shadows CKEditor's and that one of them takes
   Ctrl+Z.
3. **The reader tools' undo puts back stale CKEditor bookmark spans.** `doMarkup` snapshots
   `innerHTML` while a `cke_bm_*` span is in the box, and each undo restores one, so they pile up and
   the next comparison is off by one level. Observed by them, not re-run (PROGRESS.md:886-893). This
   is an instance of what our rule 6 prevents: a capture that keeps markup the save strips.
   *Changes:* APPROACHES §11.8.3 row "Reader tools' own undo" and MINIMUM's gains (switching that
   history off removes this and item 2).
4. **Re-running `SetupElements` adds the edit key handlers again.** `AddEditKeyHandlers`
   (bookEdit/js/bloomEditing.ts:204, called at :732) puts two handlers on `document` with
   `$(document).on("keydown", ...)` (:295, :305) and jQuery `keydown` handlers on each editable.
   `refreshCanvasElementEditing` calls `SetupElements` (canvasElementManager/CanvasElementManager.ts:
   1041, :1074). Observed by them: 1, then 2, then 3 handlers firing for Ctrl+R and F7, with F6
   wrapping the selection in `<sup>` twice (PROGRESS.md:929-944). The BL-17003 commit `2be2b67a62`
   found the same kind of growth in its own undo: rebuilding source and hint bubbles after
   `SetupElements` had already done so added a document-wide observer on every undo. *Changes:*
   APPROACHES §2 (APPROACHES.md:117-119, which calls `SetupElements` the start of re-wiring), case 7.1
   (APPROACHES.md:2006, whose `onAfterUndoOrRedo` calls `refreshCanvasElementEditing`), §11.8.3 rows
   for comicaljs and source bubbles, and MINIMUM item 11.
5. **Ctrl+Y in a CKEditor box does redo.** Our EXPERIMENTS §3.2 (EXPERIMENTS.md:465-467) reports that
   Ctrl+Y and Ctrl+Shift+Z after an undo did nothing, and APPROACHES repeats it
   (APPROACHES.md:2562-2563). The e2e spec asserts that Ctrl+Y after Ctrl+Z runs CKEditor's `redo`
   and brings the text back (src/BloomE2E/tests/undo-routing.spec.ts:107-129), and three green runs
   are reported (PROGRESS.md:1162). With real key presses Ctrl+Y redid three times out of five, the
   misses unexplained (PROGRESS.md:1183-1188). One of the two results depends on something that
   neither document states. *Changes:* EXPERIMENTS §3.2, APPROACHES case 7.7 and §9.4.
6. **`CKEDITOR.currentInstance` can belong to a box that is no longer on the page**, and its
   `undoManager` still says it can undo. Master guards against it with `isConnected`
   (editablePage.ts:352-355). Also, `ckeditorUndo` can return `false` right after `ckeditorCanUndo`
   said yes, because CKEditor clears `hasUndo` only when it next refreshes its state (comment at
   editablePage.ts:362-371). *Changes:* APPROACHES §8.2, the "Needs a running Bloom" row
   (APPROACHES.md:2800), which lists `CKEDITOR.currentInstance` after removal as still to see; and
   case 7.7 option 2, which would drive the plugin.
7. **comicaljs 0.4.2 fixes the stale bubbles** (comical-js PR 121: "Bubble objects that Comical has
   replaced no longer react to changes", commit `52e8168a68`). This branch has 0.4.1
   (package.json:156). *Changes:* APPROACHES §11.8.3 row 2 (APPROACHES.md:4135), which says to fix it
   in comicaljs.
8. **Module state in a shared chunk exists in every frame.** Vite puts `workspaceRoot` into a chunk
   that the page and toolbox bundles import, so its top level ran in all three frames, and for a
   while the Undo button undid from a second stack (PROGRESS.md:982-992; the warning stays at
   undo/UndoStack.ts:326-331). *Changes:* APPROACHES §11.8.1: say that the one `PageHistory` is
   created only by the page frame's setup and that `pageHistory()` must go through
   `getEditablePageBundleExports()` in every frame, including the page frame, never through a
   module-level instance.
9. **C# polls `canUndo`, and the poll says yes when it overlaps.** `CanUndoAsync` returns `true`
   when the previous call has not finished (src/BloomExe/WebView2Browser.cs:1313-1329). *Changes:*
   OPEN-ISSUES item 6, which says nothing tells the button when to change: the timer poll does
   today, and any `onUndoRedoChanged` design replaces or feeds it. Also APPROACHES §6.1, whose API has
   no cheap synchronous "can undo" for that poll.
10. **PR 8209's own `MutationObserver`.** Its `pageSnapshot.ts` (branch
    `origin/BL-13502-save-without-reload`, present in this clone) watches the body and posts the
    page after 25 ms of quiet; its comment says CKEditor makes about nine observer batches per
    keystroke; and PR 8209 removes `SavePending` and `DiscardInFlightSave` (PROGRESS.md:901-927,
    checked on the branch for the observer and the 25 ms). APPROACHES §3.3 relies on PR 8209's
    description only. *Changes:* APPROACHES §3.3 and §3.7 (a second observer on the same body,
    whose posts our recorder must not count as changes; inferred, not tried).
11. **An undo while a save is in flight.** Today an undo during `SavePending` can be overtaken by the
    save in flight (PLAN.md:657-663; `SavePending` at src/BloomExe/Edit/EditingStateMachine.cs:14).
    *Changes:* MINIMUM item 8, which keeps the reloads and says nothing about this case (whether it
    can damage the page under MINIMUM is inferred, not traced).
12. **CKEditor writes to boxes while it attaches**: it replaces an empty div's content with
    `<p><br></p>` after `CKEDITOR.inline()` returns, which is why `StyleEditor`, `PlaceholderProvider`
    and `GamePromptDialog` wait for it (PLAN.md:66-80); Bloom already keeps those changes out of the
    change counter by testing `undoable()` (bookEdit/js/bloomEditing.ts:2160-2167). *Changes:*
    MINIMUM item 6 (the moment when the page is ready) and CODE-WALK issue 3 must count CKEditor's
    `instanceReady` among the things that end page load.
13. **CKEditor's grouping in detail**: it ignores IME key presses (keyCode 229) and does not count a
    paste or a drop as typing (PLAN §4.14 item 24, PLAN.md:886-889). *Changes:* MINIMUM item 3 and
    case 7.7, which copy CKEditor's breaks without these two.
14. **`BloomField.PreventRemovalOfSomeElements` uses the browser's own undo** to reverse a deletion
    (bookEdit/bloomField/BloomField.ts:887-897), and should become a `beforeinput` guard that refuses
    a `delete*` over a `.bloom-preventRemoval` element (BEHAVIOR-INVENTORY.md:250-255). OUTLINE §17.2
    names this third history; MINIMUM does not. *Changes:* MINIMUM item 10.
15. **Same-page reloads that our §3.3 table does not list**: moving a page in the page list, the
    book's files changing outside Bloom, Report a Problem, and some theme changes (PLAN.md:228-236).
    CODE-WALK §5 covers Report a Problem and videos edited elsewhere. *Changes:* APPROACHES §3.3.
16. **PR 8387's code has now been read.** APPROACHES §10.1 says "That code was not read"
    (APPROACHES.md:3063-3065). *Changes:* APPROACHES §10.1, using section 1 above.

Also found by them, with no effect on our documents: `ImageUndoManager`'s page check compares
`data-page-id`, which nothing sets (js/ImageUndoManager.ts:314-320; PROGRESS.md:851-855), and
MINIMUM switches that undo off anyway; and a plain-text paste that was not HTML-encoded (BL-16982,
fixed in PR 8441).

---

## 5. What ours found that theirs missed

1. **Actions that nobody wraps.** PLAN accepts that most actions stay unrecorded "for a long time"
   and relies on each entry's own check (PLAN.md:179-185). Our rules 1 and 2 give every page change a
   transaction from the first step, and the development log of §10.7 lists what is left to wrap.
2. **Reactions that land after the action.** The markup pass 500 ms after typing, `OverflowChecker`
   100 ms after a key, and a picture's refit on `load` change content outside the box or after the
   step (§5.3; case 11.1; CODE-WALK issue 1). PLAN's Tier 1 restores one editable's `innerHTML`, so
   a canvas element that `OverflowChecker` resized around the box would keep its size from after
   the step (inferred from PLAN.md:579 and our case 11.1).
3. **Components that keep copies of page content and write from them later**: comicaljs, the
   Canvas tool panel, the Games tool's toggles, Talking Book's playback and caches, the sign
   language slider, the image description checkbox, and more (§11.8.3, with `onBeforeUndoOrRedo`
   and `onAfterUndoOrRedo`). PLAN's restore plan covers handlers, toolbox observers and highlights
   (PLAN §4.10, §4.11), not these.
4. **State outside the page and files.** Every audio recording writes a file under an id that earlier
   states name (§11.3); `DeleteSupersededAiImageFiles`, Sign language Delete and
   `editView/addImageFromUrl` delete or overwrite files that a page names (OPEN-ISSUES, "Change
   Bloom's code"); book metadata, settings and the request interceptor (§11.8.4, §11.8.7). PLAN's
   risk 8 stops at Talking Book's explicit delete and re-record.
5. **StyleEditor's rules are invisible to anything that reads the DOM**, because it writes the CSSOM
   (§2, §11.8.6). PLAN's Stage 2c expects "a snapshot of `userModifiedStyles`" to cover style undo
   (PLAN.md:1153-1154) without saying where the rules are read from.
6. **One cleanup shared with the save path** (rule 6, §3.7). Item 3 of section 4 is a bug of exactly
   the kind that this rule prevents.
7. **Changes to nodes after they are removed** (Duplicate, §4.4), and a check that covers removed
   subtrees (rule 4).
8. **Keys while focus is outside the page frame.** The toolbox, page list and top bar get the keys
   when they have focus and must forward them (EXPERIMENTS §4; case 7.7). PLAN binds keys only in the
   page frame (undo/redoKeyBinding.ts:1-9). Chromium also sends `historyUndo` to the editor when a
   focused field elsewhere runs out of its own history (§3.3, after Lexical).
9. **CKEditor fires no `beforeinput` for Ctrl+Z** (EXPERIMENTS §3.2), so the `beforeinput` fence of
   PLAN §4.4 works only where CKEditor is not loaded; while CKEditor is loaded, a capture-phase
   `keydown` handler is needed (case 7.7).
10. **A defined end of page load** before which nothing is recorded (CODE-WALK issue 3; MINIMUM
    item 6), which PLAN does not need because nothing records automatically.
11. **An action that throws is rolled back** (rule 9), where PLAN keeps its partial pushes.
12. **The reloads can be removed** (§3.3), which PLAN lists only as a later option that would make
    the stack outlive a reload.

---

## 6. What it means for MINIMUM.md

### Already built by Stage 1

- A history object that lives in the page frame and is set up on each page load (undo/pageUndo.ts:
  23-31, editablePage.ts:438), which is the place where `pageHistory()` would be created.
- The Undo button's route, which MINIMUM item 10 keeps: the button posts `editView/topBarButtonClick`,
  C# runs `topBarButtonClick` in the page frame (src/BloomExe/web/controllers/EditingViewApi.cs:470),
  which calls `handleUndo` (bookEdit/js/bloomEditing.ts:1714-1717); C# polls
  `workspaceBundle.canUndo()`, which asks the page frame (workspaceRoot.ts:257-258).
- A Ctrl+Y binding in the page frame, with the test that makes it leave the key to origami in
  Change Layout mode, which MINIMUM needs too (undo/redoKeyBinding.ts:66; see below).
- The repaint of reader and Talking Book highlights after an undo that rewrites a box, which MINIMUM
  item 11 calls "already repainted" (undo/legacyUndoProviders.ts:85-89, :129-136;
  bookEdit/js/bloomEditing.ts:2153-2158).
- The e2e routing checks and their helper (src/BloomE2E/tests/undo-routing.spec.ts,
  src/BloomE2E/helpers/undo.ts), which can test that each gesture reaches the page history and only
  it.
- Nested wrapping (`runUndoable`, compound entries), which MINIMUM leaves out.

### Built differently by their later stages

- **Typing** (MINIMUM item 3): Stage 3's `typingTransactions.ts` makes word-level steps from
  `beforeinput` in the new editor and pushes `innerHTML` snapshot entries; MINIMUM records typing
  with the observer and breaks it where CKEditor does.
- **Paste and cut** (MINIMUM item 5): Stage 3's `pasteHandler.ts` and `clipboard.ts` push one entry
  each; MINIMUM wraps the functions that C# calls.
- **Pictures** (MINIMUM item 5): Stage 4 converts `ImageUndoManager` commits to entries; MINIMUM wraps
  `changeImage` and `changeImageByElement` and switches the image undo off.
- **Deleting a canvas element**: Stage 2b (draft PR 8451) records the neighbours and reverses the
  comic family change in code; under MINIMUM it is an "Undo edit" step with no code.
- **Origami**: Stage 4 makes its copies a custom entry on the stack; MINIMUM keeps them inside the
  mode and ends the history on entering and leaving it.
- **The keys**: Stages 3 and 4 move them onto the stack; MINIMUM takes them first, at once.

### Conflicts with what PROGRESS.md says is done or decided

1. **Decision 8 and the merged `undo/tableUndo.ts`**: MINIMUM item 9 switches off bloom-table's
   history and with it the table entries that master now has. Switching it off needs a change in
   bloom-table (§17.3; inferred that no option exists today, not checked in the library).
2. **Decision 6's failure rule**: PLAN discards the whole stack on a failed undo; MINIMUM item 1 uses
   rule 4, which keeps redo.
3. **PROGRESS.md's next step is Stage 3 on `UndoStack`** (PROGRESS.md:1252), and PR 8451 builds on
   it too. MINIMUM replaces `UndoStack`, its providers and `changeOrder.ts`. Only one of the two can
   go next on the same code.
4. **Today's routing, as MINIMUM describes it**:
   - MINIMUM.md:25 and :36-38 say that the reader tools' history shadows CKEditor's and that one of
     them takes Ctrl+Z; with a reader tool active both run (section 4, item 2).
   - MINIMUM.md:38 says Ctrl+Y reaches the stack's redo; no entry on the stack can redo today (table
     entries have no `redo`, and nothing calls `runUndoable`), so the binding never acts and Ctrl+Y
     always goes to CKEditor, origami or the reader tools.
   - MINIMUM.md:36 says C# calls `handleUndo`; C# calls `topBarButtonClick`, which calls it.

### Changes that MINIMUM.md needs

- **Item 4** must exempt the Undo button. It is a click in the top bar that changes the page, so as
  written it would end the history that it is undoing.
- **Item 5** wraps `editView/topBarButtonClick` in a `doTransaction`, but that function also runs
  the Undo button's undo (bookEdit/js/bloomEditing.ts:1714-1717), and undo opens no transaction (rule
  2). Wrap the cut and copy cases only.
- **Items 9 and 10 together**: the capture-phase handler must stand down in Change Layout mode, as
  undo/redoKeyBinding.ts:66 does, or origami's own Ctrl+Z and Ctrl+Y (js/origami.ts:156) never fire.
- **Item 10** should also cancel `beforeinput` with `historyUndo` and `historyRedo`, and say what
  replaces `PreventRemovalOfSomeElements`'s `execCommand("undo")` (section 4, item 14).
- **Item 11** cannot call `refreshCanvasElementEditing` as it stands, because each call adds edit key
  handlers to `document` again (section 4, item 4). Either fix that first (PLAN §4.10's teardown, or
  attach those handlers once), or re-wire with something that does not run `SetupElements`.
- **Item 6** must count CKEditor's `instanceReady` writes (section 4, item 12).
- **Item 3** should ignore IME key presses and not count paste or drop as typing (section 4, item 13).
- **Item 8**: say what happens to an undo during `SavePending` (section 4, item 11).
- **"What today's undo covers"**: correct the three routing statements above.
- **Gains**: add that switching off the reader tools' history removes the double undo and the stale
  bookmark spans (section 4, items 2 and 3).
