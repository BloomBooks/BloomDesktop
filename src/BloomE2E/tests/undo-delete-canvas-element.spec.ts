// Undoing the deletion of a canvas element (BL-17003, stage 2 of retiring CKEditor under BL-6681).
//
// Deleting a canvas element records an entry on Bloom's one undo stack. The Undo button puts the
// element back where it was in the stacking order, as the same element, so its text and its text
// editor survive; Ctrl+Y and Ctrl+Shift+Z delete it again. docs/retire-ckeditor/PLAN.md, Stage 2b,
// has the rules; the unit tests in CanvasElementDeleteUndo.test.ts cover the corner cases (a
// neighbour gone, a family renumbered) that are slow to set up here.
//
// The tests are serial because each one starts from the book the one before it left behind.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    clickCanvasElementMenuItem,
    deleteCanvasElement,
    dragPaletteItemOntoCanvas,
    getCanvasElementCount,
    selectCanvasElement,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { pressKey, typeWithKeys } from "../helpers/keys";
import { realClick } from "../helpers/realClick";
import {
    clickUndoButton,
    expectUndoMechanismCalls,
    watchUndoMechanisms,
} from "../helpers/undo";

// The Canvas tool is a Pro feature, so the collection is on the Test enterprise subscription.
test.use({
    collectionSpec: {
        name: "undo-delete-canvas-element",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

// The attribute these tests name canvas elements by. Canvas elements have no ids of their own, and
// their index changes as elements come and go, which is the subject here.
const NAME = "data-e2e-name";

// What every legacy undo mechanism should have received: nothing. A delete is undone by the one
// undo stack's own entry, not by any of them.
const NO_LEGACY_UNDO = {
    changeLayout: 0,
    readerTools: 0,
    picture: 0,
    ckeditor: 0,
};

/** The names of the canvas elements on the page's canvas, bottom of the stack first. */
async function stackingOrder(page: Page): Promise<string[]> {
    return editablePageFrame(page)
        .locator(".bloom-canvas-element")
        .evaluateAll(
            (elements, name) =>
                elements
                    .map((e) => e.getAttribute(name))
                    .filter((n): n is string => !!n),
            NAME,
        );
}

/** Drag a palette item onto the canvas and give the new element a name to find it by. */
async function addNamedElement(
    page: Page,
    item: "none" | "speech",
    name: string,
    at: { xFraction: number; yFraction: number },
): Promise<void> {
    const index = await dragPaletteItemOntoCanvas(page, item, at);
    await editablePageFrame(page)
        .locator(".bloom-canvas-element")
        .nth(index)
        .evaluate(
            (e, [attr, value]) => e.setAttribute(attr, value),
            [NAME, name],
        );
}

/** The canvas element with this name. */
function named(page: Page, name: string) {
    return editablePageFrame(page).locator(
        `.bloom-canvas-element[${NAME}="${name}"]`,
    );
}

/** Select the named canvas element by clicking it. */
async function selectNamed(page: Page, name: string): Promise<void> {
    const index = await editablePageFrame(page)
        .locator(".bloom-canvas-element")
        .evaluateAll(
            (elements, [attr, value]) =>
                elements.findIndex((e) => e.getAttribute(attr) === value),
            [NAME, name],
        );
    expect(index, `There is no canvas element named ${name}.`).toBeGreaterThan(
        -1,
    );
    await selectCanvasElement(page, index);
}

/** The text in the named element's visible text box. */
function textOf(page: Page, name: string) {
    return named(page, name).locator(".bloom-editable:visible").first();
}

/**
 * Type at the end of the named element's text, the way a person does: select the element, then a
 * real press on its text (Bloom's drawing layer lies over the text and passes the press through,
 * so Playwright's own click, which checks what is on top, would refuse), then Ctrl+End.
 */
async function typeInto(page: Page, name: string, text: string): Promise<void> {
    await selectNamed(page, name);
    await realClick(textOf(page, name));
    await pressKey(page, "Control+End");
    await typeWithKeys(page, text);
}

/** Wait until the canvas holds exactly these named elements, in this order. */
async function expectStackingOrder(
    page: Page,
    expected: string[],
    after: string,
): Promise<void> {
    await expect
        .poll(async () => stackingOrder(page), {
            timeout: 30000,
            message: `After ${after}, the canvas elements were not in the expected order.`,
        })
        .toEqual(expected);
}

test.describe("undoing a canvas element deletion", () => {
    test("builds a Canvas page with two text blocks and a speech bubble", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Canvas", 1);
        const [canvasPage] = await getContentPages(page);
        await goToPage(page, canvasPage.id);

        await addNamedElement(page, "none", "first", {
            xFraction: 0.3,
            yFraction: 0.3,
        });
        await addNamedElement(page, "none", "second", {
            xFraction: 0.6,
            yFraction: 0.4,
        });
        await addNamedElement(page, "speech", "bubble", {
            xFraction: 0.4,
            yFraction: 0.7,
        });
        await expectStackingOrder(
            page,
            ["first", "second", "bubble"],
            "adding three elements",
        );

        await typeInto(page, "second", "Hello");
        await expect(textOf(page, "second")).toHaveText("Hello");
    });

    test("the Undo button puts a deleted text block back, with its text, and it can still be typed in", async ({
        page,
    }) => {
        await watchUndoMechanisms(page);
        await selectNamed(page, "second");
        await deleteCanvasElement(page);
        await expectStackingOrder(page, ["first", "bubble"], "the delete");

        await clickUndoButton(page);

        await expectStackingOrder(
            page,
            ["first", "second", "bubble"],
            "clicking Undo",
        );
        await expect(textOf(page, "second")).toHaveText("Hello");
        await expectUndoMechanismCalls(page, NO_LEGACY_UNDO, "clicking Undo");

        // It is the same element, so its text editor is still attached to it.
        await typeInto(page, "second", " again");
        await expect(textOf(page, "second")).toHaveText("Hello again");
    });

    test("two adjacent deletes undo in turn, back to the original order", async ({
        page,
    }) => {
        await selectNamed(page, "first");
        await deleteCanvasElement(page);
        await selectNamed(page, "second");
        await deleteCanvasElement(page);
        await expectStackingOrder(page, ["bubble"], "two deletes");

        await clickUndoButton(page);
        await expectStackingOrder(page, ["second", "bubble"], "the first Undo");
        await clickUndoButton(page);

        await expectStackingOrder(
            page,
            ["first", "second", "bubble"],
            "the second Undo",
        );
    });

    test("Ctrl+Y and Ctrl+Shift+Z redo a delete", async ({ page }) => {
        await watchUndoMechanisms(page);
        await selectNamed(page, "first");
        await deleteCanvasElement(page);
        await clickUndoButton(page);
        await expectStackingOrder(
            page,
            ["first", "second", "bubble"],
            "undoing the delete",
        );

        await pressKey(page, "Control+y");
        await expectStackingOrder(page, ["second", "bubble"], "Ctrl+Y");
        await expectUndoMechanismCalls(page, { stackRedo: 1 }, "Ctrl+Y");

        await clickUndoButton(page);
        await expectStackingOrder(
            page,
            ["first", "second", "bubble"],
            "undoing again",
        );
        await pressKey(page, "Control+Shift+Z");
        await expectStackingOrder(page, ["second", "bubble"], "Ctrl+Shift+Z");
        await expectUndoMechanismCalls(page, { stackRedo: 2 }, "Ctrl+Shift+Z");

        await clickUndoButton(page);
        await expectStackingOrder(
            page,
            ["first", "second", "bubble"],
            "the last Undo",
        );
    });

    test("a speech bubble's head comes back as the head of its family", async ({
        page,
    }) => {
        await selectNamed(page, "bubble");
        const countBefore = await getCanvasElementCount(page);
        await clickCanvasElementMenuItem(
            page,
            "EditTab.Toolbox.ComicTool.Options.AddChildBubble",
        );
        await expect
            .poll(async () => getCanvasElementCount(page), {
                timeout: 30000,
                message: "Add Child Bubble did not add a canvas element.",
            })
            .toBe(countBefore + 1);
        // The child is the element that has no name yet.
        await editablePageFrame(page)
            .locator(`.bloom-canvas-element:not([${NAME}])`)
            .last()
            .evaluate((e, attr) => e.setAttribute(attr, "child"), NAME);
        const orderOf = async (name: string) =>
            named(page, name).evaluate(
                (e) =>
                    (
                        JSON.parse(
                            e.getAttribute("data-bubble")!.replace(/`/g, '"'),
                        ) as { order?: number }
                    ).order,
            );
        expect(await orderOf("bubble")).toBe(1);
        expect(await orderOf("child")).toBe(2);

        await selectNamed(page, "bubble");
        await deleteCanvasElement(page);
        await expect.poll(async () => orderOf("child")).toBe(1);

        await clickUndoButton(page);

        await expect
            .poll(async () => named(page, "bubble").count(), {
                timeout: 30000,
                message: "Undo did not put the speech bubble back.",
            })
            .toBe(1);
        expect(await orderOf("bubble")).toBe(1);
        expect(await orderOf("child")).toBe(2);
    });
});
