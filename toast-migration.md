# Toast Migration (WinForms -> Browser/MUI Snackbar)

## Goal

Replace WinForms `ToastNotifier` usage with browser-rendered toasts (MUI Snackbar), delivered from backend to frontend over websocket.

## Current Toast Inventory

The following are active WinForms toast sources (`new ToastNotifier()`):

1. `src/BloomExe/NonFatalProblem.cs`
   - General non-fatal warning toast.
   - Optional click actions:
     - Open problem report dialog (`Report`)
     - Open details dialog (`Details`)
   - Auto-dismiss: `15s`
   - Severity: warning icon.

2. `src/BloomExe/ApplicationUpdateSupport.cs`
   - Update status toasts:
     - Checking in progress
     - Up to date
     - Updates available (`Update Now` click action)
     - Downloading
     - Downloaded waiting for restart (`Restart Bloom to Update` click action, persistent)
     - Update error (click opens error dialog)
     - Failure notification
   - Auto-dismiss varies (`5s`, `10s`, persistent)
   - Severity mostly informational; error path exists.

3. `src/BloomExe/Workspace/WorkspaceView.cs`
   - Team collection clobber warning while outside collection tab.
   - Click action: switch to collection tab.
   - Persistent (`-1` seconds in old API).
   - Severity: error icon.

4. `src/BloomExe/ErrorReporter/BloomErrorReport.cs`
   - `NotifyUserUnobtrusively(shortMsg, longerMsg)`
   - Click action: open detailed notify dialog.
   - Auto-dismiss: `10s`
   - Severity: warning/notice.

Related browser-side usage found:
- `src/BloomBrowserUI/bookEdit/pageThumbnailList/pageThumbnailList.tsx` uses `toastr` for local "Saving..." notification.
  - This is already browser-native and not WinForms-based.
  - Not in scope for this migration unless we separately decide to replace `toastr` with MUI.

## Requirements Coverage Check

Proposed requirements:

- Nature of alert (`error`, `warning`, `notice`) ✅ needed
- Localized text or `l10nId` ✅ needed
- Auto-dismiss boolean ✅ needed (plus duration for parity)
- Actions: restart, navigate browser to url, open error dialog ✅ needed
- Hyperlinks/markdown maybe ⚠️ partially needed

Additional requirements discovered from existing scenarios:

1. **Persistent toasts**
   - Needed (`WorkspaceView`, update "Restart to Update")

2. **Configurable duration**
   - Existing flows use 5/10/15 second durations.

3. **Arbitrary app actions beyond initial list**
   - Needed for:
     - Start update download
     - Switch to collection tab
   - Therefore actions must support either:
     - a richer action enum, or
     - generic backend callback action IDs.

4. **Single action button text (localized)**
   - Existing toasts show one optional CTA label.

5. **De-duplication of repeated messages**
   - Existing WinForms implementation suppresses immediate duplicate messages.

6. **Stacking behavior**
   - Explicit new requirement: multiple toasts stack.

7. **Clickable body fallback**
   - Existing WinForms behavior triggers action on click of toast area.
   - Browser version can implement via action button and optional click-on-body for parity.

8. **Safe handling when websocket is temporarily unavailable**
   - Existing infrastructure already logs unavailable sockets.
   - Migration should avoid introducing silent crashes.

## Toast Event Contract (Target)

Backend sends websocket event:
- `clientContext`: `toast`
- `id`: `show`
- payload:
  - `toastId: string`
  - `severity: "error" | "warning" | "notice"`
  - `text?: string`
  - `l10nId?: string`
  - `l10nDefaultText?: string`
  - `autoDismiss: boolean`
  - `durationMs?: number`
  - `dedupeKey?: string`
  - `action?:`
    - `label?: string`
    - `l10nId?: string`
      - `url?: string` (browser navigation target)
      - `callbackId?: string` (backend callback)

Browser sends API callback for toast action:
- `POST /bloom/api/toast/performAction`
   - `callbackId`

## Migration Plan

### Milestone 1: Requirements + design
- Produce this document.
- Confirm all current toast scenarios are represented.

### Milestone 2: Backend toast pipeline
- Add a toast service that:
  - sends websocket `toast/show` messages
  - registers callback actions (`callbackId -> Action`)
- Add API endpoint for toast action dispatch.

### Milestone 3: Frontend stacked snackbar
- Add React toast host component using MUI Snackbar + Alert.
- Subscribe to websocket `toast/show`.
- Render multiple stacked toasts.
- Support action button and auto-dismiss/persistent behavior.
- Support raw text first; add optional l10nId resolution path.
- Mount the toast host in `WorkspaceRoot` so it is attached to the root document, not a content iframe.

### Milestone 4: Migrate all WinForms toast callsites
- `NonFatalProblem.ShowToast()` -> toast service
- `ApplicationUpdateSupport` toast methods -> toast service
- `WorkspaceView` clobber toast -> toast service
- `BloomErrorReport.NotifyUserUnobtrusively()` -> toast service

### Milestone 5: Verify and cleanup
- Validate compile/test paths relevant to modified files.
- Confirm no remaining production callsites instantiate `ToastNotifier`.

