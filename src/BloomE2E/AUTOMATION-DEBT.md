# Automation debt

Bloom automation is a product. When something in Bloom (or in the automation layer
itself) is hard to drive, we spend developer time making it easy to automate — stable
test ids, `E2eTestingApi` hooks, web-UI surfaces instead of WinForms — rather than
piling up workarounds in tests. This file is the visible, schedulable backlog of that
work.

House rules:

- One `##` entry per gap. State the gap, the cost, and the known fix direction.
- When a test hits a listed gap, add a dated `seen again:` line rather than a duplicate.
- When the gap is fixed, delete the entry — this file lists only open debt.
- Ordinary dev/tooling friction goes to `PAPERCUTS.md` at the repo root instead; several
  entries below were promoted from there.

---

## WinForms surfaces are invisible to CDP

The collection Settings dialog and other `WireUpForWinforms` modals (e.g.
`CollectionChooserDialog`) open in their own WebView2 that is not exposed on the main
CDP endpoint, so tests can neither drive nor screenshot them. Today's workarounds:
edit the `.bloomCollection` file before launch, or OS-level capture with a
force-foreground trick. Fix direction: move these surfaces to the web UI (the team
direction anyway), or expose each dialog's WebView2 on a discoverable CDP port.
(Promoted from PAPERCUTS 2026-07-11.)

seen again 2026-09-01 (Test Case ID 169, `publish-text-languages.spec.ts`): the case
turns on which languages the collection has, and there is no API for that either.
`collectionSettings/changeLanguage` is not one: its only listener is the open WinForms
`CollectionSettingsDialog` (`CollectionSettingsDialog.cs:368`), so a POST to it while
the dialog is closed does nothing. The test therefore changes a collection language by
stopping Bloom, rewriting the `.bloomCollection`, and starting again, which is what the
new `bloomApp.restart(betweenStopAndStart)` fixture method is for. Each restart costs
about six seconds and loses whatever the editor had not yet saved.

## Native OS dialogs hang automation

File pickers, the Image Toolbox, and video capture open native windows Playwright
cannot dismiss; a test that triggers one hangs the run. Tests must avoid them (the
`add-e2e-test` skill forbids it). Fix direction: `--e2e`-mode alternatives via
`E2eTestingApi` for the common cases (choose image file, choose video), so journeys
that need them become automatable.

## Visual-regression cases stop at the first failed comparison

Each case in `src/BloomVisualRegressionTests/index.spec.ts` throws on the first
mismatch, so later comparisons never capture their images; stale baselines surface one
layer per ~3-minute run (BL-16638 took three accept-and-rerun rounds). Fix direction:
accumulate per-comparison failures and fail once at the end — proven during BL-16638
(~20–30 lines, confined to the spec). Loop at `index.spec.ts:426`, assertion at
`index.spec.ts:486` (pre-rewire line numbers). (Promoted from PAPERCUTS 2026-07-30.)

## The top bar has no stable test ids, so tests match on localized text

`TopBar.tsx` renders the workspace tabs as `<a role="tab">` with a localized `<Span>`
label and no id, class, or `data-testid`. Two costs, both already paid: the
component-tester's `bloomExeCdp.ts` drives `#main-tabs button`, a selector that exists
nowhere in the source, so `bloom-exe-tabs.uitest.ts` cannot have worked for some time
(it needs a developer's Bloom already running, and nothing runs it in CI — see the entry
below); and `src/BloomE2E/helpers/workspace.ts` has to map tab ids to the English labels
"Collections"/"Edit"/"Publish", so the suite silently only works in an English UI —
which rules out automating the UI-language cases. The same gap makes the fixture
identify Bloom's shell document among the CDP page targets by `[role="tablist"]`, the
only stable marker available. Fix direction: `data-testid="workspace-tab-collection"`
(etc.) on each tab and one on the shell root, and drop the label matching.
(Found 2026-09-01 while scaffolding src/BloomE2E.)

## The component-tester Playwright suites are not in CI

`nightly.yml` runs vitest, C#, and visual-regression only; nothing runs
`react_components/component-tester`'s suites, which is how the harness sat broken
(React 17 pin + config bug) unnoticed until it was green again at 144 passed. It will
rot again silently. Fix direction: a nightly job mirroring the visual-regression one
(component config only; the bloom-exe config needs the e2e launch fixture first).
(Promoted from PAPERCUTS 2026-07-27.)

## Toolbox tool registration is a side effect of toolboxBootstrap

