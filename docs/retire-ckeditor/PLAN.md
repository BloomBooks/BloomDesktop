# Retiring CKEditor from Bloom's edit mode (BL-6681)

**Status:** plan, reviewed once (§10 records what that review changed), BL-6681's own content
folded in (§11). Live state in [PROGRESS.md](PROGRESS.md).

## 1. Goals

1. **Remove CKEditor 4** (a 2015-era, hand-patched, 1.5 MB vendored copy) from Bloom's edit
   mode, replacing it with our own code. No replacement library.
2. **Give Bloom one consistent Undo stack.** Today there are five poorly-coordinated undo
   mechanisms. We want a single, ordered stack covering changes to the current page, **as it is
   currently loaded**: a page change or a same-page reload starts it empty (§4.2, §10 decision 7).
   Priority is on operations that are *hard to reverse by hand* (delete a canvas element) over
   ones that are easy (add a canvas element — just delete it).
3. **Simplify page loading and toolbox init**, most of whose complexity exists only to work
   around CKEditor mutating the DOM asynchronously during startup.
4. Do it in a way that survives **many rebases** over a long calendar period.

Non-goals (out of scope, but the design must not obstruct them): widening undo beyond the current
page, including undoing a page deletion; undo that survives a reload of the same page (§4.2 says
what it would take); undo across a Bloom restart; a rich-text editor usable outside Bloom's edit mode; the C#
multi-format clipboard write that would close BL-16459 (§10 q3). **Redo is in scope** — Ctrl+Y only,
no toolbar button (§10 q1).

### Target environment

Edit mode runs only in WebView2 (currently minimum 112, `WebView2Browser.kMinimumWebView2Version`).
Raising that minimum is permitted but should stay on **standard** platform APIs, because Bloom
is expected to be ported to a Mac browser component later. Everything this plan needs
(`beforeinput` / `InputEvent.getTargetRanges`, `Range`, `Selection`,
`ClipboardEvent.clipboardData`, `MutationObserver`) is Chrome 60-era or older, so **no version
bump is required**. Recorded here so nobody spends the budget.

## 2. What CKEditor is doing for us today

Bloom does not use CKEditor as a document editor. The `.bloom-editable` divs arrive from C#
already `contenteditable="true"`; the browser does the typing. `CKEDITOR.inline(element)` is
attached (`bloomEditing.ts` `attachToCkEditor`, `BloomField.WireToCKEditor`) for these
services:

| # | Service | Where it's consumed |
| --- | --- | --- |
| 1 | Floating selection toolbar: bold / italic / underline / superscript / text colour / remove-format / hyperlink | `attachToCkEditor` `selectionCheck`, `localizeCkeditorTooltips`, `editMode.less` `.cke_float`, `hideAllCKEditors` (BL-12448) |
| 2 | A text-edit **undo stack** we drive from our own Undo button | `editablePage.ts` `ckeditorCanUndo`/`ckeditorUndo`, `workspaceRoot.handleUndo` |
| 3 | **Paste/drop content filtering** — a strict **allow-list** (`config.pasteFilter`) that keeps users from introducing HTML Bloom's own UI could never create. See §4.8; this is a safety guarantee, not a nicety. | `config.js:119-120`; CKEditor's clipboard plugin also routes **drop** through it |
| 3b | **Paste transforms** — the `paste`/`afterPaste` events themselves | `BloomField.WireToCKEditor` (SFM verse markers, audio-span id regeneration, small-caps preservation, first-`<p>` unwrapping), `bloomEditing.pasteImpl` |
| 4 | `getData()` — "clean" HTML, notably without the ZWSP *filling char* | `EditableDivUtils.doCkEditorCleanup`, `audioRecording.cleanUpCkEditorHtml` |
| 5 | **Bookmarks** (`createBookmarks(true)`) for selection save/restore across DOM rewrites | `toolbox.ts` keystroke pipeline, `readerToolsModel.doMarkup`, `EditableDivUtils.restoreSelectionFromCkEditorBookmarks` |
| 6 | `key` event, to intercept Shift+Enter | `BloomField.WireToCKEditor` → `InsertLineBreak` |
| 7 | `insertText`; `undoManager.lock/save` for atomic multi-step edits | `bloomEditing.cutSelectionImpl`, `pasteImpl` |
| 8 | `change` event, used for the BL-13779 `data-user-deleted` tracking on `bloom-copyFromOtherLanguageIfNecessary` fields | `BloomField.ts:244-252` |
| 9 | `focus`/`blur`, used to juggle qtip z-order between hint tooltips and Source Bubbles (BL-11745) | `BloomField.ts:345-366` |
| 10 | `selectionChange` → `EnsureCaretNotInsideLineBreakSpan` | `BloomField.ts:259-261` |
| 11 | `addCommand`/`ui.addButton` for the **SetupLink** hyperlink button | `BloomField.ts:368-415` |
| 12 | Odds and ends: `autolink` plugin (BL-6845); `disableNativeSpellChecker` → `spellcheck="false"` (BL-12205); `CKEDITOR.dtd.$removeEmpty.span = 0` so empty `span.bloom-linebreak` survives (BL-3009); `removeFormat` with a filter protecting structural spans; colour-palette caching | `config.js`, `attachToCkEditor` |

And one thing it does **to** us rather than for us, which belongs in the same table because the
new editor must decide what to do instead:

| # | Interference | Evidence |
| --- | --- | --- |
| 13 | CKEditor `preventDefault()`s `copy` and `cut` inside a `.bloom-editable` and swallows `paste`, so Bloom's document-level `paste` listener never fires there. Bloom compensates with a **duplicate Ctrl+V `keydown` handler** whose comment says so outright. | `bloomEditing.ts:1752-1770` ("*The pasteHandler does not get invoked … probably because some CkEditor code intercepts it and prevents default*"); recorded again on BL-6681 from the BL-16459 investigation |

### What it costs us

**Startup-race workarounds** — all of these exist *only* because CKEditor mutates the DOM
asynchronously after `CKEDITOR.inline()` returns:
- `toolbox.ts` `doWhenCkEditorReady` / `doWhenCkEditorReadyCore` (~65 lines, with its own
  listener-remover bookkeeping, BL-12381).
- `StyleEditor.AttachToBox`'s `instanceReady` dance (~35 lines) — the format-gear icon must
  wait because CKEditor replaces an empty div's content with `<p><br></p>` and eats the icon.
- `PlaceholderProvider.ts` re-applies placeholder text on `instanceReady`.
- `GamePromptDialog.tsx:422-427` must finish setting content *before*
  `refreshCanvasElementEditing`, "because by the end of the process, the text gets set back to
  what it was".
- `bloomEditing.bootstrap` re-runs `activateLongPressFor` after wiring editors because
  "CKEditor initialization can replace editable nodes".
- `CommonApi.cs:110` — a GET fires during page init "probably hooking up CkEditor, an
  unwanted...".

**Artifact scrubbing**
- ZWSP filling char: `EditableDivUtils.removeCkEditorFillingChars` (BL-12391, BL-16490),
  `PublishHelper.cs:382`.
- `cke_bm_*` bookmark spans: `EditableDivUtils.isNodeCkEditorBookmark` /
  `fixUpEmptyishParagraphs` / `safelyReplaceContentWithCkEditorData`,
  `toolbox.ts setCkeditorBookmarkContent` + `cleanUpNbsps`, `jquery.text-markup.ts` `ckeRegex`,
  `BookData.IsCkEditorBookmarkSpan` + `NormalizeEditableInnerXml` (BL-16065).
- `cke_*` classes in saved HTML: `HtmlDom.RemoveCkEditorMarkup`.
- `data-cke-saved-href`: `HtmlDom.CleanupAnchorElements`.
- `<br>` before `</p>`: `XmlHtmlConverter.cs` regex (BL-2557).
- HTML comments injected on paste: `toolbox.removeCommentsFromEditableHtml` (BL-4775).
- Caret normalization suspected in TBT fragment splitting (`Book.cs:1334`).
- `BookProcessor.cs:179-182` strips the `<script>` tag so off-screen page processing works.
- `BloomServer.cs:1041` special-cases `ckeditor/skins/flat/icons`; `ProjectContext.cs:597`
  registers the skin folder.

### Two pieces of dead or misleading code — do not port

- **The BL-3125 `.bloom-canvas` guard is dead.** `bloomEditing.ts:1216`'s
  `if ($(this).find(".bloom-canvas").length) return;` never fires: `bootstrap` is a
  strict-mode module function called as `bootstrap()`, so `this` is `undefined` and
  `$(undefined).find(...)` is empty. Canvas-element editables *do* get CKEditor, via
  `CanvasElementManager.addEventsToFocusableElements` (`CanvasElementManager.ts:951`). Verify
  and delete.
- **`toolbox.ts:1530-1537`'s comment is stale.** It claims ArithmeticTemplate / numeric boxes
  get no editor "because the logic that invokes WireToCKEditor is looking for classes like
  bloom-content1". Not so: `utils/shared.ts:16-19` explicitly includes
  `.Equation-style[contenteditable='true']` in `ckeditableSelector`, added for that very
  template. The **real** "no editor" case is `attachToCkEditor`'s early return for elements
  with `cursor: not-allowed` (`bloomEditing.ts:1952`). Preserve that; don't preserve a
  behaviour that doesn't exist.

## 3. Current Undo: five mechanisms

`workspaceRoot.handleUndo()` / `canUndo()` consult them in a fixed order:

| Mechanism | What it really is | Notes |
| --- | --- | --- |
| `origamiCanUndo`/`origamiUndo` (`origami.ts:277-294`) | A stack of **jQuery `clone(true)` copies of `.marginBox`** — DOM plus attached handlers and data — restored with `replaceWith` | Only while Change Layout mode is active. Has its **own** `keydown.origami` Ctrl+Z/Ctrl+Y handler on `html` (`origami.ts:137`), and its own Redo. Safe today partly *because* layout mode strips `contentEditable` (`origami.ts:132`), so there are no live CKEditor instances to orphan. |
| `toolboxWindow.canUndo/undo` → `readerToolsModel` | A per-editable **text-typing** undo: `{html, text, caretOffset}` snapshots, seeded on focus (`noteFocus`, :557-568, from `decodableReaderTool.tsx:155`) and pushed on every markup-changing keystroke inside `doMarkup` (:753-764) | Gated on `shouldHandleUndo()` — `currentMarkupType !== None` (:570). It is consulted *before* CKEditor **deliberately**: when a reader tool is active it must shadow CKEditor's undo, which would restore stale decodable/leveled markup. Not "reader-setup changes". |
| `imageOperationCanUndo`/`imageOperationUndo` (`ImageUndoManager.ts`) | Restores an image's `src` / copyright / crop | Clean two-phase prepare/commit; already page-id-scoped; gated on the active element being an image container. |
| `ckeditorCanUndo`/`ckeditorUndo` | `CKEDITOR.currentInstance.undoManager`, **per editable div** | An "implementation secret". Ordering across boxes is already wrong. |
| Browser-native undo | Invisible | Called directly in `BloomField.PreventRemovalOfSomeElements` (`BloomField.ts:810-825`); also fed implicitly by every `document.execCommand("insertHTML"/"formatBlock"/"justify*"/"insertText")` in `bloomEditing.ts` and `GamePromptDialog.tsx`, and by plain typing in any contenteditable. |

**Correction, verified 2026-08-06 — the table above is the *button* path, not the keyboard path.**
`handleUndo()` has exactly one caller: `topBarButtonClick` (`bloomEditing.ts:1633-1648`), reached
when the user clicks the toolbar Undo button. There is **no Ctrl+Z handler anywhere in the workspace
frame**, and C#'s `UndoCommand.Implementer` is an empty lambda (`WebView2Browser.cs:890`) that exists
only so the button's `Enabled` can be set. So Ctrl+Z is handled entirely in the **page** frame, by
whichever of these claims it first:

| Ctrl+Z handler | Where | When it wins |
| --- | --- | --- |
| `keydown.origami` on `html` | page frame (`origami.ts:137`) | Change Layout mode only |
| per-editable `keydown` in the reader tools | page frame (`decodableReaderTool.tsx:158-178`) | any editable, whenever `currentMarkupType !== None`; `preventDefault`s and returns false |
| CKEditor's own keystroke handling | inside each editable | otherwise |
| browser-native contenteditable undo | — | when nothing above claims it |

Two consequences the plan depended on and got half right. First, the deliberate
reader-tools-before-CKEditor precedence is enforced for the keyboard by that `preventDefault`, not by
`handleUndo`'s ordering — so with a reader tool active, Ctrl+Z in a text box never reaches the shared
stack at all. Second, that is *why* Stage 1 is behaviour-neutral: it changes only the button path.
The keyboard path is not unified until those page-frame handlers are converted (Stages 3–4), and
until then a single consistent Undo exists for the button but not for the keystroke.

Two further corrections to the folklore:
- `workspaceRoot.ts:125`'s "*See also Browser.Undo; if all else fails we ask the C# browser
  object to Undo*" is **stale** — no such fallback exists in the WebView2 code. The Undo
  button's enabled state comes purely from `workspaceBundle.canUndo()` returning `"yes"`
  (`WebView2Browser.CanUndoAsync`, polled on a timer from `UpdateEditButtonsAsync`).
