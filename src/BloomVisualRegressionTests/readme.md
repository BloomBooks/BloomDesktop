# How to run

0. `pnpm install`
1. Make sure Bloom has been built (the suite launches `../../output/Debug/x64/Bloom.exe`).
2. In the terminal, run `pnpm test` (single run) or `pnpm testPatient` (same, with a huge
   per-test timeout for debugging under a breakpoint).

You do **not** need to open Bloom yourself. Each run copies the test collection to a throwaway
temp folder, launches its own dedicated Bloom on that copy (with `--e2e --automation`), drives it
over HTTP, and shuts it down and deletes the temp folder afterward. Because it launches with
`--automation`, it can run alongside a Bloom you already have open. Because it operates on a temp
copy, a run never modifies the committed collection.

# What is in here

-   `index.spec.ts` — the screenshot comparison suite this folder was made for.
-   `calendar.spec.ts` — an end-to-end test of the Wall Calendar tooling. It compares no
    screenshots; it makes a calendar book from the template, answers the setup dialog in Bloom's
    own window over CDP, and checks the month grids and the collection's `configuration.txt`.
    Run it on its own with `pnpm testCalendar` (`pnpm testScreenshots` for the other one).
-   `bloomInstance.ts` — the launching, finding and shutting down of a Bloom of our own, shared
    by both suites. Add a new suite by calling `launchDedicatedBloom` from its `beforeAll`.

The suites run one file at a time (`fileParallelism: false`), because each launches its own
Bloom.

# Testing front-end changes you have not built

The Bloom these suites launch reads its user interface from `output/browser`, which is only as
new as the last full `pnpm build`. To test TypeScript you are still working on, start a Vite dev
server and name its port in `BLOOM_VITE_PORT`; the launcher then passes `--vite-port` to Bloom
and the run uses your working tree.

-   bash: `pnpm -C ../BloomBrowserUI dev --port=5199` in one terminal, then
    `BLOOM_VITE_PORT=5199 pnpm testCalendar` in another.
-   PowerShell: `$env:BLOOM_VITE_PORT=5199; pnpm testCalendar`.

Expect it to be slow: the dev server serves unbundled modules, and making a book from the
24-page Wall Calendar template has been seen to take over a minute on a busy machine. CI builds
first and sets nothing, so it uses `output/browser` as usual.

# Which collection state is rendered

By default the suite renders the **committed (HEAD)** state of `collections/`, exported from git
(`git show` per tracked file). This makes runs deterministic and immune to accidental working-tree changes (Bloom's
own book rewrites, or a stray Bloom editing the repo copy). The reference images live in the working
tree, so updating them (regenerate → eyeball → commit) is unaffected.

-   To render your **uncommitted** working-tree changes — when you are deliberately modifying or
    adding a test book — run with `BLOOM_VR_WORKING_TREE=1`:
    -   bash: `BLOOM_VR_WORKING_TREE=1 pnpm test`
    -   PowerShell: `$env:BLOOM_VR_WORKING_TREE=1; pnpm test`
-   If the working tree has uncommitted book changes in a default run, the suite prints a note that
    it is ignoring them (so it is never a surprise), then renders committed HEAD.
-   If `git`/`tar` are unavailable or the export fails for any reason, the suite falls back to
    copying the working tree, so it still runs.

# Test failures

If a test fails, look in the `screenshots/` folder of the book that failed for
`<label>-diff.png` (the differing pixels, in red) next to `<label>-reference.png` (the committed
baseline) and `<label>-current.png` (this run). If the new render is correct, replace the reference
with the current image (or delete the reference and re-run to regenerate it) and commit.

# Brandings and themes

See the `brandings` and `themes` arrays in `index.spec.ts` for what is exercised.

# Books

Put books in `collections/basic`. Files Bloom regenerates on its own (e.g. `origami.css`,
`branding.css`, `appearance.css`, `defaultLangStyles.css`) are gitignored — do not commit them; Bloom
re-supplies them into each book folder, and leaving them untracked lets these tests catch unexpected
changes in what the distribution copies in.

# Collections

There is only the one collection. Some code anticipates more, but there is no mechanism to relaunch
Bloom on a different one; set branding/theme in the tests instead.

# TODO

-   The diffs are fairly low-resolution.
-   Could test different XMatters.
