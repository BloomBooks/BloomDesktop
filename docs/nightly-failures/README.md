# Nightly failures: open issues

**Last updated:** 2026-10-06 (nightlies through the 2026-10-06 run have been triaged)

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
  #8427 merged on 2026-10-06, after that night's run. Remove this entry once the nightlies have
  stayed green on it for a while.

## BookGridSetup component tests: the component fails to load

- **Failed:** 2026-10-06. All 21 BookGridSetup component tests (`bookgridsetup-basic` and
  `-extended.uitest.ts`); every other component passed.
- **What the trace shows (first look):** the harness could not load the component: "Failed to
  load module ../BookGridSetup/BookGridSetup: styled_default is not a function", from one of
  Vite's pre-bundled dependency chunks. BookGridSetup has not changed since September, and the
  only commit since the green 10-05 run (#8227, rotate images) does not touch it. Vite's
  "Failed to resolve dependency: @mui/styled-engine, present in optimizeDeps.include" warning
  shows in the green 10-05 run too, so it does not explain this on its own.
- **Local run (2026-10-06, current master, cold Vite cache as on CI):** all 21 fail with the
  same error, so this is a real break, not CI's environment. No package, lockfile or Vite config
  changed since the green run. The suspect is #8227's new `@mui/icons-material/Flip` and
  `RotateRight` imports, which change what Vite pre-bundles. Confirmed: those CommonJS icon
  files make esbuild initialize `styled` lazily, and Popper's chunk calls it first.
- **Status:** fix in [PR #8445](https://github.com/BloomBooks/BloomDesktop/pull/8445): the
  tester loads icons from MUI's ESM build.

## Rotate and flip pictures: dragging the speech bubble onto the canvas adds nothing

- **Failed:** 2026-10-06, its first nightly. `BloomE2E/tests/rotate-and-flip-images.spec.ts`
  "builds a book with a background picture page and a page of overlay items": after the speech
  palette item is dragged onto the canvas, the element count stays at 3 (30 s). The file runs in
  serial mode, so its other 16 tests were skipped.
- **What the trace shows (first look):** the overlay picture and the text box had already been
  added, and the text box was still selected when the speech bubble was dropped. Whether the
  drop missed, or Bloom ignored it, is not known.
- **Local runs (2026-10-06, current master):** the failing test passed 3 times out of 3, and
  every other test that ran passed too, so it does not reproduce here. It may be a race that
  only CI's slower machine hits.
- **Cause:** CI's window is about 1008x681, so the drop point (75% down an A5 page) was off
  screen, where `elementsFromPoint` finds nothing. A developer's monitor shows the whole page.
- **Status:** the test came in with [PR #8227](https://github.com/BloomBooks/BloomDesktop/pull/8227)
  (BL-16741). Fix in [PR #8445](https://github.com/BloomBooks/BloomDesktop/pull/8445), which also
  fixes two later tests in the spec that fail at CI's window size.

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
  `reader-tool-stage-and-level.spec.ts`; Test Case IDs 830, 441, 442, 460). Each restore now
  leaves alone what was decided after it read its settings.
  - Landed with the toolbox rework,
    [BL-16608](https://issues.bloomlibrary.org/youtrack/issue/BL-16608).
  - Measured locally against the Vite dev server, five runs of every test in each spec file:
    3 of 15 failed before the fix, 0 of 40 after. The dev server makes these tests fail more
    often than CI does, so **watch the first nightlies** rather than treating this as proved.
  - [PR #8409](https://github.com/BloomBooks/BloomDesktop/pull/8409) (a draft that re-read the
    settings after the wait) is superseded: re-reading is not reliable, because every change
    is saved with a fire-and-forget post, so a later read may not see one that has just
    happened. Measured, it was worse than no fix at all. Close it.

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
