// What happens when the person lets go of the mouse somewhere other than the page.
//
// WHAT THIS IS ABOUT. A drag of an inline image listens for pointermove and pointerup on the
// PAGE IFRAME's document (addPointerListeners), and nothing in Bloom calls setPointerCapture. A
// person dragging a picture upward runs out of page long before they run out of gesture: above
// the page is Bloom's own toolbar, which is the top-level document, not the page's.
//
// WHAT THIS TEST MEASURED. The gesture survives it. Dragged to (8, 8) in the top-level document,
// the drag is still under way -- so the pointermoves reached the page's document even though the
// pointer was over a different one -- and the release there ends it, commits, and syncs the
// copies. That is Chromium's mouse capture: while a button is down, the events go to the frame
// where the press happened, whatever they are over. So the page iframe boundary, which is the one
// boundary a person can cross with the button held inside Bloom's window, needs no
// setPointerCapture.
//
// WHAT IT DOES NOT COVER, and cannot: a release outside the WebView2 window altogether. Playwright
// drives the mouse through the browser, so there is no "outside the window" for it to release in
// (AUTOMATION-DEBT.md: "WinForms surfaces cannot be driven"). What would be left behind there is
// worth knowing: dragState still set, the body still carrying the dragging class, and the 50ms
// edge-scroll interval still re-applying the move from a pointer position nothing updates.
//
// So this test is here as the guard on the boundary that IS reachable. If a later change moves
// these listeners to the wrapper, or to the top-level document, this is what says so.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    addInlineImage,
    beginInlineImageDrag,
    changeInlineImagePicture,
    dragInlineImageToDock,
    getInlineImage,
    getInlineImages,
    inlineImageDragIsInProgress,
    moveInlineImageDragTo,
    scrollBlockToTop,
} from "../helpers/inlineImages";

test.use({
    collectionSpec: {
        name: "inline-images-release-outside",
        languages: ["en"],
    },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets it " +
    "is there. It watches the shadows move under the surface. When it drops, it drops straight, " +
    "and the pool closes over the place where it went in.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

test("letting go of the mouse outside the page still ends the drag [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(
        page,
        ".bookTitle",
        "en",
        "Inline Images Release Outside",
    );
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);

    const imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("bird.png"),
    );
    await scrollBlockToTop(page, BLOCK, LANG);
    await dragInlineImageToDock(page, BLOCK, LANG, imageId, "middle");

    // THE ACTION UNDER TEST. Take hold of the picture, drag it clear of the page iframe -- (8, 8)
    // in the top-level document is Bloom's own chrome, well above where the page starts -- and let
    // go there. The assertion in between is the interesting one: it says the moves are still
    // arriving at the page's document while the pointer is over a different one.
    await beginInlineImageDrag(page, BLOCK, LANG, imageId);
    await moveInlineImageDragTo(page, { x: 8, y: 8 });
    expect(
        await inlineImageDragIsInProgress(page),
        "Dragging the pointer out of the page iframe ended the drag on its own. While a mouse " +
            "button is down Chromium delivers the events to the frame where the press happened, " +
            "so the moves should still be reaching the page's document.",
    ).toBe(true);
    await page.mouse.up();

    await expect
        .poll(() => inlineImageDragIsInProgress(page), {
            message:
                "Letting go of the mouse outside the page left the drag running: the page still " +
                "has the dragging class, so dragState is still set, the context controls are " +
                "still in their moving state, and the 50ms edge-scroll interval is still " +
                "re-applying the move. Nothing would ever end it, and the person's next click " +
                "could reach a save. The pointerup needs to arrive at the document the " +
                "listeners are on, which is the page's, not the one the pointer is over.",
            timeout: 10000,
        })
        .toBe(false);

    // And the gesture has to have finished properly, not just stopped: the change is committed to
    // every copy of the picture. The copies are only ever synced at the end of a gesture, so if
    // they agree, the end ran.
    const settled = await getInlineImage(page, BLOCK, LANG, imageId);
    const copies = (await getInlineImages(page, BLOCK))
        .flatMap((block) => block.images)
        .filter((image) => image.id === imageId);
    for (const copy of copies)
        expect(
            copy.offsetPx,
            `The "${copy.languageTag}" copy is at ${copy.offsetPx}px while the "${LANG}" copy is ` +
                `at ${settled.offsetPx}px. The copies are synced at the end of a gesture, so ` +
                `disagreeing copies mean the end never ran.`,
        ).toBe(settled.offsetPx);

    // And the picture stayed in the block, wherever the pointer went. A pointer above the page is
    // asking for the top of the text, not for somewhere off it.
    expect(
        settled.offsetPx,
        `The picture ended up at ${settled.offsetPx}px, which is above the start of its block.`,
    ).toBeGreaterThanOrEqual(0);
});
