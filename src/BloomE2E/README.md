# BloomE2E

Edge-to-edge tests that drive the real `Bloom.exe`.

Each test launches its own Bloom on a known collection, clicks through the embedded WebView2 the
way a person would, asserts, and tears the instance down. Nothing here talks to a mock, and nothing
here reuses the Bloom you happen to have open.

This is a product, not a pile of scripts. When Bloom is hard to drive, the fix is to make Bloom
easier to automate; see [AUTOMATION-DEBT.md](AUTOMATION-DEBT.md), which is the visible backlog of
that work. The procedure for adding a test lives in `.github/skills/add-e2e-test/SKILL.md`.

## Writing a test

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

A test says which collection it wants, in one of two ways, and never both:

- `test.use({ collectionSpec: { name: "text-languages", languages: ["en", "de", "fr"] } })`
  makes an empty collection with those languages. **This is the default choice.** The test then
  makes the book it needs, which is journey coverage of book creation as a side effect. A test
  that shares a prepared collection inherits somebody else's assumptions, and the next person to
  edit that collection changes what the test means without reading it.
- `test.use({ collectionName: "basic" })` names a folder under
  `output/testing-inputs/collections`. Use this only for a fixture too expensive to build at run
  time, such as a collection of 200 books.

Either way the fixture launches Bloom on a temp copy and attaches to the WebView2. One Bloom
serves every test in a worker, because launching takes several seconds.

## The fixture API

`page` is Playwright's own `page` fixture, overridden to be Bloom's shell document: the top bar,
and whatever tab is showing. Most tests need nothing else.

`bloomApp` carries the rest of the launched instance:

| Field           | What it is                                                             |
| --------------- | ---------------------------------------------------------------------- |
| `page`          | The same page as the `page` fixture.                                    |
| `httpPort`      | The port Bloom's HTTP server opened on.                                 |
| `cdpPort`       | The port the embedded WebView2 answers CDP on.                          |
| `bloomPid`      | The process id of the Bloom serving this collection.                    |
| `collectionDir` | The temp copy of the collection. Build book paths from this, never from `output/testing-inputs`. |
| `restart`       | Stop Bloom, run an optional callback, start it again on the same collection, and return the new page. |

`restart(betweenStopAndStart)` is how a test changes something Bloom only reads at startup. The
collection's languages are the case that needed it, and a test does not call `restart` itself for
that: `setCollectionLanguages(bloomApp, tags)` posts the new languages to the `e2e/` hook that
writes the `.bloomCollection`, then restarts Bloom and returns the new page. Bloom is killed
rather than asked to quit, so leave the page being edited before restarting or what was typed on
it is lost. Use the page `restart` returns; the old one is closed.

Teardown kills the process tree, waits for the HTTP port to go dark, and deletes the temp copy.

A background watcher polls for Bloom's "Bloom had a problem" dialog. When one appears it reads the
exception from behind the dialog's own "Learn More" link, closes the dialog the way its Close
button does, and fails the test with that text. It never clicks Submit, which would send a report,
a screenshot, and the book to Bloom's servers. If the same problem keeps coming back, that is a
real bug in the code under test; read the message and fix it rather than working around it.

## The UI-vs-API policy

- Every UI path gets **one dedicated journey test** that drives every click from launch to result.
  If your test covers a path no journey test covers, write the journey test too.
- **Every other test takes the fastest reliable path to its start state.** Bloom's HTTP API and the
  `E2eTestingApi` hooks (`e2e/*`, registered only under `--e2e`) are fine for setup and navigation.
  `helpers/api.ts` and `helpers/collection.ts` are there for exactly this.
- **Assertions may read APIs.** `getTabs()` asks Bloom which tab is active rather than scraping the
  DOM, which is both more accurate and less brittle.
- **The action under test is never simulated through an API call.** A test of tab switching clicks
  the tab; it does not post `workspace/selectTab`.

## Helpers

- `helpers/workspace.ts` — `switchTab`, `getTabs`, `waitForActiveTab`, and the zoom control's
  `getZoom`, `setZoom`. Note that Bloom hides the Edit and Publish tabs until a book is selected.
- `helpers/formatDialog.ts` — the format gear beside the text box being edited, and the Format
  dialog it opens: `openFormatDialog`, `clickOutsideFormatDialog`, `dragFormatDialog`,
  `getFormatDialogPlacement`, and the scrolling and zooming that put the gear at the edge of the
  screen.
