// Checks where a corner drag stops making an inline image bigger, on a real page of each shape.
//
// A resize is limited two ways (continueResize in bookEdit/js/inlineImageInteractions.ts): the
// picture can be as wide as the text, and in a block that fitted its text when the resize began,
// a size that pushes anything out of the block is not kept. Which limit a drag meets first
// depends on the shape of the block. On a landscape page the block is wide and short, so the
// picture reaches the bottom of the block long before it is as wide as the text. On a portrait
// page it reaches the full width first. BL-17078 reported both going wrong: on a landscape page
// the picture could be stretched past the bottom of the page, and on a portrait page it stopped
// a little short of the edge of the text.
//
// The vitest browser suite beside the code (inlineImageResize.browser.spec.ts) covers each corner,
// each dock, and blocks that show several languages, against a text block it builds itself. This
// file is separate from it because the limits depend on how Bloom really lays a page out: the
// block's size, its padding, and the rules that let an editable scroll. It is separate from
// inline-images.spec.ts because it needs a page with little text, at two page sizes.

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
    changeInlineImagePicture,
    dragInlineImageToDock,
    getBlockLineHeightPx,
    getBlockScrollOverflowPx,
    getBlockSidewaysOverflowPx,
    getInlineImage,
    getInlineImageInEveryLanguage,
    getInlineImageRects,
    getRoomBelowInlineImagePx,
    resizeInlineImagePastBlockEdge,
    scrollBlockToTop,
} from "../helpers/inlineImages";
import { right, sameEdge } from "../helpers/geometry";
import { setPageSize } from "../helpers/pageSize";

test.use({
    collectionSpec: { name: "inline-images-resize", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

const BLOCK = ".bloom-translationGroup";
const LANG = "en";

// One line of text, so that the block has room to spare at both page sizes and the picture
// itself, not the text, is what meets the edge of the block.
const TEXT = "The kingfisher waits on the branch above the pool.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

// The image both tests resize. Set by the first test.
let imageId: string;

test.describe("how big a corner drag can make an inline image [Test Case ID 815]", () => {
    test("on a landscape page the picture stops at the bottom of its text block", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", "Inline Images Resize");
        await addPage(page, "Just Text");
        const [contentPage] = await getContentPages(page);
        await goToPage(page, contentPage.id);
        await typeInGroup(page, BLOCK, LANG, TEXT);
        imageId = await addInlineImage(page, BLOCK, LANG);
        // bird.png is a little taller than it is wide, so at the full width of a landscape block
        // it would be far taller than the block.
        await changeInlineImagePicture(
            page,
            BLOCK,
            LANG,
            imageId,
            fixtureImage("bird.png"),
        );
        await dragInlineImageToDock(page, BLOCK, LANG, imageId, "left");
        await setPageSize(page, "A5Landscape");
        await scrollBlockToTop(page, BLOCK, LANG);

        // Check the starting state: the block shows all its text, with room below the picture.
        // A resize is only limited in a block that fitted its text when the resize began.
        const before = await getInlineImage(page, BLOCK, LANG, imageId);
        const lineHeightPx = await getBlockLineHeightPx(page, BLOCK, LANG);
        expect(
            await getBlockScrollOverflowPx(page, BLOCK, LANG),
            "The block already held more than it could show before the resize, so the resize would not be limited.",
        ).toBeLessThanOrEqual(0);
        expect(
            await getRoomBelowInlineImagePx(page, BLOCK, LANG, imageId),
            "The picture already reached the bottom of the block before the resize, so there is nothing for the resize to do.",
        ).toBeGreaterThan(2 * lineHeightPx);

        // THE ACTION UNDER TEST: drag the lower right corner out past the right side of the
        // text, which asks for a picture wider than the text.
        const widthPercent = await resizeInlineImagePastBlockEdge(
            page,
            BLOCK,
            LANG,
            imageId,
            "se",
        );

        expect(widthPercent).toBeGreaterThan(before.widthPercent);
        expect(
            widthPercent,
            "The picture became as wide as the text, which on this page makes it far taller than its block.",
        ).toBeLessThan(100);
        // Nothing goes past the bottom of the block: all its content still shows, the picture
        // included. The one pixel is the rounding the resize itself allows
        // (shouldRevertInlineImageMove).
        expect(
            await getBlockScrollOverflowPx(page, BLOCK, LANG),
            "The resized picture goes past the bottom of its text block.",
        ).toBeLessThanOrEqual(1);
        // And the picture grew until it reached the bottom: less than a line of room is left
        // below it.
        expect(
            await getRoomBelowInlineImagePx(page, BLOCK, LANG, imageId),
            "The picture stopped growing well before it reached the bottom of its text block.",
        ).toBeLessThan(lineHeightPx);
        const copies = await getInlineImageInEveryLanguage(
            page,
            BLOCK,
            imageId,
        );
        for (const copy of copies) expect(copy.widthPercent).toBe(widthPercent);
    });

    test("on a portrait page the picture reaches the full width of the text", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await setPageSize(page, "A5Portrait");
        await scrollBlockToTop(page, BLOCK, LANG);
        // Check the starting state: the picture is narrower than the text, docked left, and the
        // block shows all its text.
        const before = await getInlineImage(page, BLOCK, LANG, imageId);
        expect(before.dock).toBe("left");
        expect(before.widthPercent).toBeLessThan(100);
        expect(
            await getBlockScrollOverflowPx(page, BLOCK, LANG),
            "The block already held more than it could show before the resize.",
        ).toBeLessThanOrEqual(0);

        // THE ACTION UNDER TEST: the same drag, on a page whose block is tall enough for a
        // picture as wide as the text.
        const widthPercent = await resizeInlineImagePastBlockEdge(
            page,
            BLOCK,
            LANG,
            imageId,
            "se",
        );

        expect(widthPercent).toBe(100);
        // The picture lines up with both sides of the text.
        const { block, picture } = await getInlineImageRects(
            page,
            BLOCK,
            LANG,
            imageId,
        );
        expect(
            sameEdge(picture.x, block.x),
            `The picture's left side (${picture.x}) is not at the left side of the text (${block.x}).`,
        ).toBe(true);
        expect(
            sameEdge(right(picture), right(block)),
            `The picture's right side (${right(picture)}) is not at the right side of the text (${right(block)}).`,
        ).toBe(true);
        // Nothing reaches past the side of the block, including the gap a left-docked picture
        // keeps between itself and the text beside it, and nothing is pushed out of the bottom.
        // Either would make Bloom mark the block as overflowing, in red.
        expect(
            await getBlockSidewaysOverflowPx(page, BLOCK, LANG),
            "Something in the block reaches past its side.",
        ).toBeLessThanOrEqual(0);
        expect(
            await getBlockScrollOverflowPx(page, BLOCK, LANG),
            "The full-width picture pushed part of the block's content out of the block.",
        ).toBeLessThanOrEqual(1);
    });
});
