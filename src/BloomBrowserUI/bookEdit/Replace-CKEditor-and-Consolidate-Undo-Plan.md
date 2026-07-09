# Plan: Retire CKEditor 4 and consolidate the Undo system

**Status:** proposed / not yet started. Branch: `retireCkEditor` (from `master`).

**Two intertwined goals:**
1. Remove Bloom's dependency on the old, self-hosted **CKEditor 4.5.1** (c. 2015), which
   has caused many problems.
2. Replace today's fragmented Undo mechanisms with **one Bloom-owned undo stack**, and
   start extending what can be undone — prioritizing *destructive* operations.

These are intertwined because CKEditor currently owns text-editing undo as a separate
silo, so replacing it is the natural moment to unify undo.

---

## 1. Priorities and constraints (from the request)

- **Top priority:** be able to Undo anything *destructive* — an operation that can lose
  data that isn't trivially recreatable. Canonical cases: **paste** (replaces a
  selection), and **choosing/replacing an image** (the old image could be lost
  completely).
- **Non-destructive additions don't need undo _infrastructure_:** e.g. adding an overlay
  is "undone" by just deleting it. We don't need to record those (though a consistent
  stack can still hold them cheaply — see §5).
- **Text editing is expected to be undoable** (users assume Ctrl+Z works while typing).
- **Cross-page undo is not required:** after switching pages we don't expect to undo
  changes to the previous page. Undo history may be **page-scoped** (cleared on page
  navigation), which matches today's image-undo behavior and greatly simplifies the design.
- **Nice to have:** undo **deleting a page**, and — once that works — **drop the
  "Really remove page?" warning** (the operation becomes recoverable).
- **Replacement must be free**, including for non-profits, with **no per-user tracking or
  per-user fees**. This rules out newer commercial CKEditor licensing.
- **Only one browser to support:** all Bloom editing runs in our own **WebView2
  (Chromium)**. We do **not** need cross-browser compatibility — this makes the
  "own-code" option far more attractive than it would normally be.

---

## 2. What CKEditor does for us today (and the coupling surface)

CKEditor 4.5.1 lives self-hosted (not npm) in `src/BloomBrowserUI/lib/ckeditor/` and is
loaded from C# (`Book.cs:629`) and the toolbox (`toolbox.pug:17`). It's a modified build
(auto-inlining was patched out — see `lib/ckeditor/patchNotes.txt`). Its responsibilities:

1. **Inline rich-text editing** on `bloom-editable` divs. Attached explicitly via
   `attachToCkEditor(element)` → `CKEDITOR.inline(element)`
   (`bookEdit/js/bloomEditing.ts:1853`, actual attach at `:1873`). The set of fields that
   get an editor is `ckeditableSelector` in `utils/shared.ts:16`. Note the browser already
   provides `contentEditable`; CKEditor sits on top of it.
2. **A floating format toolbar**, shown only when text is selected
   (`bloomEditing.ts:1879` `selectionCheck`; visibility hack via a `hideAllCKEditors` body
   class, BL-12448). Formatting a user can apply to a range: **Bold, Italic, Underline,
   Superscript, text Color** (colors pulled from Bloom's palette at
   `bloomEditing.ts:1946`), and **Hyperlinks** (custom `setupHyperlink` command +
   `SetupLink` button in `BloomField.ts:368-415`, plus the `autolink` plugin). Config is in
   `lib/ckeditor/config.js` (toolbar groups `:43`, `removeButtons` `:66`).
3. **Paste handling.** A whitelist `pasteFilter` (`config.js:112`) plus a substantial
   Bloom-owned pipeline in `BloomField.ts:265-328` (`on("paste")`/`on("afterPaste")`):
   `restoreHtmlMarkupIfNecessary` (color spans, BL-12357), `reconstituteParagraphsOnPlainTextPaste`
   (BL-9961), `convertStandardFormatVerseMarkersToSuperscript`, `fixPasteData`
   (Google-Docs bold, BL-8711), `removeUselessSpanMarkup` (BL-12861),
   `normalizeBloomLineBreakSpans`, audio-span handling, and first-`<p>` unwrapping.
   **Most of this is Bloom's own code and can be kept** — it just needs to hang off a
   native `paste` event instead of CKEditor's.
4. **Shift+Enter → line-break span** (`BloomField.ts:253`, BL-3009).
5. **Selection bookmarks** to preserve the caret across DOM rewrites during markup updates
   (`createBookmarks`/`selectBookmarks` in `editableDivUtils.ts:344-533` and
   `toolbox.ts:1495-1588`).
