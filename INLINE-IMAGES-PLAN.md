# Inline (Word-style) images in Bloom text blocks

## Context

Bloom text blocks (translation groups) currently cannot contain images the way Word paragraphs can: docked left or right with text wrapping around, or as a band with text above and below. Users producing textbook-style material (e.g. the SIL LEAD Uganda P4 books) have hand-hacked this with custom templates. We want a first-class feature: an image inside a text block, docked left / right / middle band / bottom, draggable and resizable, language-neutral in effect (every language version of the block shows the same image), never leaking into source bubbles.

**Chosen approach (with John):** the image markup lives *inside each `bloom-editable`* (the only way CSS floats can wrap text), replicated per language and kept in sync by edit-time JS. The copy in the first-visible-language editable is canonical; `normalizeInlineImages()` stamps it to siblings at page load and after every edit.

**Position is geometry, never DOM anchoring.** Cross-language sync forces this: languages differ in text and paragraph structure, so "after the second paragraph" cannot be translated across sibling editables, but "docked right, offset 120px, width 40%" renders identically in every language copy. The wrapper therefore never moves within the text. It is always the FIRST child of the editable (LAST child for bottom dock, the one DOM move). Dragging changes only a class and two numbers.

**Why per-editable copies rather than a canonical TG-child element:**
- New-language creation already works with **zero C# changes**: `MakeElementWithLanguageForOneGroup` + `StripOutText` clone an embedded image div into new language editables, text stripped (proven by `TranslationGroupManagerTests.cs:900`).
- No lang-less TG child means we never arm the trap in `TranslationGroupManager.cs:804` (deletes lang-less `<div>` direct children of a TG on every page load), and old Bloom versions opening the book can't destroy a canonical element, because there isn't one.
- No book-format structural change; old Bloom / bloom-player / ePub readers render the markup inertly with shipped CSS.
- Sibling editables are separate block formatting contexts (flex items), so even a TG-level image could never be wrapped by more than one language's text without per-editable spacer injection and baked geometry, which reflow (ePub) breaks. TG-level storage buys no rendering ability; ruled out for v1. Upgrade seam if ever needed: `data-*` attributes on the TG (attributes survive all C# sweeps), and the canonical copy converts mechanically.

## Bilingual / trilingual behavior (designed v1 behavior, not deferred)

Showing the same image in each visible language block looks broken. Rule: the image renders only in the **first visible language block**; the editable showing it contains its float (`display: flow-root` or equivalent) so lower language blocks start cleanly below the image at full width. CSS hook: prefer `.bloom-contentFirst` (maintained by `TranslationGroupManager.AddThemeVisibleOrderClass`), falling back to `.bloom-content1` where contentFirst isn't assigned (same preference logic as `bloomEditing.ts:411`). All copies stay in the DOM in all languages; this is display-only.

## Key existing machinery (reuse, don't rebuild)

- `src/BloomBrowserUI/bookEdit/bloomField/BloomField.ts`: `bloom-keepFirstInField` (image stays first in field, `EnsureParagraphsPresent` puts the required `<p>` after it), `bloom-preventRemoval` (undo-based ctrl+a+Del protection), backspace-at-start guard. Demo page: `src/BloomBrowserUI/bookEdit/bloomField/test.pug:93-105`.
- `src/BloomBrowserUI/bookEdit/js/bloomImages.ts`: `doImageCommand(img, "change")` works on any `<img>` (element-reference + temp-id round trip, location-agnostic); `GetRawImageUrl`, `isPlaceHolderImage`, metadata dialog.
- `contenteditable=false` islands inside CKEditor-managed editables are proven safe: the format cog (`StyleEditor.ts:1264`) is exactly that. The old guard that supposedly disabled CKEditor for embedded-image fields (`bloomEditing.ts:1216`, BL-3125) is dead code (`$(this)` in `bootstrap()` is an empty set); delete it as part of this work.
- Canvas element context menu (`CanvasElementContextControls.tsx`, `canvasControlRegistry`) is the UI pattern for the right-click menu; `CanvasElementHandleDragInteractions.ts` has the pointer-math patterns for drag/resize (ours is far simpler: one class, one offset, one width).

