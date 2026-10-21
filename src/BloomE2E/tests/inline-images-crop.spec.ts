// Checks cropping an inline image: a side handle crops it, Flip keeps the same part of the picture
// in view, the Undo button takes a crop back, Reset Image takes the crop away, and the crop is
// saved in every language's copy.
//
// A crop is four custom properties on the wrapper (bookEdit/js/inlineImageCrop.ts), so the code
// that copies a wrapper to the other languages carries it, and the book CSS in inlineImages.less
// draws it. jsdom lays nothing out, so only a real Bloom can show that dragging the handle on
// screen gives the crop the vitest suite computes, and that the text then wraps around the part
// that shows.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    addInlineImage,
    changeInlineImagePicture,
    flipInlineImage,
    getInlineImage,
    getInlineImageRects,
    readSavedInlineImages,
    resetInlineImage,
    selectInlineImage,
} from "../helpers/inlineImages";
import { clickUndoButton } from "../helpers/undo";

test.use({
    collectionSpec: { name: "inline-images-crop", languages: ["en"] },
});

const BLOCK = ".bloom-translationGroup";
const LANG = "en";
const BOOK_TITLE = "Inline Images Crop";

const TEXT =
    "The kingfisher waits on the branch above the pool, still enough that the water forgets " +
    "it is there. It watches the shadows move under the surface. When it drops, it drops " +
    "straight, and the pool closes over the place where it went in.";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

interface ICrop {
    top: number;
    right: number;
    bottom: number;
    left: number;
}

// The crop of every language's copy of the image, as the page being edited has it.
async function getCropInEveryLanguage(
    page: import("@playwright/test").Page,
    id: string,
): Promise<{ languageTag: string; crop: ICrop }[]> {
    return editablePageFrame(page).evaluate(
        ({ group, imageId }) =>
            Array.from(
                document.querySelectorAll(
                    `${group} > .bloom-editable > [data-bloom-inline-image-id="${imageId}"]`,
                ),
            ).map((wrapper) => {
                const style = (wrapper as HTMLElement).style;
                const read = (side: string) =>
                    parseFloat(
                        style.getPropertyValue(`--inline-image-crop-${side}`),
                    ) || 0;
                return {
                    languageTag:
                        wrapper.parentElement?.getAttribute("lang") ?? "",
                    crop: {
                        top: read("top"),
                        right: read("right"),
                        bottom: read("bottom"),
                        left: read("left"),
                    },
                };
            }),
        { group: BLOCK, imageId: id },
    );
}

// Drags the middle of one side handle of the selected image toward the middle of the picture.
async function dragCropHandleInward(
    page: import("@playwright/test").Page,
    id: string,
    side: "n" | "e" | "s" | "w",
    pixels: number,
): Promise<void> {
    await selectInlineImage(page, BLOCK, LANG, id);
    const handle = editablePageFrame(page)
        .locator(
            `${BLOCK} > .bloom-editable[lang="${LANG}"] > [data-bloom-inline-image-id="${id}"] .bloom-ui-inlineImage-side-handle-${side}`,
        )
        .first();
    await handle.waitFor({ state: "visible", timeout: 30000 });
    const box = await handle.boundingBox({ timeout: 30000 });
    if (!box)
        throw new Error(
            `The ${side} crop handle of inline image ${id} has no box, so there is nowhere to drag from.`,
        );
    const inward = { n: [0, 1], s: [0, -1], w: [1, 0], e: [-1, 0] }[side];
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + inward[0] * pixels, y + inward[1] * pixels, {
        steps: 12,
    });
    await page.mouse.up();
}

