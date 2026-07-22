# Bloom visual-regression tests

These tests drive a **real, running Bloom** (selecting books, changing branding/theme, staging
BloomPUBs) and screenshot the book preview and bloom-player, comparing each shot against a
committed reference image.

# How to run

## 1. Install dependencies (once)

```
pnpm install
pnpm exec playwright install chromium
```

`pnpm install` does **not** download the browser binary Playwright needs — the second command
does. Skip it and the first run dies with `browserType.launch: Executable doesn't exist`.

## 2. Have a CURRENT Bloom build

The suite talks to Bloom over test-only API endpoints (`/bloom/api/e2e/...`) that exist only in
**DEBUG** builds and were added/changed alongside these tests. If Bloom is missing an endpoint the
tests call, Bloom pops a modal "Cannot Find API Endpoint" problem dialog, its server thread wedges,
and the run fails (`ECONNRESET` / `ECONNREFUSED`, or a long hang on older versions).

The suite launches whatever `output/Debug/.../Bloom.exe` happens to be sitting there — it does **not**
build for you — so a stale exe is the single most common reason every test fails. Before running,
make sure that exe is current for this branch, e.g. by running `./go.sh` from the repo root once (it
builds the current source), or otherwise building `src/BloomExe` in Debug. See the repo `AGENTS.md`
about not trusting a stale prebuilt `Bloom.exe`.

## 3. Run the tests

```
pnpm test
```

On startup the suite either **attaches to** a Bloom already running on the test (`basic`) collection,
or **launches** the built exe on `collections/basic/basic.bloomCollection` itself and waits for it.
So you can either:

- **Let it launch Bloom** — just make sure no Bloom is running (see note below) and that the built
  exe is current (step 2), then `pnpm test`.
- **Attach to your own Bloom** — open `collections/basic/basic.bloomCollection` in a current Bloom
  first, then `pnpm test`. It discovers the actual port (Bloom takes the next free block from 8089).

Note: if a Bloom is already running on a *different* collection, the suite fails fast telling you to
close it (selecting our test books there would throw). It only attaches to a Bloom whose collection
is `basic`.

Gotcha: a DEBUG build normally pops an **"attach debugger now"** messagebox when launched with an
argument (see `Program.cs`), which would block startup — Bloom's HTTP server never comes up and the
suite gives up after 60s with "Bloom did not start on the basic collection within 60s". When the
suite launches Bloom itself it sets `BLOOM_SKIP_ATTACH_DEBUGGER_PROMPT=1` to suppress that dialog.
If you instead launch Bloom **yourself** (attach mode) from a DEBUG build, you'll still see the
dialog — just click OK.

To re-run in watch mode, just save `index.spec.ts`. Press `q` to quit the watcher.

## Timeouts

Each test does real, slow Bloom work, so the vitest defaults (5s per test, 10s per hook) are far too
short. `vitest.config.ts` raises both. Note the hook timeout can only be set via that config file —
vitest 0.34 has no `--hook-timeout` CLI flag — so the older `testPatient` script (which only raises
the *test* timeout) is not enough on its own; the config file is what makes the hooks pass.

# Test failures

If a test fails on a pixel diff, look in that book's `screenshots/` folder for
`<label>-diff.png` (alongside `<label>-reference.png` and `<label>-current.png`). If the new
rendering is the correct one, replace the `-reference.png` with the `-current.png`.

The first time a given case runs, there is no reference image yet, so the suite just *creates* the
reference and the case passes without comparing. Commit those reference PNGs.

# Brandings / themes

The list of brandings and appearance themes exercised is at the top of `index.spec.ts`
(`brandings` / `themes`). Each branding is tested with the default theme, and each non-default theme
with the Default branding.

# Books

Put test books in `collections/basic`.

# Collections

The suite can't tell Bloom to switch collections (the source-aware launcher can't be told which
collection to open), so it uses whatever collection Bloom opens with — currently only the one
`basic` collection. Branding is set per-test, so one collection is enough.

# TODO

-   The diffs are fairly low-resolution.
-   Something prevents committing Bloom html; you have to bypass that.
-   Each time Bloom is run it makes new page IDs, causing spurious file diffs when you commit.
-   Could test different XMatters.