- `config.undoStackSize = 0` in `config.js` claims to prevent a crash; CKEditor 4 reads
  `config.undoStackSize || 20`, so it silently means 20. Don't preserve the intent blindly.

**Not undoable at all today:** deleting a canvas element, deleting a page, style changes, most
toolbox operations — i.e. precisely the hard-to-reverse things.

## 4. Design decisions

### 4.1 Undo entries: snapshots by default, checked before they undo

Two architectures were considered: a command/inverse-op stack (precise, memory-light, but every
operation must be taught to undo itself) and a snapshot stack (uniform, covers operations
nobody enumerated). **Use snapshots as the default entry type, with inverse-op entries where a
snapshot is too blunt.**

```ts
export interface IUndoEntry {
    label: string;                  // "Delete canvas element" — tooltips, logging
    kind: "pageSnapshot" | "subtreeSnapshot" | "custom";
    undo(): void | Promise<void>;
    redo?(): void | Promise<void>;
    prepareRedo?(): void;           // capture the "after" state just before undo (below)
}
```

Two rules for writing an entry (also in `undoTypes.ts`, where entries are defined):

- **Check before undoing.** Many changes to a page will stay unrecorded for a long time: moving
  and resizing canvas elements, Format dialog changes, Talking Book's sentence splitting, game
  tool settings, and until Stage 3 all typing. Any of them can leave the page in a state an older
  entry does not expect. So before reversing its change, an entry checks that what it changed is
  still the way it left it, for instance by comparing the affected element's HTML with what the
  change produced. On a mismatch it throws rather than applies; the stack then discards itself
  (§4.13), so the user loses undo rather than having the page damaged. Keep entries narrow (one
  text box, one canvas element) so that unrecorded changes elsewhere cannot invalidate them, and
  so the check only has to cover that spot.
- **Prefer data that would survive a reload, but do not insist.** The stack dies with the page
  frame (§4.2), so an entry may hold references to the page's elements, ranges, or closures over
  page objects. But where it costs little, capture state as data (HTML strings, structural
  positions) and find the target again inside `undo()`. Then letting undo survive a same-page
  reload later (§4.2) would not mean rewriting the entry. Where holding a reference is clearly
  simpler, hold it, and say so in a comment where the entry is built, so the cost of changing
  course stays visible.

Bound the stack by **entry count** (~50). Skip byte accounting until something proves it
necessary; 50 page-HTML strings is single-digit MB worst case.

**Redo is in scope (§10 q1), so the stack is index-based, not pop-based.** Keep `entries[]` plus a
`currentIndex`: undo steps the index back, redo steps it forward, and any new push truncates
everything above the index (so typing after an undo discards the redo branch — standard, expected
behaviour). Two things keep the cost genuinely small:

- **Capture the "after" state lazily, at undo time**, not at commit time: when undoing a snapshot
  entry, first capture the *current* state as that entry's redo state, then restore the before
  state. So nothing extra is paid on the common path — every typing transaction — and the cost
  falls only where the user actually undoes. Not a new idea: `origamiUndo` already does exactly
  this (`origami.ts:288-292` stashes a fresh clone before decrementing).
- **`redo?()` stays optional, so Redo can arrive per entry kind.** An entry without it acts as a
  floor — `canRedo()` is false when the next entry can't redo.

`canUndo()` must stay **synchronous and cheap** — C# polls it on a timer
(`WebView2Browser.cs:963-996`, with a reentrancy guard that returns `true` on overlap). A
`canUndo` that walks entries or touches layout will make the Undo button flicker.

### 4.2 The stack lives in the page frame, and dies with it

`theOneUndoStack` lives in the **page** frame and is set up when each page loads
(`bookEdit/undo/pageUndo.ts`, called from `editablePage.ts`). So undo covers the page **as it is
currently loaded**: changing page, or anything that reloads the same page, starts with an empty
stack. The page frame is also where the Undo button lands (C# calls `topBarButtonClick("undo")`
there), where Ctrl+Z and Ctrl+Y arrive, and where every mechanism the stack arbitrates lives. The
workspace frame's `canUndo()`, which C# polls, and `handleUndo()` just ask the page frame.

This is what the old Undo already did: every pre-existing mechanism dies with the page frame. It
rules out undoing a page deletion, which would have to outlive the page (§10 decision 7).

**Things that reload the current page**, and so empty the stack, without changing page (traced
2026-10-06; BL-13502 may remove some of the save-only ones): leaving Change Layout mode; choosing
a different layout for the page, or switching it to or from a custom layout; importing a video;
turning a canvas text box into a read-only data field; the image copyright and credits dialog,
including "copy to all images"; the book's copyright and license dialog; opening the AI Image
Editor; the topic chooser; Book Settings; changing the content languages; page size or
orientation; moving a page in the page list; the book's files changing outside Bloom; Report a
Problem. Changing the UI language, and some theme changes, reload the whole Edit tab. None of
these is undoable, and each discards the undo history of the page before it, as it always has.

**What undo across a same-page reload would take**, if it is ever wanted (most likely first for
leaving Change Layout mode): the history must survive the reload (the stack back in the workspace
frame, or its entries saved and restored); its entries must find their targets on the rebuilt
page (the data preference in §4.1); and the history must stay continuous, so every reload that
changes the page must itself become an entry, backed by C# keeping the page's HTML from before the
change, whose undo restores that HTML and reloads. That last part is the hard one, and costs the
same wherever the stack lives.
### 4.3 Selection anchors, not DOM bookmarks

Avoiding CKEditor-style bookmark spans is realistic, but be honest about what exists: Bloom has
the **consume** side (`EditableDivUtils.makeSelectionIn`, which already takes a `divBrCount` for
disambiguating around `<br>`s) and a partial **capture** side
(`getElementSelectionIndex`, `editableDivUtils.ts:30-46`, which returns only a character offset
and resolves the editable via `$(anchorNode).closest("div")`). Nothing computes `brCount` /
`atStart` on capture — in-tree callers pass `-1` (`readerToolsModel.ts:587-592`). **The capture
function is new code and needs hard testing.**

```ts
export interface ISelectionAnchor {
    editable: IEditableLocator; // structural: page-relative index, or translationGroup index + lang
    textOffset: number;         // characters of text content before the caret
    brCount: number;            // line-break elements to step past after that offset
    atStart: boolean;           // tie-break at a node boundary
}
```

Locate the editable **structurally**, not by `id`: ordinary `.bloom-editable` divs have no
`id` (only the talking-book tool assigns them, `audioRecording.ts:1380, 3681`), and after a
restore or reload the element object is new anyway.

Why this beats bookmarks — and the case got **stronger** while this plan was being written.
Originally: the bookmark approach's own documented bug is that inserting a marker mid-word makes
the markup routine see `"hous"`-marker-`"e"`, so reader markup is temporarily wrong while you fix
a letter. An offset anchor doesn't perturb the DOM, so dropping bookmarks fixes that, and it
deletes the whole `fixUpEmptyishParagraphs` / `safelyReplaceContentWithCkEditorData` /
`setCkeditorBookmarkContent` / `cleanUpNbsps`-bookmark-emptying family plus the `cke_bm_`
scrubbing on the C# side.

**Then BL-16558 landed on master (2026-08), and it raises the stakes.** The decodable and leveled
reader tools no longer rewrite the DOM to show violations at all: they paint with the CSS Custom
Highlight API — `::highlight()` pseudo-elements over **live `Range` objects**
(`bookEdit/js/textHighlightManager.ts`, `readerHighlights.ts`, styles at `editMode.less:1074-1100`).
Talking Book's current-sentence highlight works the same way (`editMode.less:1145`). The
consequence, which BL-16558 had to fix for `cleanUpNbsps`, is general:

> **Any code that rebuilds an editable's text nodes silently collapses every live Range pointing
> into them, and the highlights vanish.**

That makes DOM-mutating bookmarks actively hostile to the current architecture, not merely
inelegant — inserting and removing marker spans around the caret is exactly the kind of node
churn those Ranges cannot survive. It also imposes a new obligation on *our* work; see §4.11.

**Anchors must cover a selected range, not just a caret.** Three consumers need a range: undo,
which restores the selection each step had, so undoing bold re-selects the text (§4.14 item 20);
the colour dialog, which runs in the workspace frame and must apply to the selection the user made
before it opened; and the SetupLink hyperlink dialog, which relies on the range surviving a dialog
(`BloomField.ts` `setupHyperlink`). So an anchor is a start and an end, each in the form above. For
the *snapshot* case there's a simpler trick: inject start and end markers into the captured HTML
**string** (not the live DOM, so none of the bookmark downsides apply) and strip them on restore.
Offsets then only have to serve the toolbox-markup case and the dialogs.

### 4.4 Build on `beforeinput`, and fence off native undo

For the new editor the modern primitive is `beforeinput`/`input` (with
`InputEvent.getTargetRanges()`), not `keydown`/`keypress`. It fires uniformly for typing, paste,
drop, IME commit, autocorrect **and the browser's own undo**, and `inputType` says which. That
gives one place to open/close typing transactions, one place to substitute our own DOM op, and
correct behaviour under composition (suspend DOM meddling between `compositionstart` and
`compositionend`) — important for the languages Bloom serves, and an area where the current
`keydown` code has had trouble (BL-3900, BL-5215 with longpress).

**Native undo does not go away when CKEditor does.** Plain typing in a contenteditable feeds
Chromium's own undo stack, and today CKEditor's undo plugin is what intercepts Ctrl+Z inside a
box. If native undo ever fires, the DOM changes outside our stack and the two histories
diverge. So the new editor **must** intercept `beforeinput` with `inputType`
`historyUndo`/`historyRedo`, `preventDefault()`, and route to the shared stack; that includes
Ctrl+Shift+Z, which is Redo today alongside Ctrl+Y (§4.14 item 23). This is
correctness, not polish, and it is the replacement for `BloomField.PreventRemovalOfSomeElements`'s
`document.execCommand("undo")` too: block any `delete*` whose `getTargetRanges()` covers a
`.bloom-preventRemoval` element, rather than letting the deletion happen and undoing it.

### 4.5 Own the inline-formatting engine; stop using `execCommand`

Bold/italic/underline/superscript/colour/remove-format become a pure DOM function over a
`Range` (`inlineFormat.ts`) rather than `document.execCommand`. Reasons: `execCommand` is
deprecated; it is inconsistent between engines (`BloomField.InsertLineBreak`'s comment
documents exactly this biting Bloom during the WebView2 migration); and it silently writes to a
browser undo stack we can't inspect — one of the five mechanisms we're eliminating. A pure
function is also the most testable piece of this project.

For colour, use Bloom's **existing** `colorPickerDialog` (already exposed cross-frame as
`workspaceBundle.showColorPickerDialog`) instead of CKEditor's `colorbutton` panel. That is a
UX improvement, and it deletes the palette-caching hack and the `labelForDefaultColor`
plumbing in `attachToCkEditor`.

### 4.6 Typing transactions

One undo entry per keystroke is useless. Close the current transaction on a word boundary
(space / punctuation / Enter), a switch between inserting and deleting, a caret move or focus
change, an idle timeout (~1 s), or any non-typing command. A transaction that leaves the content
as it found it records nothing, and each entry restores the selection as well as the text (§4.14
items 20–22). This approximates CKEditor's `undoManager` and matches user expectation. A
transaction holds the snapshot taken when it opened; closing it commits that entry.

### 4.7 `getData()` replacement

Once CKEditor is gone there is no filling char, no bookmark span and no injected comment, so an
editable's "clean HTML" is `div.innerHTML` plus a small normalizer (`getCleanHtml`). That one
fact deletes services 4 and 5 above and most of the artifact-scrubbing list.

### 4.8 Paste and drop sanitizing is a safety guarantee — default-deny

**This is the requirement most likely to be lost by accident**, because CKEditor provides it in
one config line and its absence is invisible until a user pastes a table into a book. State it
plainly:

> The user must not be able to introduce HTML structures that Bloom's own UI could not have
> created. Tables, `div`s, `iframe`s, images, ids, classes and arbitrary span styles pasted from
> a web page are hard or impossible to edit or delete through Bloom's UI, and may not survive
> Bloom's own processing.

There is a second rationale already written into `config.js:107-112`, worth preserving because
Stage 5 deletes that file:

> *"…by letting people paste things that cannot be duplicated by a user doing a translation, are
> we leading people to expect formatting in Bloom that translators will not actually be able to
> replicate? Therefore for now we're limiting pasting to things that a translator could also
> do."*

Four things the new `pasteSanitizer.ts` must get right:

