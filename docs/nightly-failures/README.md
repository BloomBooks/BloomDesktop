# Nightly failures: open issues

**Last updated:** 2026-11-02 (nightlies through the 2026-11-02 run have been triaged)

Known flakes and other unfixed nightly failures: a high-level record of what has been looked at
and where each stands. In-depth findings belong on a card or a branch, not here. Remove an entry
once its fix lands and the nightly has stayed green on it. The rule they are all held to: never
accept a flaky test, and a test is flaky from its first unexplained failure (root `AGENTS.md`,
Testing). So "wait and see whether it recurs" is never an entry's plan. To bring this up to date,
use the `nightly-triage` skill.

## Canvas element menu: the Flip submenu stays open

- **Failed:** 2026-11-01. `BloomE2E/tests/rotate-and-flip-images.spec.ts` "Change Image gives a
  turned and mirrored picture an upright, unmirrored new one" (Test Case 827): "Moving the pointer
  off the submenu did not close it" (`helpers/canvasElements.ts:836`, two menus still open after
  30 s).
- **Cause:** the Flip submenu was opened by keyboard focus, not by the pointer. The screenshot shows
  the menu with its Flip row highlighted and the submenu open. Bloom's submenus
  (`mui-nested-menu-item` 1.2.6) open on mouse-enter or on focus, and close only on mouse-leave
  or ArrowLeft. The menu can give its first rows focus as it opens, so a submenu opened that way
  never sees a mouse-leave, and `closeCanvasElementMenu` (`helpers/canvasElements.ts`), which
  closes a submenu by moving the pointer off it, cannot close it. `clickCanvasElementMenuItem`
  already notes that a focus-opened submenu stays open wherever the pointer goes. Why focus
  landed on Flip this time is not known; focus events need Bloom's window to have focus, which it
  has on CI but not in a local `--dont-disturb` run.
- **Status:** fix in [#8528](https://github.com/BloomBooks/BloomDesktop/pull/8528): the helper closes a submenu
  with Escape instead.

## Visual regression: staging the BloomPUB preview found no book selected

- **Failed:** 2026-11-02. `BloomVisualRegressionTests/index.spec.ts` "16x9 Landscape
  branding:Default": the test-only `e2e/makeBloomPubPreview` call failed.
- **Cause:** a race with Bloom's startup. This was the run's first case, right after launch. The
  test waits for `e2e/isCollectionReady`, which is true once the collection's books can be listed,
  then selects its book. But Bloom's own startup selection (`WorkspaceView.SelectBookAtStartup`)
  runs later, once the collection's buttons are drawn: it clears the selection, then reloads the
  last-selected book (here the same one) and brings it up to date. The test's
  `e2e/makeBloomPubPreview` came in that window, found no selected book, and threw a
  NullReferenceException in `BloomLibraryPublishModel.InitializeLanguages`. The log shows the book
  being reloaded at the moment of the error. Only a run's first case can lose this race; the
  night before it passed.
- **Status:** fix in [#8528](https://github.com/BloomBooks/BloomDesktop/pull/8528): `isCollectionReady` waits for
  the startup selection to finish.

## Link chooser: the preselected page is not scrolled into view

- **Failed:** 2026-09-23, 2026-09-30, 2026-10-17. Component test `url-sync-preselection.uitest.ts`
  "Scrolls preselected page into view".
- **Cause:** the thumbnail stylesheets arrive after the grid is laid out. They resize the
  thumbnails and push the selected page back out of view, so this is a real (cosmetic) Bloom
  bug.
- **Status:** no card or PR. A possible fix is on branch `link-chooser-wait-for-styles`
  (`3a067400a7`), not yet reviewed: `PageChooser` waits for the styles before drawing the grid,
  with a test that forces the late-styles order. It has not been merged up to date with master.

## Component tests: lost connection to the dev server

- **Failed:** once in September; again 2026-10-27, `bookgridsetup-extended.uitest.ts` "After
  adding a book, the next available book is automatically selected":
  `page.goto: net::ERR_CONNECTION_FAILED at http://127.0.0.1:5183/`.
- **What the trace shows (first look, 10-27):** the first navigation of the test failed about
  15 ms after it started, with ERR_CONNECTION_FAILED rather than a refused or reset connection.
  The log shows no Vite restart, and the other 145 tests passed. A local stress run (the suite 10
  times, 2 workers, 1,460 tests) did not reproduce it. The cause is unknown.
- **Status:** no card or PR. Since 2026-10-27, a failed first navigation also reports a raw TCP
  connect and an HTTP GET to the dev server from Node, and the machine's TCP connections by state
  (`component-tester/describeConnectionFailure.ts`). That says whether the server was reachable
  and whether the runner was short of ports. Background: "A component test lost its connection to
  the dev server" in `src/BloomE2E/AUTOMATION-DEBT.md`.

## C# upload integration tests: the live service was slow

Failed 2026-09-28, 10-15 and 10-27. A bloom-functions fix is in production, not yet measured. See
[DETAILS.md](DETAILS.md#c-upload-integration-tests-the-live-service-was-slow).

## Open questions

- The canvas e2e config has `retries: 1`, which conflicts with the no-flaky-tests rule. Keep it?
