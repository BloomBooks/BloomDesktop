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
