// Checks what happens when the person lets go of the mouse somewhere other than the page.
//
// A drag of an inline image listens for pointermove and pointerup on the page iframe's document
// (addPointerListeners), and nothing in Bloom calls setPointerCapture. A person dragging a
// picture upward can easily move the pointer past the top of the page, onto Bloom's own
// toolbar, which belongs to the top-level document.
//
// The drag keeps working when that happens. With the pointer moved to (8, 8) in the top-level
// document, the drag is still under way, so the pointermove events still reached the page's
// document. Letting go there ends the drag, saves the change, and syncs the copies in the other
// languages. This works because, while a mouse button is down, Chromium sends the events to the
// frame where the press happened, whatever the pointer is over. So crossing the edge of the page
// iframe, the one edge a person can cross while staying inside Bloom's window, needs no
// setPointerCapture.
//
// This test cannot cover letting go outside the WebView2 window altogether. Playwright drives the
// mouse through the browser, so it has nowhere outside the window to let go (AUTOMATION-DEBT.md:
// "WinForms surfaces cannot be driven"). If that happened, dragState would stay set, the body
// would keep the dragging class, and the 50ms interval that scrolls at the edge would keep
// applying the move from a pointer position that nothing updates.
//
// So this test checks the edge a person can reach. If a later change moves these listeners to the
// wrapper or to the top-level document, this test fails.

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

    // The action under test. Press on the picture, drag the pointer out of the page iframe to
    // (8, 8) in the top-level document, which is Bloom's own toolbar well above the page, and let
    // go there. The check in between confirms that the moves still reach the page's document
    // while the pointer is over a different one.
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

    // The drag must also have finished properly, with the change saved to every copy of the
    // picture. The copies are synced only when a drag ends, so if they agree, the code that ends
    // the drag ran.
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

    // The picture must stay in the block wherever the pointer went. A pointer above the page means
    // the person wants the picture at the top of the text.
    expect(
        settled.offsetPx,
        `The picture ended up at ${settled.offsetPx}px, which is above the start of its block.`,
    ).toBeGreaterThanOrEqual(0);
});
