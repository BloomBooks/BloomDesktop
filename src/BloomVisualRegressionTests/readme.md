# How to run this successfully

0. `yarn install`
1. Open Bloom with `collections/basic/basic.bloomCollection`` (The auto-run currently hits a bunch of errors)
2. In the terminal, run `yarn test` or `yarn testPatient`
3. To re-run, save the index.spec.ts.

# Test Failures

If a test fails, look in the `screenshots/` folder of the book with the failure. You should see a `<book>-<branding>-diff.png`

# Brandings

Look in the code to see the list of brandings that will be tested

# Books

Put books in `collections/basic`

# Collections

Some of the code wants to allow more than the one collection, but there isn't code to actually re-launch Bloom with particular collections. It just uses whatever it opens with.

# TODO

-   Currently the test suite can run Bloom, but the first run of the tests fail.
-   Currently the diffs are super low-resolution.
-   Currently something prevents committing bloom html, you have bypass that.
-   It might be nice to have a "server" mode where errors in Bloom.exe do not open error UI, but rather cause tests to fail.
-   Each time Bloom is run, it makes new IDs for pages, leading to file diffs when you go to commit that... shouldn't be there.
-   Could test different XMatters
-   This can't handle multiple collections, but maybe that's fine, since you can set the branding in the tests
