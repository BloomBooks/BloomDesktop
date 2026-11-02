// The stacking order of the canvas elements on a canvas page, and the Layer commands that change
// it: the "Layer" submenu of the selected element's "..." menu, and the keyboard shortcuts that
// menu names.
//
// Which element is drawn in front of which is the order of the elements in the page's DOM: a later
// one covers an earlier one. Speech bubbles are also drawn by ComicalJS, which orders them by the
// `level` in each element's data-bubble, so Bloom keeps those levels in step with the DOM order.
// The stack this module reads reports both, so a test can say that the two agree.
//
// A canvas element has no id of its own, and a Layer command moves it to another place in the
// DOM, so "the second element" means a different element after every move. This module names an
// element instead by where it sits on the page (its left and top), which no Layer command changes.
// A test that wants several elements drops them at different places, which it must anyway to see
// them.

import { expect, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import {
    activeCanvasElement,
    canvas,
    canvasElement,
    clickCanvasElementSubmenuItem,
    closeCanvasElementMenu,
    getCanvasElementSubmenuItems,
    getRect,
    selectCanvasElement,
} from "./canvasElements";
import { type IRect } from "./geometry";
import { pressKey } from "./keys";
import { realClickAt } from "./realClick";

/** The four commands of the Layer submenu. */
export type LayerCommand =
    | "Bring Forward"
    | "Bring to Front"
    | "Send Backwards"
    | "Send to Back";

/** The Layer submenu's own row in the "..." menu. */
const LAYER_SUBMENU = "EditTab.Toolbox.CanvasTool.Layer";

/** Each command's localization id (its test id) and the shortcut the menu shows for it. */
const LAYER_COMMANDS: Record<LayerCommand, { l10nId: string; key: string }> = {
    "Bring Forward": {
        l10nId: "EditTab.Toolbox.CanvasTool.Layer.BringForward",
        key: "Control+BracketRight",
    },
    "Bring to Front": {
        l10nId: "EditTab.Toolbox.CanvasTool.Layer.BringToFront",
        key: "Control+Shift+BracketRight",
    },
    "Send Backwards": {
        l10nId: "EditTab.Toolbox.CanvasTool.Layer.SendBackward",
        key: "Control+BracketLeft",
    },
    "Send to Back": {
        l10nId: "EditTab.Toolbox.CanvasTool.Layer.SendToBack",
        key: "Control+Shift+BracketLeft",
    },
};

/** One movable canvas element, as the stack reports it. */
export interface IStackedCanvasElement {
    /** Where the element sits on the page, as "left,top"; the name this module gives it. */
    id: string;
    /** The ComicalJS level in its data-bubble, or undefined when it has none. */
    level: number | undefined;
    /** Whether it is the selected element. */
    selected: boolean;
}

/**
 * The canvas elements of the page being edited that a Layer command can move, back-most first:
 * every element on the page's canvas except the background image, which always stays at the very
 * back. A child bubble counts as an element of its own; it shares its parent's level.
 */
export async function getCanvasElementStack(
    page: Page,
): Promise<IStackedCanvasElement[]> {
    return canvas(page).evaluate((bloomCanvas) =>
        Array.from(bloomCanvas.children)
            .filter(
                (child): child is HTMLElement =>
                    child instanceof HTMLElement &&
                    child.classList.contains("bloom-canvas-element") &&
                    !child.classList.contains("bloom-backgroundImage"),
            )
            .map((element) => {
                const bubble = element.getAttribute("data-bubble");
                const level = bubble
                    ? JSON.parse(bubble.replace(/`/g, '"')).level
                    : undefined;
                return {
                    id: `${parseFloat(element.style.left)},${parseFloat(element.style.top)}`,
                    level: typeof level === "number" ? level : undefined,
                    selected:
                        element.getAttribute("data-bloom-active") === "true",
                };
            }),
    );
}

/** The ids of the movable canvas elements of the page being edited, back-most first. */
export async function getStackingOrder(page: Page): Promise<string[]> {
    return (await getCanvasElementStack(page)).map((e) => e.id);
}

/** The id of the selected canvas element, or undefined when it is not a movable one. */
export async function getSelectedStackedElement(
    page: Page,
): Promise<string | undefined> {
    return (await getCanvasElementStack(page)).find((e) => e.selected)?.id;
}

/**
 * The index, among every canvas element of the page in document order, of the movable one named
 * `id`; the kind of index canvasElement() and selectCanvasElement() take. The background image is
 * at 0, so a movable element's index is its place in the stack plus one.
 */
async function indexOf(page: Page, id: string): Promise<number> {
    const order = await getStackingOrder(page);
    const place = order.indexOf(id);
    if (place < 0)
        throw new Error(
            `No movable canvas element sits at ${id}. The page has: ${order.join(" | ") || "(none)"}.`,
        );
    return place + 1;
}

/**
 * Select the movable canvas element named `id`, with a real click, as selectCanvasElement does.
 * Does nothing when it is selected already: a second click on a selected bubble puts the caret in
 * its text, after which a Layer shortcut would go to the text.
 *
 * The click lands in the middle of the element, so that middle must not be covered by another
 * element, nor by a speech bubble's outline, which ComicalJS draws wider than the bubble's text
 * box. A test that wants elements to overlap lets them overlap only at their ends.
 */
export async function selectStackedCanvasElement(
    page: Page,
    id: string,
): Promise<void> {
    if ((await getSelectedStackedElement(page)) === id) return;
    await selectCanvasElement(page, await indexOf(page, id));
}

/** Where on the screen the movable canvas element named `id` is drawn. */
export async function getStackedCanvasElementRect(
    page: Page,
    id: string,
): Promise<IRect> {
    return getRect(
        canvasElement(page, await indexOf(page, id)),
        `the canvas element at ${id}`,
    );
}

/**
 * Click, for real, the middle of the area where two movable canvas elements overlap, the way a
 * person picks out the one in front, and return the id of the element that click selected. Throws
 * when the two do not overlap.
 */
export async function clickWhereCanvasElementsOverlap(
    page: Page,
    firstId: string,
    secondId: string,
): Promise<string | undefined> {
    const a = await getStackedCanvasElementRect(page, firstId);
    const b = await getStackedCanvasElementRect(page, secondId);
    const left = Math.max(a.x, b.x);
    const top = Math.max(a.y, b.y);
    const right = Math.min(a.x + a.width, b.x + b.width);
    const bottom = Math.min(a.y + a.height, b.y + b.height);
    if (right - left < 4 || bottom - top < 4)
        throw new Error(
            `The canvas elements at ${firstId} and ${secondId} do not overlap enough to click ` +
                `where they do.`,
        );
    await realClickAt(page, (left + right) / 2, (top + bottom) / 2);
    await activeCanvasElement(page).waitFor({
        state: "visible",
        timeout: 15000,
    });
    return getSelectedStackedElement(page);
}

/**
 * The commands of the selected canvas element's Layer submenu, in order, each with whether it is
 * enabled. Opens the "..." menu and the submenu to read them, and leaves them open.
 */
export async function getLayerMenuItems(
    page: Page,
): Promise<{ command: LayerCommand; enabled: boolean }[]> {
    const items = await getCanvasElementSubmenuItems(page, LAYER_SUBMENU);
    return items.map((item) => {
        const command = (Object.keys(LAYER_COMMANDS) as LayerCommand[]).find(
            (c) => LAYER_COMMANDS[c].l10nId === item.id,
        );
        if (!command)
            throw new Error(
                `The Layer submenu offers "${item.id}", which this helper does not know.`,
            );
        return { command, enabled: item.enabled };
    });
}

/**
 * The Layer commands the selected canvas element's menu enables, in menu order. Opens the "..."
 * menu and the Layer submenu to read them, and closes both again, leaving the element selected.
 */
export async function getEnabledLayerCommands(
    page: Page,
): Promise<LayerCommand[]> {
    const items = await getLayerMenuItems(page);
    await closeCanvasElementMenu(page);
    return items.filter((i) => i.enabled).map((i) => i.command);
}

/**
 * Wait until the stack is no longer `before`, after a Layer command that should have changed it.
 * The command itself runs in the page synchronously, but a keystroke or a click reaches it only
 * after Playwright's call has returned.
 */
async function waitForStackToChange(
    page: Page,
    before: string[],
    how: string,
): Promise<void> {
    await expect
        .poll(async () => (await getStackingOrder(page)).join(" | "), {
            timeout: 15000,
            message: `${how} did not change the stacking order (${before.join(" | ")}).`,
        })
        .not.toBe(before.join(" | "));
}

/**
 * Give the selected canvas element a Layer command through its "..." menu: open the menu, rest
 * the mouse on Layer, click the command. Waits until the stacking order has changed, so use it
 * only for a command that should move the element.
 */
export async function chooseLayerCommand(
    page: Page,
    command: LayerCommand,
): Promise<void> {
    const before = await getStackingOrder(page);
    await clickCanvasElementSubmenuItem(
        page,
        LAYER_SUBMENU,
        LAYER_COMMANDS[command].l10nId,
    );
    await waitForStackToChange(page, before, `Choosing "${command}"`);
}

/**
 * Give the selected canvas element a Layer command with its keyboard shortcut (Ctrl+] and the
 * like), pressed for real. The caret must not be in any text box: Bloom ignores the shortcut there.
 * selectStackedCanvasElement normally leaves it so, but not always (a click on a bubble's parent
 * leaves the caret in a child's text), so this checks first and throws naming where the caret is.
 * Waits until the stacking order has changed, so use it only for a command that should move the
 * element.
 */
export async function pressLayerShortcut(
    page: Page,
    command: LayerCommand,
): Promise<void> {
    const before = await getStackingOrder(page);
    // Where the caret is, named as the stack names elements: the element holding the text box it
    // is in, or "" when it is in no text box.
    const caretIn = await editablePageFrame(page).evaluate(() => {
        const focused = document.activeElement as HTMLElement | null;
        if (!focused?.isContentEditable) return "";
        const holder = focused.closest<HTMLElement>(".bloom-canvas-element");
        return holder
            ? `${parseFloat(holder.style.left)},${parseFloat(holder.style.top)}`
            : "a text box outside any canvas element";
    });
    if (caretIn)
        throw new Error(
            `The caret is in the text of ${caretIn} (the selected element is ` +
                `${await getSelectedStackedElement(page)}), so ${LAYER_COMMANDS[command].key} ` +
                `would go to the text, and Bloom ignores it there.`,
        );
    await pressKey(page, LAYER_COMMANDS[command].key);
    await waitForStackToChange(
        page,
        before,
        `Pressing ${LAYER_COMMANDS[command].key} ("${command}")`,
    );
}
