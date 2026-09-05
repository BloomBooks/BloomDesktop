// Put things on a canvas page and drive the selected one: the Canvas tool's palette, the selection
// itself, and the little toolbar and menu that appear over whatever is selected.
//
// A canvas element is Bloom's name for one movable, resizable thing on a canvas page: a picture, a
// text block, a speech bubble, a table. Everything here goes through the real UI, because the
// gestures ARE the subject: a table only exists because someone dragged the Table icon out of the
// palette, and a table is only frozen or free depending on what its toolbar offers.
//
// Ported from src/BloomBrowserUI/bookEdit/canvas-e2e-tests/helpers/, which drives the same UI in a
// browser harness rather than in Bloom. Two differences worth knowing if you compare them: that
// suite reaches into the page bundle's e2e hooks for several operations, which this one does not,
// and it retries drags up to three times, which this one does not either (a drag that needs three
// attempts is a product problem, and retries: 0 is the point of this suite).

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { type IRect } from "./geometry";
import { realClick } from "./realClick";
import { openTool } from "./toolbox";

/**
 * The palette items the Canvas tool offers, named by the canvas element type each one makes, which
 * is what the palette's test ids are built from. "none" is the plain Text Block: the type is
 * genuinely called that, because a text block is a canvas element with no special behaviour.
 */
export type PaletteItem =
    | "table"
    | "image"
    | "video"
    | "speech"
    | "sound"
    | "none";

/** Where in the page frame the canvas element's floating toolbar and menu live. */
const TOOLBAR = "#canvas-element-context-controls";
const TOOLBAR_MENU_BUTTON = '[data-testid="canvas-context-menu-button"]';
/**
 * The menu the "..." button opens. It is kept mounted while shut, so everything here asks about
 * visibility rather than presence.
 */
const MENU = ".MuiMenu-list:visible";

/**
 * Open the Canvas tool in the toolbox, opening the toolbox first if it is shut, and wait until the
 * page being edited has a canvas to drop things on.
 */
export async function openCanvasTool(page: Page): Promise<Frame> {
    const toolbox = await openTool(page, "canvas", "#canvasToolControls");
    await canvas(page).waitFor({ state: "visible", timeout: 30000 });
    return toolbox;
}

/** The canvas of the page being edited: the area canvas elements sit on. */
export function canvas(page: Page): Locator {
    return editablePageFrame(page).locator(".bloom-canvas").first();
}

/** Every canvas element on the page being edited, in document order. */
export function canvasElements(page: Page): Locator {
    return editablePageFrame(page).locator(".bloom-canvas-element");
}

/** How many canvas elements the page being edited holds. */
export async function getCanvasElementCount(page: Page): Promise<number> {
    return canvasElements(page).count();
}

/** The nth canvas element on the page being edited, counting from 0 in document order. */
export function canvasElement(page: Page, index: number): Locator {
    return canvasElements(page).nth(index);
}

/**
 * The canvas element that is selected. Bloom marks it `data-bloom-active`; there is no class.
 * Throws when nothing is selected, which is nearly always a sign that a click landed elsewhere.
 */
export function activeCanvasElement(page: Page): Locator {
    return editablePageFrame(page)
        .locator('.bloom-canvas-element[data-bloom-active="true"]')
        .first();
}

/**
 * Raise the drag-and-drop events a person's drag from the palette to the canvas raises: dragstart
 * on the palette item, dragover and drop on the canvas, dragend back on the palette item. One
 * DataTransfer is shared by all four, as the browser does, because the product's handlers read
 * back what dragstart put in it.
 *
 * These are dispatched rather than driven with the real mouse, which is what a helper here would
 * normally do. A palette item is an HTML5 draggable, and pressing the real mouse on one hands the
 * drag to Windows, which runs it in a modal loop of its own: the renderer stops answering, and the
 * automation's next call never returns. It cost five minutes of a run when it happened, about one
 * run in ten. Playwright's own dragTo wedges the same way, because it too begins with a real
 * press. What this skips is the operating system's part of the drag; every line of Bloom's that a
 * real drag runs, this runs too (see ondragstart and ondragend in CanvasElementItem.tsx and
 * setDragAndDropHandlers in CanvasElementManager.ts). It is listed in AUTOMATION-DEBT.md.
 *
 * It runs in Bloom's shell document, because that is the only place both frames are in reach: the
 * palette lives in the toolbox frame and the canvas in the page frame, and one DataTransfer has to
 * pass between them.
 */
