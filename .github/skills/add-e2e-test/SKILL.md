---
name: add-e2e-test
description: How to add a new edge-to-edge (e2e) test that drives the real Bloom.exe through its UI, from writing the test to a PR that is ready to merge, with a Devin review whenever Bloom production code changes, and a short closing summary. Use when asked to automate a manual test, add an e2e test, add a journey test, or extend the nightly e2e suite.
---

# Add an edge-to-edge test

## What an e2e test is here

An e2e test launches its own `Bloom.exe` on a known collection, drives the real UI in
the embedded WebView2, asserts, and tears the instance down. You can run a single test
locally; wiring the suite into the nightly workflow (`.github/workflows/nightly.yml`)
is planned but not done yet — the Running section below tells the current truth.

Test code lives in BloomDesktop (`src/BloomE2E/`), versioned with the product code.
Test input collections live in https://github.com/BloomBooks/bloom-testing-inputs,
pinned by `build/testing-inputs.pin` and fetched to `output/testing-inputs/` by
`node build/get-testing-inputs.mjs`.

## Build Bloom whenever it helps; the usual rules are suspended here

The repo's guidance for agents says not to build Bloom: use the `agent-dotnet` wrapper, never
run `pnpm build`, let the dev server push front-end edits into the developer's running Bloom.
Those rules protect a Bloom the developer is running from this worktree. **They do not apply
while you follow this skill.** The suite launches its own `Bloom.exe` from `output/Debug/` (or
`output/Release/`), with `--e2e --automation`, on a temp copy of a collection, and that instance
runs alongside whatever Bloom the developer has open. Developing a test does not disturb them.
The Bloom you launch is also not the one the dev server feeds: it reads the front end from
`output/browser` and the C# from the built exe. So a test sees a change to Bloom only after a
build, and you build whenever it helps, without asking.

- **A fresh worktree:** `./init.sh` (dependencies, `pnpm install`, and the full front-end build),
  then `pnpm install` in `src/BloomE2E`, `node build/get-testing-inputs.mjs`, and the C# build
  below.
- **C# changed** (an `E2eTestingApi` hook, anything under `src/BloomExe/`), or no `Bloom.exe` is
  built yet: `dotnet build src/BloomExe/BloomExe.csproj`. Plain `dotnet`, not the
  `build/agent-dotnet` wrapper: the wrapper builds into a private tree and builds no `Bloom.exe`
  apphost, so the suite would never find its output.
- **Front end changed** (a `data-testid` in a `.tsx` file): `pnpm build:ui` in
  `src/BloomBrowserUI` rebuilds the bundles in `output/browser`, manifest included. Run the full
  `pnpm build` there when the change also touches pug, LESS, markdown, or content.
- **Before you trust a failure, ask whether the build is current.** A `data-testid` that is in the
  source but not in `output/browser`, or a hook that is in `E2eTestingApi.cs` but not in the exe,
  fails exactly like a bug in the test.

The one collision left is the build lock: `dotnet build` fails with **MSB3027** ("being used by
another process") while a Bloom launched from this worktree's `output/Debug/` is running. If that
Bloom is one a test run left behind, kill it. If it is the developer's, tell them and let them
choose. Never kill a Bloom you did not start.

## The UI-vs-API policy (applies to every test)

- Every UI path gets **one dedicated journey test** that drives every click from
  launch to result. If your test covers a path no journey test covers yet, write the
  journey test.
- **Every other test takes the fastest reliable path to its start state**: Bloom HTTP
  API calls and `E2eTestingApi` hooks are fine for setup and navigation. The test
  drives the real UI only for the behavior it measures.
- Assertions may read APIs (e.g. `workspace/tabs`) instead of scraping the DOM.
- The action under test is never simulated through an API call.

## When something is hard to automate

Bloom automation is a product. Do not pile up workarounds. When a step resists
automation (no stable selector, a native dialog, a WinForms surface):

1. Check `src/BloomE2E/AUTOMATION-DEBT.md` — it may already be known, with a
   workaround or a decision.
2. Prefer fixing Bloom: add a `data-testid` in the React code, or add a hook to
   `src/BloomExe/web/controllers/E2eTestingApi.cs` (registered only under `--e2e`).
   Ship that fix in the same PR as the test. That is a change to Bloom's production code,
   and "Ship" below says what it brings with it: a Devin review, and its own heading in the
   PR description and the closing summary.
3. If you cannot fix it now, record it in `AUTOMATION-DEBT.md` and mark the test
   accordingly, so the investment is visible and schedulable.

## Tie the test to the Notion test inventory

The team's test inventory lives in the Notion database "Test Case Runs" (one row per
test case per release suite run, e.g. "6.5"; ~660 cases per run). The stable identity
of a case across runs is its **`Test Case ID`** number property. Access goes through
the `BLOOM_TESTCASE_NOTION` token (User env scope); the `write-manual-test` team skill
carries the API mechanics.

