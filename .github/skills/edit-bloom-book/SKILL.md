---
name: edit-bloom-book
description: Use when an agent needs to edit or repair a Bloom book HTML file while preserving the strict Bloom.html schema used by Bloom Desktop.
argument-hint: "Bloom.html path and requested change or repair"
user-invocable: true
---

# Edit Bloom book

## Outcome
Edit a `Bloom.html` book without breaking Bloom's structural expectations. Preserve the DOM schema Bloom relies on for page identity, multilingual text, images, canvas elements, xMatter regeneration, and split-pane layouts.

## When To Use
- The user wants to edit a book's `Bloom.html` directly.
- The user needs help repairing a damaged or hand-edited Bloom book.
- The task involves changing text, image containers, layout wrappers, or metadata while keeping the book valid.
- The task requires adding or repairing Bloom-specific elements such as `.bloom-page`, `.bloom-translationGroup`, `.bloom-editable`, `.bloom-canvas`, or split-pane containers.

## Validator Script
- Before and after structural edits, run the validator script in this skill folder.
- Command:

```bash
node .github/skills/edit-bloom-book/validateBloomBook.mjs path/to/Book.htm
```

- It accepts multiple books:

```bash
node .github/skills/edit-bloom-book/validateBloomBook.mjs book1.htm book2.htm
```

- Current validator scope checks Bloom's core book schema and the common strict layout subtrees:
  - HTML parseability in a browser-style DOM
  - source-level root placement of `.bloom-page` elements
  - presence of `#bloomDataDiv`
  - presence of `meta[name="BloomFormatVersion"]`
  - at least one `.bloom-page`
  - every `.bloom-page` at body root level
  - non-empty unique page ids and expected page shell wrappers
  - non-empty `lang` on every `.bloom-editable`
  - `.bloom-translationGroup` / `div.bloom-editable` child structure
  - split-pane component/divider/inner wrapper structure
  - modern canvas-element image containers and legacy direct-image `bloom-canvas` structures
  - unique ids on `p`, `img`, and `textarea`
- Treat validator failures as blockers for direct book edits.

## Structural Grammar

This is the concise model the validator enforces. `*` means zero or more, `?` means optional, and quoted class names mean the element must include that class, though it may also carry other Bloom classes.

```bnf
Book       ::= html(head(meta[name=BloomFormatVersion], ...), body(DataDiv, Page+))
DataDiv    ::= div#bloomDataDiv

Page       ::= div."bloom-page"[id] as direct child of body
               containing MarginBox
PageChild  ::= PageLabel | PageDescription | MarginBox | PageCoverColor | TranslationGroup
MarginBox  ::= div."marginBox" containing PageContent*

TranslationGroup ::= div."bloom-translationGroup" not nested in another TranslationGroup
                     containing (Editable | textarea[lang])+
Editable         ::= div."bloom-editable"[lang] as direct child of TranslationGroup

SplitPane        ::= div."split-pane".("horizontal-percent" | "vertical-percent")
                     (SplitComponent, SplitDivider, SplitComponent, SplitResizeShim?)
HorizontalSplit  ::= SplitPane with exactly one component."position-top",
                     one component."position-bottom", and divider."horizontal-divider"
VerticalSplit    ::= SplitPane with exactly one component."position-left",
                     one component."position-right", and divider."vertical-divider"
RootSplitHolder  ::= div."marginBox"."split-pane-component" containing SplitPane
SplitComponent   ::= div."split-pane-component" containing exactly one direct SplitInner | SplitPane
SplitInner       ::= div."split-pane-component-inner"
SplitResizeShim  ::= div."split-pane-resize-shim"

Canvas           ::= div."bloom-canvas" inside Page containing LegacyImage+ | CanvasElement+
LegacyImage      ::= img as a direct child of Canvas
CanvasElement    ::= div."bloom-canvas-element" as direct child of Canvas
                     containing ImageContainer | TranslationGroup | VideoContainer
ImageContainer   ::= div."bloom-imageContainer" as direct child of CanvasElement
                     containing exactly one direct img
```

Important compatibility branches:
- Older valid books can use `bloom-canvas > img` directly, without `.bloom-canvas-element` or `.bloom-imageContainer`.
- Older valid split panes can omit `.split-pane-resize-shim`.
- Origami/custom layouts can nest a `.split-pane` directly inside a `.split-pane-component` instead of using `.split-pane-component-inner` at that level; leaf split components still use `.split-pane-component-inner`.
- `contenteditable="true"` is common on user-editable text, but not a validity invariant: some generated/xMatter fields have `.bloom-editable` and content classes while intentionally being non-editable.

