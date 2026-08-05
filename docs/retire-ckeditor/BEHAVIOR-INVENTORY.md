# Behaviour inventory — what must still work after CKEditor is gone

Stage 0 of [PLAN.md](PLAN.md). This is the acceptance criteria for the whole project.

## What is and isn't listed here

Listed: behaviours **at risk from this project** — implemented by CKEditor, or implemented in code
we will move or rewrite. Each row cites where it lives today and, where the code says so, the ticket
that caused it.

Not listed: behaviours of `.bloom-editable` handling that CKEditor is not involved in and that we
are not moving. `BloomField.ManageField` is called from `SetupElements` regardless of CKEditor, so
its arrow-key, backspace and paragraph-maintenance behaviours (BL-786, BL-933, BL-952, BL-2274,
BL-7061, BL-16518 …) stay exactly where they are. They are not in scope and listing them would
dilute the rows that matter.

**A ticket number in the `Ticket` column means the code cites it**, not that we have read the
ticket. Where the code's account of a ticket looks wrong, the row says so.

## How to use it

- `Verify` says how each row gets checked: **unit** (vitest), **live** (running Bloom over CDP via
  the `run-bloom` skill), or **manual** (a human tester; feed these to the `add-test-ideas` skill).
- Rows marked **⚠ capture first** must have today's actual behaviour recorded in
  [PASTE-DROP-BASELINE.md](PASTE-DROP-BASELINE.md) *before* any code changes, because the row is a
  guarantee whose failure is silent and the config string alone doesn't tell us what really happens.
- Rows marked **✗ must NOT survive** are current behaviour we intend to *remove*. They are here so
  nobody faithfully reimplements a workaround for a problem that no longer exists.

---

## A. Text selection toolbar (CKEditor service 1)

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| A1 | Selecting text in a `.bloom-editable` shows a floating toolbar; deselecting hides it | `attachToCkEditor` `selectionCheck` | | live |
| A2 | Toolbar offers bold, italic, underline, superscript, text colour, remove-formatting, hyperlink — and **not** cut/copy/paste/undo/redo/anchor/strike/subscript/background-colour | `config.js` `removeButtons` | | manual |
| A3 | Toolbar is positioned above the text box, moved down if it would go off-screen | `attachToCkEditor` `selectionCheck` | | manual |
| A4 | Toolbar never appears for a field with `bloom-userCannotModifyStyles` (on the field or an ancestor up to `.marginBox`), but such a field is still editable and pasteable | `attachToCkEditor` `alwaysHideToolbar` | BL-14947 | live |
| A5 | No toolbar for a field whose computed `cursor` is `not-allowed` | `attachToCkEditor` early return, `bloomEditing.ts:1952` | | live |
| A6 | No flash of the toolbar during page load, nor when moving between two fields that both have selections | `hideAllCKEditors` body class | BL-12448 | manual |
| A7 | Colour-picker panel does not re-open by itself on every subsequent selection | `attachToCkEditor` hides `.cke_panel` | | manual |
| A8 | Toolbar button tooltips are localized | `localizeCkeditorTooltips` | | manual |
| A9 | Text colour choices come from Bloom's text palette | `getHexColorsForPalette(BloomPalette.Text)` | | manual |

**Deliberate improvement:** the new toolbar should use Bloom's existing `colorPickerDialog` instead
of CKEditor's colour panel (§4.5), which supersedes A7 and A9 rather than reproducing them.

