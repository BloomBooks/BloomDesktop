# Retiring CKEditor from Bloom's edit mode (BL-6681)

**Status:** plan, reviewed once (see [REVIEW-NOTES.md](REVIEW-NOTES.md)), BL-6681's own content
folded in (§11), not yet started. Live state in [PROGRESS.md](PROGRESS.md).

## 1. Goals

1. **Remove CKEditor 4** (a 2015-era, hand-patched, 1.5 MB vendored copy) from Bloom's edit
   mode, replacing it with our own code. No replacement library.
2. **Give Bloom one consistent Undo stack.** Today there are five poorly-coordinated undo
   mechanisms. We want a single, ordered stack covering changes to the current page, plus
   "undo delete page" as the one deliberate cross-page exception. Priority is on operations
   that are *hard to reverse by hand* (delete a canvas element, delete a page) over ones that
   are easy (add a canvas element — just delete it).
3. **Simplify page loading and toolbox init**, most of whose complexity exists only to work
   around CKEditor mutating the DOM asynchronously during startup.
4. Do it in a way that survives **many rebases** over a long calendar period.

Non-goals (out of scope, but the design must not obstruct them): widening undo beyond the current
page; undo across a Bloom restart; a rich-text editor usable outside Bloom's edit mode; the C#
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
| `origamiCanUndo`/`origamiUndo` (`origami.ts:262-294`) | A stack of **jQuery `clone(true)` copies of `.marginBox`** — DOM plus attached handlers and data — restored with `replaceWith` | Only while Change Layout mode is active. Has its **own** `keydown.origami` Ctrl+Z/Ctrl+Y handler on `html` (`origami.ts:139-146`), and its own Redo. Safe today partly *because* layout mode strips `contentEditable` (`origami.ts:132`), so there are no live CKEditor instances to orphan. |
| `toolboxWindow.canUndo/undo` → `readerToolsModel` | A per-editable **text-typing** undo: `{html, text, caretOffset}` snapshots, seeded on focus (`noteFocus`, :557-568, from `decodableReaderTool.tsx:155`) and pushed on every markup-changing keystroke inside `doMarkup` (:753-764) | Gated on `shouldHandleUndo()` — `currentMarkupType !== None` (:570). It is consulted *before* CKEditor **deliberately**: when a reader tool is active it must shadow CKEditor's undo, which would restore stale decodable/leveled markup. Not "reader-setup changes". |
| `imageOperationCanUndo`/`imageOperationUndo` (`ImageUndoManager.ts`) | Restores an image's `src` / copyright / crop | Clean two-phase prepare/commit; already page-id-scoped; gated on the active element being an image container. |
| `ckeditorCanUndo`/`ckeditorUndo` | `CKEDITOR.currentInstance.undoManager`, **per editable div** | An "implementation secret". Ordering across boxes is already wrong. |
| Browser-native undo | Invisible | Called directly in `BloomField.PreventRemovalOfSomeElements` (`BloomField.ts:810-825`); also fed implicitly by every `document.execCommand("insertHTML"/"formatBlock"/"justify*"/"insertText")` in `bloomEditing.ts` and `GamePromptDialog.tsx`, and by plain typing in any contenteditable. |

Two corrections to the folklore:
- `workspaceRoot.ts:125`'s "*See also Browser.Undo; if all else fails we ask the C# browser
  object to Undo*" is **stale** — no such fallback exists in the WebView2 code. The Undo
  button's enabled state comes purely from `workspaceBundle.canUndo()` returning `"yes"`
  (`WebView2Browser.CanUndoAsync`, polled on a timer from `UpdateEditButtonsAsync`).
- `config.undoStackSize = 0` in `config.js` claims to prevent a crash; CKEditor 4 reads
  `config.undoStackSize || 20`, so it silently means 20. Don't preserve the intent blindly.

**Not undoable at all today:** deleting a canvas element, deleting a page, style changes, most
toolbox operations — i.e. precisely the hard-to-reverse things.

## 4. Design decisions

### 4.1 Undo entries are data, interpreted at undo time

