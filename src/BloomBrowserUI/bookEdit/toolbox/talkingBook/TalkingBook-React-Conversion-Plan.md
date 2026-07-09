# Plan: Convert the Talking Book tool to React

**Status:** proposed / not yet started.
**Scope of this document:** Steps 0–5 (the conversion). Step 6 (retiring the old
non-React toolbox framework) is deferred to a separate follow-up PR and is described
briefly at the end so it isn't lost.

Talking Book is the **last** non-React toolbox tool. Converting it lets us later delete
the entire legacy non-React tool framework (Step 6).

---

## 1. Current architecture (as of this writing)

### 1.1 `talkingBook.ts`
`TalkingBookTool implements ITool` directly (not via `ToolboxToolReactAdaptor`). Its
`makeRootElement()` **throws** — the tool's UI is the pug file
`talkingBookToolboxTool.pug`, routed in through two legacy hard-coded maps:

- `subpath` in `toolbox.ts` (~line 1273): `talkingBookTool: "talkingBook/talkingBookToolboxTool.html"`
- `legacyToolSubPathByToolId` in `ToolboxRoot.tsx` (~line 71): `talkingBook: "talkingBook/talkingBookToolboxTool.html"`

`talkingBook.ts` is a thin lifecycle shim that forwards to the `AudioRecording`
singleton: `showTool` (async: `initializeTalkingBookToolAsync` + `setupForRecordingAsync`),
`newPageReady` (async), `hideTool`, `detachFromPage`, and async markup
(`isUpdateMarkupAsync() === true`; `updateMarkupAsync()` returns
`getAudioRecorder().getUpdateMarkupAction()`).

### 1.2 `audioRecording.ts` (~4,900 lines) — engine **and** tool UI, running in the toolbox iframe
This is the important, counter-intuitive fact: **the `AudioRecording` object currently
runs in the _toolbox_ iframe, not the page iframe.** Evidence:

- The constructor reads toolbox controls via its own `document`:
  `document.getElementById("audio-split")`, `#audio-meter`, `#player`.
- `initializeTalkingBookToolAsync` wires jQuery handlers to the toolbox buttons
  (`$("#audio-record").mousedown(...).mouseup(...)`, `$("#audio-next").click(...)`, etc.).
- It reaches _into_ the page iframe for content via
  `getPageFrame()` → `parent.window.document.getElementById("page")` and `getPageDocBody()`.

So the object owns two very different kinds of responsibility:

1. **Tool UI** (toolbox side): the 7 main buttons, their counters, the level meter,
   device selection, the `<audio>` player, the "disabling overlay", and the
   button-state machine.
