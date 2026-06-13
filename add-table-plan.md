 we are working on integrating Bloom table into Bloom. Information on how to automate Bloom is here: .github/skills/bloom-automation/SKILL.md. Manage the running bloom by running with ./go.sh and killing that as needed (will hot reload for web things, but probably not much c#). So far, we don't seem to have a toolbox control for the grid. We are in the Bloom repo, and the Bloom table repo is at D:\bloom-table.

 Two tools that may be helpful: playwright-cli, chrome-devtools (also a cli).

 Note it's best that we leave the feature gating off via experimental flags for now. It just gets in the way.

 **Ready for review**

Select text to add comments on the plan

# **Integrate bloom-grid into Bloom as a "Table" page section type + toolbox tool**

## **Context**

Bloom pages are a strict subset of HTML whose sections are currently text (`bloom-translationGroup`), image (`bloom-canvas`), video (`bloom-videoContainer`), and widget (`bloom-widgetContainer`). We're adding a new section type, **table**, powered by the `bloom-grid` library (d:\source\bloom-grid), consumed via npm. The library is a good fit because **all grid state lives in the DOM** (`div.grid` + `div.cell` with `data-*` attributes; `render()` in `src/grid-renderer.ts` reads them and writes inline styles), matching Bloom's HTML-persistence model — the Wall Calendar template proves arbitrary markup survives Bloom's save pipeline.

### **Decisions made (with user)**

1. **Text cells contain real `bloom-translationGroup`s** (multilingual, CKEditor-managed), not the library's bare contenteditable.
2. **Publish bloom-grid to the npm registry**; Bloom depends on a version; `yarn link` for local dev (like bloom-player).
3. **Gate behind an ExperimentalFeatures.cs token** (`kTables = "tables"`).
4. **Undo wires into Bloom's undo chain** in `workspaceRoot.handleUndo()` (like origami/reader tools).
5. **Keep the library's in-page overlay UI** (edge add/delete buttons, corner drag handle), with an `attachGrid` option to disable as an escape hatch.
6. **v1 cell content types: text + image** (image template = Bloom `bloom-canvas`, so Bloom's image tooling works in cells). Video and nested-grid cells deferred.
7. **Bloom is being upgraded to React 18** (currently `react@^17.0.2`, `BloomBrowserUI/package.json:156`); plan assumes React 18 is in place. → The toolbox tool **hosts the library's own `GridMenu` React components** rather than rebuilding the panel.

### **Key verified facts**

- **MUI/Emotion bundling**: bloom-grid currently *bundles* MUI 5/Emotion (~600KB) into its dist while Bloom already ships MUI `^5.15.19`/Emotion 11. To host `GridMenu` in Bloom without duplicate copies (broken Emotion theming, doubled bundle), the library must externalize `@mui/material`, `@mui/icons-material`, `@emotion/react`, `@emotion/styled` as peerDependencies.
- **Cross-iframe events**: `GridMenu`/demo Toolbar listen for `gridHistoryUpdated` on their own frame's `document`, but in Bloom the toolbox runs in a separate iframe from the page where grids dispatch events. The library components need a way to target the page frame's document (new library work item, A6).
- **Overlay leak**: `table-size-buttons.ts` appends overlay divs to `document.body` of the page frame; `detachGrid` (`src/attach.ts:45-52`) does not remove them; Bloom saves `document.body.innerHTML` → they'd be persisted. A cleanup hook is required.
- **Hint styles leak**: `grid-renderer.ts` sets transient `-hint-*-color` inline styles on cells; the last render before save can leave them set.
- **Static rendering works**: `render()` persists `grid-template-columns/rows`, borders, corners as inline styles, so published books (bloom-player/PDF, no JS) render correctly *if* the structural CSS (`div.grid{display:grid}`, span vars) ships in book stylesheets.
- **Undo**: `gridHistoryManager` singleton (`src/history.ts:222`), innerHTML-snapshot stack (max 50, no redo), fires `gridHistoryUpdated` CustomEvent on `document`.
- **Cell types**: `defaultCellContentsForEachType` (`src/cell-contents.ts:24`) — `{id, englishName, templateHtml, regexToIdentify, icon}`, identified via `data-content-type` + regex; each cell has exactly one root child. No runtime registration API yet.

---

## **DONE: Part A — Library changes (d:\source\bloom-grid), Milestone 1**

### **A1. Runtime cell-content-type registration API — `src/cell-contents.ts`**

```tsx
export function registerCellContentType(type: CellContentType, opts?: { makeDefault?: boolean })
export function setDefaultCellContentTypeId(id: string)
```

Replaces/extends entries in `defaultCellContentsForEachType` by id. Export from `src/index.tsx`.

After `setupContentsOfCell` inserts content (and from the new-cell paths in `structure.ts` when rows/columns are added), dispatch a bubbling event so the host can post-process new content:

```tsx
cell.dispatchEvent(new CustomEvent("gridCellContentChanged", { bubbles: true, detail: { cell, contentType } }));
```

Ensure add-row/add-column creates **fresh template content** for new cells (not clones of neighbor cells' innerHTML), so Bloom cells don't carry copied text/CKEditor debris.

### **A2. "Prepare for save" cleanup hook — new `src/prepare-for-save.ts`**

```tsx
export function removeGridEditingArtifacts(root: ParentNode = document): void
```

Removes: `[data-overlay-group]` divs, corner-handle/proximity divs (tag them with a stable `data-grid-overlay` attribute in `table-size-buttons.ts` — currently unmarked), `--hint-*-color` inline properties on cells, and selection classes (`cell--selected`, `grid--selected`; audit `selection-highlight.ts` for the full list). Export from index.

### **A3. Overlay opt-out + undo convenience**

- `attachGrid(gridDiv, options?: { overlays?: boolean })` — when `overlays: false`, skip `ensureTableSizeButtons()` (escape hatch per decision 5; default true).
- `gridHistoryManager.undoLast(): boolean` — undo on any attached grid without the caller holding a grid reference.

### **A4. Packaging hygiene — `package.json`, `vite.config.ts`, `table-size-buttons.ts`**

- **Externalize `@mui/material`, `@mui/icons-material`, `@emotion/react`, `@emotion/styled`** in `vite.config.ts` and move them to `peerDependencies` (alongside react/react-dom 18) so Bloom's copies are used and the bundle shrinks ~600KB. Bloom satisfies all of them already.
- Optionally replace the MUI icon + `react-dom/server` `renderToStaticMarkup` usage in `table-size-buttons.ts` (:15-18) with inline SVG strings — keeps the core attach path framework-free for other consumers; nice-to-have now that Bloom provides React 18.
- Move `jsdom` from `dependencies` to `devDependencies`.
- Add an `exports` map (`"."` → mjs/umd/types; `"./bloom-grid.css"`).
- Publish as `bloom-grid@1.1.0` once A1–A6 land.

### **A5. CSS split — `src/bloom-grid.css`**

- **Structural/read-time** (`div.grid{display:grid}`, `-span-x/y` plumbing, cell padding/bg defaults, `.cell.skip{display:none}`): must end up in Bloom's *book* stylesheets (B5).
- **Edit-only** (selection outlines, hint gradients, overlay styling): separate file/section for Bloom's edit-mode stylesheet (auto-stripped on save by filename convention).
- Default `-grid-hint-color: transparent` (defense in depth if a hint style survives).

### **A6. Make GridMenu usable across iframes — `src/components/GridMenu.tsx` + sections**

Bloom renders `GridMenu` in the toolbox iframe while grids live in the page iframe. Today `GridMenu` (`GridMenu.tsx:16`) and its sections listen for `gridHistoryUpdated` on their own frame's `document` and locate the selected grid/cell via their own document. Add a prop/context for the **target document** (the page frame's document):

```tsx
<GridMenu targetDocument={pageDoc} />  // defaults to window.document
```

All internal `document.addEventListener("gridHistoryUpdated", ...)` and selection queries (`.cell--selected`, `.grid--selected`) use `targetDocument`. The structure/BloomGrid functions are pure DOM ops and already work cross-realm (avoid `instanceof HTMLElement` checks; use duck-typing/`nodeType`).

**M1 verification**: library tests green (`yarn test`, e2e); demo still works; dist bundle contains no MUI/Emotion (grep dist); DOM snapshot after `removeGridEditingArtifacts` shows no overlays/hints/selection classes; demo exercises `GridMenu` with an explicit `targetDocument`.

---

## **Part B — Bloom changes (worktree `D:\bloom.worktrees\Add-Tables`, branch `Add-Tables`)**

### Current status — session 2026-06-13 (B6/M3 done)

**Done:** B1 (origami), B2 (`tableEditing.ts`), B3 (save cleanup), B4 (C# tokens + migration verified generic), B5 (basePage.less structural CSS), **B6 (toolbox TableTool — DONE & runtime-verified, see below)**, B7 (undo chain), **B8 (both l10n strings — DONE)**, B9 (npm `yarn link`). Lint clean on touched TS (0 errors).

**Remaining:** Publish bloom-table (still `1.0.0`, linked not published) + turn Bloom's `package.json` link comment into a real `^1.1.0` dep. Optional polish: image-cell tooling inside grid, video/nested-cell deferral, feature-gate flip (`isExperimental()`/origami link) once shipping. **R5 fully resolved (mono + multilingual).** Both repos committed 2026-06-13 (bloom-table NOT published per user).

**Files changed, Bloom branch `Add-Tables` (uncommitted):** `tableEditing.ts`, `bloomEditing.ts`, `origami.ts`, `editablePage.ts` (+`getTableApi`, +`tableCanUndo`/`tableUndo`/`getTableApi` on `IPageFrameExports`), `workspaceRoot.ts`, `toolbox/table/tableTool.tsx` (new), `toolbox/table/tableToolPageBridge.ts` (new), `toolbox/toolboxBootstrap.ts`, `toolbox/toolIds.ts` (+`kTableToolId`), `toolbox/settings/Settings.pug`, `package.json`, `ExperimentalFeatures.cs`, `FeatureRegistry.cs`, `basePage.less`, `Bloom.xlf`, `add-table-plan.md`.

**Files changed, bloom-table worktree `D:\bloom-table.worktrees\table-api-injection` branch `table-api-injection` off master (uncommitted):** NEW `src/components/TableApiContext.tsx` (TableApi interface + defaultTableApi + context + useTableApi); refactored `TableMenu.tsx` (accepts `tableApi` prop, hosts provider, history listener now binds `currentCell.ownerDocument`), `RowSection.tsx`, `ColumnSection.tsx`, `CellSection.tsx`, `TableSection.tsx` (all call `useTableApi()` instead of static imports); `index.tsx` (exports `defaultTableApi`/`useTableApi`/`TableApiContext`/`TableApi`); `BorderControl.test.ts` (passes `defaultTableApi`). `vp run typecheck` clean, `vp test run` 115/115 pass, `vp pack` builds (MUI/Emotion stay external). Demo still passes no `tableApi` → uses default. **Nothing committed in either repo.**

**Env notes:** B6 work is in the **bloom-table worktree off master** at `D:\bloom-table.worktrees\table-api-injection` (deps via `vp install`; build `vp pack`; test `vp test run`). The global yarn link `…/Yarn/Data/link/bloom-table` was repointed (a Windows **junction**) from `D:\bloom-table` to this worktree, so `BloomBrowserUI/node_modules/bloom-table` now resolves the worktree build. (The old `D:\bloom-table` checkout has unrelated WIP and is no longer the link target.) Run Bloom with `./go.sh` (hot-reloads web; restart for C# AND after relinking/rebuilding the library so Vite re-optimizes the dep). Do NOT run `yarn build`; `yarn lint`/`eslint`/`tsc --noEmit` fine. Feature gating intentionally OFF (`isExperimental()` has no consumers in the TS codebase, so it doesn't hide the tool; visibility = the Settings.pug "More…" checkbox).

**M2 round-trip VERIFIED at runtime (2026-06-13)** via Bloom.exe + CDP (go.sh, HTTP 8089/CDP 8091). Typed distinct text into all 4 cells of a 2×2 table → switched to Collections and back to Edit (forces save+reload) → reopened page. Results:
- Text survived: all four `RTcellZero/One/Two/Three` present in the live reloaded DOM **and** in the saved `Book-e3314545.htm` on disk.
- Structure/widths intact: `data-column-widths="hug,hug"`, `data-row-heights="hug,hug"`, inline `grid-template-columns: minmax(60px,max-content) ...` persisted (→ static bloom-player/PDF rendering works).
- `SetupTableEditing` attaches cleanly (`data-table-attached="1"` on reload; editables wired).
- **Save cleanup (B3) confirmed**: saved HTML has **zero** `table--selected`/`cell--selected`, zero `data-overlay-group`/`data-grid-overlay`, zero `--hint-*-color` inline styles, zero `data-table-attached`. (`table--selected`/`cell--selected` reappear in the *live* DOM after reload because attach re-adds them — they are NOT persisted.)
- **R5 RESOLVED (monolingual)**: each runtime-created `bloom-translationGroup` cell has its `bloom-editable` (lang="en") after reload — C# `TranslationGroupManager` populates editables in grid cells at page-build time. ⚠ Still only verified on a **bloom-monolingual** book (1 editable/cell); the multilingual case (multiple `bloom-editable`s per cell for multiple collection languages) is inferred-good but not directly exercised.

**B7 undo VERIFIED at runtime (2026-06-13)**: clicked the in-page bottom-edge add-row overlay button (rows `hug,hug`→`hug,hug,hug`, cells 4→6, `tableCanUndo()`→true, `workspaceBundle.canUndo()`→`"yes"`), then invoked `window.workspaceBundle.handleUndo()` (the exact entry point C# calls) in the top frame → table reverted cross-frame to 2×2 (cells 6→4, texts intact, `canUndo()`→`"fail"`). Confirms the top-frame→page-frame routing and the C# toolbar-enable poll. (Native Ctrl+Z is a C# keyboard accelerator that synthetic CDP keystrokes don't reach, but it invokes this same `handleUndo()`.)

**B6 / M3 toolbox TableTool VERIFIED at runtime (2026-06-13)** via Bloom.exe + CDP. Enabled "Table Tool" from the toolbox "More…" list and activated its accordion; the library's `TableMenu` hosts inside the toolbox React root with **no console errors** (single React 18 + single MUI/Emotion via peerDeps — no "multiple React"/hook errors). Clicking a page-frame cell sets `currentCell` cross-frame (focusin listener bound to the page document) and the full menu (Table/Row/Column/Cell sections) renders. **Cross-frame op routing confirmed** (the core B6 blocker): "Insert Row Below" from the panel → page-frame table rows 2→3, cells 4→6, `tableCanUndo()`→true; "Insert Column Right" → cols 2→3, cells→9 (3×3). Then `workspaceBundle.handleUndo()` (C# entry point) undid both in order back to the original 2×2, cell texts preserved, `canUndo()`→`"fail"`. The injected page-frame `TableApi` (via `editablePageBundle.getTableApi()`) makes the toolbox-hosted handlers execute in the realm where the table is attached and `tableHistoryManager` records — resolving the silent-no-op blocker.

**R5 multilingual VERIFIED at runtime (2026-06-13, French added to collection):** with en+fr active, existing table cells show **both `en` and `fr`** `bloom-editable`s after reload. A freshly added row's cells start with an **empty** translationGroup (no editables immediately — `BloomField.ManageField` does not synthesize per-language editables at runtime), but after save+reload C# `TranslationGroupManager` populates every cell (old and new) with en+fr editables. So R5 is fully resolved: fresh cells get their per-language editables via the page-build reload, multilingual included. (Acceptable; matches the plan's expected fallback.)

R5 fully closed — no remaining runtime-unverified items in M1–M4.

---

**Container markup decision**: the origami container *is* the grid — `<div class="bloom-grid grid" data-column-widths=... >`. No wrapper div: the library keys off `.grid`/`.cell`, and origami detection is class-based, so adding `bloom-grid` to the container-class list suffices. `bloom-grid` is the Bloom-recognized marker; `grid` is the library's.

### **B1. Origami "Table" type — `src/BloomBrowserUI/bookEdit/js/origami.ts` (M2)** ✅ DONE

- `.bloom-table` added to `bloomContainerClasses`.
- `createTypeSelectors()`: "Table" link added, always shown (no feature gate — decided to keep ungated for development convenience).
- `makeTableFieldClickHandler`: appends `<div class='bloom-table table bloom-leadingElement'>`, calls `AttachNewTable`.
- `isSplitPaneComponentInnerEmpty` and `doesSplitPaneComponentNeedTextBoxIdentifier` work correctly.

### **B2. `SetupTableEditing` — new `src/BloomBrowserUI/bookEdit/js/tableEditing.ts`, called from `SetupElements` in `bloomEditing.ts` (~:499-518) (M2)** ✅ DONE

> **Naming note**: the published library is `bloom-table` (not `bloom-grid`). Container class `.bloom-table`; library structural classes `.table`/`.cell`; save-cleanup export `removeTableEditingArtifacts`; React panel `TableMenu`; cell-content event `kTableCellContentChangedEvent`; history singleton `tableHistoryManager`. The plan's `bloom-grid`/`GridMenu`/`removeGridEditingArtifacts`/`gridHistoryManager` names map onto these.

Implemented in `tableEditing.ts`: registers `translationGroup` (default) + `image` content types; `SetupTableEditing(container)` adds the `kTableCellContentChangedEvent` listener and calls `attachTable` on every `.bloom-table` (guarded by `data-table-attached`); `onTableCellContentChanged` runs `BloomField.ManageField` on translationGroup editables and `SetupImagesInContainer` on image cells. R5 (per-language editable creation for a runtime-created empty translationGroup) still needs a manual round-trip test in Bloom.

- One-time: `registerCellContentType` for:
    - `translationGroup` (default): `templateHtml` = empty `<div class='bloom-translationGroup bloom-trailingElement normal-style'>`, regex matching `bloom-translationGroup` class.
    - `image`: `templateHtml` = a `bloom-canvas` structure (copy from `origami.ts` :519-534 / `CanvasElementFactories.ts`), regex matching `bloom-canvas`.
- For each `.bloom-grid` in the container: `attachGrid(g)` with a `data-grid-attached` guard (toolbox calls `newPageReady` twice — once immediately, once after 600ms).
- Listen for `gridCellContentChanged` (bubbles to container): run `SetupElements`style wiring on new content — for translationGroups, materialize `bloom-editable` children + CKEditor attach; for images, `SetupImagesInContainer`.
- **Open question to resolve during M2 (Risk R5)**: whether a runtime-created empty `bloom-translationGroup` gets its per-language `bloom-editable` divs without a page reload. `TranslationGroupManager` (C#) normally does this at page-build time. If `SetupElements`/`BloomField.ManageField` doesn't create them, add a server round-trip (precedent: video/widget handlers calling C# endpoints) or replicate minimal editable creation in JS.

### **B3. Save cleanup — `bloomEditing.ts` `removeEditingDebris()` (~:1222) (M2)** ✅ DONE

Implemented in `bloomEditing.ts removeEditingDebris()`: calls `removeTableEditingArtifacts(document)` then `TeardownTableEditing(document.body)` (which `detachTable`s each `.bloom-table`).

Call `removeGridEditingArtifacts(document)` (A2). On page unload, `detachGrid` each `.bloom-grid`. Saved HTML must contain no `data-overlay-group`/`data-grid-overlay` divs, no hint inline styles, no selection classes.

### **B4. C# changes (M2)**

**Status (partial):** ✅ `ExperimentalFeatures.cs` `kTables = "tables"`; ✅ `FeatureRegistry.cs` `FeatureName.Table` (Basic tier, `ExistsInPageXPath` `.//div[contains(@class,'bloom-table')]`). ✅ **Verified** `HtmlDom.GetEditableDataCounts`/`MigrateEditableData` use generic translationGroup/canvas traversal (`GetTranslationGroupsNotInCanvasElements`, `MigrateChildrenWithCommonClass(kBloomCanvasClass)`) — table cells are ordinary translationGroups/canvases, counted and migrated without special-casing; no `.bloom-table` awareness needed. ⏳ `TranslationGroupManager.cs` page-build editable population inside grid cells still needs a runtime check (ties to R5).

- `src/BloomExe/ExperimentalFeatures.cs` (~:11-13): add `kTables = "tables"` (already served generically via `/bloom/api/app/enabledExperimentalFeatures`).
- `src/BloomExe/Book/HtmlDom.cs`: verify `GetEditableDataCounts` (~~:895) and `MigrateEditableData` (~~:998) handle translationGroups inside `.bloom-grid` cells (they're standard translationGroups, so generic traversal likely works — verify, and add `.bloom-grid` awareness only if the code special-cases container classes).
- Verify `TranslationGroupManager.cs` populates editables inside grid cells at page-build time.

### **B5. Book CSS — `src/content/bookLayout/basePage.less` (M2)** ✅ DONE

Structural/read-time styles added to `basePage.less` (`.table{display:grid}`, `.cell` span vars/padding/bg, `.cell.skip{display:none}`, table cell-content nesting). Edit-only styles ship via the library's `bloom-table-edit.css`.

Add the structural half of bloom-grid's CSS (A5) so grids render at edit time AND in published books (bloom-player/PDF run no grid JS; inline styles from `render()` carry templates/borders, but `display:grid` and span vars must come from CSS). Edit-only CSS goes in an edit-mode stylesheet (filename containing "edit" → auto-stripped by `HtmlDom.RemoveModeStyleSheets()` :437).

### **B6. Toolbox TableTool (M3)**

**⚠ BLOCKER — cross-iframe history singleton (needs decision before building).** Bloom's toolbox and the editable page are **separate iframes / JS realms**, so each loads its own copy of the `bloom-table` module and its own `tableHistoryManager` singleton. `attachTable` (called by `SetupTableEditing`) runs in the **page** frame, so only the page frame's manager has the table in its `attachedTables` set. The library's structure ops wrap every mutation in `tableHistoryManager.addHistoryEntry`, which **returns early without performing the mutation** when the table isn't attached in that frame's manager (`history.ts:55-60`). Therefore a `TableMenu` hosted in the **toolbox** frame, calling the library directly (as the demo `Toolbar.tsx` does), would **silently no-op** every structural edit. The library's A6 work added a `currentCell` prop (solves cross-frame *selection*) but not operation-routing, so hosting `TableMenu` in the toolbox is not viable as-is.

Resolution options considered:
1. **Render `TableMenu` in the page frame** (precedent: `renderDragActivityTabControl`). Keeps everything consistent but the panel would not sit in the toolbox column. **Rejected** — not how other tools work.
2. **Library change: inject page-frame ops into `TableMenu`.** Host the library panel in the toolbox; inject an operations object obtained from the page frame so handlers run in the page-frame realm. **CHOSEN.**
3. **Thin toolbox panel** that calls page-frame ops directly. No library change, but duplicates the UI into Bloom. **Rejected** — duplicated UI across two repos drifts; higher long-term maintenance.

#### ✅ DECISION: Option 2, with the bias "put logic in bloom-table, keep Bloom thin"

**Why:** the table-editing UI belongs with the table semantics (one source of truth = the library's `TableMenu`); Bloom only supplies cross-frame plumbing. This matches the **Canvas-tool bridge pattern**: the toolbox renders the React UI but delegates page-mutating operations to a **live object obtained from the page frame**. See `toolbox/canvas/canvasElementPageBridge.ts` — it does `import type { CanvasElementManager }` (type-only, so the toolbox bundle does NOT pull in the implementation) and gets the live instance via `getEditablePageBundleExports().getTheOneCanvasElementManager()`. Because that object was *created* in the page frame, its methods close over the page-frame module scope, so they use the page-frame `tableHistoryManager` (the one with the attached table). Repo-landing note: landing changes in the main Bloom repo is slow/hard; bloom-table is easy to iterate + `yarn link`, so prefer the library for any code that could live in either place.

#### Implementation design for Option 2 (next session)

**Library side (D:\bloom-table) — the bulk of the work:**

The components currently call library operations via **static imports**, so a toolbox-hosted copy uses the toolbox's (unattached) module. Make them operate through a single **injected table-API object** instead. Cleanest seam = one object, injected via React context, defaulting to the real module so the **demo `Toolbar.tsx` keeps working unchanged**.

- Operations the panel needs (audit results — these must be routed through the injected API):
  - From `index`/`structure` (re-exported via `import * as Table from "../"`): `getRowIndex`, `getRowAndColumn`, `canUndo`, `undoLastOperation`, `getTargetTable`.
  - `BloomTable` class methods: `getSpan`, `setSpan`, `addRowAt`, `removeRowAt`, `addColumnAt`, `removeColumnAt` (constructed per-table in handlers — `new BloomTable(table)`).
  - `setupContentsOfCell` (from `cell-contents`).
  - `render` (from `table-renderer`) — used by `CellSection`/`TableSection` after border edits.
  - `edge-utils`: `applyCellPerimeter`, `ensureEdgesArrays`, `applyUniformInner`, `setDefaultBorder`, `applyOuterBorders`.
  - `border-state`: `getCellPerimeterValueMap`, `getTableOuterBorderValueMap`.
  - `cell-contents`: `contentTypeOptions`, `getCurrentContentTypeId`.
  - History/event: `tableHistoryManager` (for `canUndo`/label and the `tableHistoryUpdated` listener — note that listener currently binds the toolbox `document`; for re-render in Bloom rely on the `MutationObserver` on the table element, which is cross-realm-safe, OR also accept a `targetDocument`).
- Recommended shape: a `TableApi` interface bundling the above as plain functions/factory (`makeController(table) => BloomTable`-like). Provide `TableApiContext` + `useTableApi()`; default value = an object built from the static imports. `TableMenu` accepts an optional `tableApi` prop and wraps children in the provider. **Components stop importing operations directly and call `useTableApi()`** (border-state/edge-utils/render too — currently NOT exported from index, so either export them or include them in the API object).
- Touched components: `TableMenu.tsx`, `RowSection.tsx`, `ColumnSection.tsx`, `CellSection.tsx`, `TableSection.tsx`, `SelectedCellInfo.tsx`, `BorderControl/*` (verify what it calls). Keep changes mechanical: swap static call → `api.xxx`.
- Verify with `yarn test` + e2e + demo still works (demo passes no `tableApi`, gets the default). Bump to `1.1.0`, rebuild, relink.

**Bloom side (thin):**

- In `editablePage.ts` (page frame), build the `TableApi` object from the page-frame `bloom-table` module and expose it on `window.editablePageBundle`, e.g. `getTableApi(): TableApi`. (Just re-export the page-frame module's functions/factory — they close over the right realm.) Add to `EditablePageBundleApi` interface + the object literal.
- New `toolbox/table/tableTool.tsx`: render the library `TableMenu` with `tableApi={getEditablePageBundleExports().getTableApi()}` and `currentCell` = the page-frame's selected cell. Track `currentCell` via a `focusin` listener on the **page-frame document** (mirror demo `Toolbar.tsx` but bind `pageDoc`, not toolbox `document`). Wrap in Bloom's MUI `ThemeProvider`.
- New `toolbox/table/tableToolPageBridge.ts` (mirror `canvasElementPageBridge.ts`): `import type { TableApi }` + getter via `getEditablePageBundleExports()`.

New `src/BloomBrowserUI/bookEdit/toolbox/table/tableTool.tsx`:

- `class TableTool extends ToolboxToolReactAdaptor` — `id() = "table"`, `isExperimental() = true` (pattern: `GameTool.tsx:1770`), `requiresToolId() = false`.
- Register in `toolboxBootstrap.ts` (~:133): `ToolBox.registerTool(new TableTool())`. Add checkbox entry in `toolbox/settings/Settings.pug`.
- Panel: **host the library's `TableMenu`** inside the tool's React root, wrapped in Bloom's MUI theme provider, passing the injected `tableApi` (page-frame) + `currentCell`. MUI/Emotion resolve to Bloom's copies via peerDependencies (A4).
- `newPageReady()`: re-bind the `focusin` listener / `currentCell` at the (possibly new) page iframe document; tables are attached by `SetupTableEditing` in the page frame.
- `detachFromPage()`: nothing to detach for the menu (tables detached by `TeardownTableEditing`); remove the `focusin` listener.

New `src/BloomBrowserUI/bookEdit/toolbox/table/tableTool.tsx`:

- `class TableTool extends ToolboxToolReactAdaptor` — `id() = "table"`, `isExperimental() = true` (pattern: `GameTool.tsx:1770`), `requiresToolId() = false`.
- Register in `toolboxBootstrap.ts` (~:133): `ToolBox.registerTool(new TableTool())`. Add checkbox entry in `toolbox/settings/Settings.pug`.
- Panel: **host the library's `GridMenu`** (Table/Row/Column/Cell sections + BorderControl) inside the tool's React root, wrapped in Bloom's MUI theme provider, passing `targetDocument` = the page iframe's document (A6). MUI/Emotion resolve to Bloom's copies via peerDependencies (A4). The grid/cell selection state comes from the library's `cell--selected`/`grid--selected` classes maintained in the page frame.
- `newPageReady()`: re-point `targetDocument` at the (possibly new) page iframe document; grids themselves are attached by `SetupTableEditing` in the page frame.
- `detachFromPage()`: `detachGrid` all grids on the page.
- If GridMenu's look clashes badly with the toolbox styling, theme it via the wrapper first; only fork/rebuild individual sections as a last resort.

### **B7. Undo chain (M4)** ✅ DONE

Implemented: `editablePage.ts` exports `tableCanUndo()`/`tableUndo()` (wrapping `tableHistoryManager.canUndo()`/`undoLast()`), added to `EditablePageBundleApi` + `window.editablePageBundle`. `workspaceRoot.ts handleUndo()` checks table undo **first** and returns; `canUndo()` mirrors it. Both run in the page frame where the tables are attached.

- `src/BloomBrowserUI/bookEdit/editablePage.ts`: export `gridCanUndo()` / `gridUndo()` (wrapping `gridHistoryManager.canUndo()` / `undoLast()`); add to `EditablePageBundleApi` and `window.editablePageBundle` (~:375-429).
- `src/BloomBrowserUI/bookEdit/workspaceRoot.ts`: in `handleUndo()` (~~:95-110) check grid undo **first** (before origami/ckeditor — structural grid ops have no CKEditor footprint; pure text edits leave `gridCanUndo()` false and still route to CKEditor). Mirror in `canUndo()` (~~:233) so the C# poll (`WebView2Browser.cs:840`) enables the toolbar button.

### **B8. Localization (M3)**

**Status (partial):** ✅ `EditTab.CustomPage.Table` added to `DistFiles/localization/en/Bloom.xlf` (`translate="no"`, dynamic). ⏳ `EditTab.Toolbox.TableTool` depends on B6 (the toolbox tool) and is deferred until the B6 approach is chosen.

- `EditTab.CustomPage.Table` for the origami link (mirror how `EditTab.CustomPage.Video` is referenced — search for it and copy the pattern).
- `EditTab.Toolbox.TableTool` for the tool name (the `EditTab.Toolbox.<Id>Tool` convention). New keys need only the `data-i18n`/`useL10n` reference + English default; the l10n build harvests them.

### **B9. npm dependency (M2)** ✅ DONE

`package.json` has the `bloom-table` link comment; `node_modules/bloom-table` is `yarn link`ed to the local repo. (Becomes a real `^1.1.0` dependency once published.)

`BloomBrowserUI/package.json`: add `"bloom-grid": "^1.1.0"` (Yarn 1). Local dev: `yarn link` in bloom-grid, `yarn link bloom-grid` in BloomBrowserUI (precedent: bloom-player comment at package.json:41-43). Vite (`vite.config.mts`) resolves the ESM build via the new exports map.

---

## **Milestones**

|  | Scope | Done when |
| --- | --- | --- |
| **M1** | Library hardening: A1–A6; publish `bloom-grid@1.1.0` | Lib tests green; MUI/Emotion externalized (not in dist); cleanup hook verified by DOM snapshot; GridMenu works with explicit `targetDocument` |
| **M2** | Section type + round-trip: B1–B5, B9 (flag-gated; translationGroup + image cells) | Add Table via origami → type multilingual text in cells → save → reopen: structure, widths, borders, text all survive; saved HTML clean; flag off → no Table link; bloom-player/PDF render statically |
| **M3** | Toolbox TableTool hosting GridMenu: B6, B8 | All structural ops work from the panel and persist; no "multiple React"/hook-call console errors (single React 18 + single MUI/Emotion) |
| **M4** | Undo + polish: B7, risk burn-down | Ctrl+Z undoes grid ops (walks stack); text undo still → CKEditor; C# Undo button enables correctly; thumbnails clean |

## **Verification strategy**

- **M1**: bloom-grid's existing vitest/playwright suites + a bundle-content check (grep dist for `react`/`mui`).
- **M2**: manual round-trip in Bloom (edit → save → reopen → publish preview); inspect saved page HTML in the book folder for cleanliness; flag on/off check.
- **M3/M4**: manual toolbox + Ctrl+Z testing; check WebView2 console for React errors; regenerate page thumbnails.

## **Risks to watch**

- **R5 (RESOLVED 2026-06-13, mono + multilingual)**: runtime-created translationGroups may need a C# round-trip to get per-language `bloom-editable`s — resolve early in M2, it's the main unknown.
- **R1**: this plan assumes the **React 18 upgrade of BloomBrowserUI lands first** (separate effort). M1 (library) can proceed in parallel, but M3 (hosting GridMenu) depends on it. Emotion/MUI version skew between Bloom (`^5.15.19`) and bloom-grid's peer ranges must stay compatible — pin peer ranges loosely (`^5`).
- **R2**: CKEditor inside `display:grid` cells — `render()` mutates only inline styles, not cell innerHTML, so CKEditor DOM should be stable; `doCkEditorCleanup` queries `div.bloom-editable` globally so save-cleanup covers cells.
- **R3/R4**: overlay drag handlers vs CKEditor/qtip, and CSS anchor positioning support in Bloom's WebView2 — if these bite, flip to `attachGrid(g, {overlays:false})` (A3 escape hatch) and rely on the toolbox panel.
- **R6**: add-row must create fresh template cells, not clones (addressed in A1).
- Image cells: ensure the `bloom-canvas` template plays well with Bloom's canvas-element manager inside a grid cell; if it fights, fall back to a simpler `bloom-imageContainer`style template for v1.