6. **Text undo** via CKEditor's internal `undoManager` (`editablePage.ts:305-329`).
   ⚠️ **Verify empirically:** `config.js:75` sets `undoStackSize = 0` ("required to
   prevent Bloom from crashing when the Undo button is clicked"), which appears to
   contradict the active use of `undoManager.undo()`. Determine before cutover whether
   text undo currently comes from CKEditor at all or from Chromium's native
   contentEditable undo.
7. **A large amount of artifact cleanup** that exists *only* because CKEditor inserts junk:
   - U+200B "filling chars": `editableDivUtils.removeCkEditorFillingChars` (`:462`),
     `doCkEditorCleanup` (`:344`), `audioRecording.cleanUpCkEditorHtml` (`:3599`).
   - nbsp / comments / empty paragraphs / bookmark spans: `toolbox.ts` `cleanUpNbsps`
     (`:1621`), `removeCommentsFromEditableHtml` (`:1713`), `setCkeditorBookmarkContent`
     (`:1694`); `editableDivUtils.fixUpEmptyishParagraphs` (`:471`).
   - The longpress ZWSP kludge (`lib/long-press/jquery.longpress.js:405-460`, whose own
     comment says "hopefully we can retire it when we retire CkEditor").
   - C# compensation: `XmlHtmlConverter.cs:132-151` (`<br>` before `</p>`, BL-2557);
     `HtmlDom.cs` `RemoveCkEditorMarkup` / `CleanupAnchorElements` (`:2252,2347-2383`);
     `BookData.cs:1055-1144` (`IsCkEditorBookmarkSpan`, empty-artifact divs);
     `BookProcessor.cs:193` strips the script for off-screen processing.
8. **Readiness coordination** (`doWhenCkEditorReady*` in `toolbox.ts:904-1009`, duplicated
   in `StyleEditor.ts:1185-1220`, plus `PlaceholderProvider.ts:60`) — needed because
   "CKEditor resets content to what it was initially" during init, wiping Bloom's page
   setup.

**The DOM contract to be aware of:** the codebase finds the editor via the
`element.bloomCkEditor` property (set at `BloomField.ts:419`), recognizes bookmark spans
by `id^="cke_bm_"`, and strips stray markup by class `cke_*`. A replacement must either
satisfy or let us delete these conventions.

Other event hooks currently riding on CKEditor that must be re-homed onto native events:
source-bubble focus/blur `passive-bubble` handling (`BloomField.ts:345-366`), qtip z-order
(BL-11745), StyleEditor's format-gear insertion (`StyleEditor.ts`), and
`EnsureCaretNotInsideLineBreakSpan` (`selectionChange`).

---

## 3. The Undo system today (why it needs consolidating)

There is **no unified undo stack**. `handleUndo()` / `canUndo()` in
`bookEdit/workspaceRoot.ts:96-125,247-265` poll four independent, mutually-exclusive
mechanisms in a fixed order and use the first that says it can undo:

| Mechanism | File | Active only when… | "Before" state | Survives page change | Redo |
|---|---|---|---|---|---|
| Origami | `js/origami.ts:258-307` | Change-Layout tool open | cloned `.marginBox` DOM subtree | no | yes |
| Reader tools | `toolbox/readers/readerToolsModel.ts:558-621` | a reader markup tool active | innerHTML string + caret offset | no | yes |
| Image op | `js/ImageUndoManager.ts` (whole file) | active element on an image container | in-memory metadata `{src, copyright, creator, license, crop}` | **no** (cleared on page change) | no |
| CKEditor text | `editablePage.ts:309-329` | a text box focused | CKEditor's internal per-instance undoManager | no | native |

Problems this creates:
- **Ctrl+Z and the Undo button take different paths.** The button routes React → POST →
  C# → JS → `handleUndo()` (`editTopBarControls.tsx:322` → `EditingViewApi.cs:410` →
  `bloomEditing.ts:1546` `topBarButtonClick` → router). But **Ctrl+Z is handled natively
  by Chromium/CKEditor** and never reaches the router — so Ctrl+Z effectively only does
  text undo, while origami/image/reader undo are reachable *only* via the button.
