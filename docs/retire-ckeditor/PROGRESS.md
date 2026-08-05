# CKEditor retirement — progress log

**Ticket:** BL-6681. **Plan:** [PLAN.md](PLAN.md). **Settled decisions:** [REVIEW-NOTES.md](REVIEW-NOTES.md).

To resume after an interruption, issue **`/resume-ckeditor`** (see
`.claude/skills/resume-ckeditor/SKILL.md`). Equivalent plain-English prompt:
*"Read docs/retire-ckeditor/PROGRESS.md and continue from the next unchecked item."*

## Current state

**Phase: Stage 0, environment fixed, code work done; live verification remains.** Branch
**`BL-6681-stage0-inventory`**, 6 commits, **not pushed, no PR yet**. All of PLAN.md §10 is decided
except the Stage-5 legacy-cleanup lifetime, which blocks nothing.

Stage 0 checklist (PLAN.md §6):

- [x] Planning docs committed (`b7e849c62`)
- [x] `BEHAVIOR-INVENTORY.md` — sections A–K plus cross-cutting X1–X7 (`c435b9708`)
- [x] Characterization tests for the selection functions (`07f4500a8`) — 10 tests, passing,
      falsification-checked. Covers inventory G4/G5.
- [x] Environment unblocked: `vp`/`volta` PATH untangled, `init.sh` clean, `output/browser`
      repopulated. Full front-end suite green: **591 passed**.
- [x] `toolbox.ts` selection-bracket prep commit (`2707d98a8`) — §5.4 done
- [ ] Capture the paste/drop baseline (rows C1–C7, incl. **C7 drop**): needs a running Bloom
- [ ] Handler-accumulation repro (§4.10) + the X4 listener-leak test: needs a running Bloom
- [ ] Page-reload timing baseline (§4.11): needs a running Bloom
- [x] Rebased onto `origin/master` (was 64 behind; one conflict in `toolbox.ts`, resolved). Now 0
      behind. Typecheck clean, 63 tests green.
- [x] **G1 verified, both halves.** Automated: `verifyCaretPreservation.mjs` PASS (caret at the right
      offset, bookmarks consumed, no ZWSP). Manual, by John: decodable reader open, "real typing
      seems fine" — the case automation couldn't reach, and the check `toolbox.ts` itself prescribes.
- [ ] **G2** (async markup path / BL-10133 — where the prep commit made its one deliberate behaviour
      change) and **G3** (longpress) still unverified
- [ ] **G6/G7** (new, from BL-16558): reader and Talking Book highlights are live Ranges and must
      survive typing — and our restore paths must repaint them

