// When the large Change Image button shows over a Canvas page's background image. Notion test case
// "Background Image Edit Button" (Test Case ID 340).
//
// The button shows only while the pointer is over the canvas, and then only when the background
// image is the selected element or the canvas holds nothing but its background image. With any
// other element selected it stays hidden, so it does not cover the element being worked on.
//
// The tests are serial because each one starts from the page the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import { addPage, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    deleteCanvasElement,
    dragPaletteItemOntoCanvas,
    expectChangeImageButton,
    getCanvasElementCount,
    hoverOverCanvas,
    movePointerOffPage,
    selectCanvasElement,
} from "../helpers/canvasElements";

test.use({
    collectionSpec: { name: "canvas-background-button", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// Where the pointer rests to hover the canvas: away from the bubble, which goes in the top-left
// part, and away from the canvas's centre, where a click selects the background image.
const kEmptySpot = { xFraction: 0.75, yFraction: 0.7 };
// The background image is the first canvas element on a Canvas page.
const kBackgroundImage = 0;
// The speech bubble the second test drops, by its index among the canvas elements.
let bubble: number;

test.describe("the background image's Change Image button [Test Case ID 340]", () => {
    test("on a canvas with nothing on it, it shows only while the pointer is over the canvas", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Canvas");
        expect(
            await getCanvasElementCount(page),
            "A new Canvas page should hold only its background image.",
        ).toBe(1);

        await movePointerOffPage(page);
        await expectChangeImageButton(
            page,
            false,
            "The button shows with the pointer off the page.",
        );

        await hoverOverCanvas(page, kEmptySpot);
        await expectChangeImageButton(
            page,
            true,
            "The button does not show over a canvas that holds nothing but its background image.",
        );

        await movePointerOffPage(page);
        await expectChangeImageButton(
            page,
            false,
            "The button stays after the pointer left the page.",
        );
    });

    test("with another element selected, it stays hidden", async ({ page }) => {
        bubble = await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.25,
            yFraction: 0.2,
        });
        await selectCanvasElement(page, bubble);

        await hoverOverCanvas(page, kEmptySpot);
        await expectChangeImageButton(
            page,
            false,
            "The button shows while a speech bubble is selected.",
        );
    });

    test("with the background image selected, it shows while the pointer is over the canvas", async ({
        page,
    }) => {
        await selectCanvasElement(page, kBackgroundImage);

        await hoverOverCanvas(page, kEmptySpot);
        await expectChangeImageButton(
            page,
            true,
            "The button does not show while the background image is selected.",
        );

        await movePointerOffPage(page);
        await expectChangeImageButton(
            page,
            false,
            "The button stays after the pointer left the page.",
        );
    });

    test("once the only other element is deleted, it shows again", async ({
        page,
    }) => {
        await selectCanvasElement(page, bubble);
        await hoverOverCanvas(page, kEmptySpot);
        await expectChangeImageButton(
            page,
            false,
            "The button shows while the speech bubble is selected.",
        );

        await deleteCanvasElement(page);
        expect(await getCanvasElementCount(page)).toBe(1);

        await hoverOverCanvas(page, kEmptySpot);
        await expectChangeImageButton(
            page,
            true,
            "The button does not show once the canvas holds nothing but its background image.",
        );
    });
});
