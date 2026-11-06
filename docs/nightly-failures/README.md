# Nightly failures: open issues

**Last updated:** 2026-11-06 (nightlies through the 2026-11-06 run, 42212060682, have been triaged)

Known flakes and other unfixed nightly failures: a high-level record of what has been looked at
and where each stands. In-depth findings belong on a card or a branch, not here. Remove an entry
once its fix lands and the nightly has stayed green on it. The rule they are all held to: never
accept a flaky test, and a test is flaky from its first unexplained failure (root `AGENTS.md`,
Testing). So "wait and see whether it recurs" is never an entry's plan. To bring this up to date,
use the `nightly-triage` skill.

## E2E: a title typed on a new book's cover is lost

- **Cause:** text typed while CKEditor was still loading was overwritten when it finished
  (BL-17022). The e2e helpers used to hide this by waiting for CKEditor before typing.
- **Status:** fixed by [#8542](https://github.com/BloomBooks/BloomDesktop/pull/8542), which also
  drops that wait, so every spec that types now depends on the fix. The PR keeps temporary
  `[cover-title]` / "Cover-title investigation" logging, which says whether a lost title was gone
  from the page or lost after it left. When removing this entry, after several nightlies with no
  lost title, also remove that logging: `attachToCkEditor` in
  `src/BloomBrowserUI/bookEdit/js/bloomEditing.ts`, and the bookTitle report in
  `src/BloomExe/Edit/EditingModel.cs`.

## E2E teardown: EPERM deleting Bloom's temp folder after the collection rename test

- **Failed:** 2026-11-04. All 258 e2e tests passed, but the step failed on "1 error was not a
  part of any test": the worker's teardown after `collection-settings-advanced.spec.ts` (the
  "renaming the collection on the Advanced page renames its folder and reopens it" test, Test
  Case 838) threw `EPERM, Permission denied` from `fs.rmSync` on its
  `%TEMP%\bloom-e2e-*` folder (`fixtures/launchBloom.ts:1187`, from `bloomTest.ts:540`).
- **What the log shows (first look):** `stop()` already waits for Bloom to exit before deleting
  (`8233fa93e`, 10-30), but `killAndWaitForBloomToExit` waits only for the serving pid. This test
  makes Bloom relaunch itself on the renamed collection (`followRelaunch`), so something other
  than the serving Bloom may still have held a file in the folder: a WebView2 child process, or
  another Bloom process. Which process held what is not known; the `bloom-diagnostics` artifact
  has not been looked at.
- **Status:** no card or PR.

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
