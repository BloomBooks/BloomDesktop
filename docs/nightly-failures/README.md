# Nightly failures: open issues

**Last updated:** 2026-10-19 (nightlies through the 2026-10-19 run have been triaged)

Known flakes and other unfixed nightly failures: a high-level record of what has been looked at
and where each stands. In-depth findings belong on a card or a branch, not here. Remove an entry
once its fix lands and the nightly has stayed green on it. The rule they are all held to: never
accept a flaky test, and a test is flaky from its first unexplained failure (root `AGENTS.md`,
Testing). So "wait and see whether it recurs" is never an entry's plan. To bring this up to date,
use the `nightly-triage` skill.

## Rotate and flip: four rotations leave the background one pixel off

- **Failed:** every night from 2026-10-15 to 2026-10-19.
  `BloomE2E/tests/rotate-and-flip-images.spec.ts:322` "Rotate right rotates the page's background
  picture inside its area, … and four rotations put it back" (Test Case 827): "After four
  rotations the background's left is 0, not -1." The file runs in serial mode, so the tests after
  it are skipped, which is why the run shows 15 skipped instead of 7.
- **What the trace shows (first look):** it passed on 2026-10-13 (`51e1cb6663`), the 10-14 run
  never reached the e2e suite, and it has failed every night since 2026-10-15 (`73c05442b6`). Two
  PRs landed in between: [#8459](https://github.com/BloomBooks/BloomDesktop/pull/8459) (BL-17009,
  stop white lines along the edges of full-page pictures) and #8387 (BL-16900, one undo stack).
  #8459 changes where a full-page picture's edges sit, which fits a one-pixel difference, but that
  is not confirmed.
- **Status:** no card or PR for the failure.

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

## Toolbox: the late settings restore raced the user (and the tests)

- **Failed:** several nightlies in September in the toolbox and reader-tool specs.
- **Probably seen again:** 2026-10-06, in the evening dispatched run (with #8427),
  `decodable-reader-cancel.spec.ts` "Cancel leaves the saved settings untouched": "Set Up
  Stages" went out of view and then invisible, and the screenshot shows the whole toolbox shut.
  That fits the restore hiding the toolbox again. It passed on 2026-10-07. Never confirmed, but
  it is the same symptom in a third spec file, so it is worth re-checking if it recurs after the
  fix below lands.
- **Cause:** two late restores, each applying settings read before the page was ready and so
  undoing whatever had happened since.
  - `restoreToolboxSettingsWhenPageReady` re-applied the saved toolbox visibility and the
    saved tool, so a toolbox just opened was shut again and a tool just opened closed back.
  - `readerToolsModel.restoreState()` seeded the default stage and level over the top of a
    choice already made. Despite the name it restores nothing: its `DRTState` is all
    defaults, and the book's real stage comes from the tool's `beginRestoreSettings`. It
    also passes `skipSave`, so it left nothing recorded -- the stage sprang back to 1 and
    Bloom never learned the user had chosen 2. This is why the two stage tests were the
    flakiest of the four.
- **Status:** fixed, and all seven tests are back on (`toolbox-tools.spec.ts`,
  `reader-tool-stage-and-level.spec.ts`; Test Case IDs 830, 441, 442, 460). Each late restore
  now applies a saved setting only if it has not changed since it was read.
  - Landed with the last part of the toolbox rework,
    [PR #8447](https://github.com/BloomBooks/BloomDesktop/pull/8447),
    [BL-16608](https://issues.bloomlibrary.org/youtrack/issue/BL-16608).
  - A third cause turned up later and was the one behind the flakiest two tests: the reader
    tools discarded a book's saved stage or level outright whenever the Synphony settings had
    not loaded, so a slow or failed load left the tool on 1 and, because the restore passes
    `skipSave`, recorded nothing either. It is now held until the settings arrive, and dropped
    when a restore for another book begins so it cannot cross between books.
  - That one is pinned by `readerToolRestore.spec.ts`, which reproduces it deterministically
    and fails on the unfixed code with the flake's own signature. Both spec files pass at the
    nightly window size, but a clean run of them never proved much at the rate the flake
    appeared, so **watch the first nightlies** rather than treating this as proved --
    including `decodable-reader-cancel`.
  - [PR #8409](https://github.com/BloomBooks/BloomDesktop/pull/8409) was closed unmerged: it
    re-read the settings after the wait, which is not reliable because every change is saved
    with a fire-and-forget post, so a later read may not see one that has just happened.
    Measured, it was worse than no fix at all.

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