> ## ⚠ Toolchain: use `vp`, never Volta
>
> `ReadMe.md` "Building" is authoritative: install [vite-plus (`vp`)](https://vite.plus), which reads
> `.node-version` (**24.13.0**) and `packageManager`, then run `./init.sh`. Volta was dropped
> *because it does not fully support pnpm*.
>
**RESOLVED as of 2026-08-05.** `node` is 24.13.0 and `pnpm` 11.5.2, both served by `vp`, while
`volta` still works for the maintenance worktrees. How it was untangled, since the intermediate
states were each misleading:
>
> 1. **Volta's shim dir was in *Machine* PATH**, and Windows evaluates Machine before User, so vp's
>    User-scope `~/.vite-plus/bin` could never win. No User-PATH reordering can fix that; the Machine
>    entry has to go. Removed by the developer via elevated System-PATH edit.
> 2. **That broke more than node.** Every binary in `%LOCALAPPDATA%\Volta\bin` is a shim whose entire
>    body is `volta run "$(basename $0)" "$@"` — so with `volta.exe` off PATH, `pnpm`, `pnpx`,
>    `reviewable` (used by the `reviewable-replies` skill), `nx`, `nx-cloud` and
>    `chrome-devtools-mcp` all died with "volta: command not found". This is what made `init.sh`
>    report it could not find volta. **Note for future advice: that directory is not "harmless
>    because it contains no node" — its shims need `volta.exe`.**
> 3. **`vp` does not put `pnpm` on PATH.** `vp env doctor` lists its shims as node, npm, npx,
>    corepack, vpx, vpr — no pnpm. vp runs pnpm internally for its own subcommands (`vp install`),
>    but `init.sh` calls bare `pnpm`. Fixed with the corepack that vp ships:
>    ```sh
>    corepack enable pnpm --install-directory ~/.vite-plus/bin
>    ```
>    which honours each package.json's `packageManager` (11.5.2 in `src/BloomBrowserUI`; in the repo
>    root, which has no such field, bare `pnpm` reports corepack's own default — harmless, since
>    `init.sh` cds into the package directories first).
> 4. **Restored `volta` without letting it win**, by putting `C:\Program Files\Volta\` in **User**
>    PATH (appended, so after `~/.vite-plus/bin`) rather than Machine. Verified resolution order:
>    `node` → vp 24.13.0, `pnpm` → vp 11.5.2, `volta` → 2.0.2 available; `reviewable`, `nx` and
>    `chrome-devtools-mcp` working again.
>
> Net effect: `vp` governs this repo, `volta` remains usable for the four maintenance worktrees
> (`Version6.1`/`origin-Version6.1` node 16.14.0, `Version6.2` 22.11.0, `Version6.3` 22.21.1, all
> yarn-era), and nothing has to be uninstalled.
>
> **Traps, recorded so nobody repeats them:**
> - **Never `volta install`/`volta pin`** for this repo. The stale yarn-era `volta` field frozen in
>   `output/browser/package.json` makes it look like a Volta project; it isn't.
> - **Never remove `%LOCALAPPDATA%\Volta\bin` from PATH** while any `volta install`-ed global tool is
>   still wanted — see (2).
> - `CI=true` / `confirmModulesPurge=false` make pnpm skip its "remove node_modules?" prompt. That is
>   destructive and normally wrong. It *was* used deliberately once here, for the repair install
>   below, having first confirmed nothing was running — purging was the point.
>
> **What the repair actually was:** `node_modules` held **react-dom 17.0.2** where `package.json` and
> the lockfile both require **18.3.1**, so anything importing `react-dom/client` failed to *load* —
> breaking `bloomFieldSpec.ts`, `toolboxSpec.ts`, `ImageUndoManagerSpec.ts` and blocking Bloom
> itself. `CI=true vp install` in `src/BloomBrowserUI` fixed it (1m41s). Its `prepare` step also set
> `core.hooksPath -> .githooks` automatically.
>
> **Git hooks:** a stale **husky v4** hook in `.git/hooks/` hard-codes `packageManager=yarn` and was
> running because `core.hooksPath` was unset, so commits failed with a yarn lockfile error. Now fixed
> (and `vp install`'s `prepare` keeps it fixed). If it recurs in another clone/worktree:
> `git config core.hooksPath .githooks` — **not** `--no-verify`.

## Log

### 2026-08-04 — survey, plan, review round 1

Surveyed the whole CKEditor surface: `attachToCkEditor` / `BloomField.WireToCKEditor`,
`config.js`, the bookmark & filling-char machinery in `editableDivUtils.ts` and `toolbox.ts`,
the startup-race workarounds in `toolbox.ts` / `StyleEditor.ts` / `PlaceholderProvider.ts` /
`GamePromptDialog.tsx`, and the C#-side artifact scrubbing in `HtmlDom`, `BookData`,
`XmlHtmlConverter`, `PublishHelper`, `BookProcessor`, `BloomServer`, `ProjectContext`. Mapped
the five existing undo mechanisms and how `workspaceRoot.handleUndo`/`canUndo` arbitrate.

Wrote [PLAN.md](PLAN.md), had Fable review it against the real source, verified every finding
independently, and revised. The material changes from the review are recorded in
[REVIEW-NOTES.md](REVIEW-NOTES.md); the two that changed the shape of the plan were:

- **Undo entries must be data, not closures** — the page iframe's JS context dies on same-page
  reloads (zoom, origami exit), so closure-bearing entries would mutate detached documents.
- **Stages 1–2 re-cut** — snapshot restore now waits for Stage 3, because restoring innerHTML
  under live CKEditor orphans `div.bloomCkEditor`, and since `doCkEditorCleanup` iterates that
  expando the **save path would silently skip cleanup** for restored divs. Delete-page and
  delete-canvas-element moved earlier, since they're what the user actually asked for and are
  independent of that machinery.

### Findings worth remembering (all folded into the plan)

- `bootstrap()`'s BL-3125 `.bloom-canvas` guard (`bloomEditing.ts:1216`) is **dead code** —
  `this` is `undefined` in a strict-mode module function, so `$(this).find(...)` is always empty.
  Canvas-element editables do get CKEditor via `CanvasElementManager.addEventsToFocusableElements`.
- `toolbox.ts:1530-1537`'s comment that ArithmeticTemplate boxes get no editor is **wrong**;
  `.Equation-style[contenteditable='true']` is in `ckeditableSelector` (`utils/shared.ts:16-19`).
  The real no-editor case is `cursor: not-allowed` (`bloomEditing.ts:1952`).
- `workspaceRoot.ts:125`'s "*see also Browser.Undo*" C# fallback comment is **stale** — no such
  fallback exists in the WebView2 code.
- `config.undoStackSize = 0` doesn't disable the stack; CKEditor 4 reads
  `config.undoStackSize || 20`, so it silently means 20.
- The toolbox "undo" is a per-editable **text-typing** undo, not a reader-setup undo, and its
  precedence over CKEditor's is deliberate.
- Support-file cleanup runs only from `Book.BringBookUpToDate` and publish/upload paths, not on
  page save — so undo restoring audio spans is largely safe within a session.

### 2026-08-04 (later) — BL-6681 read and folded in

`$YOUTRACK_BOT` is now set and authenticates as `Bot`. Read the ticket and its five comments.
Two findings changed the plan; both were verified in the code first.

- **A clipboard requirement the plan had missed entirely** → new §4.8, new `clipboard.ts` in
  Stage 3, new open question §10 q4. The 2026 comment from the BL-16459 investigation states the
  test a replacement must pass: *can Bloom supply the clipboard payload (rich and plain) and be
  told whether the write succeeded?* Chromium never reports clipboard write failure to JS, and a
  .NET clipboard *read* doesn't fail either (OLE serves a cached copy), so only a C# **write**
  gives an honest success signal — which is why a JS-only safe cut is impossible. PR #8140 built
  the copy-then-delete fix and withdrew it because Bloom's own cut can only write plain text, so
  every cut lost bold/links/inline pictures. Branch
  `origin/BL-16459-clipboard-failure-reporting` is deliberately preserved; **read it before
  re-deriving any of this.**
- **A CKEditor interference the plan had missed** → new §2 table row 13. CKEditor
  `preventDefault()`s copy/cut inside `.bloom-editable` and swallows paste, which is why a
  duplicate Ctrl+V `keydown` handler exists at `bloomEditing.ts:1764-1770`. Its own comment says
  as much. Deletable in Stage 5 once CKEditor is gone.

Also recorded the ticket's history in **§11**, because its title is much broader than its
original content and would otherwise mislead a future reader:

- BL-6681 was opened in 2018 about **one bug** — the talking-book `audioCurrent` highlight
  vanishing because CKEditor's async load re-set a `.bloom-editable` and clobbered a class
  (repro BL-6654) — not as a general proposal to remove CKEditor.
- **That root cause is already gone**, by rearchitecture: the highlight now lives in
  `AudioTextHighlightManager` / `this.highlightedElement`, and `audioRecording.ts:2711-2715`
  only strips `ui-audioCurrent` defensively for "older Bloom versions that used DOM marking".
  So the founding symptom is **not** a driver for this project. Don't go hunting for it.
- The surviving relative is `setHighlightSession` (`audioRecording.ts:198-202`, BL-15300), a
  superseding counter for overlapping page-setup rounds. Flagged in Stage 6 as *measure before
  touching* — "newPageReady fires twice" is not obviously CKEditor's fault.
- A 2018 comment noted CKEditor "is not designed at all to handle cross-iframe stuff" while
  Bloom's toolbox iframe must modify the page iframe, and that `onload` fires three times, once
  per frame. Useful background for the Stage 6 page-load simplification.

### 2026-08-04 (later still) — paste/drop filtering promoted to a first-class requirement

John pointed out that CKEditor's filtering of pasted HTML wasn't visible in the plan's inventory.
It *was* there, but buried inside a "paste pipeline" table row as if it were an implementation
detail, with no statement of why it matters. Fixed, and the investigation turned up a real gap:

- New **§4.8**, with the rationale in John's terms (users must not be able to introduce
  structures Bloom's UI could never create and that are hard to edit or delete) **and** the
  second rationale already written in `config.js:107-112` about not promising translators
  formatting they can't replicate. Preserved because Stage 5 deletes that file.
- **Verified gap: drop is currently filtered only by CKEditor.** Its clipboard plugin attaches
  its own `drop` listener and routes drops through the same filter as pastes
  (`ckeditor.js:622`). Bloom's own drop handling covers only internal canvas-element drags via a
  custom `text/x-bloom-canvas-element` type (`CanvasElementManager.ts:2069-2088`) and does
  nothing for externally-dropped HTML. **So this protection is invisible today and would vanish
  silently.** The new sanitizer must cover `insertFromDrop` as well as paste.
- Recorded the `allowedContent = true` + restrictive `pasteFilter` split as deliberate: the first
  attempt at BL-3899 filtered all content and broke BL-3976. So the sanitizer applies at the
  clipboard/drop boundary only — never as a DOM invariant, or it would reject Bloom's own markup.
- Recorded the BL-4775 ↔ BL-12357 tension: BL-4775 removed `span` from the filter entirely,
  BL-12357 had to allow `span{font-variant,color}` back for small caps and colour.
- Promoted to **risk 3** (renumbering the rest), moved `pasteSanitizer.ts` earlier in Stage 3,
  and added adversarial inventory rows to Stage 0 — to be captured against *today's* behaviour
  before anything changes.

Lesson for the remaining planning: a guarantee that fails **silently** needs its own inventory
row and its own risk entry, not a mention inside a row about something else.

### 2026-08-04 (later still, 2) — event-handler lifetime designed; an existing bug found

John asked what re-attaches event handlers, observers and the like after a snapshot restore, and
whether we need either an idempotent central setup function or document-level delegation. Counted
the real landscape first: **~40 `addEventListener` sites, ~50 jQuery `.on()` sites across ~25
files, 17 observer sites**, plus jQuery-UI `draggable`/`resizable`, `qtip`, `nicescroll`,
`longPress` and Comical. Answer written up as **§4.10**.

The design: distributed registration, centralized invocation, **teardown by `AbortSignal`**.
Each module calls `registerPageContributor(...)` in its own file; `setUpPage`/`tearDownPage`
know none of them; every listener takes `{ signal }` so one `abort()` removes them all, and
observers hang off the same scope via `addCleanup`. This is deliberately *not* the
"idempotent re-run" framing of the question: idempotency requires every handler to be dedupable
(no arrow functions, no closures — the discipline `CanvasElementManager.ts:943-945` is pleading
for), whereas signal-scoped teardown makes closures safe, so the easy way to write a handler
becomes the correct way. Enforcement by ESLint rule plus a CDP `DOMDebugger.getEventListeners`
leak test, because convention alone won't hold.

**Existing bug found while checking whether `SetupElements` is idempotent — it isn't.**
`SetupElements(container)` is already called re-entrantly on subtrees
(`CanvasElementManager.ts:1007` in `refreshCanvasElementEditing`, `imageDescription.tsx:336`),
but it calls `AddEditKeyHandlers(container)` (`bloomEditing.ts:727`), two of whose handlers attach
to **`document`** rather than the container (`:291` Ctrl+Space clear-formatting, `:301`
Ctrl+R/L/E justify), and which also attaches per-editable `keydown` handlers via jQuery `.on()`.
So handlers accumulate on every canvas-element refresh, today, with no restore path involved.
Most duplicated commands are near-idempotent, which likely explains why it went unnoticed; F6's
`insertHTML("<sup>"+selection+"</sup>")` is the plausible visible symptom.
**Found by code reading, not reproduced.** Attempt a repro in Stage 0 and file it as its own
card — it is independent of CKEditor and is the best evidence that this area needs the §4.10
design rather than more discipline. *Not yet filed in YouTrack.*

Two further consequences recorded:

- **The cross-frame half can't be solved by a page-frame registry.** `motionTool`, `GameTool`,
  `audioRecording`, `PlaceholderProvider`, `StyleEditor` and `BloomSourceBubbles` all observe
  page-frame elements from the *toolbox* frame. Reuse the hook that already exists for this:
  `applyToolboxStateToPage()` (`workspaceRoot.ts:164-170`). Restore must call it — "the DOM was
  replaced under you" is indistinguishable from "a new page loaded" for every one of these.
- **This strengthens the case for reload-based restore.** A real page load re-runs everything and
  needs zero migration of ~90 attachment sites. Recommended posture: ship full-page undo that
  way, treat in-place restore as a later optimization gated on `pageScope` adoption. Undo is not
  latency-critical, and it decouples undo from a large refactor. `pageScope.ts` is a new file
  that can land early and be adopted module by module — one small independent commit each.

### 2026-08-04 (later still, 3) — restore cost tiers; reload-without-save confirmed feasible

John noted that page reload is slow, so using it for every undo would be a visible regression for
small undos like typing, and asked whether an undo could reload without forcing a save. Both
points now in **§4.11**.

**Tiering (the more important half).** Restore cost is keyed to how much the operation touched:

- **Tier 1 — one editable** (typing, inline formatting, paste/cut in a box): restore
  `editable.innerHTML` + caret anchor. No reload, no C# round-trip. **Verified handler-safe** —
  nothing attaches handlers to nodes *inside* editables (no `addEventListener`/`.on()` on
  `audio-sentence`, `bloom-highlightSegment`, `bloom-linebreak`); they live on the
  `.bloom-editable` div, which survives an `innerHTML` replacement. And there is already a working
  precedent: `readerToolsModel.undo()` (`readerToolsModel.ts:574-591`) does exactly this. So the
  cheapest tier is the existing reader-tools undo, generalized with a proper caret anchor.
- **Tier 2 — one `.bloom-canvas` subtree**: restore subtree HTML + `refreshCanvasElementEditing`.
  No reload.
- **Tier 3 — page-structural** (essentially origami layout): full reinit, reload-based first.

So the overwhelming majority of undos never leave the page frame, and Tier 3's cost is acceptable
because it is rare. This also shrinks risk 2 considerably.

**Reload-without-save: yes, and the coupling runs the opposite way from the intuition.** Saving
does not exist to enable the reload — saving *forces* it, because the save path strips UI elements
and leaves the page invalid for editing (`State.SavedAndStripped`, `EditingStateMachine.cs:16-21`,
BL-13502). Nothing requires a disk write before navigating. Findings:

- `SaveThen(skipSaveToDisk: true)` already exists and already skips the disk write
  (`EditingModel.cs:220, 451`).
- For undo, even the `requestPageContent` round-trip is unnecessary — we already hold the HTML.
  So Tier 3 wants a narrower entry point than anything today: *install this HTML as page X in the
  book DOM, don't ask the browser, don't write to disk, then navigate.* Remaining cost: one
  HTML→XML conversion plus the navigation.
- Undo should deliberately **not** write to disk — the disk copy is already whatever it was, Bloom
  saves on page change, and an undo shouldn't create a save point.
- **Sharp edge:** an undo arriving during `SavePending` must not let the in-flight save merge the
  content being discarded. `DiscardInFlightSave()` (`EditingStateMachine.cs:367`) exists for this
  shape of problem. Folded into risk 5; decide discard-vs-defer deliberately.
- **BL-13502 is the same knot.** If saving stopped leaving the page invalid, `SaveThen` wouldn't
  need to navigate at all, making Tier 3 cheap and helping far more than undo. Out of scope, but
  worth noting on that ticket that undo is another reason to want it.

**Measure, don't guess** — added to Stage 0. The disk-plus-conversion hypothesis is plausible but
unmeasured, and the alternative (page-DOM regeneration + browser parse + `bootstrap`) wouldn't be
helped by skipping the save. Bloom's existing performance-log feature can attribute the time, and
the same measurement gives the baseline for showing that removing CKEditor made page loads faster —
a reload currently waits on CKEditor's async init.

### 2026-08-04 (later still, 4) — Tier 3 corrected: in place, and probably empty

John queried the Tier 3 proposal, correctly spotting that it conflated two mechanisms and
hand-waved the hard part: *if we navigate, the document comes from C# out of the book DOM, so how
does the undone state get in there apart from a Save?* It doesn't. **§4.11's Tier 3 was wrong and
is rewritten.**

- The only route into the book DOM is the save's merge phase —
  `UpdateBookDomFromBrowserPageContent` → `Book.UpdateDomFromEditedPage`
  (`EditingModel.cs:1760-1766`) — which strips the editing UI, propagates the data-div through
  `BookData`, recomputes feature requirements and decides full-vs-partial. We can skip asking the
  browser for content and skip the disk write, but **not the merge**. So "reload without saving" is
  "a save minus two of its three phases", and the phase it keeps (whole-book data-div propagation)
  is plausibly costlier than the disk write it drops. **Rejected.**
- Tier 3 now does what John described: install the snapshot into the live document, then run normal
  page init. No C#, no navigation, no save.
- Kept one incidental finding because it inverts a natural assumption: the coupling runs the
  opposite way — saving *forces* the reload, since the save path leaves the page stripped and
  invalid for editing (`State.SavedAndStripped`, BL-13502). Nothing requires a disk write before
  navigating.

**And Tier 3 is probably empty, so don't build a generic full-page restore.** Enumerated what would
land there: origami layout (already restores in place and works — `origamiRoot.replaceWith(clone)`,
sound because layout mode strips `contentEditable` at `origami.ts:132` **and** origami attaches all
its UI handlers via jQuery `.click()` at `origami.ts:404-457`, which is exactly what `clone(true)`
preserves — so **keep it**, and note that migrating origami to `addEventListener` would silently
break its undo); delete-page (C#-side, navigates anyway); style changes (deferred). Nothing left.

**Knock-on: `pageScope` (§4.10) is no longer a prerequisite for undo.** Still worth doing for the
handler-accumulation bug and for the new editor's own handler lifetime, but it gates nothing, so it
can be adopted module by module at any pace. Also means the origami and `ImageUndoManager`
conversions in Stage 4 depend on nothing in Stage 3 and could be pulled forward to Stage 1.

### 2026-08-04 (later still, 5) — the feature flag redesigned so testers can use it

John pointed out that a `localStorage` switch needs devtools, so testers can't use it, and asked
about an environment variable or a temporary Help-menu item. Bloom already has the right mechanism.
Now **§4.12**.

- **`ExperimentalFeatures`** (`ExperimentalFeatures.cs`) keeps tokens in
  `Settings.Default.EnabledExperimentalFeatures`, persisted per user, surfaced as checkboxes in
  **Collection Settings → Advanced** (`AdvancedSettingsPanel.tsx` + `CollectionSettingsDialog.cs`).
  Add `kNewTextEditor`. Better than a Help-menu item: no new menu, one place testers already know.
- **Plus `BLOOM_NEW_TEXT_EDITOR=1`** for developers and the canvas e2e specs, which launch Bloom
  themselves. Precedent: `BLOOM_AI_EDITOR_URL`, `BloomWV2Path`, `BloomSandbox`.
- **The page frame reads it synchronously from a body class**, because `useNewTextEditor()` runs
  per editable and the experimental-features API is async — reintroducing an async-init ordering
  problem in *this* project would be absurd. So C# decides in `Book.AddJavaScriptForEditing`
  (`Book.cs:621-629`): flag on → skip the CKEditor script tag **and**
  `AddClassToBody("bloom-newTextEditor")` (helper already exists, used for `template` at
  `Book.cs:1864`).

Two good properties fall out, neither of which the localStorage plan had:

1. **The flag is latched per page load by construction**, so you can never get a page with some
   editables on the CKEditor path and some on the new one. A setting change takes effect at the
   next page load.
2. **With the flag on, CKEditor is not loaded at all** — a far stronger test than loading it and
   bypassing it, since the flag-on build then cannot lean on CKEditor for anything we forgot.

**And the integration is already largely de-risked:** "CKEditor is absent" is an existing supported
mode, because `BookProcessor` strips the script tag for off-screen page processing. Guards are
already present at all four main integration points — `bloomEditing.bootstrap:1214`,
`StyleEditor.AttachToBox:1210-1211`, `toolbox.doWhenCkEditorReadyCore:995`,
`editablePage.ckeditorCanUndo:315`. With the flag on they already do the right thing, so the Stage 3
dispatches become "*also* start the new editor" rather than "skip CKEditor".

**John's decisions on the two costs I flagged:**

- **XLF:** the entry gets `translate="no"` — which is the `xlf-strings` skill's default for new
  entries anyway ("*Always mark new entries `translate="no"` unless instructed otherwise*"), so no
  translator effort is spent and later removal is free. **But this creates the project's one hard
  calendar deadline: the flag must be removed before the release carrying it goes beta**, since that
  is when strings are picked up for translation and the skill then forbids changing an entry's ID or
  source. Flagged with a ⚠ in Stage 5.
- **Don't clear the obsolete setting.** The flag never ships beyond in-house testers, so a handful of
  stale tokens is harmless and not worth migration code. The `SetValue("webView2", false)` precedent
  is noted in the plan as deliberately *not* followed, so a later reader doesn't "fix" it.

### 2026-08-04 (later still, 6) — last three open questions answered; §10 is now "Decisions"

- **Hyperlink UI: keep as-is.** `FormatToolbar.tsx` hosts the same button invoking the same
  `showLinkTargetChooserDialog`. No behaviour change.
- **Clipboard: seam only.** `clipboard.ts` produces rich+plain payloads behind one interface but
  still writes from JS; BL-16459 stays open. Explicitly *not* doing the C# multi-format write or the
  "HTML Format" byte-offset header. Added to the non-goals in §1.
- **Redo: in scope, extended rather than dropped — Ctrl+Y only, no toolbar button.** John was
  inclined to extend it if cheap, so I priced it first:
  - There is **no Redo plumbing in C# at all** today — no `RedoCommand`, no `SetEditingCommands`
    parameter, nothing in the `updateEditButtons` payload, no icon, no XLF entry. So a *button* is
    where the real cost is; Ctrl+Y is a JS-only handler needing none of it, and matches origami's
    existing affordance exactly.
  - Two cheap routes keep the stack cost near zero (now in §4.1): **index-based** stack with
    truncate-on-push, and **capturing the redo state lazily at undo time** rather than at commit
    time — so nothing is paid per keystroke, only when the user actually undoes. That lazy trick is
    already in the codebase: `origamiUndo` stashes a fresh clone before decrementing
    (`origami.ts:288-292`).
  - `redo?()` stays optional, so an entry without it acts as a redo floor. Delete-page redo (the one
    case needing real C# work) can therefore be deferred without blocking anything.
  - Sequencing constraint added to Stage 4: origami's `keydown.origami` handler must be retired in
    the *same commit* that converts its entry, or its Redo breaks in between.
  - Incidental: `readerToolsModel.redo()` (`:609`) appears **unreachable** — nothing exports or
    calls it. Noted in Stage 1; deleted in Stage 5 regardless.

§10 is retitled from "Open questions" to "Decisions", with reasoning kept inline so a later session
doesn't reopen settled ground. **One genuinely open item remains**, and it blocks nothing: whether
the C# `LegacyCkEditorCleanup` scrubbers stay indefinitely or get a one-time book migration. Decide
when Stage 5 lands.

### 2026-08-04 — Stage 0 started

Branch `BL-6681-stage0-inventory` off `master` at `2ca9f2f08c`.

**Environment fix needed first.** The first commit failed: a stale **husky v4** `pre-commit` hook in
`.git/hooks/` (installed 2026-01-29 from a *different* worktree, `…/BloomDesktop.worktrees/Version6.3`)
hard-codes `packageManager=yarn`, and yarn then died on the pnpm workspace. The repo has already
replaced husky with its own `.githooks` dispatcher, but `core.hooksPath` was unset in this clone, so
git was falling back to the stale hooks. Fixed the documented way (`.githooks/README.md`, "How to
enable it (per clone)"):

```sh
git config core.hooksPath .githooks
```

The dispatcher then correctly routed to `src/BloomBrowserUI/.vite-hooks/pre-commit` and the checks
passed. **Worth knowing for other clones/worktrees** — the symptom is a yarn lockfile error on
commit, and the fix is one git-config line, not `--no-verify`.

**`BEHAVIOR-INVENTORY.md` written.** Sections A–K plus cross-cutting X1–X7. Design choices in it
worth keeping:

- It covers only behaviours **at risk** — implemented by CKEditor or in code we will move. It
  deliberately excludes `BloomField.ManageField`'s CKEditor-independent behaviours (BL-786, BL-933,
  BL-952, BL-2274, BL-7061, BL-16518 …), which stay exactly where they are; listing them would dilute
  the rows that matter.
- Rows are tagged **⚠ capture first** (all of section C — paste/drop filtering) and
  **✗ must NOT survive** (workarounds we intend to delete, so nobody faithfully reimplements them).
  The ✗ rows are: the duplicate Ctrl+V keydown handler, the CKEditor artifact scrubbers on the
  *browser* side (C# ones stay for legacy books), the mid-word bookmark bug, and `bootstrap()`'s dead
  BL-3125 guard.
- Four rows need a *new* implementation rather than a port, each with the reason: **D8** (BL-12357
  small caps — depends on `cke/id`, meaningless without CKEditor), **K4** (`bloom-preventRemoval` —
  currently uses `execCommand("undo")`, the very stack we're fencing off), **A7/A9** (colour panel —
  superseded by Bloom's own colour dialog), and **G1** (should get strictly *better*, since offset
  anchors don't perturb the markup).
- H10–H17 record the project's *new* undo behaviour as acceptance criteria, separate from H1–H9's
  no-regression rows.
- X4 ("no listener leak") **fails today** — stated as such, so it reads as a known-red criterion
  rather than a passing one.

### 2026-08-04 — Stage 0 part 2: characterization tests, then blocked

**Found that existing coverage was much better than the plan assumed.** Inventory rows D1–D3, D5,
D6, B9, B10 and K3 are already tested in `bloomFieldSpec.ts`; F1, F2 and F4 in
`editableDivUtilsSpec.ts`. So rather than duplicating them, hunted for the genuine gap.

**The gap that mattered: `makeSelectionIn`'s `divBrCount`.** Nothing in the app passes anything but
`-1` for it (`readerToolsModel.ts:587-592`, `toolbox.ts`), yet §4.3's new selection anchors depend on
exactly that `<br>`-stepping behaviour. So it was completely unpinned. Now covered by
`bookEdit/js/editableDivUtilsSelectionSpec.ts` — 10 tests over the offset round-trip, counting across
inline markup, `divBrCount` 0/1/2-with-only-one-`<br>`, the not-at-a-boundary case, and `atStart`
either way.

**All 10 passed first run, which for characterization tests is a reason for suspicion, not
satisfaction.** So I falsified deliberately: changed the `divBrCount`-1 expectation from 2 to 99 and
confirmed it failed with `expected 2 to be 99`. The assertions genuinely observe the DOM rather than
passing vacuously. Reverted, re-confirmed green.

**A design finding the tests forced out:** both functions hard-code
`parent.window.document.getElementById("page")` and operate on that iframe's window, which is why the
test harness has to build such an iframe. The replacement `selectionApi.ts` should take a
document/root instead — testable, and reusable outside the page frame. Recorded in the spec's header
comment and in the commit message.

**Then hit the environment wall** (see the boxed warning at the top). The remaining three Stage 0
items all need either `toolboxSpec.ts` to run or Bloom to launch, and both need a working
`node_modules`.

I initially misdiagnosed this as "upgrade Node via Volta"; John pointed at `ReadMe.md`, which says
to install `vp` (vite-plus) and notes Volta was dropped precisely because it doesn't fully support
pnpm. The boxed warning now records the documented path and the two traps. **Lesson: check
`ReadMe.md`'s Building section before reasoning about toolchain state from what happens to be
installed** — the machine had Volta on PATH and no `vp`, which looks like a Volta project until you
read the docs.

Deliberately **did not** press on with the `toolbox.ts` prep commit: it refactors the most delicate
keystroke code in the app, its own spec can't currently load, and doing that unverified is exactly
the wrong trade. Better to stop and ask.

### 2026-08-05 — environment fixed, prep commit done

**Environment.** John installed `vp` and ran `init.sh`, but `react-dom` was still 17.0.2 — `init.sh`'s
backgrounded `pnpm install` had evidently failed under Volta's Node. Ran `CI=true vp install` in
`src/BloomBrowserUI` directly (nothing running, verified first); react-dom is now 18.3.1 with
`./client` exported, and the previously-unloadable specs pass. Full suite: **591 passed, 5 skipped**.

The residual PATH finding is in the boxed warning: Volta still wins for bare `node` and *cannot* be
outranked by User-PATH reordering, because Machine scope always precedes User scope. So all project
commands go through `vp`.

**Prep commit `2707d98a8` — `markupSelectionPreservation.ts`** (§5.4). Extracted the
save/restore-selection bracket out of `handleKeyboardInput` into four functions —
`boxParticipatesInMarkup`, `saveSelectionForMarkup`, `restoreSelectionAfterMarkup`,
`restoreAndResaveSelectionForMarkup` — keeping the CKEditor-bookmark implementation exactly as it
was. The saved value is typed `unknown[]` so callers can't peek; the planned replacement stores a
character offset instead. Now the anchor swap changes four function bodies and leaves the pipeline
alone.

Two things worth carrying forward:

- **One deliberate behaviour difference**, commented at the site and in the commit message. The async
  path previously called `createBookmarks` unguarded; had the editor reported no selection there it
  would have thrown inside an async function nobody awaits — an unhandled rejection leaving the pass
  half-done, comments already stripped and marker spans possibly still in the DOM. It now abandons
  the pass cleanly, which is what the first save has always done.
- **Corrected a long-wrong comment.** It claimed ArithmeticTemplate number boxes get no editor
  "because the logic that invokes WireToCKEditor is looking for classes like bloom-content1".
  `ckeditableSelector` explicitly includes `.Equation-style`, added for that very template. The real
  no-editor case is `cursor: not-allowed`. (Same error the inventory caught in the plan's own §2.)

**Verification gap to close:** `toolboxSpec.ts` covers only `cleanUpNbsps` and
`removeCommentsFromEditableHtml`, not the keystroke pipeline, so this refactor has **no direct test
coverage**. It rests on the typecheck, lint, the full suite, and a strictly mechanical diff. Inventory
rows **G1–G3** must be verified live before the PR — that is now an explicit checklist item.

### 2026-08-05 — live session: caret seam verified, harder half still open

Bloom launched cleanly from this worktree (HTTP 8089, CDP 8091). Two obstacles worth recording,
because both will recur:

- **The `2 EFL Books` collection is a Team Collection and Bloom is *Disconnected*,** so its books
  cannot be checked out — the Edit tab and "EDIT THIS BOOK" are both `aria-disabled`. Selecting a
  book is not enough. **Workaround that works: create a new book from a template**; new books are
  local, so the disconnected-TC block doesn't apply, and Edit becomes enabled immediately. (The
  alternative — switching to a non-TC collection such as `English Books` — means driving Bloom's
  WinForms collection chooser, which CDP can't reach, or restarting with different settings.)
- **A Basic Book's toolbox offers only Talking Book and "More…"**, and both stayed
  `visible: false` even after toggling `#pure-toggle-right`; no `audio-sentence` spans appeared, so
  `updateMarkup` never ran. The reader tools need a **Decodable Reader** or **Leveled Reader** book.

