# Nightly failures: open issues

**Last updated:** 2026-10-21 (nightlies through the 2026-10-21 run have been triaged)

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
- **Status:** no card or PR.

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
- **Status:** the fix (`PageChooser` waits for the styles before drawing the grid) and a test
  that forces the late-styles order are written but uncommitted, on branch
  `link-chooser-wait-for-styles` in the `nightly-investigation` worktree. There is no PR yet.

## Toolbox: the late settings restore races the user (and the tests)

- **Failed:** several nightlies in September in the toolbox and reader-tool specs.
- **Probably seen again (first look):** 2026-10-06, in the evening dispatched run (with #8427),
  `decodable-reader-cancel.spec.ts` "Cancel leaves the saved settings untouched": "Set Up
  Stages" went out of view and then invisible, and the screenshot shows the whole toolbox shut.
  That fits the restore hiding the toolbox again; not confirmed. It passed on 2026-10-07.
- **Cause:** `restoreToolboxSettingsWhenPageReady` applies settings it fetched before the page
  was ready, so it can undo a change made in the meantime. It also re-selects the saved tool.
- **Status:** on master, seven tests are still `test.fixme` (`toolbox-tools.spec.ts`,
  `reader-tool-stage-and-level.spec.ts`).
  - The fix is in the last part of the toolbox rework,
    [PR #8447](https://github.com/BloomBooks/BloomDesktop/pull/8447) (in review,
    [BL-16608](https://issues.bloomlibrary.org/youtrack/issue/BL-16608)). Each late restore
    (the toolbox's own and `readerToolsModel.restoreState()`) now applies a saved setting only
    if it has not changed since it was read. That branch re-enables the seven tests.
  - [PR #8409](https://github.com/BloomBooks/BloomDesktop/pull/8409) was closed unmerged: it
    measured worse than no fix.
  - The card has a note about the skipped tests.

## Component tests: lost connection to the dev server

- **Failed:** once in September. The cause is unknown.
- **Status:** the run is now instrumented so the next occurrence says more. See "A component
  test lost its connection to the dev server" in `src/BloomE2E/AUTOMATION-DEBT.md`.

## C# upload integration tests: the live service was slow

- **Failed:** 2026-09-28. `BookUploadAndDownloadTests` failed while the live upload service was
  slow, for a few minutes around 10:40 UTC.
- **Seen again:** 2026-10-15, `DownloadUrl_NoCollectionSettings_GetsDocument_AndSettings`: "Unable
  to initiate book upload on the server."
- **Cause:** outside Bloom: the live service.
- **Status:** the tests now report the uploader's error instead of a bare failure (`d8c050a2ef`).
  Open question: should these tests depend on the live service at all?

## Open questions

- The canvas e2e config has `retries: 1`, which conflicts with the no-flaky-tests rule. Keep it?
- Notion test-case status for the skipped toolbox tests (830, 441, 442, 460): should they be
  marked Skipped?