### Milestone 6: Remove obsolete WinForms toast code
- Remove dead helper code left behind in update flow.
- Delete `ToastNotifier` implementation files once no callsites remain:
   - `src/BloomExe/MiscUI/ToastNotifier.cs`
   - `src/BloomExe/MiscUI/ToastNotifier.designer.cs`
   - `src/BloomExe/MiscUI/ToastNotifier.resx`
- Keep toast path as single implementation source.

## Notes on Hyperlinks / Markdown

Current migration path:
- Prefer action button and optional navigate-url action.
- Keep message body as plain text initially for safety and simplicity.
- If hyperlink-in-text is required later, use markdown with a constrained renderer and existing external-link interception.

## Out of Scope for This Change

- Replacing browser `toastr` usage in page thumbnail list.
- New visual theme beyond existing MUI tokens.

## Manual Trigger Matrix

Open devtools on the main Bloom workspace window and run these commands from the root document.

- `window.bloomToastTest(scenario)` triggers backend-backed scenarios.
- `window.bloomToastShow(toastOrToasts)` remains useful for frontend-only layout checks.
- `window.bloomToastClear()` resets the current stack.

The scenarios below are organized by the production C# source that launches the toast. Where the normal user workflow is awkward or environment-dependent, the listed `bloomToastTest()` scenario routes through the owning C# code instead of re-creating the payload in JavaScript.

### `src/BloomExe/NonFatalProblem.cs`

1. Report-link nonfatal toast
   - Code path: `NonFatalProblem.Report(... showSendReport: true)` -> `ShowToast()`.
   - Manual trigger: `window.bloomToastTest("nonfatal/report")`
   - Verify: warning toast appears with `Report`; clicking it opens the problem report dialog.

2. Details-link nonfatal toast
   - Code path: `NonFatalProblem.Report(... showSendReport: false, showRequestDetails: true)` -> `ShowToast()`.
   - Manual trigger: `window.bloomToastTest("nonfatal/details")`
   - Verify: warning toast appears with `Details`; clicking it opens the details dialog.

### `src/BloomExe/ErrorReporter/BloomErrorReport.cs`

3. Unobtrusive warning toast
   - Code path: `BloomErrorReport.NotifyUserUnobtrusively()`.
   - Manual trigger: `window.bloomToastTest("errorReporter/unobtrusive")`
   - Verify: warning toast appears; clicking it opens the longer error dialog.

### `src/BloomExe/Workspace/WorkspaceView.cs`

4. Team collection clobber toast
   - Code path: `WorkspaceView` -> `ShowTeamCollectionClobberToast()`.
   - Manual trigger: `window.bloomToastTest("workspace/teamCollectionClobber")`
   - Real-world trigger: cause the current Team Collection book to be clobbered while you are outside the Collection tab.
   - Verify: error toast appears; clicking it switches to the Collection tab.

### `src/BloomExe/ApplicationUpdateSupport.cs`

5. Update check already in progress
   - Code path: `CheckForAVelopackUpdate()` when `_status == LookingForUpdates`.
   - Manual trigger: `window.bloomToastTest("update/looking")`
   - Real-world trigger: repeatedly invoke Check for Updates while an update check is still running.

6. Up-to-date toast
   - Code path: `ShowToastForUpToDate()`.
   - Manual trigger: `window.bloomToastTest("update/upToDate")`
   - Real-world trigger: manually Check for Updates when no newer version is available.

7. Updates available (`Update Now`)
   - Code path: `ShowToastForFoundUpdates()`.
   - Manual trigger: `window.bloomToastTest("update/foundUpdates")`
   - Real-world trigger: disable auto-update and check for updates when a newer version exists.

8. Downloading update
   - Code path: `ShowToastForDownloading()`.
   - Manual trigger: `window.bloomToastTest("update/downloading")`
   - Real-world trigger: start an update download, either through auto-update or by clicking `Update Now`.

9. Downloaded and waiting for restart
   - Code path: `ShowToastForDownloadedWaitingForRestart()`.
   - Manual trigger: `window.bloomToastTest("update/downloadedWaitingForRestart")`
   - Real-world trigger: let an update download finish successfully.

10. Update error toast
   - Code path: `ShowToastForError()`.
   - Manual trigger: `window.bloomToastTest("update/error")`
   - Real-world trigger: provoke an exception during update check or update download.

11. Update connectivity/failure toast
   - Code path: `ShowFailureNotification()`.
   - Manual trigger: `window.bloomToastTest("update/failure")`
   - Real-world trigger: manually Check for Updates while offline or when update server lookup fails.

### Frontend-only helpers

These are still useful for layout checks, but they do not exercise a production C# toast source:

1. Root-host and stacking layout
   - `window.bloomToastClear();`
   - `window.bloomToastShow([{ text: "Stack 1", severity: "notice", durationSeconds: 30 }, { text: "Stack 2", severity: "warning", durationSeconds: 30 }, { text: "Stack 3", severity: "error", durationSeconds: 30 }]);`

2. Dedupe and clear behavior
   - `window.bloomToastClear();`
   - `window.bloomToastShow([{ text: "Duplicate check", severity: "notice", durationSeconds: 30 }, { text: "Duplicate check", severity: "notice", durationSeconds: 30 }]);`
   - `window.bloomToastClear();`
