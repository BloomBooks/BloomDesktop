# Single Browser Plan (Up To Legacy Edit Internals Milestone)

## Goal of this plan
Reach a stable milestone where Bloom uses a **single top-level browser shell**, while Edit still uses the existing legacy internal iframe architecture (`editViewFrame` + nested iframes).

This plan intentionally stops there.

---

## Phase 0 — Architecture and Safety Rails
**Purpose:** prevent migration drift and reduce regressions while we re-route behavior.

### Work
1. Define and freeze a short architecture contract document:
   - Single top-level browser shell
   - C# remains source of truth for domain/file operations
   - Edit internals may remain legacy iframes for this milestone
2. Define command routing rule:
   - In single-browser mode, Edit commands must target the visible hosted Edit surface first.
   - Hidden legacy browser is fallback only.
3. Add lightweight logging around command routing success/fallback paths.
4. Inventory all current Edit entry points that execute JS from C#.

### Exit criteria
- Team agrees on ownership boundaries and routing rule.
- We can identify every major Edit command path and its current target.

---

## Phase 1 — Stabilize Single Browser Shell + Command Routing
**Purpose:** make current single-browser mode reliable without changing core Edit internals.

### Work
1. Ensure top-level shell is active in dev path with tab state synchronized.
2. Keep Edit rendered via React host iframe (`EditTabHost`) using server-provided frame URL.
3. Route page-switch commands to visible hosted Edit frame first, with robust fallback.
4. Route dialog-trigger commands (Add Page / Change Layout / Registration / About / Book Settings) through the same host-first path.
5. Route zoom and similar toolbar-driven Edit commands through host-first path.
6. Ensure routing code verifies host availability before claiming success.
7. Ensure UI-thread-safe execution for frame URL generation and command dispatch.
8. Add diagnostics for failed host dispatch and fallback frequency.

### Exit criteria
- In single-browser mode, page switching works reliably on the visible Edit UI.
- Add Page and Change Layout dialogs open reliably from visible Edit UI.
- No command path silently succeeds while doing nothing.

---

## Phase 2 — Functional Parity Sweep (Still Legacy Internal Iframes)
**Purpose:** close remaining behavior gaps while intentionally keeping legacy Edit internals.

### Work
1. Enumerate Edit workflows and verify each in single-browser mode:
   - Page selection/navigation
   - Add page/change layout
   - Image operations
   - Undo/cut/copy/paste
   - Zoom, keyboard interactions, focus transitions
   - Relevant dialogs launched from Edit context
2. Fix any remaining commands still bound to hidden/legacy browser path by default.
3. Verify websocket-driven UI updates still reach visible Edit surface.
4. Normalize lifecycle handling (tab change, edit visibility transitions, reloads).
5. Add targeted automated checks where practical (keep scope narrow and high-value).

### Exit criteria
- Core Edit workflows are functionally equivalent between old and single-browser paths.
- Single-browser mode is practical for regular developer dogfooding.

---

## Phase 3 — Milestone: Single Browser + Legacy Edit Internals
**Purpose:** formalize the target milestone before deeper de-iframe work.

### Milestone definition
- One top-level browser hosts workspace shell and tab UI.
- Edit tab is hosted in that shell.
- Legacy Edit internals (`editViewFrame` and nested iframes) remain in place.
- Host-first command routing is standard.
- Hidden legacy-browser execution is fallback, not primary control path.

### Done criteria
- Team can run and use single-browser mode for everyday edit work.
- Major Edit workflows pass a defined smoke checklist.
- Known gaps are documented, prioritized, and explicitly deferred to post-milestone work.

---

## Out of scope for this plan
- Removing internal Edit iframes.
- Rebuilding Edit UI architecture into fully native React modules.
- Full retirement of old Edit bundle/global assumptions.

Those are post-milestone phases and should be planned separately.

---

## Current status snapshot
- Single-browser shell exists.
- Edit host wrapper exists.
- Routing reliability work is in progress (Phase 1).

### Phase 1 progress log
- Completed: Host-first routing for page switching via `EditingView.RunJavascriptAsync`.
- Completed: Host-first routing for Edit dialogs and top-level Edit commands that already use `EditingView.RunJavascriptAsync`.
- Completed: Async host dispatch verification in `WorkspaceView` (only report success when visible Edit host executes).
- Completed: Async host dispatch now retries briefly before fallback to reduce transient "host not ready" misses.
- Completed: Added lightweight host dispatch success/fallback counters with periodic fallback diagnostics.
- Completed: Additional host-first routing in `EditingModel` for:
   - `requestPageContent()` save trigger
   - page list reload after page insert
   - `changeImage(...)` editable-page command