## Core Model

### Document Shape
- The document must remain parseable XHTML/HTML.
- `body` contains the book-level metadata div and page divs.
- Each `.bloom-page` must be a direct child of `body`.
- Do not nest one `.bloom-page` inside another element.
- Page `id` values must remain unique.
- `id` values on `img`, `p`, and `textarea` must also remain unique.

Canonical top-level shape:

```html
<html>
  <head>...</head>
  <body>
    <div id="bloomDataDiv">...</div>
    <div class="bloom-page ..." id="...">...</div>
    <div class="bloom-page ..." id="...">...</div>
  </body>
</html>
```

### Book Metadata
- Preserve `#bloomDataDiv` near the start of `body`.
- `#bloomDataDiv` stores book-wide values using elements like `<div data-book="bookTitle" lang="en">...</div>`.
- `data-book` values in page content and values in `#bloomDataDiv` are linked and should stay in sync.
- Preserve xMatter persistence keys such as `data-xmatter-page` and any paired attributes stored in `#bloomDataDiv`.

### Page Shape
- A page root is a `<div>` with class `.bloom-page` and a stable `id`.
- Preserve page classes such as size, side, matter, numbering, activity, and theme classes.
- Common direct page children include `.pageLabel`, `.pageDescription`, `.marginBox`, and sometimes other layout wrappers.
- `lang=""` on the page itself is common and should not be normalized away unless the surrounding pattern clearly requires something else.

Typical page shape:

```html
<div class="bloom-page numberedPage customPage A5Portrait side-right" id="..." data-page="..." lang="">
  <div class="pageLabel" lang="en" data-i18n="...">...</div>
  <div class="pageDescription" lang="en"></div>
  <div class="marginBox">
    ...page content...
  </div>
</div>
```

### Margin And Layout Containers
- `.marginBox` is the usual primary page content wrapper and should normally be preserved.
- Some valid pages place `.marginBox` inside another wrapper; do not flatten or "clean up" structure just because it looks redundant.
- Split layouts use `.split-pane`, `.split-pane-component`, `.split-pane-component-inner`, `.split-pane-divider`, and `.split-pane-resize-shim` in a strict pattern.
- If a page already uses split-pane layout, preserve the existing wrapper hierarchy and position classes.

Canonical split-pane patterns:

```html
<div class="split-pane horizontal-percent">
  <div class="split-pane-component position-top" style="bottom: 50%;">
    <div class="split-pane-component-inner">...</div>
  </div>
  <div class="split-pane-divider horizontal-divider" style="bottom: 50%;"></div>
  <div class="split-pane-component position-bottom" style="height: 50%;">
    <div class="split-pane-component-inner">...</div>
  </div>
  <div class="split-pane-resize-shim"></div>
</div>
```

```html
<div class="split-pane vertical-percent">
  <div class="split-pane-component position-left">
    <div class="split-pane-component-inner" style="position: relative;">...</div>
  </div>
  <div class="split-pane-divider vertical-divider"></div>
  <div class="split-pane-component position-right">
    <div class="split-pane-component-inner" style="position: relative;">...</div>
  </div>
  <div class="split-pane-resize-shim"></div>
</div>
```

### Translation Groups
- Multilingual text is normally represented by a `.bloom-translationGroup` wrapper.
- A translation group contains one or more `.bloom-editable` children, each with a `lang` attribute.
- Preserve `data-default-languages` exactly unless the task explicitly requires language-visibility changes.
- Common values include `V`, `N1`, `N2`, `L1`, `L2`, `L3`, `auto`, and `*`.
- Do not merge unrelated translation groups.
- Do not move a `.bloom-editable` from one translation group to another unless the semantic field is clearly the same.

Canonical translation group:

```html
<div class="bloom-translationGroup" data-default-languages="V,N1">
  <div class="bloom-editable bloom-content1 bloom-visibility-code-on" lang="en" contenteditable="true">
    <p>Text</p>
  </div>
  <div class="bloom-editable bloom-contentNational1 bloom-visibility-code-on" lang="fr" contenteditable="true">
    <p>Texte</p>
  </div>
</div>
```

