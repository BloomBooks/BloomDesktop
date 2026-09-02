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
## One test's tab is the next test's starting state

`fixtures/bloomTest.ts` launches one Bloom per worker, and Playwright gives every test with the
same fixture options that same worker. So a test that ends on the Edit tab makes the next one
start there, and `tests/workspace-tabs.spec.ts` fails its opening sanity check with
`collection: "enabled"` rather than for any reason to do with tabs. `tests/capture-book-page.spec.ts`
switches back to the collection tab at its end to avoid exactly this, which is a convention no
helper enforces and nothing reminds a new test about. Fix direction: reset the workspace in the
fixture's per-test setup, so the tab a test starts on is not a matter of file order.
(Found 2026-09-01, when adding capture-book-page.spec.ts broke workspace-tabs.spec.ts.)

## One toolbox harness test asserts on classes that do not exist

`react_components/ToolboxRootTestHarness`'s suite has one `test.fixme` because it asserts on
`.subscription-badge` (which only the legacy toolbox has) and `.toolbox-react-header-icon` (which
never existed). Re-enabling it needs a decision on whether the React toolbox header renders
badges and icons at all, and what classes to expose for them. Fix direction: make that decision
as part of the toolbox React refactor (BL-16608 / PR #8109), then rewrite the assertions against
what the header really renders.
(Was part of a larger entry about toolbox registration, whose other half was fixed 2026-09-01 by
extracting `bookEdit/toolbox/registerAllToolboxTools.ts`.)

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

## Bloom sometimes names a dropped language in another language of the collection

`publish-text-languages.spec.ts` used to drop a language by rewriting the `.bloomCollection`,
and it then expected the publish list to show that language's own name for itself, "español".
That assertion failed about one run in seven, once on CI run 33665790357 (2026-09-02), with
`Expected: español  Received: espagnol` — French for Spanish. The collection at that moment
holds en + fr, so Bloom was naming the dropped language in the collection's own French on some
runs and by its autonym on others. Everything else about the row was right every time.

No test covers this any more. The test now drops the language through
`e2e/setCollectionLanguages`, the code the Settings dialog's OK button runs, which keeps the
language's collection name, so the list reads "Spanish" every time and the lookup that wavers is
never reached. Fix direction: find why the name lookup resolves against a different language
from one run to the next; it is a real nondeterminism in what a user sees. (Found 2026-09-01,
recorded here 2026-09-02 when the test stopped exercising it.)

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
