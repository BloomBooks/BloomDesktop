---
name: branding-page-sizes
description: Use when adding or tuning page sizes/layouts for a Bloom enterprise/subscription branding pack (e.g. MXB-Book-Literacy, MXB-Book-Scripture, MXB-Book-Literacy-Prepub) so the dense front/back matter looks polished at every size — and when producing a real-Bloom screenshot PDF that proves it. Covers unlocking page sizes, the spacing-vs-font tuning rules, a live overflow-measuring iteration loop, the CDP capture pipeline, removing edit-view artifacts, and assembling the verification PDF.
argument-hint: "branding pack + sample book folder, and which sizes/pages to polish"
user-invocable: true
---

# Branding page sizes: layouts, per-size polish, verification & PDF

## Outcome
Take a branding pack whose front/back matter is dense (the MXB packs push a lot onto the
title and credits pages), give its books a full set of page sizes, make every size look
polished, and **prove it with a PDF of actual Bloom screenshots** — not fabricated renders.

### The bar: no overflow AND no "smell of overflow"
"Polished" means a page must neither overflow **nor give the smell of overflow.** Concretely,
the deliverable must show **no red overflow indicator** — Bloom renders an editable's text/
border in red (a red box) when its content is taller than its allotted box, even when the
page edge isn't visibly breached. That red is a real signal (the content would clip/overlap
in print), so it must be **fixed**, not hidden. Two distinct failures to hunt for, per page:
1. **Page overflow** — an element's bottom extends past the `.bloom-page` bottom (e.g. a
   cut-off logo). Detect: `el.getBoundingClientRect().bottom > pageR.bottom`.
2. **In-box overflow (the red "smell")** — an editable overflows its own box. Detect:
   `el.scrollHeight - el.clientHeight > 2` on `.bloom-editable.bloom-visibility-code-on`,
   or look for Bloom's overflow class. A box can be well within the page yet still be red
   because a fixed/`min-height` or flex-allotted height is shorter than its content.

This skill is the page-size/verification companion to two others — read them too:
- `bloom-branding` — what branding.json/branding.less do, the badge/QR mechanism, how files
  reach a book (build + reopen), and the `STARTLAYOUTS` page-size source.
- `bloom-automation` — launching this worktree's Bloom, finding the CDP target, the
  127.0.0.1-vs-localhost / Host-header gotcha, and driving the UI over CDP.

## 1. Unlock the page sizes
Sizes come from the **first** stylesheet with a `STARTLAYOUTS … ENDLAYOUTS` JSON comment
(parsed by `SizeAndOrientation.cs`). For MXB that block lives in the xmatter `.less`
(`MXBLiteracy-XMatter.less` / `MXBScripture-XMatter.less`). LESS keeps `/* */` comments, so
add each layout (e.g. `LetterPortrait`, `HalfLetterLandscape`, `QuarterLetterLandscape`,
`Device16x9Portrait`, …) to that block. Despite a code comment claiming a branding xmatter
only sets a *default*, in practice this block **restricts** the dropdown.

## 2. The tuning rules (READ FIRST)
The hard part is fitting the dense front matter on the small/short sizes. The governing rule
from the product owner:

> **Spacing is negotiable. Font size is NOT.**

So to resolve overflow / cut-off logos, never shrink the body fonts (title, contributions,
the actual content). Instead, in this order:
1. **Reclaim large fixed inter-section margins** (e.g. a default `3em` gap → `0.5em`). The MXB
   title page anchors the bottom branding logo via `#funding{margin-top:auto}` /
   `#prepubNotice{margin-top:auto}`; when the fixed content fits, that auto-margin pools the
   leftover space and re-anchors the logo at the bottom. When content overflows, the auto
   margin collapses to 0 and the logo is pushed off — so cutting fixed margins is what lets it
   re-anchor.
2. **Tighten leading (`line-height`) on auxiliary metadata blocks.** Line-height is spacing,
   not font size, so it's fair game. The prepub `#printingHistory` / "Versión Preliminar"
   block ships with a loose `line-height: 1.5`; dropping it to `1.1` (or `1.0` on the very
   short Device16x9Landscape) reclaims a lot without touching the font.