async function dispatchPaletteDrag(
    page: Page,
    item: PaletteItem,
    at: { xFraction: number; yFraction: number },
): Promise<void> {
    const failure = await page.evaluate(
        (what) => {
            // The frames are not always children of this document (Bloom nests the Edit tab's
            // frames), so walk the tree by name rather than reading window.frames.
            const documentOfFrame = (
                name: string,
                from: Document = document,
            ): Document | undefined => {
                for (const iframe of Array.from(
                    from.querySelectorAll("iframe"),
                )) {
                    const inner = iframe.contentDocument;
                    if (!inner) continue;
                    if (iframe.name === name || iframe.id === name)
                        return inner;
                    const deeper = documentOfFrame(name, inner);
                    if (deeper) return deeper;
                }
                return undefined;
            };
            const toolboxDocument = documentOfFrame("toolbox");
            const pageDocument = documentOfFrame("page");
            if (!toolboxDocument || !pageDocument)
                return (
                    `Bloom is not showing both the toolbox and the page ` +
                    `(toolbox: ${!!toolboxDocument}, page: ${!!pageDocument}).`
                );
            const source = toolboxDocument.querySelector<HTMLElement>(
                `[data-testid="palette-${what.item}"]`,
            );
            const target =
                pageDocument.querySelector<HTMLElement>(".bloom-canvas");
            if (!source) return `The palette has no "${what.item}" item.`;
            if (!target) return "The page has no canvas.";

            const transfer = new DataTransfer();
            const fire = (
                element: HTMLElement,
                type: string,
                clientX: number,
                clientY: number,
            ) => {
                const view = element.ownerDocument.defaultView;
                if (!view) throw new Error("A document with no window.");
                element.dispatchEvent(
                    new view.DragEvent(type, {
                        bubbles: true,
                        cancelable: true,
                        composed: true,
                        dataTransfer: transfer,
                        clientX,
                        clientY,
                    }),
                );
            };

            const sourceRect = source.getBoundingClientRect();
            fire(
                source,
                "dragstart",
                sourceRect.left + sourceRect.width / 2,
                sourceRect.top + sourceRect.height / 2,
            );
            // The drop point, in the page frame's own coordinates, which is what the product's
            // handlers work in (CanvasElementFactories.addCanvasElement calls
            // document.elementsFromPoint with them).
            const targetRect = target.getBoundingClientRect();
            const x = targetRect.left + targetRect.width * what.xFraction;
            const y = targetRect.top + targetRect.height * what.yFraction;
            fire(target, "dragover", x, y);
            fire(target, "drop", x, y);
            fire(source, "dragend", x, y);
            return "";
        },
        { item, xFraction: at.xFraction, yFraction: at.yFraction },
    );
    if (failure) throw new Error(`Could not drag the ${item} item: ${failure}`);
}

/**
 * Drag one palette item onto the canvas of the page being edited, the way a person does, and wait
 * until the page has one more canvas element than it did. Returns the new element's index, which is
 * the count before the drag, because Bloom appends.
 *
 * `at` places the drop within the canvas, as a fraction of its width and height, so it is
 * independent of how large the window is. It defaults to the middle. Two elements dropped at the
 * same place would sit on top of each other, so a test that wants two should say where each goes.
 */
