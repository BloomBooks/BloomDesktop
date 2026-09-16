// The Format dialog: the gear Bloom shows at the corner of the text box being edited, and the
// dialog the gear opens, where the box's style, font, and paragraph settings are changed.
//
// Both live in the Edit tab's page frame. The gear sits inside the zoomed page, next to its box.
// The dialog is appended to the frame's body, outside the zoom, and Bloom places it just to the
// right of the gear, then moves it only as far as needed to keep it wholly inside the frame's
// viewport (StyleEditor.runFormatDialog and EditableDivUtils.positionDialogAndSetDraggable). So
// that viewport is the "screen" the dialog has to stay on, and every measurement here is in the
// page frame's client coordinates.
//
// The gear is clicked at a point, not with Playwright's own click, because Playwright scrolls its
// target into view first, and the case the manual test cares about most is a gear that is only
// partly in view.

import { expect, type Page } from "@playwright/test";
import { editablePageFrame } from "./bookMaking";
import { realClickAt } from "./realClick";
import { getZoom, setZoom } from "./workspace";

/** The gear Bloom adds beside the text box that has the focus. */
const GEAR = "#formatButton";
/** The dialog itself. Bloom creates it when the gear is clicked and removes it when it closes. */
const DIALOG = "#format-toolbar";
/** The dialog's title bar, which is what a person drags it by. */
const DIALOG_TITLE_BAR = `${DIALOG} .bloomDialogTitleBar`;

/** A rectangle in the page frame's client coordinates. */
export interface IRect {
    left: number;
    top: number;
    right: number;
    bottom: number;
    width: number;
    height: number;
}

/** Where the format gear and the Format dialog are, relative to the page frame's viewport. */
export interface IFormatDialogPlacement {
    /** The Format dialog, or undefined while it is not open. */
    dialog: IRect | undefined;
    /** The format gear, or undefined while no text box has the focus. */
    gear: IRect | undefined;
    /** The page frame's viewport: the area in which the page, the gear and the dialog can be seen. */
    viewport: { width: number; height: number };
}

/**
 * Where the gear and the dialog are, and how big the viewport is, measured together inside the
 * page frame so that the three agree with each other.
 */
export async function getFormatDialogPlacement(
    page: Page,
): Promise<IFormatDialogPlacement> {
    return editablePageFrame(page).evaluate(
        ({ gearSelector, dialogSelector }) => {
            const rectOf = (selector: string): IRect | undefined => {
                const element = document.querySelector(selector);
                if (!element) return undefined;
                const r = element.getBoundingClientRect();
                return {
                    left: r.left,
                    top: r.top,
                    right: r.right,
                    bottom: r.bottom,
                    width: r.width,
                    height: r.height,
                };
            };
            return {
                dialog: rectOf(dialogSelector),
                gear: rectOf(gearSelector),
                // clientWidth/clientHeight leave out the scrollbars, which hide whatever is
                // under them.
                viewport: {
                    width: document.documentElement.clientWidth,
                    height: document.documentElement.clientHeight,
                },
            };
        },
        { gearSelector: GEAR, dialogSelector: DIALOG },
    );
}

/** A rectangle described for an error message. */
const describeRect = (r: IRect) =>
    `left ${Math.round(r.left)}, top ${Math.round(r.top)}, ${Math.round(r.width)}x${Math.round(r.height)}`;

/**
 * Wait until the text box with the focus is showing its format gear, and return where the gear is.
 * Bloom adds the gear when a box gets the focus, once CKEditor is ready, which can be a moment
 * after clickInGroup returns.
 */
export async function waitForFormatGear(page: Page): Promise<IRect> {
    let gear: IRect | undefined;
    await expect
        .poll(
            async () => {
                gear = (await getFormatDialogPlacement(page)).gear;
                return !!gear && gear.width > 0;
            },
            {
                timeout: 30000,
                message:
                    "No text box is showing its format gear. A box has to have the focus for its gear to appear (see clickInGroup).",
            },
        )
        .toBe(true);
    return gear!;
}

