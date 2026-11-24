// The Correct and Wrong tabs of a game page in the Edit tab. They show the page as it looks once the
// reader has answered right or wrong, and the author adds there what only that answer shows (a
// picture, a message). What the author made in Start shows in them too, but must stay as it was:
// it cannot be dragged, selected or typed in until the author goes back to Start (BL-14532).

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { editablePageFrame } from "../helpers/bookMaking";
import {
    canvasElement,
    dragCanvasElementCorner,
    dragCanvasElementSide,
    selectCanvasElementAt,
    typeInCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    clickGameTab,
    dragGamePaletteItemOntoPage,
    makeBookWithGamePage,
} from "../helpers/games";
import {
    chooseImageFile,
    cropImage,
    getImagePlacement,
} from "../helpers/images";
import { realClickAt, waitForClickToSettle } from "../helpers/realClick";

test.use({
    collectionSpec: {
        name: "game-correct-wrong-tabs",
        languages: ["en"],
        // Games need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

// The picture the author gives a draggable item in Start and the new item in Correct. Shipped with
// the suite; a copy of src/BloomTests/ImageProcessing/images/bird.png.
const IMAGE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

/** Where a canvas element or target sits, as its style records it. */
async function getStylePosition(
    element: Locator,
): Promise<{ left: string; top: string }> {
    return element.evaluate((e) => ({
        left: (e as HTMLElement).style.left,
        top: (e as HTMLElement).style.top,
    }));
}

/** Whether the selection or the keyboard focus of the page frame is inside the element. */
async function hasCaretOrFocusInside(element: Locator): Promise<boolean> {
    return element.evaluate((e) => {
        const doc = e.ownerDocument;
        const selection = doc.getSelection();
        const selectionInside =
            !!selection &&
            selection.rangeCount > 0 &&
            !!selection.anchorNode &&
            e.contains(selection.anchorNode);
        return selectionInside || e.contains(doc.activeElement);
    });
}

/**
 * Where to press on an item: across the middle, and `yFraction` of the way down it. A word box
 * has its picture's target in the middle, which takes a press there, so a press meant for the box
 * goes lower down, where its word is.
 */
async function pointOn(
    item: Locator,
    what: string,
    yFraction: number,
): Promise<{ x: number; y: number }> {
    const box = await item.boundingBox();
    if (!box) throw new Error(`${what} has no box on screen to press.`);
    return { x: box.x + box.width / 2, y: box.y + box.height * yFraction };
}

/**
 * Check that a person cannot change an item made in Start, from the tab showing: a click on it
 * neither selects it nor puts the insertion point or focus in it, and dragging it leaves it where
 * it was. `what` names it in the failure messages; `yFraction` is where to press (see pointOn).
 */
async function expectCannotModify(
    page: Page,
    item: Locator,
    what: string,
    yFraction = 0.5,
): Promise<void> {
    const { x, y } = await pointOn(item, what, yFraction);
    const before = await getStylePosition(item);
    expect(
        await item.getAttribute("data-bloom-active"),
        `Sanity check: ${what} should not be selected before clicking it`,
    ).not.toBe("true");

    await realClickAt(page, x, y);
    await waitForClickToSettle(editablePageFrame(page));
    expect(
        await item.getAttribute("data-bloom-active"),
        `Clicking ${what} should not select it`,
    ).not.toBe("true");
    expect(
        await hasCaretOrFocusInside(item),
        `Clicking ${what} should not put the insertion point or the focus in it`,
    ).toBe(false);

    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + 60, y + 40, { steps: 10 });
    await page.mouse.up();
    await waitForClickToSettle(editablePageFrame(page));
    expect(
        await getStylePosition(item),
        `Dragging ${what} should not move it`,
    ).toEqual(before);
    expect(
        await item.getAttribute("data-bloom-active"),
        `Dragging ${what} should not select it`,
    ).not.toBe("true");
}

/** Move the selected canvas element by dragging it with the real mouse, and return its new place. */
async function dragElementBy(
    page: Page,
    element: Locator,
    dx: number,
    dy: number,
): Promise<{ left: string; top: string }> {
    const box = (await element.boundingBox())!;
    // A quarter of the way in from the top-left corner, clear of the crop handles in the middle of
    // each side.
    const x = box.x + box.width / 4;
    const y = box.y + box.height / 4;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + dx, y + dy, { steps: 12 });
    await page.mouse.up();
    await waitForClickToSettle(editablePageFrame(page));
    return getStylePosition(element);
}

test("items made in Start show in Correct and Wrong but cannot be changed there, and items added there can [Test Case ID 782]", async ({
    page,
    step,
}) => {
    await step("Add a Drag Images to Words page", async () => {
        await makeBookWithGamePage(page, "Drag Images to Words");
    });
    // The game page stays the one being edited from here on, in every tab, so its frame does too.
    const frame = editablePageFrame(page);
    const startItems = frame.locator(
        ".bloom-canvas-element:not(.drag-item-correct):not(.drag-item-wrong)",
    );
    const draggables = frame.locator("[data-draggable-id]");
    // Items in Start that stay where they are in the game: the heading, and the boxes that hold
    // the words the pictures go to.
    const heading = frame
        .locator(".bloom-canvas-element.bloom-passive-element")
        .first();
    const wordBox = startItems
        .filter({ hasNot: frame.locator(".bloom-editable") })
        .and(
            frame.locator(
                ":not([data-draggable-id]):not(.bloom-backgroundImage)",
            ),
        )
        .first();
    const word = "Birds";
    // How far down a word box its word is, below the picture's target.
    const kWordInBox = 0.85;

    await step("Fill in the heading and a picture", async () => {
        expect(
            await draggables.count(),
            "Sanity check: the page should have draggable pictures",
        ).toBeGreaterThan(0);
        await expect(
            wordBox,
            "Sanity check: the page should have boxes for the words",
        ).toBeVisible();
        // Sanity check: in Start, a click on a word box selects it, so a click that does not in
        // the other tabs is one that Bloom refused.
        const onWord = await pointOn(wordBox, "a word box", kWordInBox);
        await realClickAt(page, onWord.x, onWord.y);
        await expect(
            wordBox,
            "Sanity check: in Start, clicking a word box should select it",
        ).toHaveAttribute("data-bloom-active", "true");
        await selectCanvasElementAt(heading, "the heading");
        await typeInCanvasElement(page, word);
        await chooseImageFile(page, IMAGE_FILE, draggables.first());
    });

    const startCount = await startItems.count();
    // Everything made in Start except the draggable pictures, which only Start and Play show.
    const fixedItems = frame.locator(
        ".bloom-canvas-element:not(.drag-item-correct):not(.drag-item-wrong):not([data-draggable-id])",
    );
    const firstDraggableId = (await draggables
        .first()
        .getAttribute("data-draggable-id"))!;
    const firstTarget = frame.locator(`[data-target-of="${firstDraggableId}"]`);
    const startPositions = await startItems.evaluateAll((items) =>
        items.map((e) => ({
            left: (e as HTMLElement).style.left,
            top: (e as HTMLElement).style.top,
        })),
    );

    await step("Switch to the Correct tab", async () => {
        await clickGameTab(page, "Correct");
    });

    await step(
        "See what was made in Start, with the pictures on their targets",
        async () => {
            for (let i = 0; i < (await fixedItems.count()); i++) {
                await expect(
                    fixedItems.nth(i),
                    `In Correct, item ${i} that does not move, made in Start, should show`,
                ).toBeVisible();
            }
            await expect(
                heading,
                "In Correct, the heading typed in Start should show",
            ).toContainText(word);
            await expect(
                firstTarget,
                "In Correct, the target of the picture should show",
            ).toBeVisible();
            expect(
                await firstTarget.evaluate((t) =>
                    Array.from(t.children).map(
                        (c) => getComputedStyle(c).opacity,
                    ),
                ),
                "In Correct, the picture should show on its target, fully opaque",
            ).toContain("1");
        },
    );

    await step("Try to change what was made in Start", async () => {
        await expectCannotModify(page, heading, "the heading made in Start");
        await expectCannotModify(
            page,
            wordBox,
            "a word box made in Start",
            kWordInBox,
        );
        await expectCannotModify(page, firstTarget, "the picture's target");
    });

    // The picture added in Correct, found by its picture, so that adding the text box in Wrong,
    // which changes the positions of the page's canvas elements, does not lose it.
    const correctImage = frame
        .locator(".bloom-canvas-element.drag-item-correct")
        .filter({ has: frame.locator('img[src*="bird"]') });

    await step(
        "Add a picture in Correct, and drag, resize and crop it",
        async () => {
            const index = await dragGamePaletteItemOntoPage(page, "image", {
                xFraction: 0.8,
                yFraction: 0.3,
            });
            await expect(
                canvasElement(page, index),
                "The picture added in Correct should be marked to show only in Correct",
            ).toHaveClass(/drag-item-correct/);
            await chooseImageFile(page, IMAGE_FILE, canvasElement(page, index));
            const added = correctImage;
            await expect(
                added,
                "The picture added in Correct should be on the page, with its picture",
            ).toHaveCount(1);
            await selectCanvasElementAt(added, "the picture added in Correct");

            const before = await getStylePosition(added);
            expect(
                await dragElementBy(page, added, -40, 30),
                "Dragging the picture added in Correct should move it",
            ).not.toEqual(before);

            const { before: smaller, after: larger } =
                await dragCanvasElementCorner(page, "se", 30, 30);
            expect(
                larger.width,
                "Dragging the corner of the picture added in Correct should make it larger",
            ).toBeGreaterThan(smaller.width);

            await cropImage(page, "e", 20, added);
            expect(
                (await getImagePlacement(page, added)).cropped,
                "The picture added in Correct should be cropped",
            ).toBe(true);
        },
    );

    await step("Try again to change what was made in Start", async () => {
        await expectCannotModify(page, heading, "the heading made in Start");
        await expectCannotModify(
            page,
            wordBox,
            "a word box made in Start",
            kWordInBox,
        );
        await expectCannotModify(page, firstTarget, "the picture's target");
    });

    await step("Switch to the Wrong tab", async () => {
        await clickGameTab(page, "Wrong");
    });

    await step(
        "See the fixed items from Start, without the pictures or what Correct added",
        async () => {
            for (let i = 0; i < (await fixedItems.count()); i++) {
                await expect(
                    fixedItems.nth(i),
                    `In Wrong, item ${i} that does not move, made in Start, should show`,
                ).toBeVisible();
            }
            await expect(
                heading,
                "In Wrong, the heading typed in Start should show",
            ).toContainText(word);
            for (let i = 0; i < (await draggables.count()); i++) {
                await expect(
                    draggables.nth(i),
                    `In Wrong, draggable picture ${i} should not show`,
                ).toBeHidden();
            }
            expect(
                await firstTarget.evaluate((t) =>
                    Array.from(t.children).map(
                        (c) => getComputedStyle(c).opacity,
                    ),
                ),
                "In Wrong, the picture should not show on its target",
            ).not.toContain("1");
            await expect(
                correctImage,
                "In Wrong, the picture added in Correct should not show",
            ).toBeHidden();
            const correctItems = frame.locator(".drag-item-correct");
            for (let i = 0; i < (await correctItems.count()); i++) {
                await expect(
                    correctItems.nth(i),
                    `In Wrong, Correct item ${i} should not show`,
                ).toBeHidden();
            }
        },
    );

    await step("Try to change what was made in Start", async () => {
        await expectCannotModify(page, heading, "the heading made in Start");
        await expectCannotModify(
            page,
            wordBox,
            "a word box made in Start",
            kWordInBox,
        );
    });

    await step("Add a text box in Wrong, type in it and widen it", async () => {
        const index = await dragGamePaletteItemOntoPage(page, "none", {
            xFraction: 0.8,
            yFraction: 0.7,
        });
        const added = canvasElement(page, index);
        await expect(
            added,
            "The text box added in Wrong should be marked to show only in Wrong",
        ).toHaveClass(/drag-item-wrong/);
        await selectCanvasElementAt(added, "the text box added in Wrong");
        await typeInCanvasElement(page, "Not quite");
        const { before: narrower, after: wider } = await dragCanvasElementSide(
            page,
            "e",
            40,
        );
        expect(
            wider.width,
            "Dragging the side of the text box added in Wrong should widen it",
        ).toBeGreaterThan(narrower.width);
    });

    await step("Try again to change what was made in Start", async () => {
        await expectCannotModify(page, heading, "the heading made in Start");
        await expectCannotModify(
            page,
            wordBox,
            "a word box made in Start",
            kWordInBox,
        );
    });

    await step("Check that nothing made in Start moved", async () => {
        const startItemsNow = frame.locator(
            ".bloom-canvas-element:not(.drag-item-correct):not(.drag-item-wrong)",
        );
        expect(
            await startItemsNow.count(),
            "No item made in Start should have been added or removed",
        ).toBe(startCount);
        expect(
            await startItemsNow.evaluateAll((items) =>
                items.map((e) => ({
                    left: (e as HTMLElement).style.left,
                    top: (e as HTMLElement).style.top,
                })),
            ),
            "No item made in Start should have moved",
        ).toEqual(startPositions);
    });
});
