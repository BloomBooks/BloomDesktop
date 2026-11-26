// Duplicating a speech bubble on a Canvas page, through the "..." menu of its toolbar. The copy has
// the original's words, Element Type and size, and lands beside it rather than on top of it. Both
// bubbles then keep everything a person does with a bubble: each can be moved, resized by its side
// handles and typed in, without the other changing. Duplicating once left no bubble on the page
// resizable (BL-13617).
//
// Automates "Duplicate Bubbles Keep Functions" (Test Case ID 331).

import { type Locator, type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    canvasElement,
    canvasElementTextBox,
    clearCanvasElementSelection,
    dragCanvasElementSide,
    dragPaletteItemOntoCanvas,
    duplicateCanvasElement,
    selectCanvasElement,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import {
    getElementSize,
    getPageScale,
    type IElementSize,
} from "../helpers/canvasResize";
import { chooseCanvasElementType } from "../helpers/canvasToolOptions";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: {
        name: "canvas-duplicate-bubble",
        languages: ["en"],
        // Below Pro, the Canvas tool's palette sits behind a subscription notice.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/** Bloom's grid, in the page's own pixels (gridSize in CanvasSnapProvider.ts). */
const kGrid = 10;

/** The words typed into the original bubble before it is duplicated. */
const kOriginalWords = "Duplicate me";

/** The ComicalJS style of a data-bubble spec, which Bloom stores with backquotes for quotes. */
async function readBubbleStyle(bubble: Locator): Promise<string> {
    const json = await bubble.getAttribute("data-bubble");
    if (!json) throw new Error("The canvas element has no data-bubble spec.");
    return (JSON.parse(json.replace(/`/g, '"')) as { style: string }).style;
}

/**
 * The text of the box a bubble shows. A space typed at the end of a box's text goes in as a
 * non-breaking one, so every run of white space, that one too, reads as one space.
 */
async function readBubbleText(page: Page, index: number): Promise<string> {
    const text = await canvasElementTextBox(page, index).innerText();
    return text.replace(/\s+/g, " ").trim();
}

/** Whether two sizes and places are the same, to the pixel. */
function samePlaceAndSize(a: IElementSize, b: IElementSize): boolean {
    return (
        Math.abs(a.left - b.left) <= 1 &&
        Math.abs(a.top - b.top) <= 1 &&
        Math.abs(a.width - b.width) <= 1 &&
        Math.abs(a.height - b.height) <= 1
    );
}

/**
 * Move the bubble at `index` by dragging it `dx`, `dy` screen pixels, the way a person does: press
 * on the bubble, a quarter of the way in from its top-left corner, and drag. Nothing is selected
 * when the press comes, so the press is a bubble's first, which moves it, and not a click into a
 * text box that is being edited, which would place the caret instead. Checks that the bubble
 * landed where the drag took it, on Bloom's grid, and that `other` did not move or change size.
 */
async function moveBubble(
    page: Page,
    index: number,
    dx: number,
    dy: number,
    other: Locator,
    what: string,
): Promise<void> {
    const bubble = canvasElement(page, index);
    await clearCanvasElementSelection(page);
    await bubble.evaluate((e) =>
        e.scrollIntoView({ block: "center", inline: "center" }),
    );
    const before = await getElementSize(bubble);
    const otherBefore = await getElementSize(other);
    const scale = await getPageScale(bubble);
    const box = await bubble.boundingBox({ timeout: 30000 });
    if (!box) throw new Error(`The ${what} has no on-screen box.`);
    const x = Math.round(box.x + box.width / 4);
    const y = Math.round(box.y + box.height / 4);
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + dx, y + dy, { steps: 12 });
    await page.mouse.up();
    // Where the drag took it, in the page's own pixels; Bloom then puts it on the grid.
    const wantedLeft = before.left + dx / scale;
    const wantedTop = before.top + dy / scale;
    await expect
        .poll(
            async () => {
                const now = await getElementSize(bubble);
                return (
                    Math.abs(now.left - wantedLeft) <= kGrid &&
                    Math.abs(now.top - wantedTop) <= kGrid
                );
            },
            {
                timeout: 15000,
                message:
                    `Dragging the ${what} by ${dx},${dy} did not move it there: it was at ` +
                    `${before.left},${before.top} and should be near ${wantedLeft},${wantedTop}.`,
            },
        )
        .toBe(true);
    const otherAfter = await getElementSize(other);
    expect(
        samePlaceAndSize(otherAfter, otherBefore),
        `Moving the ${what} also changed the other bubble: it went from ` +
            `${JSON.stringify(otherBefore)} to ${JSON.stringify(otherAfter)}.`,
    ).toBe(true);
}

/**
 * Widen the bubble at `index` by dragging its right-hand side handle `distance` screen pixels to
 * the right, and check that it grew by about that much while its left edge stayed put, and that
 * `other` did not move or change size.
 */
async function widenBubble(
    page: Page,
    index: number,
    distance: number,
    other: Locator,
    what: string,
): Promise<void> {
    const bubble = canvasElement(page, index);
    if ((await bubble.getAttribute("data-bloom-active")) !== "true")
        await selectCanvasElement(page, index);
    const before = await getElementSize(bubble);
    const otherBefore = await getElementSize(other);
    const scale = await getPageScale(bubble);
    await dragCanvasElementSide(page, "e", distance);
    const after = await getElementSize(bubble);
    const wantedWidth = before.width + distance / scale;
    expect(
        Math.abs(after.width - wantedWidth),
        `Dragging the ${what}'s right-hand handle by ${distance} should have made it about ` +
            `${wantedWidth} wide; it went from ${before.width} to ${after.width}.`,
    ).toBeLessThanOrEqual(kGrid);
    expect(
        Math.abs(after.left - before.left),
        `Widening the ${what} from its right-hand side moved its left edge from ` +
            `${before.left} to ${after.left}.`,
    ).toBeLessThanOrEqual(1);
    const otherAfter = await getElementSize(other);
    expect(
        samePlaceAndSize(otherAfter, otherBefore),
        `Widening the ${what} also changed the other bubble: it went from ` +
            `${JSON.stringify(otherBefore)} to ${JSON.stringify(otherAfter)}.`,
    ).toBe(true);
}

test("A duplicated speech bubble, and the one it was copied from, can each still be moved, resized and typed in [Test Case ID 331]", async ({
    page,
    step,
}) => {
    test.setTimeout(240000);
    await makeBookFromTemplate(page, "Basic Book");
    const errors = watchForScriptErrors(page);
    try {
        const before = (await getContentPages(page)).map((p) => p.id);
        await addPage(page, "Canvas");
        const added = (await getContentPages(page)).find(
            (p) => !before.includes(p.id),
        );
        if (!added)
            throw new Error("Adding a Canvas page added no content page.");
        await goToPage(page, added.id);

        // Up and to the left, so that the copy, which Bloom puts to the right of it, and the
        // moves and widening that follow all stay on the canvas.
        const originalIndex = await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.2,
            yFraction: 0.25,
        });
        const original = canvasElement(page, originalIndex);
        await selectCanvasElement(page, originalIndex);
        await typeInCanvasElement(page, kOriginalWords);
        // A style that no new bubble has, so that the copy can only have it from the original.
        await chooseCanvasElementType(page, "pointedArcs");
        expect(await readBubbleStyle(original)).toBe("pointedArcs");
        const originalSize = await getElementSize(original);

        let copyIndex = 0;
        await step("Duplicate the bubble", async () => {
            await selectCanvasElement(page, originalIndex);
            copyIndex = await duplicateCanvasElement(page);
        });
        const copy = canvasElement(page, copyIndex);
        await expect(
            copy,
            "Bloom should select the copy it has just made.",
        ).toHaveAttribute("data-bloom-active", "true", { timeout: 15000 });
        expect(
            await readBubbleText(page, copyIndex),
            "The copy should have the original's words.",
        ).toBe(kOriginalWords);
        expect(
            await readBubbleStyle(copy),
            "The copy should have the original's Element Type.",
        ).toBe("pointedArcs");
        const copySize = await getElementSize(copy);
        expect(
            Math.abs(copySize.width - originalSize.width) <= 1 &&
                Math.abs(copySize.height - originalSize.height) <= 1,
            `The copy should be the original's size, ${originalSize.width}x${originalSize.height}; ` +
                `it is ${copySize.width}x${copySize.height}.`,
        ).toBe(true);
        expect(
            Math.abs(copySize.left - originalSize.left) +
                Math.abs(copySize.top - originalSize.top),
            `The copy should not lie on top of the original, at ${originalSize.left},${originalSize.top}.`,
        ).toBeGreaterThan(kGrid);
        expect(
            samePlaceAndSize(await getElementSize(original), originalSize),
            "Duplicating the bubble moved or resized the original.",
        ).toBe(true);

        await step("Move and resize the copy", async () => {
            await moveBubble(page, copyIndex, 30, 80, original, "copy");
            await widenBubble(page, copyIndex, 60, original, "copy");
        });

        await step("Move and resize the original", async () => {
            await moveBubble(page, originalIndex, 20, 40, copy, "original");
            await widenBubble(page, originalIndex, 50, copy, "original");
        });

        await step("Type in the copy", async () => {
            await selectCanvasElement(page, copyIndex);
            await typeInCanvasElement(page, " again");
            expect(
                await readBubbleText(page, copyIndex),
                "The copy's words should take what was typed in it.",
            ).toBe(`${kOriginalWords} again`);
            expect(
                await readBubbleText(page, originalIndex),
                "Typing in the copy should leave the original's words alone.",
            ).toBe(kOriginalWords);
        });

        expect(errors.errors(), "Bloom raised a script error.").toEqual([]);
    } finally {
        errors.stop();
    }
});