/**
 * The point in the shell page's coordinates that the page frame's client origin (0, 0) maps to,
 * so a point measured inside the frame can be clicked with the mouse.
 */
async function pageFrameOrigin(page: Page): Promise<{ x: number; y: number }> {
    const iframe = await editablePageFrame(page).frameElement();
    const box = await iframe.boundingBox();
    if (!box)
        throw new Error(
            "The page frame has no on-screen box, so nothing in it can be clicked.",
        );
    // A border on the iframe would shift its content relative to its box.
    const border = await iframe.evaluate((element) => ({
        left: (element as HTMLElement).clientLeft,
        top: (element as HTMLElement).clientTop,
    }));
    return { x: box.x + border.left, y: box.y + border.top };
}

/** Click, with a real mouse press and release, at a point given in the page frame's coordinates. */
async function clickInPageFrameAt(
    page: Page,
    x: number,
    y: number,
): Promise<void> {
    const origin = await pageFrameOrigin(page);
    await realClickAt(page, origin.x + x, origin.y + y);
}

/**
 * Click the format gear, the way a person does, and wait for the Format dialog to open.
 *
 * The click lands on the middle of whatever part of the gear is in view, and nothing is scrolled
 * first: a gear that is half off the bottom of the frame is clicked on its visible half, which is
 * how the manual test checks where the dialog lands for a gear at the edge of the screen. Throws
 * when no part of the gear is in view; see scrollFormatGearIntoView.
 */
export async function openFormatDialog(page: Page): Promise<void> {
    await waitForFormatGear(page);
    const { gear, viewport } = await getFormatDialogPlacement(page);
    const visible = {
        left: Math.max(gear!.left, 0),
        top: Math.max(gear!.top, 0),
        right: Math.min(gear!.right, viewport.width),
        bottom: Math.min(gear!.bottom, viewport.height),
    };
    if (visible.right - visible.left < 2 || visible.bottom - visible.top < 2)
        throw new Error(
            `The format gear is out of view (it is at ${describeRect(gear!)} in a ` +
                `${viewport.width}x${viewport.height} viewport), so it cannot be clicked. ` +
                `Scroll it into view first.`,
        );
    await clickInPageFrameAt(
        page,
        (visible.left + visible.right) / 2,
        (visible.top + visible.bottom) / 2,
    );
    await expect(
        editablePageFrame(page).locator(DIALOG),
        "Clicking the format gear did not open the Format dialog.",
    ).toBeVisible({ timeout: 30000 });
}

/** True while the Format dialog is open. */
export async function isFormatDialogOpen(page: Page): Promise<boolean> {
    return editablePageFrame(page).locator(DIALOG).isVisible();
}

/** The Format dialog's tabs, named as the dialog labels them. Not every box offers every tab. */
export type FormatDialogTab =
    | "styleName"
    | "characters"
    | "paragraph"
    | "highlighting"
    | "canvasText";

/**
 * A control that lives on each tab's page, which is how a page is told apart: the dialog's tab
 * plugin (lib/tabpane.js) gives the pages no ids of their own, moves each page's heading into a
 * tab row in page order, and Bloom removes the pages a box does not need before that happens.
 */
const TAB_PAGE_CONTENT: Record<FormatDialogTab, string> = {
    styleName: "#styleSelect",
    characters: "#fontSelectComponent",
    paragraph: "#para-spacing-select",
    highlighting: "#audioHilitePage",
    canvasText: "#canvasFormatPage",
};
/** The pages of the dialog's tab control, in the order their tabs appear. */
const TAB_PAGES = `${DIALOG} #tabRoot > .tab-page`;
/** The tabs themselves, in the same order. */
const TABS = `${DIALOG} .tab-row .tab`;

/**
 * Click one of the Format dialog's tabs and wait for its page to show. The dialog opens on the
 * Style Name tab (or on the tab it was last on: the plugin remembers it in a session cookie).
 * Throws, naming the tabs the dialog does have, when it has no such tab for this box.
 */