1. **Allow-list, not deny-list.** Today's whole permitted vocabulary is
   `p br em i strong sup u; b{font-weight}; a[!href]; span{font-variant,color}` — everything
   else is dropped, attributes included. Reproduce that shape: enumerate what's allowed and
   discard the rest, so a tag nobody thought of fails closed. Note the two annotations carry
   real history and are in tension: BL-4775 removed `span` entirely ("so that you can't paste
   spans"), then BL-12357 had to allow `span{font-variant,color}` back for small caps and text
   colour. The sanitizer is where that tension lives; don't loosen either without reading both
   tickets.
2. **Sanitize on the boundary only, never as a DOM invariant.** CKEditor sets
   `config.allowedContent = true` *and* a restrictive `pasteFilter`, and the split is
   deliberate: the first attempt at BL-3899 (duplicate ids from pasted divs) filtered *all*
   content and broke BL-3976. Bloom's own code legitimately writes markup the sanitizer would
   reject — `audio-sentence` spans with ids, `bloom-linebreak`, canvas elements. So the filter
   applies to incoming clipboard/drop payloads and nothing else.
3. **Cover `drop`, not just `paste`.** Verified: CKEditor's clipboard plugin attaches its own
   `drop` listener and routes drops through the *same* filter as pastes
   (`ckeditor.js:622`, `attachListener(…"drop"…)` → `{dataTransfer, method:"drop"}`). Bloom's
   only drop handling of its own is for internal canvas-element drags via a custom
   `text/x-bloom-canvas-element` type (`CanvasElementManager.ts:2069-2088`), which does nothing
   for externally-dropped HTML. **So this protection is currently invisible and would disappear
   silently.** The new editor must handle `drop` (or `beforeinput` with
   `inputType: "insertFromDrop"`) through the same sanitizer as paste.
4. **Rich formats other than `text/html`.** Chromium normalizes Word/RTF clipboard content to
   `text/html` before it reaches the page, so one HTML sanitizer should cover those flavours —
   but *verify* rather than assume, and decide explicitly what to do when only `text/rtf` or an
   unknown flavour is on offer (recommended: fall back to `text/plain`, never attempt to parse
   an unknown format).

§4.14 items 13–19 add the details of what CKEditor's filter and paste pipeline actually do, which
the sanitizer and the paste handler must reproduce: content copied within the same page session is
not filtered, disallowed blocks become paragraphs rather than vanishing, pasted HTML is
normalized, and pasted blocks merge into the current paragraph.

Because its absence is silent, this needs **adversarial tests**, not just happy-path ones: paste
a table, a nested `div`, an `iframe`, a `<script>`, an `<img>`, a styled `<span>` soup from a real
web page, and a block copied from another Bloom book (the BL-3899 duplicate-id case) — and drop
each of those too. Every one of these belongs in the Stage 0 inventory with an expected outcome.

### 4.9 Clipboard ownership — the requirement BL-6681 actually records

BL-6681's most load-bearing content today is a 2026 comment from the BL-16459 investigation,
which names the question a replacement must answer:

> Can Bloom supply the clipboard payload (rich *and* plain) and be told whether the write
> succeeded?

The problem it comes from: when another program briefly holds the Windows clipboard, Ctrl+X
deletes the selected text **and** the clipboard write silently fails, so the text is gone from
both places. The obvious fix — copy first, delete only if the copy succeeded — was built
(PR #8140) and withdrawn, because Bloom's own cut can put only **plain text** on the clipboard,
so every cut of a phrase containing bold, a link or an inline picture lost the formatting. So
Bloom still leaves Ctrl+X to the browser and cannot protect the text.

Hard-won measurements recorded there, which constrain any design:
- **Chromium never reports a clipboard failure to Javascript at all** — `writeText` resolves,
  `execCommand` returns `true`, `readText` returns `""`. So a JS-only cut can never be safe.
- A .NET clipboard *read* also doesn't fail while another program holds the clipboard (OLE
  serves a cached copy); only **writes** fail honestly. So the success signal has to come from
  a C# write.
- Windows' "HTML Format" needs a byte-offset header that .NET does **not** write for you. That
  header is the substance of the multi-format work, not the plumbing.
- The withdrawn branch is deliberately preserved: `origin/BL-16459-clipboard-failure-reporting`,
  with findings in PR #8140's comments. Read it before re-deriving any of this.

What this plan therefore commits to: the new editor **owns `cut` and `copy`** on
`.bloom-editable` (it must anyway, to replace service 13 above), and structures them so the
payload is produced as *both* `text/html` and `text/plain` and handed to whoever writes it —
rather than calling `navigator.clipboard.writeText` and hoping. That makes a safe cut
*possible*; whether we also build the C# multi-format write is a scope question — now decided as seam-only (§10 q3).
Getting this seam right costs nothing now and is expensive to retrofit, so it goes in the
`clipboard.ts` design from the start even if BL-16459 stays a separate ticket.

### 4.10 Page-scoped handler lifetime — the prerequisite for snapshot restore

Snapshot restore replaces DOM elements, so anything attached to them dies with them. This is the
substance of risk 2, and it needs a design rather than a per-case scramble.

#### What's actually attached

Counted across the page frame: **~40 `addEventListener` sites and ~50 jQuery `.on()` sites** in
about 25 files, plus jQuery-UI `draggable`/`resizable`, `qtip`, `nicescroll`, `longPress`,
Comical, and **17 observer sites** (`MutationObserver`, `ResizeObserver`). So no single function
knows them all, and no single function ever should.

Three of those groups behave quite differently, which is the key to the design:

| Kind | Re-attachment behaviour |
| --- | --- |
| `addEventListener` with a **stable module-level function reference** | **Already idempotent.** The DOM spec makes a second `add` with the same (type, listener, capture) a no-op. Bloom exploits this deliberately — `CanvasElementManager.addEventsToFocusableElements` carries the comment "*Don't use an arrow function as an event handler here. These can never be identified as duplicate event listeners, so we'll end up with tons of duplicates*". |
| Property assignment (`el.onclick = …`, `container.ondrop = …`) | Idempotent by construction — assignment replaces. |
| jQuery `.on()`, and any arrow function / bound method / fresh closure | **Duplicates on every call.** Needs explicit `.off()` or namespaced events. |
| Observers, jQuery-UI plugins, qtip, Comical | Neither: they need explicit `disconnect()` / `destroy()`, and a *new* observer per call is a leak. |

#### An existing bug this uncovered — worth its own ticket

`SetupElements` takes a *container* and is already called re-entrantly on subtrees
(`CanvasElementManager.ts:1007` in `refreshCanvasElementEditing`, `imageDescription.tsx:336`).
But it calls `AddEditKeyHandlers(container)` (`bloomEditing.ts:727`), and two of that function's
handlers are attached to **`document`**, not to the container: the Ctrl+Space clear-formatting
handler (`:291`) and the Ctrl+R/L/E justify block (`:301`). It also attaches per-editable
`keydown` handlers via jQuery `.on()` (F6/F7/F8, Ctrl+Alt+0/1/2, show-invisibles), which
duplicate for any editable inside a re-set-up container.

So **handlers already accumulate on every canvas-element refresh**, before this project adds any
restore path. Most of the duplicated commands are near-idempotent (`justifyright` twice looks
like once), which is presumably why nobody has noticed; F6's
`insertHTML("<sup>" + selection + "</sup>")` is the one that looks likely to misbehave visibly.
Found by code reading, **not reproduced** — so Stage 0 should attempt a repro and file it
separately. It is a real bug independent of CKEditor, and it is the best possible evidence that
this area needs the design below rather than more discipline.

#### The design: distributed registration, centralized invocation, teardown by signal

The tension in "one function that knows all the handlers" versus "each client contributes its
own" dissolves if you centralize the **invocation** and distribute the **knowledge**:

```ts
// new file, e.g. bookEdit/pageSetup/pageScope.ts
export interface PageScope {
    readonly page: HTMLElement;
    readonly signal: AbortSignal;      // aborted when this page instance goes away
    addCleanup(fn: () => void): void;  // observers, qtip, jQuery-UI, Comical
}
type PageContributor = (scope: PageScope) => void;
export function registerPageContributor(name: string, fn: PageContributor): void;
export function setUpPage(page: HTMLElement): PageScope;  // runs every contributor
export function tearDownPage(scope: PageScope): void;     // abort, then run cleanups LIFO
```

- **Distributed knowledge.** Each module calls `registerPageContributor("canvasElements", …)` at
  import time, in its own file. `setUpPage` knows nobody. Adding a feature means one registration
  in the file that owns it — no central list to edit, and therefore no central list to forget.
- **Teardown by signal, not by idempotency.** `addEventListener(type, fn, { signal })` means one
  `controller.abort()` removes *every* listener in the scope at once. Restore becomes
  `tearDownPage(old)` → mutate DOM → `setUpPage(new)`.

That second point is why this beats the "idempotent re-run" framing in the original question.
Idempotency requires every handler to *be* dedupable, which forbids arrow functions and closures
and relies forever on the discipline the comment in `CanvasElementManager` is pleading for. A
signal-scoped teardown makes closures and arrow functions **safe**, so the easy way to write a
handler becomes the correct way. That is the only durable answer to "error-prone if someone
forgets": don't ask people to remember — make the default right.

Observers hang off the same scope via `addCleanup(() => obs.disconnect())`, so they get the same
one-call teardown without `pageScope.ts` knowing what they observe.

#### On the delegation alternative

Delegation (listen on the root, inspect `event.target`) has one real virtue the objections
don't cancel: a handler on a node that is never replaced is *inherently* immune to DOM
replacement. But it cannot be the whole answer, for a reason beyond the stated objections:
**many relevant events don't bubble** — `focus`/`blur` (Bloom already uses `focusin`/`focusout`
for this reason), `load`, `error`, `mouseenter`/`mouseleave` — and observers can't be delegated
at all. So the rule is:

- **Delegate on the page root** (not `document`) for handlers that are about a *class* of
  element, need no capture phase and no per-element state. Naturally restore-proof.
- **Direct listeners with `{ signal }`** for everything else.

Delegating on `document` rather than the page root is what produced the accumulation bug above,
so the distinction is not pedantic.

#### Enforcement, since convention alone won't hold

1. **An ESLint rule** for page-frame files: `addEventListener` must pass a `signal`, and jQuery
   `.on()` is banned. A custom rule is a few dozen lines and converts a discipline into a build
   failure. This is the main answer to "someone forgets".
2. **A leak test in the live-Bloom harness.** JS can't enumerate listeners, but CDP can
   (`DOMDebugger.getEventListeners`). Assert that after N setup/teardown cycles the page's
   listener count is unchanged. That is a direct regression test for this whole bug class — and
   it would fail today.
3. **A dev-mode warning** when `setUpPage` runs while a previous scope has not been torn down.

#### The cross-frame half, which a page-frame registry cannot reach

Several observers live in the **toolbox** frame watching page-frame elements: `motionTool`,
`GameTool`, `audioRecording` (`highlightIntegrityObserver`, `visibilityObserver`),
`PlaceholderProvider`, `StyleEditor`, `BloomSourceBubbles`. No page-frame registry can own those.
They already have `disconnect()` logic driven by page-change hooks, so the answer is to reuse the
hook that already exists for exactly this purpose: `applyToolboxStateToPage()`, which
`switchContentPage` calls after a page load (`workspaceRoot.ts:164-170`). **Snapshot restore must
call it too.** Treat "the page DOM was replaced under you" as indistinguishable from "a new page
loaded", because for every one of these clients it is.

#### Consequence for sequencing — and a stronger case for the reload fallback

Migrating ~90 attachment sites is not a prerequisite we want to put in front of undo. So:

- `pageScope.ts` is a **new file that can land early** and be adopted module by module, each
  adoption a small independent commit. Ideal for the rebase strategy.
- **Tier 1 restore needs no contributors at all** (§4.11) — handlers live on the `.bloom-editable`
  div, which survives an `innerHTML` replacement. Typing undo is unaffected by any of this.
- **Tier 2 narrow-subtree restore** needs only the contributors touching that subtree — a handful.
- **Tier 3 turns out to be empty** (§4.11): origami keeps its own working in-place clone restore,
  delete-page undo is out of scope, style undo is deferred. So no generic full-page restore gets
  built, and **`pageScope` is not a prerequisite for undo at all.**

That is the happy outcome: `pageScope` is worth doing for its own reasons — the accumulation bug,
and a clean lifetime for the new editor's handlers — but it no longer gates anything, so it can be
adopted at whatever pace suits, module by module.

### 4.11 Restore cost tiers — typing undo must never reload the page

Reloading a page is currently slow enough that using it for *every* undo would be a visible
regression on the most frequent undo of all: typing. So restore cost must be tiered by **how much
of the page the operation actually touched**, with the expensive path reserved for the rare
structural case. The `kind` field on `IUndoEntry` (§4.1) exists for this.

| Tier | Scope | How restore works | Cost |
| --- | --- | --- | --- |
| **1 — editable** | Typing, inline formatting, paste or cut within one `.bloom-editable` | Restore `editable.innerHTML` + `ISelectionAnchor`. Nothing else. | Instant, no C# round-trip |
| **2 — subtree** | Delete / duplicate / modify a canvas element, image swap | Restore the `.bloom-canvas` subtree's HTML, then `refreshCanvasElementEditing` — the existing path used when adding a canvas element | Fast, no reload |
| **3 — page** | Anything touching page structure | Install the snapshot into the live DOM, then re-run normal page init (§4.10). **In place — no navigation.** | Moderate; and see below: this tier may be empty |