Two architectures were considered: a command/inverse-op stack (precise, memory-light, but every
operation must be taught to undo itself) and a snapshot stack (uniform, covers operations
nobody enumerated). **Use snapshots as the default entry type, with inverse-op entries where a
snapshot is too blunt.**

The critical constraint, which shapes the contract: the page iframe's JS context dies not only
on page *change* but on same-page **reloads** — ctrl+wheel zoom regenerates the page
(`bloomEditing.ts:1268`), origami exit posts `saveChangesAndRethinkPageEvent`
(`origami.ts:193`), and several tools navigate. An entry that closes over page-frame DOM or
functions therefore becomes a live grenade: `undo()` would mutate a detached document or throw.

So **snapshot entries must be pure data**, interpreted at undo time by a restore function that
re-acquires the current page frame via `getEditablePageBundleExports()`:

```ts
export interface IUndoEntry {
    label: string;                  // "Delete canvas element" — tooltips, logging
    pageId: string | undefined;     // undefined = survives page change (delete-page)
    kind: "pageSnapshot" | "subtreeSnapshot" | "custom";
    undo(): void | Promise<void>;   // for "custom" only; snapshot kinds carry data instead
    redo?(): void | Promise<void>;
}
```

Closure-bearing (`custom`) entries are permitted only for **workspace-owned** operations —
delete-page being the main one — never for page-frame DOM. In addition to clearing page-scoped
entries when the page id changes, **re-validate or clear on page-frame unload/load**;
`switchContentPage` (`workspaceRoot.ts:135-186`) already has the hook points.

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
  floor — `canRedo()` is false when the next entry can't redo. That lets delete-page redo (the one
  case needing real C# work: deleting the page again) be deferred without blocking the rest.

`canUndo()` must stay **synchronous and cheap** — C# polls it on a timer
(`WebView2Browser.cs:963-996`, with a reentrancy guard that returns `true` on overlap). A
`canUndo` that walks entries or touches layout will make the Undo button flicker.

### 4.2 The stack lives in the workspace frame

The page iframe is destroyed on page change; the workspace frame is not. So `theOneUndoStack`
lives in the **workspace** bundle alongside `handleUndo`, and delete-page entries
(`pageId: undefined`) survive naturally rather than being bolted on. Page-frame code pushes via
the established cross-frame pattern (`getWorkspaceBundleExports().pushUndoEntry(...)` — note
the real export name, see `origami.ts:204`).

Corollary for delete-page: the entry **must not** be constructed in the page frame, which is
being torn down at that moment. C# initiates the delete, so C# (or the workspace frame on C#'s
behalf) pushes the entry.

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

Why this beats bookmarks: `toolbox.ts:1521-1528` documents the bookmark approach's own bug —
inserting a marker mid-word makes the markup routine see `"hous"`-marker-`"e"`, so
decodable/leveled reader markup is temporarily wrong while you fix a letter. An offset-based
anchor doesn't perturb the DOM, so **dropping bookmarks fixes that bug**, and it deletes the
whole `fixUpEmptyishParagraphs` / `safelyReplaceContentWithCkEditorData` /
`setCkeditorBookmarkContent` / `cleanUpNbsps`-bookmark-emptying family plus the `cke_bm_`
scrubbing on the C# side.

Defer range (non-collapsed) anchors: every identified consumer needs only a caret. And for the
*snapshot* case specifically there's a simpler trick — inject a caret marker into the captured
HTML **string** (not the live DOM, so none of the bookmark downsides apply) and strip it on
restore. Offsets then only have to serve the toolbox-markup case.

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
`historyUndo`/`historyRedo`, `preventDefault()`, and route to the shared stack. This is
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
(space / punctuation / Enter), a caret move or focus change, an idle timeout (~1 s), or any
non-typing command. This approximates CKEditor's `undoManager` and matches user expectation. A
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
  delete-page is a C# mechanism, style undo is deferred. So no generic full-page restore gets
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
- **Delete page** — C#-side, and navigation happens regardless because the page list changes. A
  different mechanism entirely (Stage 2a), not a page snapshot.
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
half-undoes. Nested wrapping will keep happening as call sites accrete, so specify the
semantics in `undoTypes.ts` up front: a depth counter, outermost entry wins, inner pushes are
no-ops.