- Completed: Fallback diagnostic logging when host dispatch falls back to legacy Edit browser.
- Completed: Removed obsolete synchronous host bridge path to keep one verified async dispatch pipeline.
- Completed: Fixed thumbnail-switch crash when toolbox exports were temporarily unavailable (`applyToolboxStateToPage` now runs through `doWhenToolboxLoaded`).
- Completed: Fixed Edit zoom plus/minus no-op (corrected `setZoom` CSS transform in `editViewFrame.ts`).
- Completed: Disabled browser-level Ctrl+wheel/Ctrl+plus zoom on the single-browser shell host (`ReactControl.DisableBrowserZoomControl`).
- Completed: Hardened `setZoom` to update all relevant page-scaling-container contexts in wrapped Edit mode.
- Completed: Re-applied browser zoom disabling after navigation (`DocumentCompleted`) to prevent shell zoom regressions.

### Phase 1 closeout notes
- High-impact routing has been moved to host-first path for the main Edit command flows.
- Remaining issue intentionally deferred during milestone push: Ctrl+scroll host zoom side effect.

### Smoke run status
- Smoke checklist completed through item #8 by manual dogfooding.
- Known issue (deferred for now): Ctrl+scroll can still affect host-level zoom behavior in some runs.
- Added diagnostics endpoint for follow-up investigation: `GET workspace/singleBrowserDiagnostics`.
- Diagnostics endpoint now also reports shell WebView zoom factor for Ctrl+scroll investigation.
- Diagnostics now include shell WebView zoom-factor change counters and last observed value.

### Next focus (Phase 2)
- Start functional parity tracking for key Edit workflows while keeping legacy internal iframes.
- Log and prioritize remaining non-blocking issues found during dogfooding.

### Phase 2 parity tracker (current)
| Workflow | Status | Notes |
| --- | --- | --- |
| Enter Edit tab and render page | pass | Working in single-browser shell mode |
| Thumbnail page navigation | pass | Prior toolbox race crash fixed |
| Add Page dialog and insert | pass | Smoke-tested |
| Change Layout dialog | pass | Smoke-tested |
| Registration/About dialogs from Edit | pass | Smoke-tested |
| Zoom plus/minus behavior | pass | Control updates page zoom in latest smoke run |
| Ctrl+scroll zoom behavior | partial | Deferred: sometimes affects host-level zoom too |
| Tab switching (Collection/Edit/Publish) | pass | Smoke-tested |
| Host dispatch telemetry visibility | pass | `GET workspace/singleBrowserDiagnostics` available |

### Phase 2 next implementation targets
1. Investigate and isolate Ctrl+scroll host zoom leakage path.
2. Add a repeatable quick parity regression checklist run template (date/build/result).
3. Continue workflow-by-workflow verification for image operations and undo/cut/copy/paste.

### Phase 2 quick parity run template
Use this to record each dogfooding pass concisely:

- Date:
- Build/commit:
- Environment:
- Workflows checked:
   - Enter Edit + render:
   - Thumbnail navigation:
   - Add Page / Change Layout:
   - Registration/About dialogs:
   - Zoom buttons:
   - Ctrl+scroll behavior:
   - Tab switching:
   - Image operations:
   - Undo/Cut/Copy/Paste:
- New issues found:
- Regressions from previous run:
- Next actions:

---

## Smoke checklist for Milestone 3 readiness
Run this checklist in single-browser mode, with Edit still using legacy internal iframes:

1. Open collection, switch to Edit, confirm page renders.
2. Click several page thumbnails and confirm visible page switches correctly.
3. Use **Add Page** and confirm chooser dialog appears and page can be inserted.
4. Use **Change Layout** and confirm chooser dialog appears.
5. Open registration/about dialogs from Edit context and confirm they appear.
6. Change zoom from Edit UI and confirm page zoom updates.
7. Switch tabs (Collection/Edit/Publish) repeatedly and confirm Edit resumes correctly.
8. Watch logs for repeated fallback diagnostics; investigate if frequent during normal flow.

Passing this checklist repeatedly is the practical bar for declaring Milestone 3 complete.