export async function dragPaletteItemOntoCanvas(
    page: Page,
    item: PaletteItem,
    at: { xFraction: number; yFraction: number } = {
        xFraction: 0.5,
        yFraction: 0.5,
    },
): Promise<number> {
    const toolbox = await openCanvasTool(page);
    const source = toolbox.locator(`[data-testid="palette-${item}"]:visible`);
    // Wait rather than count once: an item that is behind a subscription tier or an experiment
    // (the table is behind both) appears only once the palette has heard back from
    // features/status, so it is missing for a moment every time the tool opens.
    const appeared = await source
        .first()
        .waitFor({ state: "visible", timeout: 20000 })
        .then(() => true)
        .catch(() => false);
    if (!appeared) {
        const offered = await toolbox
            .locator("[data-testid^=palette-]:visible")
            .evaluateAll((els) =>
                els.map((e) => e.getAttribute("data-testid") ?? ""),
            );
        throw new Error(
            `The Canvas tool's palette does not offer "${item}". It offers: ` +
                `${offered.join(", ") || "(nothing)"}. An item whose feature is off, or whose ` +
                `subscription tier the collection does not have, is simply absent.`,
        );
    }
    await canvas(page).waitFor({ state: "visible", timeout: 30000 });
    const countBefore = await getCanvasElementCount(page);
    await dispatchPaletteDrag(page, item, at);

    await expect
        .poll(async () => getCanvasElementCount(page), {
            timeout: 30000,
            message:
                `Dragging the ${item} palette item onto the canvas did not add a canvas element ` +
                `(there are still ${countBefore}).`,
        })
        .toBe(countBefore + 1);
    return countBefore;
}

/**
 * Select a canvas element by clicking it, and wait until Bloom marks it active. Returns it.
 *
 * The click is a real mouse press, not Playwright's own: Bloom lays a drawing surface over the
 * page, so Playwright would refuse to click an element it sees something else covering.
 */
export async function selectCanvasElement(
    page: Page,
    index: number,
): Promise<Locator> {
    const element = canvasElement(page, index);
    await element.waitFor({ state: "visible", timeout: 30000 });
    await realClick(element);
    await expect(
        element,
        `Clicking canvas element ${index} did not select it.`,
    ).toHaveAttribute("data-bloom-active", "true", { timeout: 15000 });
    return element;
}

/** Where on the page a locator is drawn. Throws naming it when it has no on-screen box. */
export async function getRect(locator: Locator, what: string): Promise<IRect> {
    return requireBox(locator, what);
}

/** Where on the page the canvas of the page being edited is drawn. */
export async function getCanvasRect(page: Page): Promise<IRect> {
    return requireBox(canvas(page), "the canvas of the page being edited");
}

/** Where on the page the selected canvas element is drawn. */
export async function getActiveCanvasElementRect(page: Page): Promise<IRect> {
    return requireBox(activeCanvasElement(page), "the selected canvas element");
}

/** Where on the page the selected canvas element's floating toolbar is drawn. */
export async function getCanvasElementToolbarRect(page: Page): Promise<IRect> {
    // Measure the row of buttons, not the container: the container is a full-width layer with
    // pointer-events off, so its rect says nothing about where a person can click.
    return requireBox(
        editablePageFrame(page).locator(`${TOOLBAR}:visible button`).first(),
        "the canvas element toolbar",
    );
}

/**
 * How many buttons the selected canvas element's toolbar shows.
 *
 * A count rather than a list of which ones, because the toolbar's buttons carry no id, class,
 * label or test id — only an SVG icon and a tooltip — and they are in a file this task may not
 * change. To assert on a NAMED command, use the "..." menu instead, whose items are identified by
 * their localization id. (AUTOMATION-DEBT.md: "Canvas element toolbar buttons are anonymous".)
 */
export async function getCanvasElementToolbarButtonCount(
    page: Page,
): Promise<number> {
    await waitForCanvasElementToolbar(page);
    return editablePageFrame(page).locator(`${TOOLBAR}:visible button`).count();
}

/** Wait until the selected canvas element's floating toolbar is showing. */
export async function waitForCanvasElementToolbar(page: Page): Promise<void> {
    await editablePageFrame(page)
        .locator(`${TOOLBAR}:visible`)
        .first()
        .waitFor({ state: "visible", timeout: 30000 });
}

/**
 * Open the selected canvas element's "..." menu and wait until it is showing. Does nothing when it
 * is open already.
 */
