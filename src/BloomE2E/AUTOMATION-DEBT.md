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

Work in progress, 2026-09-03 (BL-16799): every entry below that carries a **being fixed**
line is being paid down now, in a stack of small pull requests, one per improvement, each
branching off the one before it. The pull requests do not exist yet, so the branch name is
the identity here. Before you start on a marked entry, ask the owner of its branch.

| Branch | What it pays down |
| --- | --- |
| `BL-16799-automation-scripts` | The `bloom-automation` scripts answer `--help` without killing anything, and reject an unknown flag or a malformed process id. |
| `BL-16799-vr-collect-failures` | A visual-regression case collects every failed comparison and fails once at the end. |
| `BL-16799-component-tests-in-ci` | The component-tester Playwright suites get a nightly job. |
| `BL-16799-vite-port` | `BLOOM_E2E_VITE_PORT` makes a run test the working tree's front end. Adds a new entry for what remains. |
| `BL-16799-type-in-one-call` | Typing in a text box is one insertion, not one key press per character. Adds a new entry: typing now raises no key events. |
| `BL-16799-page-screenshot` | A helper captures a whole book page, which absorbs the `captureBeyondViewport` footgun. |
| `BL-16799-toolbox-registration` | One `registerAllToolboxTools()` that both the bootstrap and the test harness call. |
| `BL-16799-shell-document` | A test can no longer attach to a shell document Bloom does not drive: one WebView2 environment per run, plus the `e2e/shellUrl` hook. |
| `BL-16799-tab-test-ids` | `data-testid` on the workspace tabs, so no test matches a localized label. |
| `BL-16799-page-change` | `editView/jumpToPage` refuses a jump it cannot do, and every page-changing helper waits for the Edit tab to settle. |
| `BL-16799-collection-languages` | The `e2e/setCollectionLanguages` hook, so no test composes `.bloomCollection` XML. |

Three of these also add entries of their own, for the debt that is left after the fix. The
stack replaces PR #8276, which did all of this at once.

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

being fixed on `BL-16799-collection-languages`: the `e2e/setCollectionLanguages` hook
does the work of the Settings dialog's OK button, so no test composes `.bloomCollection`
XML. The dialog itself stays on this entry: nothing can drive or screenshot it.

seen again 2026-09-01 (Test Case ID 349, `duplicate-page.spec.ts`): "Duplicate Page Many
Times..." asks how many copies in `DuplicateManyDialog`, which is `WireUpForWinforms`, so the
one step of that manual test that uses it ("make 3 duplicates") is not automated. The dialog
only posts `editView/duplicatePageMany`, which `duplicateCurrentPage` already calls for setup,
so the fix is the same as for every other WinForms dialog: host it in the web UI.

seen again 2026-09-02 (Test Case ID 66, `xmatter-packs.spec.ts`): the case tries each
front/back matter pack, and the pack is chosen only in the same dialog. `settings/xmatter`
is not an API for it either: its POST only records a pending choice on the open dialog
(`CollectionSettingsApi.UpdatePendingXmatter`). So the test changes the pack the same way,
now through `helpers/collectionSettings.ts` (`restartWithCollectionSettings`), and the
journey of picking a pack in Settings stays untested. Story Producer is worse off: it is
not in the dialog's list at all, only forced by its branding, so the test sets the branding
through the `e2e/setBranding` hook.

## Native OS dialogs hang automation

File pickers, the Image Toolbox, and video capture open native windows Playwright
cannot dismiss; a test that triggers one hangs the run. Tests must avoid them (the
`add-e2e-test` skill forbids it). Fix direction: `--e2e`-mode alternatives via
`E2eTestingApi` for the common cases (choose image file, choose video), so journeys
that need them become automatable.

