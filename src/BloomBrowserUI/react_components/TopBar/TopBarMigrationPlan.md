# Top Bar Migration Plan (TopBarAllReact)

## Goal
Replace the entire WinForms top tab bar (Messir TabStrip + scattered ReactControls) with **a single ReactControl** running one bundle: **`topBarBundle`**.

When finished:
- UI top bar is fully React.
- The only remaining “tab strip” concept is the **React tab buttons** already in `TopBar.tsx`.
- There are **no references to the legacy Messir TabStrip** anywhere.
- Old top bar bundles go away:
  - `collectionTopBarControlsBundle`
  - `editTopBarControlsBundle`
  - `workspaceTopRightControlsBundle`

## Architecture (target)
- `TopBar` (`TopBar.tsx`) is the root component for the whole bar.
- `TopBarControls` (new) lives inside the bar and:
  - swaps tool-specific controls (Collections/Edit/Publish)
  - always includes the right-side controls currently in `WorkspaceTopRightControls`
- A new JS bundle `topBarBundle` is loaded by WinForms via `ReactControl`.

## Milestones / Steps

### 1) Add `topBarBundle` (frontend)
- Add a Vite entry key `topBarBundle` in `src/BloomBrowserUI/vite.config.mts`.
- Add an entry file (e.g. `TopBar.entry.tsx`) that bootstraps `TopBar`.
- Update `TopBar.tsx` to wire up for WinForms using `WireUpForWinforms(TopBar)`.

**Done when:** Vite config contains `topBarBundle`, and we have a matching entry file.

### 2) Implement `TopBarControls` (frontend)
- Create `TopBarControls.tsx` under `react_components/TopBar/`.
- It should:
  - Render tool-specific controls based on active tab (from props + websocket updates)
    - Collections: `CollectionTopBarControls`
    - Edit: `EditTopBarControls`
    - Publish: empty (unless later specified)
  - Render `WorkspaceTopRightControls` on the right.
- `TopBar` should call `POST workspace/selectTab` when a tab is clicked.

**Done when:** switching tabs via React calls into C# and updates selected styling.

### 3) Add C# API endpoints for tab selection
- Add `POST workspace/selectTab` in `WorkspaceApi`.
- Implement actual switching in `WorkspaceView`.
- Provide initial active tab to React via `ReactControl.Props`.
- Send websocket bundle updates for active tab changes (new channel like `topBar`).

**Done when:** React can switch Workspace tabs reliably.

### 4) Replace WinForms top bar with one ReactControl
- Update `WorkspaceView` designer/code:
  - Remove Messir `_tabStrip` usage.
  - Remove `_toolSpecificTopBarPanel` usage.
  - Remove `_panelHoldingTopRightReactControl` usage.
  - Add a new `ReactControl` for the full top bar using `JavascriptBundleName = "topBarBundle"`.

**Done when:** UI top bar is a single ReactControl.

### 5) Remove old topbar ReactControls + bundles
- Remove the old host controls:
  - `CollectionTabView` top bar ReactControl
  - `EditingView` top bar ReactControl
  - `WorkspaceView` top-right ReactControl
- Remove old bundle keys from Vite config.
- Remove old bundle mappings from `ReactControl.cs`.

**Done when:** only `topBarBundle` remains for the top bar.

### 6) Remove legacy Messir TabStrip entirely
- Remove all usage/imports of the legacy Messir TabStrip.
- Update any dependencies (e.g. shell hit-testing) to not reference the old TabStrip.
- Remove or orphan `TabStrip.cs`/`TabStripRenderer.cs` if no longer used.

**Done when:** repo-wide search finds no Messir TabStrip references.

### 7) Build validation
- Run `dotnet build` and fix migration-related compile errors.

## Commits
We will commit at each milestone completion:
1. Add `topBarBundle` scaffold + entry/wire-up.
2. Implement `TopBarControls` + initial tab switching plumbing.
3. Replace WinForms top bar host in `WorkspaceView`.
4. Remove old bundles/hosts.
5. Remove Messir dependency entirely + build fixes.

## Progress Log
- 2025-12-18: Plan created.
- 2025-12-18: Milestone 1 in progress: added `topBarBundle` scaffold (Vite key + ReactControl mapping + entrypoint + TopBar WinForms wire-up).
- 2025-12-18: Milestone 2/3 in progress: added `TopBarControls` and `workspace/selectTab` endpoint.
- 2025-12-18: Milestone 4 done: `WorkspaceView` now hosts a single `ReactControl` using `topBarBundle` (Messir TabStrip removed).
- 2025-12-18: Milestone 5 done: removed legacy top-bar ReactControl hosts from `CollectionTabView` and `EditingView` (commit `ca7757c9c`).
- 2025-12-18: Milestone 5 continued: removed old top-bar bundle entries/mappings (`collectionTopBarControlsBundle`, `editTopBarControlsBundle`, `workspaceTopRightControlsBundle`).
- 2025-12-18: Milestone 6 continued: removed Messir `TabStrip` source files and cleaned `ClassDiagram1.cd` references.
