// Pasting a short, wide picture onto a Canvas page with Ctrl+V gives the new picture a box of the
// picture's own shape. Automates "Paste Image Aspect Ratio" (Test Case ID 772).
//
// Where the picture goes depends on what is selected (CanvasElementClipboard
// .finishPasteImageFromClipboard). On an empty canvas it becomes the canvas's background, which
// Bloom fits to the picture's shape (adjustBackgroundImageSize). Once the canvas holds a picture,
// a paste with nothing selected adds a new picture element: Bloom first makes its box a third of
// the canvas wide and a third of it high, the canvas's own shape, and then fits the box to the
// picture (adjustContainerAspectRatio in CanvasElementManager.ts). The test pastes both ways. A
// picture four times as wide as it is tall shows plainly whether the fitting happened.
//
// Clicking a canvas that holds a background picture selects that picture, and a paste then
// replaces it, so the second paste follows a click on the page beside the canvas, which leaves
// nothing selected.
//
// The picture goes on Bloom's clipboard (helpers/clipboard.ts), and the paste is a real Ctrl+V:
// Bloom's keydown handler asks its C# for the clipboard, so the Windows clipboard plays no part.

import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    canvasElements,
    getCanvasRect,
    getRect,
} from "../helpers/canvasElements";
import { copyToClipboard } from "../helpers/clipboard";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { pressKey } from "../helpers/keys";
import { makeJpegFile, readImageFileSize } from "../helpers/images";
import { realClickAt } from "../helpers/realClick";