> **⚠ New obligation from BL-16558 (§4.3): every tier must repaint live-Range highlights.**
> Because the reader tools and Talking Book now draw their highlights as `::highlight()`
> pseudo-elements over live `Range`s, restoring `editable.innerHTML` — Tier 1's whole mechanism —
> rebuilds the text nodes and collapses those Ranges, so the highlights disappear with no error.
> So a Tier 1/2 restore must, after writing the HTML, ask the highlight owners to repaint:
> `textHighlightManager` for the reader violations and `audioTextHighlightManager` for the current
> audio sentence. Add this to `reinitializePageAfterRestore()`'s contract, and note the same trap
> applies to the toolbox-markup anchor path — which is precisely why BL-16558 had to move
> `updateMarkup()` to *after* `cleanUpNbsps`. **The symptom is silent**: undo appears to work and the
> highlights are simply gone until something else repaints them.

**Tier 1 is verified safe and already has a working precedent in-tree.** No event handlers are
attached to nodes *inside* editables — they attach to the `.bloom-editable` div itself, which
survives an `innerHTML` replacement (checked: no `addEventListener` / `.on()` on `audio-sentence`,
`bloom-highlightSegment` or `bloom-linebreak`). And `readerToolsModel.undo()`
(`readerToolsModel.ts:574-591`) *already* restores `activeElement.innerHTML` and reselects at a
caret offset. So the cheapest tier is not new machinery — it is the existing reader-tools undo,
generalized and given a proper caret anchor.

That is the substantive reason not to reach for page snapshots by default: **typing and formatting
undo, the overwhelming majority of undos, never leave the page frame.**

#### Tier 3 restores in place. Navigation is rejected, for the reason you'd expect

An earlier draft of this plan offered "reload the page without saving" as Tier 3's first
implementation. That was wrong, and the reason is worth recording so nobody proposes it again.

**If we navigate, the document the iframe loads is generated by C# from the book DOM — so the book
DOM must already hold the undone state.** The only route into the book DOM is the save's merge
phase: `UpdateBookDomFromBrowserPageContent` → `Book.UpdateDomFromEditedPage`
(`EditingModel.cs:1760-1766`), which strips the editing UI, propagates the data-div through
`BookData`, recomputes feature requirements, and decides full-versus-partial save. We could skip
asking the browser for content (we already hold the HTML) and skip the disk write
(`SaveThen(skipSaveToDisk: true)` already exists, `EditingModel.cs:220, 451`) — but **not the
merge.** So "reload without saving" is really "a save minus two of its three phases", and the merge
it keeps is plausibly the expensive part, not the disk write it drops. It buys much less than it
appeared to, while adding a whole-book data-div propagation to every undo.

So **Tier 3 does what you described: make the live document what we want from the snapshot, then run
the normal init as if we had navigated.** No C#, no navigation, no save.

