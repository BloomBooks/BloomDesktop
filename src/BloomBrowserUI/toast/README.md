# Toasts

This folder contains the browser-side toast UI.

## Overview

- `ToastHost.tsx` subscribes to websocket `toast/show` events, de-duplicates repeated toasts, renders stacked toasts, and dispatches toast actions.
- `Toast.tsx` renders a single toast and defines the browser-side toast payload types.
- `toastUtils.ts` provides window-event helpers used by the devtools testing hooks.

## Cross-layer Contract

Keep the browser and C# toast contracts in sync:

- Browser types live in `Toast.tsx`.
- Backend sender and callback registry live in `src/BloomExe/web/ToastService.cs`.
- Backend action/test endpoints live in `src/BloomExe/web/controllers/WorkspaceApi.cs`.

Important invariants:

- Severity values must stay aligned between `Toast.tsx` and `ToastService.cs`.
- Action shape must stay aligned between `Toast.tsx` and `ToastService.cs`.
- `ToastHost.tsx` currently de-duplicates by `l10nId`, falling back to `text`.
- Backend callbacks are one-shot: `toast/performAction` removes the callback registration before invoking it.
- If repeated calls represent the same logical toast and carry an action, the backend caller should pass a `toastId` so repeated show events reuse one callback slot instead of leaking registrations the UI will never expose.

## Testing

### Manual testing from devtools

The workspace root installs three debug helpers:

- `window.bloomToastTest(scenario)` triggers backend-backed toast scenarios.
- `window.bloomToastShow(toastOrToasts)` injects frontend-only test toasts.
- `window.bloomToastClear()` clears the current toast stack.

If devtools is attached to some other child frame, use `window.top...`.

Useful backend-backed scenarios:

- `window.bloomToastTest("nonfatal/report")`
- `window.bloomToastTest("nonfatal/details")`
- `window.bloomToastTest("errorReporter/unobtrusive")`
- `window.bloomToastTest("workspace/teamCollectionClobber")`
- `window.bloomToastTest("update/looking")`
- `window.bloomToastTest("update/upToDate")`
- `window.bloomToastTest("update/foundUpdates")`
- `window.bloomToastTest("update/downloading")`
- `window.bloomToastTest("update/downloadedWaitingForRestart")`
- `window.bloomToastTest("update/error")`
- `window.bloomToastTest("update/failure")`

Useful frontend-only checks:

```js
window.bloomToastClear();
window.bloomToastShow([
  { text: "Stack 1", severity: "notice", durationSeconds: 30 },
  { text: "Stack 2", severity: "warning", durationSeconds: 30 },
  { text: "Stack 3", severity: "error", durationSeconds: 30 },
]);
```

```js
window.bloomToastClear();
window.bloomToastShow([
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
- callback-based actions vs URL actions
- localization via `l10nId`

### Automated testing

Backend callback registration behavior is covered in `src/BloomTests/web/ToastServiceTests.cs`.

The most important regression here is repeated actionable toasts that are visually de-duplicated by the browser. The existing test verifies that repeated `ShowToast(..., toastId: ...)` calls reuse a single registered callback and keep the latest callback.

## Current Production Sources

Current backend-owned production toast sources include:

- `src/BloomExe/NonFatalProblem.cs`
- `src/BloomExe/ErrorReporter/BloomErrorReport.cs`
- `src/BloomExe/ApplicationUpdateSupport.cs`
- `src/BloomExe/Workspace/WorkspaceView.cs`