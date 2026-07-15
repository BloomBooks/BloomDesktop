# Spike findings: OS keyboard switching under WebView2 (plan item 0)

**Date:** 2026-07-09
**Author:** Claude Opus 4.8 (spike teammate)
**Instance under test:** Bloom launched from source via `./go.sh` (collection "FAL Thai Test"),
WebView2 content hosted in a separate `msedgewebview2.exe` process.

## Verdict

**GO-VIA-FALLBACK.**

- The plan's assumed mechanism — libpalaso `KeyboardController` → `IKeyboardDefinition.Activate()`,
  which ends in `ITfInputProcessorProfileMgr.ActivateProfile(..., TF_IPPMF_FORPROCESS)` — **does NOT
  change what is typed into a bloom-editable hosted in WebView2.** This is the plan's ranked-#1 risk,
  and it is realized. **NO-GO for that mechanism as written.**
- **Active OS switching is still achievable in v1**, but item 2/4 must switch the input language of
  **Bloom's own foreground top-level window** (which WebView2 follows), not call libpalaso's
  process-scoped `Activate()`. Posting `WM_INPUTLANGCHANGEREQUEST` to Bloom's main-form HWND
  demonstrably switched the WebView2 process's input thread to the target layout, scoped to Bloom
  (a co-running Chrome/Meet was unaffected).
- One honest caveat (see "What I could not do"): the gold-standard test — real OS-level keystrokes
  into a focused field producing Thai glyphs — could not be run because the machine's user was in a
  live video meeting and reliable key injection requires stealing foreground. The conclusion rests on
  two strong, converging instruments instead (per-thread `GetKeyboardLayout` and the webview's own
  `navigator.keyboard.getLayoutMap()`). A 30-second manual confirmation is recommended before building.

## The core architectural fact

Bloom is `pid A` (`Bloom.exe`). Its WebView2 keyboard input is processed in a **separate process**
`pid B` (`msedgewebview2.exe`) — the window that receives keystrokes is a
`Chrome_RenderWidgetHostHWND` / `Chrome_WidgetWin_1` owned by `pid B`, reparented under Bloom's window.
`TF_IPPMF_FORPROCESS` activates a TSF profile **for the calling process only** (`pid A`). It therefore
cannot, by definition, affect `pid B`'s input threads. This was then confirmed empirically.

## Evidence

Two instruments, both non-intrusive:
1. **`GetKeyboardLayout(threadId)`** for the UI thread of `Bloom.exe`, the render-widget thread of
   `msedgewebview2.exe`, and (as a control) a co-running Chrome/Meet window. `0x04090409` = US English,
   `0x041E041E` = Thai Kedmanee.
2. **`navigator.keyboard.getLayoutMap()`** read over CDP inside the bloom-editable's document — reports
   the character each physical key would produce under the webview's *current* layout. Supported in
   this WebView2 build.

| Action | Bloom UI thread HKL | **WebView thread HKL** | getLayoutMap (KeyA / Semicolon) |
|---|---|---|---|
| Baseline (default) | `0x04090409` US | **`0x04090409` US** | `a` / `;` |
| libpalaso `Activate()` Thai (**FORPROCESS**) | `0x041E041E` Thai | **`0x04090409` US — UNCHANGED** | `a` / `;` — UNCHANGED |
| `WM_INPUTLANGCHANGEREQUEST` → child webview HWND | (n/a) | **`0x04090409` US — UNCHANGED** | unchanged |
| our TSF `ActivateProfile` **FORSESSION** (Bloom in background) | Thai | **US — UNCHANGED** | unchanged |
| our TSF `ActivateProfile` **FORSESSION** (Bloom **foreground**) | Thai | **US — UNCHANGED** | unchanged |
| **`WM_INPUTLANGCHANGEREQUEST` → Bloom's foreground TOP window** | Thai | **`0x041E041E` Thai — CHANGED** | changed (see caveat) |
| restore: `WM_INPUTLANGCHANGEREQUEST`(US) → foreground TOP | Thai | **`0x04090409` US — restored** | `a` / `;` restored |