2. **Content engine** (conceptually page side): sentence markup / `audio-sentence`
   spans, highlighting, playback + timings, recording (via C# API), splitting, image
   descriptions, and playback order.

It already renders one React fragment: `updateDisplay()` renders
`TalkingBookAdvancedSection` (`talkingBookAdvancedSection.tsx`) into
`#advanced-talking-book-controls-react-container` via `renderRoot`, using a props object
the engine computes.

### 1.3 The button-state machine (the part that changes most)
- `Status` enum: `Disabled | DisabledUnlessHover | Enabled | Expected | Active`, applied
  today as **CSS classes on the 7 `<button>` elements** by `setStatus`/`getStatus`.
- Event wiring lives in `initializeTalkingBookToolAsync` (jQuery).
- **Decision logic:** `changeStateAndSetExpectedAsync` → `updateButtonStateAsync` →
  `updateButtonStateHelper` (+ `updateListenButtonStateAsync`) query the recording API
  and compute the correct status for each button. **This logic is valuable and must be
  preserved** — only its _output_ (poke the DOM) should change to (drive React state).
- Related helpers: `setEnabledOrExpecting`, `isEnabledOrExpected`,
  `removeExpectedStatusFromAll`, `disableInteraction`, `setDisableEverythingMode`.

### 1.4 Cross-bundle duplication hazard
`audioRecording.ts` is **also pulled into the page bundle** by static-only imports:

| Importer (page bundle) | What it uses |
|---|---|
| `bloomField/BloomField.ts` | `AudioRecording.createValidXhtmlUniqueId()` |
| `js/canvasElementManager/CanvasElementDuplication.ts` | `AudioRecording.createValidXhtmlUniqueId()` |
| `js/canvasElementManager/CanvasElementContextControls.tsx` | `AudioRecording.audioExistsForIdsAsync()` |
| `toolbox/canvas/canvasControlRegistry.ts` | `AudioRecording.showTalkingBookTool()` |
| `js/bloomImages.ts` | constant `kPlaybackOrderContainerClass` |

That creates a **second copy** of the class and of the module-level `theOneAudioRecorder`
in the page iframe. Today it's masked because `getAudioRecorder()` deliberately routes
through `getToolboxBundleExports().getTheOneAudioRecorderForExportOnly()` to reach the
real (toolbox) singleton. This is the "accidental duplicate singleton across iframes"
hazard; Step 0 removes it.

### 1.5 The iframe framework (context for the move)
Cross-iframe calls always go through `bookEdit/js/workspaceFrames.ts`:
`getEditablePageBundleExports()` (the `#page` frame's `window.editablePageBundle`),
`getToolboxBundleExports()` (the `#toolbox` frame's `window.toolboxBundle`), and
`getWorkspaceBundleExports()` (root). You must **never `import`** across the boundary —
that pulls a file into the wrong bundle. Bundles are Vite/Rollup entry points in
`src/BloomBrowserUI/vite.config.mts` (`editablePageBundle` = `bookEdit/editablePage.ts`;
`toolboxBundle` = `bookEdit/toolbox/toolboxBootstrap.ts`).

The toolbox iframe is expected to be **merged into the root document eventually** (the
workspace root was already de-iframed). This plan moves us toward that: the UI becomes
iframe-agnostic React, and the engine lives with the content it manipulates.

### 1.6 The React tool pattern to follow (Motion / Music / LeveledReader)
- A tool extends `ToolboxToolReactAdaptor` (`toolboxToolReactAdaptor.tsx`) and implements
  `makeRootElement(): HTMLDivElement` + `id(): string`. The base class provides no-op
  lifecycle defaults (all overridable, may be async) and `adaptReactElement(element)`
  which mounts React into a wrapper div via `renderRoot`.
- Registration is already done: `ToolBox.registerTool(new TalkingBookTool())` in
  `toolboxBootstrap.ts`; `talkingBook` is in `alwaysOnToolIds` and has an icon entry in
  `ToolboxRoot.tsx`.
- Styling: MUI (`@mui/material`) + Emotion `css` prop, wrapped in
  `<ThemeProvider theme={toolboxTheme}>` (from `bloomMaterialUITheme`), using Bloom's
  `react_components` (`BloomButton`, `BloomSwitch`, `BloomTooltip`, the l10n components
  `Div`/`Span`/`LocalizedString`/`useL10n`, `ToolBottomHelpLink`).

---

## 2. Target architecture

```
┌─ Toolbox/root iframe ────────────┐        ┌─ Page iframe ─────────────────────┐
│ TalkingBookControls (React)      │        │ AudioRecording engine (the object)│
│  • 7 main buttons + counters     │        │  • sentence markup / audio-sentence│
│  • record/play/split/next/prev/  │──────► │  • highlight + playback + timings  │
│    clear/listen handlers         │ calls  │  • recording (C# API) + split      │
│  • level meter + device display  │ via    │  • image descriptions, playback    │
│  • advanced section (existing)   │exports │    order, fixHighlighting          │
│  • disabling overlay             │◄────── │  • owns <audio> player             │
│  • holds button-state model      │notify  │                                    │
│    as React state                │        │  document === the page document    │
└──────────────────────────────────┘        └────────────────────────────────────┘
        TalkingBookTool extends ToolboxToolReactAdaptor
```

- **Toolbox/root side** owns everything the user sees and clicks, as a normal React tool.
- **Page side** owns the DOM engine. Because it now runs _in_ the page, every
  `getPageDocBody()` / `getPageFrame()` collapses to the local `document`.

---

## 3. Communication design across the boundary

Use the existing `workspaceFrames` framework; no new global plumbing.

1. **UI → engine (commands).** Add `getTheOneAudioRecorder()` to `IPageFrameExports` /
   `window.editablePageBundle` in `editablePage.ts`. Flip `getAudioRecorder()` to pull
   from `getEditablePageBundleExports()` instead of the toolbox exports. The React
   component invokes engine methods through this reference:
   `startRecordCurrentAsync`, `endRecordCurrentAsync`, `togglePlayCurrentAsync`,
   `listenAsync`, `clearRecordingAsync`, `moveToNextAudioElement`,
   `moveToPrevAudioElementAsync`, `setRecordingModeAsync`, `setShowPlaybackOrderMode`,
   `setShowingImageDescriptions`, `insertSegmentMarker`, import/split, device selection.

2. **Engine → UI (state).** Extend `IAudioRecorder` with
   `registerStateListener(cb: (state: TalkingBookUiState) => void)`. The engine computes
   a single immutable `TalkingBookUiState` and pushes it to the listener whenever it
   changes:

   ```ts
   interface TalkingBookUiState {
       buttons: Record<
           "record" | "play" | "split" | "next" | "prev" | "clear" | "listen",
           Status
       >;
       recordingMode: RecordingMode;
       hasAudio: boolean;
       hasRecordableDivs: boolean;
       haveACurrentTextboxModeRecording: boolean;
       inShowPlaybackOrderMode: boolean;
       showingImageDescriptions: boolean;
       currentInputDevice?: { name: string; iconSrc: string };
       disableEverything: boolean;
       isPlaying: boolean; // drives the Play/Pause label
   }
   ```

   This subsumes both the existing `TalkingBookAdvancedSection` props flow and the
   `Status`-class DOM poking into one channel. `setStatus`/`getStatus`/
   `updateButtonStateHelper` are rewritten to read/write this in-memory model instead of
   the DOM; **the decision logic in `changeStateAndSetExpectedAsync` and
   `updateButtonState*` stays as close to byte-for-byte as possible.**

`IAudioRecorder` (`IAudioRecorder.ts`) is the deliberately implementation-free interface
other iframes use; extend it, don't bypass it.

---

## 4. Decomposition — what moves where

| Concern | Today (`audioRecording.ts`, toolbox) | Target |
|---|---|---|
| 7 buttons + counters + expected/active look | `<button>` + `setStatus` CSS classes | React in `TalkingBookControls` (MUI/emotion), driven by `state.buttons` |
| Event handlers | jQuery `.click/.mousedown` in `initializeTalkingBookToolAsync` | React `onClick`/`onMouseDown/Up` → engine methods via exports |
| Button-state **decision logic** | `changeStateAndSetExpectedAsync`, `updateButtonState*`, `updateListenButtonState`, `disableInteraction` | **Stays in engine**, retargeted to emit `TalkingBookUiState` |
| `Status`/`setStatus`/`getStatus`/`removeExpectedStatusFromAll`/`setEnabledOrExpecting`/`isEnabledOrExpected` | DOM class manipulation | small in-memory model getters/setters, or folded into React |
| Level meter canvas + `addAudioLevelListener` | `#audio-meter` fed by websocket | React (toolbox); engine forwards peak levels (via state field or a small websocket subscription in the component) |
| Device selection (`selectInputDevice`, `updateInputDeviceDisplay`) | `#audio-devlist`, `#audio-input-dev` | React (toolbox); engine keeps `audio/devices` API calls and returns data |
| `<audio id="player">` + play-label swap (`Common.Pause`) | toolbox pug element | player element **moves to page iframe** with the engine; "Play/Pause" label is UI → React (`state.isPlaying`) |
| Disabling overlay (`setDisableEverythingMode`, `#disablingOverlay`) | toolbox pug + class toggling | React conditional styling from `state.disableEverything` |
| Advanced section | already React, re-rendered via `renderRoot` | becomes a child of `TalkingBookControls`; `updateDisplay`'s `renderRoot` call deleted |
| Page markup / highlight / split / timings / image-desc / playback-order | engine, reaching across via `getPageDocBody()` | **stays engine, now runs page-side**; `getPageDocBody()`→`document.body`, `getPageFrame()` retired |
| Help link | pug `a.help-link` | `ToolBottomHelpLink` React component |
| l10n (`data-i18n`) | pug + `insertLangAttributesIntoToolboxElements` | `Div`/`LocalizedString`/`useL10n`; drop the legacy lang-attr path for this tool |

### Churn-minimizing trick (please follow)
Moving the engine into the page iframe would otherwise change hundreds of call sites
(`getPageDocBody()` → `document.body`, etc.). Instead, **keep the helper names**
`getPageDocBody()` / `getPageFrame()` and reimplement only their _bodies_ to return the
local `document` now that the engine runs page-side. That leaves the bulk of the engine
untouched so the diff shows only genuine changes. Reviewers strongly prefer that
`audioRecording.ts` change as little as possible.

---

## 5. Step-by-step

### Step 0 — De-duplicate across bundles (prerequisite + iframe hygiene)
Extract the page-side-consumed statics/constants into small, dependency-light modules so
the page bundle stops importing the whole engine:

- `createValidXhtmlUniqueId` → a shared id util (BloomField, CanvasElementDuplication).
- `audioExistsForIdsAsync` + `kAnyRecordingApiUrl` → a shared audio-query util
  (CanvasElementContextControls).
- `kPlaybackOrderContainerClass`, `kAudioSentence`, `kAudioCurrent`, `AudioMode` → a
  shared constants file (bloomImages, etc.).
- `showTalkingBookTool` → a toolbox-activation helper (canvasControlRegistry).

Then confirm via the Vite manifest / chunk output that `audioRecording.ts` is in exactly
**one** bundle. Do this first — a clean single-iframe home for the engine is the whole
point of the move.

### Step 1 — Build `TalkingBookControls` (toolbox React component)
New `TalkingBookControls.tsx`: renders the check/meter row, the 7 main buttons with
counter labels, the disabling overlay, `<TalkingBookAdvancedSection>` (reused as-is), and
the help link — under `<ThemeProvider theme={toolboxTheme}>`, MUI + Emotion,
`BloomButton`/`BloomSwitch`/`BloomTooltip`/l10n components, matching Motion/Music styling.
It takes a `TalkingBookUiState` and an engine reference (or a callbacks bundle). Prefer a
functional component holding `useState` fed by `registerStateListener`. Control styling
may shift toward the MUI look used by other tools (pixel-identical is not required).

### Step 2 — Refactor the engine's UI layer into a state model
In `audioRecording.ts`: replace the DOM work in `setStatus`/`getStatus`/
`removeExpectedStatusFromAll`/`updateDisplay` with reads/writes of an in-memory
`TalkingBookUiState` + a `notifyStateChanged()` that calls the registered listener.
Delete the jQuery event wiring in `initializeTalkingBookToolAsync` (React owns events).
Keep `changeStateAndSetExpectedAsync` and the `updateButtonState*` decision logic intact.
Repoint `getMediaPlayer`, the level meter, and the device code at their new homes.

### Step 3 — Move the engine to the page iframe & flip the wiring
- Ensure `theOneAudioRecorder` is constructed page-side; export
  `getTheOneAudioRecorder()` from `editablePage.ts`. Flip `getAudioRecorder()` to
  `getEditablePageBundleExports()`.
- Reimplement `getPageFrame()`/`getPageDocBody()` bodies to return the local document
  (the churn-minimizer in §4).
- **Motion/Music narration-player dependency:** both do `new AudioRecording()` in the
  toolbox purely for playback (`motionTool.tsx`, `musicToolControls.tsx`). Decide during
  implementation between: (a) keep a slim playback-only path available toolbox-side, or
  (b) have them obtain the page-side engine for playback via exports. Pick based on how
  much playback logic they actually touch.

### Step 4 — Convert `TalkingBookTool` to the React adaptor
- `TalkingBookTool extends ToolboxToolReactAdaptor`; implement `makeRootElement()` →
  `adaptReactElement(<TalkingBookControls .../>)`. Keep the async lifecycle
  (`showTool`, `newPageReady`, `updateMarkupAsync` returning the commit fn,
  `detachFromPage`, `hideTool`). The `updateMarkupAsync` contract (see
  `toolbox.ts` ~line 1529) must be preserved: it must not mutate the DOM except via the
  synchronous function it returns.
- Remove the two legacy routings (`subpath` in `toolbox.ts`, `legacyToolSubPathByToolId`
  in `ToolboxRoot.tsx`) so the tool flows through `makeRootElement`. Delete the pug file
  `talkingBookToolboxTool.pug` and its `.html` output reference.
- Keep the `talkingBook` icon / `alwaysOn` entries in `ToolboxRoot.tsx`.

### Step 5 — Verify iframe hygiene
- Confirm `TalkingBookControls`/`talkingBook.ts` do **not** leak into the page bundle,
  and the engine does **not** leak into the toolbox bundle.
- Confirm exactly one `theOneAudioRecorder` exists (page side).
- Confirm dialogs (`showAdjustTimingsDialogFromWorkspaceRoot`, the confirm-replace
  dialog) still render in the **root** document via `getWorkspaceBundleExports()`.

---

## 6. Watch-outs

- **Async / race hazards to respect (and, where cheap, fix):**
  - `newPageReady` is deliberately re-fired ~600ms later; it must stay idempotent.
  - The `ensureHighlight` 4-second polling loop is a workaround for BL-10471; the
    `currentAudioSessionNum` counter guards against stale playback callbacks. Preserve
    both.
  - `updateMarkupAsync` must not mutate the DOM except via the returned commit function
    (BL-10133). Do not regress this when wiring it through React.
  - Moving the engine page-side removes a class of cross-iframe timing bugs (no more
    reaching through `parent.window` while frames swap) — a genuine simplification — but
    re-validate the `showTool` → `setupForRecordingAsync` and page-switch sequences.
- **Singleton discipline:** after the flip, page-side code uses the local
  `theOneAudioRecorder`; toolbox/root code always goes through
  `getEditablePageBundleExports()`. Leave no path that `new`s a second engine in the
  wrong frame.
- **Meter websocket** (`audio-recording` context, peak levels): decide whether the React
  meter subscribes directly or the engine relays; avoid double-subscription.
- **CSS:** `audioRecording.less`/`.css` is currently loaded into **both** iframes
  (`editablePage.ts` loads it too). Split into engine-side styles (highlighting on page
  content) vs UI-side styles (buttons/meter → Emotion in the component) so each iframe
  gets only what it needs.

---

## 7. Testing

- Keep the existing specs green: `audioRecordingSpec.ts` (~2,400 lines) and
  `talkingBookSpec.ts` exercise the engine and decision logic we are preserving. Specs
  that assert on DOM classes / `getStatus` will need porting to the new state model.
- Manual matrix (from the lifecycle comment at the top of `talkingBook.ts`): open toolbox
  → tool shows; create/switch page while open; type in a text box; switch tools; close
  toolbox; Origami "Change Layout" then off. Plus the full **record → check → split
  (adjust timings) → next** flow, sentence vs whole-text-box modes, import recording,
  show-playback-order, image descriptions, and input-device switching.
- Use the `run-bloom` skill to drive the desktop app for manual verification.

---

## 8. Deferred: Step 6 — retire the old non-React toolbox framework (separate PR)

Once Steps 0–5 land and are verified, Talking Book is no longer non-React, so the legacy
framework can be deleted. This is **intentionally a separate follow-up PR** — it touches
shared toolbox machinery used by all tools and would swamp the conversion diff; it is not
meaningfully easier to do in the same change, and the conversion must be proven first.

Candidate deletions for that PR:
- The `subpath` map and `beginAddTool`'s premade-HTML branch in `toolbox.ts`.
- `legacyToolSubPathByToolId` and the `LiveToolBodyHost` / legacy jQuery-accordion bridge
  in `ToolboxRoot.tsx`.
- `insertLangAttributesIntoToolboxElements` and its `data-i18n` scan.
- Related pug/HTML build wiring.

Rough size: comparable to the conversion itself.