export async function showFormatDialogTab(
    page: Page,
    tab: FormatDialogTab,
): Promise<void> {
    const frame = editablePageFrame(page);
    const pages = frame.locator(TAB_PAGES);
    await expect(
        pages.first(),
        "The Format dialog is not open, or has no tabs.",
    ).toBeAttached({ timeout: 30000 });
    const count = await pages.count();
    let index = -1;
    for (let i = 0; i < count && index < 0; i++) {
        if ((await pages.nth(i).locator(TAB_PAGE_CONTENT[tab]).count()) > 0)
            index = i;
    }
    if (index < 0) {
        const tabs = await frame.locator(TABS).allTextContents();
        throw new Error(
            `The Format dialog has no "${tab}" tab for this box. Its tabs: ${tabs.map((t) => t.trim()).join(", ")}.`,
        );
    }
    await frame.locator(TABS).nth(index).click({ timeout: 30000 });
    await expect(
        pages.nth(index),
        `Clicking the Format dialog's "${tab}" tab did not show its page.`,
    ).toBeVisible({ timeout: 30000 });
}

/**
 * Click on the page outside the Format dialog, the way a person dismisses it, and wait for the
 * dialog to go away.
 *
 * The click lands near the corner of the frame's viewport farthest from the dialog, which is the
 * spot least likely to be on the dialog or on anything that reacts to a click of its own.
 */
export async function clickOutsideFormatDialog(page: Page): Promise<void> {
    const { dialog, viewport } = await getFormatDialogPlacement(page);
    if (!dialog)
        throw new Error(
            "The Format dialog is not open, so there is nothing to click outside of.",
        );
    const inset = 8;
    const corners = [
        { x: inset, y: inset },
        { x: viewport.width - inset, y: inset },
        { x: inset, y: viewport.height - inset },
        { x: viewport.width - inset, y: viewport.height - inset },
    ];
    const centre = {
        x: (dialog.left + dialog.right) / 2,
        y: (dialog.top + dialog.bottom) / 2,
    };
    const distance = (p: { x: number; y: number }) =>
        Math.hypot(p.x - centre.x, p.y - centre.y);
    const target = corners.sort((a, b) => distance(b) - distance(a))[0];
    await clickInPageFrameAt(page, target.x, target.y);
    await expect(
        editablePageFrame(page).locator(DIALOG),
        "The Format dialog stayed open after a click outside it.",
    ).toBeHidden({ timeout: 30000 });
}

/**
 * Drag the Format dialog by its title bar, `dx` pixels to the right and `dy` down, with a real
 * press, move and release, and wait until the dialog has moved.
 */
export async function dragFormatDialog(
    page: Page,
    dx: number,
    dy: number,
): Promise<void> {
    const titleBar = editablePageFrame(page).locator(DIALOG_TITLE_BAR);
    await titleBar.waitFor({ state: "visible", timeout: 30000 });
    const before = (await getFormatDialogPlacement(page)).dialog!;
    const box = (await titleBar.boundingBox())!;
    // Grab the bar left of its middle: the language code sits at its right end.
    const x = box.x + box.width / 4;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    // The dialog only starts to follow the pointer once it has travelled some way, so travel in
    // steps rather than jumping.
    await page.mouse.move(x + dx, y + dy, { steps: 10 });
    await page.mouse.up();
    await expect
        .poll(
            async () => {
                const dialog = (await getFormatDialogPlacement(page)).dialog;
                return dialog
                    ? Math.hypot(
                          dialog.left - before.left,
                          dialog.top - before.top,
                      )
                    : 0;
            },
            {
                timeout: 30000,
                message: `Dragging the Format dialog's title bar by (${dx}, ${dy}) did not move the dialog.`,
            },
        )
        .toBeGreaterThan(Math.hypot(dx, dy) / 2);
}

