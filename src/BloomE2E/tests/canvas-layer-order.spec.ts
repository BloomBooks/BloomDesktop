// Changing which canvas element is in front: the Layer submenu of a canvas element's "..." menu
// (Bring Forward, Bring to Front, Send Backwards, Send to Back) and the keyboard shortcuts it names
// (BL-15992). Notion test case "Canvas element Layer order" (Test Case ID 840).
//
// The page holds two speech bubbles that overlap a little, so a click where they overlap shows
// which one is in front, and a picture placeholder away from both. The last test gives one bubble
// a child bubble, because a family must move as one, and moving one used to throw inside ComicalJS.
//
// The tests are serial because each one starts from the page the one before it left behind.

import { type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    addChildBubble,
    dragPaletteItemOntoCanvas,
} from "../helpers/canvasElements";
import {
    chooseLayerCommand,
    clickWhereCanvasElementsOverlap,
    getCanvasElementStack,
    getEnabledLayerCommands,
    getSelectedStackedElement,
    getStackingOrder,
    pressLayerShortcut,
    selectStackedCanvasElement,
} from "../helpers/canvasLayers";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: { name: "canvas-layer-order", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The elements on the page, named by where they sit (see canvasLayers.ts). Set by the first test.
// bubbleA and bubbleB overlap at bubbleA's right end; the picture overlaps neither.
let bubbleA: string;
let bubbleB: string;
let picture: string;
let canvasPage: IBookPage;

/**
 * Assert the stacking order, back-most first, and that ComicalJS's levels agree with it: they
 * climb from 2 (the background image is 1) one step per element, except that a child bubble shares
 * its parent's level. `children` names the elements that are child bubbles.
 */
async function expectStack(
    page: Page,
    expected: string[],
    children: string[] = [],
): Promise<void> {
    await expect
        .poll(async () => getStackingOrder(page), {
            timeout: 15000,
            message: "The stacking order is not the one expected.",
        })
        .toEqual(expected);
    let level = 1;
    const expectedLevels = expected.map((id) =>
        children.includes(id) ? level : ++level,
    );
    const stack = await getCanvasElementStack(page);
    expect(
        stack.map((e) => e.level),
        `ComicalJS's levels do not follow the stacking order ${expected.join(" | ")}.`,
    ).toEqual(expectedLevels);
}

test.describe("canvas element Layer order [Test Case ID 840]", () => {
    test("builds a canvas page with two overlapping bubbles and a picture", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Canvas");
        await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.35,
            yFraction: 0.43,
        });
        await dragPaletteItemOntoCanvas(page, "speech", {
            xFraction: 0.62,
            yFraction: 0.41,
        });
        await dragPaletteItemOntoCanvas(page, "image", {
            xFraction: 0.5,
            yFraction: 0.75,
        });
        [bubbleA, bubbleB, picture] = await getStackingOrder(page);
        [canvasPage] = await getContentPages(page);

        // Sanity check what the rest of the file rests on: three distinct elements, stacked in the
        // order they were added, with levels to match, and the canvas page found.
        expect(new Set([bubbleA, bubbleB, picture]).size).toBe(3);
        await expectStack(page, [bubbleA, bubbleB, picture]);
        expect(canvasPage, "The book has no content page.").toBeDefined();
    });

    test("the Layer menu enables only the moves the element can make", async ({
        page,
    }) => {
        await selectStackedCanvasElement(page, bubbleA);
        expect(await getEnabledLayerCommands(page)).toEqual([
            "Bring Forward",
            "Bring to Front",
        ]);

        await selectStackedCanvasElement(page, bubbleB);
        expect(await getEnabledLayerCommands(page)).toEqual([
            "Bring Forward",
            "Bring to Front",
            "Send Backwards",
            "Send to Back",
        ]);

        await selectStackedCanvasElement(page, picture);
        expect(await getEnabledLayerCommands(page)).toEqual([
            "Send Backwards",
            "Send to Back",
        ]);
    });

    test("the Layer menu moves the selected element", async ({ page }) => {
        await selectStackedCanvasElement(page, bubbleA);

        await chooseLayerCommand(page, "Bring Forward");
        await expectStack(page, [bubbleB, bubbleA, picture]);

        await chooseLayerCommand(page, "Bring to Front");
        await expectStack(page, [bubbleB, picture, bubbleA]);
        expect(await getEnabledLayerCommands(page)).toEqual([
            "Send Backwards",
            "Send to Back",
        ]);

        await chooseLayerCommand(page, "Send Backwards");
        await expectStack(page, [bubbleB, bubbleA, picture]);

        await chooseLayerCommand(page, "Send to Back");
        await expectStack(page, [bubbleA, bubbleB, picture]);

        // The element moved stays the selected one throughout.
        expect(await getSelectedStackedElement(page)).toBe(bubbleA);
    });

    test("the keyboard shortcuts move the selected element", async ({
        page,
    }) => {
        await selectStackedCanvasElement(page, picture);

        await pressLayerShortcut(page, "Send Backwards");
        await expectStack(page, [bubbleA, picture, bubbleB]);

        await pressLayerShortcut(page, "Send to Back");
        await expectStack(page, [picture, bubbleA, bubbleB]);

        await pressLayerShortcut(page, "Bring Forward");
        await expectStack(page, [bubbleA, picture, bubbleB]);

        await pressLayerShortcut(page, "Bring to Front");
        await expectStack(page, [bubbleA, bubbleB, picture]);
    });

    test("a click where two bubbles overlap picks the one in front", async ({
        page,
    }) => {
        // Select the picture first, so a click that picked a bubble is seen to change the selection.
        await selectStackedCanvasElement(page, picture);
        expect(
            await clickWhereCanvasElementsOverlap(page, bubbleA, bubbleB),
        ).toBe(bubbleB);

        await selectStackedCanvasElement(page, bubbleA);
        await pressLayerShortcut(page, "Bring to Front");
        await expectStack(page, [bubbleB, picture, bubbleA]);

        await selectStackedCanvasElement(page, picture);
        expect(
            await clickWhereCanvasElementsOverlap(page, bubbleA, bubbleB),
        ).toBe(bubbleA);
    });

    test("the order is kept after leaving the page and coming back", async ({
        page,
    }) => {
        const otherPage = (await getPages(page)).find((p) => !p.isContentPage)!;
        await goToPage(page, otherPage.id);
        await goToPage(page, canvasPage.id);
        await expectStack(page, [bubbleB, picture, bubbleA]);
    });

    test("a bubble and its child move as one, with no script error", async ({
        page,
    }) => {
        await selectStackedCanvasElement(page, bubbleB);
        const before = await getStackingOrder(page);
        await addChildBubble(page);
        const child = (await getStackingOrder(page)).find(
            (id) => !before.includes(id),
        )!;
        expect(child, "Add Child Bubble added no element.").toBeDefined();

        const errors = watchForScriptErrors(page);
        try {
            // bubbleB is at the back, with its child; bringing it to the front brings the child.
            // The picture is selected first because Add Child Bubble leaves the caret in the new
            // child's text, and a click on the parent alone selects the parent but leaves the caret
            // there, where Bloom ignores a Layer shortcut. Selecting another element clears it.
            await selectStackedCanvasElement(page, picture);
            await selectStackedCanvasElement(page, bubbleB);
            await pressLayerShortcut(page, "Bring to Front");
            await expectStack(
                page,
                [picture, bubbleA, bubbleB, child],
                [child],
            );

            // One step back passes the whole family behind bubbleA.
            await chooseLayerCommand(page, "Send Backwards");
            await expectStack(
                page,
                [picture, bubbleB, child, bubbleA],
                [child],
            );

            // And the family goes all the way to the back together.
            await pressLayerShortcut(page, "Send to Back");
            await expectStack(
                page,
                [bubbleB, child, picture, bubbleA],
                [child],
            );

            expect(
                errors.errors(),
                "Moving a bubble family raised a script error.",
            ).toEqual([]);
        } finally {
            errors.stop();
        }
    });
});