## Markup (new class; do NOT reuse `bloom-imageContainer`)

```html
<div class="bloom-editable ..." lang="en" contenteditable="true">
  <div class="bloom-inlineImage bloom-inlineImageRight bloom-keepFirstInField bloom-preventRemoval"
       contenteditable="false" style="--inline-image-offset: 120px; width: 40%; aspect-ratio: 800 / 600;">
    <img src="flower.jpg" data-copyright="..." data-license="..."/>
  </div>
  <p>Text that wraps…</p>
</div>
```

- Dock modifiers: `bloom-inlineImageLeft` / `bloom-inlineImageRight` / `bloom-inlineImageMiddle` (full-width band, image centered, text above and below) / `bloom-inlineImageBottom` (wrapper is last child, normal flow).
- Vertical offset (left/right/middle docks) = `--inline-image-offset`, realized with `padding-top` + `shape-outside: inset(...)`; see CSS.
- Size = inline `width` (% of editable) + `aspect-ratio` from natural dimensions (stable layout before image load; keeps OverflowChecker honest).
- The new class dodges `BookStorage.MigrateToLevel7BloomCanvas` (matches only `bloom-imageContainer`) and `SetupImagesInContainer`'s selectors (`bloomImages.ts:117`). Never prefix inline-image classes with `bloom-content` (the `UpdateContentLanguageClasses` sweep strips those).
- Locked in by C# tests (see Tests): the class sweep visits the wrapper on every page load and strips any `bloom-visibility-code*`/`bloom-content*` class and `dir` attribute from it, so the wrapper must never rely on those; and `contenteditable="false"` in the PERSISTED DOM is load-bearing, since it is the only thing keeping the lang-stamping sweep (`TranslationGroupManager.cs:816`) from treating the wrapper as an editable. Edit-time code must never save it as `true`.
- No limit on the number of inline images per translation group (requirement from live testing 2026-08-05; replaces the earlier one-image v1 rule). Each wrapper carries a persistent id attribute so sync/undo can match copies across editables. Floating-dock wrappers cluster in order at the top of the editable; bottom-docked ones at the end.

## CSS: shape-outside, not the legacy pusher-downer

Decision (John): give no weight to the old `imagePusherDowner` two-element trick; that feature is no longer supported. `shape-outside` (all engines since ~2018; every renderer we control is current Chromium) is the mechanism. The wrapper's own box includes the offset as transparent padding; the shape excludes only where the image is, so text flows through the padding at full width:

```less
// → src/content/bookLayout/basePage-sharedRules.less
// (imported by basePage.less AND publish/ePUBPublish/baseEPUB.less:48 —
//  covers edit, PDF, bloom-player, ePub with zero publish-code changes)
.bloom-editable .bloom-inlineImage {
    --inline-image-offset: 0px;
    img { display: block; width: 100%; }

    &.bloom-inlineImageLeft, &.bloom-inlineImageRight, &.bloom-inlineImageMiddle {
        padding-top: var(--inline-image-offset);
        shape-outside: inset(var(--inline-image-offset) 0 0 0);
        clear: both;
    }
    &.bloom-inlineImageLeft   { float: left;  margin: 0 1em 0.5em 0; }
    &.bloom-inlineImageRight  { float: right; margin: 0 0 0.5em 1em; }
    &.bloom-inlineImageMiddle { float: left; width: 100% !important; // band; inner img centered at its own width
                                img { width: var(--inline-image-width, 40%); margin: 0 auto; } }
    &.bloom-inlineImageBottom { float: none; clear: both; margin: 0.5em auto 0 auto; }
}
// containment + bilingual show-once rules alongside (see Bilingual section)
```

(Exact selectors/structure to be refined in implementation; the middle band may carry width on the img via a second variable as sketched.)