- **Automating an existing manual test:** the request normally points at a Notion
  card. Put its `Test Case ID` in the source code — in the test's title, e.g.
  `test("change UI language repeatedly [Test Case ID 69]", ...)` — so the code and the
  inventory stay tied. Read the card's Test Steps checkboxes; they are the behavior
  contract. When the automated test lands, set the card's `Automation` property to
  `Automated` — or to `Partial` when the automated test covers only part of the steps,
  and say which part in `Automation Notes`. While the test is still in an open PR, the
  card belongs in `PR Pending` instead, with the PR URL in `Automation Notes`. The title string is the whole mechanism;
  the library provides no helper or annotation for it, deliberately, so that grepping
  `Test Case ID` across `src/BloomE2E/tests/` finds every tie.
- **Writing a new e2e test that has no manual card:** add a row to the inventory so it
  remains the inventory of ALL tests, not only human-run ones. Allocate the next free
  `Test Case ID`, fill in the title, Summary, and Areas, and set `Automation` to
  `Automated`.
- **The `Automation` select property** holds the case's automation lifecycle:
  `Manual` → `Planned` → `Building` → `PR Pending` → `Automated` (or `Partial`), with
  `Keep manual` as the deliberate opt-out.
  - Empty means the same as `Manual` — the legacy rows were not bulk-stamped.
  - `Planned` marks a case the team judged a good automation candidate. To find work,
    filter the current suite run on `Automation = Planned`.
  - `Building` means someone is automating it right now. Set it when you start, so two
    people or agents do not automate the same case; set `Automated` (or `Partial`,
    with the covered part named in `Automation Notes`) when the test lands.
  - `PR Pending` means the test exists in an open PR that has not merged. Put the PR URL
    in `Automation Notes`. The `improve-test-automation-coverage` skill leaves cards here;
    a human (or a later sweep) moves them to `Automated` or `Partial` after the merge.
  - `Has automation problems` means an automation attempt found the card not automatable as
    written. `Automation Notes` says which step blocks it and what the card, or Bloom, needs.
    The developer who owns the card fixes that and sets `Planned` again.
  - `Keep manual` is a deliberate decision that a case stays human-run (e.g. installer
    feel, print quality); do not propose those for automation.
- Do not confuse `Test Case ID` (the number, stable, ours) with `Dokimion ID` (e.g.
  "TC41", a legacy import reference to BloomBooks/bloom-test-cases; 28 of the 6.5 rows
  do not have one).
- The long-term goal: every manual case either becomes `Automated` or is deliberately
  `Keep manual`; the inventory shows which is which.

## Choosing or creating test inputs

- **Default: a test creates its own collection** (and its own book, through the real UI,
  which is journey coverage of book creation as a side effect). Shared fixtures make
  tests fragile: when someone later edits a shared collection, its assumptions change
  under every test that uses it.
- **Use the inputs repo only for a fixture too expensive to build at run time** — for
  example, a collection of 200 books — and for the reference baselines of the
  visual-regression suite. `manifest.json` in that repo says what each collection is
  for; the `basic` collection serves tests whose subject is not the content (e.g. the
  workspace-tabs smoke test).
- To add or change a collection there: clone bloom-testing-inputs, author the books
  **with Bloom itself**, copy the folder in, add a `manifest.json` entry, run
  `pnpm validate` there, open a PR there. After it merges, advance
  `build/testing-inputs.pin` in your BloomDesktop PR. The two PRs land together: data
  first, then the pin.
- During local development, point the suite at your inputs checkout with
  `BLOOM_TESTING_INPUTS_DIR=<path>` instead of re-pinning on every edit.
- Tests must never modify `output/testing-inputs/` — the fixture copies the
  collection to a temp folder and Bloom runs against the copy.

## Writing the test