`ToolboxRoot` only renders tools registered via importing `toolboxBootstrap.ts`, which
also renders and clobbers globals, so the test harness duplicates the 11
`ToolBox.registerTool(...)` calls with a "keep in sync" comment. Fix direction: extract
a side-effect-free `registerAllToolboxTools()` both import — probably folded into the
toolbox React refactor (BL-16608 / PR #8109). Related: one harness test is `test.fixme`
because it asserts on `.subscription-badge` (legacy-toolbox-only) and
`.toolbox-react-header-icon` (never existed); re-enabling it needs a decision on whether
the React header renders badges/icons and what classes to expose.
(Promoted from PAPERCUTS 2026-07-27.)

## AI-image-editor selectors are an untested cross-repo contract

`driveAiImageEditor.mjs` drives the `bloom-ai-image-tools` overlay by role/text
selectors copied from that repo's e2e spec; two drifted silently (tool-button text now
includes the description; an Enhance button became ambiguous), each costing a
30-second timeout with no hint the UI changed. The elements that had `data-testid`s
did NOT drift. Fix direction: stable `data-testid`s on tool tiles and category headers
in the editor repo, or have it publish its host-harness selectors for import.
(Promoted from PAPERCUTS 2026-07-30, BL-16603.)

## Driver-level CDP footguns that the automation library must absorb

Known WebView2/CDP behaviors that every ad-hoc script rediscovers the hard way; the
`src/BloomE2E` helper layer should encode them once:

- `Page.captureScreenshot` with `captureBeyondViewport:true` hangs (no response, no
  error). Working pattern: `Emulation.setDeviceMetricsOverride` large enough for the
  whole `.bloom-page`, screenshot with a `clip`, then `clearDeviceMetricsOverride`;
  give every CDP request a timeout.
- Never `taskkill //IM node.exe //F` to clean up a hung capture — it kills the go.sh
  vite/dotnet-watch flow and takes Bloom's server down. Kill only the script's own PID.
- Reopening a book re-stamps it with freshly compiled xmatter CSS from `output/`, so
  "before" captures taken after a restart already show the new layout.

(Promoted from PAPERCUTS 2026-07-22.)

## Visual-regression baselines only match the CI runner

The pixelmatch comparison demands zero differing pixels, and the committed baselines
render exactly only on windows-latest CI. On a developer machine the bloom-player
pages come out 1–884 pixels different (text shifted ~2 px vertically; previews match
exactly), deterministically across runs, exe configs, and bloom-player versions —
leading suspect: locally installed TTF Andika vs the WOFF2 Bloom ships. So a local
run of the suite cannot go green, which makes local verification and baseline
authoring painful. Fix direction: a small per-comparison pixel tolerance, or
machine-profile baselines, or render fonts only from Bloom's own WOFF2 set in --e2e
mode. (Found 2026-09-01 while verifying the bloom-testing-inputs rewire.)

## Automation helper scripts run destructive defaults on unknown flags

`node .github/skills/bloom-automation/killBloomProcess.mjs --help` killed the running
Bloom: unknown flags are ignored and the destructive default runs, so "read the usage
first" is itself the dangerous move; sibling scripts may share the shape. Fix
direction: recognize `--help`/`-h` and reject unknown flags in every script that kills
processes — and fold these helpers' jobs into the library's audited launch/teardown
fixture over time. (Promoted from PAPERCUTS 2026-07-24.)

## Adding a page needs the Add Page dialog, which offers nothing to automate against

A book made from a template starts with front and back matter only, because every page
of a template book is a template page. So almost any test that needs content has to add
a page, and the only production route is the Add Page dialog: `edit/pageControls/addPage`
opens it, and the thumbnails it shows are built by loading the template book's HTML in
the dialog and reading the `.pageLabel` out of each page. Nothing in the API says which
template pages exist or what they are called, so a test either drives that dialog or
hard-codes a page GUID. The `e2e/templatePages` hook added for Test Case ID 169 reports
each template page's id, label, and template book path, which is enough to call the
production `addPage` endpoint. Fix direction: if the page chooser ever gets its list
from C# instead of from the HTML, retire the hook and read that list.
(Found 2026-09-01 automating Test Case ID 169.)

## The Edit tab silently drops a jump to a page while it is loading

`editView/jumpToPage` is the only way to move a test to a particular page, and it is
also how a test saves what it typed, because Bloom writes a page only when the book
leaves it. Coming back from the Publish tab, the Edit tab accepts the POST, replies
success, and shows nothing: the page iframe stays empty until the test asks again. So
`helpers/bookMaking.ts` asks up to three times. Two costs: a test that jumps at the
wrong moment waits 20 seconds per attempt, and a real "this page will not load" bug
would look like the same flake. Fix direction: have `jumpToPage` queue the request
until the Edit tab is ready, or report that it refused it.
(Found 2026-09-01 automating Test Case ID 169.)

## Filling a text box directly leaves part of the old text behind

A `.bloom-editable` is a CKEditor surface, and Playwright's `fill()` on one leaves a
tail of what was there ("Deux" became "eux"), so `typeInGroup` clicks in, selects all,
deletes, and types the new text one key at a time. That is closer to what a person does
and it is reliable, but it is also slow for anything longer than a few words, and no
test can currently clear a box by any faster route. Fix direction: understand what
CKEditor does with a programmatic value change; a supported "set the text of this box"
path would let long text be set at once.
(Found 2026-09-01 automating Test Case ID 169.)
