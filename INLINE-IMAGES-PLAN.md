# Inline (Word-style) images in Bloom text blocks

## Context

Bloom text blocks (translation groups) currently cannot contain images the way Word paragraphs can — docked left or right with text wrapping around, or centered as a block. Users producing textbook-style material (e.g. the SIL LEAD Uganda P4 books) have hand-hacked this with custom templates. We want a first-class feature: an image inside a text block, docked left / right / center, language-neutral in effect (every language version of the block shows the same image), never leaking into source bubbles.

**Chosen approach (with John):** the image markup lives *inside each `bloom-editable`* (the only way CSS floats can wrap text), replicated per language and kept in sync by edit-time JS — "Option 1", hardened with a canonical-copy normalization rule borrowed from Option 2. Multilingual display policy (2–3 visible languages) is deliberately deferred; edit UX reuses the existing image-container commands (change image, metadata) plus a minimal dock + size control. No new toolbox tool in v1.

**Why Option 1 over a canonical TG-child element (Option 2):**
- New-language creation already works with **zero C# changes**: `MakeElementWithLanguageForOneGroup` + `StripOutText` clone an embedded image div into new language editables, text stripped — proven by existing test `TranslationGroupManagerTests.cs:900`.
- No lang-less TG child means we never arm the trap in `TranslationGroupManager.cs:804` (deletes lang-less `<div>` direct children of a TG on every page load) — and old Bloom versions opening the book can't destroy the canonical element, because there isn't one.
- No book-format structural change; old Bloom / bloom-player / ePub readers render the markup inertly with shipped CSS.
- Option 2's advantage (single source of truth) is captured by the rule: *the copy in the first visible editable (prefer `.bloom-content1`) is canonical*; `normalizeInlineImages()` stamps it to siblings at page load and after every edit. Upgrade seam for multilingual policy later: move dock/size to `data-*` attributes on the TG (attributes survive all C# sweeps).

## Key existing machinery (reuse, don't rebuild)

- `src/BloomBrowserUI/bookEdit/bloomField/BloomField.ts` — `bloom-keepFirstInField` (image stays first in field, `EnsureParagraphsPresent` puts the required `<p>` after it), `bloom-preventRemoval` (undo-based ctrl+a+Del protection), backspace-at-start guard. Demo page with acceptance criteria: `src/BloomBrowserUI/bookEdit/bloomField/test.pug:93-105`.
- `src/BloomBrowserUI/bookEdit/js/bloomImages.ts` — `doImageCommand(img, "change")` works on any `<img>` (element-reference + temp-id round trip, location-agnostic); `GetRawImageUrl`, `isPlaceHolderImage`, metadata dialog.
- `contenteditable=false` islands inside CKEditor-managed editables are proven safe: the format cog (`StyleEditor.ts:1264`) is exactly that. The old guard that supposedly disabled CKEditor for embedded-image fields (`bloomEditing.ts:1216`, BL-3125) is dead code (`$(this)` in `bootstrap()` is an empty set) — delete it as part of this work.

## Markup (new class; do NOT reuse `bloom-imageContainer`)

```html
<div class="bloom-editable ..." lang="en" contenteditable="true">
  <div class="bloom-inlineImage bloom-inlineImageLeft bloom-keepFirstInField bloom-preventRemoval"
       contenteditable="false" style="width: 40%; aspect-ratio: 800 / 600;">
    <img src="flower.jpg" data-copyright="..." data-license="..."/>
  </div>
  <p>Text that wraps…</p>
</div>
```

- Dock modifiers: `bloom-inlineImageLeft` / `bloom-inlineImageRight` / `bloom-inlineImageCenter`.
- Size = inline `width` (% of editable) + `aspect-ratio` from natural dimensions (stable layout before image load; keeps OverflowChecker honest).
- The new class dodges `BookStorage.MigrateToLevel7BloomCanvas` (matches only `bloom-imageContainer`) and `SetupImagesInContainer`'s selectors (`bloomImages.ts:117`). Never prefix inline-image classes with `bloom-content` (the `UpdateContentLanguageClasses` sweep strips those).
- v1 rules: max one inline image per translation group; always first child of the editable; center mode = block at top (text below only).

## CSS

Display rules → `src/content/bookLayout/basePage-sharedRules.less` (imported by `basePage.less` AND `src/BloomBrowserUI/publish/ePUBPublish/baseEPUB.less:48` — covers edit, PDF, bloom-player, ePub with zero publish-code changes):

```less
.bloom-editable .bloom-inlineImage {
    img { width: 100%; height: 100%; object-fit: contain; object-position: 0 0; display: block; }
    &.bloom-inlineImageLeft  { float: left;  clear: both; margin: 0 1em 0.5em 0; }
    &.bloom-inlineImageRight { float: right; clear: both; margin: 0 0 0.5em 1em; }
    &.bloom-inlineImageCenter{ float: none; margin: 0 auto 0.5em auto; }
}
```

Edit-time rules (hover outline, button cluster positioning, hide in thumbnails) → `bookEdit/css/editMode.less`. Drop the legacy `imagePusherDowner` vertical-offset trick for v1 (keep old rules for legacy books).

## Implementation steps

