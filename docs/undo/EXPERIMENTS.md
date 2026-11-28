# Undo in Bloom: experiments in a running Bloom

Status: results, 2026-10-14. Companion to [OUTLINE.md](OUTLINE.md) and [APPROACHES.md](APPROACHES.md);
"§" numbers refer to the outline, "APPROACHES §" to the other file. Today's undo implementation was
not read, and none of these experiments changed Bloom's source.

Order follows the request as revised during the work: experiment 2 (recording every change, with
its writer) first, then experiment 1 (re-inserting the same node objects), then experiment 3
(Ctrl+Z, Ctrl+Y and the browser's `beforeinput`). Section 4 was run afterwards, in a separate
Bloom, with real key presses from the operating system.

Each result is marked **Observed** (seen in the running Bloom, with the evidence named) or
**Inferred** (reasoned from observations or code, not seen directly).

---

## 0. Setup common to all three

- **Bloom.** Built from this worktree (`D:\undo`, branch `more-undo-exploration`, commit
  `d846317156`) by the `run-bloom` launcher, then launched the way `src/BloomE2E` launches it: the
  e2e fixture (`fixtures/launchBloom.ts`) with a new throwaway collection in `%TEMP%`, its own
  `--user-settings-folder`, `--e2e --automation --dont-disturb`, the `tables` experiment, and the
  Enterprise test subscription code. No developer collection or book was opened or edited.
- **Front end.** Served by a Vite dev server on port 5173 (`BLOOM_E2E_VITE_PORT=5173`). The built
  bundle in `output/browser` could not be used: it was built from other source. It posted
  `editView/pageSnapshot` and posted `editView/pageDomLoaded` without a `text/plain` body, neither
  of which this worktree's C# accepts, so the Edit tab never reached its Editing state
  (Bloom's log: 574 "Cannot Find API Endpoint" reports and a Debug.Assert in
  `HandlePageDomLoaded`). The fixture's freshness check compares file times only and passed it.
  Logged in `PAPERCUTS.md`.
- **Test book.** Basic Book with Leveled Reader enabled, one "Just Text" page (one English text box
  with CKEditor) and one "Canvas" page with a speech bubble and a `bloom-table` table (2 by 2),
  made by `host.spec.ts`, which then kept that Bloom alive for the drivers. A later run added a
  second Canvas page with a fresh table (experiment 1e).
- **Driving.** Playwright attached over CDP. Clicks, drags and typing are CDP input events
  (`Input.dispatchMouseEvent`, `Input.dispatchKeyEvent`), which the page sees as trusted
  (`isTrusted` true). Experiment scripts were injected with `Page.addScriptToEvaluateOnNewDocument`
  (Playwright `addInitScript`), so they ran before Bloom's own scripts in each new page frame.
- **Stopped.** The throwaway Bloom, the launcher stack and Vite were stopped at the end.

The experiments used the scripts, logs and screenshots below. They were working files in a
temporary folder on the machine that ran the experiments, and they were not kept. The results
recorded in this document are the record of the experiments.

| File | What it is |
|---|---|
| `setup\host.spec.ts`, `setup\pw.config.ts` | Launch Bloom, make the test book, keep Bloom alive |
| `recorder.js` | The mutation recorder injected into the page frame (experiment 2) |
| `drive\exp2.spec.ts`, `drive\exp2b.spec.ts` | Editing sessions and drag gestures (experiment 2) |
| `analyze.py`, `net.py`, `gestures.py`, `detached.py`, `show.py`, `dump_cat.py` | Analysis of the recorder logs |
| `exp2-A.json`, `exp2-B.json`, `exp2-C.json`, `exp2b.json`, `net-out.txt` | Raw logs and net-change analysis |
| `drive\exp1.spec.ts`, `exp1c`, `exp1d`, `exp1e` | Re-insertion and clone trials (experiment 1) |
| `exp1-reinsert.run.log`, `exp1-clone.run.log`, `exp1e.json`, `shots\*.png` | Their results and screenshots |
| `drive\exp3.spec.ts`, `exp3.json` | Ctrl+Z / Ctrl+Y event order (experiment 3) |

They were run this way, with paths relative to that folder: Vite on 5173 in `src/BloomBrowserUI`,
then `BLOOM_E2E_VITE_PORT=5173 pnpm -C src/BloomE2E exec playwright test --config setup\pw.config.ts`
in the background; once `setup\info.json` said `"ready"`, each driver ran with
`pnpm -C src/BloomE2E exec playwright test --config drive\pw.config.ts exp2`
(or `exp1`, `exp1e`, `exp3`). Creating `setup\STOP` ended the host and its Bloom.

---

## 1. Experiment 2: what writes the page, and how often it hits what the user changed

**Questions** (behind APPROACHES §3.1, §4.4 and §5.3, extended): which code writes the page DOM, with
or without user input nearby; when Talking Book adds its sentence spans or a reader tool updates
its markup, does it wrap or split the text the user just typed; how much a drag produces; what
happens to removed nodes while they are detached; does any React component render into saved
page content.

### 1.1 How the recording works

`recorder.js`, injected at document start in every frame, does three things and filters nothing:

- A `MutationObserver` on `document` with `subtree`, `childList`, `attributes`,
  `attributeOldValue`, `characterData` and `characterDataOldValue`.
- It wraps the DOM methods and setters that write: `setAttribute`, `removeAttribute`,
  `toggleAttribute`, `append`/`prepend`/`before`/`after`/`remove`/`replaceWith`/`replaceChildren`,
  `insertAdjacent*`, `appendChild`/`insertBefore`/`removeChild`/`replaceChild`/`normalize`,
  `innerHTML`/`outerHTML`/`textContent`/`nodeValue`/`data` setters, `CharacterData` edits,
  `splitText`, `Range.deleteContents`/`extractContents`/`insertNode`/`surroundContents`,
  `document.execCommand`, `classList` methods, `img.src`, `a.href`, `style.setProperty` and,
  through a `Proxy` on `element.style`, every `style.x = ...` (in this WebView2 the CSS
  properties are own properties of each declaration, not prototype accessors). Around the
  outermost wrapped call it calls `observer.takeRecords()` before and after, so the records that
  call caused are stored with `new Error().stack`. Records with no wrapped call around them
  (typing, the parser) are stored as "native-or-unpatched".
- It logs trusted `keydown`, `keyup`, `beforeinput`, `input`, `pointerdown`, `pointerup`,
  `click`, `paste`, `cut`, `drop` and composition events in every frame, and stamps each record
  with the time since the last of them. CSSOM writes (`insertRule`, rule `setProperty`) are
  logged separately because they produce no records.
- Every element removed is watched by its own `MutationObserver` for 4 seconds, and its
  `outerHTML` and CKEditor instance status are compared at removal and after.

Sessions (`exp2.spec.ts`), each step separated by markers, 2.5 to 6 seconds apart:

- **Phase A, toolbox closed.** Text page: type a sentence, Backspace x5, double-click a word and
  Ctrl+B, Enter and a new line, select a word and Delete, 6 s idle. Canvas page: select the
  bubble, drag it, resize it, drag its tail, type in it, type in a table cell, drag a column
  boundary, add a row with "+", delete a row from the row menu, duplicate the bubble, delete the
  duplicate, 6 s idle.
- **Phase B, Talking Book open.** The same text steps; on the canvas page, select, drag and type
  in the bubble and a cell.
- **Phase C, Leveled Reader open.** The same, plus one long sentence typed (300 keystrokes).

Gestures (`exp2b.spec.ts`), toolbox closed: drag the bubble, resize it by its side handle, drag
its tail tip, drag a column boundary, drag a row boundary.

"User change" below means a record made within 100 ms of a trusted input event by any writer
except Talking Book, the reader tools and `OverflowChecker`. "Bloom's own change" is every other
record in page content. "Net change" means the value at the end of a step differs from the value
at its start: an attribute or text set and set back, or a node added and removed again within the
step, does not count. Analysis: `net.py`.

### 1.2 Results: volume, and what most records are

**Observed.**

- **Most records are not page content.** Typing one sentence (27 keystrokes, 4.1 s, phase A) made
  775 records, 213 KB as logged: 471 in page content, 247 outside `.bloom-page` (CKEditor's
  toolbar, `#measureTextDiv`, qTip), 57 jQuery temporary ids. The net change in page content was
  one text node.
- **`OverflowChecker` clones the text box on every keystroke to measure it.**
  `OverflowChecker.contentHeight` (`OverflowChecker.ts:121`) inserts a copy of the
  `.bloom-editable` into the translation group, styles its paragraphs (`flex-grow`,
  `flex-shrink`), measures, and removes it. 432 of the 471 page-content records above are this.
  None of it is a net change. It also writes `#measureTextDiv` outside the page.
- **jQuery writes temporary `id`s on page elements.** Sizzle sets `id="sizzle..."` on the context
  element of a scoped query and removes it in the same call: on `body`, on `.bloom-editable`
  elements (from `BloomField.ts:527` on every keystroke, from the reader tools on every markup
  pass). One 400 ms bubble drag in phase A made 1,042 of these.
- **Idle produces nothing.** Every 6-second idle step, in all three phases, recorded zero
  records. No timer wrote to the page while the user did nothing.

Record volume per drag gesture (`exp2b.json`, `gestures.py`; "during" is pointerdown to
pointerup, "after" is the 2.5 s that followed):

| Gesture | Duration | Records during (KB) | Of which page content | Page-content records after |
|---|---|---|---|---|
| Drag bubble 40,25 | 388 ms | 266 (66 KB) | 23: `style` 8, `data-bubble` 7, `class` and `data-bloom-active` the rest | 1 |
| Resize bubble by side handle | 762 ms | 1,347 (441 KB) | 624, of which 600 `OverflowChecker` copies | 147, all `OverflowChecker` |
| Drag tail tip | 406 ms | 208 (57 KB) | 22 | 1 |
| Drag table column boundary | 200 ms | 42 (17 KB) | 27: `data-column-widths` 13, table `style` 12 | 12 |
| Drag table row boundary | 205 ms | 78 (23 KB) | 27: `data-row-heights` 12, table `style` 12 | 12 |

The intermediate values of a drag are written once per pointer move to the same few attributes;
the net change of each drag is one value per attribute.

### 1.3 Results: the key question, Talking Book and reader tool writes

**Observed, Talking Book.** About 530 ms after the last keystroke, Talking Book's markup pass
(`AudioRecording.getUpdateMarkupAction`, through `mainTask` in `pageEditingMarkup.ts`) replaces
the **content of the paragraph** the user typed in with `$(p).html(...)`
(`audioRecording.ts:2679`). The text node the user's keystrokes edited is removed, and new
`span.audio-sentence` elements and text nodes take its place, splitting the typed text into
sentences. It also writes `data-element-we-are-processing` on every paragraph and removes it
again, and writes `data-audiorecordingmode` on the box to its existing value. Paragraphs not
typed in were not rewritten in this run. One case, step "B text type sentence", the third
paragraph:

Before (the user has just typed " The sun is very hot today." inside an existing sentence span):

```html
<p><span id="d5fd1d3a-..." class="audio-sentence">is a new line of text. The sun is very hot today.<span data-cke-bookmark="1" id="cke_bm_29C" style="display: none;">&nbsp;</span></span></p>
```

After (new node objects throughout; the old span and text node are detached):

```html
<p><span id="d5fd1d3a-..." class="audio-sentence">is a new line of text.</span> <span id="i1d22be14-..." class="audio-sentence">The sun is very hot today.</span><span id="cke_bm_29C" style="display: none;">&nbsp;</span></p>
```

Opening the Talking Book tool on the text page did the same to the paragraphs (net: 11 nodes
added, 4 removed, 2 attributes), and going to the canvas page with the tool open did the same to
the bubble and cells.

**Observed, Leveled Reader.** About 515 ms after the last keystroke, the reader's markup pass
(`LeveledReaderTool.updateMarkup` → `ReaderToolsModel.doMarkup` →
`EditableDivUtils.doCkEditorCleanup` → `safelyReplaceContentWithCkEditorData`) sets
`innerHTML` of the **whole text box**. Every paragraph, span and text node in the box is replaced
by a new object, including paragraphs the user did not touch, although the resulting HTML is
almost the same as before. One case, step "C text type sentence": the HTML before and after
differ only in the CKEditor bookmark span, which loses its marker attribute:

```html
before: ...The sun is very hot today.<span data-cke-bookmark="1" id="cke_bm_38C" style="display: none;">&nbsp;</span></span></p>
after:  ...The sun is very hot today.<span id="cke_bm_38C" style="display: none;">&nbsp;</span></span></p>
```

(the five `<p>` elements are removed and five new ones added). It ran after every typing step
with the tool open, and on opening the tool and on each page load.

**Observed, highlight markup.** No reader highlight markup was written to the DOM in this run.
On this branch the reader tools paint violations with `::highlight()` over live ranges instead of
spans (comment at `readers/readerHighlights.ts:9-18`, read as code). The test level had no
sentence-length limit, so no violation was shown either; whether highlights appear without DOM
writes was therefore **not observed**, only read in the code.

**Observed, smaller passes in the same timer.** `cleanUpNbsps` (`pageEditingMarkup.ts:221`)
rewrites the text inside CKEditor's bookmark span about 505 ms after typing; CKEditor adds and
removes its bookmark spans (`span[data-cke-bookmark]`) around these passes.

**What a filter that drops whole nodes could describe** (inferred from the above):

- Talking Book's spans cannot be described as nodes added around the user's text. The pass
  removes the very text node the user's typing changed and builds new ones, splitting the text at
  sentence boundaries. To a recorder these are child-list changes of the paragraph (the user's
  paragraph), not additions of separate nodes.
- The reader pass restructures the whole box even when nothing visible changes: every node in it
  is replaced. A recorder that holds node objects from the typing transaction holds detached ones after
  this pass.
- Both happen about half a second after typing stops, outside a 500 ms window after the last
  input.

### 1.4 Results: how often Bloom's own changes hit a recent user change

Net changes only; editing steps only (steps that leave or load a page are boundaries under
§4.0 and are counted separately). "Hit" means the change touched the same node and attribute,
the same text node or the same child list as a user change in the last 20 steps, or added or
removed nodes inside a text box the user edited in the last 20 steps. **Observed** (`net-out.txt`):

| Phase | Bloom's own net changes in editing steps | Of which hit a recent user change | Main writers of the hits |
|---|---|---|---|
| A, no tool | 42 | 13 | comicaljs rewriting `data-bubble` after a drag (5), canvas element classes (5), `cleanUpNbsps` in the bookmark span (2), CKEditor (1) |
| B, Talking Book | 38 | 34 | Talking Book (15, all in the paragraph just typed in), `cleanUpNbsps` (12), CKEditor bookmarks (7) |
| C, Leveled Reader | 90 | 82 | reader pass replacing the box's paragraphs (50), CKEditor bookmarks (16), `cleanUpNbsps` (14), `bloomEditing` (2) |

Page loads and leaving a page made a further 125 (A), 172 (B) and 250 (C) net changes, with 7, 6
and 55 hits. When leaving the canvas page, the save path changes the live DOM: `bloom-table`
removes `cell--selected` and `data-btable-anchor-name` from cells, comicaljs replaces its
`canvas` with an `svg`, and the active canvas element loses `bloom-focusedCanvasElement`.

**Writers seen with no trusted input within 500 ms, during editing:** Talking Book's markup pass,
the reader markup pass, `cleanUpNbsps`, CKEditor's bookmark handling, `OverflowChecker` refitting
after a column drag (all its writes were to copies it then removed), comicaljs and canvas element
code finishing a drag. On page load and save: `bloomEditing`, `CanvasElementManager`,
`bloom-table`, `tableEditing`, comicaljs, `BloomSourceBubbles`/qTip, `LocalizationManager`,
`StyleEditor`, CKEditor attaching, and parser insertions.

### 1.5 Results: removed nodes while detached

**Observed** (`detached.py` on `exp2-A.json`; `exp1` logs):

- **Canvas element deleted with Bloom's Delete.** One change while detached: the bubble's
  `.bloom-editable` `class` changed (CKEditor's focus class). Its CKEditor instance was still
  `ready` 4 s later.
- **Table row deleted with the row menu's Delete Row.** One change while detached: the cell's
  editable `class`. The cell's CKEditor instance stayed `ready`.
- **Duplicate.** `CanvasElementDuplication.duplicateCanvasElementFamily` sets `innerHTML` on the
  new element; a translation group it removed that way then received 17 changes while detached
  (`contenteditable`, `class`, `tabindex`, `spellcheck`, `role`, `aria-label`, `style`, child list),
  and a CKEditor instance reported `ready` on an editable inside it.
- **Text deleted** (select a word, Delete): the removed text nodes were not changed afterwards.

**Code read** (not the undo code): `CanvasElementManager.deleteCanvasElement`
(`CanvasElementManager.ts:3024`) calls `Comical.update`, `BloomSourceBubbles.removeSourceBubbles`,
`Comical.deleteBubbleFromFamily`, `refreshCanvasElementEditing`, `setActiveElement(undefined)`,
`removeDetachedTargets` and `normalizeCoverImageDesignation`. Nothing in `src/BloomBrowserUI/bookEdit`
calls CKEditor's `destroy()`; deleted elements keep their instances. `bloom-table`'s
`removeRowAt` runs `removeLineAt` inside `addHistoryEntry` (`bloom-table.mjs:7640`).

### 1.6 Code read: React rendering into the page

`renderRoot` call sites in `bookEdit` (grep, not exhaustive beyond that folder): the ones that
render inside the page frame render into `bloom-ui` containers, which the save path strips:
`AbovePageControls` (`.above-page-control-container.bloom-ui`), Talking Book's playback order
buttons (`div.bloom-ui`), `CanvasElementContextControls`, `TextContextMenu`, source bubbles. The
Games tool renders `DragActivityControls` in the toolbox frame. No React component was found that
renders saved page content; quiz and game page content is written by plain DOM code.

### 1.7 What it means for the finalists (APPROACHES §9.1)

Inferred:

- **For both finalists**, the half-second markup passes are the main source of untracked changes
  on the same values as the user's: with Talking Book or a reader tool open, 34 of 38 and 82 of 90
  of Bloom's own changes in editing steps hit what the user had just changed.
- **Recorded mutations** lose most of their advantage inside text boxes while either tool is open.
  The reader pass replaces every node of the box; the Talking Book pass replaces the nodes of the
  paragraph. A transaction recorded for typing holds node objects that are detached half a second
  later, so the "every node still has its after value" check (APPROACHES §4.4) fails for every
  typing transaction made before the pass, unless the pass is swept into the typing transaction or
  changed. The reader pass changes no visible content in the case above, so changing it to write
  only when the HTML differs (goal 6) looks cheap; the Talking Book pass changes structure, so it
  would have to be swept up or recorded as part of the transaction.
- **Differences between snapshots** compare text, so the reader pass is invisible to it (the HTML
  is nearly unchanged, apart from the bookmark attribute that the save path's rules would strip).
  The Talking Book pass is a real difference in the same part (the text box) as the typing.
- **A recorder needs its filter for volume, not only for correctness.** Most records are chrome
  outside the page, jQuery temporary ids, and `OverflowChecker`'s copies. All of these cancel out
  within one call or one task; a recorder that keeps first-before and last-after values per node
  and ignores nodes added and removed within a transaction would see none of them, as `net.py` does.
- **Detached nodes are not left untouched**, but in the cases seen only CKEditor's focus class
  changed. The duplicate case shows that code can wire up elements it has already thrown away.

---

## 2. Experiment 1: re-inserting the same node objects

**Question** (behind APPROACHES §4.4 and §9.1): after an element is removed and the same node
objects are put back, do CKEditor, `bloom-table` and comicaljs still work, and is the saved page
right? And the alternative: what breaks when the element is replaced by a clone parsed from
earlier HTML?

### 2.1 What was done

`drive\exp1.spec.ts` (two runs: `EXP1_MODE=reinsert`, `EXP1_MODE=clone`), `exp1c`, `exp1d`,
`exp1e`. Removal and re-insertion were plain DOM calls run in the page frame, for example:

```js
const parent = ce.parentElement, next = ce.nextSibling;
ce.remove();
await new Promise((r) => setTimeout(r, 1000));
parent.insertBefore(ce, next);
```

After each trial the script, through real clicks and keys: checked the CKEditor instance bound to
the element (`CKEDITOR.instances[n].element.$ === editable`, its `status`); clicked in the box and
typed a unique word; checked the word reached `editor.getData()`; double-clicked the word and read
the CKEditor toolbar's `display`; pressed Ctrl+B and counted `<strong>` (CKEditor's bold) against
`<b>` (the browser's own bold). For the bubble: clicked the page margin to deselect, dragged the
bubble from its middle, clicked to select it and compared the control frame's rectangle with the
element's, dragged the tail tip, took a screenshot. For tables: checked `data-table-attached`,
dragged a column boundary, pressed the "+" button, typed in a cell. At the end it went to another
page and back (a save and reload) and searched the saved `.htm` for the typed words and for
edit-time markup.

Trials:

| | Element | How it was taken out and put back |
|---|---|---|
| T1 | Text box (CKEditor) | its child nodes removed, the same nodes appended back |
| T2 | Text box | the `.bloom-editable` element itself removed and re-inserted |
| T4 | Speech bubble canvas element | removed and re-inserted with DOM calls |
| T5 | Speech bubble | deleted with Bloom's Delete command, then the same element object re-inserted |
| T5b | Same bubble | then `getTheOneCanvasElementManager().refreshCanvasElementEditing(canvas, undefined, true, false)` called, the kind of call an `onAfterUndoOrRedo` listener would make |
| T6, 1e(a) | Table row (its two `.bloom-cell` elements) | removed and re-inserted with DOM calls |
| T7, 1e(b) | Table row | deleted with Delete Row, then the same cells re-inserted and the table's attributes put back to their values before the delete |
| T8 | Table canvas element | removed and re-inserted with DOM calls |
| T3, T9, T10 | Text box, bubble, table canvas element | replaced by a clone parsed from HTML captured earlier (`template.innerHTML = html`) |

### 2.2 Results

**Observed**, re-inserting the same objects (`exp1-reinsert.run.log`, `exp1-reinsert2.run.log`,
`exp1e.json`, screenshots in `shots\`):

- **CKEditor (T1, T2, bubble text in T4, T5, T5b, cells in 1e).** The same instance stayed bound
  to the element and stayed `ready`, also while detached. After re-insertion, clicking gave the
  box focus, typed text reached `getData()`, the toolbar showed on selecting a word (`display:
  block`) for the text box and cells, and Ctrl+B produced CKEditor's `<strong>`. In the bubble
  the toolbar did not show on selection in any trial, including the untouched baseline, so that is
  how bubbles behave, not a result of re-insertion.
- **Speech bubble removed and re-inserted with DOM calls (T4).** Everything worked: drag moved it
  (`left: 320px; top: 330px` to `350px; 350px`), selection put the control frame on it, the tail
  drag changed `data-bubble` (`tipX` 321 to 346), typing reached CKEditor, and the screenshot shows
  the bubble outline and tail (`shots\T4-after-tail-drag.png`).
- **Speech bubble deleted with Bloom's Delete, then re-inserted (T5).** CKEditor worked, and
  clicking selected it with the control frame in place. But comicaljs no longer drew it: the
  screenshot shows the text box without the bubble outline or tail (`shots\T5-after-select.png`),
  dragging the unselected bubble did not move it (`style` unchanged), and the tail drag did
  nothing (`data-bubble` unchanged).
- **After `refreshCanvasElementEditing` (T5b).** Outline and tail were drawn again, the drag moved
  the bubble (`350px; 350px` to `380px; 370px`), and the tail drag worked (`tipX` 346 to 371).
- **Table cells removed and re-inserted with DOM calls (T6, 1e(a)), and the whole table canvas
  element (T8).** `data-table-attached` stayed `"1"` and was true: the column drag changed
  `data-column-widths`, "+" added a row, cells kept their CKEditor instances, typing reached
  CKEditor, Ctrl+B gave `<strong>`.
- **Table row deleted with Delete Row, then re-inserted (T7, 1e(b)).** After the cells were put
  back before the next row and the table's `data-row-heights`, `data-column-widths`, `style` and
  `class` set back, `bloom-table` laid the rows out again from the attributes (cell rectangles
  matched those before the delete), typing reached CKEditor in the re-inserted cell, the column
  drag and "+" worked, and after a save and reload the table had 4 rows, 8 cells and the typed
  text in the right cell (`exp1e.json`).
- **Saved page.** Every word typed into re-inserted elements was in the saved book (`Alpha`,
  `Bravo`, `Delta`, `BubT4`, `BubT5`, and the 1e words); the saved HTML had no `cke_editable`,
  `cke_bm` or `data-table-attached`.
- **Console.** No errors in the re-insertion runs apart from three "invoked update on an element
  that is not active" messages in 1e (source not traced).

**Observed**, replacing with a clone parsed from earlier HTML (`exp1-clone.run.log`):

- **Text box clone (T3).** The clone carries CKEditor's classes (`cke_editable cke_editable_inline
  cke_contents_ltr cke_focus`) and `contenteditable="true"`, but no CKEditor instance is bound to
  it; the old instance stays bound to the detached original. Typing works, through the browser
  alone. No toolbar. Ctrl+B produced the browser's `<b>`, not `<strong>`.
- **Bubble clone (T9).** Clicking did not select it; dragging did not move it; the tail drag did
  nothing; no CKEditor instance; typing worked through the browser and Ctrl+B gave `<b>`. The
  screenshot still shows a bubble outline, drawn by comicaljs for the old element.
- **Table clone (T10).** The clone carries `data-table-attached="1"`, so Bloom does not attach it.
  The column drag still worked (the library finds the table from the event), but "+" did not add
  a row, and no cell had a CKEditor instance.
- **Saved page.** Text typed into clones was saved; the copied CKEditor classes and
  `data-table-attached` were stripped by the save path.

**Not settled.** In the first runs, typing failed in cells whose rows were 20 px high. A control
click on a cell that had never been removed failed the same way (`exp1c`), so that failure came
from where the click landed, not from re-insertion; with taller rows (`exp1c`, `exp1d`, `exp1e`)
typing worked. The first table, after many trials, ended with 5 row heights for 8 cells, and
"+" then failed with `TableHistoryManager: ... snapshotCellSettings ... reading 'getAttribute'`.
Which trial caused the mismatch was not found; the clean repeat on a fresh table (1e) did not
produce it.

### 2.3 What it means for the finalists

Inferred:

- **Recorded mutations: whether re-inserting the same objects keeps them working (APPROACHES
  §4.4) is answered yes for CKEditor and `bloom-table`.** Re-inserting the same objects keeps CKEditor instances, the table's wiring and
  the elements' listeners, with nothing to re-run, as long as the attributes the library renders
  from are put back too (which a recorder of every attribute does).
- **comicaljs needs the notice after a restore** that APPROACHES §3.5 already plans
  (`onAfterUndoOrRedo` calling `refreshCanvasElementEditing` or `Comical.update` for the canvas). Without
  it, a bubble whose deletion went through Bloom's Delete is not drawn and cannot be dragged.
- **Differences between snapshots**, which put content back from text, would meet the clone
  results for every part that holds a CKEditor box, a table or a bubble: CKEditor's classes and
  `data-table-attached` come back looking wired when they are not, typing falls through to the
  browser's own editing (`<b>` instead of `<strong>`), bubbles cannot be selected, and "+" does
  not work. So the second finalist needs: markers such as `cke_*` classes and
  `data-table-attached` left out of the stored text; `attachToCkEditor` (or `editor.loadSnapshot`
  on the existing instance, for a text box's content) after each restore; table attach; and
  `SetupElements` for canvas elements.

---

## 3. Experiment 3: Ctrl+Z, Ctrl+Y and the browser's `beforeinput`

**Question** (behind APPROACHES case 7.7 and §8.2): in WebView2 with CKEditor attached, does Ctrl+Z / Ctrl+Y produce a
cancelable `beforeinput` with `inputType` `historyUndo` / `historyRedo` before CKEditor handles
the key? What is the event order, what happens if `beforeinput` is cancelled, and does IME
composition change it?

### 3.1 What was done

`drive\exp3.spec.ts`. Listeners on `window` in the page frame, in the capture phase (before any
listener on the element) and in the bubble phase (after the element's listeners, showing
`defaultPrevented`), for `keydown`, `keypress`, `keyup`, `beforeinput`, `input` and composition
events; plus CKEditor's `key`, `beforeCommandExec`, `afterCommandExec`, `saveSnapshot` and `change`
events on each instance. In each box: click, type a few words, then press the keys and record the
events and the text before and after. Boxes: the page's text box (CKEditor), and a plain
`contenteditable` div added outside `.bloom-page` with no CKEditor (removed at the end). Each was
tried as is and with a capture listener that calls `preventDefault()` on `beforeinput` whose
`inputType` starts with `history`. IME: `Input.imeSetComposition` ("k", "ka") then
`Input.insertText` ("か"), then Ctrl+Z; and Ctrl+Z while a composition was still open.

**Limit of this experiment.** CDP key events are delivered to the renderer directly. In real use
a key press could first pass through WebView2's accelerator handling in the browser process; the
`run-bloom` skill's `reference.md` says C# handles Ctrl+Z as an accelerator and that synthetic
Ctrl+Z over CDP does not reach it. Whether a real Ctrl+Z ever reaches the page was not tested here:
doing so needs OS-level input, which `--dont-disturb` and the developer's session rule out. Section
4 tested it later with real key presses: C# does not take Ctrl+Z, and the page gets it. Dead
keys could not be simulated over CDP either.

### 3.2 Results

**Observed** (`exp3.json`):

- **In the CKEditor box, there is no `beforeinput` at all.** Order for Ctrl+Z: `keydown` Control
  (capture), CKEditor `key`, `keydown` Control (bubble), `keydown` z (capture), CKEditor `key`,
  CKEditor `beforeCommandExec` `undo`, `afterCommandExec` `undo`, `keyup`. The bubble-phase
  `keydown` for z never arrives: the element's handler stops it. No `beforeinput` or `input` with
  `historyUndo` is fired. The text changed (" uno dos" removed).
- **Cancelling `beforeinput` has no effect in the CKEditor box**, because there is none to
  cancel: with the cancelling listener installed, Ctrl+Z still undid the typing through
  CKEditor's `undo` command.
- **Ctrl+Y and Ctrl+Shift+Z in the CKEditor box after an undo did nothing**: CKEditor received the
  `key` event, ran no command, the `keydown` did not reach the bubble phase, there was no
  `beforeinput`, and the text did not change.
- **In the plain `contenteditable`**, Ctrl+Z gave `keydown` z (capture and bubble, not prevented),
  then `beforeinput` `historyUndo` with `cancelable: true`, then `input` `historyUndo`; the whole
  run of typed text (" siete", 6 keystrokes at 40 ms) was undone in one step. Ctrl+Y and
  Ctrl+Shift+Z gave `beforeinput` `historyRedo`, cancelable, then `input`, and redid it.
- **Cancelling works in the plain box**: with `preventDefault()` on `beforeinput` `historyUndo`,
  the text did not change and no `input` followed.
- **`document.execCommand("undo")`** fires `input` with `historyUndo` (not cancelable) and no
  `beforeinput`.
- **IME.** Composition gives `compositionstart`, then for each update `compositionupdate`,
  `beforeinput` `insertCompositionText` (`isComposing` true, not cancelable) and `input`, then
  `compositionend`. Ctrl+Z afterwards, in the CKEditor box, went to CKEditor's `undo` as above
  and removed the composed character. Ctrl+Z while a composition was open arrived as `keydown`
  with `isComposing` true, and CKEditor still ran `undo`; CKEditor also handled Ctrl+Z when the
  plain box's composition was open, which shows focus had stayed with the CKEditor box in that
  trial (the plain box result for IME is therefore not valid).
- **Focus moved back.** On the first attempt to focus the plain box (trial C), focus stayed in or
  returned to the CKEditor box even after `.focus()`; the second attempt worked. Not traced.

### 3.3 What it means

Inferred:

- With CKEditor attached, cancelling `beforeinput` `historyUndo` (as Lexical does, §15) is not
  enough and not even reached: CKEditor takes Ctrl+Z in its `keydown` handler and stops it. A
  page-level handler must take Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z in the capture phase of `keydown`
  (or CKEditor's keystrokes for undo and redo must be removed, goal 6) to keep CKEditor's history
  out of the way.
- Once CKEditor no longer handles the key, the browser's own history does fire a cancelable
  `beforeinput`, so the page should cancel `historyUndo` and `historyRedo` as well, as APPROACHES
  §3.3 already says.
- Which process receives a real Ctrl+Z first, C# or the page, was left open by this experiment;
  it decides whether the page handler above is ever reached. Section 4 answered it: the page
  receives it, so the handler is reached.
- For both finalists alike; none of this favours one over the other.

---

## 4. Real Ctrl+Z routing

**Question** (APPROACHES §9.2 question 4, left open by experiment 3): when the user presses a real Ctrl+Z,
Ctrl+Y or Ctrl+Shift+Z in the Edit tab, does the key event reach the JavaScript of the `page`
frame, or does C# take it first (WinForms accelerator handling, WebView2's
`AcceleratorKeyPressed`, menu shortcuts)? And which frame gets it when focus is elsewhere?

### 4.1 Setup

- **Bloom.** The same build and launch as section 0 (commit `d846317156`, the e2e fixture, a new
  throwaway collection `ctrlz-experiment` in `%TEMP%`, front end from Vite on port 5173), except
  that `BLOOM_E2E_DONT_DISTURB=0` left out `--dont-disturb`, so Bloom's window could take the
  foreground. No subscription code and no `tables` experiment this time. The developer's books were
  not opened.
- **Test book.** Basic Book with a "Just Text" page holding "The cat sat on the mat." and a
  "Canvas" page with a speech bubble and an image placeholder.
- **Listeners** (`listeners.js`), installed over CDP in the root document, `page`, `toolbox` and
  `pageList` frames: on `window`, in the capture phase and in the bubble phase, for `keydown`,
  `keyup`, `beforeinput` and `input`, logging frame, key, `ctrlKey`, `shiftKey`, `isTrusted`,
  target, `document.activeElement`, `inputType`, `defaultPrevented` and time into an array in the
  root document, read back after each step.
- **Keys** (`keys.ps1`): `keybd_event` from PowerShell, after `SetForegroundWindow` on Bloom's main
  window. Before every key step the script checked that `GetForegroundWindow` was Bloom's main
  window; it was, for every step.
- **Clicks**: real mouse clicks (`SetCursorPos` and `mouse_event`) at a screen position computed from
  the `Chrome_RenderWidgetHostHWND` window's rectangle and the element's rectangle from CDP. One
  drag of the image on the canvas page was a CDP mouse drag, to give Ctrl+Z something to undo.

The cases used these files, which, like those of section 0, were working files in a temporary
folder and were not kept: `setup\host.spec.ts` (launch and test book), `setup\drive.mjs` (the
cases), `listeners.js`, `keys.ps1`, `focusinfo.ps1` (which window has keyboard focus), `il.ps1`
(reads the WebView2 WinForms control's handler), and the logs `text.json`, `canvas.json`,
`toolbox.json`, `pageList.json`, `cross.json`, with screenshots in `shots\`. The results below
are the record of these experiments.

### 4.2 A CDP click does not give the browser keyboard focus

**Observed.** In the first run the text box was clicked over CDP after Bloom had been brought to the
foreground. Bloom was the foreground window, the page frame's `document.hasFocus()` was true and
the text box was `document.activeElement`, but no real key press, not even plain letters, produced
any event in any frame, and nothing was typed. `GetGUIThreadInfo` (`focusinfo.ps1`) showed
keyboard focus on a WinForms window (the child of the main form whose title is the collection
name), not on the browser's window. After one real mouse click in the text box, the focus chain was
`Chrome_WidgetWin_1` (the msedgewebview2 process) inside `Chrome_WidgetWin_0` inside the WinForms
windows, and every key press after that arrived. All results below come after a real click.

**Inferred.** Tests that send real keys must click for real first. Why activating the window did
not put focus in the browser (`EditingView.ParentForm_Activated` calls `SelectBrowser`) was not
traced.

### 4.3 Results

**Observed.** Where the keys went, with focus placed by a real click:

| Focus | Keys | Frames with events | `beforeinput` | Visible effect |
|---|---|---|---|---|
| Text box (CKEditor), after typing "xyz" with real keys | Ctrl+Z | `page` only | none | "xyz" removed, in one step |
| Same, next | Ctrl+Y | `page` only | none | nothing |
| Same, after another Ctrl+Z | Ctrl+Shift+Z | `page` only | none | nothing |
| Image on the canvas page selected (`activeElement` is `body`), after dragging it | Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z | `page` only | none | nothing; the image stayed where the drag left it |
| Page margin clicked, nothing selected | Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z | `page` only | none | nothing |
| Toolbox ("More..." header clicked, which opened that section) | Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z | `toolbox` only | none | nothing |
| Empty area of the page list | Ctrl+Z, Ctrl+Y, Ctrl+Shift+Z | `pageList` only | none | nothing |
| Empty area of the top bar (root document) | Ctrl+Z, Ctrl+Y | root only | none | nothing |

Details:

- **Every key press reached the focused frame**, with `isTrusted` true, starting with `keydown`
  for Control (and Shift) in the capture phase, then `keydown` for z or y in the capture phase.
  No key press was missing from the log, and no frame other than the focused one logged anything.
- **In the CKEditor box** the `keydown` for z, y and Z did not reach the bubble phase on `window`
  (stopped by a listener on the element), and no `beforeinput` or `input` followed, as in
  experiment 3 with CDP keys. Plain letters typed for real gave `keydown` in both phases, then
  `beforeinput` and `input` with `insertText`.
- **Outside a text box** (canvas element selected, nothing selected, toolbox, page list, top bar)
  `keydown` for z and y reached the bubble phase with `defaultPrevented` false, and no
  `beforeinput` followed.
- **The canvas case shows routing only.** The drag had moved the image (`left: 200px; top: 430px`
  to `240px; 460px`), but `workspaceBundle.canUndo()` returned `"fail"` and calling
  `workspaceBundle.handleUndo()` did not move it back either, so this build had nothing to undo
  there.
- **Focus outside the page leaves undoable typing alone** (`cross.json`). After typing "abc" in
  the text box, `canUndo()` returned `"yes"`. Ctrl+Z with focus in the page list, then in the
  toolbox, then in the top bar, and Ctrl+Y in the top bar, each reached only that frame; the text
  stayed "The cat sat on the mat.abc" and `canUndo()` still returned `"yes"`.

### 4.4 C# code read

- `src/BloomExe` has no handler for `AcceleratorKeyPressed` (grep) and does not set
  `AreBrowserAcceleratorKeysEnabled`.
- The WinForms `WebView2` control handles `AcceleratorKeyPressed` itself. Its handler
  (`_coreWebView2Controller_AcceleratorKeyPressed` in `Microsoft.Web.WebView2.WinForms.dll`, read
  as the methods its IL calls, `il.ps1`) calls `Control.OnKeyDown` or `Control.OnKeyUp` on the
  control and copies `KeyEventArgs.Handled` to the event's `Handled`. So C# could only take the key
  through a `KeyDown` handler on the browser control. `BloomWebView2` overrides only `WndProc`, and
  the only `KeyDown` subscriber in `src/BloomExe` is `ReactDialog.Form_KeyDown`, which handles
  Escape.
- `Shell.ProcessCmdKey` (`Shell.cs:599`) raises `ControlKeyEvent` for Control combinations and
  always returns `base.ProcessCmdKey`. The only subscriber, `EditingView.HandleControlKeyEvent`
  (`EditingView.cs:272`), checks for Ctrl+N and does nothing.
- `UndoCommand` in C# only carries the Undo button's enabled state: its `Implementer` is empty
  (`WebView2Browser.cs:1223`) and `Enabled` comes from `workspaceBundle?.canUndo?.()`
  (`WebView2Browser.cs:1323`). No C# shortcut is bound to it.
- **Inferred.** Key presses typed into the browser go to the msedgewebview2 process's window, not
  through the WinForms message loop, so `Shell.ProcessCmdKey` is not reached for them; and if it
  were, it consumes nothing. Not observed: seeing this from the C# side needs logging that Bloom
  does not have, and adding it would change source. The `run-bloom` skill's `reference.md` says
  "Undo is a C# accelerator"; no C# code in this commit handles Ctrl+Z.

### 4.5 What it means

Inferred:

- **C# does not take Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z.** The page frame gets them as ordinary trusted
  `keydown` events, so the capture-phase `keydown` handler that section 3.3 calls for will be
  reached, ahead of CKEditor's own handler. Nothing has to change in C# for that.
- **The key goes only to the frame that has focus.** With focus in the toolbox, the page list or
  the top bar, the page frame never sees Ctrl+Z, and today nothing undoes, although the Undo button
  is enabled. If Ctrl+Z is to mean "undo" wherever focus is in the Edit tab, each of the four
  documents needs its own `keydown` listener that sends the key to the one undo stack, or the
  design accepts that the keys work only while the page has focus. A listener in the toolbox would
  also have to leave the browser's own undo alone inside text fields there.
- **For both finalists alike**; this is about where to listen, not about how changes are recorded.
- **Testing.** An automated test can reach this routing only with real OS input and a real click
  first (section 4.2); CDP key events skip it, as section 3.1 noted.

---

## 5. Summary for APPROACHES

- **Re-inserting the same objects** (APPROACHES §4.4, §9.1): yes for CKEditor and `bloom-table`, with no re-wiring;
  comicaljs needs `refreshCanvasElementEditing` after a restore when Bloom's Delete ran. Clones
  from HTML break CKEditor, bubble selection and dragging, and table "+", while carrying markers
  that say they are wired.
- **Untracked changes on recent values** (APPROACHES §3.1, §5.3): low with no tool open (13 of 42 of Bloom's own
  changes in editing steps, mostly comicaljs finishing a drag); high with Talking Book (34 of 38)
  or Leveled Reader (82 of 90), because their markup passes rewrite the text box or paragraph
  about half a second after typing stops, replacing the nodes the typing changed.
- **Ctrl+Z with CDP keys** (section 3; APPROACHES case 7.7): in a CKEditor box Ctrl+Z never produces `beforeinput`; CKEditor takes the key in
  `keydown`.
- **Real key routing** (section 4): C# does not take Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z; the focused
  frame gets them as trusted `keydown` events. Only the focused frame gets them, so with focus in
  the toolbox, page list or top bar the page never sees the key.
