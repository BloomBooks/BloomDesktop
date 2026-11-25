// A canvas element keeps its background color when its Element Type changes. The Canvas tool
// rebuilds the element's ComicalJS spec when a new type is chosen (handleStyleChanged in
// CanvasToolControls.tsx), and only a change to Caption is meant to touch the background.
//
// The test reads what Bloom saved: the element's data-bubble, which holds its type and background,
// and the drawing ComicalJS saved for the page (svg.comical-generated), which is what every reader
// of the book sees. Both are read before the type changes as well as after, so the test shows that
// it can see the red background it then checks for.
//
// Automates "Bubble Color Survives Style Change" (Test Case ID 339).

import { type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import {
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
} from "../helpers/canvasElements";
import {
    typeCanvasElementBackgroundColor,
    chooseCanvasElementType,
    getCanvasElementTypeShown,
    readSavedBubbleSpecs,
    readSavedCanvasShapes,
} from "../helpers/canvasToolOptions";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { cssColorToHex } from "../helpers/cssValues";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: {
        name: "canvas-bubble-color-style-change",
        languages: ["en"],
        // Below Pro, the Canvas tool's options sit behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** The red of Bloom's text color palette (bloomPalette.ts), typed in, since the background palette has no red. */
const kRed = "#ff1616";

/** The colors of the filled shapes in a saved drawing, as "#rrggbb" where they are colors. */
function fillColors(fills: string[]): string[] {
    return fills.map((fill) => {
        try {
            return cssColorToHex(fill);
        } catch {
            return fill;
        }
    });
}

/**
 * Save the page by leaving it for the cover, then wait for the saved book to say that the
 * element at `index` has the Element Type `style` and a red background, and that the page's saved
 * drawing has a red shape. Then come back to the page and select the element again.
 */
async function expectSavedRedElement(
    page: Page,
    bookFolder: string,
    pageId: string,
    index: number,
    style: string,
    why: string,
): Promise<void> {
    // Leaving a page saves the last snapshot of it that Bloom received, which is sent a moment
    // after the page last changed; let the change reach Bloom first.
    await waitForSnapshotStreamIdle(page);
    const cover = (await getPages(page))[0];
    await goToPage(page, cover.id);
    const describeSaved = async () => {
        const spec = (await readSavedBubbleSpecs(page, bookFolder, pageId))[
            index
        ];
        const shapes = await readSavedCanvasShapes(page, bookFolder, pageId);
        const fills = fillColors(shapes.map((s) => s.fill));
        return {
            style: spec?.style,
            backgroundColors: (spec?.backgroundColors ?? []).map((c) =>
                c.toLowerCase(),
            ),
            drawingHasRedShape: fills.includes(kRed),
            drawingFills: fills,
        };
    };
    await expect
        .poll(
            async () => {
                const saved = await describeSaved();
                // The fills say what the drawing holds when it has no red shape.
                return saved.drawingHasRedShape
                    ? { ...saved, drawingFills: undefined }
                    : saved;
            },
            { timeout: 15000, message: why },
        )
        .toEqual({
            style,
            backgroundColors: [kRed],
            drawingHasRedShape: true,
            drawingFills: undefined,
        });
    await goToPage(page, pageId);
    await selectCanvasElement(page, index);
}

test("A text block keeps its background color when its Element Type changes to Exclamation [Test Case ID 339]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        let pageId = "";
        let index = -1;

        await step("Drag a Text Block onto a Canvas page", async () => {
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

        await step("Give the text block a red background", async () => {
            await typeCanvasElementBackgroundColor(page, kRed);
            await expectSavedRedElement(
                page,
                bookFolder,
                pageId,
                index,
                "none",
                "The text block was not saved with the red background it was given.",
            );
        });

        await step("Change its Element Type to Exclamation", async () => {
            await chooseCanvasElementType(page, "pointedArcs");
            await expectSavedRedElement(
                page,
                bookFolder,
                pageId,
                index,
                "pointedArcs",
                "After its Element Type changed to Exclamation, the element was not saved as an " +
                    "Exclamation with its red background.",
            );
        });

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