**Phase 0 — Spike (throwaway).** Hand-edit a book's .htm with the markup + custom CSS. Validate: (1) floats wrap in all editable styles — check whether any style makes editables `display:flex` (vertical centering), which kills float wrap; may need `.bloom-editable:has(.bloom-inlineImage) { display: block }`; (2) typing/caret/undo around the island under CKEditor+WebView2; (3) `bloom-preventRemoval` undo trick still works (CKEditor's own undo stack is disabled, `lib/ckeditor/config.js:82`; the trick uses native `document.execCommand("undo")`); (4) overflow detection; (5) bloom-player render.

**Phase 1 — Rendering + format lock-in.**
1. CSS in `basePage-sharedRules.less` (check `basePage-legacy-5-6.less` for legacy-theme needs).
2. C# tests locking the dodges (no production C# change expected):
   - `TranslationGroupManagerTests`: new-language creation clones `.bloom-inlineImage` intact; `PrepareElementsOnPageOneLanguage` leaves it alone (nested, not direct child).
   - `BookStorageTests`: `MigrateToLevel7BloomCanvas` ignores `.bloom-inlineImage`.
3. Audio exclusion: `audioRecording.ts` child recursion (~line 3722) must skip the island — skip `child.getAttribute("contenteditable") === "false"`.

**Phase 2 — Edit experience (the bulk). New module `src/BloomBrowserUI/bookEdit/js/inlineImages.ts`:**
4. `setupInlineImages(container)` wired into `SetupElements` (`bloomEditing.ts`, near `SetupImagesInContainer`). Hover button cluster (all `bloom-ui`, auto-stripped on save): change image (`doImageCommand(img, "change")`), metadata (parallel of `SetupMetadataButton` — the originals are hard-wired to canvas structure, write thin parallels reusing `doImageCommand`/`GetRawImageUrl`/`isPlaceHolderImage`/`showCopyrightAndLicenseDialog`), dock toggle (left/center/right), size presets (25/40/60%).
5. Insertion affordance: formatButton-style `bloom-ui` button on focused eligible editables (TG has no inline image yet) → inserts placeholder markup into ALL sibling editables via sync, then opens the image chooser.
6. Sync: `syncInlineImagesFromEditable(editable)` — serialize wrapper (strip `bloom-ui` children), replace/insert as first child of each sibling editable; idempotent. `normalizeInlineImages(tg)` — canonical = `.bloom-content1`'s copy, else first found; called at page load. Hook `changeImage` completion in `bloomImages.ts` (~445-495): if img is inside `.bloom-inlineImage`, sync. Every mutation → overflow re-check + page-dirty path.
7. Delete dead CKEditor guard `bloomEditing.ts:1216-1222`.
8. BloomField: confirm keepFirstInField/preventRemoval cover the new class (they key off classes); extend `bloomFieldSpec.ts`; add `img.onload` overflow re-check.

**Source bubbles: no change needed for v1** — `BloomSourceBubbles.tsx:109` strips text-less divs from bubble clones (`hasNoText`), so image-only wrappers vanish; the spurious `find("textarea, div").length > 1` gate then produces an empty bubble set. Lock with a vitest.

**Publishing: no changes needed** — copies are persisted real content; hidden-language copies leave with their editables via `PublishHelper.RemoveUnwantedContent` (display-based); ePub/BloomPub collect files via generic `//img[@src]` (`EpubMaker.cs`, `HtmlDom.cs:3012`); floats are ePub-safe CSS. QA: run epubcheck on a sample.

## Tests

- C#: the two lock-in tests above + an ePub/PublishHelper test (image file in manifest; hidden-language copy removed).
- Vitest: `inlineImages.test.ts` (sync preserves sibling text, idempotence, canonical selection, bloom-ui stripping, insert-into-all); `bloomFieldSpec.ts` (ctrl+a Del protection, `<p>` after image); source-bubble spec (no img/.bloom-inlineImage in bubble clone); audio spec (markup skips the island).
- Manual: extend `bookEdit/bloomField/test.pug` with left/right/center `bloom-inlineImage` fields; QA book covering bilingual display, talking-book recording over a wrapped block, overflow, ePub/BloomPub/PDF outputs.

## Deferred (Phase 3+)

Captions (bubble-stripping + `StripOutText` interplay); multilingual visible-languages policy (stopgap available: hide in non-content1 blocks via CSS); true "text above and below" center placement (mid-flow anchoring, replaces imagePusherDowner); drag-resize; crop; `shape-outside`; spreadsheet import/export; possible `FeatureVersionRequirement` entry for old-Bloom editing warnings.

## Critical files

- `src/BloomBrowserUI/bookEdit/js/inlineImages.ts` (new)
- `src/BloomBrowserUI/bookEdit/js/bloomImages.ts`, `bookEdit/js/bloomEditing.ts`
- `src/BloomBrowserUI/bookEdit/bloomField/BloomField.ts` + `test.pug`
- `src/content/bookLayout/basePage-sharedRules.less`, `bookEdit/css/editMode.less`
- `src/BloomBrowserUI/bookEdit/toolbox/talkingBook/audioRecording.ts`
- `src/BloomExe/Book/TranslationGroupManager.cs`, `src/BloomExe/Book/BookStorage.cs` (tests only)