Put the file in `src/BloomE2E/tests/`, named after the behavior. Import `test` and
`expect` from `../fixtures/bloomTest` (never from `@playwright/test` directly — that
would give you Playwright's own browser instead of Bloom's WebView2), and say which
collection you want with `test.use`. Ask for a new one with `collectionSpec`:

```ts
test.use({
    collectionSpec: { name: "text-languages", languages: ["en", "de", "fr"] },
});
```

Name a prepared one with `collectionName` only for a fixture too expensive to build at
run time:

```ts
import { expect, test } from "../fixtures/bloomTest";
import { selectBook } from "../helpers/collection";
import { getTabs, switchTab } from "../helpers/workspace";

test.use({ collectionName: "basic" });

test("switching workspace tabs through the real top bar", async ({ page, bloomApp }) => {
    expect((await getTabs(page)).tabStates.collection).toBe("active");
    await selectBook(page, `${bloomApp.collectionDir}/A5 Portrait`);
    await switchTab(page, "publish");
    await switchTab(page, "edit");
});
```

The worker-scoped fixture launches Bloom on a temp copy of that collection and yields:

- `page` — Playwright's `page`, overridden to be Bloom's shell document (the top bar and
  the showing tab). Most tests need nothing else.
- `bloomApp` — `{ page, httpPort, cdpPort, bloomPid, collectionDir, restart }`. Build book
  paths from `collectionDir`, never from `output/testing-inputs`. `restart(callback)`
  stops Bloom, runs the callback, and starts it again, which is how a test changes what
  Bloom reads only at startup, such as the collection's languages.

The fixture also watches for the "Bloom had a problem" dialog and fails the test with the
exception it scrapes from behind the dialog's own "Learn More" link. See
`src/BloomE2E/README.md` for the whole story.

Rules that hold regardless of the final API:

- One behavior per test; name the file after the behavior.
- Real mouse events, not synthetic `element.click()`, for targets that need them
  (book tiles, Settings, PREVIEW); the helper layer handles this — never hand-roll
  `Input.dispatchMouseEvent` inside a test.
- NEVER trigger native OS dialogs (file pickers, the WinForms Image Toolbox, video
  capture) — Playwright cannot dismiss them and the run hangs.
- NEVER submit a problem report. The fixture fails the test with gathered detail when
  a "Bloom had a problem" dialog appears; do not loop-dismiss it.
- Waits are event/state-based (poll an API, await a selector), never fixed sleeps.

## Every step is a helper call

This suite will grow to a few thousand tests. It stays maintainable only if the knowledge
of HOW to do each thing in Bloom lives in exactly one place. A test says WHAT happens; the
helper layer in `src/BloomE2E/helpers/` says how.

**A test spells out nothing that is not the behavior it measures.** If a test has to crop
an image on the way to what it checks, it calls `cropImage(...)`. It does not find the
crop handle, work out the drag, and wait for the result. When Bloom changes how an image is
cropped, one helper changes, and every test that crops an image keeps working. The same
holds for the step that IS under test: the journey test for cropping also calls the helper
that drives the UI, so the click path is written once.

### When you write a test

1. **Look for the helper before you write a step.** There is one module per Bloom surface
   (see the list below). Read the header comment of the module for the surface you are on,
   then grep the folder for the concept, e.g. `grep -ri crop src/BloomE2E/helpers`.
2. **If a helper nearly fits, extend it.** Add a parameter or an option. Do not copy the
   helper into your test and change one line.
3. **If no helper exists, write one in `helpers/`, in the same PR.** Put it in the module
   for its surface, or start a module when the surface is new. Do this even when yours is
   the only test that needs it today. The second test comes soon, and the person who writes
   it will copy from yours.
4. **Selectors, `data-testid`s, API paths, and Bloom's quirks live only in helpers.** A test
   contains no CSS selector, no `getByTestId`, no `apiGet`/`apiPost`, and no retry loop. To
   read Bloom's state for an assertion, call a named reading helper such as
   `getLanguagesInBook`, so the API path is in one place. The one exception is the class of
   a translation group, such as `.bookTitle`, passed to `typeInGroup`: that names book
   content, not Bloom's UI.
5. **A sequence that appears twice is a helper.** Two tests, or two places in one test,
   that do the same three lines get one function. This includes sequences that only build
   a start state, such as "a book with two content pages in two languages". A helper that
   composes other helpers into a start state belongs in `helpers/` as well.
6. **A repeated expected value gets a name.** When the same literal block of expected state
   appears more than twice in a file, make it a constant or a small builder function at the
   top of the file.
