// Undoing changes to canvas elements. Before the page history, moving, resizing, cropping, adding
// and deleting canvas elements could not be undone; now each should be one "Undo edit" step, which
// Ctrl+Z takes back and Ctrl+Y puts back (docs/undo/MINIMUM.md, "Gains over today").
//
// Each test makes one change the way a person does, then checks that Ctrl+Z puts every canvas
// element on the page back exactly as it was before, and that Ctrl+Y puts back exactly what the
// change made: the elements' markup, less what editing adds, and where each is drawn
// (getCanvasElementMarkupAndRects in helpers/canvasElements.ts). Each press must run one undo or
// one redo of the page history. After the undo, the element the change was made to must be
// selected, so that the user sees what was undone; after undoing an add, nothing is. Some tests
// also check what the redo selects, and the first selects another element before each key press,
// so that its checks fail if the undo or redo selects nothing. Last, the test selects an element
// and drags it, to show that Bloom still handles canvas elements after the undo and redo.
//
// Changing a bubble's Element Type in the Canvas tool's panel is an undo step too, although the
// change is made from the toolbox, which otherwise ends the history (MINIMUM.md item 4). Its test
// also checks that the panel shows the style that undo and redo put back, and that a step made
// before the change can still be undone after it.
//
// The tests are serial because each one starts from the page the one before it left behind.

import type { Page } from "@playwright/test";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import { checkUndoRoundTripAfterEachTest } from "../helpers/undoRoundTrip";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    canvasElement,
    chooseElementTypeInCanvasTool,
    clickInCanvasElementText,
    deleteCanvasElement,
    dragCanvasElement,
    dragCanvasElementCorner,
    dragCanvasElementSide,
    dragPaletteItemOntoCanvas,
    dragPaletteItemOntoCanvasWithMouse,
    dragRotateHandle,
    dragSpeechBubbleTail,
    duplicateCanvasElement,
    getBubbleStyle,
    getCanvasElementMarkupAndRects,
    getElementTypeInCanvasTool,
    getSelectedCanvasElementIndex,
    openCanvasTool,
    selectCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { chooseImageFile, cropImage } from "../helpers/images";
import { typeWithKeys } from "../helpers/keys";
import {
    redoWithCtrlYAndExpect,
    undoWithCtrlZAndExpect,
    waitForOpenTransactionToEnd,
    waitForPageHistory,
} from "../helpers/undo";