**What is verified.** `docs/retire-ckeditor/verifyCaretPreservation.mjs` (kept in the repo so the
next session doesn't rebuild it) types a character mid-word and checks where the caret lands. Run
twice independently, **PASS** both times:

| Check | Result |
| --- | --- |
| Text after typing `z` at offset 4 of "house" | `housze` ✓ |
| Caret character-offset afterwards | **5** — immediately after the typed character ✓ |
| Leftover `cke_bm_*` bookmark spans | **0** — the restore ran and consumed them ✓ |
| Stray ZWSP filling chars | **0** ✓ |

That exercises the wiring of all four extracted functions on the synchronous path, and confirms the
bookmark lifecycle still balances. It is real evidence the prep commit didn't break the pipeline.

**What is NOT verified — do not let this be forgotten.** With no tool active, `updateMarkup` never
runs, so the DOM is *unchanged* between save and restore. That is the easy half. Still open:

- **G1 proper** — caret survival while markup actually rewrites the DOM around it (the case
  bookmarks exist for at all).
- **G2** — the async-markup path and BL-10133 (keystrokes during the `await` must not land at the
  wrong position). This is the branch where the prep commit made its one deliberate behaviour
  change, so it deserves direct attention.
- **G3** — longpress interaction (BL-3900, BL-5215).

**To finish it:** create a book from the **Decodable Reader** template (not Basic Book), open the
toolbox, activate the reader tool, then re-run the harness — it already prints the span counts
needed to confirm markup ran. My initial expectation string in the harness was wrong (`houzse` for
`housze`, an off-by-one in my own arithmetic, not a defect in Bloom); it is corrected in the
committed version.

**Leftover to clean up:** creating the test book left `Book-121f7932` in
`Documents/Bloom/2 EFL Books/`. Harmless, but it is mine, not the developer's.

### 2026-08-05 (later) — rebased onto master; BL-16558 changes two premises

**Drift was real and immediate.** Master had moved **64 commits** ahead, and three files this stage
touches had changed: `toolbox.ts`, `bloomEditing.ts`, `BloomField.ts`. Rebased onto `origin/master`;
one conflict, in `toolbox.ts`, in exactly the region the prep commit refactored. Resolved by keeping
master's change and re-wording only my trailing comment. Now **0 behind master**; typecheck clean;
`toolboxSpec.ts` (7 tests now — master added two), `editableDivUtilsSelectionSpec.ts` and
`bloomFieldSpec.ts` all green, 63 tests.

Worth noting for the project's rebase strategy: this is the first stage, it sat unpushed for part of
one day, and it already collided. The plan's "land small PRs promptly, don't keep a long-lived
branch" (§5.2) is not theoretical — **get Stage 0 onto master.**

**BL-16558 (on master) invalidates two premises and adds a requirement.** The decodable and leveled
reader tools no longer rewrite the DOM to show violations: they paint `::highlight()` pseudo-elements
over **live `Range` objects** (`bookEdit/js/textHighlightManager.ts`, `readerHighlights.ts`, styles
at `editMode.less:1074-1100`). Talking Book's current-sentence highlight is the same
(`editMode.less:1145`). Consequences, now folded into the plan and inventory:

1. **§4.3's case against bookmarks got stronger, not weaker.** DOM-mutating marker spans are now
   actively hostile: inserting and removing nodes around the caret is exactly the churn live Ranges
   cannot survive. Previously the argument was "bookmarks briefly confuse the markup"; now it is
   "bookmarks would break the highlight architecture".
2. **New obligation on our restore paths (§4.11).** A Tier 1 undo restores `editable.innerHTML`,
   which rebuilds the text nodes and **collapses every live Range**, so the highlights vanish —
   with no error. `reinitializePageAfterRestore()` must repaint via `textHighlightManager` and
   `audioTextHighlightManager`. This is exactly why BL-16558 had to move `updateMarkup()` to after
   `cleanUpNbsps` and make the latter write `innerHTML` only when it changed something.
3. **New inventory rows G6/G7**, with the acceptance test `toolbox.ts` itself prescribes: type in a
   Leveled Reader book and watch the over-long sentences stay highlighted.

**G1 proper is now verified — by John, manually.** He switched to a non-TC collection, opened a
decodable reader, and reports "real typing seems fine". That is precisely the check master's own
comment asks for, and it covers the case automation could not reach (a reader tool active, markup
running). Combined with the automated harness result, the prep commit is verified on both halves.
**G2 (async path / BL-10133) and G3 (longpress) remain unverified.**

## Next actions

All of Stage 0's code work is done. What remains needs a **running Bloom** — do it in one session
(`run-bloom` skill), on branch `BL-6681-stage0-inventory`:

1. **Finish rows G1–G3.** The seam's wiring is verified; the DOM-rewriting case is not (see the
   2026-08-05 entry). Create a book from the **Decodable Reader** template, open the toolbox,
   activate the reader tool, and re-run `node docs/retire-ckeditor/verifyCaretPreservation.mjs
   <cdpPort>` — it already reports the span counts that show whether markup ran. Then G2 (async
   path / BL-10133, where the prep commit made its one deliberate behaviour change) and G3
   (longpress). Remember: a disconnected Team Collection blocks editing existing books, so make a
   new one; and `toolboxIsShowing()` gates markup, so the pane must genuinely be open.