7. **A file-local function is fine only for file-local knowledge**, such as the shape of
   the one book this file builds. The moment it encodes how to drive Bloom, it moves to
   `helpers/`.

### What a helper must do

Thousands of tests will lean on each helper, so each one meets this bar:

- **Named for the user's intent, in the words the UI uses.** `openPublishDestination`,
  `cropImage`, `setContentLanguages`. Not `clickThirdTab` or `postLanguageChange`.
- **Takes `page` first, then what varies.** Nothing test-specific inside: no book titles,
  no language lists, no page counts baked in.
- **Waits for its own result before it returns**, by polling Bloom's state. `goToPage`
  returns when the page is showing; `addPage` returns when Bloom reports the new page
  count. A caller never adds a wait after a helper call.
- **Fails with a message that names what Bloom offered instead.** When the template page
  asked for does not exist, the error lists the pages that do. The existing helpers show
  the pattern.
- **Carries a doc comment** that says which user action it stands for, and names any quirk
  it absorbs, with the `AUTOMATION-DEBT.md` entry if there is one.
- **Puts both routes for one action side by side.** The API route for setup (`selectBook`)
  and the UI route the journey test drives (a real click on the book tile) sit in the same
  module. A UI change is then fixed in one file.
- **Never sleeps for a fixed time.**

### Layers

Dependencies point down only:

1. **Primitives**: `helpers/api.ts` (`apiGet`, `apiPost`, `apiGetJson`, which run `fetch`
   inside the page with a relative URL, because Bloom's server rejects a `127.0.0.1` Host
   header and the CDP endpoint does not answer on `localhost`) and `helpers/realClick.ts`
   (`realClick`, `realClickAt`). Tests do not import these; surface modules wrap them.
2. **One module per Bloom surface**, named after the surface:
   - `helpers/workspace.ts` — `switchTab`, `getTabs`, `waitForActiveTab`. Bloom hides the
     Edit and Publish tabs until a book is selected.
   - `helpers/collection.ts` — `selectBook`, `waitForCollectionReady`.
   - `helpers/bookMaking.ts` — `makeBookFromTemplate`, `addPage`, `findBookFolder`,
     `setContentLanguages`, `getPages`, `getContentPages`, `goToPage`, `typeInGroup`,
     `waitForEditablePage`, `editablePageFrame`. A book made from a template starts with
     front and back matter only, so a test that needs content calls `addPage`. Bloom writes
     a page only when the book leaves it, so `goToPage` is also how a test saves what it
     typed.
   - `helpers/pageList.ts` — `duplicatePageWithButton`, `duplicatePageWithContextMenu`,
     `movePageToSlotOf`, and the API route `duplicateCurrentPage` for setup.
   - `helpers/images.ts` — `chooseImageFile` (setup, no picker), `cropImage`,
     `getImagePlacement`.
   - `helpers/publish.ts` — `openPublishDestination`, `getTextLanguageRows`,
     `expectTextLanguageRows`, `clickTextLanguage`, `showBloomPubPreview`,
     `getPreviewLanguages`, `getLanguagesInBook`, `getTooltipForLanguage`.
   This list is a map, not the index. The folder is the index: new modules appear there
   before anyone updates this file.
3. **Tests**, which call surface helpers and nothing lower.

### When you change a helper, or the Bloom UI a helper drives

- Grep `src/BloomE2E/tests/` for the helper's name and run every test that calls it before
  you open the PR.
- When you change a `data-testid`, an API endpoint, or a UI flow that a helper drives, grep
  `src/BloomE2E/helpers/` for the old id or path and update the helper in the same PR. That
  is the payoff of the whole rule: one change, in one file, and the suite follows.

## Running

From `src/BloomE2E/` (its own pnpm package — run `pnpm install` there once):

```bash
pnpm test                                                # the whole suite
pnpm exec playwright test tests/workspace-tabs.spec.ts   # one file
pnpm exec playwright test -g "switching workspace tabs"  # one test by title
```

A run launches a real Bloom, but no window appears: the fixture passes `--headless`, which puts
Bloom's window far outside every monitor, so a run does not take your desktop over. Set
`BLOOM_E2E_HEADED=1` to watch it (`--debug` sets it for you). The window goes off-screen rather
than minimized or hidden because WebView2 stops painting a minimized window, which makes every
screenshot blank.

