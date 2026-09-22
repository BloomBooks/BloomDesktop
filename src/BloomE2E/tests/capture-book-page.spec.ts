// Proves the capture helper works against a real WebView2, on a page taller than the window.
//
// This is the test for helpers/screenshot.ts rather than a test of a Bloom behavior. It exists
// because the driver-level footgun it absorbs is invisible from the outside: the obvious CDP route
// to this image (captureBeyondViewport) hangs with no error, and nothing else in the suite would
// notice if the safe pattern stopped working. See AUTOMATION-DEBT.md, "Driver-level CDP footguns
// that the automation library must absorb".
//
// This test has no "[Test Case ID N]" tag because it covers the automation library, not a case in
// the Notion test inventory.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import { selectBook } from "../helpers/collection";
import { getPages } from "../helpers/bookMaking";
import { captureCurrentBookPage } from "../helpers/screenshot";
import { switchTab } from "../helpers/workspace";

test.use({ collectionName: "basic" });

test("capturing the cover page gives a PNG the size of the page", async ({
    page,
    bloomApp,
}) => {
    await selectBook(page, Path.join(bloomApp.collectionDir, "A5 Portrait"));
    await switchTab(page, "edit");

    // Sanity check: the Edit tab opens on the cover, so there is a page to capture and it is the
    // one this test says it captures.
    const pages = await getPages(page);
    expect(pages.length).toBeGreaterThan(0);

    const image = await captureCurrentBookPage(page);

    // Non-empty, and a PNG: readPngSize inside the helper already rejects anything else, so a
    // plausible byte count is what is left to check.
    expect(image.png.length).toBeGreaterThan(1000);

    // The image is the page, not the window: its pixels match the element's own box. CDP rounds
    // the clip, so allow a pixel either way.
    expect(image.width).toBeGreaterThan(100);
    expect(image.height).toBeGreaterThan(100);
    expect(Math.abs(image.width - image.elementWidth)).toBeLessThanOrEqual(1);
    expect(Math.abs(image.height - image.elementHeight)).toBeLessThanOrEqual(1);

    // A5 Portrait is taller than it is wide, which is the case that needs the window override.
    expect(image.height).toBeGreaterThan(image.width);

    // Put the workspace back on the collection tab. The launched Bloom is worker-scoped, so every
    // test with these same options shares it: a test that ends on the Edit tab makes the next one
    // start there. See AUTOMATION-DEBT.md, "One test's tab is the next test's starting state".
    await switchTab(page, "collection");
});