Control throughout: the co-running Chrome/Meet process stayed `0x04090409` US — the working mechanism
is scoped to Bloom + its webview, it does **not** hijack the whole session's other apps.

### Fallbacks, evaluated in the plan's order
- **(a) `WM_INPUTLANGCHANGEREQUEST` to the focused WebView2 HWND.** Posting to the *child* webview
  windows (`Chrome_RenderWidgetHostHWND`, `Chrome_WidgetWin_1`): **no effect.** Posting to Bloom's
  **foreground top-level window** (the same message a language-bar / Win+Space switch generates):
  **WORKS** — the webview process's input thread switched to the posted HKL. This is the recommended
  fallback.
- **(b) our own `ActivateProfile` with `TF_IPPMF_FORSESSION`.** Implemented via minimal TSF COM interop
  (`ITfInputProcessorProfileMgr.ActivateProfile`, profile type = keyboard layout). The call returned
  success and changed `Bloom.exe`'s own thread, but produced **no observed change in the webview
  thread's HKL or getLayoutMap**, even with Bloom foreground. Either the session-wide activation does
  not propagate to an already-running child process's input thread the way `WM_INPUTLANGCHANGEREQUEST`
  does, or my interop is incomplete (TSF activation for HKL-backed layouts is finicky). Not pursued
  further because fallback (a) already works and is far simpler.

### The `getLayoutMap` caveat (why not a slam-dunk)
`getLayoutMap()` is live (it tracked the working switch: `;` → `ö` → `;`), but it is **focus-dependent
and lags** — it refreshes when the webview processes a layout-change while focused, not on a bare
`GetKeyboardLayout` change. In the one run that flipped the webview thread to Thai, `getLayoutMap` still
reported a **Swedish-ish** mapping (`KeyA`→`a`, `Semicolon`→`ö`), *not* Thai (`KeyA`→`ฟ`). So the two
instruments briefly disagreed. Interpretation: the **thread HKL is authoritative** for what Windows
feeds Chromium at keystroke time (→ Thai), and `getLayoutMap` was a stale cache because the webview
wasn't OS-focused. But because I could not fire a real keystroke to settle it 100%, I am flagging it.

## `KeyboardController.Initialize()` cost
Measured by timing the one-time `KeyboardController.Initialize()` inside the spike endpoint:
**~160–500 ms on the first call** (495 ms and 457 ms on two cold instances; 160–177 ms on a warmer one),
**one-time** — subsequent `AvailableKeyboards` enumeration is instant. It enumerated **22 installed
input methods**, including the target `th-TH_Thai Kedmanee_Thai Kedmanee`. Confirms the plan's item-2
approach of doing `Initialize()` once as a low-priority startup action on the UI thread is fine; budget
up to ~½ second on first use.

## Effect of `Initialize()` on the existing `IsFormUsingInputProcessor` check
**None.** The existing `keyboarding/useLongpress` endpoint returned `"true"` **both before and after**
`KeyboardController.Initialize()` on the same running instance. `Initialize()` does not disturb the
`IsFormUsingInputProcessor(form)` code path that item 8 / BL-1071 relies on.

## What item 2/4 should do differently from the plan
1. **Do NOT use `IKeyboardDefinition.Activate()` / libpalaso's `KeyboardController` to *activate* the
   keyboard for the edit view.** Its `TF_IPPMF_FORPROCESS` activation stays in `Bloom.exe` and never
   reaches the `msedgewebview2.exe` process, so typing is unaffected. (libpalaso is still fine for
   *enumeration* — `AvailableKeyboards`, `TryGetKeyboard`, resolving the HKL — just not for the
   activation that must affect typing.)
