// Put things on a canvas page and drive the selected one: the Canvas tool's palette, the selection
// itself, and the little toolbar and menu that appear over whatever is selected.
//
// A canvas element is Bloom's name for one movable, resizable thing on a canvas page: a picture, a
// text block, a speech bubble, a video. Everything here goes through the real UI, because the
// gestures ARE the subject: an element only exists because someone dragged its icon out of the
// palette, and what can be done to it is only what its toolbar and menu offer.
//
// Ported from src/BloomBrowserUI/bookEdit/canvas-e2e-tests/helpers/, which drives the same UI in a
// browser harness rather than in Bloom. Two differences worth knowing if you compare them: that
// suite reaches into the page bundle's e2e hooks for several operations, which this one does not,
// and it retries drags up to three times, which this one does not either (a drag that needs three
// attempts is a product problem, and retries: 0 is the point of this suite).

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { type IRect } from "./geometry";
import { realClick, realClickAt } from "./realClick";
import { showToolbox, toolboxFrame } from "./toolbox";

/**
 * The palette items the Canvas tool offers, named by the canvas element type each one makes, which
 * is what the palette's test ids are built from. "none" is the plain Text Block: the type is
 * genuinely called that, because a text block is a canvas element with no special behaviour.
 */
export type PaletteItem = "image" | "video" | "speech" | "sound" | "none";

/** The controls the Canvas tool shows once it is open. */
const CANVAS_TOOL_CONTROLS = "#canvasToolControls";
/** The canvas that owns the Canvas tool; see canvas(). */
const CANVAS_SELECTOR = '.bloom-canvas[data-tool-id="canvas"]';

/** Where in the page frame the canvas element's floating toolbar and menu live. */
const TOOLBAR = "#canvas-element-context-controls";
const TOOLBAR_MENU_BUTTON = '[data-testid="canvas-context-menu-button"]';
/**
 * The menu the "..." button opens. It is kept mounted while shut, so everything here asks about
 * visibility rather than presence.
 */
const MENU = ".MuiMenu-list:visible";

/**
 * Open the Canvas tool, the way a person does on a Canvas page: by clicking the page's canvas.
 * Returns the toolbox frame once the tool's controls are showing.
 *
 * A new book's toolbox does not offer the Canvas tool, and this does not go and turn it on through
 * the toolbox's "More..." check boxes. It clicks the canvas instead, which is what Bloom itself
 * watches for: the canvas of a Canvas page carries data-tool-id="canvas", and a click on it, away
 * from any element already on it, has Bloom enable and show the Canvas tool (see
 * SetupClickToShowCanvasTool in CanvasElementManager.ts). So the page being edited must be a
 * Canvas page, such as one added with addPage(page, "Canvas"); anything else has no such canvas,
 * and this throws saying so.
 */
export async function openCanvasTool(page: Page): Promise<Frame> {
    await showToolbox(page);
    const toolbox = toolboxFrame(page);
    const controls = toolbox.locator(CANVAS_TOOL_CONTROLS).first();
    if (await controls.isVisible().catch(() => false)) return toolbox;

    const canvasBlock = canvas(page);
    if ((await canvasBlock.count()) === 0)
        throw new Error(
            'The page being edited has no canvas with data-tool-id="canvas", so there is ' +
                "nothing to click to open the Canvas tool. Add a Canvas page first.",
        );
    await canvasBlock.waitFor({ state: "visible", timeout: 30000 });
    // A real click, aimed just inside the canvas's top-left corner so it lands on the canvas
    // itself and not on an element sitting on it, which would select that element instead.
    // Playwright's own click would refuse, because Bloom lays a drawing surface over the page.
    const box = await canvasBlock.boundingBox({ timeout: 30000 });
    if (!box)
        throw new Error(
            "The canvas is visible but has no on-screen box, so there is nowhere to click.",
        );
    await realClickAt(page, box.x + 4, box.y + 4);
    await controls.waitFor({ state: "visible", timeout: 30000 });
    return toolbox;
}

