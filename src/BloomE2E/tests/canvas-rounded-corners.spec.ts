// The Canvas tool's Rounded Corners option. A caption, and a Text Block once it has a background
// color, take it; ticking it gives the element's box rounded corners in the drawing Bloom saves
// with the page (ComicalJS's svg.comical-generated), which is what every reader of the book sees.
// A Text Block with no background has no box to round, so the option is disabled for it.
//
// Each element's saved shape is read before the option is ticked as well as after, so the test
// shows that it can tell a square corner from a rounded one.
//
// Automates "Rounded Corners Caption Option" (Test Case ID 333).

import { type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    canvasElement,
    dragPaletteItemOntoCanvas,
    selectCanvasElement,
    type PaletteItem,
} from "../helpers/canvasElements";
import {
    chooseCanvasElementBackgroundColor,
    readSavedCanvasShapes,
    roundedCornersBox,
    setRoundedCorners,
    type ISavedShape,
} from "../helpers/canvasToolOptions";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: {
        name: "canvas-rounded-corners",
        languages: ["en"],
        // Below Pro, the Canvas tool's options sit behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** A canvas element the test put on a Canvas page of its own. */
interface IPlacedElement {
    pageId: string;
    /** The element's index among the page's canvas elements. */
    index: number;
}

/**
 * How far a shape ComicalJS draws for an element may be from the element's own size, in pixels:
 * the box it draws sits a border's width, and for a Text Block a little padding, outside the text.
 */
const kShapeToElementTolerance = 12;

/**
 * Save the element's page by leaving it for the cover, and wait for the saved drawing of the
 * element to have square corners or rounded ones. Then come back to the element's page.
 *
 * The element's shapes in the drawing are the ones of its size: the drawing also holds the
 * shapes of the page's other canvas elements, such as its background image. Each must be a box:
 * it covers its bounding box's edges away from the corners, which an ellipse does not. Square
 * means each also covers all four corners of its bounding box; rounded means none covers any.
 */
async function expectSavedCorners(
    page: Page,
    bookFolder: string,
    element: IPlacedElement,
    corners: "square" | "rounded",
    why: string,
): Promise<void> {
    const size = await canvasElement(page, element.index).evaluate((e) => ({
        width: (e as HTMLElement).offsetWidth,
        height: (e as HTMLElement).offsetHeight,
    }));
    const cover = (await getPages(page))[0];
    await goToPage(page, cover.id);
    // What the element's saved shapes say, with every saved shape, so a failure shows what was
    // saved.
    const describeSavedCorners = async () => {
        const shapes: ISavedShape[] = await readSavedCanvasShapes(
            page,
            bookFolder,
            element.pageId,
        );
        const elementShapes = shapes.filter(
            (s) =>
                Math.abs(s.width - size.width) <= kShapeToElementTolerance &&
                Math.abs(s.height - size.height) <= kShapeToElementTolerance,
        );
        let found: string;
        if (elementShapes.length === 0)
            found = "no shape of the element's size";
        else if (elementShapes.some((s) => s.edgePointsCovered !== 8))
            found = "not a box with straight edges";
        else if (elementShapes.every((s) => s.cornersCovered === 4))
            found = "square";
        else if (elementShapes.every((s) => s.cornersCovered === 0))
            found = "rounded";
        else found = "some corners covered";
        return (
            `${found}; the element is ${size.width}x${size.height}; ` +
            `saved shapes: ${JSON.stringify(shapes)}`
        );
    };
    await expect
        .poll(describeSavedCorners, { timeout: 15000, message: why })
        .toMatch(new RegExp(`^${corners};`));
    await goToPage(page, element.pageId);
}

/**
 * Add a Canvas page, drop `item` on it and select it. Returns the page's id and the element's index
 * on it (a Canvas page starts with its background image's element, so the index is not 0).
 */
async function addCanvasPageWith(
    page: Page,
    item: PaletteItem,
): Promise<IPlacedElement> {
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Canvas");
    const added = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    );
    if (!added) throw new Error("Adding a Canvas page added no content page.");
    await goToPage(page, added.id);
    const index = await dragPaletteItemOntoCanvas(page, item);
    await selectCanvasElement(page, index);
    return { pageId: added.id, index };
}

test("Rounded Corners rounds a caption, and a text block with a background [Test Case ID 333]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        await step("Round the corners of a caption", async () => {
            const caption = await addCanvasPageWith(page, "caption");
            await expectSavedCorners(
                page,
                bookFolder,
                caption,
                "square",
                "A new caption should be saved with square corners.",
            );
            await selectCanvasElement(page, caption.index);
            await setRoundedCorners(page, true);
            await expectSavedCorners(
                page,
                bookFolder,
                caption,
                "rounded",
                "Ticking Rounded Corners did not round the saved caption.",
            );
            await selectCanvasElement(page, caption.index);
            await expect(
                await roundedCornersBox(page),
                "Rounded Corners is not ticked for the caption when the page is shown again.",
            ).toBeChecked();
        });

        await step(
            "Give a text block a background and round its corners",
            async () => {
                const textBlock = await addCanvasPageWith(page, "none");
                await expect(
                    await roundedCornersBox(page),
                    "A text block with no background has no box to round, so Rounded Corners " +
                        "should be disabled.",
                ).toBeDisabled();
                await chooseCanvasElementBackgroundColor(page, "#000000");
                await expectSavedCorners(
                    page,
                    bookFolder,
                    textBlock,
                    "square",
                    "A text block given a background should be saved with square corners.",
                );
                await selectCanvasElement(page, textBlock.index);
                await setRoundedCorners(page, true);
                await expectSavedCorners(
                    page,
                    bookFolder,
                    textBlock,
                    "rounded",
                    "Ticking Rounded Corners did not round the saved text block.",
                );
            },
        );

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