seen again 2026-09-01 (Test Case ID 349, `duplicate-page.spec.ts`): the manual test puts a
picture, a recording, and a video on a page. The picture has a route: the image chooser is a
web dialog now, and once a file is chosen it posts `imageGallery/imageGalleryResult` and then
applies the answer with the page bundle's `changeImageByElement`, so `helpers/images.ts` does
those two steps for a file the test supplies and never opens the picker. The recording and the
video have none: the Talking Book tool records from a real microphone, and a video arrives only
through the Sign Language tool's native file picker or camera, so those two sections of the
manual test stay manual.

## The Bloom Library login cannot be done for real in a test

Bloom's login state lives in machine-wide settings (`Settings.Default.WebUserId`), which an e2e
Bloom shares with the developer's own Bloom, and signing in goes out to an external browser with
real credentials. So a test can drive neither half of it: posting `account/logout` would sign the
developer out of their own Bloom, and `account/login` would sit waiting for a human. The e2e hook
`e2e/loginState` therefore makes Bloom *report* a login state without touching the real one, which
is enough for the gate the upload screen enforces (Upload is offered only to a signed-in user) but
covers neither the real sign-in and sign-out buttons nor anything that needs a real account —
which is every manual case that uploads for real (#204, #205, #211-#213, #215, #217, #218, #220),
so none of those can be automated either. Fix direction: a test account plus a per-instance login
store (a login the `--e2e` instance keeps to itself), so a run can sign in for real and upload to
dev.bloomlibrary.org without touching the developer's settings. The per-instance half of that is
the same fix "Every Bloom of one build shares one user.config" asks for, below; the test account
is the rest. Note that the pretense changes only what Bloom reports, so Bloom under `--e2e` now
refuses to upload at all rather than let an automated click publish under the developer's real
account.
(Found 2026-09-02 automating Test Case ID 606, `upload-required-items.spec.ts`.)

## Visual-regression cases stop at the first failed comparison

Each case in `src/BloomVisualRegressionTests/index.spec.ts` throws on the first
mismatch, so later comparisons never capture their images; stale baselines surface one
layer per ~3-minute run (BL-16638 took three accept-and-rerun rounds). Fix direction:
accumulate per-comparison failures and fail once at the end — proven during BL-16638
(~20–30 lines, confined to the spec). Loop at `index.spec.ts:426`, assertion at
`index.spec.ts:486` (pre-rewire line numbers). (Promoted from PAPERCUTS 2026-07-30.)

being fixed on `BL-16799-vr-collect-failures`.

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

being fixed on two branches: `BL-16799-shell-document` puts a test id on the top bar and
stops the fixture identifying the shell document by `[role="tablist"]`, and
`BL-16799-tab-test-ids` puts one on each tab and drops the label matching.

seen again 2026-09-01, in the Edit tab's page thumbnail menu: the items
`pageThumbnailList.tsx` renders carry no id, class or `data-testid` (all their styling is
inline), so `src/BloomE2E/helpers/pageThumbnails.ts` has to find "Copy Page" and "Paste Page"
by their English labels, exactly as the top bar does. Same fix: a `data-testid` per command,
taken from the `commandId` the menu already has.

## The component-tester Playwright suites are not in CI

`nightly.yml` runs vitest, C#, visual-regression and BloomE2E; nothing runs
`react_components/component-tester`'s suites, which is how the harness sat broken
(React 17 pin + config bug) unnoticed until it was green again at 144 passed. It will
rot again silently. Fix direction: a nightly job mirroring the visual-regression one
(component config only; the bloom-exe config needs the e2e launch fixture first).
(Promoted from PAPERCUTS 2026-07-27.)

being fixed on `BL-16799-component-tests-in-ci`, as that nightly job. The bloom-exe config
stays out of it, for the reason given above.

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

being fixed on `BL-16799-toolbox-registration`, which extracts
`bookEdit/toolbox/registerAllToolboxTools.ts`. The `test.fixme` half is not fixed there; it
stays as an entry of its own.

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
  being fixed on `BL-16799-page-screenshot`, which absorbs this into
  `helpers/screenshot.ts`. The other two items in this list stay.
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

being fixed on `BL-16799-automation-scripts`, for the `--help`, unknown-flag and
malformed-process-id part. Folding the helpers into the fixture is not part of it.

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

seen again 2026-09-02 (Test Case ID 72, `derivative-keeps-template-pages.spec.ts`): a
fresh template made from Template Starter has no pages of its own, so the hook, which
reported only the book's own template, offered nothing to add. It now reports every
template book the dialog would show, each page tagged with its book's title, through
`PageTemplatesApi.GetTemplateBookPathsForAddPage`. The dialog still parses the HTML
itself; the hook only mirrors which books it is handed.

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

being fixed on `BL-16799-page-change`: it reports that it refused the jump, rather than
queueing it, and the helpers wait for the Edit tab to settle before asking. Queueing was
tried first and made things worse. That branch adds an entry for the Bloom defect behind
this one, which it does not fix.

seen again 2026-09-02 (Test Case ID 72, `derivative-keeps-template-pages.spec.ts`): the
same drop hits `addPage`. Every action that saves the page first goes through
`EditingModel.SaveThen`, whose "not in the right state" branch does nothing and still
replies success, and the state machine is not yet in `Editing` when the page iframe already
shows a `.bloom-page`. Two page adds in a row therefore lost the second one.
`waitForEditablePage` now polls the `e2e/isEditingPage` hook as well as the DOM, so the
helpers no longer act early; the production endpoints still reply success to a request they
dropped.

## Filling a text box directly leaves part of the old text behind

A `.bloom-editable` is a CKEditor surface, and Playwright's `fill()` on one leaves a
tail of what was there ("Deux" became "eux"), so `typeInGroup` clicks in, selects all,
deletes, and types the new text one key at a time. That is closer to what a person does
and it is reliable, but it is also slow for anything longer than a few words, and no
test can currently clear a box by any faster route. Fix direction: understand what
CKEditor does with a programmatic value change; a supported "set the text of this box"
path would let long text be set at once.
(Found 2026-09-01 automating Test Case ID 169.)

being fixed on `BL-16799-type-in-one-call`, for the typing half only: one insertion
instead of a key press per character. Clearing a box still needs Control+A and Delete, and
that branch adds an entry saying that typing now raises no key events.

## The page menu offers commands that silently do nothing while a page is loading

Copy Page and Paste Page go through `EditingModel.SaveThen`, which quietly gives up when the
editing state machine is not in Editing or NoPage (`EditingStateMachine.ToSavePending` returns
false and `CopyPage` passes `() => { }` as its "wrong state" action). The menu does not know
this: `PageThumbnailList.IsContextMenuCommandEnabled` disables commands during SavePending, but
NOT during Navigating, so while a page is still loading both commands look available and both
do nothing at all, with no error and no message. Copy Page itself then saves and reloads the
page, which reopens the same window for the very next click.

Cost, twice over. For a person: click Copy Page and then Paste Page quickly and the paste is
lost with no feedback. For a test: `src/BloomE2E/helpers/pageThumbnails.ts` has to carry
`markEditablePage` / `waitForEditablePageReload`, which stamp the page's document and wait for
Bloom to replace it, purely to know when the model has come back to Editing — the page url
cannot answer it, because Bloom reloads a page to the same in-memory url. Fix direction: make
the enabled test cover the Navigating state too, so a command that cannot run is greyed out;
or, better, queue the command instead of dropping it. Either would let the helper drop the
document-marking dance.
(Found 2026-09-01 while automating Test Case ID 348, copy page preserves everything.)

## Copying a page between two Bloom instances cannot be tested at all

The manual case "Copy Page Preserves Everything" (Test Case ID 348) ends by copying a page from
one running Bloom into a second one. Bloom's page clipboard is a pair of fields on the one
`EditingModel` instance (`_pageDivFromCopyPage`, `_bookPathFromCopyPage`), not the Windows
clipboard, so nothing crosses a process boundary; the feature is known not to work in 6.5. The
e2e fixture is also built around one Bloom per worker, so a test could not stage it today even
if the feature worked. The automated test therefore covers the within-book and between-books
cases only, so the Notion card splits: the cross-instance step belongs on a manual portion
row, per the card-splitting rule in `add-e2e-test`. Fix direction: decide whether cross-instance
copy is a feature we want; if it is, put the page on the real clipboard, and give the launch
fixture a way to run a second instance.
(Found 2026-09-01 while automating Test Case ID 348.)

## Every Bloom of one build shares one user.config, so a run inherits another Bloom's settings

Bloom keeps its user settings (UI language, page zoom, and the rest of `Settings.Default`) in
`%LOCALAPPDATA%\SIL\Bloom\<version>\user.config`, one file per build version, and `--e2e` does
nothing to change that. So the Bloom a test launches starts from whatever the last Bloom of the
same version saved, and saves its own changes for the next one. The e2e lock keeps suites from
running at once, but a developer's own Bloom from a worktree of the same version is outside the
lock and shares the file all the same, and so does the previous run of any suite.

Seen 2026-09-02 (Test Case ID 356, `format-gear-positioning.spec.ts`): two runs found every
factory template named in Turkish, then in French, and failed in `makeBookFromTemplate`, which
matches the English title; a Bloom nobody in the suite had started was running at the time, and
the file said `en` again a moment later. The same test has to restore the zoom it changes, because
that setting is shared too. Fix direction: under `--e2e`, point the settings provider at a
per-instance folder (a sibling of the temp collection would do), so a test's Bloom starts from
defaults and its changes die with it.

## No way to run the suite at a chosen monitor resolution and scale factor

Every run takes the resolution and the scale factor of whatever monitor it lands on, so a
suite proves the layout only at the DPI of the machine that ran it. That is exactly where a
class of Bloom bugs lives: a control that fits at 100% and overlaps at 150%, a dialog that
opens off the edge on a short screen, a size computed in one coordinate space and used in
another. A developer at 150% and a CI runner at 100% each pass while the other's bug goes
unseen, and neither can reproduce what a user reports.

Found 2026-09-03, twice in one change (BL-16804), which is what makes this worth scheduling:

- The off-screen window asked for the primary monitor's working-area size, and Windows
  interpreted that size at the scale factor of the nearest monitor. On a machine with a 150%
  primary and a 100% monitor beside it, a window meant to be 3840x2100 came out 3840x2100 real
  pixels, taller than any monitor on the machine. `format-gear-positioning.spec.ts` failed
  because the page viewport was 1990 CSS pixels high, a size no user has. The same mismatch,
  in its first guise, had eaten all but 27 pixels of a 1000-pixel off-screen cushion.
- Both bugs passed every unit test, because a unit test compares numbers inside one process's
  own coordinate space. Only a real window at a real scale factor shows them.

Fix direction, cheapest first, none of it tried yet:

- **An RDP session to the machine.** An `.rdp` file takes `desktopwidth`, `desktopheight` and
  `desktopscalefactor` (100, 125, 150, 175, 200), so one connection per combination gives a
  real desktop at a chosen scale with no driver to install. This looks like the least work and
  the most likely to run in CI, but nobody has tried driving the suite inside one.
- **A virtual display driver.** Windows has an indirect-display driver model (IddCx), and
  several drivers built on it create a monitor with no hardware behind it, at a resolution the
  driver is told to offer. Setting that monitor's *scale factor* is the harder half: Windows
  exposes per-monitor scale only through display-config calls Microsoft does not document.
  Worth an afternoon of investigation before committing to it.
- **A virtual machine or Windows Sandbox** at a chosen resolution and scale. Heaviest, but it
  is the only one that also isolates the shared `user.config` described above.

Whatever the mechanism, the suite needs the same thing from it: a way to say "run these tests
at 1920x1080 at 150%" and have the run either honour it or refuse, rather than silently using
the desktop it found.

One piece of this is a known limit in the code already, and it is what the fix direction above
would settle. `AutomationWindowPlacement.GetBoundsOffEveryMonitor` puts an off-screen window
directly below the primary monitor, because the nearest monitor is the one whose scale factor
Windows applies, and on the layouts we have that keeps the primary nearest. It stops being true
when a monitor sits *below* the primary in the same band of x: that lower monitor is then nearest,
and if its scale factor differs the window comes out the wrong size, which is the same bug in a
new layout. Fixing it properly means asking Windows for the nearest monitor's scale factor and
scaling the requested size by the ratio, which needs the per-monitor DPI calls this entry is
about. Nobody on the team has such a layout today, which is why it is written down rather than
fixed. (Devin raised it on PR 8285, 2026-09-03.)

## Every run takes the developer's window size, so small-screen bugs go unseen

A run makes its window as big as the monitor it lands on, so the suite proves the layout only at
the size of a developer's screen. Many Bloom users are on inexpensive machines with small screens,
and that is where a class of bugs lives that nobody on the team meets: a control that overlaps
another, a dialog that opens past an edge, a toolbar that quietly drops an item. This is the
window-size half of the DPI entry above, and it is much cheaper to fix, because it needs no
virtual monitor.

The plan: give every automation run a window of **1024x586**, the working area of a 1024x768
screen once a task bar of the usual height is taken off, wherever the window goes.
`BLOOM_AUTOMATION_WINDOW_SIZE=1600x900` asks for a different size, for chasing a bug that only
shows on a big screen. The floor is 400x300, which is `Shell.MinimumSize`; anything Bloom cannot
use, a typo included, gives the default rather than a broken run. The size must be the same for
all three values of `BLOOM_AUTOMATION_MONITOR`, so that variable decides only *where* a window
goes: a suite whose size changed with its placement would let one test pass in one mode and fail
in another, a trap that caught this code twice on BL-16804.

The work is not the window size, which is about thirty lines in `AutomationWindowPlacement.cs` and
`Shell.cs`. The work is the suite going red, which is the point of the change. One full run of the
35 tests at 1024x586 on 2026-09-03 gave **16 passed, 6 failed, 13 did not run**, against 23
passed and 2 failed at the size of a developer's monitor. Two of the six fail at either size, so
they are not the window's doing: Test Case ID 349 (BL-16807) and Test Case ID 606, which times out
after 60 seconds waiting for the publish-to-web steps. The small window is what added these four:

- `copy-page.spec.ts:85` (Test Case ID 348), failed in 8 seconds.
- `derivative-keeps-template-pages.spec.ts:106` (Test Case ID 72), failed after 48 seconds.
- `format-gear-positioning.spec.ts:130` (Test Case ID 356), failed in 335 ms: the Format dialog no
  longer opened close to its gear, while the test above it in the same file passed. So the small
  window moved the dialog.
- `publish-text-languages.spec.ts:416` (Test Case ID 169), failed after 37 seconds. Read this one
  with care: it is BL-16806, which is machine-dependent, and it passed in the full-size run of the
  same build. So the window may have caused it or may not.

Because the suite is serial per file, those 6 failures also stop 13 more tests from running, so
the small window costs 7 passes and hides 13 results until the fixes land.

Each failure then needs triage into one of two piles, and the second pile is the reason to do any
of this: either the test assumed a large window and has to be rewritten, or **Bloom itself
misbehaves at 1024x586**, which is a real user-facing bug and wants its own card. Timeouts rather
than quick failures are the common failure mode, so the suite is also much slower while the fixes
are outstanding. Whoever picks this up has to decide what the nightly workflow does in the
meantime: run small and stay red, or stay large until the tests are fixed.

(Written and measured on 2026-09-03 during BL-16804, then deliberately taken back out: the
developer chose to record the plan here rather than carry a red suite. The code is not in the
history, so rebuilding it from this entry is part of the job.)