test.use({
    collectionSpec: {
        name: "canvas-paste-image-aspect-ratio",
        languages: ["en"],
        // Below Pro, pasting a picture onto a canvas shows a subscription notice instead.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const WIDE_WIDTH = 1200;
const WIDE_HEIGHT = 300;

interface IPictureBox {
    /** The canvas element's size on screen. */
    box: { width: number; height: number };
    /** Its <img>'s size on screen. */
    img: { width: number; height: number };
    /** The picture file's own size, as the browser loaded it. */
    natural: { width: number; height: number };
}

async function getPictureBox(element: Locator): Promise<IPictureBox> {
    return element.evaluate((el) => {
        const img = el.querySelector("img") as HTMLImageElement;
        const box = el.getBoundingClientRect();
        const imgBox = img.getBoundingClientRect();
        return {
            box: { width: box.width, height: box.height },
            img: { width: imgBox.width, height: imgBox.height },
            natural: { width: img.naturalWidth, height: img.naturalHeight },
        };
    });
}

/**
 * Put `file` on Bloom's clipboard, click, and press Ctrl+V. The click lands in the middle of the
 * canvas, or, for "beside the canvas", halfway between the page's left edge and the canvas's.
 */
async function pastePicture(
    page: Page,
    file: string,
    clickWhere: "on the canvas" | "beside the canvas",
): Promise<void> {
    await copyToClipboard(page, { picture: file });
    const canvasRect = await getCanvasRect(page);
    if (clickWhere === "on the canvas") {
        await realClickAt(
            page,
            canvasRect.x + canvasRect.width / 2,
            canvasRect.y + canvasRect.height / 2,
        );
    } else {
        const pageRect = await getRect(
            editablePageFrame(page).locator(".bloom-page").first(),
            "the page being edited",
        );
        expect(
            canvasRect.x - pageRect.x,
            "test setup: the page has a margin to the left of its canvas to click in",
        ).toBeGreaterThan(6);
        await realClickAt(
            page,
            (pageRect.x + canvasRect.x) / 2,
            canvasRect.y + canvasRect.height / 2,
        );
        await expect(
            editablePageFrame(page).locator(
                '.bloom-canvas-element[data-bloom-active="true"]',
            ),
            "Clicking the page beside the canvas left a canvas element selected.",
        ).toHaveCount(0, { timeout: 15000 });
    }
    await pressKey(page, "Control+v");
}

/** Expect a box to have the shape of a picture `ratio` times as wide as it is tall. */
function expectShape(
    size: { width: number; height: number },
    ratio: number,
    what: string,
): void {
    // A pixel either way in the height, as Bloom rounds the box to whole pixels.
    const ratioIfOnePixelTaller = size.width / (size.height + 1);
    const ratioIfOnePixelShorter = size.width / (size.height - 1);
    expect(
        ratio,
        `${what} is ${size.width} x ${size.height} on screen, which is not the shape of the ` +
            `${ratio}:1 picture it holds.`,
    ).toBeGreaterThanOrEqual(ratioIfOnePixelTaller);
    expect(
        ratio,
        `${what} is ${size.width} x ${size.height} on screen, which is not the shape of the ` +
            `${ratio}:1 picture it holds.`,
    ).toBeLessThanOrEqual(ratioIfOnePixelShorter);
}

test("a short, wide picture pasted onto a canvas keeps its shape [Test Case ID 772]", async ({
    page,
    step,
}, testInfo) => {
    test.setTimeout(300000);
    const wideFile = testInfo.outputPath("wide-picture.jpg");
    const ratio = WIDE_WIDTH / WIDE_HEIGHT;

    await step("Go to a new Canvas page", async () => {
        await makeBookFromTemplate(page, "Basic Book");
        await makeJpegFile(page, wideFile, WIDE_WIDTH, WIDE_HEIGHT);
        expect(
            readImageFileSize(wideFile),
            "test setup: the picture to paste has the size it was made with",
        ).toEqual({ width: WIDE_WIDTH, height: WIDE_HEIGHT });
        await addPage(page, "Canvas");
        const [canvasPage] = await getContentPages(page);
        await goToPage(page, canvasPage.id);
    });

    // On an empty canvas the picture becomes the canvas's background.
    await step("Paste the wide picture onto the empty canvas", async () => {
        const before = await canvasElements(page).count();
        await pastePicture(page, wideFile, "on the canvas");
        const background = editablePageFrame(page)
            .locator(".bloom-canvas-element.bloom-backgroundImage")
            .first();
        await expect
            .poll(async () => (await getPictureBox(background)).natural.width, {
                message:
                    "The pasted picture never loaded as the canvas's background.",
                timeout: 30000,
            })
            .toBe(WIDE_WIDTH);
        expect(
            await canvasElements(page).count(),
            "Pasting onto the empty canvas added an element instead of filling its background.",
        ).toBe(before);
        // Waits only until Bloom has fitted the box at all; expectShape below is the exact check.
        await expect
            .poll(
                async () => {
                    const shown = await getPictureBox(background);
                    return Math.abs(shown.box.width / shown.box.height - ratio);
                },
                {
                    message:
                        "The background picture's box never took the picture's shape.",
                    timeout: 15000,
                },
            )
            .toBeLessThan(0.5);
        const shown = await getPictureBox(background);
        expectShape(shown.box, ratio, "The background picture's box");
        expectShape(shown.img, ratio, "The background picture");
    });

    // With a picture already on the canvas and nothing selected, the paste adds a new picture.
    await step("Paste the wide picture again, as a new picture", async () => {
        const before = await canvasElements(page).count();
        await pastePicture(page, wideFile, "beside the canvas");
        await expect(
            canvasElements(page),
            "Pasting onto a canvas that holds a picture did not add a new picture element.",
        ).toHaveCount(before + 1, { timeout: 30000 });
        const added = canvasElements(page).nth(before);
        await expect
            .poll(async () => (await getPictureBox(added)).natural.width, {
                message: "The pasted picture never loaded in its new element.",
                timeout: 30000,
            })
            .toBe(WIDE_WIDTH);
        // Bloom makes the box the canvas's shape first and then fits it to the picture. This waits
        // only until the box is fitted at all; expectShape below is the exact check.
        await expect
            .poll(
                async () => {
                    const shown = await getPictureBox(added);
                    return Math.abs(shown.box.width / shown.box.height - ratio);
                },
                {
                    message:
                        "The new picture's box never took the picture's shape.",
                    timeout: 15000,
                },
            )
            .toBeLessThan(0.5);
        const shown = await getPictureBox(added);
        expectShape(shown.box, ratio, "The new picture's box");
        expectShape(shown.img, ratio, "The new picture");
        // The picture fills its box: no bars above and below it, nothing cut off.
        expect(
            Math.abs(shown.img.width - shown.box.width),
            `The picture is ${shown.img.width} wide in a box ${shown.box.width} wide.`,
        ).toBeLessThanOrEqual(1.5);
        expect(
            Math.abs(shown.img.height - shown.box.height),
            `The picture is ${shown.img.height} high in a box ${shown.box.height} high.`,
        ).toBeLessThanOrEqual(1.5);
    });
});