## 5. Rebase strategy

1. **Almost all new code in new directories** — `src/BloomBrowserUI/bookEdit/undo/` and
   `src/BloomBrowserUI/bookEdit/textEditor/`. New files never conflict.
2. **Don't keep a long-lived branch.** The real defence against repeated rebasing is not to
   rebase: land a dozen small PRs on `master`, each green, each inert behind a flag.
3. **Integration points into existing files are one-line dispatches** wherever possible:

   ```ts
   export function attachToCkEditor(element) {
       if (useNewTextEditor()) return attachBloomTextEditor(element);   // ← the whole edit
       ...existing body unchanged...
   }
   ```
   Note the dispatch goes *inside* `attachToCkEditor`, so its two call sites (`bloomEditing.ts:1226`,
   `CanvasElementManager.ts:951`) need no edit at all.
4. **One exception, and it needs a prep commit.** The toolbox keystroke pipeline
   (`toolbox.ts:1509-1607`) interleaves `createBookmarks`, `removeCommentsFromEditableHtml`, the
   async-updateMarkup double-bookmark dance (BL-10133), `cleanUpNbsps`, and `selectBookmarks`.
   Swapping bookmarks for anchors there rewrites ~100 lines of the most delicate keystroke code
   in the app, inside a churn-prone file. So do a **mechanical, behaviour-preserving prep commit
   early** (Stage 0): extract the save-selection / restore-selection bracket into two small
   functions with a clean seam. Then the eventual change swaps one function body instead of
   performing open-heart surgery mid-project.
5. **The flag is read in exactly one function**, `useNewTextEditor()`, in one new file — a
   synchronous body-class check, set by C# at page-generation time from an
   `ExperimentalFeatures` token (with an env-var override). See **§4.12** for why, and for what
   falls out of it.
6. **All deletion is last** (Stage 5), in a few mechanical commits. Never rebase those —
   regenerate them.
7. **Avoid the churn-prone files** until late: `bloomEditing.ts` (2092 lines),
   `CanvasElementManager.ts` (3224), `toolbox.ts`, `audioRecording.ts` (5121),
   `StyleEditor.ts` (2627).
8. Keep [PROGRESS.md](PROGRESS.md) current so an interrupted session resumes cleanly.

## 6. Stages

Stages 1–2 deliver the Undo improvements **without touching CKEditor at all**, and are ordered
by user value per unit of risk. If the project stalls, Bloom is still better off.

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
- **The toolbox prep commit** from §5.4.
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

*New:* `bookEdit/undo/UndoStack.ts`, `undoTypes.ts`, `runUndoable.ts`, plus specs.

- `UndoStack` in the workspace bundle: push / undo / **redo** / canUndo / **canRedo** /
  clearForPage / clearOnPageFrameReload. Index-based with truncate-on-push (§4.1), count-bounded,
  `canUndo` and `canRedo` both O(1).
- `workspaceRoot.canUndo`/`handleUndo` become thin delegations (two small edits, one file). Redo
  needs no C# counterpart — it is reached only by Ctrl+Y (§10 q1), so it stays entirely in JS.