3. **Collapse the margins around those blocks** (`margin-bottom: 0`, etc.).

Design steer for the tall sizes (Letter): let the front matter **flow from the top** and
leave whitespace at the bottom — undo the `margin-top:auto` bottom-anchoring there.

Title-text steer: when the title gets longer, let it **grow upward** into empty space rather
than scrunching the font or pushing the logo down.

### Where the rules live
`src/content/templates/xMatter/project-specific/MXBLiteracy-XMatter/MXBCommon-XMatter.less`
is shared by both MXB Literacy and MXB Scripture (and their Device variants), so one edit
covers all. Scope size-specific rules with the size class, e.g.
`.titlePage.QuarterLetterLandscape #contributions { … }`. Watch CSS specificity: MXB packs
use `#id` selectors, so a size-scoped override often needs the id too (and to come later in
the file) to win.

### Prepub specifics
The `-Prepub` branding shows extra content the normal book doesn't: a `#printingHistory`
block on the **title page** ("Versión Preliminar / Primera edición / title / language / code /
place / year") plus a "Versión Preliminar" watermark. That block is ~6 lines at the default
size and overflows the short sizes. Tightening its leading/margins (rule 2 & 3) fixes it; on
non-prepub books the block is empty, so the same rules are harmless no-ops.

## 3. The live iteration loop (fast)
Reopening a book per CSS tweak is slow, and **Bloom does not re-copy the xmatter CSS into a
book on a mere tab switch** (it caches "up to date" for the session). So iterate live by
injecting candidate CSS into the page over CDP, measuring real overflow, screenshotting — then
bake into LESS once it's right. The reusable tool is `iterate.mjs` (see "Capture tooling").

For each candidate it: closes the Talking Book toolbox, strips edit artifacts, switches size
via the API, jumps to the page, injects the candidate `<style>`, then reports overflow:
```js
// per element: does its bottom exceed the page bottom? does it overflow its own box?
var pageR = bloomPage.getBoundingClientRect();
el.getBoundingClientRect().bottom > pageR.bottom        // past the page → cut off
el.scrollHeight - el.clientHeight > 2                    // overflowing its box
marginBox.scrollHeight - marginBox.clientHeight          // total page overflow
```
Measure the marginBox children (`getBoundingClientRect` top/bottom/height + computed
margins/line-height) to find *which* block is eating the height before guessing at CSS.

## 4. Bake the fix → get it into the running book
1. Edit `MXBCommon-XMatter.less`. The dev watcher (`./go.sh` flow) recompiles it into every
   dependent xmatter `.css` under `output/browser/templates/xMatter/...` — confirm the
   `[dev] [LESS] ✓ … MXBCommon-XMatter.less → …` lines and grep the compiled `.css` for your
   rule (and check it lands *after* any same-specificity rule).
2. The open book still has its **stale** copy. Either restart Bloom, or (fast path) copy the
   freshly compiled `output/.../MXBLiteracy-XMatter.css` over the book's own
   `MXBLiteracy-XMatter.css`. A `jumpToPage` then reloads the page iframe with the new CSS.
3. Re-capture with the **real** compiled CSS (no injected candidate) and re-measure to confirm
   `overflowY: 0` and nothing past the page bottom.

## 5. Capture pipeline (real Bloom, authoritative)
Verification PDFs must be **actual Bloom screenshots**, never fabricated/headless-rendered
pages (runtime-only things — the real QR code, branding-file copy, autofit — exist only in
real Bloom). First confirm you're driving **this worktree's** Bloom (`bloomProcessStatus.mjs
--json` → `matchesExpectedRepoRoot: true`); other agents run Blooms from other worktrees.

Per size × page (`capture.mjs`):
- Switch size: in-page `fetch('/bloom/api/editView/topBar/layoutChoiceChange', {POST, json:{layoutChoiceId}})`.
- Jump page: in-page `fetch('/bloom/api/editView/jumpToPage', {POST, body: pageId})` (page ids
  parsed from the book `.htm`: split on `<div class="bloom-page`, match `data-xmatter-page` +
  `id`).
- Issue these **from inside the page** via CDP `Runtime.evaluate` (the page origin is
  `localhost`, so the Host header is accepted; a Node `fetch` to `127.0.0.1` gets 400). Use
  `127.0.0.1` for the CDP `/json` + websocket though (localhost → ::1 returns the wrong/empty
  target). This is the #1 footgun — see `bloom-automation`.
- Poll the `#page` iframe's `.bloom-page` className for the size class and the right page id
  before screenshotting.
- `Page.captureScreenshot` with a `clip` computed from the `.bloom-page` bounding rect (+a few
  px), `scale: 2`.

### Make screenshots look published (remove edit-view artifacts)
Before each shot:
- **Close the Talking Book toolbox** so it stops re-highlighting the current audio segment on
  every navigation: `document.getElementById('toolbox').contentWindow.toolboxBundle
  .getTheOneToolbox()` → `if(tb.toolboxIsShowing()) tb.toggleToolbox()`, then
  `removeToolboxMarkup()`. (`toolbox.ts applyToolboxStateToUpdatedPage` only re-marks while the
  toolbox is showing.) Also strip the `.ui-audioCurrent` class directly as a belt-and-braces —
  that orange highlight is the thing reviewers notice.
- Blur focus, remove `.cke_focus`, clear the selection, and inject a `<style>` hiding
  `.qtip, .bloom-ui, .uibloomSourceTextsBubble, .cke_float, .nicescroll-rails` (the
  publish-stripped UI), set `caret-color: transparent`, and `.bloom-editable p::after{content:none}`
  to drop the faint `¶` paragraph marks. (The `¶` is harmless editor chrome — fine to leave if
  asked.)

### Resilience
A layout switch can occasionally drop the CDP socket or take Bloom down at the very last size,
so a run may finish 31/32. Relaunch this worktree's Bloom (`./go.sh`), reopen the book, and
re-capture just the missing pages (`capture_one.mjs` reconnects fresh each run). Sanity-check
the result: `md5sum` the PNGs — distinct count should equal shot count (identical hashes mean
the layout/page change silently failed, usually the Host-header 400).

## 6. Assemble the PDF
Build a labeled HTML gallery (one section per size, the four xmatter pages per row with
captions) and print it with Edge headless:
```
msedge --headless=new --disable-gpu --no-sandbox \
  --user-data-dir=<WARM dir> --virtual-time-budget=12000 \
  --print-to-pdf=<out.pdf> file:///<gallery.html>
```
Headless `--print-to-pdf` omits headers/footers by default. **Gotcha:** a *fresh*
`--user-data-dir` often silently produces nothing (first-run setup); reuse a warmed profile
dir. Then copy the PDF where the user can open it.

## Capture tooling
Working scripts for this task live under `C:\Users\hatto\AppData\Local\Temp\mxb_render\`
(non-repo): `iterate.mjs` (inject candidate CSS + measure overflow + shoot one page),
`capture.mjs` (all 8 sizes × 4 pages), `capture_one.mjs` (re-do one size's pages with a fresh
reconnect), `measure_children.mjs` (marginBox child geometry), and `gallery_*.mjs` (build the
PDF gallery HTML). They are throwaway harness code — re-create or adapt as needed; the durable
knowledge is in this skill.

## Gotchas checklist
- Screenshots all identical → the layout/page API silently 400'd (Host: 127.0.0.1). Drive the
  API from inside the page over CDP.
- Orange highlight on text → Talking Book toolbox still open; close it AND strip `.ui-audioCurrent`.
- Red box / overflow indicator in a shot → usually an edit-view/talking-book artifact, not real
  overflow; confirm with a `scrollHeight - clientHeight` measurement before "fixing" CSS.
- Logo cut off at the bottom of a short page → fixed content overflows so `margin-top:auto`
  can't anchor it; cut fixed inter-section margins / tighten auxiliary-block leading.
- CSS edit not showing → recompiled LESS but the book kept its cached CSS copy; push the
  compiled `.css` into the book folder (or restart Bloom) and reload the page.
- Tempted to shrink a font to fit → don't; tighten spacing/leading instead, and if a block
  genuinely can't fit at its font, surface it to the user rather than silently shrinking.