/**
 * Scroll the page frame so that the format gear is where `wantedTop` and `wantedLeft` put it,
 * and wait until it is there according to `isPlaced`.
 */
async function scrollFormatGearTo(
    page: Page,
    wanted: (
        gear: IRect,
        viewport: { width: number; height: number },
    ) => {
        left: number;
        top: number;
    },
    isPlaced: (
        gear: IRect,
        viewport: { width: number; height: number },
    ) => boolean,
    description: string,
): Promise<void> {
    await waitForFormatGear(page);
    const placement = await getFormatDialogPlacement(page);
    const target = wanted(placement.gear!, placement.viewport);
    await editablePageFrame(page).evaluate(
        ({ selector, left, top }) => {
            const gear = document
                .querySelector(selector)!
                .getBoundingClientRect();
            window.scrollBy(gear.left - left, gear.top - top);
        },
        { selector: GEAR, left: target.left, top: target.top },
    );
    await expect
        .poll(
            async () => {
                const { gear, viewport } = await getFormatDialogPlacement(page);
                return !!gear && isPlaced(gear, viewport);
            },
            {
                timeout: 30000,
                message: `The format gear never came to be ${description}.`,
            },
        )
        .toBe(true);
}

/** Room to leave between the gear and the left edge of the viewport when scrolling to it. */
const GEAR_MARGIN_PX = 40;

/**
 * Scroll the page frame so that the whole format gear is in view, with a little room around it.
 * Setup for clicking the gear when the page is bigger than the frame.
 */
export async function scrollFormatGearIntoView(page: Page): Promise<void> {
    await scrollFormatGearTo(
        page,
        (gear, viewport) => ({
            left: Math.min(gear.left, GEAR_MARGIN_PX),
            top: Math.min(
                gear.top,
                viewport.height - gear.height - GEAR_MARGIN_PX,
            ),
        }),
        (gear, viewport) =>
            gear.left >= 0 &&
            gear.top >= 0 &&
            gear.right <= viewport.width &&
            gear.bottom <= viewport.height,
        "wholly in view",
    );
}

/**
 * Scroll the page frame so that the format gear straddles the bottom edge of the viewport: its
 * top half in view, its bottom half below the edge. Setup for checking where the Format dialog
 * lands when the gear is at the edge of the screen.
 */
export async function scrollFormatGearPartlyIntoView(
    page: Page,
): Promise<void> {
    await scrollFormatGearTo(
        page,
        (gear, viewport) => ({
            left: Math.min(gear.left, GEAR_MARGIN_PX),
            top: viewport.height - gear.height / 2,
        }),
        (gear, viewport) =>
            gear.left >= 0 &&
            gear.right <= viewport.width &&
            gear.top < viewport.height &&
            gear.bottom > viewport.height,
        "half off the bottom edge of the viewport",
    );
}

/**
 * Zoom the page in, one notch at a time as the top bar's + button does, until the format gear has
 * scrolled out of view below the bottom of the frame, and return the zoom that did it. This is
 * the manual tester's "increase zoom until the format gear is scrolled out of view": the smallest
 * zoom at which the box at the bottom of the page no longer fits, which is a zoom people use.
 *
 * Throws if the gear is still in view at Bloom's largest zoom.
 */
export async function zoomUntilFormatGearIsOutOfView(
    page: Page,
): Promise<number> {
    await waitForFormatGear(page);
    const isOutOfView = async () => {
        const { gear, viewport } = await getFormatDialogPlacement(page);
        return !!gear && gear.top >= viewport.height;
    };
    let { zoom, maxZoom } = await getZoom(page);
    while (!(await isOutOfView())) {
        if (zoom >= maxZoom)
            throw new Error(
                `The format gear is still in view at Bloom's largest zoom, ${maxZoom}%, so it ` +
                    `cannot be scrolled out of view by zooming. Is the text box near the bottom of the page?`,
            );
        zoom = Math.min(zoom + 10, maxZoom);
        await setZoom(page, zoom);
    }
    return zoom;
}