One incidental finding worth keeping, since it inverts a natural assumption: the save/reload
coupling runs the *opposite* way from the intuition. Saving does not exist to enable the reload —
**saving forces the reload**, because the save path leaves the page stripped and invalid for
editing (`State.SavedAndStripped`, whose comment reads "*The page has been saved; in the process,
we stripped various UI elements from it, so it's not in a valid state for editing. We hope to fix
this one day (BL-13502)*", `EditingStateMachine.cs:16-21`). Nothing requires a disk write before
navigating. That is why BL-13502 keeps surfacing below as the same knot.

#### Tier 3 may be empty — don't build it until something needs it

Enumerate what would actually land there:

- **Origami layout changes** — origami already restores in place, with no reinit at all, and it
  works today: `origamiRoot.replaceWith(clone)` where the clone is a jQuery `clone(true)`. Two
  properties make that sound, and both should be recorded because a well-meaning refactor could
  break them: layout mode strips `contentEditable` (`origami.ts:132`), so there is no editing UI to
  resurrect; and origami attaches its own UI handlers exclusively through jQuery (`.click()` at
  `origami.ts:404-457`), which is exactly what `clone(true)` preserves. **Keep this mechanism**,
  adapted as a custom `IUndoEntry` so it joins the shared stack's ordering. Note that migrating
  origami to `addEventListener` would silently break its undo, since `clone(true)` does not copy
  raw listeners.
- **Delete page** — out of scope: the stack dies with the page (§4.2, §10 decision 7).
- **Style changes** — deferred (§6 Stage 2c).

That leaves nothing requiring a *generic* full-page restore. So: **do not build one.** If something
later needs it, the mechanism is the in-place one above, and it will need `pageScope` (§4.10)
adoption for the contributors it touches.

Consequence: `pageScope` stops being a prerequisite for undo. It remains worth doing on its own
merits — the handler-accumulation bug, and giving the new editor's own handlers a clean lifetime —
but the undo work no longer waits on a ~90-site migration, and neither does it need the
reload path.

#### The sharp edge that survives: an in-flight save

Independent of tiering. An undo arriving while the state machine is in `SavePending` must not let
the in-flight save merge content we are discarding — the hazard `DiscardInFlightSave()`
(`EditingStateMachine.cs:367`) was built for on the external-process path. Either discard or defer;
decide deliberately and test it. This is the concrete form of risk 5, and Tier 1 undos are frequent
enough that the interleaving will be exercised constantly.

#### Measure before optimizing

The hypothesis that most of the reload cost is disk persistence plus HTML↔XML conversion is
plausible but unmeasured, and the alternative — page-DOM regeneration plus browser parse and
`bootstrap()` — would not be helped by skipping the save. Bloom has a performance-log feature
(`performance/PerformanceLogPage.tsx`); use it to attribute the time across: browser-side
serialize → HTML→XML → disk write → page-DOM regeneration → browser parse + `bootstrap`. Do this
in Stage 0, since it also sets the baseline for judging whether the project made page loads
faster.

Two things worth knowing before that measurement:

- **This project should make page loads faster regardless.** A reload currently waits on
  CKEditor's async init, and on the workarounds that exist to wait for it (§2). Removing them
  removes work from every page load, not just from undo.
- **BL-13502 would decouple save from reload generally.** If the save path stopped leaving the
  page invalid, `SaveThen` would no longer have to navigate at all — which would make Tier 3
  cheap and would benefit far more than undo. Out of scope here, but it is the same knot, and
  worth noting on that ticket that undo is another reason to want it.

### 4.12 The feature flag: an experimental-feature checkbox, latched by a body class

Testers need to turn the new editor on and off, so a `localStorage` switch (an earlier draft's
choice) is wrong — it needs devtools. Bloom already has exactly the right mechanism, and using it
also produces a *stronger* test than a JS-side flag would.

**The switch: `ExperimentalFeatures`.** `ExperimentalFeatures.cs` keeps a token list in
`Settings.Default.EnabledExperimentalFeatures` (per user, persisted across restarts), surfaced as
checkboxes in **Collection Settings → Advanced** (`AdvancedSettingsPanel.tsx` +
`CollectionSettingsDialog.cs`) and readable from JS via `app/enabledExperimentalFeatures`
(`AppApi.cs:47-52`). Add `kNewTextEditor = "new-text-editor"` alongside `kAppBuilder` and
`kAiImageEditing`. Testers get a checkbox in a dialog they already know, in the place Bloom already
puts this kind of thing — which is better than a Help-menu item, since it needs no new menu, no new
localization surface, and gives testers one place to look.

**Plus an environment-variable override for developers and automated tests:**
`BLOOM_NEW_TEXT_EDITOR=1`, read in the same C# place, winning over the setting. There is ample
precedent (`BLOOM_AI_EDITOR_URL`, `BloomWV2Path`, `BloomSandbox`), and the canvas e2e specs launch
Bloom themselves, so they need a switch that doesn't involve clicking through a dialog.

**How the page frame reads it — synchronously, latched per page load.** `useNewTextEditor()` is
called once per editable from `attachToCkEditor`, so it must be synchronous; the experimental-
features API is async, and reintroducing an async-init ordering problem in *this* project would be
absurd. Instead, let C# decide at page-generation time, in `Book.AddJavaScriptForEditing` — the
same method that currently adds the CKEditor script tag (`Book.cs:621-629`):

- flag on → **don't add the `lib/ckeditor/ckeditor.js` script tag at all**, and
  `dom.RawDom.AddClassToBody("bloom-newTextEditor")` (the `AddClassToBody` helper already exists;
  `Book.cs:1864` uses it for `template`).
- `useNewTextEditor()` is then just
  `document.body.classList.contains("bloom-newTextEditor")` — synchronous, no fetch, no ordering.

Two properties fall out of doing it this way, both valuable:

1. **The flag is automatically stable for the lifetime of a page load.** This matters more than it
   sounds: if the flag could change mid-page you could get some editables on the CKEditor path and
   some on the new one, which would be an unholy mess to debug. Latching it in the generated HTML
   makes that impossible by construction, and a setting change simply takes effect on the next page
   load.
2. **With the flag on, CKEditor is not merely unused — it is not loaded.** That is a far stronger
   test of the new path than leaving it loaded and bypassed, and it means the flag-on build cannot
   accidentally lean on CKEditor for something we forgot to replace.

**And the integration is already largely de-risked**, because "CKEditor is absent" is an existing
supported mode: `BookProcessor` strips the script tag for off-screen page processing, so guards are
already in place at every one of the main integration points —
`bloomEditing.bootstrap` (`:1214`), `StyleEditor.AttachToBox` (`:1210-1211`),
`toolbox.doWhenCkEditorReadyCore` (`:995`), and `editablePage.ckeditorCanUndo` (`:315`). With the
flag on, those guards already do the right thing; the Stage 3 dispatches become "*also* start the
new editor" rather than "skip CKEditor".

**The XLF entry is cheap, with one scheduling constraint.** The checkbox label is a localizable
string, and every existing experimental checkbox is localized via `useL10n` (e.g.
`CollectionSettingsDialog.AdvancedTab.Experimental.AppBuilder`), so add one following
`.github/skills/xlf-strings/SKILL.md` — including asking which priority file it belongs in. It gets
`translate="no"`, which is that skill's default for new entries anyway, so **no translator effort is
spent on it and removing it later costs nothing.**

The constraint that follows: **the flag must be gone before the Bloom release carrying it goes
beta**, because that is when strings get picked up for translation. After that point the entry is no
longer freely removable (the skill's rule: never change the ID or source of a translated entry).
This is the one hard calendar deadline in the whole project — note it in Stage 5.

### 4.13 `runUndoable` must nest from day one

`deleteCanvasElement`'s background-image branch already records its own image undo
(`CanvasElementManager.ts:2755-2770`: `prepareUndoForImageOperation` … 
`commitPendingImageOperationUndo`). Naïvely wrapping `deleteCurrentCanvasElement` in
`runUndoable` would then produce **two** entries for one gesture, so the first Ctrl+Z
half-undoes. Nested wrapping will keep happening as call sites accrete, so the semantics are fixed
up front (`UndoStack.endUndoableScope`, `compoundUndoEntry.ts`):

- **The outermost scope defines the one entry.** Everything pushed while it runs, by it or by
  anything nested inside it, becomes a part of that entry, which carries the outermost label. A
  nested `runUndoable` only deepens the scope; closing it records nothing.
- **Undo reverses every part, last first; redo replays them in the original order.** The entry is
  redoable only if every part is. Each part captures its redo state just before its own undo. A
  single push is recorded as it is.
- **A failed undo or redo discards the whole stack**, compound or not, and the error still
  propagates to Bloom's error reporting. A retry would rarely help (a failure is almost always a
  bug, which fails the same way again), and the failure leaves the document in a state no entry
  recorded, so the older entries could no longer be trusted to undo correctly. The legacy
  mechanisms are unaffected. An entry whose check before undoing (§4.1) finds the page changed
  fails the same way. A gesture still being recorded when the stack is cleared records nothing.

### 4.14 What CKEditor does without being asked

§2 lists the services Bloom calls CKEditor for. A read of the CKEditor 4.5.1 code we ship
(`lib/ckeditor/ckeditor.js` and its `plugins/`), done 2026-10-02, found more that it does
implicitly, in Chromium, with Bloom's config. None of it is a service Bloom asked for, so none of it
would be noticed missing until a user hit it. Each item below names the Stage 3 file that owns it.
Where an item says what Chromium does instead, that is expected behaviour, not a measurement:
**verify each in WebView2** before building against it.

**Enter and Backspace** (owner: `keyCommands.ts`, through `beforeinput` `insertParagraph` and
`delete*`). Stage 3 must not leave Enter to the browser.
1. **Enter always makes a `<p>`**, also at the end of a heading, and wraps bare text in a `<p>`
   first (`enterkey` plugin, `enterBlock`). Chromium's paragraph separator defaults to `<div>`,
   which Bloom's paragraph CSS, `kBlockElementSelector`, the reader tools and Talking Book would not
   treat as a paragraph. At minimum set `defaultParagraphSeparator` to `p` for every page.
2. **Splitting an element at the caret drops its `id` from the second half** (`range.splitBlock` /
   `node.clone` without ids). Chromium copies every attribute, so pressing Enter inside a recorded
   `span.audio-sentence` would give two spans with the same id. The split must remove the id from
   the new half.
3. **The new paragraph inherits the inline formatting at the caret as `strong`/`em`/`u`/`sup`**
   (the elements in `CKEDITOR.dtd.$removeEmpty`; `span` excluded by Bloom's config). Chromium
   re-applies a "typing style", which tends to produce `<b>`, `<i>` or `<font>`.
4. **BL-16649 "Do Not Indent This Paragraph"** removes `bloom-noIndent` from paragraphs that Enter
   creates. It hooks CKEditor's `key` event and its `enter` command (`BloomField.WireToCKEditor`).
   Without them every paragraph started from a no-indent paragraph stays unindented, so it needs an
   `insertParagraph` hook of its own.
5. **Backspace or Delete across a block boundary is done by CKEditor**, not the browser
   (`mergeBlocksCollapsedSelection`, `mergeBlocksNonCollapsedSelection`, "Prevent Webkit/Blink from
   going rogue when joining blocks"). Blink wraps moved text in `<span style="font-size:…;
   line-height:…">` to keep its old look, which would then ignore the Format dialog and pollute the
   saved HTML. Join blocks by moving the nodes ourselves.

**Inline formatting** (owner: `inlineFormat.ts`, `keyCommands.ts`, `FormatToolbar.tsx`).
6. **A format applied to a collapsed caret applies to what is typed next.** Ctrl+B with nothing
   selected, then typing, gives bold text; clear-formatting at a caret inside bold splits the bold
   there. CKEditor inserts an empty element with a ZWSP filling char to hold the caret. Since §4.7
   removes the filling char, the new engine needs a **pending format** applied to the next
   `insertText` in `beforeinput`, cleared by a caret move.
7. **Which tags count as the same format, and the toggle rule.** CKEditor writes `strong`/`em`/`u`/
   `sup`, but treats legacy `b`/`i` as bold/italic when checking and removing, and removes a nested
   `b` when applying bold. Whether a click applies or removes depends on the format at the
   selection's *start* (`style.checkActive` on the start path). Adjacent identical elements are
   merged (`mergeSiblings`). Without this, Bold over old `<b>` text nests `<strong>` inside it, and
   markup fragments into `<strong>a</strong><strong>b</strong>`.
8. **Toolbar buttons show their state.** Bold, italic, underline and superscript appear pressed
   (`cke_button_on`, `aria-pressed`) when the selection has that format, updated on every selection
   change. `FormatToolbar.tsx` needs the same, from the rule in item 7.
9. **Colour.** Applying a colour first removes or splits every existing colour span in the range,
   so colours never nest; "default" just removes the colour; inside a link the colour span goes
   *inside* the `<a>`, or the link's own colour wins (`colorbutton` plugin). Using
   `colorPickerDialog` (§4.5) adds its own requirements: it reports colours live while the user
   drags, so each report must replace the last and the whole drag must be one undo step; Cancel
   must restore the original, possibly mixed, colours rather than re-applying one colour; and it can
   return transparent or gradient values, which text colour must refuse.
10. **Clear formatting** (`removeformat` plugin) enlarges the range to whole formatting elements,
    splits partly selected ones at both ends, stops at the block, skips non-editable subtrees,
    unwraps rather than deletes, and re-selects the original range afterwards.
11. **The toolbar keeps the selection and follows the box.** Pressing a toolbar control must not
    move focus or the selection (`preventDefault` on `mousedown`, as CKEditor's floating space
    does), or Bloom's blur handlers (qtip, `hideInvisibles`, change detection) run. The toolbar
    repositions on scroll, resize and content change while the box has focus, and hides on blur.
12. Probably low value, recorded so it is a choice: Alt+F10 moves focus to the toolbar, arrow keys
    move between buttons and Esc returns to the text, with `role`/`aria` attributes throughout. And
    Ctrl+B/I/U and Ctrl+Space still work in `bloom-userCannotModifyStyles` fields, where only the
    toolbar is hidden today (A4); keep that unless decided otherwise.

**Paste, drop and copy** (owner: `pasteSanitizer.ts`, `pasteHandler.ts`, `clipboard.ts`; adds to
§4.8 and §4.9).
13. **Copies made within the same page session are not filtered.** CKEditor marks its own copies
    (`cke/id`) and applies `pasteFilter` only to external pastes and drops
    (`DATA_TRANSFER_EXTERNAL`). That is why pasted `bloom-linebreak` spans (D4) and audio-sentence
    spans (D5) survive today. `clipboard.ts` therefore needs its own "copied from this page" marker,
    and the sanitizer must treat marked content the way CKEditor does. A copy from another page or
    book stays external, which is what keeps BL-3899's duplicate ids out.
14. **What a disallowed element becomes** (`filter.js` `removeElement`): a block or table row becomes
    a `<p>` (`stripBlock`), `script`/`style` disappear with their content, void elements disappear,
    other inline elements are unwrapped keeping their text, and empty inline elements are removed
    (`span` excepted). "Discard the rest" in §4.8 must mean this, or table cells and list items run
    together on one line.
15. **Normalizing pasted HTML.** Keep only what lies between `<!--StartFragment-->` and
    `<!--EndFragment-->`; turn a trailing `<br class="Apple-interchange-newline">` into a paragraph
    end rather than a stray line break; collapse whitespace runs to one space and wrap top-level
    inline text in paragraphs; give an empty paragraph a `<br>` so the caret can enter it.
16. **Pasted blocks merge into the current paragraph** (`editable.insertHtml`): it splits the
    paragraph, joins leading inline text to it, inserts following blocks as siblings, and never nests
    `<p>`. The existing first-`<p>` unwrapping (D7) assumes this has already happened, so it does not
    "move unchanged" on its own. Neither `Range.insertNode` nor Chromium's `insertHTML` behaves this
    way.
17. **Plain text is converted, not inserted.** The toolbar Paste path (`pasteImpl` → `insertText`)
    HTML-encodes the text, turns a blank line into a new paragraph and a single newline into `<br>`,
    and tabs into spaces. Today that path skips every BloomField transform, so Ctrl+V and the Paste
    button give different results; the new handler should give both the same treatment.
    `reconstituteParagraphsOnPlainTextPaste` (BL-9961) must HTML-encode each line before wrapping it;
    today it does not (see "Found while reading", below).
18. **Copy and cut write CKEditor's own HTML** (`preventDefault`, then `getSelectedHtml` and
    `getSelectedText`). Chromium's own serializer bakes computed colours and fonts into spans, which
    would pass the `span{color}` allowance and freeze theme colours into every paste within Bloom.
    `clipboard.ts` must serialize the selection itself.
19. **Drops.** CKEditor cancels every drop, files included, so an Explorer file dropped on a box does
    nothing; the new code must decide that deliberately (check it cannot navigate the frame;
    `WebView2Browser.cs` `NavigationStarting`). It computes the insertion point from the drop
    position, and a drag within one box is a *move* recorded as one undo step (`internalDrop`); a drag
    between boxes deletes from the source box. If the sanitizer takes over drops, it must do the move
    itself, or a drag-move becomes a copy.

**Undo** (owner: `typingTransactions.ts`; adds to §4.6).
20. **Undo restores the selection, ranges included.** Each CKEditor snapshot stores the selection
    (`createBookmarks2`), and one is taken around every command, so undoing bold re-selects the text
    that was bold. See §4.3.
21. **A switch between inserting and deleting closes a step** (CKEditor's PRINTABLE and FUNCTIONAL
    key groups). Typing "abc", Backspace twice, then "xy", then Ctrl+Z removes only "xy".
22. **A step that changes nothing is not recorded** (`equalsContent`), so Ctrl+Z never appears to do
    nothing and the Undo button is never enabled with nothing to undo.
23. **Ctrl+Shift+Z is Redo today**, as well as Ctrl+Y. It stays (§10 decision 1): the `historyRedo`
    fence (§4.4) routes it to the stack, and the Stage 1 Ctrl+Y binding (`redoKeyBinding.ts`) gains
    it when the stack first holds entries (Stage 2).
24. CKEditor groups typing more coarsely than §4.6 (a step every 25 input events, or on a navigation
    key or click), so word-level steps will feel finer than today. That is intended, but it is a
    change testers may notice. It ignores IME keydowns (keyCode 229) and does not count paste or drop
    as typing; copy that.

**Accessibility attributes** (owner: `BloomTextEditor.ts`).
25. CKEditor sets `role="textbox"`, `aria-label` (the literal `"false"`, because Bloom sets
    `config.title = false`) and `tabindex` on every inline editable, and they get saved into books.
    `EditableDivUtils.pasteImageCredits` relies on `role=="textbox"` and `aria-label=="false"` to
    take its Source Bubble path. Change that test to something Bloom owns rather than reproduce the
    odd `aria-label`.

**Not a loss:** CKEditor 4.5.1 does nothing for IME composition, so §4.4 is an improvement there.
Tab, tables and lists are not used by Bloom. Wrapping a caret left directly in the editable into a
`<p>` (`fixDom`) is mostly covered by `BloomField.EnsureParagraphsPresent` and
`ManageWhatHappensIfTheyDeleteEverything`; those keyup fix-ups must not open undo steps of their own.

**Found while reading, independent of this project:** `BloomField.reconstituteParagraphsOnPlainTextPaste`
(BL-9961) wraps each line of a plain-text paste in `<p>` without HTML-encoding it, *after*
CKEditor's paste filter has run. So a multi-line paste of "a < b" is mangled, and a line such as
`<img src=x onerror=…>` would run script in the page.

## 5. Keeping up with master

The project runs for months against a fast-moving `master`, and its files are among the most
frequently edited in the front end (§5.1). The defence is to land small PRs promptly and never keep a
long-lived branch (§5.2); §5.7 is about keeping each PR's conflicts small in the first place.

### 5.1 How much drift there actually is

Guessing at this would give either paranoid over-syncing or a nasty surprise, so it was measured
(30 days to 2026-08-06):

| | Commits |
| --- | --- |
| All of `master` | **522** (~17/day) |
| Touching any file this project touches | **50** (~1.7/day) |

And the risk is concentrated — four paths are 74% of it:

| Commits (30d) | File |
| --- | --- |
| 19 | `bookEdit/js/bloomEditing.ts` |
| 9 | `bookEdit/toolbox/toolbox.ts` |
| 5 | `bookEdit/bloomField/BloomField.ts` |
| 4 | `lib/ckeditor/` |
| 3 | `bookEdit/StyleEditor/StyleEditor.ts` |
| 2 | `bookEdit/toolbox/readers/readerToolsModel.ts` |
| 1 each | `editableDivUtils.ts`, `canvasElementManager/CanvasElementManager.ts` |
| **0** | `workspaceRoot.ts`, `origami.ts`, `ImageUndoManager.ts`, `editablePage.ts` |

> **Correction (2026-09-07):** the zero row was measured with the wrong path for `workspaceRoot.ts`
> (it is `bookEdit/workspaceRoot.ts`, not `bookEdit/js/`). Re-measured over the following month
> (2026-08-06 → 09-07): `workspaceRoot.ts` **5** commits — BL-16558 changed `handleUndo` itself —
> `editablePage.ts` **3**, `origami.ts` and `ImageUndoManager.ts` genuinely 0. So Stage 1's
> integration risk was low, not zero, and the BL-16558 change had to be folded into the legacy
> providers. **When measuring drift, get the paths from `git ls-tree`, not from memory.**

Three things follow directly:

- **1.7 commits a day is a weekly sync, not a daily one.** A month between syncs would mean ~50
  commits to reconcile at once, which is what made the one Stage 0 rebase painful.
- **Stage 1's integration risk is low** (not zero — see the correction above). Stages 3 and 6 are
  where the cost lands, because that is where `bloomEditing.ts` and `toolbox.ts` are.
- **`lib/ckeditor/` is still being actively patched** — 4 commits in 30 days, to the library we are
  deleting. Each is a behaviour somebody needed. Stage 5 must diff that directory against the
  project's start point and account for every change, rather than deleting a directory assumed
  frozen.

### 5.2 Topology: short stage branches straight off `master`

Each stage is a **short-lived branch off `master`**, PR'd into `master`, squashed to one commit when
it goes to human review, and merged. The next stage branches from `master` after that merge. This is
the plan's original defence against drift — land small PRs promptly, never keep a long-lived
branch — and it is back in force: the `Version6.5` branch was cut on 2026-09-04, John decided on
2026-09-16 that this project targets `master` (6.6), Stage 0 merged to `master` on 2026-09-21 as
one squashed commit, and the integration branch `BL-6681-ckeditor` that the 2026-08-06 constraint
had required is retired (left in place for its history; nothing branches from it).

Each stage PR gets its own YouTrack card, a subtask of BL-6681 (Stage 0: BL-16878; Stage 1:
BL-16900), and its branch name starts with that card's id. Preflight reads the card id off the
branch name, so the card-side steps land on the right card without hand-work.

### 5.3 Sync procedure — merge, never rebase

A stage branch that lives longer than a few days merges `origin/master` in (`git merge`, never
rebase, never `--force` over a branch a reviewer has looked at). Keep `git config rerere.enabled
true` so a conflict resolved once is replayed. The squash at review time is the only history
rewrite, and `pr-ready-for-human` does it.

### 5.4 Keep every stage boundary shippable

Every stage PR must be a state that could ship as-is: green, flag-inert, no half-finished dispatch.
That is what preserves the "if the project stalls, Bloom is still better off" property.

### 5.5 Coverage a stage branch does not get on its own

The nightly workflow runs against `master` only, and it is the only thing that runs the full C#
suite and the visual-regression suite. A stage branch gets those the day it merges. For a stage that
changes editing UI and lives more than a week, run the nightly on the branch by hand:
`gh workflow run nightly.yml --ref <branch>`.

### 5.6 Stage 0 and the retired integration branch

Stage 0's PR (#8153, card BL-16878) merged to `master` on 2026-09-21. The Stage 1 work was carried
from the integration-branch topology onto a fresh branch off `master` (`BL-16900-undo-stack`) as one
squashed commit; the old `BL-6681-stage1-undostack` branch and its PR #8317 are superseded.

### 5.7 Keeping the conflicts small in the first place

These rules predate the no-merging constraint and all survive it — several matter considerably more
now than they did when stages were landing weekly.

1. **Almost all new code in new directories** — `src/BloomBrowserUI/bookEdit/undo/` and
   `src/BloomBrowserUI/bookEdit/textEditor/`. New files never conflict, which is the single biggest
   reason a months-long branch is survivable at all.
2. **Integration points into existing files are one-line dispatches** wherever possible:

   ```ts
   export function attachToCkEditor(element) {
       if (useNewTextEditor()) return attachBloomTextEditor(element);   // ← the whole edit
       ...existing body unchanged...
   }
   ```
   Note the dispatch goes *inside* `attachToCkEditor`, so its two call sites (`bloomEditing.ts:1226`,
   `CanvasElementManager.ts:951`) need no edit at all.
3. **One exception, and it needs a prep commit.** The toolbox keystroke pipeline
   (`toolbox.ts:1509-1607`) interleaves `createBookmarks`, `removeCommentsFromEditableHtml`, the
   async-updateMarkup double-bookmark dance (BL-10133), `cleanUpNbsps`, and `selectBookmarks`.
   Swapping bookmarks for anchors there rewrites ~100 lines of the most delicate keystroke code
   in the app, inside a churn-prone file. So do a **mechanical, behaviour-preserving prep commit
   early** (Stage 0): extract the save-selection / restore-selection bracket into two small
   functions with a clean seam. Then the eventual change swaps one function body instead of
   performing open-heart surgery mid-project.
4. **The flag is read in exactly one function**, `useNewTextEditor()`, in one new file — a
   synchronous body-class check, set by C# at page-generation time from an
   `ExperimentalFeatures` token (with an env-var override). See **§4.12** for why, and for what
   falls out of it.
5. **All deletion is last** (Stage 5), in a few mechanical commits. **Regenerate them, never
   reconcile them** — if a deletion commit conflicts with an incoming master change, throw it away
   and redo it mechanically against the new state. §5.1's finding that `lib/ckeditor/` is still
   being patched makes this concrete rather than theoretical.
6. **Avoid the churn-prone files** until late: `bloomEditing.ts` (2092 lines),
   `CanvasElementManager.ts` (3224), `toolbox.ts`, `audioRecording.ts` (5121),
   `StyleEditor.ts` (2627). §5.1's measurements confirm the guess: `bloomEditing.ts` and
   `toolbox.ts` alone are 56% of all watchlist churn.
7. Keep [PROGRESS.md](PROGRESS.md) current so an interrupted session resumes cleanly — and record
   each master-sync SHA there (§5.3).

## 6. Stages

Stages 1–2 deliver the Undo improvements **without touching CKEditor at all**, and are ordered
by user value per unit of risk.

The original reason for that ordering was "if the project stalls, Bloom is still better off",
which assumed each stage landed as it finished. Under the no-merging constraint (§5) nothing lands
until the end, so the property has to be maintained deliberately instead: **every stage boundary is
a green, flag-inert state the integration branch could merge as-is** (§5.4). The ordering then still
earns its keep — it means that whenever the merge window opens, whatever is finished is the most
valuable subset, not an arbitrary one.

### Stage 0 — Inventory, safety net, and the one prep commit

*New files, plus one behaviour-preserving refactor.*

- `docs/retire-ckeditor/BEHAVIOR-INVENTORY.md`: every behaviour that must survive, traced to
  the code and to the ticket its comment cites — BL-2484, BL-2557, BL-2746, BL-3009, BL-3125,
  BL-3899, BL-3900, BL-3976, BL-4775, BL-5215, BL-6721, BL-6845, BL-10133, BL-11745, BL-12205,
  BL-12357, BL-12381, BL-12391, BL-12448, BL-13779, BL-14004, BL-14051, BL-14947, BL-16065,
  BL-16330, BL-16490. Each row becomes a vitest case or a manual test idea. Include the
  easily-missed ones: the BL-13779 `data-user-deleted` hook, the BL-11745 qtip z-order juggling,
  `EnsureCaretNotInsideLineBreakSpan` on selection change, the SetupLink hyperlink command, the
  `cursor: not-allowed` no-editor case, and `PreventRemovalOfSomeElements`.
- **The paste/drop sanitizing rows are the most important ones in the inventory** (§4.8), because
  losing that protection is invisible rather than obviously broken. Write them as adversarial
  cases with expected outcomes — table, nested `div`, `iframe`, `<script>`, `<img>`, real-web-page
  `<span>` soup, and a block copied from another Bloom book (BL-3899 duplicate ids) — each one
  **pasted and dropped**. Capture today's actual behaviour for each before changing anything, so
  the new sanitizer is measured against reality rather than against the config string.
- Characterization tests pinning the pure-ish functions before they move.
- **The toolbox prep commit** from §5.7.3.
- **Attempt to reproduce the handler-accumulation bug** described in §4.10 (repeated
  `refreshCanvasElementEditing` → duplicate `document` keydown handlers and duplicate
  per-editable jQuery handlers; F6 is the likeliest visible symptom). If it reproduces, file it
  as its own card and fix it separately — it predates this project. Either way, add the CDP
  listener-count leak test from §4.10, which should fail before the fix and pass after.
- **Measure where page-reload time actually goes** (§4.11), using the existing performance-log
  feature: browser-side serialize → HTML→XML → disk write → page-DOM regeneration → browser parse
  + `bootstrap`. This decides how much Tier 3 really costs, and doubles as the baseline for
  showing that removing CKEditor made page loads faster.

Exit criteria: inventory reviewed; `pnpm test` green; prep commit demonstrably behaviour-neutral.

### Stage 1 — One entry point, no conversions

*New:* `bookEdit/undo/UndoStack.ts`, `undoTypes.ts`, `runUndoable.ts`, `compoundUndoEntry.ts`,
`pageUndo.ts`, `legacyUndoProviders.ts`, `redoKeyBinding.ts`, plus specs.

- `UndoStack` in the page frame, set up on each page load (§4.2): push / undo / **redo** /
  canUndo / **canRedo** / clear. Index-based with truncate-on-push (§4.1), count-bounded,
  `canUndo` and `canRedo` both O(1).
- The Undo button's page-frame handler (`topBarButtonClick`) calls the stack directly;
  `workspaceRoot.canUndo`/`handleUndo` become thin delegations to the page frame. Redo needs no C#
  counterpart — it is reached only by Ctrl+Y (§10 q1) — so it is a page-frame keydown binding
  (as both existing Ctrl+Y handlers are), acting only when nothing earlier claimed the key.
- **Wrap all four existing mechanisms as legacy providers in their current priority order.**
  No conversions, no behaviour change. **Note precisely what that order governs**, which §3's
  correction spells out: `handleUndo` is reached only from the top-bar Undo button, so wrapping it
  reproduces the *button* path exactly and leaves the keyboard path — which is handled per-context in
  the page frame and never enters `handleUndo` — untouched. Behaviour-neutrality holds, but not
  because the ordering is preserved; because the keyboard path was never in scope.
- Redo has no legacy providers to wrap, and there are **two** existing Redos, not one: origami's and
  the reader tools'. Both keep working via their own page-frame handlers until converted.
  (**Correction, verified 2026-08-06:** the earlier claim that `readerToolsModel.redo()` is
  unreachable was wrong — `decodableReaderTool.tsx:170` calls it. Stage 5 must **not** delete it
  blind; doing so would silently remove a working Ctrl+Y/Ctrl+Shift+Z for reader-tool typing.)
- `runUndoable(label, fn)` with the nesting semantics of §4.13.

Rationale for doing *no* conversions here: the four existing mechanisms are contextually
exclusive in practice (origami only in layout mode, reader undo only with an active markup tool,
image undo only on an image container), so their relative *ordering* only starts to matter once
text edits enter the shared stack — which is Stage 3. Converting them now would mean maturing
the riskiest new machinery (in-place snapshot restore) in the worst possible environment: a page
with live CKEditor instances (see Stage 3's note on `reinitializePageAfterRestore`).

Exit criteria: one entry point; `pnpm test` green; no user-visible change.

### Stage 2 — The undos the user actually wants

**2a — Undo delete page: dropped** (2026-10-06, §10 decision 7). Undo is scoped to the page as
it is currently loaded (§4.2), and a page deletion would have to outlive its page.

**2b — Undo delete canvas element.** Do *not* use a whole-page snapshot. Preferred: an
inverse-op / narrow-subtree entry that re-inserts the element's `outerHTML` into its
`.bloom-canvas` and calls the **existing, battle-tested** `refreshCanvasElementEditing` — the
same path used when adding or duplicating a canvas element
(`CanvasElementManager.ts:974-1010`; `GamePromptDialog.tsx:428-437` depends on it). The inverse
of `deleteCanvasElement` (`CanvasElementManager.ts:2747-2799`) tells us what's needed:
`Comical.update`, `removeDetachedTargets`, `normalizeCoverImageDesignation`.

Two things to verify before committing to the inverse-op, because they are the reason this
isn't trivial: (i) `Comical.deleteBubbleFromFamily` removes the element from a bubble family —
confirm the family can be re-linked from the restored `data-bubble` spec alone; (ii) a
drag-activity **target** removed by `removeDetachedTargets` must come back too. If either
proves messy, fall back to a **`.bloom-canvas`-subtree snapshot** restored through
`refreshCanvasElementEditing` — still far narrower than a page snapshot.

Also honour §4.13: the background-image branch already records an image undo, so the wrapper
must not double-record.

**Where to put it back.** Order among a .bloom-canvas's children is the stacking order (no
z-index), and Comical's bubble levels must agree with it (djustCanvasElementOrdering). New
elements go last; rectangles and background images go first; draggables are kept at the end. So the
entry records the deleted element's **neighbours**, not its index: put it back just below the
element that was directly above it, or failing that just above the one below it. An intervening
create (not undoable) then does no harm, which an index would not survive if anything reordered
the siblings. Holding the neighbours as element references is the simple way, and acceptable
under §4.1's preference rule: canvas elements carry no ids to find them by; say so in a comment.
Comical.deleteBubbleFromFamily rewrites the *other* family members' bubble data, so the entry
saves and restores theirs too. Per §4.1 it checks before undoing that the canvas still holds what
the deletion left. That matters most for the subtree-snapshot fallback, which would otherwise
silently delete any element created since.

**Ctrl+Shift+Z.** Stage 2 is the first time the stack holds entries, so `redoKeyBinding.ts`'s
`isRedoKeystroke` must accept Ctrl+Shift+Z as well as Ctrl+Y here (§4.14 item 23), with a test.

**2c** *(deferred, documented not built)*: undo for style changes — a snapshot of
`userModifiedStyles` would cover it, and the entry contract already allows it.

Exit criteria: deleting a canvas element is undoable, restored at its old place in the stacking
order and with its comic family intact; exactly one entry per gesture.

### Stage 3 — The new text editor, behind a flag, off by default

*New directory* `bookEdit/textEditor/`, built roughly in this order:

| File | What |
| --- | --- |
| `inlineFormat.ts` | Pure `Range`→DOM formatting engine: bold, italic, underline, superscript, colour, remove-format. **Do this first and test it hard.** Must preserve structural spans (`audio-sentence`, `bloom-highlightSegment`, `bloom-linebreak`) exactly as today's `addRemoveFormatFilter` does. Must also reproduce §4.14 items 6, 7, 9 and 10: a pending format for a collapsed caret, legacy `b`/`i` counted as bold/italic, the start-of-selection toggle rule, merging, non-nesting colours, and clear-formatting's splitting. |
| `selectionApi.ts` | `getSelectionAnchor` / `restoreSelectionAnchor` (§4.3 — the capture side is new code), `getCleanHtml(div)`. |
| `pasteSanitizer.ts` | **Default-deny allow-list** replacing `config.pasteFilter` (§4.8) — the project's main safety guarantee, applied to **both paste and drop**. Pure function, so it can be tested adversarially. Build it early (right after `inlineFormat.ts`) rather than late: it is the one piece whose absence is silent. Reproduces §4.14 items 13–15: same-session copies pass, disallowed blocks become paragraphs, pasted HTML is normalized. |
| `clipboard.ts` | Owns `cut` and `copy` on `.bloom-editable`, replacing CKEditor's interception (service 13). Produces the payload as **both** `text/html` and `text/plain` and keeps the write behind one seam, so a safe cut (§4.9) becomes possible. Read `origin/BL-16459-clipboard-failure-reporting` and PR #8140 first. Also subsumes `bloomEditing.cutSelectionImpl`, which currently uses `undoManager.lock/save` to make the cut one undo step. Serializes the selection itself and marks same-session copies (§4.14 items 13, 18). |
| `pasteHandler.ts` | Owns the `paste` event **and** the C#-initiated `pasteClipboard` entry point. Must cover *both* existing paths: normal insert-at-selection, and `pasteImpl`'s replace-whole-content path for a canvas element that is selected but not being text-edited (`bloomEditing.ts:1792-1842`: `setData("<p><p>")` + `insertText` under an undo lock, then `updateAutoHeight()` + `scheduleMarkupUpdateAfterPaste()`). Calls the BloomField transforms; pushes **one** undo entry. Merges pasted blocks into the current paragraph, converts plain text the same way for Ctrl+V and the Paste button, and owns drops, including moves and files (§4.14 items 16, 17, 19). |
| `typingTransactions.ts` | `beforeinput`-driven coalescing (§4.6), composition-aware, plus the `historyUndo`/`historyRedo` fence (§4.4), routing Ctrl+Y and Ctrl+Shift+Z to the stack. Each entry restores the selection, ranges included (§4.14 items 20–24). |
| `keyCommands.ts` | Enter and block joins (§4.14 items 1–5: always `<p>`, no duplicated ids, inherited formatting as `strong`/`em`, BL-16649's no-indent rule, joins without style spans); Shift+Enter → `span.bloom-linebreak`; F6/F7/F8; Ctrl+Alt+0/1/2; justify; Ctrl+Space (remove-format); Ctrl+B/I/U. Replaces every `execCommand` call **in the page frame** (`readerSetup.ui.ts:454` lives in the reader-setup dialog and is out of scope). |
| `autolink.ts` | Turns a paste that is exactly one URL (`http`, `https` or `ftp`) or one email address into a link (BL-6845). **Paste only**, as CKEditor's `autolink` plugin is: Bloom has never linked a URL as it is typed. Skips pastes containing markup and copies from within the page. Called from `pasteHandler.ts`. |
| `FormatToolbar.tsx` | React floating toolbar replacing `.cke_float`, positioned from the selection rect, localized directly (so `localizeCkeditorTooltips` dies), hidden for `bloom-userCannotModifyStyles` (BL-14947). Hosts the SetupLink hyperlink button. Buttons show pressed state; pressing one keeps focus and selection; the toolbar follows scroll, resize and content change (§4.14 items 8, 11, 12). |
| `BloomTextEditor.ts` | Per-editable attach/detach. **Synchronous** — no `instanceReady`, no async DOM rewrite. Also owns the BL-13779 content-changed hook, the BL-11745 qtip z-order handling, `EnsureCaretNotInsideLineBreakSpan` on `selectionchange`, and the `role`/`aria-label` dependency of `pasteImageCredits` (§4.14 item 25). |
| `useNewTextEditor.ts` | The one flag read: `document.body.classList.contains("bloom-newTextEditor")` (§4.12). Synchronous by design. |

Plus four small additive edits outside the new directory, all covered by §4.12: a
`kNewTextEditor` token in `ExperimentalFeatures.cs`; a checkbox in `AdvancedSettingsPanel.tsx` +
`CollectionSettingsDialog.cs`; and in `Book.AddJavaScriptForEditing` (`Book.cs:621-629`), skip the
CKEditor script tag and add the body class when the flag is on. That last one is the whole
mechanism, and it means the flag-on build never loads CKEditor at all.

Also in Stage 3, now that the flag-on path has no CKEditor instances to resurrect:
`PageSnapshot.ts` + a single `reinitializePageAfterRestore()`, used by every snapshot entry.

> **Why snapshot restore waits for Stage 3.** Restoring `.marginBox` innerHTML while CKEditor is
> live orphans every `div.bloomCkEditor` expando (`BloomField.ts:419`), which costs more than a
> missing toolbar: `doCkEditorCleanup` iterates `div.bloomCkEditor` (`editableDivUtils.ts:350`)
> and `getBodyContentForSavePage` calls it (`bloomEditing.ts:1483`), so the **save path would
> silently skip cleanup** for restored divs. Restoring under live CKEditor would mean re-running
> `attachToCkEditor` and waiting out the very `instanceReady` dance this project exists to kill.

Integration dispatches (one line each, added as late as possible): `attachToCkEditor`,
`doWhenCkEditorReady`, `StyleEditor.AttachToBox`'s gate,
`EditableDivUtils.doCkEditorCleanup` / `restoreSelectionFromCkEditorBookmarks`,
`audioRecording.cleanUpCkEditorHtml`, `ckeditorCanUndo`/`ckeditorUndo`, and the
`toolbox.ts` selection bracket extracted in Stage 0.

**One transform that cannot simply be moved.** `BloomField.restoreHtmlMarkupIfNecessary`
(`BloomField.ts:425-456`, BL-12357 small caps) works by detecting CKEditor-internal copies via
`dataTransfer.getData("cke/id")` and compensating for CKEditor's *own* span-stripping of
`dataValue`. With CKEditor gone, nothing stamps `cke/id` and nothing strips the spans, so the
transform is meaningless as written — and the problem it solves may simply not exist when
`pasteHandler.ts` reads raw `clipboardData`. **Verify against the BL-12357 repro; don't port.**
The verse-marker and audio-id transforms move unchanged. The first-`<p>` unwrapping does not move
on its own: it only makes sense on top of CKEditor's merging of pasted blocks into the current
paragraph, which `pasteHandler.ts` must now do itself (§4.14 item 16). The transforms also assume
HTML in CKEditor's normalized form (a bare `<p>`, `<b style="font-weight:normal">`), so they run
after the sanitizer and normalizer, not on raw clipboard HTML.

With the flag on, text edits push onto the **same** stack as everything else — the payoff of the
whole project. At that point the reader-tools and CKEditor legacy providers become redundant
(shared-stack snapshots capture markup too) and are deleted in Stage 4/5.

Exit criteria: with the flag on, the behaviour inventory passes; `pnpm test`, `pnpm lint`,
`pnpm typecheck`, `build/agent-vite.sh` and the C# suite green with the flag both off and on.

### Stage 4 — Flip the default and soak

- Flip `useNewTextEditor()` to true; the old path stays reachable by flag through the dev period
  only (we control both ends and don't owe legacy support).
- Delete the reader-tools and CKEditor legacy providers. Retire origami's private `keydown.origami`
  handler, moving **both** Ctrl+Z and Ctrl+Y onto the shared global handler — origami's Redo must
  keep working across this change, so convert its entry (Stage 4's first optional cleanup) in the
  same commit that removes its handler, not after.
- Optional cleanups, neither of which depends on anything in Stage 3 — so either may be pulled
  forward into Stage 1 if convenient: convert origami's `clone(true)` stack to shared **custom**
  entries (**keeping the clone** — it carries the jQuery-bound handlers that make its in-place
  restore work, §4.11), and convert `ImageUndoManager` commits to shared entries.
- Real-book soak testing: a talking-book book, a decodable/leveled reader book, a drag-activity
  book, an RTL book, and a book with an image embedded in a text field.

### Stage 5 — Delete (mechanical; regenerate rather than rebase)

- `src/BloomBrowserUI/lib/ckeditor/**` (1.5 MB) and `typings/ckeditor/`.
- C#: `Book.cs:629` `AddJavascriptFile` (and the dead commented `:578` line);
  `ProjectContext.cs:597` skin path; `BloomServer.cs:1041` icon special-case;
  `BookProcessor.cs:179-182` script-strip.
- CSS: `.cke_*` rules in `editMode.less`, `audioRecording.less`, `qtipOverrides`; the whole
  `hideAllCKEditors` mechanism (BL-12448 becomes moot — our toolbar simply isn't rendered until
  we want it).
- TS: `localizeCkeditorTooltips`, `updateCkEditorButtonStatus`, `doWhenCkEditorReady*`,
  `removeCommentsFromEditableHtml` (BL-4775 was a CKEditor artifact), `setCkeditorBookmarkContent`
  and `cleanUpNbsps`'s bookmark-emptying, `removeCkEditorFillingChars`, `fixUpEmptyishParagraphs`
  and `safelyReplaceContentWithCkEditorData` (verify first), `ckeRegex` in
  `jquery.text-markup.ts`, `PlaceholderProvider`'s `instanceReady` branch,
  `StyleEditor.AttachToBox`'s gate, `bootstrap`'s dead BL-3125 guard and the post-init
  `activateLongPressFor` re-attach.
- The **duplicate Ctrl+V `keydown` handler** (`bloomEditing.ts:1764-1770`), which exists only
  because CKEditor swallows `paste` inside editables. Once it doesn't, the document-level
  `paste` listener fires normally. Verify that before deleting — this is exactly the kind of
  workaround whose removal is the payoff.
- Renames: `ckeditableSelector` → `richTextEditableSelector`, `attachToCkEditor` →
  `attachBloomTextEditor`, and the `ckeditorCanUndo`/`ckeditorUndo` cross-frame exports.
- **The flag itself** (§4.12): the `kNewTextEditor` token, the Advanced-tab checkbox and its XLF
  entry, the `BLOOM_NEW_TEXT_EDITOR` override, the body class and `useNewTextEditor()`.
  **⚠ This must happen before the release carrying the flag goes beta**, while the XLF entry is
  still `translate="no"` and therefore freely removable (§4.12). It is the project's only hard
  calendar deadline.
  Deliberately *not* doing: clearing the obsolete token from users' saved settings. There is
  precedent for it (`MigrateFromOldSettings` does `SetValue("webView2", false)`), but the flag never
  ships beyond in-house testers, so a handful of stale tokens in their settings is harmless and not
  worth the migration code.
- Specs encoding CKEditor artifacts: `editableDivUtilsSpec.ts`, `toolboxSpec.ts`,
  `audioRecordingSpec.ts`, `jquery.text-markupSpec.js`.
- **Keep, but relabel, the legacy-book cleanups.** Books on disk contain `cke_*` classes,
  `cke_bm_*` spans, `data-cke-saved-href`, ZWSPs and `<br></p>`. Move `HtmlDom.RemoveCkEditorMarkup`,
  `BookData.IsCkEditorBookmarkSpan`, `XmlHtmlConverter`'s `<br></p>` regex and `PublishHelper`'s
  ZWSP scrub into one new `LegacyCkEditorCleanup` class, documented as migration-only, and leave
  the call sites. Removing them is a separate future decision (§10 "Still genuinely open").

### Stage 6 — Reap the simplification

With the CKEditor references gone, the async-init scaffolding has no reason to exist:
`doWhenPageReady` becomes trivial, `SetupElements` no longer races anything, and the
`requestPageContentDelay` bookkeeping can probably shrink. Do this as a **separate** pass with
its own review, not smuggled into Stage 5, because it changes real page-load ordering.

Also examine, but do **not** assume deletable: `audioRecording.setHighlightSession`
(`audioRecording.ts:198-202`), a superseding counter for overlapping page-setup rounds
("*newPageReady fires twice*", BL-15300 highlight flash). It is the surviving relative of the
race that originally opened BL-6681 (see §11), but "newPageReady fires twice" is not obviously
CKEditor's doing. Measure before touching it.

## 7. Test strategy

- **Vitest / jsdom** for everything pure or static-DOM-shaped: `inlineFormat`, `pasteSanitizer`,
  `ISelectionAnchor` round-trips, `getCleanHtml`, `autolink`, `UndoStack` bounds / page-scoping /
  nesting, `PageSnapshot` capture.
- **Not testable in jsdom:** `typingTransactions.ts` and `keyCommands.ts`. jsdom has no native
  editing behaviour and does not emit `beforeinput` or support `getTargetRanges()`. These can
  only be verified against a live WebView2 — budget for the CDP harness rather than for faking
  `InputEvent`s.
- **C# tests** through `build/agent-dotnet.sh` for `LegacyCkEditorCleanup`.
- **Live-Bloom verification** via the `run-bloom` / `bloom-automation` skills: attach over CDP,
  exercise a page, read the DOM back. The dev server pushes `.ts`/`.tsx` edits into a running
  Bloom, so most iteration needs no build.
- **Manual test ideas** per stage via the `add-test-ideas` skill, posted on the tracker card.
- A dev-only harness page for the formatting engine (nested formatting, partial selections
  crossing element boundaries, RTL, structural spans) pays for itself early.
- **`pasteSanitizer` deserves the most aggressive test suite in the project**, because it is a
  pure string→string function guarding a safety property (§4.8) whose failure is silent. Treat it
  the way one treats a sanitizer: adversarial corpus, fail-closed assertions (assert on what
  *survives*, so an unlisted tag can't slip through by nobody having written a case for it), and
  a real captured clipboard payload from a live web page rather than hand-written tidy HTML. The
  drop path needs the live-WebView2 harness, since `DataTransfer` is not meaningfully
  constructible in jsdom.

## 8. Risks, ranked

1. **Inline-formatting engine correctness.** Nesting, selections crossing element boundaries,
   RTL, Bloom's structural spans. *Mitigation:* pure functions, heavy unit tests, a dev harness
   page, and shipping behind the flag long before it's default.
2. **Snapshot restore re-initialization.** Restoring HTML must leave canvas elements, Comical,
   qtips and talking-book highlighting working. Origami is precedent for the *concept* only —
   it restores a `clone(true)` (handlers and data included) in layout mode where
   `contentEditable` has been stripped, so it is not precedent for the innerHTML restore path.
   *Mitigation:* the handler-lifetime design in **§4.10** is the prerequisite, not an
   afterthought — including calling `applyToolboxStateToPage()` so the toolbox frame's observers
   recover. Land `reinitializePageAfterRestore()` in Stage 3 where no CKEditor instances need
   resurrecting. **Mostly dissolved by tiering (§4.11)**: typing and formatting undo restore one
   editable's `innerHTML` and reinit nothing; canvas-element undo restores one subtree through the
   existing `refreshCanvasElementEditing`; origami keeps its own working clone restore. Nothing left needs a generic full-page reinit, so **don't build
   one.** Navigation-based restore was considered and rejected — it would require the book DOM to
   already hold the undone state, whose only route in is the save's merge phase (§4.11).
3. **Silently losing paste/drop sanitizing.** Ranked this high not because it is hard but
   because it is **invisible**: nothing fails, no test goes red, and the damage arrives later as
   a user's book containing a pasted table that Bloom's UI cannot edit or delete. Drop is the
   sharper edge of the two, since Bloom has no drop filtering of its own at all today and
   CKEditor has been quietly covering it (§4.8 point 3). *Mitigation:* adversarial inventory
   rows captured **before** any change, a pure sanitizer built early, and both paths tested.
4. **Native browser undo diverging from our stack.** Addressed by the `beforeinput`
   `historyUndo`/`historyRedo` fence (§4.4); listed here because forgetting it is silent and
   corrupting rather than obvious.
5. **Save interleaving.** Snapshot → save → undo → save must end with the restored HTML on
   disk. The
   sharpest case: an undo arriving while the state machine is in `SavePending` must not let the
   in-flight save merge content we are discarding — `DiscardInFlightSave()`
   (`EditingStateMachine.cs:367`) exists for this shape of problem; decide discard-vs-defer
   deliberately and test it (§4.11).
6. **IME / longpress / complex scripts.** The current `keydown` code has a history here
   (BL-3900, BL-5215). `beforeinput` plus composition-awareness is the fix, but it needs testing
   with a real IME and with the longpress character map.
7. **Paste fidelity.** The BloomField transforms encode hard-won behaviour. Move them unchanged
   and test against the inventory — except BL-12357, which cannot be moved unchanged (Stage 3).
   Distinct from risk 3: that one is about letting *too much* through, this one about mangling
   what we do let through.
8. **Talking-book audio files vs undo — mostly benign, one path to check.** Support-file cleanup
   runs only from `Book.BringBookUpToDate` (`Book.cs:1112`) and publish/upload paths
   (`BookStorage.CleanupUnusedSupportFiles`, `BookStorage.cs:2623-2632`), **not** on page save.
   So within an edit session, files referenced by restored spans still exist. The remaining
   exposure is the talking-book tool's *explicit* delete / re-record actions; scope the
   investigation to that path only.
9. **Cross-frame lifetime.** Settled in §4.2: the stack lives in the page frame and dies with it,
   so no entry can outlive the page it describes. Keep it settled.
10. **Unrecorded changes.** Much that changes a page will stay unrecorded for a long time (§4.1),
    and any of it can leave the page in a state an older entry does not expect. Mitigated by the
    check-before-undo rule (§4.1) and by keeping entries narrow; tests should interleave recorded
    and unrecorded changes.

## 9. Follow-ups this design makes cheap

- **A visible Redo button.** Redo itself is in scope, but Ctrl+Y-only (§10 q1). A toolbar button is
  where the remaining cost sits: there is **no Redo plumbing in C# at all** today — no
  `RedoCommand`, no `SetEditingCommands` parameter, nothing in the `updateEditButtons` websocket
  payload, no icon, no XLF entry. All of that is separable and can be added later without touching
  the stack.
- **Undo labels in the UI** — "Undo Delete canvas element" as the button tooltip.
- **Wider undo scope** — style changes, book-level operations, multi-page undo all plug in as new
  entry types without touching the stack.

## 10. Decisions

Everything here is settled. Recorded with the reasoning so a later session doesn't reopen it.

1. **Redo: in scope, extended rather than dropped — keys only (Ctrl+Y, and Ctrl+Shift+Z, which
   is also Redo today and stays), no toolbar button.**
   Origami has a Redo today, and removing it while unifying the stacks would be a small regression
   for layout-mode users. The cost is small provided we take the two cheap routes in §4.1: an
   index-based stack, and capturing the redo state lazily at undo time (origami's existing trick),
   so nothing is paid per keystroke. Ctrl+Y is a JS-only key handler, matching origami's current
   affordance exactly and needing **zero C# plumbing** — which matters, because Bloom has no Redo
   plumbing whatsoever today (no `RedoCommand`, no `updateEditButtons` field, no icon, no XLF
   entry). A visible button is deferred to §9 and can arrive later without touching the stack.
   `redo?()` stays optional, so an entry kind whose redo is hard can come later, acting as a redo
   floor until it does.
2. **Hyperlink UI: keep the current `showLinkTargetChooserDialog` flow exactly.** The new
   `FormatToolbar.tsx` hosts the same button invoking the same dialog. No behaviour change.
3. **Clipboard (BL-16459): seam only.** `clipboard.ts` produces rich **and** plain payloads and
   keeps the write behind one interface, but still writes from JS, so a genuinely safe cut remains
   impossible and BL-16459 stays open — much cheaper to close later, because the seam is the part
   that is expensive to retrofit (§4.9). Explicitly **not** doing the C# multi-format write or the
   "HTML Format" byte-offset header in this project.
4. ~~Delete-page undo keeps every deletion in the session~~: superseded by decision 7, which drops
   delete-page undo.
5. **The flag** is an `ExperimentalFeatures` checkbox in Collection Settings → Advanced plus a
   `BLOOM_NEW_TEXT_EDITOR` env-var override, latched into the page by a body class (§4.12). Its XLF
   entry is `translate="no"`, so removal is free — **but must happen before that release goes beta**,
   the project's one hard calendar deadline. Deliberately *not* clearing the obsolete token from
   testers' settings.
6. **A `runUndoable` gesture is one compound entry of everything pushed inside it** (§4.13),
   decided 2026-10-02 in John's review of Stage 1. Undo reverses all the parts, last first; redo
   replays them in order, and only if every part can. Keeping just one of the pushes would work only
   if the outer operation's entry happened to capture the whole gesture, and would oblige every inner
   layer to wrap itself in a scope of its own. And a failed undo or redo, of any entry, discards the whole stack rather than offering a
   retry, because what it leaves is a state no remaining entry can be trusted against.

7. **Undo is scoped to the page as it is currently loaded; undoing a page deletion is dropped**,
   decided 2026-10-06 after Hatton's review of Stage 1 (raised in standup, then on PR #8387). The
   stack lives in the page frame and dies with it (§4.2). Undoing a page deletion was the only
   reason it had lived in the workspace frame, and keeping it there cost page ids on entries,
   generation counts guarding `runUndoable` scopes against reloads, cross-frame calls for the
   button and Ctrl+Y, and a rule that entries be pure data. Nothing users had depended on it: every
   pre-existing undo already died with the page frame, and the workspace stack cleared its page
   entries on every reload too. Two rules came with the decision (§4.1): entries check before they
   undo, and they *prefer* state that would survive a reload, without insisting, noting exceptions
   in comments, so that undo surviving a same-page reload stays affordable to add later.
### What the first review changed (2026-08-04)

The first draft of this plan was reviewed by Fable (Claude) against the real source, and every
finding was independently verified before being accepted. The corrections themselves live in the
sections they concern; this list records *that* they came from review, and why, so a later session
does not reopen them. If you think one of these is wrong, say so explicitly rather than quietly
changing course.

- **Undo entries are data, not closures** (§4.1; relaxed to a preference by decision 7). The page iframe's JS context dies on same-page
  reloads too (ctrl+wheel zoom, origami exit), so page-id-scoped clearing alone would leave entries
  closing over a dead document. `canUndo()` stays synchronous and O(1) for the same section's
  reason: C# polls it on a timer.
- **Native browser undo is actively fenced** (§4.4, Risk 3): `preventDefault()` on `beforeinput`
  with `inputType` `historyUndo`/`historyRedo`. This also replaces
  `PreventRemovalOfSomeElements`'s `execCommand("undo")` — block the deletion instead of undoing it.
- **Stages 1–2 were re-cut** (§6). The draft spent Stage 1 converting three mechanisms that already
  worked and forced in-place snapshot restore to mature on a page full of live CKEditor instances,
  where restoring innerHTML orphans `div.bloomCkEditor` — and because `doCkEditorCleanup` iterates
  that expando, the save path would silently skip cleanup for restored divs. Stage 1 now wraps all
  four mechanisms unchanged; delete-page and delete-canvas-element move to Stage 2; `PageSnapshot`
  waits for Stage 3; conversions are optional Stage 4 cleanups. The draft's "convert the
  reader-tools undo" step was deleted outright: it is a text-typing undo, so converting it would
  need typing transactions first.
- **Delete canvas element is an inverse operation on a narrow subtree, not a page snapshot**
  (Stage 2b), reusing `refreshCanvasElementEditing`. Comical bubble-family re-linking and restoring
  a drag-activity target are the two things to verify first.
- **Delete-page capture happens inside the `SaveThen` callback** (moot: decision 7 drops delete-page undo), and restore re-raises the
  page-list events and navigates rather than just renumbering (Stage 2a).
- **`runUndoable` nests from day one** (§4.13; its rule is decision 6 above).
- **Selection anchors locate the editable structurally, not by `id`** (§4.3): ordinary
  `.bloom-editable` divs have none. The capture side is new code — the draft overstated what
  existed. Range anchors are deferred; snapshots use a caret marker in the captured *string*.
- **The toolbox keystroke pipeline is the one place "one-line dispatch" fails** (§5.4), hence the
  behaviour-preserving prep commit in Stage 0 that extracts the save/restore-selection bracket.
- **The flag is not a URL parameter** (§4.12): the page iframe''s `src` comes from C#.
- **Dependencies the draft missed, now in §2 and Stage 3:** the BL-13779 `change`-event
  `data-user-deleted` tracking, the BL-11745 qtip z-order juggling, `EnsureCaretNotInsideLineBreakSpan`
  on selection change, the SetupLink command; the second paste path (a selected but not text-edited
  canvas element) and the C#-initiated `pasteClipboard` entry; that BL-12357''s small-caps transform
  keys on CKEditor''s `cke/id` and may become moot; that jsdom cannot test the `beforeinput` layer
  (§7).
- **Cut:** byte-budget accounting on the stack (a count cap suffices); `focusOffset`/range support;
  the "delete-page depth" open question, answered instead (every deletion in the session, capped ~10).
- **Factual corrections, folded in:** `Equation-style` boxes *do* get an editor (§2); the toolbox
  undo is a per-editable text-typing undo, consulted before CKEditor on purpose (§3); origami''s undo
  is a jQuery `clone(true)` of DOM plus handlers, not an innerHTML snapshot (§3); `deleteCanvasElement`''s
  background-image branch already records an image undo, so wrapping it would double-record (Stage
  2b); support-file cleanup runs only from `BringBookUpToDate` and the publish paths, not on page
  save (Risk 7); the cross-frame export is `getWorkspaceBundleExports` (§4.2).

### Still genuinely open

- **Legacy cleanup lifetime** — leave the C# CKEditor-artifact scrubbers
  (`LegacyCkEditorCleanup`) in place indefinitely, or schedule a one-time book migration? Not
  urgent: nothing in Stages 0–5 depends on the answer, and keeping them is safe. Decide when Stage 5
  lands.

## 11. What BL-6681 itself says

Worth recording, because the ticket's title ("Remove ckeditor?") is much broader than its
original content, and a future reader will otherwise misjudge what it is asking for.

**It was opened in 2018 about one specific bug**, not as a general proposal: the talking-book
`audioCurrent` highlight would vanish, because CKEditor's load code asynchronously re-set a
`.bloom-editable` to its original value and clobbered the class another part of the code had
just written — last write wins (repro on BL-6654). Two mitigations were in place: setting the
highlight several times, and recovering by resetting it to the first element.

**That root cause is already gone — by rearchitecture, not by being fixed.** The highlight is
now tracked in `this.highlightedElement` / `AudioTextHighlightManager` rather than by marking the
DOM; `audioRecording.ts:2711-2715` only strips `ui-audioCurrent` defensively, for "*older Bloom
versions that used DOM marking*". So the ticket's founding symptom is **not** a driver for this
project, and nobody should go looking for it. Its surviving relative is `setHighlightSession`
(Stage 6).

**A 2018 comment observed** that CKEditor "is not designed at all to be able to handle
cross-iframe stuff", while Bloom's toolbox iframe must modify the editable-page iframe — and that
`onload` and its callbacks fire three times, once per frame, so the timing controls may be
waiting on the wrong events. Useful background for the Stage 6 page-load simplification.

**A 2018 comment asked for exactly the inventory in §2** — "*It would help to list just what
it's doing for us*", offering character formatting and paste safety and a question mark. §2 is
that list, eight years later, and it is longer than anyone expected.

**The 2026 comment is the load-bearing one** — the clipboard requirement, now §4.9 and §10 q3.
