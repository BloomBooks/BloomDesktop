# Nightly failures: open issues

**Last updated:** 2026-10-30 (nightlies through the 2026-10-30 run have been triaged)

Known flakes and other unfixed nightly failures: a high-level record of what has been looked at
and where each stands. In-depth findings belong on a card or a branch, not here. Remove an entry
once its fix lands and the nightly has stayed green on it. The rule they are all held to: never
accept a flaky test, and a test is flaky from its first unexplained failure (root `AGENTS.md`,
Testing). So "wait and see whether it recurs" is never an entry's plan. To bring this up to date,
use the `nightly-triage` skill.

## E2E teardown: the test's temp folder cannot be deleted

- **Failed:** 2026-10-12, 2026-10-13 and 2026-10-30. No test failed; the suite failed on "1
  error was not a part of any test".
- **Cause:** the fixture's cleanup (`fixtures/launchBloom.ts` `stop()`) deletes the temp folder
  right after killing Bloom, counting on `fs.rmSync`'s `maxRetries` to wait while Bloom lets go of
  its files. On Node 24 (the repo's `.node-version`) `rmSync` does not retry on EPERM: with a file
  held open by another process it throws at once, with the same message as the nightly. On 10-30
  it threw 0.15 s after the test ended, so no retry happened. The specs involved
  (`collection-settings-advanced`, `toolbox-tools`) both restart or relaunch Bloom just before
  teardown. `removeTempRoot` has the same unused retry, but only logs the failure.
- **Status:** we think [#8515](https://github.com/BloomBooks/BloomDesktop/pull/8515) fixes it:
  teardown now waits until Bloom has exited before deleting the folder. The 12 specs that
  restart or relaunch Bloom passed locally with it; not yet confirmed by a nightly.

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