- **No ordering across mechanisms** — the code itself admits it (`workspaceRoot.ts:111`).
- **Enable-state is a third path:** a C# polling timer (`WebView2Browser.UpdateEditButtonsAsync`
  `:868`, `CanUndoAsync` `:910`) calls `canUndo()` and pushes `{undo,cut,copy,paste}` to
  the React top bar over a websocket (`EditingView.cs:1288`).
- **Destructive ops are largely NOT undoable today:** page deletion isn't; deleting an
  overlay isn't; pasting a *new* image element isn't (the `removeElement` kind in
  `ImageUndoManager` is commented out); replacing an image is undoable only on the same
  page while the image element stays active.

**Best existing building block:** `ImageUndoManager.ts` — a clean, **unit-tested**
(`ImageUndoManagerSpec.ts`), typed, two-phase (`prepare`/`commit`) command-record stack
with a host interface, page-change invalidation, and a discriminated-union item type that
already anticipates more kinds. **This is the seed for the consolidated manager.** There is
essentially no C# undo engine to build on (`Command.cs` is only a button-command
abstraction; there is no `IBrowser.Undo` despite a stale comment).

---

## 4. Replacement decision: library vs. own code

### Licensing of candidate libraries (all free, no per-user fees)
| Library | License | Notes |
|---|---|---|
| ProseMirror | MIT | Schema-based model; schema could *enforce* Bloom's allowed markup. |
| Lexical (Meta) | MIT | Modern, framework-ish; its own model + history. |
| Quill | BSD-3 | Fully free, no paid tiers. |
| Slate | MIT | Fully free. |
| TipTap | MIT (core) | Only the optional **cloud/collab platform** is paid; the editor library is free. |

All are acceptable on the licensing constraint. Newer **CKEditor 5** is GPL-or-commercial
with per-user commercial licensing → excluded per the request.

### Recommendation: **own code, operating directly on the existing contentEditable DOM, plus a Bloom-owned unified undo stack.**

Rationale specific to Bloom:
- **The unified-undo goal fights a library's built-in history.** ProseMirror/Lexical each
  own a document model and an internal undo history. Integrating that with a single
  Bloom-wide stack that also covers image-replace, paste, delete, and page-delete
  reproduces today's "text undo is a separate silo" problem. Owning the stack is the
  cleanest route to the stated top priority.
- **A library imposes a document model.** All of Bloom's other code — `audioRecording`
  (audio-sentence spans), reader tools, StyleEditor, and the C# side — reads and writes the
  **existing HTML DOM** directly. A library would require a bidirectional
  HTML↔model mapping layer for Bloom's bespoke markup (bloom-linebreak spans,
  audio-sentence ids, etc.), a large surface for subtle bugs.
- **Our editing needs are modest and Chromium-only.** Small fields; inline formatting
  (bold/italic/underline/superscript/color/hyperlink) + line breaks + paragraphs; no
  tables/lists inside editables. WebView2 gives us a stable, modern Selection/Range API and
  `contentEditable`. Most cross-browser pain a library would save us doesn't apply.
- **We already own the hard part (paste sanitization).** The paste pipeline is Bloom code;
  it only needs to hang off the native `paste` event.

**Keep the library option open** if the team prefers not to maintain editing primitives.
If a library is chosen, **ProseMirror** is the best fit because its schema can double as the
paste/content filter; plan to **disable its history plugin** and drive undo from Bloom's
unified stack. **De-risk the decision with a time-boxed spike (Phase 0).**

---

## 5. Target architecture

### 5.1 Editing layer (`BloomEditor`, replaces CKEditor)
A thin module attached per editable field (replacing `attachToCkEditor`/`WireToCKEditor`),
operating on the native `contentEditable` element:
- **Formatting commands** (bold, italic, underline, superscript, text color, hyperlink)
  implemented with the Selection/Range API, producing exactly the markup Bloom wants (no
  `cke_*` artifacts). Toggle by wrapping/unwrapping `<strong>/<em>/<u>/<sup>` and
  color `<span>`s. (`document.execCommand` still works in Chromium and is a viable
  interim; prefer explicit Range manipulation for control.)
- **A React/MUI floating format toolbar**, shown on non-empty selection (reuse the
  show/hide/position logic conceptually from `selectionCheck`, driven by the native
  `selectionchange`/`mouseup`). Bloom already uses MUI + Emotion everywhere, so this fits.
- **Line-break handling** (Shift+Enter → `span.bloom-linebreak`) and caret management, on
  native `keydown`.
