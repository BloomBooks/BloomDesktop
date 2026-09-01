# How to run

0. `pnpm install`
1. `node ../../build/get-testing-inputs.mjs` (from the repository root: `node build/get-testing-inputs.mjs`).
   This fetches the test books and reference screenshots; see "Where the test inputs come from"
   below. It is a fast no-op once you have them, so it is safe to run every time.
2. Make sure Bloom has been built (the suite launches `output/Debug/x64/Bloom.exe` or one of the
   other build configurations).
3. In the terminal, run `pnpm test` (single run) or `pnpm testPatient` (same, with a huge
   per-test timeout for debugging under a breakpoint).

You do **not** need to open Bloom yourself. Each run copies the test collection to a throwaway
temp folder, launches its own dedicated Bloom on that copy (with `--e2e --automation`), drives it
over HTTP, and shuts it down and deletes the temp folder afterward. Because it launches with
`--automation`, it can run alongside a Bloom you already have open. Because it operates on a temp
copy, a run never modifies the books it renders.

# Where the test inputs come from

The books and their reference screenshots are **not in this repository**. They live in
https://github.com/BloomBooks/bloom-testing-inputs, so that other projects can use the same
inputs and so that changing a test book does not churn BloomDesktop's history.

`build/testing-inputs.pin` names that repository and the exact commit this branch tests against.
`node build/get-testing-inputs.mjs` materializes that commit into `output/testing-inputs/`
(gitignored). Because it is one exact commit, a run is reproducible: the same BloomDesktop commit
always renders the same books against the same reference images.

-   `node build/get-testing-inputs.mjs --check` reports whether `output/testing-inputs/` matches
    the pin, and exits non-zero if it does not. Useful when a run renders something you did not
    expect.
-   If the suite cannot find the inputs, it fails immediately and tells you to run
    `node build/get-testing-inputs.mjs`.

## Using your own checkout of the inputs

Set **`BLOOM_TESTING_INPUTS_DIR`** to the folder that contains `collections/` in your own clone of
bloom-testing-inputs. The suite then renders (and writes screenshots into) that checkout instead of
`output/testing-inputs/`. This is how you edit a test book or accept a new baseline.

-   bash: `BLOOM_TESTING_INPUTS_DIR=/d/bloom-testing-inputs pnpm test`
-   PowerShell: `$env:BLOOM_TESTING_INPUTS_DIR="D:/bloom-testing-inputs"; pnpm test`

# Test failures

If a test fails, look in the `screenshots/` folder of the book that failed — inside the inputs tree
(`output/testing-inputs/collections/basic/<book>/screenshots/`, or your `BLOOM_TESTING_INPUTS_DIR`
checkout) — for `<label>-diff.png` (the differing pixels: blue was darker in the reference, red is
darker now) next to `<label>-reference.png` (the baseline) and `<label>-current.png` (this run).

# Updating a reference image

A baseline now lives in another repository, so accepting a new render takes two commits:

1. Clone https://github.com/BloomBooks/bloom-testing-inputs and point the suite at it with
   `BLOOM_TESTING_INPUTS_DIR` (see above).
2. Re-run the suite. It writes `*-current.png` beside the reference in that checkout. If the new
   render is correct, replace the `*-reference.png` with the `*-current.png` (or delete the
   reference and re-run to regenerate it).
3. Open a pull request in bloom-testing-inputs with the new baselines and merge it.
4. Put the resulting commit SHA in `build/testing-inputs.pin` in **the same BloomDesktop pull
   request as whatever change made the render differ**, so the code change and the baseline it
   requires land together.

The suite always reads and writes screenshots in the source inputs tree, never in the temp copy
Bloom is driven on, so what you edit in step 2 is a real file you can commit.

# Brandings and themes

See the `brandings` and `themes` arrays in `index.spec.ts` for what is exercised.

# Books

Books go in `collections/basic` **in the inputs repository**. That repository's `.gitignore` covers
the files Bloom regenerates on its own (`origami.css`, `branding.css`, `appearance.css`,
`defaultLangStyles.css`, and so on) — do not commit those; Bloom re-supplies them into each book
folder, and leaving them untracked lets these tests catch unexpected changes in what the
distribution copies in. Its `manifest.json` must have an entry for every collection folder; its CI
enforces that.

# Collections

There is only the one collection. Some code anticipates more, but there is no mechanism to relaunch
Bloom on a different one; set branding/theme in the tests instead.

# TODO

-   The diffs are fairly low-resolution.
-   Could test different XMatters.
