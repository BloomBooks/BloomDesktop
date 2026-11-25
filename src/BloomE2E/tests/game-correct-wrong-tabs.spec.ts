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
    getPictureOnTarget,
    getTabItemsCovering,
    makeBookWithGamePage,
    selectTextBoxInsideAnotherItem,
    type GameTab,
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

/** Where a canvas element or target sits and how big it is, as its style records them. */
interface IStylePlace {
    left: string;
    top: string;
    width: string;
    height: string;
}

/** The place of the one item `element` locates. */
async function getStylePlace(element: Locator): Promise<IStylePlace> {
    return (await getStylePlaces(element))[0];
}

/** The place of each item `elements` locates, in document order. */
async function getStylePlaces(elements: Locator): Promise<IStylePlace[]> {
    return elements.evaluateAll((items) =>
        items.map((e) => {
            const style = (e as HTMLElement).style;
            return {
                left: style.left,
                top: style.top,
                width: style.width,
                height: style.height,
            };
        }),
    );
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

/** Where on an item to press, as fractions of its width and height from its top-left corner. */
interface IPressPoint {
    xFraction: number;
    yFraction: number;
}

/**
 * Near the left edge of an item, half-way down it. The Correct and Wrong tabs each lay a panel of
 * their own over the middle of the page, which covers most of each word box, word and target. A
 * press on the covered part goes to the panel, so a press meant for an item made in Start goes
 * here, on the part that sticks out to the left of the panel. expectCannotModify checks that the
 * point really is clear.
 */
const kClearOfTabPanel: IPressPoint = { xFraction: 0.08, yFraction: 0.5 };
/** The middle of an item. */
const kMiddle: IPressPoint = { xFraction: 0.5, yFraction: 0.5 };

/** The point of the window at `at` on an item. `what` names the item in the failure message. */
async function pointOn(
    item: Locator,
    what: string,
    at: IPressPoint,
): Promise<{ x: number; y: number }> {
    const box = await item.boundingBox();
    if (!box) throw new Error(`${what} has no box on screen to press.`);
    return {
        x: box.x + box.width * at.xFraction,
        y: box.y + box.height * at.yFraction,
    };
}

/**
 * Check that a person cannot change an item made in Start, from the tab showing: a click on it
 * neither selects it nor puts the insertion point or focus in it, and dragging it leaves it where
 * it was. `what` names it in the failure messages; `at` is where on it to press.
 */
async function expectCannotModify(
    page: Page,
    item: Locator,
    what: string,
    at: IPressPoint,
): Promise<void> {
    const { x, y } = await pointOn(item, what, at);
    expect(
        await getTabItemsCovering(page, x, y),
        `Sanity check: the press on ${what} should land on it, not on an item of the tab showing that covers it there`,
    ).toEqual([]);
    const before = await getStylePlace(item);
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
        await getStylePlace(item),
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
): Promise<IStylePlace> {
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
    return getStylePlace(element);
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
    // Items in Start that stay where they are in the game: the heading, the boxes that the
    // pictures go to, and the word in each box.
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
    const wordLabel = startItems
        .filter({ has: frame.locator(".bloom-editable") })
        .and(frame.locator(":not(.bloom-passive-element)"))
        .first();
    // The picture's target is the leftmost one, the first in the page, because part of it sticks
    // out from under the panels of the Correct and Wrong tabs, where it can be pressed. Bloom
    // pairs a new page's targets with its pictures at random, so the picture to fill in is
    // whichever one that target is for.
    const pictureTarget = frame.locator("[data-target-of]").first();
    const pictureItem = frame.locator(
        `[data-draggable-id="${await pictureTarget.getAttribute("data-target-of")}"]`,
    );
    const headingText = "Birds";
    const word = "robin";

    await step("Fill in the heading, a word and a picture", async () => {
        expect(
            await draggables.count(),
            "Sanity check: the page should have draggable pictures",
        ).toBeGreaterThan(0);
        await expect(
            wordBox,
            "Sanity check: the page should have boxes for the words",
        ).toBeVisible();
        // Sanity check: in Start, a click on a word box, at the point the other tabs are pressed
        // at, selects it, so a click that does not in the other tabs is one that Bloom refused.
        const onWordBox = await pointOn(
            wordBox,
            "a word box",
            kClearOfTabPanel,
        );
        await realClickAt(page, onWordBox.x, onWordBox.y);
        await expect(
            wordBox,
            "Sanity check: in Start, clicking a word box should select it",
        ).toHaveAttribute("data-bloom-active", "true");
        await selectCanvasElementAt(heading, "the heading");
        await typeInCanvasElement(page, headingText);
        await selectTextBoxInsideAnotherItem(
            page,
            wordLabel,
            "the word in a word box",
        );
        await typeInCanvasElement(page, word);
        // Sanity check: in Start, a click on the word, at the point the other tabs are pressed at,
        // puts the insertion point in it. Clicking the heading first takes it out of the word.
        await selectCanvasElementAt(heading, "the heading");
        expect(
            await hasCaretOrFocusInside(wordLabel),
            "Sanity check: with the heading selected, the insertion point should not be in the word",
        ).toBe(false);
        const onWord = await pointOn(
            wordLabel,
            "the word in a word box",
            kClearOfTabPanel,
        );
        await realClickAt(page, onWord.x, onWord.y);
        await realClickAt(page, onWord.x, onWord.y);
        await expect
            .poll(() => hasCaretOrFocusInside(wordLabel), {
                message:
                    "Sanity check: in Start, clicking the word (once for its box, once for the word) should put the insertion point in it",
            })
            .toBe(true);
        await expect(
            pictureItem,
            "Sanity check: the leftmost target should be the target of one draggable picture",
        ).toHaveCount(1);
        await chooseImageFile(page, IMAGE_FILE, pictureItem);
    });

    const startCount = await startItems.count();
    // The heading as Start leaves it: the template's own text with what was typed after it.
    const startHeadingText = (await heading.textContent())!;
    expect(
        startHeadingText,
        "Sanity check: the heading should hold what was typed in Start",
    ).toContain(headingText);
    // Everything made in Start except the draggable pictures, which only Start and Play show.
    const fixedItems = frame.locator(
        ".bloom-canvas-element:not(.drag-item-correct):not(.drag-item-wrong):not([data-draggable-id])",
    );
    // Everything made in Start, and the targets, which are not canvas elements.
    const startItemsAndTargets = frame.locator(
        ".bloom-canvas-element:not(.drag-item-correct):not(.drag-item-wrong), [data-target-of]",
    );
    const startPlaces = await getStylePlaces(startItemsAndTargets);
    const pictureFile = Path.basename(IMAGE_FILE);

    /**
     * Check, from the Correct or Wrong tab, that none of the kinds of item made in Start can be
     * changed: the heading, a word box, the word in it, and a picture's target.
     */
    const expectStartItemsCannotBeModified = async (
        tab: GameTab,
    ): Promise<void> => {
        await expectCannotModify(
            page,
            heading,
            `in ${tab}, the heading made in Start`,
            kMiddle,
        );
        await expectCannotModify(
            page,
            wordBox,
            `in ${tab}, a word box made in Start`,
            kClearOfTabPanel,
        );
        await expectCannotModify(
            page,
            wordLabel,
            `in ${tab}, the word typed in Start`,
            kClearOfTabPanel,
        );
        await expectCannotModify(
            page,
            pictureTarget,
            `in ${tab}, the picture's target`,
            kClearOfTabPanel,
        );
        await expect(
            wordLabel,
            `In ${tab}, the word typed in Start should be as it was typed`,
        ).toHaveText(word);
    };

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
            ).toContainText(headingText);
            await expect(
                wordLabel,
                "In Correct, the word typed in Start should show",
            ).toHaveText(word);
            await expect(
                pictureTarget,
                "In Correct, the target of the picture should show",
            ).toBeVisible();
            // The target holds a copy of its item's picture, a placeholder until the item has one.
            await expect
                .poll(() => getPictureOnTarget(pictureTarget), {
                    message:
                        "In Correct, the picture chosen in Start should show on its target, loaded and fully opaque",
                })
                .toEqual({
                    fileName: pictureFile,
                    loaded: true,
                    laidOut: true,
                    opacity: 1,
                });
        },
    );

    await step("Try to change what was made in Start", async () => {
        await expectStartItemsCannotBeModified("Correct");
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

            const before = await getStylePlace(added);
            const dragged = await dragElementBy(page, added, -40, 30);
            expect(
                dragged.left,
                "Dragging the picture added in Correct to the left should move it sideways",
            ).not.toBe(before.left);
            expect(
                dragged.top,
                "Dragging the picture added in Correct downwards should move it down",
            ).not.toBe(before.top);
            expect(
                { width: dragged.width, height: dragged.height },
                "Dragging the picture added in Correct should not change its size",
            ).toEqual({ width: before.width, height: before.height });

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
        await expectStartItemsCannotBeModified("Correct");
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
            ).toContainText(headingText);
            await expect(
                wordLabel,
                "In Wrong, the word typed in Start should show",
            ).toHaveText(word);
            for (let i = 0; i < (await draggables.count()); i++) {
                await expect(
                    draggables.nth(i),
                    `In Wrong, draggable picture ${i} should not show`,
                ).toBeHidden();
            }
            // Not even faintly: Start shows a shadow of the picture on its target, and Wrong must
            // not give the answer away.
            // However Bloom hides it: a picture that takes up no room is not seen either.
            const inWrong = await getPictureOnTarget(pictureTarget);
            expect(
                inWrong.laidOut ? inWrong.opacity : 0,
                "In Wrong, the picture chosen in Start should not show on its target at all",
            ).toBe(0);
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
        await expectStartItemsCannotBeModified("Wrong");
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
        await expectStartItemsCannotBeModified("Wrong");
    });

    await step("Check that nothing made in Start changed", async () => {
        expect(
            await startItems.count(),
            "No item made in Start should have been added or removed",
        ).toBe(startCount);
        expect(
            await getStylePlaces(startItemsAndTargets),
            "No item made in Start, and no target, should have moved or changed size",
        ).toEqual(startPlaces);
        await expect(
            heading,
            "The heading should be as it was typed in Start",
        ).toHaveText(startHeadingText);
        await expect(
            wordLabel,
            "The word should be as it was typed in Start",
        ).toHaveText(word);
    });
});