/**
 * The canvas of the page being edited: the area canvas elements sit on. This is the canvas that
 * owns the Canvas tool (data-tool-id="canvas"); a picture box on an ordinary page is also a
 * bloom-canvas, but not one a person can drop palette items on.
 */
export function canvas(page: Page): Locator {
    return editablePageFrame(page).locator(CANVAS_SELECTOR).first();
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
            // The Games tool's panel carries palette items with the same test ids, and the
            // accordion keeps every tool's panel in the document, so take the Canvas tool's own
            // item, and only one that is laid out (a hidden panel's items have no rects).
            const source = Array.from(
                toolboxDocument.querySelectorAll<HTMLElement>(
                    `${what.controls} [data-testid="palette-${what.item}"]`,
                ),
            ).find((el) => el.getClientRects().length > 0);
            const target = pageDocument.querySelector<HTMLElement>(
                what.canvasSelector,
            );
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
        {
            item,
            xFraction: at.xFraction,
            yFraction: at.yFraction,
            controls: CANVAS_TOOL_CONTROLS,
            canvasSelector: CANVAS_SELECTOR,
        },
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
    // appears only once the palette has heard back from features/status, so it is missing for a
    // moment every time the tool opens.
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
 * each paired with whether it is enabled. This is how a test says "Duplicate is not on offer for
 * this element" without naming an English label.
 *
 * A command the collection's subscription tier does not reach counts as not enabled. Bloom keeps
 * such an item clickable (a click opens the Subscription settings), so it does not carry MUI's
 * disabled class; LocalizableMenuItem marks it with data-subscription-gated instead. That mark
 * arrives only once the item has asked Bloom about its feature, and until then the item looks
 * enabled, so this waits for every item to have its answer before reading.
 */
export async function getCanvasElementMenuItems(
    page: Page,
): Promise<{ id: string; enabled: boolean }[]> {
    await openCanvasElementMenu(page);
    await expect(
        editablePageFrame(page).locator(
            `${MENU} li[role="menuitem"][data-feature-status-pending]`,
        ),
        "Some menu items never learned whether their feature is on offer.",
    ).toHaveCount(0, { timeout: 30000 });
    return editablePageFrame(page)
        .locator(`${MENU} li[role="menuitem"]`)
        .evaluateAll((items) =>
            items.map((item) => ({
                id: item.getAttribute("data-testid") ?? "",
                enabled:
                    !item.classList.contains("Mui-disabled") &&
                    !item.hasAttribute("data-subscription-gated"),
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
    // Bloom answers a click on a tier-gated command by opening the WinForms Settings dialog, which
    // would hang the run (AUTOMATION-DEBT.md, "Native OS dialogs hang automation"). Refuse first.
    if (await item.evaluate((el) => el.hasAttribute("data-subscription-gated")))
        throw new Error(
            `The "${l10nId}" command is behind a subscription tier this collection does not ` +
                `have; clicking it would open the Settings dialog. Launch the collection with ` +
                `kEnterpriseSubscriptionCode if the test needs it.`,
        );
    await item.click();
    await frame
        .locator(MENU)
        .first()
        .waitFor({ state: "hidden", timeout: 30000 });
}

/**
 * Open the submenu of one row of the selected canvas element's "..." menu, such as Flip, by
 * resting the real pointer on that row, and wait until the submenu is showing beside the menu. The
 * row is named by localization id. Opens the menu first if it is shut. The submenu stays open only
 * while the pointer stays on the row or on the submenu itself.
 */
export async function openCanvasElementSubmenu(
    page: Page,
    parentL10nId: string,
): Promise<void> {
    await openCanvasElementMenu(page);
    const frame = editablePageFrame(page);
    const parent = frame
        .locator(`${MENU} li[data-testid="${parentL10nId}"]`)
        .first();
    if ((await parent.count()) === 0) {
        const offered = (await getCanvasElementMenuItems(page)).map(
            (i) => i.id,
        );
        throw new Error(
            `The canvas element menu has no "${parentL10nId}" row. It offers: ` +
                `${offered.join(", ") || "(nothing)"}.`,
        );
    }
    await parent.hover();
    // The submenu is a second menu list, drawn beside the first, and it exists only while open.
    await expect
        .poll(async () => frame.locator(MENU).count(), {
            timeout: 15000,
            message: `Resting the pointer on "${parentL10nId}" did not open its submenu.`,
        })
        .toBeGreaterThan(1);
}

/**
 * The panels of the selected canvas element's "..." menu that are showing: the menu itself, and
 * beside it the submenu of the row the pointer rests on, if one is open. For a picture of the menu.
 */
export function canvasElementMenuPanels(page: Page): Locator {
    return editablePageFrame(page).locator(".MuiMenu-paper:visible");
}

/**
 * Click one command on a submenu of the selected canvas element's "..." menu, such as Flip, then
 * Flip horizontal. Both rows are named by localization id. Opens the menu first if it is shut, and
 * waits until it has closed, so the command has been sent before this returns.
 *
 * A submenu opens only while the pointer is over its parent row (mui-nested-menu-item listens for
 * mouseenter), so this hovers the parent with the real pointer, then jumps straight to the command.
 * A path that crossed other rows on the way would close the submenu before the click arrived.
 */
export async function clickCanvasElementSubmenuItem(
    page: Page,
    parentL10nId: string,
    l10nId: string,
): Promise<void> {
    await openCanvasElementSubmenu(page, parentL10nId);
    const frame = editablePageFrame(page);
    const item = frame.locator(`${MENU} li[data-testid="${l10nId}"]`).first();
    const opened = await item
        .waitFor({ state: "visible", timeout: 15000 })
        .then(() => true)
        .catch(() => false);
    if (!opened) {
        const offered = await frame
            .locator(`${MENU} li[role="menuitem"]`)
            .evaluateAll((items) =>
                items.map((i) => i.getAttribute("data-testid") ?? ""),
            );
        throw new Error(
            `Hovering "${parentL10nId}" did not show a "${l10nId}" command. The open menus offer: ` +
                `${offered.join(", ") || "(nothing)"}.`,
        );
    }
    await item.click();
    // The click shuts the menu, but the submenu stays drawn until the pointer leaves it: its
    // parent row stays mounted in the shut menu and still counts the pointer as resting on it. A
    // person's pointer moves on at once, and so does this one.
    await page.mouse.move(5, 5);
    await frame
        .locator(MENU)
        .first()
        .waitFor({ state: "hidden", timeout: 30000 });
}

/**
 * The selected canvas element's "..." menu as the person sees it: its commands, by localization id,
 * in groups, where each group is what lies between two of the menu's dividing lines. A row that
 * opens a submenu (Flip, Transparency) is in the list under its own id; its submenu is not.
 */
export async function getCanvasElementMenuGroups(
    page: Page,
): Promise<string[][]> {
    // Waits for every item to know its feature status, so the menu has finished drawing.
    await getCanvasElementMenuItems(page);
    return editablePageFrame(page)
        .locator(`${MENU} li[role="menuitem"], ${MENU} .MuiDivider-root`)
        .evaluateAll((rows) => {
            const groups: string[][] = [[]];
            for (const row of rows) {
                if (row.classList.contains("MuiDivider-root")) groups.push([]);
                else
                    groups[groups.length - 1].push(
                        row.getAttribute("data-testid") ?? "",
                    );
            }
            return groups.filter((group) => group.length > 0);
        });
}

/**
 * Turn the selected picture a quarter turn clockwise with the Rotate right command on its "..."
 * menu. What turns is Bloom's business: an overlay picture turns as a whole box, while the page's
 * background picture turns inside its box and the box changes shape.
 */
export async function rotateSelectedImageRight(page: Page): Promise<void> {
    await clickCanvasElementMenuItem(page, "EditTab.Image.RotateRight");
}

/**
 * Mirror the selected picture with the Flip submenu of its "..." menu. "horizontal" swaps the left
 * and the right of the picture as it is seen on screen, "vertical" the top and the bottom, however
 * the picture or its box is turned.
 */
export async function flipSelectedImage(
    page: Page,
    axis: "horizontal" | "vertical",
): Promise<void> {
    await clickCanvasElementSubmenuItem(
        page,
        "EditTab.Image.Flip",
        axis === "horizontal"
            ? "EditTab.Image.FlipHorizontal"
            : "EditTab.Image.FlipVertical",
    );
}

/**
 * Set the selected picture's transparency with the Transparency submenu of its "..." menu.
 */
export async function setSelectedImageTransparency(
    page: Page,
    choice: "Auto" | "Transparent" | "Opaque",
): Promise<void> {
    await clickCanvasElementSubmenuItem(
        page,
        "EditTab.Image.Transparency",
        `EditTab.Image.Transparency.${choice}`,
    );
}

/**
 * Put the selected picture back the way it arrived with the Reset Image command on its "..." menu.
 */
export async function resetSelectedImage(page: Page): Promise<void> {
    await clickCanvasElementMenuItem(page, "EditTab.Image.Reset");
}

/** The round knob above the selected canvas element that turns it when dragged. */
function rotateHandle(page: Page): Locator {
    return editablePageFrame(page).locator(
        "#canvas-element-control-frame .bloom-ui-canvas-element-rotate-handle",
    );
}

/**
 * Wait until the selected canvas element's rotation knob is showing, or until it is not, as
 * `shown` says. Bloom offers the knob only for an element it can turn; `what` names the element
 * for the failure message.
 */
export async function expectRotateHandleShown(
    page: Page,
    shown: boolean,
    what: string,
): Promise<void> {
    await editablePageFrame(page)
        .locator("#canvas-element-control-frame")
        .waitFor({ state: "visible", timeout: 30000 });
    await expect
        .poll(async () => rotateHandle(page).isVisible(), {
            timeout: 15000,
            message: shown
                ? `The rotation knob never appeared for ${what}.`
                : `The rotation knob is showing for ${what}, which Bloom should not offer to turn.`,
        })
        .toBe(shown);
}

/**
 * The angle a canvas element is turned by, in degrees clockwise from 0 up to 360, read from the
 * rotate() in its inline style, which is what Bloom saves in the book. 0 when it has none.
 */
export async function getCanvasElementRotation(
    element: Locator,
): Promise<number> {
    return element.evaluate((el) => {
        const match = /rotate\(\s*(-?[0-9]*\.?[0-9]+)deg\s*\)/.exec(
            (el as HTMLElement).style.transform,
        );
        if (!match) return 0;
        const degrees = parseFloat(match[1]) % 360;
        return degrees < 0 ? degrees + 360 : degrees;
    });
}

/**
 * Where a canvas element sits on its canvas and how big it is, as the four numbers in its inline
 * style, in CSS pixels. A turn is about the element's centre, so these do not change when it turns,
 * and a turned element that moved on its own shows here.
 */
export async function getCanvasElementPlacement(element: Locator): Promise<{
    left: number;
    top: number;
    width: number;
    height: number;
}> {
    return element.evaluate((el) => {
        const style = (el as HTMLElement).style;
        return {
            left: parseFloat(style.left),
            top: parseFloat(style.top),
            width: parseFloat(style.width),
            height: parseFloat(style.height),
        };
    });
}

/**
 * Turn the selected canvas element by dragging its rotation knob `degrees` around the element's
 * centre, clockwise when positive, the way a person does. With `withCtrl`, Ctrl is held for the
 * drag, which stops the angle snapping to the nearest multiple of 45 degrees. Returns the angle the
 * element ends at, once it has changed.
 *
 * The pointer travels round a circle through the knob, in small steps, so that the angle Bloom
 * measures from the centre to the pointer never jumps. Bloom turns the element by however far that
 * angle moves, so where the knob starts (above the element, or below it once turned past 135
 * degrees) does not matter.
 */
export async function dragRotateHandle(
    page: Page,
    degrees: number,
    options: { withCtrl?: boolean } = {},
): Promise<number> {
    const element = activeCanvasElement(page);
    const before = await getCanvasElementRotation(element);
    const knob = await requireBox(rotateHandle(page), "the rotation knob");
    // Bloom turns about the centre of the element's on-screen box, turned or not.
    const box = await getActiveCanvasElementRect(page);
    const centreX = box.x + box.width / 2;
    const centreY = box.y + box.height / 2;
    const startX = knob.x + knob.width / 2;
    const startY = knob.y + knob.height / 2;
    const radius = Math.hypot(startX - centreX, startY - centreY);
    const startAngle = Math.atan2(startY - centreY, startX - centreX);
    const steps = Math.max(8, Math.ceil(Math.abs(degrees) / 5));

    await page.mouse.move(startX, startY);
    if (options.withCtrl) await page.keyboard.down("Control");
    // Always let go of the button and of Ctrl, even when a move fails: the tests after this one
    // drive the same Bloom, and a key held down would change every click they make.
    try {
        await page.mouse.down();
        for (let i = 1; i <= steps; i++) {
            const angle =
                startAngle + ((degrees * Math.PI) / 180) * (i / steps);
            await page.mouse.move(
                centreX + radius * Math.cos(angle),
                centreY + radius * Math.sin(angle),
            );
        }
    } finally {
        await page.mouse.up();
        if (options.withCtrl) await page.keyboard.up("Control");
    }

    await expect
        .poll(async () => getCanvasElementRotation(element), {
            timeout: 15000,
            message: `Dragging the rotation knob by ${degrees} degrees did not turn the element.`,
        })
        .not.toBe(before);
    return getCanvasElementRotation(element);
}

/**
 * Close the canvas element menu without choosing anything, the way pressing Escape does. This also
 * closes a submenu that is open beside it.
 *
 * The key is pressed on the menu itself. The menu opens without taking the keyboard focus, so a
 * bare Escape goes to whatever had the focus before, such as a text box, and the menu never sees it.
 * An open submenu is shut first, by taking the pointer off its row: while it is open it is the
 * topmost menu, and MUI lets only the topmost one answer Escape.
 */
export async function closeCanvasElementMenu(page: Page): Promise<void> {
    const frame = editablePageFrame(page);
    const menu = frame.locator(MENU).first();
    if (!(await menu.isVisible().catch(() => false))) return;
    if ((await frame.locator(MENU).count()) > 1) {
        await page.mouse.move(5, 5);
        await expect
            .poll(async () => frame.locator(MENU).count(), {
                timeout: 15000,
                message:
                    "Taking the pointer off the menu did not shut its submenu.",
            })
            .toBe(1);
    }
    await menu.press("Escape");
    await menu.waitFor({ state: "hidden", timeout: 30000 });
}

/**
 * Duplicate the selected canvas element through its "..." menu, and wait until the page has more
 * elements than it did. Returns the index the first new element takes, which is the count before
 * the duplicate, because Bloom appends.
 *
 * More, rather than exactly one more: an element can hold elements of its own, and duplicating
 * such an element adds every one of them.
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
 * Delete the selected canvas element through its "..." menu, and wait until the page has one fewer.
 */
export async function deleteCanvasElement(page: Page): Promise<void> {
    const countBefore = await getCanvasElementCount(page);
    await clickCanvasElementMenuItem(page, "Common.Delete");
    await expect
        .poll(async () => getCanvasElementCount(page), {
            timeout: 30000,
            message: `Delete did not remove a canvas element (there are still ${countBefore}).`,
        })
        .toBe(countBefore - 1);
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

/**
 * Right-click the selected canvas element's own frame, outside anything inside it. Use this to ask
 * what a right-click on the element's edge does, as opposed to a right-click on what it holds.
 *
 * The point is just inside the element's top-left corner.
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