2. **Capture the paste/drop baseline** → `PASTE-DROP-BASELINE.md`, rows C1–C7. Use a **real web-page
   clipboard payload**, not hand-written tidy HTML. Do it before any further code change — this is the
   row-set whose failure is silent. Include **C7 (drop)**, the row CKEditor has been covering
   invisibly.
3. **Handler-accumulation repro** (§4.10): drive `refreshCanvasElementEditing` repeatedly and watch
   for duplicate `document` keydown handlers via CDP `DOMDebugger.getEventListeners`; F6 is the
   likeliest visible symptom. File its own card if it reproduces. Add the X4 listener-leak test either
   way — it should fail before any fix.
4. **Page-reload timing baseline** (§4.11) with the performance-log feature.

Then: open the Stage 0 PR via `preflight`.

Note: launching Bloom uses `./go.sh`. If it fails with missing types like `PodcastUtilities` or
`IDevice` (CS0246), this worktree lacks its C# dependencies — run `./init.sh` (see `AGENTS.md`).

Later, not Stage 0:
- Before designing `clipboard.ts`, read PR #8140 and `origin/BL-16459-clipboard-failure-reporting`.
- Optional, offered but not done: comment on **BL-13502** that undo is another reason to want
  save/reload decoupled.

### 2026-08-05 (later still) — Stage 0 preflighted; PR #8153 open as draft