A run needs a built `Bloom.exe` under `output/{Debug,Release}/{x64,AnyCPU,}/` (build it yourself;
see "Build Bloom whenever it helps") and the inputs at `output/testing-inputs`. Point
`BLOOM_TESTING_INPUTS_DIR` at a bloom-testing-inputs checkout to use your own in-progress
collections instead of the pinned ones.

The launched Bloom serves its React UI from the built `output/browser`, so **an edit to a `.tsx`
file does not reach a run until that bundle is rebuilt.** To test the working tree instead, start
a dev server and name its port in `BLOOM_E2E_VITE_PORT`; the fixture passes `--vite-port` and
Bloom loads every React control from it. Set `PORT` as well as `--port`, or the dev server's
HMR and React-Refresh URLs still point at 5173 and the page fails to load its entry module.

```bash
PORT=5173 pnpm exec vite --port 5173 --strictPort   # in src/BloomBrowserUI
BLOOM_E2E_VITE_PORT=5173 pnpm test                  # in src/BloomE2E
```

**Use 5173, and set the variable.** The page list and the toolbox write `http://localhost:5173`
into their own imports, so on any other port those two frames load nothing and come up empty,
which reads as the feature being missing. And leaving the variable unset does not mean "no dev
server": a dev build of Bloom probes 5173 by itself, so an unset variable and a server elsewhere
means the run quietly tests the built bundle, however old it is. Stop a Bloom that already holds
5173 rather than moving the dev server. See AUTOMATION-DEBT.md.

`.github/workflows/nightly.yml` does not run this suite yet. The step it will need is the
same `pnpm test` in that folder, after the Release build and the testing-inputs fetch that
the visual-regression job already does.

## Ship: a PR that is ready to merge

The job is not done when the test is green. It is done when a PR is open, every reviewer has
spoken, and the developer can merge it, or can read one short message to see what is left.

**Invoking this skill is the authorization for these writes**, without stopping to confirm:
commit and push the branch; create the PR and later mark it ready for review; reply to bot
review threads and resolve them; the `devin-review` skill's own writes; and the Notion card's
`Automation` and `Automation Notes` properties. It does not authorize merging the PR, requesting
a reviewer, or dismissing a human's comment. If the auto-mode classifier denies a `gh` write, do
not route around it: report that step as pending in the summary.

**Base branch: `master`.** `src/BloomE2E` exists only there; this is the exception to the "new
work targets Version6.5" rule in `AGENTS.md`. A caller that owns the PR lifecycle (for example
the `improve-test-automation-coverage` worker brief, which ships through `preflight`) says so,
and its instructions replace steps 2 to 5 below.

### 1. Gate

- The new test passes **three times in a row**, launched with no Bloom of yours running, and no
  `Bloom.exe` you started survives the run. A test that passes two of three is not done.
- Every test that calls a helper you changed passes.
- `pnpm typecheck` in `src/BloomE2E`.
- If you changed the front end: `pnpm lint` and `pnpm typecheck` in `src/BloomBrowserUI`, and the
  bundle you built for the test compiled. If you changed C#: it builds, and the unit tests for the
  classes you touched pass (`build/agent-dotnet.ps1 test ... --filter ...`).

### 2. Commit, integrate, push, open the PR

- Commit everything with a descriptive message and the repo's commit trailer; let the hooks run.
- `git fetch origin`, then merge `origin/master`. Re-run the gate if the merge touched anything
  you touched. Abort a merge with a semantic conflict and hand it to the developer in the summary.
- Push, then open a **draft** PR against `master`:
  `gh pr create --draft --base master --title "<what the test covers> (Notion test case <id>)" --body-file <file>`.
  Always `--body-file`; an inline body does not expand newlines in PowerShell.
- The PR body is the same text as the closing summary (step 6), minus the run-specific details
  under "Problems" that belong only in chat. It always contains the line
  `Automates Notion test case <id> (<title>): <card url>` and the **Bloom production code
  changes** heading, even when that section says "None".
- Set the Notion card to `PR Pending` with the PR URL and the covered and not-covered steps in
  `Automation Notes` (see "Tie the test to the Notion test inventory").

### 3. Decide whether production code changed