## B. Formatting commands

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| B1 | Bold / italic / underline / superscript apply and un-apply over a selection, including one spanning several paragraphs | CKEditor `basicstyles`; `<strong>`/`<em>`/`<u>`/`<sup>` | | unit + live |
| B2 | Bold produces `<strong>`, italic `<em>`, underline `<u>`, superscript `<sup>` — the tags the talking-book code expects to see inside a sentence | `audioRecording.ts:3727-3730` | | unit |
| B3 | Text colour produces a **bare** `<span style="color:…">` with no class or id | `config.js` colorbutton | | unit |
| B4 | Ctrl+Space ("clear formatting") strips only `b,strong,i,em,u,sup,sub,font,span` | `config.js removeFormatTags`, `bloomEditing.ts:291-299` | | unit |
| B5 | Clear-formatting **preserves any `<span>` carrying a class or id** — audio segments (`audio-sentence`, `bloom-highlightSegment`) and `bloom-linebreak` | `attachToCkEditor` `addRemoveFormatFilter` | | unit |
| B6 | Clear-formatting does **not** strip `class`/`style`/`align`/`lang` from elements it keeps (e.g. paragraphs merely spanned by the selection) | `config.js removeFormatAttributes = ""` | | unit |
| B7 | F6 wraps the selection in `<sup>`; F7 → `<h1>`; F8 → `<h2>`; Ctrl+Alt+0 → `<p>`; Ctrl+Alt+1 → `<h1>`; Ctrl+Alt+2 → `<h2>` | `AddEditKeyHandlers` | | live |
| B8 | Ctrl+R / Ctrl+L / Ctrl+E right/left/centre justify | `AddEditKeyHandlers` | | live |
| B9 | Shift+Enter inserts `<span class="bloom-linebreak"></span>` plus a ZWNJ if needed, and leaves the caret **after** it | `BloomField.InsertLineBreak` | BL-3009 | unit + live |
| B10 | The caret is never left *inside* a `bloom-linebreak` span | `BloomField.EnsureCaretNotInsideLineBreakSpan`, on selection change | | unit |
| B11 | An **empty** `span.bloom-linebreak` survives round-tripping and is not stripped as an empty element | `CKEDITOR.dtd.$removeEmpty.span = 0` | BL-3009 | unit |
| B12 | Typing a URL then a space/enter turns it into a live link | `autolink` plugin | BL-6845 | live |
| B13 | `.bloom-editable` divs get `spellcheck="false"` — no red squiggles | `config.disableNativeSpellChecker` | BL-12205 | live |
| B14 | Hyperlink button opens the link-target chooser and wraps the selection in `<a href>`; failure on a complex selection shows the `EditTab.HyperlinkPasteFailure` message | `BloomField` `setupHyperlink` command | | manual |

**Decision (§10 q2):** B14's dialog and flow are kept exactly as-is.

## C. Paste and drop filtering — the silent-failure guarantee

