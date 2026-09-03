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

seen again 2026-09-01, and the collection-languages half of it is now fixed: tests set
the collection's languages through the `e2e/setCollectionLanguages` hook, which does
the same work as clicking OK in the Collection Settings dialog, and
`helpers/collection.ts setCollectionLanguages` wraps it with the restart Bloom still
needs (about six seconds, and it loses whatever the editor had not yet saved). No test
composes `.bloomCollection` XML any more. What remains is the dialog itself: nothing
can drive or screenshot the Settings UI, so the journey test for it cannot be written.

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

## Which front end the e2e suite tests depends on what else is running

A launched Bloom serves its React front end either from the built `output/browser` or from a Vite
dev server, and until the fixture is told which, the answer depends on the machine. Three facts,
established 2026-09-01:

- **There is no way to point Bloom at another folder.** `BloomFileLocator.BrowserRoot` computes
  `output/browser` (or `browser`) from where the app sits, with no environment variable and no
  command-line option, so the isolated bundle that `build/agent-vite.ps1` writes under
  `output/agent/<key>/browser` cannot be used by a launched Bloom.
- **A dev server is the supported route, and the fixture now takes it.** Set
  `BLOOM_E2E_VITE_PORT=<n>` and `fixtures/launchBloom.ts` passes `--vite-port <n>`, so the suite
  tests the working tree with no build at all. Start the server with `PORT` set as well as
  `--port`: the port in `vite.config.mts` comes from `process.env.PORT`, so `--port` alone moves
  the server but leaves its HMR and React-Refresh URLs pointing at 5173, and the page then fails
  to load its entry module.
- **Leaving the variable unset does not mean "no dev server".** A dev build probes port 5173 by
  itself (`ReactControl.TryGetActiveViteDevPort`), so a developer's own dev server silently
  decides what the suite tests, and Bloom offers no option that means "ignore any dev server"
  (`--vite-port` rejects 0, and `ValidateStartupVitePort` requires the port to answer).

What remains: the fixture neither starts a dev server of its own nor checks that `output/browser`
is newer than `src/BloomBrowserUI`, so a run with the variable unset can still test a stale
bundle without saying so. Fix direction: have the fixture own the choice, either by starting a
dev server on a port of its own choosing, or by refusing to run against an `output/browser` older
than the source and naming the file that is newer. Bloom needs an explicit "no dev server"
option before the second half of that can be trusted.
(Found 2026-09-01 while fixing the top-bar test ids.)

## The Edit tab's page thumbnail menu has no stable test ids, so tests match on localized text

The items `pageThumbnailList.tsx` renders carry no id, class or `data-testid` (all their
styling is inline), so `src/BloomE2E/helpers/pageThumbnails.ts` has to find "Copy Page" and "Paste Page"
by their English labels, exactly as the top bar does. Same fix: a `data-testid` per command,
taken from the `commandId` the menu already has.
(Found 2026-09-01 while scaffolding src/BloomE2E.)

## One toolbox harness test asserts on classes that do not exist

`react_components/ToolboxRootTestHarness`'s suite has one `test.fixme` because it asserts on
`.subscription-badge` (which only the legacy toolbox has) and `.toolbox-react-header-icon` (which
never existed). Re-enabling it needs a decision on whether the React toolbox header renders
badges and icons at all, and what classes to expose for them. Fix direction: make that decision
as part of the toolbox React refactor (BL-16608 / PR #8109), then rewrite the assertions against
what the header really renders.
(Was part of a larger entry about toolbox registration, whose other half was fixed 2026-09-01 by
extracting `bookEdit/toolbox/registerAllToolboxTools.ts`.)

## One test's tab is the next test's starting state

`fixtures/bloomTest.ts` launches one Bloom per worker, and Playwright gives every test with the
same fixture options that same worker. So a test that ends on the Edit tab makes the next one
start there, and `tests/workspace-tabs.spec.ts` fails its opening sanity check with
`collection: "enabled"` rather than for any reason to do with tabs. `tests/capture-book-page.spec.ts`
switches back to the collection tab at its end to avoid exactly this, which is a convention no
helper enforces and nothing reminds a new test about. Fix direction: reset the workspace in the
fixture's per-test setup, so the tab a test starts on is not a matter of file order.
(Found 2026-09-01, when adding capture-book-page.spec.ts broke workspace-tabs.spec.ts.)

## AI-image-editor selectors are an untested cross-repo contract

`driveAiImageEditor.mjs` drives the `bloom-ai-image-tools` overlay by role/text
selectors copied from that repo's e2e spec; two drifted silently (tool-button text now
includes the description; an Enhance button became ambiguous), each costing a
30-second timeout with no hint the UI changed. The elements that had `data-testid`s
did NOT drift. Fix direction: stable `data-testid`s on tool tiles and category headers
in the editor repo, or have it publish its host-harness selectors for import.
(Promoted from PAPERCUTS 2026-07-30, BL-16603.)

## Driver-level CDP footguns the helper layer does not cover yet

