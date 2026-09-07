# CKEditor retirement — progress log

**Ticket:** BL-6681. **Plan:** [PLAN.md](PLAN.md). **Settled decisions:** [REVIEW-NOTES.md](REVIEW-NOTES.md).

## How to resume

Issue **`/resume-ckeditor`** (`.claude/skills/resume-ckeditor/SKILL.md`). Equivalent plain-English
prompt: *"Read docs/retire-ckeditor/PROGRESS.md and continue from the next unchecked item."*

**On a fresh clone or a different computer, check out a project branch first.** Nothing here exists
on `master` — not the plan, not the code, not even the skill file — because nothing may merge until
the 6.5 branch is cut. So `/resume-ckeditor` does not exist until you do this:

```sh
git fetch origin
git branch -r | grep BL-6681                     # what exists
git checkout BL-6681-stage1-undostack            # the current tip; see the branch table below
cd src/BloomBrowserUI && vp install              # if node_modules is absent or stale
```

The **branch table** below is authoritative for which branch is the working tip — it changes as
stages advance, so trust it over any branch name you remember. If `node` is not 24.13.0 or `pnpm`
not 11.5.2, read the boxed toolchain warning further down **before** touching the toolchain: the
answer is `vp`, never Volta, and the intermediate states are all misleading.

## Current state

> ## ⚠ Nothing merges to `master` until the 6.5 branch is cut
>
> Decided 2026-08-06 by John's manager: no part of this project may land on `master` until a
> `Version6.5` branch exists, which happens once 6.5 is mostly finished. **This reversed the plan's
> central rebase defence** ("land small PRs promptly, never keep a long-lived branch"), so §5 of
> [PLAN.md](PLAN.md) is rewritten around a long-lived integration branch. Read §5 before doing any
> branch work; the short version is the table below.

> ## ⚠ `Version6.5` has been cut (2026-09-04) — the merge window may be open
>
> The constraint below was "nothing merges to `master` until a `Version6.5` branch is cut". That
> branch now exists (`origin/Version6.5`, first commit 2026-09-04; master is 160 commits past it).
> Master's `AGENTS.md` carries a temporary header saying ordinary new work should target
> `Version6.5`, not `master`, during the transition — which is about 6.5 fixes. This project is 6.6
> work, so `master` is presumably now its correct target, and the integration branch could open its
> PR. **Nothing has been merged or retargeted; that is John's call**, and it changes §5's economics
> (stage PRs could go straight to master again). Raised in the 2026-09-07 entry.

Stage 0's PR is reviewed-ready and awaiting a human; its card is in *Ready For Code Review*, the QA
test-ideas comment is posted, and Devin is clean against HEAD `6bd49463`. **Stage 1 is live and
verified in a running Bloom** (2026-09-07): the Undo button and Ctrl+Y go through the one stack, and
four live checks show each legacy mechanism is reached exactly as before. It has no PR yet.

**Branch topology** — one integration branch tracks `master`; each stage is a short-lived branch off
it, PR'd into it and **squash-merged**, so integration carries one commit per stage:

| Branch | What | State |
| --- | --- | --- |
| **`BL-6681-ckeditor`** | The project's trunk. The only branch that merges `master` in. Eventually one PR into `master`. | Pushed. Synced to master `f0d9f1472` (2026-09-07) |
| **`BL-6681-stage1-undostack`** | ← **the working tip.** `bookEdit/undo/` — the one undo stack, **active**: `handleUndo`/`canUndo` delegate to it, Ctrl+Y bound in the page frame | Pushed (rebased onto integration 2026-09-07 — allowed: unreviewed, no PR). Green: 52 undo tests, full suite, typecheck. Live-verified. No PR yet; when there is one it targets `BL-6681-ckeditor`, not master |
| `BL-6681-stage0-inventory` | PR [#8153](https://github.com/BloomBooks/BloomDesktop/pull/8153) — docs, characterization tests, the `toolbox.ts` seam | Pushed; ready for review, awaiting a human. Left targeting `master` on purpose (§5.6). **Don't push more to it** — it would restart the review |

**Master-sync log** (§5.3 — record every sync here so the next drift check has a start point):

| Date | Merged `master` at | Watchlist commits in that range |
| --- | --- | --- |
| 2026-08-06 | `9b6ba1cd9` | **0** of 51 — clean merge, nothing of ours touched |
| 2026-09-07 | `f0d9f1472` | **11** of 433 — one conflict, `toolbox.ts` (BL-16717 made bookmarks conditional inside the extracted seam); resolved by teaching the seam. Nightly [34134257000](https://github.com/BloomBooks/BloomDesktop/actions/runs/34134257000): TS, C#, visual-regression and React suites **green**; BloomE2E failed 2 of 47 (Test Case 356 gear positioning — in active development in another worktree; 170 publish talking-book languages), neither near undo, and master's own nightlies have failed daily this week |

All of PLAN.md §10 is decided except the Stage-5 legacy-cleanup lifetime, which blocks nothing.

**Three Stage 0 verification items are deliberately still open** — they are
carried, not forgotten: the paste/drop baseline, the handler-accumulation repro, and the page-reload
timing baseline, plus G2/G6/G7 below. None of them block this PR, because it changes no behaviour in
those areas.

Stage 0 checklist (PLAN.md §6):

- [x] Planning docs committed (`b7e849c62`)
- [x] `BEHAVIOR-INVENTORY.md` — sections A–K plus cross-cutting X1–X7 (`c435b9708`)
- [x] Characterization tests for the selection functions (`07f4500a8`) — 10 tests, passing,
      falsification-checked. Covers inventory G4/G5.
- [x] Environment unblocked: `vp`/`volta` PATH untangled, `init.sh` clean, `output/browser`
      repopulated. Full front-end suite green: **591 passed**.
- [x] `toolbox.ts` selection-bracket prep commit (`2707d98a8`) — §5.7.3 done
- [x] **Paste/drop baseline captured** (2026-09-07, `PASTE-DROP-BASELINE.md`, C1–C7 incl. drop) — and
      it found that **the paste filter is bypassed whenever the payload contains a styled span**
      (BL-12357's `cke/id` test is always true), letting tables/iframes/images/divs into the book.
- [x] Handler-accumulation repro (§4.10) — **reproduced** 2026-09-07 (1 → 2 → 3 handlers);
      `liveChecks/handlerAccumulation.mjs` is the X4 test, failing until §4.10 lands
- [~] Page-reload timing baseline (§4.11) — superseded by BL-13502's measurements (see 2026-09-07)
- [x] Rebased onto `origin/master` (was 64 behind; one conflict in `toolbox.ts`, resolved). Now 0
      behind. Typecheck clean, 63 tests green.
- [x] **G1 verified, both halves.** Automated: `verifyCaretPreservation.mjs` PASS (caret at the right
      offset, bookmarks consumed, no ZWSP). Manual, by John: decodable reader open, "real typing
      seems fine" — the case automation couldn't reach, and the check `toolbox.ts` itself prescribes.
      **2026-09-07: the automated harness now also passes with the Decodable Reader tool active**
      (markup running), on the post-BL-16717 code.
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
    **← WRONG. Corrected 2026-08-06; see that day's entry. It is called from
    `decodableReaderTool.tsx:170`.**

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

**Prep commit `2707d98a8` — `markupSelectionPreservation.ts`** (§5.7.3). Extracted the
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
branch" is not theoretical — **get Stage 0 onto master.**

> **Overtaken by events, 2026-08-06.** That rule no longer exists: nothing may merge to master until
> the 6.5 branch is cut, so the project *must* keep a long-lived branch. §5 is rewritten around that.
> The observation itself still stands and is now the evidence for syncing weekly rather than
> occasionally — see §5.1, which measures the drift instead of guessing at it.

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


### 2026-08-06 — Stage 1's new code written; two plan claims about Undo found wrong

Stage 0's PR (#8153) is waiting on a human reviewer, so Stage 1's *new* code was written in parallel
on a **temporary local branch `BL-6681-stage1-undostack`** (branched off Stage 0's HEAD, so the docs
are in scope). New files only — nothing imports them, so the change is behaviourally inert and the
two reviews stay independent. The edits to existing files that would activate it are written up in
the new **[DEFERRED-EDITS.md](DEFERRED-EDITS.md)**, to land after Stage 0 merges. *(Superseded a few
hours later by the no-merging constraint — see the next entry. The edits are no longer blocked,
because Stage 0's commits became the integration branch's base.)*

**New:** `src/BloomBrowserUI/bookEdit/undo/` — `undoTypes.ts` (`IUndoEntry`,
`ILegacyUndoProvider`, `kMaxUndoEntries`), `UndoStack.ts` (index-based with truncate-on-push, O(1)
`canUndo`/`canRedo`, count bound, page scoping, lazy redo capture, legacy-provider arbitration,
`runUndoable` scope plumbing), `legacyUndoProviders.ts` (the four wrappers), `runUndoable.ts`, and
specs. **31 tests**, typecheck clean, no new lint warnings.

**Mutation-tested rather than trusted**, since they passed first run — three separate mutations, each
caught by exactly the test that should catch it: dropping truncate-on-push failed 1 test; disabling
the nested-push suppression failed 6; making `keepOnly` recompute the index unconditionally failed 1.

**Two claims in the plan turned out to be wrong, both found by spending the "worth a moment's check"
the plan itself asked for.** Both are corrected in PLAN.md at the point of error, not only here.

1. **`readerToolsModel.redo()` is *not* unreachable** — `decodableReaderTool.tsx:170` calls it. So
   there are **two** existing Redos, not one, and Stage 5's "deleted regardless" would have silently
   removed a working Ctrl+Y/Ctrl+Shift+Z for reader-tool typing.
2. **§3's ordering table describes the *button* path only.** `handleUndo()` has exactly one caller,
   `topBarButtonClick` (`bloomEditing.ts:1633-1648`) — the toolbar Undo button. There is **no Ctrl+Z
   handler in the workspace frame at all**, and C#'s `UndoCommand.Implementer` is an empty lambda
   (`WebView2Browser.cs:890`) that exists only so the button's `Enabled` can be set. Ctrl+Z is
   claimed in the *page* frame, by whichever gets it first: origami's `keydown.origami`
   (`origami.ts:137`, layout mode), the reader tools' per-editable handler
   (`decodableReaderTool.tsx:158-178`, whenever `currentMarkupType !== None` — it `preventDefault`s),
   then CKEditor, then native contenteditable undo.

   Three consequences, all recorded:
   - The deliberate reader-tools-before-CKEditor precedence is enforced for the keyboard by that
     `preventDefault`, **not** by `handleUndo`'s ordering. §3 said the ordering was what preserved
     it, so wrapping the providers "for free" was the right conclusion reached by a wrong argument.
   - Stage 1 is behaviour-neutral for a different reason than the plan gave: it changes only the
     button path. So "one consistent Undo stack" arrives for the button now and for the keystroke
     only when the page-frame handlers are converted (Stages 3–4). Worth being straight about.
   - **Redo cannot be a workspace-frame keydown handler**, which is what Stage 1 assumed. Keyboard
     events inside the page iframe never reach the parent document, and typing is precisely when
     Redo is wanted — which is why both existing handlers are in the page frame. Rewritten as
     DEFERRED-EDITS.md 1e, including that the new binding must be the last resort behind the two
     that `preventDefault`.

**Design points settled while writing it, so they are not reopened:**

- **Legacy providers are consulted before our own entries.** That reproduces today's button path
  exactly. It does *not* give true chronological order across the boundary (an entry recorded here
  followed by a legacy operation undoes in the right order; the reverse does not), which was already
  true between the old mechanisms. Documented on `UndoStack.undo` rather than fixed: inventing
  cross-mechanism sequencing for a state we are deleting is not worth it.
- **`push` is deliberately not exposed across frames.** Handing page-frame code a way to put a
  page-frame *closure* on the stack is the exact failure §4.1 exists to prevent, so the cross-frame
  surface must take data and let the workspace frame build the entry. Designed with Stage 2's first
  real caller, not before (DEFERRED-EDITS.md 1f).
- **`setCurrentPageId` is not enough on its own.** Ctrl+wheel zoom and leaving Change Layout mode
  reload the page frame *without* changing page, so `clearPageScopedEntries()` needs its own hook;
  `pageUnloading()` is the candidate, unverified. Harmless in Stage 1 (nothing pushes yet), and it
  must be settled before Stage 3 records typing.
- Page identity comes from `data-page-id`, reusing what `ImageUndoManager` already does
  (`ImageUndoManager.ts:154-161`) rather than inventing a second notion of it.

Incidental drift fixed while checking citations: `origami.ts:139-146` → `:137`, and the
`origamiCanUndo`/`origamiUndo` range → `:277-294`.
### 2026-08-06 (later) — the no-merging constraint, and §5 rewritten around it

John's manager ruled that **nothing from this project may merge to `master` until a `Version6.5`
branch is cut**, after 6.5 is mostly finished. That invalidates the plan's whole rebase strategy,
whose first rule was "don't keep a long-lived branch — land a dozen small PRs promptly". §5 is
rewritten; the old text is not worth preserving because following it is now impossible.

**Measured the drift instead of guessing at it**, since the sync cadence is the main decision and a
guess would give either paranoid over-syncing or a nasty surprise. Over the 30 days to 2026-08-06:

- `master` took **522 commits** (~17/day)
- of which **50** touched any file this project touches (~1.7/day)
- and four paths are **74%** of that: `bloomEditing.ts` (19), `toolbox.ts` (9), `BloomField.ts` (5),
  `lib/ckeditor/` (4)

Three things fell straight out, all now in §5.1:

- **Weekly syncing, not occasional.** ~12 watchlist commits per sync is tractable; a month's worth
  (~50) is what made the one Stage 0 rebase painful.
- **Stage 1's integration risk is near zero.** Every file its deferred edits touch —
  `workspaceRoot.ts`, `origami.ts`, `ImageUndoManager.ts`, `editablePage.ts` — had **zero** commits
  in 30 days. The cost lands in Stages 3 and 6, which is where `bloomEditing.ts` and `toolbox.ts`
  are.
- **`lib/ckeditor/` is still being actively patched** — 4 commits in 30 days to the library we are
  deleting. Each is a behaviour someone needed. Stage 5 must diff that directory against the
  project's start point rather than deleting a directory assumed frozen. New, and it would have
  been missed.

**Topology: one integration branch, not a chain of stage branches.** John suggested each stage
branching from the previous one and merging master into each in turn. Same diff and review
properties, worse economics: a chain costs *N* strictly-ordered merges per sync and keeps every
branch alive even when nobody is on it, where one integration branch costs a single merge and every
already-merged stage comes along free. By Stage 4 that is five merges per sync versus one. The chain
also has no natural place to squash; squash-merging each stage PR into integration gives the "one or
a few commits per stage" John asked for as a side effect of the merge button.

**Merge, never rebase — a deliberate reversal of what Stage 0 did.** Rebasing was right for a
short-lived unreviewed branch. It is wrong now: it rewrites commits already reviewed on a PR,
discards their review threads, needs a force-push, and replays every project commit over each new
master state so the same conflict is re-resolved repeatedly. A merge resolves it once and records
it. (`git config rerere.enabled true` is worth setting.) Individual unreviewed stage branches may
still be rebased.

**Two gaps this creates, both now closed in §5.5:**

1. **CI and Devin still work** — checked, rather than assumed: `pr-automation.yml` triggers on
   `pull_request: [opened, synchronize]` with **no base-branch filter**, so a stage PR into the
   integration branch gets the same checks and Devin trigger. Nothing to change.
2. **The nightly does not.** `nightly.yml` is schedule-only and is described in its own header as
   "everything on master" — and it is the only thing that runs the **full C# suite** and the
   **visual-regression suite**. A branch that never merges gets neither, for months, on a project
   that changes editing UI. It supports `workflow_dispatch`, so §5.5 requires
   `gh workflow run nightly.yml --ref BL-6681-ckeditor` after every master sync.

**Did the first sync while setting this up:** created `BL-6681-ckeditor` at Stage 0's HEAD and merged
`origin/master` (`9b6ba1cd9`). 51 commits behind after roughly one day — and **none** of them touched
the watchlist, so the merge was clean. A good illustration of why the raw commit count is the wrong
thing to watch.

**PR #8153 is left targeting `master`** (§5.6). Retargeting a PR mid-review churns it for no benefit,
and it cannot merge either way under the constraint. When the window opens, either merge it with a
**merge commit rather than a squash** (a squash would duplicate content the integration merge then
has to reconcile) or close it as superseded by the integration PR, which contains the same commits.

**One knock-on worth noticing: Stage 1's deferred edits are no longer blocked.** They were waiting on
"Stage 0 merges to master", which is now months away — that would have left Stage 1 unverifiable for
the whole period. Stage 0's commits are the integration branch's base, so the edits can land on the
Stage 1 branch now, and DEFERRED-EDITS.md's trigger is updated to say so.

### 2026-09-07 — master sync, Stage 1 activated and live-verified, BL-13502 assessed

Autonomous session (John mostly unavailable). Everything below is pushed.

**Master sync (§5.3).** Merged `origin/master` `f0d9f1472` into `BL-6681-ckeditor`: 433 commits, 11
on the watchlist, one conflict — `toolbox.ts`, in exactly the region the Stage 0 prep commit
extracted. Master's **BL-16717** (ligature glyphs vanishing) made the CKEditor bookmark *conditional*:
it is only taken when a tool is active or the box has a comment/nbsp to clean up, because the bookmark
span splits the text node. Rather than let that logic grow back inline, the seam learned it:
`saveSelectionForMarkup(editableDiv, boxMightBeRewritten)` records nothing when nothing can move the
caret, and `restoreSelectionAfterMarkup` no-ops on such a record. The decision is made *before* the
record, as master did, because the bookmark span itself contains an nbsp. Master's
`mergeAdjacentTextNodes` sweep stays in the pipeline after the restore — it is about backspace and
long-press splits too, so it must survive the anchor swap. Full suite 794 green; nightly triggered.

Two things to know about that merge:

- **The pre-commit hook reformatted three of master's own files** that were staged as part of the
  merge (`crowdin.yml`, `aiImageEditorOverlay.test.ts`, `SIL-Niger/branding.less`) — master's copies
  don't satisfy this repo's prettier. Restoring master's bytes needs a `--no-verify` commit, which I
  did not do without asking. Harmless noise; **John: say the word and it's one commit.**
- **§5.1's drift table was wrong for `workspaceRoot.ts`** — measured with the wrong path. It had 5
  commits, one of them BL-16558 changing `handleUndo` itself (see below). Corrected in PLAN.md.

**`Version6.5` exists.** See the box at the top. Not acted on.

**Stage 1 activated** (`f92383031`): DEFERRED-EDITS 1a–1e applied, three ways different from how they
were written:

1. **BL-16558** (master, 2026-08) had made `handleUndo` call `updateMarkupAfterUndoOrRedo()` after the
   reader-tools and CKEditor undos, because both rewrite an editable's innerHTML and so detach the
   `::highlight()` Ranges painted over it. The toolbox and ckeditor providers now do the same;
   `legacyUndoProvidersSpec.ts` pins it, and the order of the four.
2. **Page identity is `.bloom-page`'s `id`, not `data-page-id`.** Nothing in Bloom sets
   `data-page-id` — only `ImageUndoManagerSpec` does — so the check in
   `ImageUndoManager.clearImageOperationUndoOnPageChange` compares undefined with undefined and never
   fires (harmless there: the manager dies with the page frame). The plan's "reuse what
   ImageUndoManager does" would have reproduced a dead check. *Worth a small card of its own.*
3. **Ctrl+wheel zoom no longer reloads the page** — `EditingView.SetZoom` → `workspaceBundle.setZoom`,
   a CSS transform. The plan cited zoom as the canonical same-page reload in §4.1, §4.11 and 1d; all
   corrected. The real same-page reloads (leaving Change Layout mode, importing a video, changing the
   topic) **all go through `workspaceRoot.switchContentPage`** — the only route C# uses to navigate
   the page frame (`EditingView.cs`, three sites) — so 1d's question ("does `pageUnloading` fire on a
   same-page reload?") is moot: `pageFrameNavigating()` clears page-scoped entries in
   `switchContentPage` before the frame is touched, and `pageFrameLoaded()` records the id on load.

Redo is bound in the **page** frame (`undo/redoKeyBinding.ts`, one call from `editablePage.ts`), at
the document, bubble phase, acting only when nothing earlier claimed Ctrl+Y *and* the stack has
something to redo — so origami's and the reader tools' handlers and CKEditor's own redo keep winning
until converted. The workspace bundle grew `canRedo()`/`handleRedo()`.

**Live verification — four harnesses, kept in `docs/retire-ckeditor/liveChecks/`** (see its README).
Each wraps the cross-frame entry points and CKEditor's `afterCommandExec`, so a gesture is attributed
by counters rather than by "the text changed back". Results against Bloom launched from this worktree
(English Books collection):

| Check | Result |
| --- | --- |
| Decodable Reader tool active: Undo button → reader-tools undo only (`tb=1 ck=0 markup=1`, no CKEditor command); our Ctrl+Y binding never fires | **PASS** |
| Basic Book, no reader tool: Undo button → CKEditor undo only (`ck=1 tb=0 markup=1`); Ctrl+Z/Ctrl+Y run CKEditor's commands exactly once; our binding declines; round trip restores the text | **PASS** (7/7) |
| Change Layout mode: Undo button → `origamiUndo` (`ori=1`); origami's Ctrl+Z/Ctrl+Y fire once; ours declines | **PASS** (6/6) |
| Undoable copyright change on an image (`changeImageByElement`, the dialog's entry point): Undo button → `imageOperationUndo` (`img=1`), copyright restored | **PASS** (4/4) |
| Undo button enabled state tracks `canUndo()` in all four | **PASS** (observed each time) |

**Two pre-existing bugs the harness exposed** — both in the reader-tools mechanism, both caused by
the very things this project removes, neither introduced here (the provider calls exactly what the
old `handleUndo` called). Recorded as expected failures A4/A6/A7 in `verifyReader.mjs`:

- **The reader-tools undo restores a snapshot containing a stale CKEditor bookmark span.**
  `readerToolsModel.doMarkup` snapshots `innerHTML` while the `cke_bm_*` span is in the DOM, so
  undoing restores it: after one undo the box read `"…on sun\u00a0"` with a
  `<span id="cke_bm_31C" style="display:none">&nbsp;</span>` inside, and they accumulate (a page had
  two after two runs). The snapshot's `text` also carries the nbsp, so the "is this the current state"
  comparison in `undo()` fails and it steps back one fewer level than intended. This is the
  "mid-word bookmark bug" of the inventory's ✗ rows made concrete, and it is content corruption, not
  just wrong analysis.
- **Ctrl+Z with a reader tool active runs TWO undos.** The reader tools' per-editable handler
  `return false`s, which stops *propagation* — but CKEditor's keystroke handler is on the same
  element, so it fires regardless (`ckCmds: ["undo"]` observed alongside the reader undo). Ctrl+Y
  likewise runs both redos, and the round trip does not restore the typed text (`" pot"` was lost).
  §3's "the reader tools claim Ctrl+Z" is therefore only half true: they act, but so does CKEditor.
  *Should be reproduced on master and filed; it is user-visible today.*

**BL-13502 (`origin/BL-13502-save-without-reload`, PR #8209, draft, 22 commits, 20 behind master)
assessed** — likely to merge before us, and it matters to us more than expected:

- **It removes the in-flight-save hazard entirely.** `SavePending`, `SavedAndStripped`,
  `RequestBrowserToSave`, `editView/pageContent` and `DiscardInFlightSave` are gone; the browser
  *volunteers* the page (`pageSnapshot.ts`, a body `MutationObserver` + 25 ms debounce) and C# saves
  synchronously from the last snapshot. So §4.11's "sharp edge" and risk 5 evaporate — and a Tier 1
  innerHTML restore needs *no* save integration at all: the observer sees it and posts within ~50 ms.
- **Name clash.** Their `pageSnapshot.ts` / `PageSnapshot.cs` mean "the last content the browser
  posted for saving". Our Stage 3 `PageSnapshot` (an undo entry kind) must be renamed — `undoSnapshot`
  or similar — before it is written.
- **A new CKEditor dependency to inventory.** The save path now clones the body and copies CKEditor's
  cleaned data into the clone (`EditableDivUtils.copyCkEditorDataToClone`) instead of writing it back
  over the live editors. REVIEW-NOTES' "restored divs silently skip cleanup" concern changes shape but
  does not go away; Stage 3/5 must give that function a no-CKEditor path. Add to BEHAVIOR-INVENTORY
  once it merges.
- **Stage 2a's citations will be wrong.** `SaveThen` is now `MergeCurrentPageThenSave`, delete-page
  receives its content from the page list and the "capture inside the SaveThen callback" reasoning
  changes. **Do Stage 2a after BL-13502 merges**, re-deriving from the new code.
- **It answers the Stage 0 timing item.** Their `SavingWithoutReloading.md` measured a page change at
  ~790 ms, ~80% of it building the new page; gather is 0.4–0.7 ms; and "one keystroke produces ~9
  MutationObserver batches because CKEditor does a lot of DOM work per key". Adopt those numbers as
  the baseline (with their `benchPageChange.mjs`) rather than re-measuring; the ~9 batches per key is
  a ready-made before/after metric for Stage 6.
- Also touches `editablePage.ts` (the ready handler where our one-liner went), `toolbox.ts`,
  `bloomEditing.ts`, `origami.ts` and `decodableReaderTool.tsx` — expect a small conflict at the
  next sync after it lands; nothing structural.

**Handler accumulation (§4.10, inventory X4) — reproduced.** `liveChecks/handlerAccumulation.mjs`
stubs `document.execCommand` in the page frame, dispatches Ctrl+R (the handler on `document`) and F7
(the per-editable handler), and counts how many handlers fired; then calls the cross-frame
`editablePageBundle.SetupElements(page)` again, as `refreshCanvasElementEditing` does on a subtree:

| | Ctrl+R handlers fired | F7 handlers fired |
| --- | --- | --- |
| page as loaded | 1 | 1 |
| after one extra `SetupElements` | 2 | 2 |
| after two | 3 | 3 |

So the code-reading finding of 2026-08-04 is real, not theoretical. (Counting native listeners over
CDP would have shown nothing: jQuery multiplexes all its handlers behind one native listener, which
is why the plan's suggested `DOMDebugger.getEventListeners` check was the wrong instrument.) In real
use the trigger is anything that calls `refreshCanvasElementEditing` — adding or duplicating a canvas
element — after which F6 wraps the selection in `<sup>` twice. *Ready to file as its own card.*

**G2 (async markup path) attempted, still open.** With the Talking Book tool verified current
(`getCurrentTool().id() === "talkingBook"`, `isUpdateMarkupAsync() === true`, toolbox showing), typing
into a Basic Book text box produced **no `audio-sentence` markup at all** — and neither did calling
`updateMarkupAsync()` and applying its result directly. So the async branch of the keystroke pipeline
was not exercised, and the caret harness's PASS in that state proves nothing about G2. The tool has
some gating of its own (probably the box must be its current recording div, or the page must be one
it has set up) that I did not chase. **To close G2:** find what makes the Talking Book tool mark up a
box on typing (start in `audioRecording.ts`'s `updateMarkupAsync`), get spans to appear, then re-run
`verifyCaretPreservation.mjs` and check the caret *and* that the spans are there.

**Paste/drop baseline captured (§4.8, rows C1–C7)** — `liveChecks/pasteDropBaseline.mjs` →
`PASTE-DROP-BASELINE.md`, by dispatching synthetic `paste` and `drop` events carrying `text/html`,
which go through CKEditor's clipboard plugin and Bloom's paste transforms exactly as real ones do.
Every row except C5 behaves as the inventory says, and drop matches paste throughout. **C5 does not,
and the reason is a real bug:** `BloomField.restoreHtmlMarkupIfNecessary` (BL-12357) tests
`dataTransfer.getData("cke/id")` to detect an internal CKEditor copy, but CKEditor assigns an id to
*every* transfer, so the test is always true — and when the pasted HTML contains `<span style=` it
replaces CKEditor's *filtered* HTML with the *full* clipboard HTML. `liveChecks/pasteFilterBypass.mjs`
shows the same payload with and without one styled span: without, `table/iframe/img/div#id` are all
stripped; with, **all four reach the book**. So the BL-3899 guarantee is effectively off for
web-page pastes today. Full write-up in `PASTE-DROP-BASELINE.md` ▸ Findings. **This is worth a card
of its own, ahead of anything else found today** — it is a one-condition fix on master
(`getTransferType() === DATA_TRANSFER_INTERNAL`), and it should be confirmed once with a real
clipboard paste, since the capture used synthetic events.

**A harness lesson:** the jQuery-UI accordion's `h3.ui-accordion-header-active` class is not a reliable
"which tool is active" signal — it said Canvas Tool while the Talking Book panel was plainly open.
`toolboxBundle.getTheOneToolbox().getCurrentTool().id()` is; the harnesses now use that, and
`liveChecks/activateTool.mjs` switches tools through `activateToolFromId`.

### 2026-09-07 (later) — Stage 1 preflighted: PR #8317; two stacks found and fixed

**PR:** https://github.com/BloomBooks/BloomDesktop/pull/8317 (draft, into `BL-6681-ckeditor`). Devin
and a read-only local review both ran; every finding was acted on the same day. Two of them matter
beyond this PR:

1. **There were two "one" stacks.** The Undo button does not call the workspace bundle's
   `handleUndo`: C# runs `getEditablePageBundleExports().topBarButtonClick({command:"undo"})` in the
   **page** frame, and `bloomEditing.ts` imported `handleUndo` from `../workspaceRoot` — so
   `workspaceRoot`'s module (with `theOneUndoStack` and the module-level provider registration) was
   also executing in the page frame, and the button undid from *that* copy. Neutral while both were
   empty, wrong the moment anything is pushed. **Fixed:** `topBarButtonClick` now calls
   `getWorkspaceBundleExports().handleUndo()`, the import is gone, and registration runs only when
   `window.parent === window`. The production build confirms why the guard is needed: Vite still puts
   `workspaceRoot` in a chunk (`requiresSubscriptionBundle-main.js`) that the page and toolbox bundles
   import, so its top level runs in all three frames regardless. **Lesson for the harnesses:** the
   live checks had called `workspaceBundle.handleUndo()` directly and so could not see this;
   `pressUndoButton` now goes through the page frame's `topBarButtonClick`, the real entry point.
2. **Devin's three bugs, all real, all fixed:** a failed undo/redo moved `currentIndex` anyway (now
   transactional — the failing entry stays the next thing to undo, tests for sync and async failure
   both ways); `runUndoable` kept the *first* push inside a scope rather than the outer gesture's own
   entry (now: pushes are held while a scope is open and, when the outermost closes, the first
   depth-1 push wins, else the first inner one — with the discipline that an inner operation records
   inside its own `runUndoable`); and Ctrl+Y in Change Layout mode would have fired origami's redo
   *and* ours once the stack held anything (the binding now stands down when
   `.marginBox.origami-layout-mode` is present, until Stage 4 retires origami's handler). Also a
   dedicated once-only `load` listener records the page id even when the 1500 ms fallback ran first.
   Devin's second round added two navigation races on top of the first fix, both real and fixed:
   the failure rollback now restores by entry identity (a page change during an in-flight async
   undo may have dropped and renumbered entries), and `keepOnly` also filters the pushes held by
   an open `runUndoable` scope.

**Preflight finished (2026-09-07 evening).** Four Devin rounds (one per push), eight distinct findings, all
fixed or answered and resolved on their threads; round four raised nothing new. Full suite 857 green at
`ecff07547`; live checks green on a fresh Bloom through the real button entry point. Report:
https://bloombooks.github.io/dev-process-artifacts/deciders/bloomdesktop-bl-6681-stage1-undostack.html
(linked on the card, with a Stage 1 test-ideas comment). The PR stays draft for John's own review; the
three decisions it asks (merge target now that 6.5 exists; restore the hook-reformatted files; file the
bugs) are in the report. The reader-tools "arming" below was traced to session-state pollution from the
harness's own `SetupElements` re-runs: on a freshly launched Bloom the same check passed 7/7.

**Stage 2b is next**, on `BL-6681-stage2b-undo-delete-canvas-element`, stacked on the Stage 1 tip
(`ecff07547`). When Stage 1 squash-merges, rebase only Stage 2b's own commits onto the target:
`git rebase --onto <target> BL-6681-stage1-undostack BL-6681-stage2b-undo-delete-canvas-element`.

**Observed, not chased — the reader-tools undo arms itself in books without a reader tool.** In "A
house for mouse" (Basic Book, toolbox shows only Canvas/Talking Book/Settings), after this session
had earlier opened a Decodable Reader book and re-run `SetupElements` on this page, typing in a text
box made `toolboxBundle.canUndo()` true, so the Undo button ran the reader-tools undo instead of
CKEditor's — with the Canvas tool active as well as the Talking Book tool. `doMarkup` (which pushes
the model's undo snapshots) is only reachable from the decodable/leveled tools' own keyup handlers,
so those handlers were attached to this page's editables somehow. Pre-existing (the old
`handleUndo` used the same order and the same `toolbox.canUndo()`), and possibly an artefact of the
session's own harness runs, so it needs a repro from a fresh launch before it is filed. Stage 3
removes both mechanisms anyway.

## Next actions

Everything below is pushed; nothing is half-applied, and both branches are green with a clean
working tree. Bloom can be launched from this worktree with the `run-bloom` skill; the live checks in
`docs/retire-ckeditor/liveChecks/` drive it.

### Decisions John needs to make

- **The merge window.** `Version6.5` exists. Does the project now target `master` (6.6)? If so, §5
  could go back to "stage PRs straight to master" — cheaper than the integration branch — and the
  integration branch's first PR could open now. Nothing done pending the answer.
- **Restore the three hook-reformatted master files** in the sync merge (needs one `--no-verify`
  commit)? Or leave the noise.
- **File the bugs found today** — in priority order: (1) the **paste-filter bypass** (BL-12357's
  `cke/id` test admits every paste containing a styled span; tables/iframes/images/divs get in) —
  confirm with a real clipboard first; (2) Ctrl+Z with a reader tool active runs two undos and breaks
  Ctrl+Y; (3) the reader-tools undo restores stale `cke_bm_` bookmark spans; (4) edit key handlers
  accumulate on every `SetupElements` re-run (F6 double-wraps); (5) the dead `data-page-id` check in
  `ImageUndoManager`. All reproduced on this branch; (2)–(5) are untouched by our changes, and (1) is
  in code we have not modified at all.

### Stage 0's remainder — four items, all needing a running Bloom

Do these in one session (`run-bloom` skill). **Not on `BL-6681-stage0-inventory`** — that branch is
under human review, and pushing to it would restart the review for work that is purely additive.
Branch off **`BL-6681-ckeditor`** instead (the files below are new; nothing conflicts).

1. ~~Finish G1~~ (done 2026-09-07, harness passes with the reader tool active). **G2** (async
   markup path / BL-10133 — Talking Book tool, where the prep commit made its one deliberate
   behaviour change) is still unverified: the tool did not mark up the box on typing in the
   2026-09-07 attempt (see that entry), so first work out what makes it mark up, then re-run
   `verifyCaretPreservation.mjs` and confirm `audio-sentence` spans appear alongside the caret check.
   G3 is verified.
2. ~~Capture the paste/drop baseline~~ — done 2026-09-07 with synthetic events; **remaining:** one
   manual confirmation with a real clipboard (copy a web-page table containing coloured text into a
   Bloom box) that the styled-span bypass happens for real pastes too, then file it.
3. ~~Handler-accumulation repro~~ — **reproduced 2026-09-07** (`liveChecks/handlerAccumulation.mjs`,
   1 → 2 → 3 handlers). Remaining: file its card (John's call), and keep that script as the X4
   listener-leak test — it fails today and should pass once §4.10's signal-scoped teardown lands.
4. ~~Page-reload timing baseline~~ — adopt BL-13502's measurements (see the 2026-09-07 entry) once it
   merges; re-run its `benchPageChange.mjs` on our branch only if something looks off.

### Stage 1 — branch `BL-6681-stage1-undostack`, off `BL-6681-ckeditor`

Active, tested (52 tests) and live-verified. What remains:

5. ~~Apply DEFERRED-EDITS 1a–1e~~ — done 2026-09-07. 1f (the cross-frame push) waits for Stage 2's
   first caller by design.
6. ~~Where `clearPageScopedEntries()` hangs off~~ — settled: `switchContentPage`, which every
   page-frame navigation goes through.
7. **PR the branch into `BL-6681-ckeditor`** (or into `master`, if John opens the window — see the
   decisions above) and run `preflight` on it. Then squash-merge and delete the branch (§5.2).

### Stage 2 — after the Stage 1 PR

- **2b (undo delete canvas element) first**, not 2a: it is pure front-end, and 2a's C# citations are
  about to be invalidated by BL-13502. Design 1f (the data-not-closure cross-frame push) with it.
- **2a (undo delete page) after BL-13502 merges**, re-derived from `MergeCurrentPageThenSave`.
- Rename our planned `PageSnapshot` entry kind before Stage 3 (BL-13502 owns that name).

**Standing chores while the branch is long-lived** (§5.3, §5.5):

- **Weekly:** `git checkout BL-6681-ckeditor && git merge origin/master`, then record the master SHA
  and the watchlist-commit count in the sync table at the top of this file.
- **After every sync:** `gh workflow run nightly.yml --ref BL-6681-ckeditor` — otherwise the branch
  gets no full C# suite and no visual-regression coverage at all, for months.
- Set `git config rerere.enabled true` once, so a conflict resolved in one merge is reapplied in the
  next.

Note: launching Bloom uses `./go.sh`. If it fails with missing types like `PodcastUtilities` or
`IDevice` (CS0246), this worktree lacks its C# dependencies — run `./init.sh` (see `AGENTS.md`).

Later, not Stage 0:
- Before designing `clipboard.ts`, read PR #8140 and `origin/BL-16459-clipboard-failure-reporting`.
- Optional, offered but not done: comment on **BL-13502** that undo is another reason to want
  save/reload decoupled.