export async function openCanvasElementMenu(page: Page): Promise<void> {
    const frame = editablePageFrame(page);
    if (
        await frame
            .locator(MENU)
            .first()
            .isVisible()
            .catch(() => false)
    )
        return;
    await waitForCanvasElementToolbar(page);
    await frame.locator(`${TOOLBAR} ${TOOLBAR_MENU_BUTTON}`).click();
    await frame
        .locator(MENU)
        .first()
        .waitFor({ state: "visible", timeout: 30000 });
}

/**
 * The localization ids of the commands the selected canvas element's "..." menu offers, in order,
 * each paired with whether it is enabled. This is how a test says "Duplicate is not on offer for a
 * table here" without naming an English label.
 */
export async function getCanvasElementMenuItems(
    page: Page,
): Promise<{ id: string; enabled: boolean }[]> {
    await openCanvasElementMenu(page);
    return editablePageFrame(page)
        .locator(`${MENU} li[role="menuitem"]`)
        .evaluateAll((items) =>
            items.map((item) => ({
                id: item.getAttribute("data-testid") ?? "",
                enabled: !item.classList.contains("Mui-disabled"),
            })),
        );
}

/**
 * Click one command in the selected canvas element's "..." menu, by its localization id (e.g.
 * "EditTab.Toolbox.ComicTool.Options.Duplicate"), opening the menu first if it is shut. Waits until
 * the menu has closed, so the command has been sent before this returns.
 */
export async function clickCanvasElementMenuItem(
    page: Page,
    l10nId: string,
): Promise<void> {
    await openCanvasElementMenu(page);
    const frame = editablePageFrame(page);
    const item = frame.locator(`${MENU} li[data-testid="${l10nId}"]`).first();
    if ((await item.count()) === 0) {
        const offered = (await getCanvasElementMenuItems(page)).map(
            (i) => i.id,
        );
        throw new Error(
            `The canvas element menu has no "${l10nId}" command. It offers: ` +
                `${offered.join(", ") || "(nothing)"}.`,
        );
    }
    await item.click();
    await frame
        .locator(MENU)
        .first()
        .waitFor({ state: "hidden", timeout: 30000 });
}

/** Close the canvas element menu without choosing anything, the way pressing Escape does. */
export async function closeCanvasElementMenu(page: Page): Promise<void> {
    const menu = editablePageFrame(page).locator(MENU).first();
    if (!(await menu.isVisible().catch(() => false))) return;
    await page.keyboard.press("Escape");
    await menu.waitFor({ state: "hidden", timeout: 30000 });
}

/**
 * Duplicate the selected canvas element through its "..." menu, and wait until the page has more
 * elements than it did. Returns the index the first new element takes, which is the count before
 * the duplicate, because Bloom appends.
 *
 * More, rather than exactly one more: an element can hold elements of its own. A table with a
 * picture in one of its cells is itself a canvas element containing another, so duplicating it
 * adds two.
 */
export async function duplicateCanvasElement(page: Page): Promise<number> {
    const countBefore = await getCanvasElementCount(page);
    await clickCanvasElementMenuItem(
        page,
        "EditTab.Toolbox.ComicTool.Options.Duplicate",
    );
    await expect
        .poll(async () => getCanvasElementCount(page), {
            timeout: 30000,
            message: `Duplicate did not add a canvas element (there are still ${countBefore}).`,
        })
        .toBeGreaterThan(countBefore);
    return countBefore;
}

/**
 * Delete the selected canvas element through its "..." menu, and wait until the page has fewer.
 *
 * Fewer rather than one fewer: an element can hold others, and deleting it takes those with it. A
 * table whose cell holds a table is the case that shows this, going from three elements to one.
 */
export async function deleteCanvasElement(page: Page): Promise<void> {
    const countBefore = await getCanvasElementCount(page);
    await clickCanvasElementMenuItem(page, "Common.Delete");
    await expect
        .poll(async () => getCanvasElementCount(page), {
            timeout: 30000,
            message: `Delete did not remove a canvas element (there are still ${countBefore}).`,
        })
        .toBeLessThan(countBefore);
}

/** The corners of a selected canvas element, by the names its resize handles use. */
export type Corner = "nw" | "ne" | "sw" | "se";