test("a side handle crops an inline image in every language, Flip keeps the crop, and Reset Image removes it [Test Case ID 815]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, BLOCK, LANG, TEXT);
    const bookFolder = await findBookFolder(page, BOOK_TITLE);
    const imageId = await addInlineImage(page, BLOCK, LANG);
    await changeInlineImagePicture(
        page,
        BLOCK,
        LANG,
        imageId,
        fixtureImage("house-landscape.png"),
    );
    const before = await getInlineImage(page, BLOCK, LANG, imageId);
    const pictureBefore = (
        await getInlineImageRects(page, BLOCK, LANG, imageId)
    ).picture;
    const copies = await getCropInEveryLanguage(page, imageId);
    expect(
        copies.length,
        "The group holds fewer than two copies of the picture, so this test cannot see a copy " +
            "being left behind.",
    ).toBeGreaterThan(1);
    expect(copies.map((copy) => copy.crop.right)).toEqual(copies.map(() => 0));

    // The action under test: drag the right-hand crop handle a quarter of the way in.
    await dragCropHandleInward(
        page,
        imageId,
        "e",
        Math.round(pictureBefore.width / 4),
    );

    await expect
        .poll(
            async () =>
                (await getCropInEveryLanguage(page, imageId))[0].crop.right,
            {
                timeout: 30000,
                message:
                    "Dragging the right-hand crop handle did not crop the picture.",
            },
        )
        .toBeGreaterThan(15);
    for (const copy of await getCropInEveryLanguage(page, imageId)) {
        expect(
            copy.crop.right,
            `The "${copy.languageTag}" copy did not get the crop.`,
        ).toBeCloseTo(25, -1);
        expect(copy.crop.left).toBe(0);
    }
    const after = await getInlineImage(page, BLOCK, LANG, imageId);
    // The picture stays the same size, so the part that shows is narrower, and the text wraps
    // around a box of that new shape.
    expect(after.widthPercent).toBeLessThan(before.widthPercent);
    expect(after.aspectRatio).not.toBe(before.aspectRatio);
    const pictureAfter = (await getInlineImageRects(page, BLOCK, LANG, imageId))
        .picture;
    expect(pictureAfter.height).toBeCloseTo(pictureBefore.height, 0);

    // The action under test: Flip. The same part of the picture stays in view, mirrored, so
    // the cut moves to the other side.
    await flipInlineImage(page, BLOCK, LANG, imageId, "horizontal");
    for (const copy of await getCropInEveryLanguage(page, imageId)) {
        expect(copy.crop.right).toBe(0);
        expect(copy.crop.left).toBeCloseTo(25, -1);
    }

    // The action under test: the Undo button takes back the flip, which puts the cut back on the
    // right.
    await clickUndoButton(page);
    await expect
        .poll(
            async () =>
                (await getCropInEveryLanguage(page, imageId))[0].crop.left,
            {
                timeout: 30000,
                message:
                    "Undo did not take back the flip of the cropped picture.",
            },
        )
        .toBe(0);

    // Leaving the page saves it, and the crop is in the saved file for every language.
    const pages = await getPages(page);
    await goToPage(page, pages[0].id);
    const saved = (await readSavedInlineImages(page, bookFolder)).filter(
        (one) => one.id === imageId,
    );
    expect(saved.length).toBe(copies.length);
    for (const one of saved)
        expect(
            one.style,
            `The saved "${one.languageTag}" copy lost its crop.`,
        ).toContain("--inline-image-crop-right");
    await goToPage(page, contentPage.id);

    // The action under test: Reset Image, which shows the whole picture again.
    // resetInlineImage waits for the flip to go, and the Undo above already took the flip back,
    // so this waits for the crop itself.
    await resetInlineImage(page, BLOCK, LANG, imageId);
    await expect
        .poll(
            async () =>
                (await getCropInEveryLanguage(page, imageId)).map(
                    (copy) => copy.crop,
                ),
            {
                timeout: 30000,
                message:
                    "Reset Image did not take the crop away in every language.",
            },
        )
        .toEqual(copies.map(() => ({ top: 0, right: 0, bottom: 0, left: 0 })));
    const reset = await getInlineImage(page, BLOCK, LANG, imageId);
    expect(reset.aspectRatio).toBe(before.aspectRatio);
});
