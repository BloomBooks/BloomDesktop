# Toasts

This folder contains the browser-side toast UI.

## Overview

- `ToastHost.tsx` subscribes to websocket `toast/show` events, de-duplicates repeated toasts, renders stacked toasts, and dispatches toast actions.
- `Toast.tsx` renders a single toast and defines the browser-side toast payload types.
- `toastUtils.ts` provides the window-event bridge that lets development and test code inject toast payloads into the production toast host.

## Cross-layer Contract

Keep the browser and C# toast contracts in sync:

- Browser types live in `Toast.tsx`.
- Backend sender and callback registry live in `src/BloomExe/web/ToastService.cs`.
- Backend action/test endpoints live in `src/BloomExe/web/controllers/WorkspaceApi.cs`.

Important invariants:

- Severity values must stay aligned between `Toast.tsx` and `ToastService.cs`.
- `actionInfo` shape must stay aligned between `Toast.tsx` and `ToastService.cs`.
- `ToastHost.tsx` de-duplicates only while a matching toast is already on screen, using `l10nId` when present and otherwise falling back to `text`.
- When a duplicate toast arrives with the same identity, the host keeps the existing toast unless the new one would stay visible longer. In that case it replaces the existing entry with a merged copy so the longer lifetime wins.
- In practical terms, two `show` events are considered duplicates only if they arrive while the first toast is still on screen and they resolve to the same identity. If the earlier toast has already been dismissed or timed out, the same message can be shown again later as a new toast.
- Example: two quick `show` events with `text: "Duplicate check"` count as the same on-screen toast. A later `show` event with the same text after the first one disappears is a fresh toast, not a permanent suppression rule.
- Backend callbacks are one-shot: `toast/performAction` removes the callback registration before invoking it.
- If repeated calls represent the same logical toast and carry an action, the backend caller should pass a `toastId` so repeated show events reuse one callback slot instead of leaking registrations the UI will never expose.
- A backend callback registration is just the C# side remembering which `Action` to run if the user clicks that toast. Without `toastId`, repeated backend calls for one logical persistent toast would create multiple stored callbacks even though the browser only exposes one visible toast to click.

## Testing

### Manual testing from devtools

The workspace root installs three global helpers:

- `window.showToastScenario(scenario)` asks the backend to trigger one of the named production toast scenarios, which then comes back through the normal websocket path.
- `window.showToast(toastOrToasts)` skips the backend and injects toast payloads straight into the host so layout and interaction can be checked without a matching C# trigger.
- `window.clearToasts()` clears the current toast stack.

If devtools is attached to some other child frame, use `window.top...`.

Useful backend-backed scenarios:

- `window.showToastScenario("nonfatal/report")`
- `window.showToastScenario("nonfatal/details")`
- `window.showToastScenario("errorReporter/unobtrusive")`
- `window.showToastScenario("workspace/teamCollectionClobber")`
- `window.showToastScenario("update/looking")`
- `window.showToastScenario("update/upToDate")`
- `window.showToastScenario("update/foundUpdates")`
- `window.showToastScenario("update/downloading")`
- `window.showToastScenario("update/downloadedWaitingForRestart")`
- `window.showToastScenario("update/error")`
- `window.showToastScenario("update/failure")`

Useful frontend-only checks:

```js
window.clearToasts();
window.showToast([
  { text: "Stack 1", severity: "notice", durationSeconds: 30 },
  { text: "Stack 2", severity: "warning", durationSeconds: 30 },
  { text: "Stack 3", severity: "error", durationSeconds: 30 },
]);
```

```js
window.clearToasts();
window.showToast([
  { text: "Duplicate check", severity: "notice", durationSeconds: 30 },
  { text: "Duplicate check", severity: "notice", durationSeconds: 30 },
]);
```

Things worth checking manually when changing toast behavior:

- stacking order and spacing
- de-duplication
- auto-dismiss vs persistent toasts
- close button behavior
- click-on-body action behavior
- action-button behavior
- callback-based actions
- localization via `l10nId`

### Automated testing

Backend callback registration behavior is covered in `src/BloomTests/web/ToastServiceTests.cs`.

The regression this test protects against is leaking multiple backend callback registrations for one logical toast. The browser only shows one on-screen toast when repeated `ShowToast(..., toastId: ...)` calls describe the same logical message, so the backend must also reuse one callback slot and keep the latest callback.

## Current Production Sources

Current backend-owned production toast sources include:

- `src/BloomExe/NonFatalProblem.cs`
- `src/BloomExe/ErrorReporter/BloomErrorReport.cs`
- `src/BloomExe/ApplicationUpdateSupport.cs`
- `src/BloomExe/Workspace/WorkspaceView.cs`