# Nightly failures: open issues

**Last updated:** 2026-10-07 (nightlies through the 2026-10-07 run have been triaged)

Known flakes and other unfixed nightly failures: a high-level record of what has been looked at
and where each stands. In-depth findings belong on a card or a branch, not here. Remove an entry
once its fix lands and the nightly has stayed green on it. The rule they are all held to: never
accept a flaky test, and a test is flaky from its first unexplained failure (root `AGENTS.md`,
Testing). So "wait and see whether it recurs" is never an entry's plan. To bring this up to date,
use the `nightly-triage` skill.

## Reader setup: the Levels dialog sometimes never opens

- **Failed:** 2026-10-02. `BloomE2E/tests/reader-setup-dialogs-coexist.spec.ts`. After
  "Set Up Levels" is clicked, `#settings_frame` never appears (30 s timeout).
- **Cause:** a real Bloom bug (the button does nothing for users too), reproduced locally in
  about 40% of runs. The legacy jQuery accordion still runs on the hidden `#toolbox`. When it
  refreshes while a tool body is still sitting there, it takes that body for a header and binds
  its header click handler, which calls `preventDefault`. The body then moves into the React
  toolbox with the handler still attached, so the "Set Up Levels" `javascript:` link never runs.
  This is a second cause of the BL-16732 symptom, and is about as old as the React toolbox.
- **Status:** not fixed separately. The toolbox rework removes the jQuery accordion entirely
  ([PR #8427](https://github.com/BloomBooks/BloomDesktop/pull/8427),
  [BL-16608](https://issues.bloomlibrary.org/youtrack/issue/BL-16608)), which should fix it.
  #8427 merged on 2026-10-06, after that night's run. The test passed in the two runs since
  (the 2026-10-06 evening dispatch and 2026-10-07). Remove this entry once the nightlies have
  stayed green on it for a while.

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

- **Failed:** 2026-09-23, 2026-09-30. Component test `url-sync-preselection.uitest.ts`
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
  - Measured locally against the Vite dev server, five runs of every test in each spec file:
    3 of 15 failed before the fix, 0 of 40 after, and both files pass at the nightly window
    size. The dev server makes these tests fail more often than CI does, so **watch the first
    nightlies** rather than treating this as proved -- including `decodable-reader-cancel`.
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
- **Cause:** outside Bloom: the live service.
- **Status:** the tests now report the uploader's error instead of a bare failure (`d8c050a2ef`).
  Open question: should these tests depend on the live service at all?

## Open questions

- The canvas e2e config has `retries: 1`, which conflicts with the no-flaky-tests rule. Keep it?