**PR:** https://github.com/BloomBooks/BloomDesktop/pull/8153 (draft). Branch pushed, card linked,
QA test-ideas comment posted, Devin consultation logged.

Reviewer outcomes at HEAD `178269d78`:

| Reviewer | Outcome |
| --- | --- |
| Local review (light, 1 subagent) | Clean — no correctness problems. It mutation-tested the new spec (neutering `selectAtOffset` fails 9 of 10 tests) and raised one accuracy note, which was fixed. |
| Devin | **Re-review clean** — 0 bugs, 0 investigate flags, 8 informational. Three informational items acted on; the rest declined with reasons, recorded in the PR consultation log. |
| CI (`pr-automation`) | pass |
| CodeRabbit | see the run's final report |

**Three things Devin's informational tier caught that were worth fixing** — a reminder that the
lowest-signal tier is not always noise:

1. The committed harness hard-coded `repoRoot = "C:/github/BloomDesktop"`, so it only ran in the
   checkout it was written in. Now derived from `import.meta.url`.
2. **The extraction has two deliberate behaviour differences, not one.**
   `restoreSelectionAfterMarkup` re-reads the editor and no-ops if it has gone, where the old code
   sat inside `if (ckeditorOfThisBox)` and would have thrown. Unreachable in practice
   (`bloomCkEditor` is assigned once and never cleared) but real, and now documented on the
   function. Notable because this branch already corrects two *other* comments in the same pipeline
   that misled by overstating — a third would have been poor form.
3. The inventory pointed at `PASTE-DROP-BASELINE.md` as if it existed.

**G3 (longpress) is now verified too** — John spot-checked it manually and reports it basically
works. So of the G rows, G1 and G3 are verified, and **G2 (async markup path / BL-10133) is the one
still open**, along with the new G6/G7 highlight rows.