/**
 * Resize the selected canvas element by dragging one of its corner handles, and wait until it has
 * changed size. `dx` and `dy` are how far the corner moves, in the page's own pixels.
 *
 * Returns the element's rect before and after, so the caller can say what should have changed.
 */
export async function dragCanvasElementCorner(
    page: Page,
    corner: Corner,
    dx: number,
    dy: number,
): Promise<{ before: IRect; after: IRect }> {
    const frame = editablePageFrame(page);
    const handle = frame.locator(
        `#canvas-element-control-frame .bloom-ui-canvas-element-resize-handle-${corner}`,
    );
    await handle.waitFor({ state: "visible", timeout: 30000 });
    const before = await getActiveCanvasElementRect(page);
    const box = await requireBox(handle, `the ${corner} resize handle`);
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + dx, y + dy, { steps: 12 });
    await page.mouse.up();
    await expect
        .poll(
            async () => {
                const now = await getActiveCanvasElementRect(page);
                return (
                    Math.abs(now.width - before.width) +
                    Math.abs(now.height - before.height)
                );
            },
            {
                timeout: 30000,
                message:
                    `Dragging the ${corner} handle by ${dx},${dy} did not change the canvas ` +
                    `element's size.`,
            },
        )
        .toBeGreaterThan(2);
    return { before, after: await getActiveCanvasElementRect(page) };
}

/** Which side handle to drag, as the handles themselves are named. */
export type Side = "n" | "e" | "s" | "w";

/**
 * Resize the selected canvas element by dragging one of its side handles, and wait until it has
 * changed size. `distance` is how far the handle moves, in the page's own pixels: outwards for a
 * positive number on the east or south side, and for a negative one on the west or north.
 *
 * A canvas element holding text, which a table does, is resized this way rather than by its
 * corners: Bloom hides the corner handles for such an element (`has-text` in `editMode.less`) and
 * shows the east and west ones instead, plus the south one when the element's height is not
 * automatic.
 *
 * Returns the element's rect before and after, so the caller can say what should have changed.
 */
export async function dragCanvasElementSide(
    page: Page,
    side: Side,
    distance: number,
): Promise<{ before: IRect; after: IRect }> {
    const frame = editablePageFrame(page);
    const handle = frame.locator(
        `#canvas-element-control-frame .bloom-ui-canvas-element-side-handle-${side}`,
    );
    await handle.waitFor({ state: "visible", timeout: 30000 });
    const before = await getActiveCanvasElementRect(page);
    const box = await requireBox(handle, `the ${side} side handle`);
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    const horizontal = side === "e" || side === "w";
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(
        horizontal ? x + distance : x,
        horizontal ? y : y + distance,
        { steps: 12 },
    );
    await page.mouse.up();
    await expect
        .poll(
            async () => {
                const now = await getActiveCanvasElementRect(page);
                return (
                    Math.abs(now.width - before.width) +
                    Math.abs(now.height - before.height)
                );
            },
            {
                timeout: 30000,
                message:
                    `Dragging the ${side} handle by ${distance} did not change the canvas ` +
                    `element's size.`,
            },
        )
        .toBeGreaterThan(2);
    return { before, after: await getActiveCanvasElementRect(page) };
}

/**
 * Right-click the selected canvas element's own frame, outside anything inside it. Use this to ask
 * what a right-click on the table's edge does, as opposed to a right-click in one of its cells.
 *
 * The point is just inside the element's top-left corner, which for a table is its border and gap
 * rather than a cell.
 */
export async function rightClickCanvasElementEdge(page: Page): Promise<void> {
    const rect = await getActiveCanvasElementRect(page);
    await page.mouse.move(rect.x + 2, rect.y + 2);
    await page.mouse.click(rect.x + 2, rect.y + 2, { button: "right" });
}

async function requireBox(locator: Locator, what: string): Promise<IRect> {
    await locator.waitFor({ state: "visible", timeout: 30000 });
    const box = await locator.boundingBox({ timeout: 30000 });
    if (!box)
        throw new Error(
            `${what} is visible but has no on-screen box, so there is nowhere to measure or ` +
                `click. It may be inside a collapsed or zero-size container.`,
        );
    return box;
}
