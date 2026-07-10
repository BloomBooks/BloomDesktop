# Plan: Convert the Talking Book tool to React

**Status:** proposed / not yet started.
**Scope of this document:** Phase A (the React conversion, one PR) and Phase B (moving the
engine into the page iframe, a separate follow-up PR). Step 6 (retiring the old non-React
toolbox framework) is a third PR, described briefly at the end so it isn't lost.

Talking Book is the **last** non-React toolbox tool. Converting it lets us later delete
the entire legacy non-React tool framework (Step 6).

## 0. Sequencing rationale — why the engine move is a separate PR

An earlier draft of this plan did the React conversion and the engine move in one PR.
They are now split, for two reasons:

1. **The engine move is not needed for the goal.** Deleting the legacy framework only
   requires the tool *UI* to be React. The engine can keep living in the toolbox iframe,
   reaching into the page exactly as it does today.
2. **The page iframe reloads on every page switch.** `switchContentPage` in
   `workspaceRoot.ts` (~line 134) sets `iframe.src = newSource`, destroying the page
   iframe's whole JS realm. Today `theOneAudioRecorder` lives in the toolbox iframe and
   **survives for the whole editing session**; moved page-side, it would be torn down and
   reconstructed on *every page switch*. That is a real architectural change (see §6), not
   the mechanical "flip the wiring" the earlier draft implied — it needs its own design
   and its own PR.

The split is also what makes Phase B safe: after Phase A the engine no longer touches any
toolbox DOM (it talks to the UI only through a state object and a couple of registered
element refs), so Phase B becomes a pure relocation-and-lifetime problem.

---

## 1. Current architecture (verified against source, 2026-07)

### 1.1 `talkingBook.ts`
`TalkingBookTool implements ITool` directly (not via `ToolboxToolReactAdaptor`). Its
`makeRootElement()` **throws** — the tool's UI is the pug file
`talkingBookToolboxTool.pug`, routed in through two legacy hard-coded maps:

- `subpath` in `toolbox.ts` (~line 1273): `talkingBookTool: "talkingBook/talkingBookToolboxTool.html"`
- `legacyToolSubPathByToolId` in `ToolboxRoot.tsx` (~line 71): `talkingBook: "talkingBook/talkingBookToolboxTool.html"`

It is a thin lifecycle shim over the `AudioRecording` singleton, but note the exact
forwards (the conversion must preserve all of them):

- `showTool()` → module-level `initializeTalkingBookToolAsync()` (lazily constructs the
  singleton) then `getAudioRecorder().setupForRecordingAsync()`.
- `newPageReady()` → `showImageDescriptionsIfAny()` (its own page-DOM logic, BL-8515)
  then `getAudioRecorder().handleNewPageReady(TalkingBookTool.deshroudPhraseDelimiters)`.
- `hideTool()` → `handleToolHiding()`.
- `detachFromPage()` → `removeRecordingSetup()`, then `hideImageDescriptions(page)` and
  `TalkingBookTool.enshroudPhraseDelimiters(page)`.
- `updateMarkupAsync()` → `getAudioRecorder().getUpdateMarkupAction()`;
  `isUpdateMarkupAsync()` returns `true`.