### Editable Text Rules
- `.bloom-editable` marks user-editable content. Preserve it.
- Every `.bloom-editable` should keep its `lang`.
- Keep `contenteditable="true"` on user-editable text unless the surrounding pattern clearly uses a non-editable stored value.
- Preserve content classes such as `.bloom-content1`, `.bloom-content2`, `.bloom-content3`, `.bloom-contentNational1`, `.bloom-contentNational2`, and `.Equation-style`.
- These content classes are important for editor attachment and visibility behavior.
- `lang="z"` is a special prototype-only placeholder used in templates and some generated content. Do not rewrite it to a real language unless the task is explicitly converting template content into concrete book content.
- Preserve other significant attributes such as `data-book`, `data-audiorecordingmode`, `spellcheck`, `tabindex`, `role`, and `aria-label` when present.

### Images And Canvas Rules
- Older books may still use older image-container structures; prefer preserving and minimally repairing instead of aggressively modernizing.
- Current canvas-based pages commonly use `.bloom-canvas` containing one or more `.bloom-canvas-element` children.
- Background images typically use `.bloom-canvas-element.bloom-backgroundImage > .bloom-imageContainer > img`.
- Preserve image metadata such as `data-book`, `data-copyright`, `data-license`, `data-creator`, `data-imgsizebasedon`, and comic `data-bubble` payloads.
- Do not discard `svg.comical-generated` or `.bloom-canvas-element` nodes on comic pages.

Canonical canvas background-image pattern:

```html
<div class="bloom-canvas bloom-has-canvas-element" data-tool-id="canvas">
  <div class="bloom-canvas-element bloom-backgroundImage" style="width:100%; height:100%;">
    <div class="bloom-imageContainer" data-tool-id="canvas">
      <img data-book="coverImage" src="placeHolder.png" />
    </div>
  </div>
</div>
```

### Image Copyright / License Metadata
- Image attribution lives on the `img` as `data-copyright`, `data-creator` (the photographer/artist), and `data-license` (a license token such as `cc-by`, `cc-by-sa`, or `cc0`; the license *version* is not encoded here).
- IMPORTANT: the image **file's** embedded metadata (XMP/EXIF) is the source of truth, not the HTML. On load and on save Bloom runs `ImageUpdater.UpdateImgMetadataAttributesToMatchImage()`, which reads metadata from the file and **rewrites** (or, when the file has none, **removes**) these `data-*` attributes. So setting only the HTML attributes does NOT stick: a photo with no embedded metadata shows the red "?©" badge and its `data-*` get blanked on the next save.
- To make attribution persist, write it into the image file with Bloom's own libpalaso API: `SIL.Windows.Forms.ClearShare.Metadata` → set `CopyrightNotice` / `Creator` / `License` (e.g. `CreativeCommonsLicense.FromLicenseUrl(...)`) → `WriteIntellectualPropertyOnly(path)`. Bloom reads it back via `RobustFileIO.MetadataFromFile` (TagLib#). After that, the HTML `data-*` are derived/repopulated automatically. See the `bird-book` skill for a working batch tool (`embed-metadata/`).
- CAUTION: Bloom owns the currently-open book file and re-saves it from its in-memory copy, silently clobbering external edits and re-stripping image attributes. **Close Bloom before hand-editing the book HTML or its image files**, then reopen.

### xMatter And Regenerated Pages
- Pages with xMatter roles such as front cover, title page, credits, and back cover may be regenerated by Bloom.
- Preserve `data-xmatter-page` and related persisted attributes.
- Avoid hand-editing xMatter structure unless the task is specifically about xMatter templates or repairing a broken book so Bloom can reopen it.
- If the user wants a durable xMatter change, prefer editing the underlying template/source rather than only the generated page.

## Hard Rules
- Never move a `.bloom-page` away from `body` root level.
- Never delete or replace a page `id` unless you are intentionally creating a new page and generating a new unique one.
- Never introduce duplicate `id` values.
- Never remove `.bloom-translationGroup` wrappers around multilingual content.
- Never strip `lang` from `.bloom-editable` elements.
- Never strip content classes such as `.bloom-content1` or `.bloom-contentNational1` without understanding the field's visibility behavior.
- Never remove `data-default-languages` unless the task explicitly changes language visibility.
- Never flatten split-pane wrappers on a split layout page.
- Never throw away image attribution, licensing, comic, or sizing metadata.
- Never assume xMatter pages are ordinary custom pages.
- Never assume image copyright/license set only in HTML `data-*` will persist; Bloom rewrites those from the image file's embedded metadata. Embed it in the file, and edit only while Bloom is closed.