- Margin-top can't replace the padding trick (line boxes avoid a float's whole margin box). `translateY` moves paint, not wrap. Anchor positioning is out-of-flow, never wrapped. CSS Exclusions is dead. Floats + shape-outside is the whole design space.
- **ePub on ancient readers degrades gracefully**: without shape-outside the text runs in a narrower column beside the transparent padding instead of flowing above the image. Readable, accepted (we routinely compromise on ePub). Only if QA on real readers demands it: a contained publish-time transform in `EpubMaker` (insert spacer, drop padding). Not built now.
- Bonus later: contour wrap via `shape-outside: circle()` etc.
- Edit-time rules (drag handles, cursor, wrapper stacking) → `bookEdit/css/editMode.less`. No hover/selected outline on the wrapper: its box includes the transparent offset padding, so an outline runs to the top of the block when the image is dragged down (John, live testing); the corner handles on the image box are the selection indicator. The wrapper needs `position: relative; z-index: 1` at edit time because Bloom's paragraphs are position:relative and would otherwise hit-test above the float, making the image unclickable.

## Edit UX

**Insertion: right-click context menu.** Master's BL-16649 `textContextMenu/TextContextMenu.tsx` is the menu; the inline-image items are wired into it (`buildInlineImageMenuItems` returns `ILocalizableMenuItemProps[]`). Right-click in an eligible `bloom-editable` (inside a TG, not inside a canvas element) shows "Add Image" — always, with no limit on how many images the TG already has. Choosing it inserts the wrapper (placeHolder.png, docked right, 40%, offset 0) into ALL sibling editables via sync and selects it. It does NOT open the image chooser (John, live testing); the user changes the picture afterwards via right-click → Change Image. Right-click on an existing wrapper offers Change Image, image credits, Remove Image.

**Drag = updating numbers, never re-parenting (except bottom).** While dragging the image:
- crossing horizontal thirds of the box switches dock class (left / middle band / right);
- vertical delta writes `--inline-image-offset` (clamped ≥ 0);
- drop in bottom zone moves the wrapper to last child (`bloom-inlineImageBottom`); drag out of it moves it back to first child;
- corner handles write `width` (aspect-ratio keeps height honest). Continuous drag-resize, not presets.
Then one call to `syncInlineImagesFromEditable()` stamps the wrapper into sibling editables, plus overflow re-check and the page-dirty path.

**Undo: every operation undoable via the workspace undo chain.** `workspaceRoot.handleUndo()`/`canUndo()` try origami → toolbox → `ImageUndoManager` → CKEditor, and CKEditor's stack cannot see programmatic DOM changes, so inline-image operations need their own layer, modeled on `ImageUndoManager.ts`: `inlineImages.ts` keeps a stack of per-operation snapshots of the whole TG's inline-image state (wrapper outerHTML + slot per editable, or null), recorded before insert / remove / image change / dock change / drag (at drag start, committed at drop) / resize. Undo restores the snapshot to all editables. Stack clears on page change (same `clearImageOperationUndoOnPageChange` pattern). Gate `inlineImageCanUndo()` on the inline image being selected/active, mirroring how `canUndoImageOperation` gates on an image container, so our stack never shadows a more recent text edit. Wire `inlineImageCanUndo`/`inlineImageUndo` into the chain in `workspaceRoot.ts` (both `handleUndo` and `canUndo`) and export through `editablePage.ts` like `imageOperationUndo`. No redo, matching the image-operation layer.

**Hover buttons on the wrapper** (all `bloom-ui`, auto-stripped on save): change image (`doImageCommand`), metadata (thin parallel of `SetupMetadataButton`, which is hard-wired to canvas structure; reuse `doImageCommand`/`GetRawImageUrl`/`isPlaceHolderImage`/`showCopyrightAndLicenseDialog`), delete inline image (removes from all siblings).

## Implementation steps

**Phase 1 — Rendering + format lock-in.**
1. CSS in `basePage-sharedRules.less` (+ `basePage-legacy-5-6.less` check), incl. bilingual show-once + float containment.
2. C# tests locking the dodges (no production C# change expected):
   - `TranslationGroupManagerTests`: new-language creation clones `.bloom-inlineImage` intact; `PrepareElementsOnPageOneLanguage` leaves it alone (nested, not direct child).
   - `BookStorageTests`: `MigrateToLevel7BloomCanvas` ignores `.bloom-inlineImage`.
