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