Known WebView2/CDP behaviors that every ad-hoc script rediscovers the hard way. The screenshot
one is now absorbed by `helpers/screenshot.ts` (enlarge the window, clip, clear the override,
and time out every CDP request); these two are not, because they are about the scripts around a
capture rather than the capture itself:

- Never `taskkill //IM node.exe //F` to clean up a hung capture — it kills the go.sh
  vite/dotnet-watch flow and takes Bloom's server down. Kill only the script's own PID.
- Reopening a book re-stamps it with freshly compiled xmatter CSS from `output/`, so
  "before" captures taken after a restart already show the new layout.

(Promoted from PAPERCUTS 2026-07-22; the screenshot item removed 2026-09-01.)

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

Answered for tests, 2026-09-02: `editView/jumpToPage` no longer replies success to a jump
it drops. It refuses the jump and says why, and every helper that changes the page waits for
`waitForEditTabSettled` first. What remains is the Bloom defect itself, in the section "A page
change asked for while the Edit tab is still loading a page can be lost" below.

seen again 2026-09-02 (Test Case ID 72, `derivative-keeps-template-pages.spec.ts`): the
same drop hits `addPage`. Every action that saves the page first goes through
`EditingModel.SaveThen`, whose "not in the right state" branch does nothing and still
replies success, and the state machine is not yet in `Editing` when the page iframe already
shows a `.bloom-page`. Two page adds in a row therefore lost the second one.
`waitForEditablePage` now polls the `e2e/isEditingPage` hook as well as the DOM, so the
helpers no longer act early; the production endpoints still reply success to a request they
dropped.

## Bloom names a dropped language differently on CI and on a developer machine (BL-16806)

`publish-text-languages.spec.ts` used to drop a language by rewriting the `.bloomCollection`,
and it then expected the publish list to show that language's own name for itself, "español".
On CI that assertion failed every time, with `Expected: español  Received: espagnol` — French
for Spanish. Everything else about the row was right.

The cause is known, and BL-16806 is the card that fixes it. Bloom asks LibPalaso for the name of
the dropped language "in" the collection's metadata language, French here. LibPalaso honors such
a request only where it can find a native ICU library, and Bloom ships icu.net but no
`icuuc.dll`, so the answer depends on the machine, not on the run: the CI runner answers
"espagnol", and a developer machine ignores the request and gives the autonym "español". It is
a real difference in what Bloom shows a user, not a test problem.

No test on this branch covers it any more. The test now drops the language through
`e2e/setCollectionLanguages`, the code the Settings dialog's OK button runs, which keeps the
language's collection name, so the list reads "Spanish" on every machine and the lookup that
differs is never reached. Recorded here so the coverage loss is visible; the defect itself
belongs to BL-16806. (Found 2026-09-01, diagnosed on master 2026-09-02.)
## Filling a text box directly leaves part of the old text behind

A `.bloom-editable` is a CKEditor surface, and Playwright's `fill()` on one leaves a
tail of what was there ("Deux" became "eux"), so `typeInGroup` clicks in, selects all,
deletes, and then puts the new text in.

Partly fixed 2026-09-01: the typing half is no longer a key press per character.
`typeInGroup` now inserts the whole string in one call (`keyboard.insertText`), which
CKEditor and Bloom's markup code both handle through the input event it raises, so the
cost of typing no longer grows with the length of the text. What remains is clearing a
box: that still needs a click, Control+A and Delete, because neither `fill()` nor
setting the value leaves CKEditor in a state Bloom then saves correctly. Fix direction:
an `e2e/` hook, or a supported CKEditor path, that sets the text of one box outright.
(Found 2026-09-01 automating Test Case ID 169.)

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

## A Vite dev server only reaches the whole UI on port 5173

`--vite-port` tells Bloom's shell which dev server to load the front end from, but two of the
Edit tab's frames ignore it. `bookEdit/pageThumbnailList/pageThumbnailList.vite-dev.pug` and
`bookEdit/toolbox/toolbox.vite-dev.pug` write `http://localhost:5173/...` into every import
they emit, so on any other port the page list and the toolbox load nothing and come up empty.

That failure looks like the feature being missing, not like a port problem. A run on port 5199
failed `duplicate-page.spec.ts` on 2026-09-02 with "waiting for
getByTestId('duplicate-page-button') to be visible", 30 seconds, because `#PageControls` had
never been filled. Nothing in the message points at the dev server.

The same run showed the second half of it: `BLOOM_E2E_VITE_PORT` was unset, so Bloom fell back
to probing 5173 by itself, found nothing there, and served the built `output/browser` instead.
That bundle was a day old, so the suite silently tested yesterday's front end and reported the
new test id as absent.

So both halves say the same thing: **serve the dev server on 5173 and set
`BLOOM_E2E_VITE_PORT=5173`.** Fix direction: emit the port into those two pug files the way the
shell gets it, so `--vite-port` means what it says; and give Bloom an option that means "ignore
any dev server", so a run can state which front end it is testing rather than inherit it from
the machine. (Found 2026-09-02.)

