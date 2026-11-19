# Inline image wrap-geometry tests

`pnpm e2e inline-images` (from `src/BloomBrowserUI`).

These tests are about one thing: how text flows around an inline image. They compile the
real `src/content/bookLayout/inlineImages.less`, build a page with one image in one
`bloom-editable`, and read back where every line of text actually landed. Nothing here
talks to a running Bloom, unlike the canvas suite next door, so it runs from a cold
checkout in a couple of seconds.

Layout is exactly the kind of thing unit tests cannot see: jsdom does not lay anything
out, so `inlineImages.test.ts` (vitest) can only check the classes and custom properties
we write. Whether those produce the intended shape on the page is what this suite is for.

## What the contour tests are doing

Contour wrap — text following the image's transparent silhouette rather than its
rectangle — is a committed requirement that is not built yet. `inlineImages.less` already
has the hooks for it (`--inline-image-contour` and `--inline-image-contour-margin`), and
these tests set those properties by hand, which is what the eventual code will do.

They exist because four separate things about CSS shapes turned out to work differently
from what the design assumed, and each was settled by measuring Chromium (see the CONTOUR
WRAP comment in `inlineImages.less`). Rediscovering any of them is expensive, so each is
pinned by a test: the content-box reference, percentage scaling, the gap needing both a
margin and a `shape-margin`, and the middle band staying out of it.

## Adding a test

`helpers/inlineImagePage.ts` builds the page and measures the lines. `measureLines`
returns each line's top/left/right relative to the editable's content box; with the
monospaced host font, a left edge is exact, while a right edge can fall a character short
of the shape (use `measureCharacterWidth` for the tolerance).

If a test seems to pass without proving anything, break it on purpose first: drop
`content-box` from the polygon and four tests should go red. That check caught nothing
this time, but it is the fastest way to notice that the CSS never loaded.
