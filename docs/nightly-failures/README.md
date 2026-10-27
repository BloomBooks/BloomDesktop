# Nightly failures: open issues

**Last updated:** 2026-10-27 (nightlies through the 2026-10-27 run have been triaged)

Known flakes and other unfixed nightly failures: a high-level record of what has been looked at
and where each stands. In-depth findings belong on a card or a branch, not here. Remove an entry
once its fix lands and the nightly has stayed green on it. The rule they are all held to: never
accept a flaky test, and a test is flaky from its first unexplained failure (root `AGENTS.md`,
Testing). So "wait and see whether it recurs" is never an entry's plan. To bring this up to date,
use the `nightly-triage` skill.

## Bulk upload test: two different failures against dev.bloomlibrary.org

- **Failed:** 2026-10-11 and 2026-10-15. `BloomE2E/tests/bulk-upload-quick-test.spec.ts` "bulk
  upload: refused without a bookshelf, then new / skipped / updated / moved to another bookshelf"
  (Test Case 211).
- **What the trace shows (first look):** on 10-11, the upload-agreement checkbox was still
  unchecked 15 s after it was clicked. On 10-15, moving the collection to another bookshelf did
  not update all four books. The 10-15 run's C# upload test also failed against the same service
  that morning (see the C# upload entry), so the service may have been having trouble; whether
  that explains either symptom is not known.
- **Status:** [#8405](https://github.com/BloomBooks/BloomDesktop/pull/8405)
  ([BL-16922](https://issues.bloomlibrary.org/youtrack/issue/BL-16922)), merged 2026-10-23, makes the
  test check the bookshelf in what Bloom uploaded rather than in the harvested record, which
  addresses the 10-15 symptom. The test has passed every night since. The 10-11 symptom is not
  explained.

## E2E teardown: the test's temp folder cannot be deleted

- **Failed:** 2026-10-12 and 2026-10-13. No test failed; the suite failed on "1 error was not a
  part of any test".
- **What the trace shows (first look):** after a spec finished, the fixture's cleanup
  (`fixtures/launchBloom.ts:1111`) failed with "EPERM, Permission denied" deleting the test's
  `bloom-e2e-*` temp folder, after a different spec each night
  (`collection-settings-advanced`, `toolbox-tools`). The code comment there already expects
  Bloom to let go of files slightly after it dies, and retries; here the retries ran out. What
  still held the folder is not known.
- **Status:** no card or PR.

## Nightly setup: a setup step fails, and the rest of the run is skipped

- **Failed:** 2026-10-10 and 2026-10-14. Both nights the component-tester suite also reported
  "no results file": it runs even when setup fails, and without `pnpm install` it cannot resolve
  its imports (`axios`, `@emotion/react`).
- **Cause:** two different setup failures.
  - 10-10: `src/BloomE2E/scripts/install-test-fonts.ps1` installed Alef and Luciole, but GDI+ then
    did not list them, and the script stops the run when that happens. Why GDI+ missed them is not
    known.
  - 10-14: build.palaso.org (TeamCity) returned HTTP 502 for `ghostscript-win32.zip`, so getting
    the binary dependencies failed. That is outside Bloom.
- **Status:** no card or PR.

## Publish: the Text Languages list is missing after a restart

- **Failed:** 2026-10-07. `BloomE2E/tests/publish-text-languages.spec.ts` "keeps the choice a
  person makes, across screens and across a restart" (Test Case 169): the Spanish row's
  `checked` was `undefined`, not `false`, so the row was not found at all.
- **What the trace shows (first look):** on the BloomPUB screen after the restart, the right-hand
  panel shows Features and Help but no text-languages list. Whether the list was slow to appear
  or never came is not known. It is the test's only failure so far; #8388 (settings shell,
  BL-16902) landed the day before, and nobody has checked whether it is involved.
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

- **Failed:** 2026-09-28. `BookUploadAndDownloadTests` failed while the live upload service was
  slow, for a few minutes around 10:40 UTC.
- **Seen again:** 2026-10-15, `DownloadUrl_NoCollectionSettings_GetsDocument_AndSettings`: "Unable
  to initiate book upload on the server."
- **Seen again:** 2026-10-27, the same message in three tests:
  `DownloadUrl_NoCollectionSettings_GetsDocument_AndSettings`,
  `UploadBook_FillsInMetaData_AndDownloadUrl_GetsDocumentAndSettings`,
  `UploadBook_SameId_ReplacesFilesAndSetsParseFieldsCorrectly`.
- **Cause:** outside Bloom: the live service. On 10-27 Application Insights shows bloom-functions
  degraded across functions between 09:38 and 09:50 UTC. `upload-start` calls hung inside the
  `books` function for about 2 minutes, until the client gave up (HTTP 499); `stats` and `status`
  calls failed the same way. The tests' own requests were among them. Every new bloom-functions
  instance holds its requests for about 3 minutes while it loads its code from the file share;
  that morning many requests landed on new instances. Times, Application Insights findings and
  queries are in [DETAILS.md](DETAILS.md#c-upload-integration-tests-the-live-service-was-slow).
- **Status:** the tests now report the uploader's error instead of a bare failure (`d8c050a2ef`),
  and, since 2026-10-27, each API call's status and error from the client's log (the nightly runs
  them against the live service on purpose).
  [BL-17051](https://issues.bloomlibrary.org/youtrack/issue/BL-17051), in progress, makes Bloom
  tell users the service isn't responding instead of suggesting they restart Bloom.

## Open questions

- The canvas e2e config has `retries: 1`, which conflicts with the no-flaky-tests rule. Keep it?
