// Duplicating a text block on a Canvas page with the Duplicate button of its toolbar. The copy is
// the same size as the box it was copied from, whether that box's height is automatic or not, and a
// copy of a box with Auto Height turned off has it off too (BL-13931).
//
// Automates "Auto Height After Duplicate" (Test Case ID 739).

import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { addPage, makeBookFromTemplate } from "../helpers/bookMaking";
import {
    canvasElement,
    clearCanvasElementSelection,
    dragCanvasElementSide,
    dragPaletteItemOntoCanvas,
    duplicateCanvasElementFromToolbar,
    expectCanvasElementSelected,
    getAutoHeightShownInMenu,
    getCanvasElementAutoHeight,
    getCanvasElementText,
    getCanvasElementTextFit,
    isCanvasElementSideHandleShown,
    selectCanvasElement,
    setCanvasElementAutoHeight,
    typeInCanvasElement,
    waitForHeightCheckOfSelectedElement,
} from "../helpers/canvasElements";
import { getElementSize } from "../helpers/canvasResize";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: { name: "canvas-duplicate-auto-height", languages: ["en"] },
});

/** "A bunch of text": enough to take several lines, so the box grows well past a new one's height. */
const kSentences =
    "The little goat climbed the hill. The wind blew across the grass. " +
    "At the top she found her friends waiting for her.";

/**
 * How far the copy's bottom handle is dragged down, in screen pixels, once its Auto Height is off.
 * That leaves it taller than its text needs, a height that Auto Height would never give it, so
 * that a copy of it can only be that tall by taking its size from it.
 */
const kExtraHeight = 60;

/** A canvas element's width and height, in the page's own pixels, as its style gives them. */
async function readSize(
    element: Locator,
): Promise<{ width: number; height: number }> {
    const size = await getElementSize(element);
    return { width: size.width, height: size.height };
}

/**
 * Wait until canvas element `copy` has the width and height of `source`, to the whole pixel. Bloom
 * fits a box with Auto Height a moment after it is placed, so the size is waited for, not read
 * once. A failure shows the size the copy had last.
 */
async function expectSameSize(
    copy: Locator,
    source: { width: number; height: number },
    what: string,
): Promise<void> {
    const rounded = (size: { width: number; height: number }) => ({
        width: Math.round(size.width),
        height: Math.round(size.height),
    });
    await expect
        .poll(async () => rounded(await readSize(copy)), {
            timeout: 15000,
            message: `${what} should be the size of the box it was copied from.`,
        })
        .toEqual(rounded(source));
}

/**
 * Wait until the selected text box is taller than `tallerThan` and tall enough for all of its
 * text, as Auto Height makes it after typing.
 */
async function waitForBoxToFitItsText(
    page: Page,
    tallerThan: number,
): Promise<void> {
    await expect
        .poll(
            async () => {
                const fit = await getCanvasElementTextFit(page);
                return {
                    grew: fit.elementHeight > tallerThan,
                    allTextShows: fit.textHeight <= fit.shownHeight,
                };
            },
            {
                timeout: 15000,
                message:
                    "With Auto Height on, the text block did not grow until all of its text showed.",
            },
        )
        .toEqual({ grew: true, allTextShows: true });
}

test("a duplicated text block keeps its source's size and its Auto Height setting [Test Case ID 739]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Canvas");
    const errors = watchForScriptErrors(page);
    try {
        // Up and to the left, so that each copy, which Bloom puts to the right, has room.
        const originalIndex = await dragPaletteItemOntoCanvas(page, "none", {
            xFraction: 0.15,
            yFraction: 0.15,
        });
        const original = canvasElement(page, originalIndex);
        await selectCanvasElement(page, originalIndex);
        const empty = await getCanvasElementTextFit(page);
        await typeInCanvasElement(page, kSentences, { withKeys: true });
        await waitForBoxToFitItsText(page, empty.elementHeight);
        const originalWords = await getCanvasElementText(page, originalIndex);
        const originalSize = await readSize(original);

        let firstCopyIndex = 0;
        await step("Duplicate the text block", async () => {
            firstCopyIndex = await duplicateCanvasElementFromToolbar(page);
        });
        const firstCopy = canvasElement(page, firstCopyIndex);
        await expectCanvasElementSelected(
            firstCopy,
            "Bloom should select the copy it has just made.",
        );
        expect(
            await getCanvasElementText(page, firstCopyIndex),
            "The copy should have the original's words.",
        ).toBe(originalWords);
        // Read the size once Bloom has fitted the copy's height, not before.
        await waitForHeightCheckOfSelectedElement(page);
        await expectSameSize(firstCopy, originalSize, "The copy");
        expect(
            await readSize(original),
            "Duplicating the text block changed the original's size.",
        ).toEqual(originalSize);

        await step("Turn the copy's Auto Height off", async () => {
            // Sanity check that the menu's tick tells the two settings apart: the copy of a box
            // with Auto Height has it too, and its menu shows the tick.
            expect(
                await getAutoHeightShownInMenu(page),
                "The copy of a text block with Auto Height should show Auto Height on in its menu.",
            ).toBe(true);
            await setCanvasElementAutoHeight(page, false);
            await expect
                .poll(() => isCanvasElementSideHandleShown(page, "s"), {
                    timeout: 15000,
                    message:
                        "Turning Auto Height off did not give the copy its bottom handle.",
                })
                .toBe(true);
            const before = await readSize(firstCopy);
            await dragCanvasElementSide(page, "s", kExtraHeight);
            const after = await readSize(firstCopy);
            expect(
                after.height,
                "Dragging the copy's bottom handle down did not make it taller.",
            ).toBeGreaterThan(before.height);
            // The original has the same words and width, and Auto Height made it just tall enough
            // for them, so the copy is now taller than Auto Height would make it.
            expect(
                after.width,
                "The copy should still be as wide as the original.",
            ).toBe(originalSize.width);
            expect(
                after.height,
                "The copy should now be taller than Auto Height makes a box with these words.",
            ).toBeGreaterThan(originalSize.height);
        });
        const firstCopySize = await readSize(firstCopy);

        let secondCopyIndex = 0;
        await step("Duplicate the copy", async () => {
            secondCopyIndex = await duplicateCanvasElementFromToolbar(page);
        });
        const secondCopy = canvasElement(page, secondCopyIndex);
        await expectCanvasElementSelected(
            secondCopy,
            "Bloom should select the second copy it has just made.",
        );
        await waitForHeightCheckOfSelectedElement(page);
        await expectSameSize(secondCopy, firstCopySize, "The second copy");

        await step("Select the second copy and read its menu", async () => {
            await clearCanvasElementSelection(page);
            await selectCanvasElement(page, secondCopyIndex);
            // What the menu shows a person, and the setting Bloom keeps for the element.
            expect(
                await getAutoHeightShownInMenu(page),
                "The copy of a text block with Auto Height off should show Auto Height off in its menu.",
            ).toBe(false);
            expect(
                await getCanvasElementAutoHeight(page),
                "The copy of a text block with Auto Height off should have Auto Height off.",
            ).toBe(false);
            // Selecting it again gave Bloom the chance to fit its height, which it must not have.
            await waitForHeightCheckOfSelectedElement(page);
            await expectSameSize(
                secondCopy,
                firstCopySize,
                "The second copy, selected again,",
            );
        });

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