- `beginRestoreSettings()` → `beginLoadSynphonySettings()` (shares sentence-ending
  punctuation settings with the Leveled Reader tool — easy to lose in conversion, since
  the adaptor's default `beginRestoreSettings` is a no-op).
- `isAlwaysEnabled()` returns `true` (adaptor default is `false` — must override).
- The static `enshroudPhraseDelimiters`/`deshroudPhraseDelimiters` helpers operate on the
  *page* DOM and stay with the tool.

The lifecycle comment at the top of the file (lines ~38–48) enumerates when
showTool/newPageReady/updateMarkup fire — it is the manual-test matrix (§8).

### 1.2 `audioRecording.ts` (~4,900 lines) — engine **and** tool UI, running in the toolbox iframe
The important, counter-intuitive fact: **the `AudioRecording` object runs in the
_toolbox_ iframe, not the page iframe.** Evidence:

- The constructor reads toolbox controls via its own `document`: `#audio-split`
  (→ `this.audioSplitButton`) and `#audio-meter` (→ `this.levelCanvas`), and calls
  `updateDisplay()` which touches `#audio-split-wrapper` and
  `#advanced-talking-book-controls-react-container`. (It does *not* touch `#player`;
  that is wired in `initializeTalkingBookToolAsync`, and `#disablingOverlay` is grabbed
  in `setupForRecordingAsync`.)
- `initializeTalkingBookToolAsync` (the class method, ~line 229) wires jQuery handlers to
  the toolbox buttons: `#audio-record` mousedown/mouseup, click handlers on
  `#audio-play` (with a **ctrl-click eSpeak-preview easter egg** — preserve it),
  `#audio-split` (opens the Adjust Timings dialog via `getWorkspaceBundleExports()`),
  `#audio-next`, `#audio-prev`, `#audio-clear`, `#audio-listen`, `#audio-input-dev`;
  plus `#player` events (`onended`, `onerror`, `ondurationchange`), `toastr.options`
  (position `toast-toolbox-bottom`), a `WholeTextBoxAudio` feature-status fetch, and
  `pullDefaultRecordingModeAsync()`.
- It reaches _into_ the page iframe for content via `getPageFrame()`
  (`parent.window.document.getElementById("page")`, ~line 2366) and `getPageDocBody()`
  — ~26 direct call sites plus ~9 via `getPageDocBodyJQuery()`.
- It also installs listeners **in the page document**: a capture-phase `mousedown` on the
  page body (`moveRecordingHighlightToClick`) and a `MutationObserver` watching
  visibility-affecting class changes (`watchElementsThatMightChangeAffectingVisibility`).
  These already run cross-iframe today and are re-installed in `handleNewPageReady`.

So the object owns two very different kinds of responsibility:

1. **Tool UI** (toolbox side): the 7 main buttons, their counters, the level meter
   canvas, device selection (`#audio-input-dev` icon + `#audio-devlist` jQuery menu),
   the `<audio id="player">` element, the disabling overlay, the button-state machine,
   busy cursors (`showBusy`/`endBusy` — which also touch the *root* document), and
   toastr error toasts.
2. **Content engine** (page-DOM side, conceptually): sentence markup / `audio-sentence`
   spans, highlighting, playback + timings, recording (via C# API), splitting, image
   descriptions, and playback order.

It already renders one React fragment: `updateDisplay()` renders
`TalkingBookAdvancedSection` (`talkingBookAdvancedSection.tsx`) into
`#advanced-talking-book-controls-react-container` via `renderRoot`, passing a props
object the engine computes (recordingMode, hasAudio, hasRecordableDivs,
haveACurrentTextboxModeRecording, inShowPlaybackOrderMode, showingImageDescriptions,
plus command callbacks). The "checkboxes" for playback order and image descriptions are
already React `BloomSwitch`es in that section; the pug's `checkboxWithHiddenClickHandler`
mixin is **dead code**.

### 1.3 The button-state machine (the part that changes most)
- `Status` enum: `Disabled | DisabledUnlessHover | Enabled | Expected | Active`, applied
  today as **camelCase CSS classes** on the 7 buttons (`audio-record`, `audio-play`,
  `audio-split`, `audio-next`, `audio-prev`, `audio-clear`, `audio-listen`) by
  `setStatus`/`getStatus`.
- `setStatus` also: toggles `expected` on the matching `#audio-<verb>-label`; swaps the
  play label to localized "Pause" (`Common.Pause`) while `Active` and restores
  `originalPlayLabel` after; and has a branch for `#audio-<verb>-list-item` ids that
  **do not exist in the pug — dead code, do not port**.
- **The DOM classes are also read back as state**: `getStatus`, `isEnabledOrExpected`,
  `doesRecordingExistForCurrentSelection` (reads `#audio-play`'s `enabled` class), and
  `split()` all consult button classes. The new in-memory model must become the single
  source of truth for these reads, not just a render target.
- **Decision logic:** `changeStateAndSetExpectedAsync` → `updateButtonStateAsync` →
  `updateButtonStateHelper` (+ `updateListenButtonStateAsync`) query
  `/bloom/api/audio/checkForAnyRecording?ids=` and compute the correct status for each
  button. **This logic is valuable and must be preserved** — only its _output_ changes
  (from poking the DOM to updating the state model).
- Related helpers: `setEnabledOrExpecting`, `isEnabledOrExpected`,
  `removeExpectedStatusFromAll`, `disableInteraction`, `setDisableEverythingMode`,
  `updateSplitButton` (toggles the split wrapper's counter/visibility classes).

### 1.4 Cross-bundle duplication hazard
`audioRecording.ts` is pulled into **three** bundles by static imports:

| Importer | What it uses | Bundle |
|---|---|---|
| `bloomField/BloomField.ts` | `AudioRecording.createValidXhtmlUniqueId()` | page |
| `js/canvasElementManager/CanvasElementDuplication.ts` | `createValidXhtmlUniqueId()` | page |
| `js/canvasElementManager/CanvasElementContextControls.tsx` | `AudioRecording.audioExistsForIdsAsync()` | page |
| `toolbox/canvas/canvasControlRegistry.ts` | `AudioRecording.showTalkingBookTool()` | page (+ toolbox) |
| `js/bloomImages.ts` | constant `kPlaybackOrderContainerClass` | page |
| `spreadsheet/spreadsheetBundleRoot.ts` | `AudioRecording.getChecksum()` | spreadsheet |

(Plus legitimate toolbox-bundle importers: `toolboxBootstrap.ts`, `talkingBook.ts`,
`recordable.ts`, `AdjustTimingsDialog.tsx`, `musicToolControls.tsx`, `motionTool.tsx`.)

Note the mechanism precisely: Rollup emits shared chunks (one copy of the *code*), but
each iframe is a separate JS realm, so the page iframe gets its **own module instance**
and its own (never-initialized) `theOneAudioRecorder`. Today this is masked because
`getAudioRecorder()` routes through
`getToolboxBundleExports().getTheOneAudioRecorderForExportOnly()` to reach the real
toolbox singleton. Nothing in the build flags a module landing in multiple entry graphs —
verification in Step 0 has to be done by inspecting the import graph / built output.

### 1.5 The iframe framework (context)
Cross-iframe calls always go through `bookEdit/js/workspaceFrames.ts`:
`getEditablePageBundleExports()` (the `#page` frame's `window.editablePageBundle`, whose
curated surface `IPageFrameExports` is defined in `editablePage.ts` — currently nothing
audio-related), `getToolboxBundleExports()`, and `getWorkspaceBundleExports()` (root).
You must **never `import`** across the boundary — that pulls a file into the wrong
bundle. Bundle entry points are in `vite.config.mts` (`editablePageBundle` =
`bookEdit/editablePage.ts`; `toolboxBundle` = `bookEdit/toolbox/toolboxBootstrap.ts`).

Two lifecycle facts that shape everything:
- **The page iframe reloads on every page switch** (`switchContentPage` sets
  `iframe.src`); the toolbox iframe persists for the session.
- The toolbox iframe is expected to be merged into the root document eventually (the
  workspace root was already de-iframed). Phase A moves toward that: the UI becomes
  iframe-agnostic React.

### 1.6 The React tool pattern to follow (Motion / Music / LeveledReader)
- A tool extends `ToolboxToolReactAdaptor` (`toolboxToolReactAdaptor.tsx`) and implements
  `makeRootElement(): HTMLDivElement` + `id(): string`. The base class provides no-op
  lifecycle defaults (all overridable, may be async) and `adaptReactElement(element)`
  which mounts React into a wrapper div via `renderRoot`.
- Registration is already done: `ToolBox.registerTool(new TalkingBookTool())` in
  `toolboxBootstrap.ts`; `talkingBook` is in `alwaysOnToolIds` and has an icon entry in
  `ToolboxRoot.tsx`.
- `IReactTool.featureName` exists for subscription-badged tools; Talking Book doesn't
  need it.
- Styling: MUI (`@mui/material`) + Emotion `css` prop, wrapped in
  `<ThemeProvider theme={toolboxTheme}>` (from `bloomMaterialUITheme`), using Bloom's
  `react_components` (`BloomButton`, `BloomSwitch`, `BloomTooltip`, the l10n components
  `Div`/`Span`/`LocalizedString`/`useL10n`, `ToolBottomHelpLink`).
- Follow `.github/skills/react-useeffect/SKILL.md` when writing the component (it exists
  precisely for conversions like this).

### 1.7 UI inventory to reproduce (from the pug + engine code)
Controls, top to bottom (`talkingBookToolboxTool.pug`):
- `#disablingOverlay` (shown during show-playback-order mode via
  `setDisableEverythingMode`; the advanced section's playback-order switch deliberately
  z-indexes above it).
- "Check your recording setup" label (`...CheckSettingsLabel`) — a numbered step.
- Input-device icon `#audio-input-dev` (device-type SVG chosen by
  `updateInputDeviceDisplay` from `audio/devices` API; `title` = product name) +
  `#audio-devlist` popup menu (`selectInputDevice`: 2 devices toggle directly, >2 shows
  the menu; selection POSTs `audio/currentRecordingDevice`).
- Level meter `<canvas id="audio-meter" width=80 height=15>` — drawn imperatively by
  `setStaticPeakLevel`, fed by WebSocket context `"audio-recording"`, event id
  `peakAudioLevel` (`addAudioLevelListener`). `stopListeningForLevels()` POSTs
  `audio/stopMonitoring` and closes the socket. There is also `addMicErrorListener`
  (`recordingStartError`/`monitoringStartError` → toastr + disable record button).
- "Look at each sentence…" label (`...LookAtSentenceLabel`) — numbered step.
- The 7 button groups (button + optional counter label): record ("Speak", starts
  `expected`), play ("Check"), split ("Split", `AdjustTimings`, wrapper starts hidden /
  `initial-state`), next ("Next"), prev ("Back", no counter), clear ("Clear"),
  listen ("Listen to the whole page"). The numbered-circle counters come from CSS
  counters over `talking-book-counter` elements in `audioRecording.less`.
- `#advanced-talking-book-controls-react-container` → `TalkingBookAdvancedSection`
  (already React; reuse).
- Help link (`Common.Help` → `ToolBottomHelpLink`).
- `<audio id="player" preload="none">` — invisible; **belongs to the engine, not the
  UI** (see §4).

l10n keys the React version must keep emitting (verify against
`.github/skills/xlf-strings/SKILL.md` rules — no new strings should be needed):
`EditTab.Toolbox.TalkingBookTool` (heading, supplied by the React toolbox shell),
`.CheckSettingsLabel`, `.LookAtSentenceLabel`, `.SpeakLabel`, `.CheckLabel`,
`.AdjustTimings`, `.NextLabel`, `.Back`, `.Clear`, `.Listen`, `Common.Help`,
`Common.Pause` (dynamic play/pause swap). The advanced section and the import-recording
confirm dialog already have their keys in React code.

---

## 2. Target architecture

### Phase A (this PR): React UI, engine stays put

```
┌─ Toolbox iframe ──────────────────────────────────────────────┐
│  TalkingBookControls (React)          AudioRecording engine   │
│   • 7 buttons + counters + overlay      (same window)         │
│   • meter canvas + device menu        • state machine logic   │
│   • advanced section (existing)       • markup/highlight/     │
│   • help link                           playback/record/split │
│        │  direct method calls  ▲      • owns <audio> element  │
│        ▼                       │      • reaches page iframe   │
│      engine.commandX()   state listener   as today            │
└───────────────────────────────────────────────────────────────┘
```

Because UI and engine share a window in Phase A, the listener/command wiring is plain
same-realm JavaScript — no new cross-iframe plumbing at all. `getAudioRecorder()` and
the toolbox exports are untouched. Motion and Music keep working unchanged.

### Phase B (separate PR): engine moves to the page iframe

The engine relocates to live with the DOM it manipulates; the UI talks to it via
`getEditablePageBundleExports()`. §6 covers what that actually requires — chiefly a
deliberate answer to the per-page-lifetime problem from §1.5.

---

## 3. UI ↔ engine contract

1. **UI → engine (commands).** The React component calls engine methods directly (Phase
   A: same window via `getAudioRecorder()`): `startRecordCurrentAsync`,
   `endRecordCurrentAsync`, `togglePlayCurrentAsync` (and the ctrl-click
   `playESpeakPreview`), the split-dialog opener, `listenAsync`, `clearRecordingAsync`,
   `moveToNextAudioElement`, `moveToPrevAudioElementAsync`, `setRecordingModeAsync`,
   `setShowPlaybackOrderMode`, `setShowingImageDescriptions`, `insertSegmentMarker`,
   import recording, and device selection (see below).

2. **Engine → UI (state).** Add `registerStateListener(cb: (state: TalkingBookUiState)
   => void)` (and an unregister). The engine computes a single immutable
   `TalkingBookUiState` and pushes it whenever anything changes:

   ```ts
   interface TalkingBookUiState {
       buttons: Record<
           "record" | "play" | "split" | "next" | "prev" | "clear" | "listen",
           Status
       >;
       isPlaying: boolean; // drives the Check/Pause label swap
       splitButtonVisible: boolean; // today: updateSplitButton's wrapper classes
       recordingMode: RecordingMode;
       hasAudio: boolean;
       hasRecordableDivs: boolean;
       haveACurrentTextboxModeRecording: boolean;
       inShowPlaybackOrderMode: boolean;
       showingImageDescriptions: boolean;
       inputDevice?: { iconSrc: string; title: string };
       disableEverything: boolean; // the overlay
   }
   ```

   This subsumes both the existing `TalkingBookAdvancedSection` props flow and the
   `Status`-class DOM poking into one channel. `setStatus`/`getStatus`/
   `updateButtonStateHelper` are rewritten to read/write this in-memory model (which is
   now the *single source of truth* — remember §1.3's DOM-reads-as-state);
   **the decision logic in `changeStateAndSetExpectedAsync` and `updateButtonState*`
   stays as close to byte-for-byte as possible.**

3. **Imperative escape hatches (deliberately not in state):**
   - **Level meter:** 60fps-ish peak levels shouldn't round-trip through React state.
     The component renders `<canvas ref>` and registers the element with the engine
     (`setLevelCanvas(el | null)` on mount/unmount); `setStaticPeakLevel` keeps drawing
     exactly as today.
   - **`<audio>` player:** the engine creates its own element
     (`document.createElement("audio")`) lazily in `getMediaPlayer()` instead of finding
     `#player` in the pug. This removes the engine's last dependency on tool markup and
     keeps Motion/Music's `setupForListen()` working. (Being element-based, it needs no
     DOM attachment to play.)
   - **Device menu data:** UI state carries the current-device icon/title; on click the
     component asks the engine (`getInputDevicesAsync()` wrapping `audio/devices`,
     `setInputDeviceAsync(name)` wrapping the POST) and renders an MUI `Menu` — the
     jQuery `#audio-devlist` menu goes away, the engine keeps the API logic and the
     two-devices-toggle behavior.
   - **Toasts (mic errors, split errors):** stay engine-side via toastr, unchanged in
     Phase A.

`IAudioRecorder` (`IAudioRecorder.ts`) remains the implementation-free interface other
iframes use. Phase A doesn't need to extend it (same-window callers can use the class
type); Phase B moves the whole UI-facing surface into it.

---

## 4. Decomposition — what moves where (Phase A)

| Concern | Today (`audioRecording.ts` + pug) | Target |
|---|---|---|
| 7 buttons + counters + expected/active look | `<button>` + `setStatus` CSS classes | React in `TalkingBookControls`, driven by `state.buttons`; keep/port the CSS-counter numbering |
| Event handlers | jQuery wiring in `initializeTalkingBookToolAsync` | React `onClick`/`onMouseDown/Up` → engine methods (incl. ctrl-click eSpeak preview) |
| Button-state **decision logic** | `changeStateAndSetExpectedAsync`, `updateButtonState*`, `updateListenButtonStateAsync`, `disableInteraction` | **Stays in engine**, retargeted to emit `TalkingBookUiState` |
| `setStatus`/`getStatus`/`removeExpectedStatusFromAll`/`setEnabledOrExpecting`/`isEnabledOrExpected` + class *reads* (`doesRecordingExistForCurrentSelection`, `split`) | DOM class manipulation/inspection | in-memory model getters/setters; the `-list-item` branch is dead — drop it |
| Play-label "Pause" swap (`Common.Pause`, `originalPlayLabel`) | `setStatus` special case | `state.isPlaying` → label in React |
| Split-button show/hide (`updateSplitButton`) | wrapper class toggling | `state.splitButtonVisible` |
| Level meter canvas | `#audio-meter` + `setStaticPeakLevel` | React `<canvas ref>` registered with engine; drawing code unchanged |
| Device selection | `#audio-devlist` jQuery menu, `#audio-input-dev` | React MUI menu; engine keeps `audio/devices` + POST logic |
| `<audio id="player">` | pug element in toolbox doc | engine-created element (`getMediaPlayer` creates lazily); **not part of the React UI** |
| Disabling overlay | pug `#disablingOverlay` + class toggle | React conditional styling from `state.disableEverything` |
| Advanced section | React, re-rendered via `renderRoot` from `updateDisplay` | child of `TalkingBookControls`; `updateDisplay` becomes "recompute state + notify" |
| Busy cursors (`showBusy`/`endBusy`) | classes on `#toolbox`, page editables, root dialog | unchanged in Phase A (revisit in Phase B) |
| Page markup / highlight / split / timings / image-desc / playback-order / page listeners | engine, cross-iframe via `getPageDocBody()` | **unchanged in Phase A** |
| Help link | pug `a.help-link` | `ToolBottomHelpLink` |
| l10n (`data-i18n`) | pug + `insertLangAttributesIntoToolboxElements` | `Div`/`LocalizedString`/`useL10n`; same keys (§1.7); drop the legacy lang-attr path for this tool |

---

## 5. Phase A step-by-step

### Step 0 — De-duplicate across bundles (prerequisite + iframe hygiene)
Extract the statics/constants consumed outside the toolbox into small, dependency-light
modules so the page and spreadsheet bundles stop importing the whole engine:

- `createValidXhtmlUniqueId` → a shared id util (BloomField, CanvasElementDuplication).
- `audioExistsForIdsAsync` + `kAnyRecordingApiUrl` → a shared audio-query util
  (CanvasElementContextControls; `recordable.ts` can use it too).
- `getChecksum` → shared util (spreadsheetBundleRoot).
- `kPlaybackOrderContainerClass`, `kAudioSentence`, `kAudioCurrent`, `AudioMode` → a
  shared constants file (bloomImages, AdjustTimingsDialog, etc.).
- `showTalkingBookTool` → a toolbox-activation helper (canvasControlRegistry) — it
  already just calls `getToolboxBundleExports().activateToolFromId(...)`.

Then confirm `audioRecording.ts` is reachable from exactly **one** entry graph
(toolbox). Nothing in the build flags multi-bundle modules, so check the built output —
e.g. that no chunk imported by `editablePageBundle.js` or `spreadsheetBundle.js`
contains a distinctive engine symbol (`initializeTalkingBookToolAsync`). This step is
independently landable.

### Step 1 — Build `TalkingBookControls` (toolbox React component)
New `TalkingBookControls.tsx` rendering the full §1.7 inventory (minus the player, which
the engine now owns) under `<ThemeProvider theme={toolboxTheme}>`, matching Motion/Music
styling conventions. It takes the engine reference, holds `TalkingBookUiState` in
`useState` fed by `registerStateListener` (registered in an effect with cleanup —
follow the react-useeffect skill). Reuse `<TalkingBookAdvancedSection>` as-is, now fed
from the same state object. Control styling may shift toward the MUI look used by other
tools (pixel-identical is not required), but keep the numbered-circle counters and the
overall layout.

### Step 2 — Refactor the engine's UI layer into the state model
In `audioRecording.ts`:
- Replace the DOM work in `setStatus`/`getStatus`/`removeExpectedStatusFromAll`/
  `updateSplitButton`/`setDisableEverythingMode`/`updateInputDeviceDisplay` with the
  in-memory `TalkingBookUiState` + `notifyStateChanged()`. All class *reads* (§1.3) now
  read the model.
- `updateDisplay()` becomes "recompute state, notify" (its `renderRoot` call is deleted).
- Delete the jQuery event wiring in `initializeTalkingBookToolAsync` (React owns
  events); keep everything else that method does (player setup, toastr options, feature
  status, default recording mode).
- `getMediaPlayer()` creates the engine-owned `<audio>` element; `selectInputDevice`
  splits into data/command methods for the React menu; the meter targets the registered
  canvas.
- Keep `changeStateAndSetExpectedAsync` and the `updateButtonState*` decision logic
  intact, and don't disturb `setupForListen`/`listenAsync` (Motion/Music call them on
  their own `new AudioRecording(...)` instances, which after this step no longer need
  any tool DOM to exist).

### Step 3 — Convert `TalkingBookTool` to the React adaptor
- `TalkingBookTool extends ToolboxToolReactAdaptor`; `makeRootElement()` →
  `adaptReactElement(<TalkingBookControls .../>)`. Keep every override from §1.1:
  the async lifecycle, `isUpdateMarkupAsync() === true`, `updateMarkupAsync` returning
  the commit function, `isAlwaysEnabled() === true`, `beginRestoreSettings` →
  `beginLoadSynphonySettings()`, and the phrase-delimiter/image-description page work.
  The `updateMarkupAsync` contract (`toolbox.ts` ~line 1529, BL-10133) must be
  preserved: no DOM mutation except via the returned synchronous function.
- Remove the two legacy routings (`subpath` in `toolbox.ts`, `legacyToolSubPathByToolId`
  in `ToolboxRoot.tsx`) so the tool flows through `makeRootElement`. Delete
  `talkingBookToolboxTool.pug` and its `.html` build output wiring. Confirm the React
  toolbox shell supplies the header label and ordering that the pug's `h3`
  (`data-order='30'`, `data-i18n`) used to provide.
- Keep the `talkingBook` icon / `alwaysOn` entries in `ToolboxRoot.tsx`.
- Split the stylesheet: page-side highlighting/markup styles stay in
  `audioRecording.less`/`.css` (still listed in `editablePage.ts`'s `styleSheets` array
  and loaded by `toolbox.pug`); toolbox-only button/meter rules migrate into Emotion in
  the component or a component-scoped stylesheet, and the toolbox `<link>` can drop once
  nothing toolbox-side needs the file. (Note: both iframes load the *compiled css* via
  link tags, not JS imports.)

### Step 4 — Verify
- Bundle hygiene: `TalkingBookControls`/`talkingBook.ts` don't leak into the page
  bundle; Step 0's single-graph property still holds.
- Dialogs (`showAdjustTimingsDialogFromWorkspaceRoot`, the import-recording confirm
  dialog) still render in the **root** document via `getWorkspaceBundleExports()`.
- The manual matrix in §8.

---

## 6. Phase B (separate PR): move the engine to the page iframe

Goal: the engine lives with the content it manipulates; every `getPageDocBody()` /
`getPageFrame()` collapses to the local `document`, removing the class of cross-iframe
timing bugs where the engine reaches through `parent.window` while frames swap.

**The core design problem — engine lifetime.** The page iframe reloads on every page
switch, so page-side `theOneAudioRecorder` is destroyed/recreated per page. Design
explicitly for that:

- Enumerate fields that today outlive a page and decide a home for each:
  `inShowPlaybackOrderMode` (deliberately persists), `showingImageDescriptions`
  (recomputed by the tool each page, but cached), `cachedCollectionDefaultRecordingMode`,
  `wholeTextBoxAudioFeatureStatus`, `sentenceToIdListMap`, `previousRecordMode`,
  `isShowing`. Natural home: the persistent toolbox-side React component (or tool
  object) re-seeds each fresh engine in `newPageReady`; per-page things just die with
  the page.
- The UI must treat its engine reference and listener registration as per-page: re-fetch
  and re-register on every `newPageReady`, and tolerate a missing engine between pages.
  Cross-frame function refs (including the commit function returned by
  `getUpdateMarkupAction`) go stale if the page navigates mid-flight.
- One-time init (`initializeTalkingBookToolAsync`) becomes per-page init; the websocket
  subscription (`audio-recording` context) is torn down and re-created per page —
  verify the C# side tolerates that, and avoid double-subscription.
- Playback/recording that spans a page switch dies with the iframe (probably acceptable
  — it is already suspect today — but state it and test it).
- Toasts and busy-cursor UI would now originate in the page iframe; make sure they still
  present correctly (toastr renders in the engine's document).

Mechanics (mostly as previously planned):
- Construct `theOneAudioRecorder` page-side; expose `getTheOneAudioRecorder()` on
  `IPageFrameExports` / `window.editablePageBundle` in `editablePage.ts` (where that
  interface actually lives). Flip `getAudioRecorder()` to
  `getEditablePageBundleExports()`. Extend `IAudioRecorder` to cover the full UI-facing
  surface, including `registerStateListener`.
- **Churn-minimizing trick:** keep the helper names `getPageDocBody()`/`getPageFrame()`
  and reimplement only their bodies to return the local document. That leaves the ~35
  call sites untouched so the diff shows only genuine changes. (Watch the two spots
  that use `getPageFrame().src` — e.g. `urlPrefix()` — which need `window.location`
  instead.)
- **Motion/Music narration playback:** they use only `new AudioRecording(false)` +
  `setupForListen()` + `listenAsync()`. Extract that trio into a slim toolbox-usable
  narration-player module, or route them to the page-side engine via exports — the tiny
  surface makes either cheap; decide during implementation.
- Singleton discipline: after the flip, page-side code uses the local singleton;
  toolbox/root code always goes through `getEditablePageBundleExports()`. Leave no path
  that `new`s a second engine in the wrong frame (except the sanctioned narration
  player, if kept).

---

## 7. Watch-outs

- **Async / race hazards to respect (and, where cheap, fix):**
  - `newPageReady` is deliberately re-fired ~600ms later by
    `scheduleDelayedNewPageReady` in `toolbox.ts` (~line 890); it must stay idempotent.
  - `ensureHighlight(20)` polls every 200ms (~4s total) as a workaround for BL-10471;
    the `currentAudioSessionNum` counter guards against stale playback callbacks (it is
    bumped in six places). Preserve both; `clearTimeouts()` must keep working.
  - `updateMarkupAsync` must not mutate the DOM except via the returned commit function
    (BL-10133). Do not regress this when wiring through React.
  - The page-document listeners (capture mousedown, visibility MutationObserver) are
    added in `handleNewPageReady` and removed in `handleToolHiding` /
    `removeVisibilityObserver` — keep that pairing intact through the refactor.
- **State-model fidelity:** everything that used to *read* button classes (§1.3) must
  read the model, and the model must be updated before any code path that reads it —
  the old code had read-your-own-write consistency via the DOM.
- **Meter websocket:** keep exactly one subscription (`addAudioLevelListener` +
  `addMicErrorListener` on context `audio-recording`); the React meter registers a
  canvas, it does not subscribe itself.
- **CSS:** the split in Step 3 must keep the page-side highlighting styles loading in
  the page iframe (via `editablePage.ts`'s `styleSheets` list) — they are unrelated to
  the tool UI.

---

## 8. Testing

- Keep the existing specs green: `audioRecordingSpec.ts` (~2,400 lines) and
  `talkingBookSpec.ts` exercise the engine and decision logic we are preserving.
  Expect real porting work: the specs' DOM fixtures include the toolbox button ids, and
  there are ~144 assertions touching classes/`getStatus`/`ui-audioCurrent` in
  audioRecordingSpec alone. Assertions about *page* markup (`ui-audioCurrent`,
  `MakeAudioSentenceElements`, next/prev navigation) stay as-is; assertions about
  *button* classes port to the state model (arguably becoming simpler and less brittle).
- Manual matrix (from the lifecycle comment at the top of `talkingBook.ts`): open
  toolbox → tool shows; create/switch page while open; type in a text box; switch tools;
  close toolbox; Origami "Change Layout" then off. Plus the full **record → check →
  split (adjust timings) → next** flow, sentence vs whole-text-box modes, phrase markers
  (`|` / segment marker), import recording (confirm-replace dialog), show-playback-order
  (overlay + bump up/down), image descriptions, input-device switching (0, 1, 2, and >2
  devices), mic-error toast (unplug device mid-record), ctrl-click eSpeak preview, and
  Motion/Music "Preview with narration" (their playback path shares this engine).
- Use the `run-bloom` skill to drive the desktop app for manual verification.

---

## 9. Deferred: Step 6 — retire the old non-React toolbox framework (separate PR)

Once Phase A lands and is verified, Talking Book is no longer non-React, so the legacy
framework can be deleted. This is **intentionally a separate follow-up PR** — it touches
shared toolbox machinery used by all tools and would swamp the conversion diff; it is
not meaningfully easier to do in the same change, and the conversion must be proven
first. (It does not need to wait for Phase B.)

Candidate deletions for that PR:
- The `subpath` map and `beginAddTool`'s premade-HTML branch in `toolbox.ts`.
- `legacyToolSubPathByToolId` and the `LiveToolBodyHost` / legacy jQuery-accordion bridge
  in `ToolboxRoot.tsx`.
- `insertLangAttributesIntoToolboxElements` and its `data-i18n` scan.
- Related pug/HTML build wiring.

Rough size: comparable to the conversion itself.