## Safe Editing Strategy
1. Identify the smallest owning structure before editing: page, margin box, split pane, translation group, editable, or canvas element.
2. Preserve all existing Bloom-specific classes and attributes unless you have a concrete reason to change one.
3. If changing text, prefer editing only the inner HTML/text of the correct `.bloom-editable`.
4. If adding multilingual text, add another `.bloom-editable` inside the existing `.bloom-translationGroup`; do not create a parallel structure unless the page pattern already does that.
5. If adding an image to a canvas page, reuse the existing `.bloom-canvas` / `.bloom-canvas-element` / `.bloom-imageContainer` structure.
6. If repairing broken markup, restore the nearest known-good pattern from the same book or from the canonical template structure.

## Safe Repairs
- Add a missing unique `id` to a `.bloom-page`.
- Move a misplaced `.bloom-page` back to body root level.
- Restore a missing `.bloom-translationGroup` wrapper when multiple language variants clearly belong to the same field.
- Restore `lang` and `contenteditable="true"` on `.bloom-editable` nodes when they were accidentally stripped.
- Restore `.bloom-content1` or `.bloom-contentNational1` when the field's role is obvious from neighboring sibling editables.
- Rebuild a missing `.bloom-imageContainer` inside an existing `.bloom-canvas-element` when the intended image pattern is unambiguous.
- Repair a split-pane layout by recreating missing `.split-pane-component-inner` wrappers without changing the overall split geometry.

## Risky Changes
- Changing `data-default-languages`.
- Moving nodes between translation groups.
- Restructuring `.marginBox` or split-pane boundaries.
- Editing xMatter page structure instead of template sources.
- Modifying comic `data-bubble` JSON or canvas sizing metadata.

## Practical Heuristics
- When a page already contains a valid example of the pattern you need, copy that local pattern instead of inventing a new one.
- When repairing a text field, prefer preserving sibling editables and only fixing the damaged one.
- If a field uses `data-book`, check whether the matching `#bloomDataDiv` value also needs to be repaired.
- If the page is a simple text/image custom page, assume `.marginBox` and `.split-pane-component-inner` are meaningful even when they look decorative.
- If a template or xMatter file uses `lang="z"`, treat that as intentional template placeholder content.

## Minimal Valid Snippets

Simple text field:

```html
<div class="bloom-translationGroup" data-default-languages="auto">
  <div class="bloom-editable bloom-content1 bloom-visibility-code-on" lang="en" contenteditable="true">
    <p>Text</p>
  </div>
</div>
```

Image-only page content:

```html
<div class="marginBox">
  <div class="split-pane-component-inner">
    <div class="bloom-canvas">
      <img src="placeHolder.png" alt="" />
    </div>
  </div>
</div>
```

Canvas page content:

```html
<div class="split-pane-component-inner">
  <div class="bloom-canvas bloom-has-canvas-element" data-tool-id="canvas">
    <div class="bloom-canvas-element bloom-backgroundImage" style="width:100%; height:100%;">
      <div class="bloom-imageContainer" data-tool-id="canvas">
        <img src="placeHolder.png" />
      </div>
    </div>
  </div>
</div>
```

## Source-Of-Truth References In This Repo
- `src/BloomExe/Book/HtmlDom.cs`: `ValidateBook()` enforces page presence, root-level `.bloom-page` placement, and unique ids.
- `src/BloomExe/Book/TranslationGroupManager.cs`: defines how Bloom prepares `.bloom-translationGroup` and `.bloom-editable` structures for active languages.
- `src/BloomExe/Book/BookData.cs`: defines how `#bloomDataDiv`, `data-book`, and `data-xmatter-page` metadata are synchronized.
- `src/content/templates/template books/standard-page-mixins.pug`: canonical split-pane and canvas page patterns.
- `src/content/templates/xMatter/TemplateStarter-XMatter/TemplateStarter-XMatter.pug`: canonical xMatter cover/about structures.
- `src/BloomBrowserUI/utils/shared.ts`: editor attachment depends on content classes plus `contenteditable="true"`.
- `src/BloomTests/Book/BookStorageTests.cs`: concrete examples of valid and repaired page structures, including nested `.marginBox` and canvas cases.

## Workflow
1. Read the target `Bloom.html` and identify the specific page or field to change.
2. Find the nearest matching pattern in the same book.
3. If the local book does not provide one, use the canonical repo patterns listed above.
4. Make the smallest structural edit that satisfies the request.
5. Re-check page root placement, id uniqueness, translation-group integrity, and metadata links before finishing.