// The Canvas tool is a Pro feature, so the collection is on the Test enterprise subscription.
test.use({
    collectionSpec: {
        name: "undo-canvas",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

checkUndoRoundTripAfterEachTest(test);

test.describe.configure({ mode: "serial" });

// The picture for the picture element. A copy of src/BloomTests/ImageProcessing/images/bird.png.
const IMAGE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// Where the speech bubble and the picture sit among the page's canvas elements, in the order the
// palette added them. Set by the first test.
let bubble: number;
let picture: number;

/** What one test checks: a change, and which elements it concerns. */
interface ICanvasChange {
    /** What the test does, for the failure messages, e.g. "moving a speech bubble". */
    change: string;
    /** Make the change, the way a person does. */
    makeChange: () => Promise<unknown>;
    /**
     * The element that must be selected once Ctrl+Z has undone the change, as the index
     * canvasElement takes; undefined when nothing must be, as after undoing an add.
     */
    selectedAfterUndo: () => number | undefined;
    /**
     * An element other than the one changed, to select after the change and again after Ctrl+Z,
     * before each key press. Without it the changed element is still selected when Ctrl+Z is
     * pressed, so the check of what is selected afterwards passes even if the undo selects
     * nothing.
     */
    selectBeforeUndoAndRedo?: () => number;
    /**
     * The element that must be selected once Ctrl+Y has redone the change, as for
     * selectedAfterUndo. When this is absent, what Ctrl+Y selects is not checked.
     */
    selectedAfterRedo?: () => number | undefined;
    /** Anything more to check once Ctrl+Z has undone the change. */
    alsoAfterUndo?: () => Promise<void>;
    /** Anything more to check, or to do, once Ctrl+Y has redone the change, before the drag. */
    alsoAfterRedo?: () => Promise<void>;
    /** The element to select and drag at the end, once Ctrl+Y has redone the change. */
    elementToDragAfterwards: () => number;
}

/**
 * Make the change, check that Ctrl+Z puts every canvas element back as it was and selects the
 * element the change was made to, check that Ctrl+Y puts back what the change made, then select an
 * element and drag it. Select the element the change is made to before calling this: selecting a
 * picture can refit its box to the picture, and that belongs to the state before the change.
 */
async function expectCtrlZAndCtrlYToReverse(
    page: Page,
    check: ICanvasChange,
): Promise<void> {
    const read = () => getCanvasElementMarkupAndRects(page);
    const before = await read();
    await check.makeChange();
    const after = await read();
    expect(
        after,
        `${check.change} changed nothing on the canvas, so there is nothing to undo.`,
    ).not.toEqual(before);

    if (check.selectBeforeUndoAndRedo)
        await selectCanvasElement(page, check.selectBeforeUndoAndRedo());
    await undoWithCtrlZAndExpect(page, read, before, check.change);
    await expectSelected(
        page,
        check.selectedAfterUndo(),
        `After Ctrl+Z undid ${check.change}`,
    );
    await check.alsoAfterUndo?.();
    if (check.selectBeforeUndoAndRedo)
        await selectCanvasElement(page, check.selectBeforeUndoAndRedo());
    await redoWithCtrlYAndExpect(page, read, after, check.change);
    if (check.selectedAfterRedo)
        await expectSelected(
            page,
            check.selectedAfterRedo(),
            `After Ctrl+Y redid ${check.change}`,
        );
    await check.alsoAfterRedo?.();

    await selectCanvasElement(page, check.elementToDragAfterwards());
    await dragCanvasElement(page, 15, 10);
}

/**
 * Wait for the canvas element at index expected to be the one selected, or for none to be when
 * expected is undefined. when says what has just happened, for the failure message.
 */
async function expectSelected(
    page: Page,
    expected: number | undefined,
    when: string,
): Promise<void> {
    await expect
        .poll(async () => getSelectedCanvasElementIndex(page), {
            timeout: 15000,
            message:
                expected === undefined
                    ? `${when}, a canvas element is selected, where none should be.`
                    : `${when}, canvas element ${expected}, which the change was made to, ` +
                      `is not the one selected.`,
        })
        .toBe(expected);
}

test.describe("Ctrl+Z and Ctrl+Y undo and redo changes to canvas elements", () => {
    test("moving a speech bubble", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Canvas");
        const [canvasPage] = await getContentPages(page);
        await goToPage(page, canvasPage.id);
        await waitForPageHistory(page);
        await openCanvasTool(page);
        bubble = await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.3,
            yFraction: 0.25,
        });
        picture = await dragPaletteItemOntoCanvas(page, "image", {
            xFraction: 0.6,
            yFraction: 0.6,
        });
        await chooseImageFile(page, IMAGE_FILE, canvasElement(page, picture));
        await waitForOpenTransactionToEnd(page);

        await selectCanvasElement(page, bubble);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "moving a speech bubble",
            makeChange: () => dragCanvasElement(page, 40, 30),
            // With the picture selected when each key is pressed, the bubble is selected
            // afterwards only if the undo and the redo select it.
            selectBeforeUndoAndRedo: () => picture,
            selectedAfterUndo: () => bubble,
            selectedAfterRedo: () => bubble,
            elementToDragAfterwards: () => bubble,
        });
    });

    test("resizing a speech bubble by its side", async ({ page }) => {
        await selectCanvasElement(page, bubble);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "resizing a speech bubble by its side",
            makeChange: () => dragCanvasElementSide(page, "e", 40),
            selectedAfterUndo: () => bubble,
            elementToDragAfterwards: () => bubble,
        });
    });

    test("dragging the tail of a speech bubble", async ({ page }) => {
        // A bubble of its own, with the tail the palette gave it. The tip of a tail stays put when
        // its bubble moves or widens, and the tests above have moved and widened the first bubble
        // over its tip, so it no longer shows a tail to drag.
        const bubbleWithTail = await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.2,
            yFraction: 0.85,
        });
        await selectCanvasElement(page, bubbleWithTail);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "dragging the tail of a speech bubble",
            makeChange: () => dragSpeechBubbleTail(page, 40, 30),
            selectedAfterUndo: () => bubbleWithTail,
            elementToDragAfterwards: () => bubbleWithTail,
        });
    });

    test("typing in a speech bubble", async ({ page }) => {
        await selectCanvasElement(page, bubble);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "typing in a speech bubble",
            makeChange: async () => {
                await clickInCanvasElementText(page, bubble);
                await typeWithKeys(page, "hello");
            },
            selectedAfterUndo: () => bubble,
            elementToDragAfterwards: () => bubble,
        });
    });

    test("changing a speech bubble's Element Type in the Canvas tool", async ({
        page,
    }) => {
        const read = () => getCanvasElementMarkupAndRects(page);
        const change =
            "changing a speech bubble's Element Type in the Canvas tool";
        const bubbleElement = canvasElement(page, bubble);
        // A step before the change, to show afterwards that the change did not end the history.
        await selectCanvasElement(page, bubble);
        const beforeMove = await read();
        await dragCanvasElement(page, 30, 20);
        const beforeChange = await read();
        const styleBefore = await getBubbleStyle(bubbleElement);
        expect(
            styleBefore,
            "The bubble already has the style this test changes it to.",
        ).not.toBe("ellipse");

        await expectCtrlZAndCtrlYToReverse(page, {
            change,
            makeChange: () => chooseElementTypeInCanvasTool(page, "ellipse"),
            selectedAfterUndo: () => bubble,
            alsoAfterUndo: async () => {
                await expect(
                    getBubbleStyle(bubbleElement),
                    `Ctrl+Z did not put back the bubble's style "${styleBefore}" in its data-bubble.`,
                ).resolves.toBe(styleBefore);
                await expect
                    .poll(async () => getElementTypeInCanvasTool(page), {
                        timeout: 15000,
                        message: `After Ctrl+Z, the Canvas tool's Element Type control does not show "${styleBefore}" again.`,
                    })
                    .toBe(styleBefore);
            },
            alsoAfterRedo: async () => {
                await expect
                    .poll(async () => getElementTypeInCanvasTool(page), {
                        timeout: 15000,
                        message: `After Ctrl+Y, the Canvas tool's Element Type control does not show "ellipse" again.`,
                    })
                    .toBe("ellipse");
                // Undo the change again, then the move made before it: a change from the toolbox
                // that ended the history would leave nothing to undo here.
                await undoWithCtrlZAndExpect(page, read, beforeChange, change);
                await undoWithCtrlZAndExpect(
                    page,
                    read,
                    beforeMove,
                    "moving the bubble before the Element Type change",
                );
            },
            elementToDragAfterwards: () => bubble,
        });
    });

    test("cropping a picture", async ({ page }) => {
        await selectCanvasElement(page, picture);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "cropping a picture",
            makeChange: () =>
                cropImage(page, "e", 30, canvasElement(page, picture)),
            selectedAfterUndo: () => picture,
            elementToDragAfterwards: () => picture,
        });
    });

    test("rotating a picture with its rotation knob", async ({ page }) => {
        await selectCanvasElement(page, picture);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "rotating a picture with its rotation knob",
            makeChange: () => dragRotateHandle(page, 30, { withCtrl: true }),
            selectedAfterUndo: () => picture,
            elementToDragAfterwards: () => picture,
        });
    });

    test("resizing a picture by a corner", async ({ page }) => {
        await selectCanvasElement(page, picture);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "resizing a picture by a corner",
            makeChange: () => dragCanvasElementCorner(page, "se", 40, 30),
            selectedAfterUndo: () => picture,
            elementToDragAfterwards: () => picture,
        });
    });

    test("adding a speech bubble from the palette", async ({ page }) => {
        let added = -1;
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "adding a speech bubble from the palette",
            // With the mouse, so that the page history sees the events a person's drag raises
            // (see dragPaletteItemOntoCanvasWithMouse).
            makeChange: async () => {
                added = await dragPaletteItemOntoCanvasWithMouse(
                    page,
                    "speech",
                    { xFraction: 0.7, yFraction: 0.2 },
                );
            },
            selectedAfterUndo: () => undefined,
            selectedAfterRedo: () => added,
            elementToDragAfterwards: () => added,
        });
    });

    test("duplicating a speech bubble", async ({ page }) => {
        await selectCanvasElement(page, bubble);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "duplicating a speech bubble",
            makeChange: () => duplicateCanvasElement(page),
            // A duplicate is an add, so undoing it leaves nothing selected.
            selectedAfterUndo: () => undefined,
            elementToDragAfterwards: () => bubble,
        });
    });

    test("deleting a speech bubble", async ({ page }) => {
        await selectCanvasElement(page, bubble);
        await expectCtrlZAndCtrlYToReverse(page, {
            change: "deleting a speech bubble",
            makeChange: () => deleteCanvasElement(page),
            // Undoing the delete puts the bubble back where it was, and selects it.
            selectedAfterUndo: () => bubble,
            // The redo takes the selected bubble out of the page and puts nothing back.
            selectedAfterRedo: () => undefined,
            // The bubble is gone again after the redo, and the picture, added after it, moved up one.
            elementToDragAfterwards: () => picture - 1,
        });
    });
});