3. Audio exclusion: `audioRecording.ts` child recursion (~line 3722) must skip the island (skip `contenteditable="false"` children).

**Phase 2 — Edit experience. New module `src/BloomBrowserUI/bookEdit/js/inlineImages.ts`:**
4. `setupInlineImages(container)` wired into `SetupElements` (`bloomEditing.ts`, near `SetupImagesInContainer`): normalization at load, hover buttons, drag/resize handlers, context-menu registration.
5. Sync: `syncInlineImagesFromEditable(editable)` — serialize wrapper (strip `bloom-ui` children), replace/insert at the correct slot in each sibling editable; idempotent. `normalizeInlineImages(tg)` — canonical = first-visible copy (contentFirst, else content1, else first found); stamp to all. Hook `changeImage` completion in `bloomImages.ts` (~445-495): if img is inside `.bloom-inlineImage`, sync.
6. Delete dead CKEditor guard `bloomEditing.ts:1216-1222`.
7. BloomField: confirm keepFirstInField/preventRemoval cover the new class (they key off classes); extend `bloomFieldSpec.ts`; `img.onload` overflow re-check. Note keepFirstInField logic must tolerate the bottom-dock last-child slot.

**Source bubbles: no change needed for v1.** `BloomSourceBubbles.tsx:109` strips text-less divs from bubble clones (`hasNoText`), so image-only wrappers vanish; the spurious `find("textarea, div").length > 1` gate then produces an empty bubble set. Lock with a vitest.

**Publishing: no changes needed.** Copies are persisted real content; hidden-language copies leave with their editables via `PublishHelper.RemoveUnwantedContent` (display-based); ePub/BloomPub collect files via generic `//img[@src]` (`EpubMaker.cs`, `HtmlDom.cs:3012`). QA: epubcheck a sample; eyeball a reader without shape-outside for the accepted degradation.

## Tests

- C#: the two lock-in tests above + an ePub/PublishHelper test (image file in manifest; hidden-language copy removed).
- Vitest: `inlineImages.test.ts` (insert into all siblings; sync preserves sibling text; idempotence; canonical selection; bloom-ui stripping; dock class switching; bottom dock moves slot; offset/width writes); `bloomFieldSpec.ts` (ctrl+a Del protection, `<p>` after image); source-bubble spec (no img/.bloom-inlineImage in bubble clone); audio spec (markup skips the island).
- Manual: extend `bookEdit/bloomField/test.pug` with docked variants; QA book covering bilingual display, talking-book recording over a wrapped block, overflow, ePub/BloomPub/PDF outputs.

## Deferred (later phases)

Captions (bubble-stripping + `StripOutText` interplay); crop (canvas-element crop machinery reuse); contour wrap (`shape-outside: circle()`); Word-style in-text anchoring as an advanced mode for monolingual books; spreadsheet import/export; possible `FeatureVersionRequirement` entry for old-Bloom editing warnings; publish-time ePub spacer fallback only if reader QA demands it.

## Critical files

- `src/BloomBrowserUI/bookEdit/js/inlineImages.ts` (new)
- `src/BloomBrowserUI/bookEdit/js/bloomImages.ts`, `bookEdit/js/bloomEditing.ts`
- `src/BloomBrowserUI/bookEdit/bloomField/BloomField.ts` + `test.pug`
- `src/content/bookLayout/basePage-sharedRules.less`, `bookEdit/css/editMode.less`
- `src/BloomBrowserUI/bookEdit/toolbox/talkingBook/audioRecording.ts`
- `src/BloomBrowserUI/bookEdit/js/canvasElementManager/CanvasElementContextControls.tsx` (menu pattern; possibly shared components)
- `src/BloomExe/Book/TranslationGroupManager.cs`, `src/BloomExe/Book/BookStorage.cs` (tests only)