## Typing in a text box raises no key events

`typeInGroup` puts the whole string in with `keyboard.insertText`, which raises `input`
and nothing else. So no test that types exercises anything in Bloom that listens for
`keydown`, `keypress` or `keyup`, and the `toHaveText` check that follows cannot tell the
difference: the text arrives either way. The pieces of Bloom that watch for a particular
key, rather than for a change to the text, are therefore not covered by any test that
types.

This is a deliberate trade for speed, taken because a key press per character made every
test that fills a book slower in proportion to how much it typed. Fix direction: a helper
that presses one named key in a box, for the tests whose subject is the key press itself
(Enter splitting a paragraph, Tab moving between boxes, a shortcut), and a note in that
helper that `typeInGroup` is not the way to test those. (Found 2026-09-02, during the
review of the headless work.)


## A test can attach to a shell document Bloom does not drive

More than one document in a run carries the workspace root's markup, and therefore the
top bar's `data-testid`, so `fixtures/bloomTest.ts findShellPage` returns whichever the
debugging protocol lists first. When that is not the document Bloom drives, the test is
silently broken rather than failing: its own clicking and typing work, `expect` on what
it typed passes, and every page Bloom loads goes into the document it cannot see. The
symptom is a 60-second wait in `goToPage` for a page Bloom's own log says it showed.
This is why `publish-text-languages.spec.ts` fails perhaps one run in three.

Fixed for tests, 2026-09-01. `e2e/shellUrl` reports the URL of the document Bloom drives,
and `findShellPage` now takes the page whose URL has the same file name (Bloom and the
debugging protocol escape the rest of the URL differently), re-resolving after
`bloomApp.restart`. It falls back to the first page carrying the marker only when the
endpoint never answers, which is what an old `Bloom.exe` in `output/Debug` does, and says
so. `goToPage`'s failure message names both URLs. Also, under `--e2e` every browser built
on the UI thread shares one CoreWebView2Environment, so those documents live in one
browser process with one debugging listener; before that, each environment was given the
same port number and only the first process to start could listen on it. That sharing is
deliberately limited to the UI thread: an environment belongs to the thread that created
it, and handing it to a browser built on the thread serving an API call hangs that thread.
Publishing a BloomPUB does exactly that, and its preview never appeared.

What remains: nobody knows why a run has a second workspace root document at all. Bloom
creates one `_workspaceReactControl`. Worth finding, because the duplicate is what makes
the test-side check necessary.
(Found 2026-09-01 while making `jumpToPage` queue a jump.)

## A page change asked for while the Edit tab is still loading a page can be lost

A page change that reaches the Edit tab between two announcements of one page load wedges
that tab: it stays in SavePending and refuses every later page change. The caller waits 60
seconds for a page that never comes, and a user who clicks a page thumbnail in that window
cannot change pages at all until they leave the tab.

The cause is a page that announces itself to Bloom twice. Bloom's own log, from the run
that found this on 2026-09-02:

```
Navigating(f45a2ef8) --> editing(f45a2ef8)
Editing(f45a2ef8) --> savePending()
Ignoring edit() request while in SavePending(f45a2ef8)
```

The first announcement moves the tab to Editing. The page change then asks the browser for
the page content so it can save the page it is leaving. The second announcement arrives, is
refused because a save is in flight, and the browser never answers the save request. The
tab stays in SavePending. The same three lines in the order that works read `--> editing`,
`Ignoring edit()`, then `--> savePending`, and the save completes.

Any request that changes the page does this, not only a jump. The run that found it lost a
language change from `setContentLanguages`, and the Edit tab then held the tab switch that
followed, so a Publish test failed instead.

A first attempt at this, on 2026-09-02, had `JumpToPage` queue a jump that arrived while
the tab was navigating and release it on the next page-load announcement. That made the
wedge easier to reach rather than harder, because the released jump is itself a page change
arriving in exactly the window above. `JumpToPage` now refuses such a jump and says so, so
the caller can wait and ask again.

Worked around for tests, 2026-09-02: `e2e/editState` reports what the Edit tab is doing and
how many times the page it shows has announced itself, and every helper that changes the
page waits for `waitForEditTabSettled` first. That keeps the request out of the window. The
wait reads the state twice, 1500 ms apart, and needs Editing both times with the count
unchanged: the state alone reads Editing between the two announcements, when a request
would still be lost, and the gap between them has been seen to reach a second. No test can
see any of this from the DOM, because Bloom leaves the previous page in the frame while it
loads the next one.

What remains: the Bloom defect itself, which is older than this suite. A user hits it
whenever something asks for a page change in that window, and the Edit tab then stops
responding to page changes altogether. Fix direction: either stop the browser announcing a
page twice, or make the state machine treat a second announcement of the page it is saving
as a reason to discard that save (`DiscardInFlightSave` already exists for BL-16766). Both
change production save behavior, so this needs a decision rather than a quiet fix.
