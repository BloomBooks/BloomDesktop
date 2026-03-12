# Reactify Plan

## Goal

Make `http://localhost:8089/bloom/CURRENTPAGE` behave like a usable browser-hosted Bloom workspace: top-level tab changes should switch the visible shell, and normal navigation should continue working without requiring the desktop host to imperatively poke the embedded browser.

This is not a "rewrite Bloom into one React app" plan. It is an incremental shell migration plan that preserves the working C# backend and existing iframe islands while moving more orchestration into browser-owned state.

## What We Did

Completed in this branch so far:

- Added browser-owned workspace shell synchronization in `workspaceRoot.ts`:
  - initial fetch of `workspace/tabs`
  - websocket subscription to `workspace` / `tabs`
  - in-browser shell mode updates via `setWorkspaceMode(...)`
- Added edit iframe source propagation to the `workspace/tabs` payload in `WorkspaceView.cs`.
- Split the workspace tab payload implementation into:
  - plain object for the HTTP API response
  - `DynamicJson` wrapper for websocket publishing
  so `BloomWebSocketServer.SendBundle()` can still attach routing fields without crashing.
- Fixed a regression where stale `pageSrc` / `pageListSrc` URL params could win over the current server-provided edit iframe URLs after reload.
- Verified that the browser shell now updates both iframe sources to the current book and that the current page and page-list memsim URLs return HTTP 200.
- Fixed an unrelated React warning by stopping `disableEventsInIframe` from leaking onto a DOM element in `CollectionsTabBookPane.tsx`.

## Current State

- Phase 1 is implemented.
- The browser shell can now follow top-level workspace tab state without depending solely on desktop-hosted JS injection.
- The edit shell can restore the correct `pageSrc` and `pageListSrc` after reload even if stale URL params are present.
- The current desktop-hosted imperative sync path still exists, but browser-owned sync is now active and working.

## Verified Current Architecture

- `CURRENTPAGE` is the workspace root document, produced from `WorkspaceRoot.html` or `WorkspaceRoot.vite-dev.html` by `WorkspaceView.GetWorkspaceRootDocument()`.
- The workspace root already contains three top-level hosts:
  - `#topBar` iframe
  - `#collectionTab` iframe
  - `#publishTab` iframe
  - the legacy edit workspace container with `#pageList`, `#page`, and `#toolbox`
- The top bar is already React and uses `workspace/tabs` plus the `workspace` websocket context.
- `workspace/selectTab` already works in C# and calls `WorkspaceView.ChangeTab()`.
- `WorkspaceView.ChangeTab()` already lazy-loads collection/publish iframe bundles and updates tab state.
- The current browser shell mode is still switched by an imperative desktop-only call:
  - `WorkspaceView.SyncWorkspaceRootModeToTab()` runs `workspaceBundle.setWorkspaceMode(...)` through `_mainBrowser.RunJavascriptFireAndForget(...)`.

## Verified Browser Gap

Observed with Bloom running and `CURRENTPAGE` opened in a plain browser:

- Clicking `Edit` in the top bar updates the top bar state.
- The outer page remains in `collection-mode`.
- The URL remains `?mode=collection`.
- The collection iframe stays visible and the edit shell stays hidden.
- Manually calling `workspaceBundle.setWorkspaceMode('edit')` in the page immediately fixes the shell:
  - body class changes to `edit-mode`
  - URL changes to `?mode=edit`
  - the edit workspace becomes visible

Conclusion: the first blocker is not missing rendering code. The blocker is that the workspace root does not observe active-tab changes on its own; it depends on the desktop host to mutate the embedded browser directly.

## Constraints

- Keep at least two iframe islands for now:
  - edit page content
  - publish PDF display
- Do not force the backend to support multiple simultaneous "current" views yet.
- Preserve lazy loading and code splitting:
  - do not eagerly load edit and publish surfaces at startup
  - continue splitting large optional edit features, especially game-related code
- Favor incremental, testable steps over a shell rewrite.

## Phase 1: Make Workspace Root Browser-Reachable

Objective: make top-level tab selection work in a plain browser using existing APIs.

Status: completed.

Tasks:

- Teach `workspaceRoot.ts` to observe `workspace/tabs` directly.
- On initial load, fetch `workspace/tabs` and derive the active tab.
- Subscribe to the `workspace` websocket context and react to `tabs` updates.
- When the active tab changes, call `setWorkspaceMode(...)` from inside the page.
- Keep the current C# `RunJavascriptFireAndForget()` path temporarily; browser-owned sync should be additive and idempotent.

Success criteria:

- Opening `CURRENTPAGE` in a browser shows the same top-level visible tab as Bloom.
- Clicking `Collections`, `Edit`, and `Publish` in the browser changes the visible shell correctly.
- The URL `mode` query string tracks the actual visible mode.

## Phase 2: Stabilize Navigation Through Existing Shell

Objective: make ordinary navigation continue to work after tab switches.

Status: partially completed.

Tasks:

- Verify that collection actions which lead into edit or publish still work when initiated from the browser.
- Identify any remaining shell transitions that currently depend on desktop-only browser injection.
- Replace those with API or websocket-driven browser-owned reactions where practical.
- Add lightweight diagnostics around shell transitions if needed.

Likely focus areas:

- book selection leading into edit
- commands that return from publish to collection
- mode-specific URL restoration for `pageSrc` and `pageListSrc`

Completed in this phase so far:

- `pageSrc` / `pageListSrc` are now carried by `workspace/tabs`.
- Browser-side edit iframe restoration is now synchronized from authoritative tab-state data instead of relying only on stale URL params.
- A stale-URL regression was found and fixed so current server-provided iframe URLs replace mismatched loaded ones.

Success criteria:

- A browser user can switch tabs and continue navigating through normal Bloom flows without manually invoking JS.

## Phase 3: Introduce a Browser-Owned Workspace State Adapter

Objective: stop scattering workspace orchestration across iframe HTML, imperative C# JS injection, and isolated React surfaces.

Tasks:

- Create a small browser-side workspace state adapter as the source of truth for shell state.
- The adapter should consume backend APIs/websocket events and expose:
  - active tab
  - tab availability/enabled state
  - current book/navigation metadata needed by the shell
  - shell actions such as select tab
- Keep C# as the authoritative backend state owner.
- Avoid changing backend semantics beyond what is needed to publish state cleanly.

This phase should reduce special-case code in `workspaceRoot.ts` and make later React shell migration straightforward.

## Phase 4: Move the Outer Shell to React

Objective: replace the Pug/CSS-mode shell logic with a React-owned workspace shell while preserving existing iframe islands.

Tasks:

- Promote the workspace root from imperative TS plus Pug toggles to a React shell entrypoint.
- Render the top bar in the same React tree instead of a separate top-bar iframe when practical.
- Keep edit page/PDF iframe islands mounted behind React-managed containers.
- Use lazy imports so collection, edit chrome, and publish chrome are loaded on demand.

Design target:

- React owns shell visibility and layout.
- Existing legacy/edit island code continues to run inside retained iframes until migrated.
- Collection and publish React panes become ordinary lazy children instead of iframe-mounted islands when ready.

## Phase 5: Incremental De-Iframing

Objective: reduce iframe usage where it helps, without disturbing the content/PDF islands that still provide value.

Tasks:

- Move collection and publish shell UI out of iframe hosts when the state adapter is mature enough.
- Keep edit content and publish PDF as the explicit long-lived iframe exceptions.
- Split edit-mode bundles further around optional tools so that blank/simple books do not pay for game code and other specialized tooling upfront.

## Bundle Strategy

- Preserve separate entry boundaries for collection, edit, and publish during migration.
- Keep lazy loading at tab boundaries.
- Within edit, add or preserve sub-bundle splits around optional tools and heavy feature families.
- Do not let the new outer shell force eager imports of toolbox/game/publish internals.

## Validation Checklist

- `CURRENTPAGE` in a plain browser reflects the current active tab.
- Clicking each top-level tab updates both visible shell state and URL mode.
- Entering edit restores `pageSrc` and `pageListSrc` correctly, with authoritative server values replacing stale URL-restored values.
- Publish can be entered without loading publish code before first use.
- Returning to collection works after publish/edit flows.
- No new websocket listener leaks are introduced by the workspace shell changes.

## Next Step

Continue Phase 2 by checking remaining tab-transition flows for desktop-only assumptions, especially:

- collection actions that enter edit
- transitions back from publish to collection
- any remaining cases where the browser shell and desktop host can disagree about the active frame sources