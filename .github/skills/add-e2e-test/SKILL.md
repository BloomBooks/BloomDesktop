---
name: add-e2e-test
description: How to add a new edge-to-edge (e2e) test that drives the real Bloom.exe through its UI, using a collection from the bloom-testing-inputs repo. Use when asked to automate a manual test, add an e2e test, add a journey test, or extend the nightly e2e suite.
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
   Ship that fix in the same PR as the test.
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
  and say which part in `Automation Notes`. The title string is the whole mechanism;
  the library provides no helper or annotation for it, deliberately, so that grepping
  `Test Case ID` across `src/BloomE2E/tests/` finds every tie.
- **Writing a new e2e test that has no manual card:** add a row to the inventory so it
  remains the inventory of ALL tests, not only human-run ones. Allocate the next free
  `Test Case ID`, fill in the title, Summary, and Areas, and set `Automation` to
  `Automated`.
- **The `Automation` select property** holds the case's automation lifecycle:
  `Manual` → `Planned` → `Building` → `Automated` (or `Partial`), with `Keep manual`
  as the deliberate opt-out.
  - Empty means the same as `Manual` — the legacy rows were not bulk-stamped.
  - `Planned` marks a case the team judged a good automation candidate. To find work,
    filter the current suite run on `Automation = Planned`.
  - `Building` means someone is automating it right now. Set it when you start, so two
    people or agents do not automate the same case; set `Automated` (or `Partial`,
    with the covered part named in `Automation Notes`) when the test lands.
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

Helper modules:

- `helpers/workspace.ts` — `switchTab`, `getTabs`, `waitForActiveTab`. Bloom hides the
  Edit and Publish tabs until a book is selected.
- `helpers/collection.ts` — `selectBook`, `waitForCollectionReady`.
- `helpers/bookMaking.ts` — `makeBookFromTemplate`, `addPage`, `findBookFolder`,
  `setContentLanguages`, `getPages`, `getContentPages`, `goToPage`, `typeInGroup`. A book
  made from a template starts with front and back matter only, so a test that needs
  content calls `addPage`. Bloom writes a page only when the book leaves it, so `goToPage`
  is also how a test saves what it typed.
- `helpers/api.ts` — `apiGet`, `apiPost`, `apiGetJson`, which run `fetch` inside the page
  with a relative URL (Bloom's server rejects a `127.0.0.1` Host header; the CDP endpoint
  does not answer on `localhost`).
- `helpers/realClick.ts` — `realClick`, `realClickAt`.

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

## Running

From `src/BloomE2E/` (its own pnpm package — run `pnpm install` there once):

```bash
pnpm test                                                # the whole suite
pnpm exec playwright test tests/workspace-tabs.spec.ts   # one file
pnpm exec playwright test -g "switching workspace tabs"  # one test by title
```

A run opens a real Bloom window; that is expected. It needs a built `Bloom.exe` under
`output/{Debug,Release}/{x64,AnyCPU,}/` and the inputs at `output/testing-inputs`. Point
`BLOOM_TESTING_INPUTS_DIR` at a bloom-testing-inputs checkout to use your own in-progress
collections instead of the pinned ones.

`.github/workflows/nightly.yml` does not run this suite yet. The step it will need is the
same `pnpm test` in that folder, after the Release build and the testing-inputs fetch that
the visual-regression job already does.

## Checklist before you call it done

- [ ] Journey coverage exists for the UI path (yours or a pre-existing journey test).
- [ ] The test passes locally, launched from a clean state (no Bloom running).
- [ ] No Bloom.exe process survives the run.
- [ ] New inputs: PR merged in bloom-testing-inputs, pin advanced here, both green.
- [ ] Anything you could not automate cleanly is recorded in `AUTOMATION-DEBT.md`.