- **Paste**: attach Bloom's existing pipeline (`BloomField` paste functions) to the native
  `paste` event; replace CKEditor's `pasteFilter` with an explicit sanitizer applied to the
  pasted fragment (the allowlist is small and already documented in `config.js:112`).
- **Selection preservation** across markup rewrites: ideally *unnecessary* — see §5.4. If
  any DOM-mutating markup remains during editing, use a small own-code save/restore
  (character-offset or marker-based) replacing `createBookmarks`/`selectBookmarks`.
- **Editor handle**: replace the `element.bloomCkEditor` convention with our own
  (e.g. `element.bloomEditor`) or a WeakMap.
- Re-home the focus/blur (source bubbles, qtip), selection, and StyleEditor-gear hooks onto
  native events.

### 5.2 Unified Undo (`UndoManager`, generalize `ImageUndoManager`)
- **One page-scoped stack** of undoable operations, each represented as a command record
  (discriminated union or objects carrying an `undo()`/optional `redo()` closure), reusing
  `ImageUndoManager`'s proven two-phase `prepare`/`commit` pattern so canceled operations
  never get recorded.
- **Register command kinds**, destructive-first:
  1. **Image replace/choose** — already implemented; fold in as the first kind.
  2. **Paste** — snapshot the affected field's HTML (and selection) before applying.
  3. **Delete canvas element / overlay** — snapshot the element's markup before removal
     (currently unrecoverable).
  4. **Text edit** — see §5.3.
  5. **Page delete** — snapshot the page XML before deletion (§7).
- **Route both the Undo button and Ctrl+Z through this one manager.** Add a global
  `keydown` handler (Ctrl+Z / Ctrl+Y) that calls the manager, eliminating the current
  button-vs-keyboard split. The button's existing round-trip can be simplified to call the
  same entry point.
- **Enable state**: keep the existing websocket/poll plumbing but have `canUndo()` query
  only the unified manager (optionally exposing a short description for tooltips).
