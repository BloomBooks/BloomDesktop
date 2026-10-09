# BL-13502 handoff: saving a page without reloading it

For an agent continuing PR #8209 (branch `BL-13502-save-without-reload`, base `master`) on another
machine, most likely to answer a human reviewer. Everything here is either not in the code or easy
to miss there. Delete this folder before the PR merges.

## Where things are

- **The PR:** https://github.com/BloomBooks/BloomDesktop/pull/8209. Its description (between the
  `preflight-narrative` markers) is the current summary of what the PR does, why, the risks, e2e
  coverage and Notion cards. Keep it current if the PR changes.
- **The card:** https://issues.bloomlibrary.org/youtrack/issue/BL-13502. It carries the tester's
  notes (a comment marked `test-ideas`).
- **The design:** `src/BloomExe/Edit/SavingWithoutReloading.md`. Read this first.
- **The preflight report** (for John, with his decisions so far):
  https://bloombooks.github.io/dev-process-artifacts/deciders/BloomDesktop-BL-13502-save-without-reload.html,
  in the `BloomBooks/dev-process-artifacts` repo at
  `deciders/BloomDesktop-BL-13502-save-without-reload.html`.
- **The e2e plan** this PR's tests came from: a PR comment,
  https://github.com/BloomBooks/BloomDesktop/pull/8209#issuecomment-6025653325.
- **Devin:** https://devinreview.com/BloomBooks/BloomDesktop/pull/8209. Every finding has been
  mirrored to the PR and answered; nothing is open.

## Decisions John has made (don't reopen them without a reason)

- **Every save uses the snapshot the browser volunteers.** No request carries the page. A
  snapshot means the page is idle unless it carries `stillBusyWith`.
- **No retries.** A failed snapshot post is reported to the user, once per page, and is not
  retried: "Failure should be a crash."
- **Only the `.bloom-page` element is gathered and sent.** C# wraps it in a trivial body. Nothing
  reads the body element's attributes.
- **A click, command or exit within about 25 ms of a keystroke** saves the snapshot from before
  that keystroke. This is accepted: nobody can click that fast.
- **The wait for a busy page** is a plain sleep with a two-second cap (`PageSnapshot.WaitUntilIdle`).
  The log names the work that was still running if the cap is reached.
- **Attribute order is ignored** when deciding whether a page changed
  (`SafeXmlNode.GetXmlIgnoringAttributeOrder`). It is a generic XML helper, which is why it lives
  in `SafeXmlNode`, not `HtmlDom`.
- **Optional branding images that are missing get no "missing image" alt text**
  (`isBrandingImage` in `bloomImages.ts`). That text was being saved into book-wide data.
- **Table editing markup is stripped from the saved copy by Bloom's own copy of bloom-table's
  list** (`removeTableEditingMarkupFromClone` in `tableEditing.ts`). bloom-table's
  `removeTableEditingArtifacts` also ends Paint Format and Border Brush mode, which a save that runs
  on every change must not do, and it doesn't export its markup-only part. A unit test compares the
  two. If bloom-table ever exports that part, call it instead.
- **Any request that makes C# save the page and then reload it** must first send the page:
  `saveChangesAndRethinkPage` or `postAfterSendingSnapshot` in `pageSnapshot.ts`. A feature merged
  from master that posts such a request directly will lose the last moments of typing. The
  original-copyright sentence's unlock and relock needed this when master was merged on 10-09.
- **The flaky test fix is in this PR.** `Xml_DoesNotProvide_ThreadSafety` was flaky on master;
  John asked for the fix to go in here.
- **Left alone on purpose:** bloom-player's `prepareActivity` leaves `touch-action: none` on
  draggables, so playing a game page and then saving writes it into the book. It predates this
  PR, and the tester's notes say not to report it.
- **Comments say why the code is as it is now.** No history, except where old books can still
  contain the older DOM.