- **Wrap all four existing mechanisms as legacy providers in their current priority order.**
  No conversions, no behaviour change. This preserves the deliberate reader-tools-before-CKEditor
  precedence (§3) for free. Redo has no legacy providers to wrap — origami's is the only Redo that
  exists, and it keeps working via its own handler until Stage 4 converts it. (Note
  `readerToolsModel.redo()` at `:609` appears to be **unreachable** — nothing exports or calls it;
  worth a moment's check, but it is deleted in Stage 5 regardless.)
- `runUndoable(label, fn)` with the nesting semantics of §4.13.

Rationale for doing *no* conversions here: the four existing mechanisms are contextually
exclusive in practice (origami only in layout mode, reader undo only with an active markup tool,
image undo only on an image container), so their relative *ordering* only starts to matter once
text edits enter the shared stack — which is Stage 3. Converting them now would mean maturing
the riskiest new machinery (in-place snapshot restore) in the worst possible environment: a page
with live CKEditor instances (see Stage 3's note on `reinitializePageAfterRestore`).

Exit criteria: one entry point; `pnpm test` green; no user-visible change.

### Stage 2 — The undos the user actually wants

**2a — Undo delete page.** The highest value-per-risk item in the plan; independent of
CKEditor, of the page frame, and of snapshot restore. Can ship even before Stage 1 settles.
- *New C# file* `src/BloomExe/Edit/DeletedPageUndoManager.cs`: a session-only stack of
  `{ pageXml, index, pageId }`, plus an `edit/undoDeletePage` endpoint.
- Capture **inside the `SaveThen` callback, after the save completes** — `EditingModel.DeletePage`
  wraps the delete in `SaveThen(..., forceFullSave: true)` (`EditingModel.cs:590-624`), so
  capturing earlier would snapshot a page missing the user's last edits.
- Restore must mirror what `Book.DeletePage` (`Book.cs:4110-4132`) tears down: re-insert at the
  saved index (clamped to the current page count), then `UpdatePageNumberAndSideClassOfPages`,
  `_pageListChangedEvent.Raise`, `InvokeContentsChanged`, and navigate to the restored page.
- The matching front-end entry (`pageId: undefined`, `kind: "custom"`) is pushed **by C# into
  the workspace bundle**, not by the page frame — which is being torn down at that moment.
- Depth: keep every deletion in the session (capped ~10). The shared stack already provides
  ordering, so depth costs nothing extra.

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

**2c** *(deferred, documented not built)*: undo for style changes — a snapshot of
`userModifiedStyles` would cover it, and the entry contract already allows it.

Exit criteria: deleting a page and deleting a canvas element are both undoable; page
renumbering and navigation are correct after undo; exactly one entry per gesture.

### Stage 3 — The new text editor, behind a flag, off by default

*New directory* `bookEdit/textEditor/`, built roughly in this order:

| File | What |
| --- | --- |
| `inlineFormat.ts` | Pure `Range`→DOM formatting engine: bold, italic, underline, superscript, colour, remove-format. **Do this first and test it hard.** Must preserve structural spans (`audio-sentence`, `bloom-highlightSegment`, `bloom-linebreak`) exactly as today's `addRemoveFormatFilter` does. |
| `selectionApi.ts` | `getSelectionAnchor` / `restoreSelectionAnchor` (§4.3 — the capture side is new code), `getCleanHtml(div)`. |
| `pasteSanitizer.ts` | **Default-deny allow-list** replacing `config.pasteFilter` (§4.8) — the project's main safety guarantee, applied to **both paste and drop**. Pure function, so it can be tested adversarially. Build it early (right after `inlineFormat.ts`) rather than late: it is the one piece whose absence is silent. |
| `clipboard.ts` | Owns `cut` and `copy` on `.bloom-editable`, replacing CKEditor's interception (service 13). Produces the payload as **both** `text/html` and `text/plain` and keeps the write behind one seam, so a safe cut (§4.9) becomes possible. Read `origin/BL-16459-clipboard-failure-reporting` and PR #8140 first. Also subsumes `bloomEditing.cutSelectionImpl`, which currently uses `undoManager.lock/save` to make the cut one undo step. |
| `pasteHandler.ts` | Owns the `paste` event **and** the C#-initiated `pasteClipboard` entry point. Must cover *both* existing paths: normal insert-at-selection, and `pasteImpl`'s replace-whole-content path for a canvas element that is selected but not being text-edited (`bloomEditing.ts:1792-1842`: `setData("<p><p>")` + `insertText` under an undo lock, then `updateAutoHeight()` + `scheduleMarkupUpdateAfterPaste()`). Calls the BloomField transforms; pushes **one** undo entry. |
| `typingTransactions.ts` | `beforeinput`-driven coalescing (§4.6), composition-aware, plus the `historyUndo`/`historyRedo` fence (§4.4). |
| `keyCommands.ts` | Shift+Enter → `span.bloom-linebreak`; F6/F7/F8; Ctrl+Alt+0/1/2; justify; Ctrl+Space (remove-format); Ctrl+B/I/U. Replaces every `execCommand` call **in the page frame** (`readerSetup.ui.ts:454` lives in the reader-setup dialog and is out of scope). |
| `autolink.ts` | Word-boundary URL detection (BL-6845). |
| `FormatToolbar.tsx` | React floating toolbar replacing `.cke_float`, positioned from the selection rect, localized directly (so `localizeCkeditorTooltips` dies), hidden for `bloom-userCannotModifyStyles` (BL-14947). Hosts the SetupLink hyperlink button. |
| `BloomTextEditor.ts` | Per-editable attach/detach. **Synchronous** — no `instanceReady`, no async DOM rewrite. Also owns the BL-13779 content-changed hook, the BL-11745 qtip z-order handling, and `EnsureCaretNotInsideLineBreakSpan` on `selectionchange`. |
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
Everything else in the paste pipeline (verse markers, audio-id copying, `<p>` unwrapping) moves
unchanged.

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
- **C# tests** through `build/agent-dotnet.sh` for `DeletedPageUndoManager` and
  `LegacyCkEditorCleanup`.
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
   existing `refreshCanvasElementEditing`; origami keeps its own working clone restore; delete-page
   is a separate C# mechanism. Nothing left needs a generic full-page reinit, so **don't build
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
   disk. Page-scoped clearing and page-frame-reload invalidation must be exactly right. The
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
9. **Cross-frame lifetime.** Settled in §4.1–4.2 (data-not-closure entries, invalidate on
   page-frame reload, C# pushes the delete-page entry). Keep it settled.

## 9. Follow-ups this design makes cheap

- **A visible Redo button.** Redo itself is in scope, but Ctrl+Y-only (§10 q1). A toolbar button is
  where the remaining cost sits: there is **no Redo plumbing in C# at all** today — no
  `RedoCommand`, no `SetEditingCommands` parameter, nothing in the `updateEditButtons` websocket
  payload, no icon, no XLF entry. All of that is separable and can be added later without touching
  the stack.
- **Undo labels in the UI** — "Undo Delete Page" as the button tooltip.
- **Wider undo scope** — style changes, book-level operations, multi-page undo all plug in as new
  entry types without touching the stack.

## 10. Decisions

Everything here is settled. Recorded with the reasoning so a later session doesn't reopen it; see
also [REVIEW-NOTES.md](REVIEW-NOTES.md).

1. **Redo: in scope, extended rather than dropped — but Ctrl+Y only, no toolbar button.**
   Origami has a Redo today, and removing it while unifying the stacks would be a small regression
   for layout-mode users. The cost is small provided we take the two cheap routes in §4.1: an
   index-based stack, and capturing the redo state lazily at undo time (origami's existing trick),
   so nothing is paid per keystroke. Ctrl+Y is a JS-only key handler, matching origami's current
   affordance exactly and needing **zero C# plumbing** — which matters, because Bloom has no Redo
   plumbing whatsoever today (no `RedoCommand`, no `updateEditButtons` field, no icon, no XLF
   entry). A visible button is deferred to §9 and can arrive later without touching the stack.
   `redo?()` stays optional, so delete-page redo — the one case needing real C# work — can also come
   later, acting as a redo floor until it does.
2. **Hyperlink UI: keep the current `showLinkTargetChooserDialog` flow exactly.** The new
   `FormatToolbar.tsx` hosts the same button invoking the same dialog. No behaviour change.
3. **Clipboard (BL-16459): seam only.** `clipboard.ts` produces rich **and** plain payloads and
   keeps the write behind one interface, but still writes from JS, so a genuinely safe cut remains
   impossible and BL-16459 stays open — much cheaper to close later, because the seam is the part
   that is expensive to retrofit (§4.9). Explicitly **not** doing the C# multi-format write or the
   "HTML Format" byte-offset header in this project.
4. **Delete-page undo keeps every deletion in the session**, capped at ~10 (§6 Stage 2a).
5. **The flag** is an `ExperimentalFeatures` checkbox in Collection Settings → Advanced plus a
   `BLOOM_NEW_TEXT_EDITOR` env-var override, latched into the page by a body class (§4.12). Its XLF
   entry is `translate="no"`, so removal is free — **but must happen before that release goes beta**,
   the project's one hard calendar deadline. Deliberately *not* clearing the obsolete token from
   testers' settings.

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
