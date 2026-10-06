# Nightly failures: open issues

**Last updated:** 2026-10-02 (nightlies through the 2026-10-02 run have been triaged)

Known flakes and other unfixed nightly failures: a high-level record of what has been looked at
and where each stands. In-depth findings belong on a card or a branch, not here. Remove an entry
once its fix lands and the nightly has stayed green on it. The rule they are all held to: never
accept a known-flaky test (root `AGENTS.md`, Testing). To bring this up to date, use the
`nightly-triage` skill.

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
  Confirm the test stays green once that lands.

## Link chooser: the preselected page is not scrolled into view

- **Failed:** 2026-09-23, 2026-09-30. Component test `url-sync-preselection.uitest.ts`
  "Scrolls preselected page into view".
- **Cause:** the thumbnail stylesheets arrive after the grid is laid out. They resize the
  thumbnails and push the selected page back out of view, so this is a real (cosmetic) Bloom
  bug.
- **Status:** the fix (`PageChooser` waits for the styles before drawing the grid) and a test
  that forces the late-styles order are written but uncommitted, on branch
  `link-chooser-wait-for-styles` in the `nightly-investigation` worktree. There is no PR yet.

## Toolbox: the late settings restore races the user (and the tests)

- **Failed:** several nightlies in September in the toolbox and reader-tool specs.
- **Cause:** `restoreToolboxSettingsWhenPageReady` applies settings it fetched before the page
  was ready, so it can undo a change made in the meantime. It also re-selects the saved tool.
- **Status:** seven tests are `test.fixme` (`toolbox-sections.spec.ts`,
  `reader-tool-stage-and-level.spec.ts`). They must be re-enabled before the BL-16608 rework
  finishes.
  - Settings half: [PR #8409](https://github.com/BloomBooks/BloomDesktop/pull/8409), a draft
    left open while we investigate.
  - Tool half: left to the toolbox rework,
    [BL-16608](https://issues.bloomlibrary.org/youtrack/issue/BL-16608)
    ([PR #8109](https://github.com/BloomBooks/BloomDesktop/pull/8109),
    [PR #8427](https://github.com/BloomBooks/BloomDesktop/pull/8427)).
  - The card has a note about the skipped tests.

## Component tests: lost connection to the dev server

- **Failed:** once in September. The cause is unknown.
- **Status:** the run is now instrumented so the next occurrence says more. See "A component
  test lost its connection to the dev server" in `src/BloomE2E/AUTOMATION-DEBT.md`.

## C# upload integration tests: the live service was slow

- **Failed:** 2026-09-28. `BookUploadAndDownloadTests` failed while the live upload service was
  slow, for a few minutes around 10:40 UTC.
- **Status:** the tests now report the uploader's error instead of a bare failure (`d8c050a2ef`).
  Nothing more is planned unless it recurs.

## Open questions

- The canvas e2e config has `retries: 1`, which conflicts with the no-flaky-tests rule. Keep it?
- Notion test-case status for the skipped toolbox tests (830, 441, 442, 460): should they be
  marked Skipped?