- **Page-scoped**: clear on page navigation (matches the "no cross-page undo" constraint
  and today's image-undo behavior). Page-delete undo is the deliberate exception (§7).
- **Migrate origami and reader-tools undo** onto the same stack as their command kinds
  (they already snapshot DOM/innerHTML — wrap those as command records). This removes the
  four-way poll in `workspaceRoot.ts` entirely.

### 5.3 Text-edit undo — the key design question
Two viable approaches; pick during Phase 0:
- **(Recommended) Own text undo:** intercept Ctrl+Z and record coalesced per-field
  snapshots (debounced by typing pauses / word boundaries) as command records on the
  unified stack. Gives true consistency (text and destructive ops interleave correctly in
  one history) — the reader-tools stack already demonstrates innerHTML+offset snapshots.
- **(Pragmatic fallback) Keep Chromium's native in-field typing undo** for pure typing, and
  only push unified-stack restore points for destructive text ops (paste, replace). Less
  work, but reintroduces a smaller version of the native-vs-Bloom ordering seam. Acceptable
  only if the own-text-undo cost proves high in the spike.

### 5.4 Eliminate editing-time DOM mutation via the CSS Custom Highlight API (removes the need for bookmarks)

**This is the key to deleting the selection-bookmark machinery, not just replacing it.**

Today the caret/selection is lost during `updateMarkup` because Bloom **rewrites the
editable's DOM to show highlighting** — chiefly the reader tools, whose
`jquery.text-markup.ts` wraps words in `<span class="sight-word|possible-word|word-not-found"
data-segment="…">` and later `contents().unwrap()`s them. That churn is the whole reason
`createBookmarks`/`selectBookmarks` exist. (It's also why `jquery.text-markup.ts` itself
carries a CKEditor `display:none`-span regex at `:501`.)

Talking Book already pioneered the better approach:
`bookEdit/toolbox/talkingBook/audioTextHighlightManager.ts` (on the `improveHighlighting`
branch) paints highlights via the **CSS Custom Highlight API** — it builds `Range` objects
over the *existing* text and registers them in `CSS.highlights` with `::highlight()`
pseudo-elements, **without touching the DOM**. Its own comment notes this was adopted for
BL-15300 (the browser was hoisting computed CSS into inline styles inside a contenteditable)
and is "the direction we want to move in for highlighting."

**Plan: extend that strategy to the other editing-time highlighting** (reader-tools
decodability/sight-word coloring first; then audit remaining cases). Consequences:
- If **all purely-visual** editing highlighting is painted with `::highlight` instead of
  wrapper spans, `updateMarkup` no longer mutates the editable's text DOM, so the caret is
  never disturbed and **selection bookmarks can be removed outright** (§8) rather than
  reimplemented. This shrinks both the CKEditor coupling and the `updateMarkup` complexity.
- Distinguish **visual highlighting** from **structural markup**. Talking Book's
  audio-sentence segmentation adds `<span class="audio-sentence" id=…>` that *carries data*
  (ids tie text to audio files) — it is not mere highlighting and cannot be a `Range`. Where
  such structural re-markup still happens during typing, either defer it (segment on
  blur/commit rather than per keystroke) or keep a minimal caret save/restore just for that
  path. The goal is to shrink DOM-mutating markup as close to zero as possible during active
  editing.
- This also simplifies the new editor and the unified undo: fewer DOM rewrites means fewer
  points where text-edit undo snapshots and highlighting can interfere.

**What we keep vs. delete differs by kind of highlight:**
- **Talking Book highlighting — keep the old class + background-color CSS.** Published
  books play the audio, so **Bloom Player** renders these highlights from the existing
  class/background-color rules. Older players don't understand `::highlight`, so we keep
  those rules for publishing even as the Edit tab switches to pseudo-element painting (this
  is the compatibility note already in `audioTextHighlightManager.ts`).
- **Reader-tools error highlighting — delete the old span-wrapping and its CSS.** This
  decodability/sight-word coloring only ever appears **during editing**; it is never
  published. So once it's painted via `::highlight`, the old wrapper-span markup in
  `jquery.text-markup.ts` **and** its CSS can be removed outright — there is no
  publishing-compatibility reason to retain them (unlike Talking Book).
- **Whole-box error highlighting — keep as-is; it's out of scope here.** Some errors are
  shown by styling an entire element rather than wrapping text — e.g. drawing a rectangle
  around a text box (the `cssTooMuchStuffOnPage`-style class applied to the page/box div in
  `jquery.text-markup.ts:212`). Because these are element-level classes that don't wrap or
  split text nodes, they **don't affect the caret or text editing**, so they neither need
  migrating to `::highlight` nor threaten the "no bookmarks" goal. Leave them alone.

**Verify:** the CSS Custom Highlight API is available in our WebView2 (Chromium ≥105).

**Sequencing note:** this highlighting migration is largely independent of the CKEditor swap
and could land first (it has already begun for Talking Book). Doing it before Phase 2/3 means
the new editor is built in a world with little or no editing-time DOM mutation, making "no
bookmarks at all" the default rather than a later cleanup.

---

## 6. Phased plan

**Phase 0 — Decision spike (time-boxed).**
Confirm the own-code vs. library choice on a real field: prototype the floating toolbar +
2-3 formatting commands + native paste + a single unified undo entry covering
paste-and-image on one page. Empirically resolve the `undoStackSize=0` question (§2.6) so we
know what text undo relies on today. Output: a go/no-go on own-code and the §5.3 text-undo
decision.

**Phase 1 — Unified UndoManager (independent of CKEditor removal).**
Generalize `ImageUndoManager` into `UndoManager`; fold in image ops; add paste and
canvas-element-delete kinds; route the Undo button and add a global Ctrl+Z handler through
it; migrate origami and reader-tools undo onto it; collapse the `workspaceRoot.ts` four-way
poll. Keep CKEditor's text undo temporarily as one more registered kind so nothing
regresses. Extend `ImageUndoManagerSpec` coverage to the new kinds.

**Phase 1b — Migrate editing-time highlighting to the CSS Custom Highlight API (§5.4).**
Extend the Talking Book `AudioTextHighlightManager` approach to reader-tools coloring and
any other purely-visual editing highlight, so `updateMarkup` stops mutating the editable's
text DOM, then delete the now-obsolete reader-tools wrapper-span markup and CSS (edit-only,
so nothing to keep for publishing — unlike the Talking Book highlight CSS, which stays).
Leave whole-box error styling (e.g. the rectangle-around-a-box class) untouched. Can proceed
in parallel with (or ahead of) Phase 1; landing it before Phase 2 lets the new editor assume
a stable DOM and skip bookmarks entirely.

**Phase 2 — Build `BloomEditor` (the editing layer) behind the existing attach seam.**
Implement formatting commands, floating toolbar, line-break, selection save/restore, and
native paste (reusing Bloom's paste functions). Re-home focus/blur/selection/StyleEditor
hooks. Do not remove CKEditor yet; build to the same `attachToCkEditor` call sites
(`bloomEditing.ts:1206`, `CanvasElementManager.ts:951`, `imageDescription.tsx:342`).

**Phase 3 — Cut over.**
Swap `attachToCkEditor` → `attachBloomEditor`; remove `WireToCKEditor`; plug text-edit undo
into the unified manager (per §5.3); delete the CKEditor undo silo
(`ckeditorCanUndo`/`ckeditorUndo`). Verify the full editing matrix (below) before deleting
the library.

**Phase 4 — Remove CKEditor and its compensating cruft (see §8).**

**Phase 5 — Page-delete undo + drop the warning (see §7).**

Phases 1 and 2 are largely parallelizable. Phase 1 delivers value even before CKEditor is
gone (Ctrl+Z consistency, undoable deletes).

---

## 7. Page deletion: make it undoable, then drop the warning

Today: `confirmRemovePage.ts` shows a React "Really remove page?" dialog; on confirm,
`edit/pageControls/deletePage` → `EditingModel.OnDeletePage/DeletePage`
(`EditingModel.cs:578-618`) does a **full save** and mutates the book DOM via
`Book.DeletePage` (`Book.cs:4058`). Not undoable.

Plan:
1. Before deletion, capture the page's XML (and its index) as an undoable **page-delete
   command**. This is a C#-side operation, so this command kind must be reachable from the
   unified undo path — either by a small C# undo record that the manager can invoke via the
   API, or by having the JS manager call a C# `restoreDeletedPage(index, xml)` endpoint.
   (This is the one command that must outlive a page navigation, so treat it as a
   deliberate exception to page-scoping — a single "last deleted page" slot is enough.)
2. Wire it into the unified Undo (button + Ctrl+Z), with an appropriate description.
3. Once undo is reliable, **remove the `confirmRemovePage` warning** and just delete,
   relying on Undo for recovery (BL-16421 introduced the TS dialog; this reverses that
   trade-off now that recovery exists).

Note the interaction with the auto-save/`SaveThen` flow: page delete currently forces a
full save, so the undo record must be captured *before* that save and restoration must
re-insert and re-save. Validate this carefully.

---

## 8. What we can remove once the new code works

Track these as a checklist for Phase 4 (delete only after verifying the replacement no
longer produces the artifact each item cleaned up):

**The library and its loading**
- `src/BloomBrowserUI/lib/ckeditor/` (entire tree: `ckeditor.js`, `config.js`, `styles.js`,
  `adapters/`, `plugins/`, `lang/`, `skins/`).
- `Book.cs:629` (script injection); `toolbox.pug:17`; the `package.json:89-91` copy
  entries; `ProjectContext.cs:565` skin path; `BookProcessor.cs:193` strip logic;
  `typings/ckeditor/ckeditor.d.ts`.

**Readiness / init workarounds** (should become unnecessary — our editor won't reset
content on init)
- `doWhenCkEditorReady`/`doWhenCkEditorReadyCore`/`doWhenPageReady` (`toolbox.ts:904-1009`)
  and their callers; the duplicated readiness logic in `StyleEditor.ts:1185-1220`; the
  `PlaceholderProvider.ts:60-75` `instanceReady` race.

**Artifact cleanup** (verify our editor doesn't produce the artifact, then delete)
- U+200B filling-char code: `editableDivUtils.removeCkEditorFillingChars` (`:462`),
  `doCkEditorCleanup` (`:344`), `safelyReplaceContentWithCkEditorData` (`:392`);
  `audioRecording.cleanUpCkEditorHtml` (`:3599`).
- nbsp/comment/empty-paragraph cleanup: `toolbox.ts` `cleanUpNbsps` (`:1621`),
  `NbspIsOnEdgeOfParagraph` (`:1597`), `removeCommentsFromEditableHtml` (`:1713`),
  `setCkeditorBookmarkContent` (`:1694`); `editableDivUtils.fixUpEmptyishParagraphs` (`:471`).
- Selection bookmarks: `createBookmarks`/`selectBookmarks`/`isNodeCkEditorBookmark`/
  `restoreSelectionFromCkEditorBookmarks` (`editableDivUtils.ts:344-533`) and the
  bookmark usage in `toolbox.ts:1495-1588`. **Deletable outright once §5.4 lands** (no
  editing-time DOM mutation → nothing to preserve the caret against); otherwise replace
  with a minimal own save/restore.
- The longpress ZWSP kludge (`lib/long-press/jquery.longpress.js:405-460`).
- The `.bloomCkEditor` property convention and `cke_*` / `cke_bm_` recognition everywhere.

**Undo silo**
- `ckeditorCanUndo`/`ckeditorUndo` (`editablePage.ts:309-329`) and their exports; the
  CKEditor branch of the (by then removed) undo router.

**C# compensation** (verify per item — some may still be wanted if our editor or Chromium
introduces the same artifact)
- `XmlHtmlConverter.cs:132-151` (`<br>` before `</p>`, BL-2557) — verify.
- `HtmlDom.RemoveCkEditorMarkup` / `CleanupAnchorElements` (`:2252,2347-2383`) — drop once
  no `cke_*` classes or `data-cke-saved-href` appear.
- `BookData.cs:1055-1144` `IsCkEditorBookmarkSpan` and empty-artifact-div removal — verify
  whether any remain relevant to our markup.

**Config semantics that disappear with CKEditor:** `undoStackSize=0` crash workaround,
`removeButtons`, `pasteFilter`, `allowedContent`, `disableNativeSpellChecker` (re-provide
`spellcheck="false"` ourselves if still wanted, BL-12205), `floatSpacePreferRight`.

---

## 9. Risks and things to verify

- **`undoStackSize=0` paradox** (§2.6): determine what text undo actually relies on before
  removing anything.
- **Selection/caret fidelity:** CKEditor's bookmarks are battle-tested against markup
  rewrites during `updateMarkup` (reader tools, audio-sentence markup). The best fix is to
  **stop rewriting the DOM** (§5.4) so there is nothing to preserve the caret against; audit
  every remaining editing-time DOM mutation before assuming bookmarks can be dropped. For
  any structural re-markup that must still happen while editing, respect the
  `toolbox.ts:1495-1588` async markup flow and its BL-10133 constraint that
  `updateMarkupAsync` must not mutate the DOM except via the returned commit function.
- **Paste parity:** the paste pipeline has many YouTrack-driven special cases
  (BL-12357, BL-9961, BL-8711, BL-12861, verse markers, audio spans). Keep them; add
  regression tests. The pipeline is unit-testable (`BloomField` paste methods already are).
- **Spellcheck:** CKEditor set `spellcheck="false"` (BL-12205). Reproduce on our fields.
- **IME/composition & longpress:** validate composed input and the longpress diacritic UI
  once the ZWSP kludge is gone.
- **Off-screen processing:** `BookProcessor` strips the CKEditor script so bootstrap skips
  attachment; ensure our editor likewise never attaches during headless processing.
- **StyleEditor gear** and **source bubbles**: reproduce their focus/selection behavior on
  native events.
- **Hyperlinks:** reproduce the `setupHyperlink` command + autolink behavior.

---

## 10. Testing

- **Unit:** extend `ImageUndoManagerSpec.ts` into the unified manager's spec (paste,
  delete, text, page-delete kinds); keep/extend `BloomField` paste specs; keep
  `editableDivUtilsSpec.ts` where still relevant.
- **Manual editing matrix:** type/edit in each field type (content1/2/3, national,
  Equation), Shift+Enter line breaks, apply each format (bold/italic/underline/superscript/
  color) to a range, insert a hyperlink, paste from Word/Google Docs/plain text, paste with
  audio present (Sentence and TextBox modes), longpress diacritics, StyleEditor gear, source
  bubbles.
- **Undo matrix (button *and* Ctrl+Z, which must now behave identically):** undo a paste,
  an image replace, an overlay delete, a text edit, and a page delete; confirm history
  clears on page change (except the page-delete slot); confirm Undo enable-state in the top
  bar tracks correctly.
- Use the `run-bloom` skill to drive the desktop app for manual verification.

---

## Sources (library licensing, verified July 2026)

- [Tiptap vs Quill vs Lexical vs Slate (PkgPulse, 2026)](https://www.pkgpulse.com/guides/tiptap-vs-lexical-vs-slate-vs-quill-rich-text-editor-2026)
- [Tiptap product/editor page](https://tiptap.dev/product/editor)
- [Top rich text editors for developers 2026 (Eddyter)](https://eddyter.com/blogs/top-rich-text-editors-for-web-developers-2026)