**Production code is anything that ships in Bloom or builds it:** `src/BloomExe/**`,
`src/BloomBrowserUI/**`, `src/content/**`, `DistFiles/**`, `build/**`, and any `*.csproj`,
`*.sln`, or `package.json` outside `src/BloomE2E`. Not production code: `src/BloomE2E/**`,
`.github/**`, and Markdown. Decide from `git diff --name-only origin/master...HEAD`, not from
memory. A `data-testid` and an `E2eTestingApi` hook are small, but they are production code.

**Test code inside those folders does not count.** Unit tests live beside or near the code they
test: `src/BloomTests/**` (C#), and in `src/BloomBrowserUI` the `*.test.ts(x)` and `*.spec.ts(x)`
files, `__tests__` folders, and the `canvas-e2e-tests` folder. Changing one of them, which is
rare while building an e2e test, means the affected unit tests must run and pass in the gate,
but it does not by itself trigger Devin or the **Bloom production code changes** heading.

### 4. Reviewers

- **CI, always.** `gh pr checks <n>` until every check is terminal. A failure is yours to fix
  unless it is clearly unrelated to the diff, in which case the summary names it.
- **Devin, when production code changed.** Run the `devin-review` skill on the PR through to its
  terminal result (BloomDesktop's `pr-automation.yml` triggers Devin on every push; the skill
  waits, gathers, and mirrors the findings). Treat each finding as `preflight` does:
  - clear, correct, and confined to the test hook: fix it, push, reply "Fixed in `<sha>`", and
    resolve the thread. A new push restarts Devin, so run the skill again; cap this at four
    rounds;
  - mistaken: reply with the reason and resolve;
  - anything else, such as a finding that would change what a user sees or how Bloom behaves
    outside `--e2e`, or one you cannot settle from the code: leave the thread **open** and defer
    it to the developer in the summary, with Devin's text, your assessment, and a recommendation.

  Devin still analyzing is not "clean". If it has not finished after 30 minutes, record "timed
  out after 30 min" and say so in the summary.
- **Any other bot** that posts on its own (Greptile and the like) gets the same fix, refute, or
  defer treatment whether or not production code changed: its comments are on the PR either way.
- A test-only PR does not need Devin. Say so in the summary in one line.

### 5. Mark it ready

When CI is green, every bot thread has a documented outcome, and nothing is deferred to the
developer: `gh pr ready <n>`. The PR is now ready to merge, and the developer does the merging.
If anything is deferred, leave the PR a draft and let the summary say why. Never merge, and never
request a reviewer.

### 6. Close with the summary

The last message of the run is short (about 200 words) and stands on its own. In this order:

1. **Bloom production code changes**, only when there are any, and then at the very top so
   nobody misses it: every file outside `src/BloomE2E`, what changed and why, and Devin's
   outcome for it. When there are none, one line at the end instead: "No Bloom production code
   changed; Devin not run."
2. **Purpose**: the behavior the test protects, and the Notion card (`Test Case ID` and title).
3. **Steps**: what the test does, as a few bullets in the order it does them.
4. **How it verifies the result**: what state it reads (an API, the page list, a file on disk)
   and what it expects; how many green runs you saw; that no Bloom survived.
5. **PR**: URL, draft or ready, CI state, and each reviewer's terminal state.
6. **Problems**: steps that stay manual and why, `AUTOMATION-DEBT.md` entries you added,
   anything deferred to the developer with your recommendation, and anything the environment
   made hard (a build that would not go, a flaky run you had to chase). If none, say "None".

## Checklist before you call it done

- [ ] Journey coverage exists for the UI path (yours or a pre-existing journey test).
- [ ] The test contains no selector, test id, API path, or retry loop. Every step is a
      helper call.
- [ ] Every new helper is in `src/BloomE2E/helpers/`, has a doc comment, and waits for its
      own result.
- [ ] No sequence appears twice; no expected-value literal appears more than twice.
- [ ] If you changed a helper, every test that calls it still passes.
- [ ] The test passes locally, launched from a clean state (no Bloom running).
- [ ] No Bloom.exe process survives the run.
- [ ] New inputs: PR merged in bloom-testing-inputs, pin advanced here, both green.
- [ ] Anything you could not automate cleanly is recorded in `AUTOMATION-DEBT.md`.
- [ ] The PR is open against `master`, CI is green, and it is marked ready unless something is
      deferred to the developer.
- [ ] If production code changed, Devin ran to a terminal state and every finding has an
      outcome on its thread or in the summary.
- [ ] The Notion card is `PR Pending` with the PR URL.
- [ ] The closing summary was delivered, with production code changes at the top when there
      are any.