- `helpers/collection.ts` — `selectBook`, `waitForCollectionReady`, `setCollectionLanguages`.
- `helpers/bookMaking.ts` — make a book, add pages, type into it, read its pages.
- `helpers/addPageDialog.ts` — open, read, scroll and close the real Add Page dialog, and add a
  page through it. Tests that only need a page in their book call `addPage` instead.
- `helpers/files.ts` — `fingerprintFolder`, `isInsideFolder`, for checks against the disk.
- `helpers/api.ts` — `apiGet`, `apiPost`, `apiGetJson`. These run `fetch` inside the page with a
  relative URL, which is not a style choice: Bloom's server rejects a `127.0.0.1` Host header, and
  the CDP endpoint does not answer on `localhost`. The file explains it.
- `helpers/realClick.ts` — `realClick`, `realClickAt`. Book tiles, Settings, and PREVIEW ignore a
  synthetic `element.click()`. Never hand-roll `Input.dispatchMouseEvent` in a test; add the
  gesture here.
- `helpers/screenshot.ts` — `captureCurrentBookPage`, `captureElement`, `readPngSize`. Captures an
  element taller than the window. `Page.captureScreenshot` with `captureBeyondViewport` hangs in
  WebView2, so this enlarges the window, clips, clears the override, and times out every CDP
  request. Never open a CDP session in a test; add the capture here.

Two things a test must never do: trigger a native OS dialog (file pickers, the WinForms Image
Toolbox, video capture), because Playwright cannot dismiss one and the run hangs; and wait on a
fixed sleep instead of a state (`expect.poll` an API, or await a selector).

## The Notion test inventory

The team's test inventory is the Notion database "Test Case Runs", one row per test case per
release suite. A case's stable identity is its `Test Case ID` number. When an automated test covers
a case, put that id in the test title so the code and the inventory stay tied:

```ts
test("change UI language repeatedly [Test Case ID 69]", async ({ page }) => { ... });
```

Then set the card's `Automation` property to `Automated`. If the test covers only part of the card's
steps, split the card first into an `[Automated portion]` that keeps the id and a `[Manual portion]`
with a new id, so no human-run step hides behind an automated card. A new test with no manual card
gets a new inventory row, so the inventory stays the inventory of all tests rather than only the
human-run ones. `.github/skills/add-e2e-test/SKILL.md` has the details.

## Running

```bash
pnpm install                         # once, in this folder
pnpm test                            # the whole suite
pnpm exec playwright test tests/workspace-tabs.spec.ts        # one file
pnpm exec playwright test -g "switching workspace tabs"       # one test by title
pnpm exec playwright test --debug    # step through it
```

A run opens a real Bloom window on your desktop. That is expected; do not click in it.

### Where the Bloom window goes: `BLOOM_AUTOMATION_MONITOR`

One environment variable decides where every window an e2e run opens goes, the main window and
the splash screen alike. Set it in your shell, or per run:

| Value | What happens |
| --- | --- |
| `headless`, or `0` | Every window opens far outside every monitor. You see nothing, so a run can go on while you work. |
| a 1-based monitor number, counted left to right | Every window opens on that monitor, so a run stays off the one you are working on. |
| unset, or anything else | Bloom places its windows as it always does, and you see the run. |

```bash
BLOOM_AUTOMATION_MONITOR=headless pnpm test    # see nothing
BLOOM_AUTOMATION_MONITOR=0 pnpm test           # the same thing: no monitor at all
BLOOM_AUTOMATION_MONITOR=2 pnpm test           # on the second monitor from the left
pnpm test                                      # wherever Bloom normally opens
```

A value Bloom cannot use, a typo or a monitor you do not have, counts as "anything else": you get
a visible window, which is exactly what tells you the variable did not take effect. A setting that
hid the window on a typo would leave you nothing to notice.

**The number counts left to right, which is not the number Windows Settings shows.** Monitor 1 is
your leftmost monitor, 2 the next one to the right, and so on, matching the arrangement picture in
Windows Settings but not the numbers printed on it. Windows does not document how the Settings app
makes those numbers, and no API reproduces them: on one three-monitor machine Windows Settings said
1 (primary, centre), 2 (right) and 3 (left), while left to right is 1 (left), 2 (primary, centre)
and 3 (right). So read the arrangement, not the numbers on it. Bloom also writes the whole mapping
to `%TEMP%\SIL\Bloom\Log.txt` on every automation start:

```
BLOOM_AUTOMATION_MONITOR='2': every window goes on the monitor at {X=0,Y=0,Width=2560,Height=1440}.
The monitors this process sees, numbered left to right as this variable numbers them (which is NOT
how Windows Settings numbers them): 1=(-1920,601) 1920x1200, 2=(0,0) 2560x1440 primary,
3=(3840,432) 1920x1200.
```

`headless` moves the window off-screen rather than minimizing or hiding it, because WebView2 stops
painting a minimized window, which would make every screenshot blank. Off-screen the window paints
exactly as it would in front of you, so rendering and keyboard input behave the same.

It goes **below** your primary monitor, not off to one side, and that matters on a machine whose
monitors run at different scale factors. Windows gives a window the scale factor of the monitor
nearest to it. A window out to the left would take the leftmost monitor's scale factor while
carrying a size measured in the primary's, and on a 150% primary beside a 100% monitor that made a
window 3840x2100 real pixels, taller than any monitor on the machine, with a page viewport no user
could have. Directly below the primary, the primary stays the nearest monitor and the size is
right. See `AutomationWindowPlacement.GetBoundsOffEveryMonitor`.

`--debug` clears a `headless` setting for you: stepping through a test whose window you cannot see
is pointless. A setting that names a monitor is left alone, because that window is visible anyway.

The variable applies only to a run under `--automation`, which is every e2e run and nothing else.
A Bloom you start yourself is unaffected, however the variable is set.

The suite needs a built `Bloom.exe` under `output/{Debug,Release}/{x64,AnyCPU,}/` and the test
inputs at `output/testing-inputs`, fetched by `node build/get-testing-inputs.mjs` at the commit
`build/testing-inputs.pin` names.

To test against your own in-progress input collections instead of the pinned ones, point
`BLOOM_TESTING_INPUTS_DIR` at a checkout of
[bloom-testing-inputs](https://github.com/BloomBooks/bloom-testing-inputs) — the folder that
contains `collections/`:

```bash
BLOOM_TESTING_INPUTS_DIR=D:/bloom-testing-inputs pnpm test
```

Either way the fixture copies the collection before Bloom opens it, so a run never modifies your
inputs.

## Testing a front-end change

The launched Bloom serves its React UI from the built `output/browser`, so an edit to a `.tsx`
file does not reach a run until somebody rebuilds that bundle. To test the working tree instead,
start a Vite dev server and name its port in `BLOOM_E2E_VITE_PORT`; the fixture then passes
`--vite-port` to Bloom, which loads every React control from the dev server.

```bash
# In one terminal, in src/BloomBrowserUI. Set PORT as well as --port: the port in
# vite.config.mts comes from process.env.PORT, and --port alone leaves the HMR and
# React-Refresh URLs pointing at 5173, which makes the page fail to load its entry module.
PORT=5173 pnpm exec vite --port 5173 --strictPort

# In another, in src/BloomE2E
BLOOM_E2E_VITE_PORT=5173 pnpm exec playwright test
```

**Use 5173, and set the variable.** The port is not free to choose: the page list and the toolbox
write `http://localhost:5173` into their own imports, so on any other port those two frames load
nothing and come up empty, which reads as the feature being missing rather than as a port
problem. And leaving `BLOOM_E2E_VITE_PORT` unset does not mean "no dev server": a dev build of
Bloom probes 5173 by itself, so an unset variable and a server somewhere else means the run
quietly tests the built bundle, however old it is. Both halves are in AUTOMATION-DEBT.md under
"A Vite dev server only reaches the whole UI on port 5173".

So stop a Bloom that is already using 5173 before a run, rather than moving the dev server.

## In CI

`.github/workflows/nightly.yml` runs the whole suite every night against the Release build it has
just made, and reports it as its own check run, "Nightly tests: BloomE2E (Playwright)". A failing
night uploads Playwright's HTML report and traces as the `e2e-report` artifact. A manual run of
that workflow can tick this suite alone, which is the quick way to see how a test behaves on the
runner rather than on your machine.