2. **Switch the input language on Bloom's foreground main window instead**, and let WebView2 follow.
   The proven channel is `PostMessage(mainFormHwnd, WM_INPUTLANGCHANGEREQUEST, INPUTLANGCHANGE_SYSCHARSET,
   hkl)`. Equivalent candidates worth trying in the real implementation: `ActivateKeyboardLayout(hkl, …)`
   executed **on Bloom's UI thread while it is foreground**. The activation must happen when Bloom is
   foreground and the field focused — which is exactly the real-world condition (a user is typing), so
   this is not a limitation in production, only in headless testing.
3. **Resolve the target to an HKL.** libpalaso's `AvailableKeyboards[].Id` is a composite string
   (e.g. `th-TH_Thai Kedmanee_Thai Kedmanee`), not an HKL. Item 4 will need the actual `HKL`
   (`LoadKeyboardLayout`/the layout list) to post in `WM_INPUTLANGCHANGEREQUEST`. Plan the resolver to
   carry the HKL, not just libpalaso's Id.
4. **Verify with real typing, not `getLayoutMap`.** `getLayoutMap` lags when unfocused and briefly
   mis-reported the active layout here. The manual verification (focus a Thai field, type home-row
   `asdfjkl;`, expect Thai glyphs; focus English, expect Latin) remains the source of truth.
5. **The passive/ambient premise still holds** and is unaffected by this finding: a user *manually*
   switching to an installed Keyman/OS keyboard (Win+Space, session-wide) works under WebView2 as the
   plan assumes. This spike only concerns Bloom *programmatically driving* the switch, which is where
   FORPROCESS fails and the foreground-window approach succeeds.

## Machine-state changes and how to undo them
1. **Added Thai Kedmanee (`041E:0000041E`) to the Windows `th` language** (it previously had an empty
   input-method list). **Left installed** per the task brief so feature work can proceed. To undo:
   ```powershell
   $list = Get-WinUserLanguageList
   foreach ($e in $list) { if ($e.LanguageTag -eq 'th') { $e.InputMethodTips.Clear() } }
   Set-WinUserLanguageList $list -Force
   ```
   (Use the `foreach` *statement*, not `Get-WinUserLanguageList | ForEach-Object` — the cmdlet returns a
   generic `List<T>` the pipeline does not unroll; enumerate by `foreach`/index.)
2. **Foreground-lock timeout:** I attempted to set `SPI_SETFOREGROUNDLOCKTIMEOUT` to 0 to make
   `SetForegroundWindow` reliable; the set **did not take** (it stayed pinned at `0x7FFFFFFF`). **No net
   change** — nothing to undo.
3. **Bloom + its WebView2 layout:** left the WebView2 process back on **US** (restored and verified).
   The throwaway Bloom instance's WinForms *shell* thread was left showing Thai — inconsequential (users
   type in the webview, not the shell) and gone when the instance is killed. **No other app was
   affected** (Chrome/Meet stayed US throughout).
4. **No software was installed.** No Keyman-for-Windows install. No npm, no `yarn build`, no commits.

## Spike code (uncommitted, to be discarded)
Temporary endpoints in `src/BloomExe/web/controllers/KeyboardingConfigApi.cs`, all marked
`// SPIKE - temporary`:
- `keyboarding/spikeList` — one-time `KeyboardController.Initialize()` (timed) + `AvailableKeyboards`
  JSON + `IsFormUsingInputProcessor` readout.
- `keyboarding/spikeActivate?id=<id|default>` — `TryGetKeyboard(id).Activate()` (FORPROCESS) /
  `ActivateDefaultKeyboard()`.
- `keyboarding/spikeForSession?hkl=&lang=` — our TSF `ActivateProfile(..., TF_IPPMF_FORSESSION)` interop.
- `keyboarding/fieldFocused` — a `useKmw:false` **stub** added only so the orchestrator's item-5 browser
  code (which POSTs `fieldFocused` on every focus) stops raising "Cannot Find API Endpoint" dialogs that
  wrecked the test. Not part of the answer; discard with the rest.

Test tooling used (in the session scratchpad, outside the repo): a CDP driver
(`editDriver.mjs` — list/focus/clear/read/`layoutmap`) and PowerShell HKL/thread-layout probes.