**⚠ Every row in this section must be captured first** (§4.8). The permitted vocabulary today is
`p br em i strong sup u; b{font-weight}; a[!href]; span{font-variant,color}`
(`config.js:119-120`); everything else, attributes included, is dropped.

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| C1 | Pasting a **table** does not produce a table | `pasteFilter` | | ⚠ capture, then unit + live |
| C2 | Pasting nested **`<div>`s** does not produce divs — importantly, a div copied from another Bloom book must not arrive with its **id**, which would duplicate an id in this book | `pasteFilter` | BL-3899 | ⚠ capture, then unit + live |
| C3 | Pasting an **`<iframe>`**, `<script>`, `<style>`, `<object>` or `<embed>` produces none of them | `pasteFilter` | | ⚠ capture, then unit |
| C4 | Pasting an **`<img>`** does not embed the image in the text | `pasteFilter` | | ⚠ capture, then live |
| C5 | Pasting arbitrary styled `<span>` soup from a real web page keeps only `font-variant` and `color` | `pasteFilter` | BL-4775, BL-12357 | ⚠ capture, then unit |
| C6 | Pasting `<a href>` keeps the link; other attributes on it are dropped | `pasteFilter` `a[!href]` | | ⚠ capture, then unit |
| C7 | **Dropping** any of C1–C6 from outside Bloom is filtered the same way as pasting | CKEditor's clipboard plugin routes `drop` through the same filter, `ckeditor.js:622` | | ⚠ capture, then live |
| C8 | Dragging a canvas element from the toolbox onto a page still works (Bloom's own internal drag, custom `text/x-bloom-canvas-element` type) | `CanvasElementManager.ts:2069-2088` | BL-7958 (Linux) | live |
| C9 | Pasting **plain** text with several lines produces several paragraphs | `reconstituteParagraphsOnPlainTextPaste` | BL-9961 | unit |
| C10 | Bloom's own code can still write markup the paste filter would reject (audio spans with ids, `bloom-linebreak`, canvas elements) — the filter applies **only** at the clipboard/drop boundary | `config.allowedContent = true` alongside a restrictive `pasteFilter` | BL-3899 / BL-3976 (the first fix filtered everything and broke this) | unit |

## D. Paste transforms

These move to the new `pasteHandler.ts` essentially unchanged (§6 Stage 3), except D8.

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| D1 | `<b>`/`<i>` from other sources become `<strong>`/`<em>`; a Google-Docs `<b style="font-weight:normal">` does **not** paste as bold | `BloomField.fixPasteData` | BL-8711 | unit |
| D2 | Attribute-less `<span>` elements from Word are removed (they broke aeneas audio splitting) | `BloomField.removeUselessSpanMarkup` | BL-12861 | unit |
| D3 | `\v 12` (Standard Format verse markers) become superscripts | `BloomField.convertStandardFormatVerseMarkersToSuperscript` | | unit |
| D4 | `bloom-linebreak` spans in pasted HTML are normalized, and any content wrongly inside one is moved out | `BloomField.normalizeBloomLineBreakSpans`, `EditableDivUtils.normalizeBloomLineBreakSpansInElement` | | unit |
| D5 | Pasting into a **Sentence**-mode recording div copies the audio files under **new** guid ids, so ids are not duplicated | `BloomField.copyAudioFilesWithNewIdsDuringPasting` | | live |
| D6 | Pasting into a **TextBox**-mode recording div strips all audio span markup | `BloomField.removeAudioSpanMarkupDuringPasting` | | unit |
| D7 | The first pasted `<p>` is unwrapped so the paste joins the current paragraph rather than starting a new one; any placeholder paragraph used to force a following break is removed afterwards | `BloomField` paste / `afterPaste` `.removeMe` | | unit + live |
| D8 | Small caps and text colour survive a copy-paste **within** Bloom | `BloomField.restoreHtmlMarkupIfNecessary` | BL-12357 | ⚠ see below |

**D8 cannot be ported as-is.** It works by detecting CKEditor-internal copies via
`dataTransfer.getData("cke/id")` and compensating for CKEditor's *own* stripping of spans from
`dataValue`. With CKEditor gone, nothing stamps `cke/id` and nothing strips the spans, so the
transform is meaningless as written — and the problem may not exist at all once `pasteHandler.ts`
reads raw `clipboardData`. **Verify against the BL-12357 repro; do not port.**

## E. Clipboard: cut, copy, paste plumbing

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| E1 | The toolbar Copy button copies the selection; with no selection it copies the active canvas element's whole text, trimmed of trailing whitespace | `bloomEditing.copyImpl` | BL-14051 | live |
| E2 | The toolbar Cut button removes the selection and it is undoable as **one** step | `cutSelectionImpl` via `undoManager.save/lock` | | live |
| E3 | The toolbar Paste button inserts at the selection, as one undo step | `pasteImpl` | | live |
| E4 | Pasting onto a canvas element that is *selected but not being text-edited* replaces its **entire** content, then updates the element's auto-height and reschedules toolbox markup | `pasteImpl` canvas branch | BL-14004 | live |
| E5 | Ctrl+V with an image on the clipboard pastes the image onto the canvas rather than into the text | `pasteHandler` + `editView/paste` API | BL-15123 | manual |
| E6 | Paste into an `<input>`/`<textarea>` (e.g. a canvas-element label) uses default browser behaviour | `pasteHandler` early return | | live |
| E7 | Cut/copy/paste **buttons' enabled state** tracks whether there is a selection and what is on the clipboard | `WebView2Browser.UpdateEditButtonsAsync` | | live |

**Scope (§10 q3):** clipboard work here is a **seam only**. The new `clipboard.ts` must produce both
`text/html` and `text/plain` and keep the write behind one interface, but still writes from JS — so
the BL-16459 data-loss-on-cut problem is *not* fixed here. See §4.9 and PR #8140 before touching it.

**✗ Must NOT survive:** the duplicate Ctrl+V `keydown` handler at `bloomEditing.ts:1764-1770`,
which exists only because CKEditor swallows `paste` inside editables. Once it doesn't, the
document-level `paste` listener fires normally. Verify, then delete.

## F. Getting clean HTML out of an editable

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| F1 | Saved HTML contains no zero-width-space "filling char" (U+200B) | `getData()` + `removeCkEditorFillingChars` | BL-12391, BL-16490 | unit |
| F2 | U+200C (ZWNJ) and U+200D (ZWJ) **are preserved** — they are legitimate in some scripts, and `InsertLineBreak` deliberately inserts a ZWNJ | `removeCkEditorFillingChars` removes only U+200B | | unit |
| F3 | Saved HTML contains no `cke_*` classes, `cke_bm_*` bookmark spans, `data-cke-saved-href`, or injected HTML comments | JS + C# scrubbing | BL-4775, BL-16065 | unit (C#) |
| F4 | A paragraph containing only a `<br>` is not silently turned into one containing `&nbsp;` | `fixUpEmptyishParagraphs` | | unit |
| F5 | Reader-tools markup rewriting an editable's `innerHTML` does not leave an orphaned filling char behind that corrupts word matching | `doCkEditorCleanup` strips before comparing | BL-16490 | unit |

**✗ Should become unnecessary:** F1, F3, F4 and F5 are all artifacts of CKEditor. After Stage 5 the
"clean HTML" of an editable should be `innerHTML` plus a minimal normalizer. **The C#-side scrubbers
stay** (as `LegacyCkEditorCleanup`) because books already on disk contain these artifacts — that is a
different requirement from the browser producing them.

## G. Selection survival across DOM rewrites

The reason CKEditor bookmarks exist. Being replaced by offset-based anchors (§4.3).

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| G1 | Typing in a box with a reader tool active leaves the caret where the user put it, though the markup around it was rewritten | `createBookmarks`/`selectBookmarks` in `toolbox.ts` | BL-3900 | live |
| G2 | With an **async** `updateMarkupAsync` tool, keystrokes arriving during the await do not go to a wrong position | double bookmark save/restore | BL-10133 | live |
| G3 | Longpress (the character map) is not broken by markup updates, and its replacement of the preceding letter still works | keydown-not-keypress; `isLongPressEvaluating` guard | BL-3900, BL-5215 | manual |
| G4 | Caret survives the reader-tools `innerHTML` rewrite | `readerToolsModel` caret offset + `makeSelectionIn` | | unit |
| G5 | Caret position is restored correctly around `<br>` elements | `makeSelectionIn`'s `divBrCount` | | unit |
| G6 | **Reader violation highlights survive typing** — over-long sentences/words and non-decodable words stay highlighted as you type, rather than flashing off at every pause | `textHighlightManager.ts` / `readerHighlights.ts`, `::highlight()` over live Ranges; `editMode.less:1074-1100` | BL-16558 | live (see note) |
| G7 | **The Talking Book current-sentence highlight survives the same way** | `audioTextHighlightManager`, `editMode.less:1145` | BL-16558 | live |

**G6/G7 are new since this inventory was first written, and they change G1's premise.** Reader
markup no longer rewrites the DOM to show violations — it paints `::highlight()` pseudo-elements
over **live `Range` objects**. So:

- The old worry "markup rewrites the DOM around the caret" is now *less* true for reader tools, and
  the new worry is the reverse: **anything that rebuilds an editable's text nodes collapses those
  Ranges and the highlights silently vanish.** That is what BL-16558 fixed by making `cleanUpNbsps`
  write `innerHTML` only when it actually changed something, and by moving `updateMarkup()` to after
  it.
- This binds our work directly: a Tier 1 undo restores `editable.innerHTML` (PLAN §4.11), which will
  collapse them. The restore must repaint. **The failure mode is silent** — no error, undo looks
  fine, the highlights are just gone.
- `toolbox.ts` says outright that the ordering here is not unit-testable and must be checked by
  "*typing in a Leveled Reader book and watching the over-long sentences stay highlighted*". Treat
  that as the acceptance test for G6.

**✗ Must NOT survive — a bug to fix, not preserve:** the bookmark approach makes the markup routine
see `"hous"`-marker-`"e"` while the user fixes a letter mid-word, so reader markup is briefly wrong
(`toolbox.ts:1521-1528` documents this against itself). An offset-based anchor doesn't perturb the
DOM, so **G1 should get strictly better**.

## H. Undo and Redo

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| H1 | The Undo button is enabled exactly when something can be undone | `workspaceBundle.canUndo()` polled from C# | | live |
| H2 | `canUndo()` stays cheap — C# polls it on a timer; expensive work makes the button flicker | `WebView2Browser.cs:963-996` | | unit |
| H3 | Undo reverses text edits, coalesced into sensible units rather than one per keystroke | CKEditor `undoManager` | | live |
| H4 | Undo reverses a cut and a paste as single steps | `undoManager.save/lock` | | live |
| H5 | Undo reverses origami (Change Layout) changes, and **Ctrl+Y redoes them** | `origamiUndo`/`origamiRedo`, own `keydown.origami` | | live |
| H6 | Undo reverses an image replacement, restoring src, copyright, creator, licence and crop | `ImageUndoManager` | BL-16330 | live |
| H7 | With a reader tool active, Undo restores the *pre-markup* text rather than re-applying stale markup — i.e. reader-tools undo takes precedence over CKEditor's | `handleUndo` order; `shouldHandleUndo` | | live |
| H8 | Undoing an image operation is offered only when the active element is an image container | `canUndoImageOperation` | | unit |
| H9 | Undo state is dropped when the page changes | `clearImageOperationUndoOnPageChange` | | unit |

**New behaviour this project adds** (not regressions to guard, but acceptance criteria):

| # | New behaviour | Plan |
|---|---|---|
| H10 | One ordered stack: operations undo in the order performed, across text, images, layout and structure | §4.1, Stage 1 |
| H11 | **Deleting a canvas element is undoable** | Stage 2b |
| H12 | **Deleting a page is undoable**, restoring it at its original index with correct page numbering | Stage 2a |
| H13 | Ctrl+Z / Ctrl+Y work anywhere in the page, not only in layout mode | Stage 4 |
| H14 | Redo works for everything undoable that has a `redo` (entries without one act as a redo floor) | §4.1, §10 q1 |
| H15 | The browser's own undo can never diverge from Bloom's stack | §4.4 `beforeinput` fence |
| H16 | Undo of typing is fast — no page reload, no C# round-trip | §4.11 Tier 1 |
| H17 | One undo step per user gesture — no operation records two entries | §4.13 nesting |

## I. Startup: things that must still work once the async races are gone

These rows exist because Stage 5 **deletes** the workarounds. The behaviour must survive their
removal.

| # | Behaviour | Workaround being deleted | Ticket | Verify |
|---|---|---|---|---|
| I1 | The format gear ("cog") appears on the focused text box and is not eaten during page setup | `StyleEditor.AttachToBox`'s `instanceReady` wait | | live |
| I2 | A tool's saved state is applied to a freshly loaded page and is not overwritten by editor init | `doWhenCkEditorReady` | BL-12381 | live |
| I3 | Placeholder text set during page load survives | `PlaceholderProvider`'s `instanceReady` re-set | | live |
| I4 | Game prompt text set programmatically is not reverted | `GamePromptDialog.tsx:422-427` ordering | | live |
| I5 | Longpress works on editables after page setup | post-init `activateLongPressFor` re-attach | | live |
| I6 | Talking-book highlight does not vanish or flash during page setup | `setHighlightSession` counter | BL-15300; **originally BL-6681 itself** | live |
| I7 | Off-screen page processing (`external/process-book`) still works — it runs with CKEditor absent today, which is why the flag-on path is already viable | `BookProcessor` strips the script tag | | unit (C#) |

**On I6:** the race that opened BL-6681 — CKEditor asynchronously resetting a `.bloom-editable` and
clobbering the highlight class — is **already gone**, because the highlight moved out of the DOM into
`AudioTextHighlightManager`. Do not go looking for it. `setHighlightSession` guards a different thing
("*newPageReady fires twice*") that may not be CKEditor's fault at all: **measure before deleting**.

## J. Fields that deliberately get no rich-text editor

| # | Behaviour | Today | Verify |
|---|---|---|---|
| J1 | Only fields matching `ckeditableSelector` get an editor: `bloom-content1/2/3`, `bloom-contentNational1/2`, and **`Equation-style`** — all requiring `contenteditable="true"` | `utils/shared.ts:16-19` | unit |
| J2 | A field whose computed cursor is `not-allowed` gets no editor | `attachToCkEditor` early return | live |

**✗ Do not port:** `bootstrap()`'s BL-3125 guard skipping fields containing a `.bloom-canvas`
(`bloomEditing.ts:1216`) is **dead code** — `this` is `undefined` in a strict-mode module function,
so `$(this).find(...)` is always empty, and canvas-element editables do get an editor via
`CanvasElementManager.addEventsToFocusableElements`. Also note `toolbox.ts:1530-1537`'s claim that
ArithmeticTemplate boxes get no editor is **false**; see J1.

## K. Focus, bubbles and incidental wiring

| # | Behaviour | Today | Ticket | Verify |
|---|---|---|---|---|
| K1 | The focused field's hint tooltip sits above Source Bubbles; on blur it drops back | `WireToCKEditor` focus/blur qtip class juggling | BL-11745 | manual |
| K2 | If the user deletes all text in a `bloom-copyFromOtherLanguageIfNecessary` field, `data-user-deleted="true"` is set so text is not copied back in later | `WireToCKEditor` `change` handler | BL-13779 | live |
| K3 | Every editable ends up containing at least one `<p>` | `BloomField.EnsureParagraphsPresent` | BL-6721 | unit |
| K4 | Elements marked `bloom-preventRemoval` inside an editable cannot be removed by Ctrl+A Del | `PreventRemovalOfSomeElements` via `execCommand("undo")` | | live |

**K4 needs a new implementation, not a port.** It currently *allows* the deletion and then calls
`document.execCommand("undo")` to reverse it — using the very browser undo stack we are fencing off
(§4.4). Replace with a `beforeinput` guard: reject any `delete*` whose `getTargetRanges()` covers a
`.bloom-preventRemoval` element.

---

## Cross-cutting acceptance criteria

| # | Criterion |
|---|---|
| X1 | Everything above holds with the flag **off** (CKEditor present) and **on** (CKEditor not even loaded) |
| X2 | `pnpm test`, `pnpm lint`, `pnpm typecheck` and `build/agent-vite.sh` green in both states |
| X3 | The C# suite green via `build/agent-dotnet.sh` |
| X4 | No listener leak: after N page setup/teardown cycles the page's listener count is unchanged (CDP `DOMDebugger.getEventListeners`, §4.10). **This fails today** |
| X5 | Page load is no slower than the Stage 0 baseline, and expected to be faster |
| X6 | Books saved by the new path open correctly in the previous Bloom version's editor (no new markup it can't handle) |
| X7 | Soak: a talking-book book, a decodable/leveled reader book, a drag-activity book, an RTL book, and a book with an image embedded in a text field |