- **Fixed waits in e2e tests** are allowed only for a specific, known interval. The one in use is
  `waitForPageToSettle`, which waits for the overflow checker's 1000 ms timer and then for the
  page to have nothing left to send.

## Running things on this branch

- **C#:** build and test through `build/agent-dotnet.sh`, never bare `dotnet`. It builds into an
  isolated folder.
- **Bloom:** start it through the run-bloom skill. The launcher script is
  `.claude/skills/run-bloom/launcherControl.mjs`, not `.github/skills/...`.
- **E2E tests (`src/BloomE2E`)** run the `Bloom.exe` in `output/Debug`, not the isolated build. So:
  1. Rebuild with `launcherControl.mjs --restart --wait-ready`.
  2. Quit that Bloom with `launcherControl.mjs --quit-bloom`; it blocks a second Bloom. Vite stays
     up.
  3. Run `node build/get-testing-inputs.mjs` once.
  4. Run the specs with `BLOOM_E2E_VITE_PORT=<the launcher's current vitePort>` and
     `BLOOM_AUTOMATION_MONITOR=headless`:
     `pnpm exec playwright test tests/<spec> --reporter=line`.

  The fixture refuses a build older than any C# source.
  After merging master, also run `pnpm run build:pageSizes` in `src/content`. It writes
  `output/browser/pageSizesLookup.json`, which C# needs (about 30 C# tests fail without it). The
  full front-end build would make it too, but agents must not run that. The commit hook reformats C# files
  (csharpier), which can make the build look stale after a commit: rebuild, then rerun.
- **The new specs:**
  - `typing-survives-leaving-the-page`
  - `saved-book-stays-clean`
  - `change-layout-keeps-typing`
  - `duplicate-page-many-times`

  They have 16 tests, all passing.
- **The older specs that touch this code:**
  - `duplicate-page`
  - `copy-page`
  - `workspace-tabs`
  - `toolbox-tools`
  - `talking-book-paste`
  - `rotate-and-flip-images`
  - `capture-book-page`

  All 28 of their tests pass.
- **E2E tests that type and then leave the page** need `waitForBloomToHaveTyping` in between, as
  a person's pause would provide. Master's specs, written before this PR, may not have it; on
  10-09, `original-copyright-sentence.spec.ts` needed it.
- **Quitting Bloom from a test:** `quitAndRestart` in the fixture posts `WM_CLOSE` to Bloom's
  visible windows. In `--dont-disturb` mode the main window has a hidden owner, so `taskkill`
  without `/F` and `CloseMainWindow` both miss it.
- **CI:** the only PR workflow is `pr-automation`, which just starts Devin. On 2026-10-09 GitHub
  started no run for the pushes that day. If Devin has no job for the head commit, start it by
  loading the review page (see the `devin-review` skill).

## Notion test cards

Database "Test Case Runs", `38c4bb19-df12-8123-8bc8-e65b962cb12f`. Token: the
`BLOOM_TESTCASE_NOTION` user environment variable, the secret of the internal integration
`test-management-access`. Use the REST API directly (Notion-Version `2022-06-28`).

The cards this PR automates are all set to Automation = `PR Pending`:

- #659 Quit Without Losing Changes
- #663 Clean Saved Book HTML
- #841 Typing Survives Leaving the Page
- #842 Looking at Pages Does Not Rewrite the Book
- #843 Change Layout Keeps Typing
- #844 Duplicate Page Many Times

When the PR merges, set them to `Automated`. #366 notes that #843 covers part of it. #810's Many
Times step moved to #844. Ask John before creating or changing cards.

## Conventions for posting

- **Attribution:** comments and replies posted under John's account start with a model tag, e.g.
  `[Claude Opus 5.5 from John Thomson's machine]`.
- **Devin:** never post an `@devin` mention.
- **Human reviewers:** a fix you make at a reviewer's suggestion gets a short reply saying what
  changed, then the thread is resolved. Disagreeing with a human goes to John first.
- **Commits** end with the `Co-Authored-By` line for the model in use.
