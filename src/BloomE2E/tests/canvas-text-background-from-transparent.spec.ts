// A Just Text element starts with a transparent background. When a person opens its Background
// Color picker and clicks in the saturation box at the top, the picker leaves transparency behind:
// its alpha slider goes to 100% and every control shows the color clicked, not a transparent one.
// The element then gets that color as its background.
//
// The test checks that the element's background is transparent before the picker opens, reads the
// picker's controls after the click, then OKs the picker and reads what Bloom saved: the element's
// data-bubble and the drawing ComicalJS saved for the page (svg.comical-generated), which is what
// every reader sees. It does not check how the picker looks before the click: it opens showing
// "10000%" and #00000063 there (BL-17224).
//
// Automates "Just Text Background Color" (Test Case ID 324).

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import { type Page } from "@playwright/test";
import {
    canvasElement,
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
} from "../helpers/canvasElements";
import {
    getCanvasElementTypeShown,
    openCanvasElementBackgroundColorPicker,
    readSavedBubbleSpecs,
    readSavedCanvasShapes,
} from "../helpers/canvasToolOptions";
import {
    clickSaturationBox,
    closeColorPickerWith,
    hexToRgba,
    readColorPicker,
    type IRgba,
} from "../helpers/colorPicker";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { cssColorToHex } from "../helpers/cssValues";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: {
        name: "canvas-text-background-from-transparent",
        languages: ["en"],
        // Below Pro, the Canvas tool's options sit behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** Color channels are rounded where the browser and react-color convert between color spaces. */
const kChannelTolerance = 3;
/** Where the saturation box is clicked, in percent of its width and height: a strong, dark color. */
const kClickLeft = 80;
const kClickTop = 20;

/**
 * The color the saturation box gives at a point, from the pure hue it shows behind it: the left
 * edge is white-to-hue (saturation), the top edge full brightness (value), as react-color draws it.
 */
function colorAtSaturationPoint(
    hue: IRgba,
    leftPercent: number,
    topPercent: number,
): IRgba {
    const s = leftPercent / 100;
    const v = 1 - topPercent / 100;
    const channel = (pure: number) => v * (255 - s * (255 - pure));
    return { r: channel(hue.r), g: channel(hue.g), b: channel(hue.b), a: 1 };
}

/**
 * The background colors in the data-bubble of the canvas element at `index` on the page being
 * edited, as ComicalJS keeps them while the page is open.
 */
async function liveBackgroundColors(
    page: Page,
    index: number,
): Promise<string[]> {
    const json = await canvasElement(page, index).getAttribute("data-bubble");
    if (!json) throw new Error(`Canvas element ${index} has no data-bubble.`);
    // Bloom stores the spec with backquotes in place of double quotes.
    const spec = JSON.parse(json.replace(/`/g, '"')) as {
        backgroundColors?: string[];
    };
    return (spec.backgroundColors ?? []).map((c) => c.toLowerCase());
}

/** The largest difference between two colors' red, green and blue channels. */
function channelDistance(a: IRgba, b: IRgba): number {
    return Math.max(
        Math.abs(a.r - b.r),
        Math.abs(a.g - b.g),
        Math.abs(a.b - b.b),
    );
}

test("Clicking the saturation box of a transparent Just Text's background makes it opaque [Test Case ID 324]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        let pageId = "";
        let index = -1;

        await step('Add a "Just Text" to a Canvas page', async () => {
            const before = (await getContentPages(page)).map((p) => p.id);
            await addPage(page, "Canvas");
            const added = (await getContentPages(page)).find(
                (p) => !before.includes(p.id),
            );
            if (!added)
                throw new Error("Adding a Canvas page added no content page.");
            pageId = added.id;
            await goToPage(page, pageId);
            index = await dragPaletteItemOntoCanvas(page, "none");
            await selectCanvasElement(page, index);
            expect(
                await getCanvasElementTypeShown(page),
                "A new Text Block should show the Element Type Just Text.",
            ).toBe("none");
        });

        let made: IRgba = { r: 0, g: 0, b: 0, a: 0 };
        let madeHex = "";
        await step(
            "Open Background Color and click in the top palette box",
            async () => {
                expect(
                    await liveBackgroundColors(page, index),
                    "A new Just Text should start with a transparent background.",
                ).toEqual(["transparent"]);
                const frame =
                    await openCanvasElementBackgroundColorPicker(page);

                await clickSaturationBox(frame, kClickLeft, kClickTop);
                await expect
                    .poll(
                        async () =>
                            (await readColorPicker(frame)).alphaPercentage,
                        {
                            timeout: 15000,
                            message:
                                "After a click in the saturation box, the alpha slider should show 100% (0% transparency).",
                        },
                    )
                    .toBe(100);
                const after = await readColorPicker(frame);
                const expected = colorAtSaturationPoint(
                    after.saturationHue,
                    kClickLeft,
                    kClickTop,
                );
                // The hex box may show the alpha as a last pair of digits; it must then be opaque.
                expect(
                    after.hex,
                    "The hex box should show an opaque color.",
                ).toMatch(/^#[0-9a-f]{6}(ff)?$/i);
                madeHex = after.hex.slice(0, 7).toLowerCase();
                made = hexToRgba(madeHex);
                expect(
                    channelDistance(made, expected),
                    `The hex box shows ${after.hex}, not the color clicked in the saturation box (${JSON.stringify(expected)}).`,
                ).toBeLessThanOrEqual(kChannelTolerance);
                expect(
                    after.alphaPointer,
                    "The alpha slider's pointer should be at its right end.",
                ).toBe(100);
                expect(
                    after.sample.a,
                    "The picker's sample should be opaque.",
                ).toBe(1);
                expect(
                    channelDistance(after.sample, made),
                    `The picker's sample shows ${JSON.stringify(after.sample)}, not the hex box's ${madeHex}.`,
                ).toBeLessThanOrEqual(kChannelTolerance);
                await closeColorPickerWith(frame, "OK");
            },
        );

        await step(
            "The Just Text is saved with that color as an opaque background",
            async () => {
                // Leaving a page saves the last snapshot of it that Bloom received, which is sent a
                // moment after the page last changed; let the change reach Bloom first.
                await waitForSnapshotStreamIdle(page);
                const cover = (await getPages(page))[0];
                await goToPage(page, cover.id);
                const describeSaved = async () => {
                    const spec = (
                        await readSavedBubbleSpecs(page, bookFolder, pageId)
                    )[index];
                    const shapes = await readSavedCanvasShapes(
                        page,
                        bookFolder,
                        pageId,
                    );
                    // Each filled shape's color and how opaque it is drawn, e.g. "#cd2929 at 1".
                    const fills = shapes.map((s) => {
                        let color = s.fill;
                        try {
                            color = cssColorToHex(s.fill);
                        } catch {
                            // Keep the fill as saved, to show it in a failure message.
                        }
                        return `${color} at ${s.fillOpacity}`;
                    });
                    const opaqueFill = `${madeHex} at 1`;
                    return {
                        backgroundColors: (spec?.backgroundColors ?? []).map(
                            (c) => c.toLowerCase(),
                        ),
                        drawingFilledWithItOpaque: fills.includes(opaqueFill),
                        // The fills say what the drawing holds when it is not what was expected.
                        fills: fills.includes(opaqueFill) ? undefined : fills,
                    };
                };
                await expect
                    .poll(describeSaved, {
                        timeout: 15000,
                        message:
                            "The Just Text was not saved with the opaque color made in the picker.",
                    })
                    .toEqual({
                        backgroundColors: [madeHex],
                        drawingFilledWithItOpaque: true,
                        fills: undefined,
                    });
            },
        );

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
