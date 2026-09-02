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
//
// The second half of this file drives the dialog's four tabs (Style Name, Characters, Paragraph,
// Highlighting) and reads what they did to the text boxes on the page. What a control changes:
//
//  - Every control writes CSS rules for the box's STYLE ("normal", or one the user created) into
//    the book's userModifiedStyles sheet, so it changes every box of that style, on every page.
//  - A Characters-tab change made on a box in the collection's first language goes into the
//    style's language-independent rule and so reaches the other languages too; made on a box in
//    another language it goes into that language's own rule only. Font is the deliberate
//    exception: a font suits a script, so it is always per language.
//  - Paragraph-tab and Highlighting-tab settings are per style, never per language.
//
// The dialog's dropdowns are select2 controls over hidden <select>s (the font list is a MUI
// select), and its color buttons open Bloom's color picker dialog, which lands in the page frame.

import { expect, type Frame, type Locator, type Page } from "@playwright/test";
import { editablePageFrame, goToPage, type IBookPage } from "./bookMaking";
import { cssColorToHex, cssPxToPt } from "./cssValues";
import { realClickAt } from "./realClick";
import { getZoom, setZoom } from "./workspace";

/** The gear Bloom adds beside the text box that has the focus. */
const GEAR = "#formatButton";
/** The dialog itself. Bloom creates it when the gear is clicked and removes it when it closes. */
const DIALOG = "#format-toolbar";
/** The dialog's title bar, which is what a person drags it by. */
const DIALOG_TITLE_BAR = `${DIALOG} .bloomDialogTitleBar`;

/** Bloom's color picker dialog, which the dialog's color buttons open. */
const COLOR_PICKER = '[role="dialog"]';
/** One swatch in the color picker: the inner div is the part that takes the click. */
const COLOR_SWATCH = ".color-swatch > div:last-child";

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

// ---------------------------------------------------------------------------------------------
// The dialog's tabs and controls.
// ---------------------------------------------------------------------------------------------

/** The Format dialog itself, as a locator in the page frame. */
function formatDialog(page: Page): Locator {
    return editablePageFrame(page).locator(DIALOG);
}

/** Close the Format dialog if it is open, the way a person does: with a click outside it. */
export async function closeFormatDialog(page: Page): Promise<void> {
    if (await isFormatDialogOpen(page)) await clickOutsideFormatDialog(page);
}

/** The dialog's tabs, named as the dialog labels them. */
export type FormatDialogTab =
    | "Style Name"
    | "Characters"
    | "Paragraph"
    | "Highlighting";

// The tabs are found by their localization keys, not their labels, so the helper works in any UI
// language.
const TAB_KEY: Record<FormatDialogTab, string> = {
    "Style Name": "EditTab.FormatDialog.StyleNameTab",
    Characters: "EditTab.FormatDialog.CharactersTab",
    Paragraph: "EditTab.FormatDialog.ParagraphTab",
    Highlighting: "EditTab.FormatDialog.Highlighting",
};

/** Click one of the Format dialog's tabs and wait for it to be the selected one. */
export async function switchFormatDialogTab(
    page: Page,
    tab: FormatDialogTab,
): Promise<void> {
    const header = formatDialog(page).locator(
        `h2.tab[data-i18n="${TAB_KEY[tab]}"]`,
    );
    await header.click();
    await expect(
        header,
        `The Format dialog's "${tab}" tab did not become the selected tab.`,
    ).toHaveClass(/selected/, { timeout: 15000 });
}

/** One entry of a <select> in the dialog. */
interface ISelectOption {
    value: string;
    text: string;
}

/** The options of one of the dialog's <select>s, in order. */
async function getSelectOptions(
    frame: Frame,
    selectId: string,
): Promise<ISelectOption[]> {
    // The dialog fills its lists as it opens, the style list only once the style names have
    // been localized, so an empty list means "not yet", never "nothing to offer".
    await expect
        .poll(() => frame.locator(`#${selectId} option`).count(), {
            timeout: 15000,
            message: `The Format dialog's #${selectId} list never got its entries.`,
        })
        .toBeGreaterThan(0);
    return frame.locator(`#${selectId} option`).evaluateAll((options) =>
        options.map((o) => ({
            value: (o as HTMLOptionElement).value,
            text: (o as HTMLOptionElement).text,
        })),
    );
}

/** Open the select2 dropdown that stands in for one of the dialog's <select>s. */
async function openSelect2(frame: Frame, selectId: string): Promise<void> {
    // select2 hides the <select> and puts its own control right after it.
    await frame.locator(`#${selectId} + .select2 .select2-selection`).click();
    await expect(
        frame.locator(".select2-container--open .select2-results__options"),
        `The dropdown for #${selectId} did not open.`,
    ).toBeVisible({ timeout: 15000 });
}

/**
 * Choose, in one of the dialog's select2 dropdowns, the entry that `matches`, by opening the
 * dropdown and clicking the entry as a person does, then wait until the <select> holds it.
 * `wanted` describes the entry for the error when there is no such entry.
 */
async function chooseSelect2Option(
    frame: Frame,
    selectId: string,
    matches: (option: ISelectOption) => boolean,
    wanted: string,
): Promise<void> {
    const options = await getSelectOptions(frame, selectId);
    const index = options.findIndex(matches);
    if (index < 0)
        throw new Error(
            `The Format dialog offers no ${wanted}. It offers: ` +
                options.map((o) => `"${o.text}"`).join(", ") +
                ".",
        );
    await openSelect2(frame, selectId);
    // The dropdown lists the <select>'s options in the same order.
    await frame
        .locator(".select2-container--open .select2-results__option")
        .nth(index)
        .click();
    await expect
        .poll(() => frame.locator(`#${selectId}`).inputValue(), {
            timeout: 15000,
            message: `Choosing ${wanted} did not change the dialog's selection.`,
        })
        .toBe(options[index].value);
}

/**
 * Choose a font on the Characters tab, by name as the list shows it (e.g. "Arial"), and wait for
 * the control to show it. Throws, listing the fonts offered, if there is no such font.
 */
export async function chooseFont(page: Page, fontName: string): Promise<void> {
    const frame = editablePageFrame(page);
    const control = frame.locator('#fontSelectComponent [role="combobox"]');
    await control.click();
    const list = frame.locator('[role="listbox"]');
    await list.waitFor({ state: "visible", timeout: 15000 });
    const option = list.getByRole("option", { name: fontName, exact: true });
    if ((await option.count()) === 0) {
        const offered = await list.getByRole("option").allInnerTexts();
        throw new Error(
            `Bloom offers no font called "${fontName}". It offers: ${offered
                .map((f) => f.trim())
                .join(", ")}.`,
        );
    }
    await option.click();
    await expect(
        control,
        `The font control did not change to "${fontName}".`,
    ).toContainText(fontName, { timeout: 15000 });
}

/**
 * Set the font size on the Characters tab, in points, by typing it into the size box the way a
 * person enters a size that is not in the list, then pressing Enter. Works for listed sizes too.
 */
export async function chooseFontSize(
    page: Page,
    points: number,
): Promise<void> {
    const frame = editablePageFrame(page);
    await openSelect2(frame, "size-select");
    const search = frame.locator(
        ".select2-container--open .select2-search__field",
    );
    await search.pressSequentially(String(points));
    await search.press("Enter");
    await expect
        .poll(() => frame.locator("#size-select").inputValue(), {
            timeout: 15000,
            message: `Typing ${points} into the font size box did not select that size.`,
        })
        .toBe(String(points));
}

/** The sizes the Characters tab's size list offers, in points, in the order it lists them. */
export async function getFontSizeChoices(page: Page): Promise<number[]> {
    const options = await getSelectOptions(
        editablePageFrame(page),
        "size-select",
    );
    return options.map((o) => Number(o.value));
}

/** The font size the Characters tab's size control shows, in points. */
export async function getFontSizeShown(page: Page): Promise<number> {
    return Number(
        await editablePageFrame(page).locator("#size-select").inputValue(),
    );
}

/** The line spacings the Characters tab offers, as multiples of the font size. */
export type LineSpacing =
    | "0.7"
    | "0.8"
    | "1.0"
    | "1.1"
    | "1.2"
    | "1.3"
    | "1.4"
    | "1.5"
    | "1.6"
    | "1.8"
    | "2.0"
    | "2.5"
    | "3.0";

/** Choose a line spacing on the Characters tab. */
export async function chooseLineSpacing(
    page: Page,
    spacing: LineSpacing,
): Promise<void> {
    await chooseSelect2Option(
        editablePageFrame(page),
        "line-height-select",
        (o) => o.value === spacing,
        `line spacing of ${spacing}`,
    );
}

/** The word spacings the Characters tab offers, as it labels them in English. */
export type WordSpacing = "Normal" | "Wide" | "Extra Wide";
// The labels are localized, so the helper chooses by position, which is what changeWordSpace in
// StyleEditor.ts reads too.
const WORD_SPACING_INDEX: Record<WordSpacing, number> = {
    Normal: 0,
    Wide: 1,
    "Extra Wide": 2,
};

/** Choose a word spacing on the Characters tab. */
export async function chooseWordSpacing(
    page: Page,
    spacing: WordSpacing,
): Promise<void> {
    const frame = editablePageFrame(page);
    const options = await getSelectOptions(frame, "word-space-select");
    const wanted = options[WORD_SPACING_INDEX[spacing]];
    await chooseSelect2Option(
        frame,
        "word-space-select",
        (o) => !!wanted && o.value === wanted.value,
        `word spacing "${spacing}"`,
    );
}

/** The three emphasis buttons on the Characters tab. */
export type Emphasis = "bold" | "italic" | "underline";

/** Whether the Characters tab shows this emphasis as on. */
export async function isEmphasisOn(
    page: Page,
    emphasis: Emphasis,
): Promise<boolean> {
    return formatDialog(page)
        .locator(`#${emphasis}`)
        .evaluate((b) => b.classList.contains("selectedIcon"));
}

/**
 * Click one of the emphasis buttons (B, I, U) on the Characters tab, which turns that emphasis on
 * if it was off and off if it was on, and return the new state.
 */
export async function toggleEmphasis(
    page: Page,
    emphasis: Emphasis,
): Promise<boolean> {
    const wasOn = await isEmphasisOn(page, emphasis);
    await formatDialog(page).locator(`#${emphasis}`).click();
    await expect
        .poll(() => isEmphasisOn(page, emphasis), {
            timeout: 15000,
            message: `Clicking the ${emphasis} button did not turn it ${wasOn ? "off" : "on"}.`,
        })
        .toBe(!wasOn);
    return !wasOn;
}

/** The color an element is painted, as "#rrggbb". */
async function backgroundColorOf(element: Locator): Promise<string> {
    return cssColorToHex(
        await element.evaluate((e) => getComputedStyle(e).backgroundColor),
    );
}

/**
 * In Bloom's color picker dialog, which something has just opened, click the swatch of this color
 * ("#rrggbb"), then OK, and wait for the dialog to close. Throws, listing the swatches, if no
 * swatch has that color: the pickers offer a fixed palette, and a test should choose from it.
 */
async function pickColor(frame: Frame, color: string): Promise<void> {
    const dialog = frame.locator(COLOR_PICKER);
    await dialog.waitFor({ state: "visible", timeout: 15000 });
    const swatches = dialog.locator(COLOR_SWATCH);
    await swatches.first().waitFor({ state: "visible", timeout: 15000 });
    const colors = (
        await swatches.evaluateAll((elements) =>
            elements.map((e) => getComputedStyle(e).backgroundColor),
        )
    ).map(cssColorToHex);
    const index = colors.indexOf(color.toLowerCase());
    if (index < 0)
        throw new Error(
            `The color picker has no swatch of ${color}. Its swatches are: ${colors.join(", ")}.`,
        );
    await swatches.nth(index).click();
    await dialog.getByRole("button", { name: "OK" }).click();
    await expect(dialog, "The color picker did not close after OK.").toBeHidden(
        { timeout: 15000 },
    );
}

/**
 * Open a color button's picker, pick `color` ("#rrggbb", one of the picker's palette), OK it,
 * and wait for the button to show the new color.
 */
async function chooseColorWithButton(
    page: Page,
    button: Locator,
    color: string,
    description: string,
): Promise<void> {
    await button.click();
    await pickColor(editablePageFrame(page), color);
    await expect
        .poll(() => backgroundColorOf(button), {
            timeout: 15000,
            message: `The ${description} button did not change to ${color}.`,
        })
        .toBe(color.toLowerCase());
}

/**
 * Choose the text color on the Characters tab: open the color picker from the Color button, pick
 * the swatch of `color` ("#rrggbb", one of the picker's palette), and OK it.
 */
export async function chooseTextColor(
    page: Page,
    color: string,
): Promise<void> {
    await chooseColorWithButton(
        page,
        formatDialog(page).locator("#colorSelectButton"),
        color,
        "Color",
    );
}

/** The indent choices on the Paragraph tab. */
export type Indent = "none" | "indented";
/** The alignment choices on the Paragraph tab. */
export type Alignment = "left" | "center" | "right" | "justify";
/** The paragraph spacings the Paragraph tab offers, in ems. */
export type ParagraphSpacing = "0" | "0.5" | "0.75" | "1" | "1.25";

/** Click one of a group of picture buttons in the dialog and wait for it to show as chosen. */
async function chooseButton(
    page: Page,
    buttonId: string,
    description: string,
): Promise<void> {
    const button = formatDialog(page).locator(`#${buttonId}`);
    await button.click();
    await expect(
        button,
        `Clicking the ${description} button did not select it.`,
    ).toHaveClass(/selectedIcon/, { timeout: 15000 });
}

/** Choose an indent on the Paragraph tab. */
export async function chooseIndent(page: Page, indent: Indent): Promise<void> {
    await chooseButton(page, `indent-${indent}`, `"${indent}" indent`);
}

/** Choose an alignment on the Paragraph tab. */
export async function chooseAlignment(
    page: Page,
    alignment: Alignment,
): Promise<void> {
    await chooseButton(
        page,
        `position-${alignment}`,
        `"${alignment}" alignment`,
    );
}

/** Choose a space between paragraphs on the Paragraph tab. */
export async function chooseParagraphSpacing(
    page: Page,
    spacing: ParagraphSpacing,
): Promise<void> {
    await chooseSelect2Option(
        editablePageFrame(page),
        "para-spacing-select",
        (o) => o.value === spacing,
        `paragraph spacing of ${spacing}`,
    );
}

/** The Highlighting tab's color buttons: background first, then text, as the tab lays them out. */
function highlightColorButton(page: Page, which: 0 | 1): Locator {
    return formatDialog(page)
        .locator('#audioHilitePage [data-testid="color-display-button-swatch"]')
        .nth(which);
}

/**
 * On the Highlighting tab, choose the background color the Talking Book tool highlights this
 * style's text with while its audio plays. `color` is "#rrggbb", one of the picker's palette.
 */
export async function chooseHighlightBackgroundColor(
    page: Page,
    color: string,
): Promise<void> {
    await chooseColorWithButton(
        page,
        highlightColorButton(page, 0),
        color,
        "highlight background color",
    );
}

/**
 * On the Highlighting tab, turn on "Text Color" if it is off, and choose the color the highlighted
 * text is drawn in. `color` is "#rrggbb", one of the picker's palette.
 */
export async function chooseHighlightTextColor(
    page: Page,
    color: string,
): Promise<void> {
    // The tab's checkboxes are "Background color" then "Text Color".
    const checkbox = formatDialog(page)
        .locator('#audioHilitePage input[type="checkbox"]')
        .nth(1);
    if (!(await checkbox.isChecked())) {
        await checkbox.click();
        await expect(
            checkbox,
            'Clicking "Text Color" on the Highlighting tab did not turn it on.',
        ).toBeChecked({ timeout: 15000 });
    }
    await chooseColorWithButton(
        page,
        highlightColorButton(page, 1),
        color,
        "highlight text color",
    );
}

/** The style names the Style Name tab's menu offers, as it shows them ("Normal", "Heading 1"...). */
export async function getStyleMenuEntries(page: Page): Promise<string[]> {
    return (await getSelectOptions(editablePageFrame(page), "styleSelect")).map(
        (o) => o.text,
    );
}

/** The style the Style Name tab's menu shows as the current one, e.g. "Normal". */
export async function getStyleShown(page: Page): Promise<string> {
    return editablePageFrame(page)
        .locator("#styleSelect option:checked")
        .evaluate((o) => (o as HTMLOptionElement).text);
}

/**
 * On the Style Name tab, apply an existing style to the box, by choosing it in the menu by the
 * name the menu shows ("Heading 1", or a style a test created).
 */
export async function applyStyle(page: Page, styleName: string): Promise<void> {
    await chooseSelect2Option(
        editablePageFrame(page),
        "styleSelect",
        (o) => o.text === styleName,
        `style called "${styleName}"`,
    );
}

/**
 * On the Style Name tab, create a new style called `styleName` and apply it to the box: click
 * "Create a new style", type the name, click Create. The new style starts with the box's current
 * settings. Returns when the menu shows the new style as the current one.
 */
export async function createStyle(
    page: Page,
    styleName: string,
): Promise<void> {
    const dialog = formatDialog(page);
    await dialog.locator("#show-createStyle").click();
    const input = dialog.locator("#style-select-input");
    await input.waitFor({ state: "visible", timeout: 15000 });
    await input.pressSequentially(styleName);
    const create = dialog.locator("#create-button");
    await expect(
        create,
        `Typing "${styleName}" did not enable the Create button. Style names are letters only.`,
    ).toBeEnabled({ timeout: 15000 });
    await create.click();
    await expect
        .poll(() => getStyleShown(page), {
            timeout: 15000,
            message: `Creating the style "${styleName}" did not make it the current style.`,
        })
        .toBe(styleName);
}

// ---------------------------------------------------------------------------------------------
// Reading what the dialog did to the page.
// ---------------------------------------------------------------------------------------------

/** How one text box on the page is formatted: everything the Format dialog controls, as drawn. */
export interface ITextBoxFormatting {
    /** Which translation group on the page the box is in, in document order, from 0. */
    group: number;
    /** The box's language tag. */
    lang: string;
    /** The box's style, as Bloom names it in the markup: "normal", or a style the user created. */
    style: string;
    fontFamily: string;
    fontSizePt: number;
    /** Line spacing as a multiple of the font size, to one decimal, as the dialog lists it. */
    lineSpacing: number;
    wordSpacingPt: number;
    bold: boolean;
    italic: boolean;
    underline: boolean;
    /** The text color, as "#rrggbb". */
    color: string;
    alignment: Alignment;
    indent: Indent;
    /**
     * Space below a paragraph as a multiple of the font size, as the dialog lists it, measured on
     * the box's FIRST paragraph: Bloom draws the last paragraph of a box with no space below it,
     * whatever the style says, so a box needs two paragraphs for this to show.
     */
    paragraphSpacingEm: number;
}

/** What getTextBoxFormatting measures in the page, before the units are converted. */
interface IRawTextBoxFormatting {
    group: number;
    lang: string;
    style: string;
    fontFamily: string;
    fontSize: string;
    lineHeight: string;
    wordSpacing: string;
    fontWeight: string;
    fontStyle: string;
    textDecorationLine: string;
    color: string;
    textAlign: string;
    direction: string;
    textIndent: string;
    marginBottom: string;
}

/**
 * How every visible text box on the page being edited is formatted, in document order, measured
 * from the boxes themselves (their computed styles), which is what the reader sees.
 *
 * Only boxes that are showing count: a group's hidden languages are left out. Front and back
 * matter pages have boxes too, so a test that wants content boxes asks on a content page.
 */
export async function getTextBoxFormatting(
    page: Page,
): Promise<ITextBoxFormatting[]> {
    const raw: IRawTextBoxFormatting[] = await editablePageFrame(page).evaluate(
        () => {
            const groups = Array.from(
                document.querySelectorAll(
                    ".bloom-page .bloom-translationGroup",
                ),
            );
            const result: IRawTextBoxFormatting[] = [];
            groups.forEach((group, groupIndex) => {
                for (const box of Array.from(
                    group.querySelectorAll(".bloom-editable"),
                )) {
                    const boxStyle = getComputedStyle(box);
                    if (boxStyle.display === "none") continue;
                    // The paragraph settings live on the paragraphs inside the box.
                    const paragraph = box.querySelector("p") ?? box;
                    const paragraphStyle = getComputedStyle(paragraph);
                    result.push({
                        group: groupIndex,
                        lang: box.getAttribute("lang") ?? "",
                        style:
                            Array.from(box.classList)
                                .find((c) => c.endsWith("-style"))
                                ?.replace(/-style$/, "") ?? "",
                        fontFamily: boxStyle.fontFamily,
                        fontSize: boxStyle.fontSize,
                        lineHeight: boxStyle.lineHeight,
                        wordSpacing: boxStyle.wordSpacing,
                        fontWeight: boxStyle.fontWeight,
                        fontStyle: boxStyle.fontStyle,
                        textDecorationLine: boxStyle.textDecorationLine,
                        color: boxStyle.color,
                        textAlign: paragraphStyle.textAlign,
                        direction: paragraphStyle.direction,
                        textIndent: paragraphStyle.textIndent,
                        marginBottom: paragraphStyle.marginBottom,
                    });
                }
            });
            return result;
        },
    );
    return raw.map((r) => {
        const fontSizePx = parseFloat(r.fontSize);
        return {
            group: r.group,
            lang: r.lang,
            style: r.style,
            // Computed font families keep the quotes a name with spaces needs; the dialog does not.
            fontFamily: r.fontFamily.replace(/^["']|["']$/g, ""),
            fontSizePt: cssPxToPt(r.fontSize),
            lineSpacing:
                Math.round((parseFloat(r.lineHeight) / fontSizePx) * 10) / 10,
            wordSpacingPt:
                r.wordSpacing === "normal" ? 0 : cssPxToPt(r.wordSpacing),
            bold: parseInt(r.fontWeight, 10) >= 600,
            italic: r.fontStyle === "italic",
            underline: r.textDecorationLine.includes("underline"),
            color: cssColorToHex(r.color),
            alignment: alignmentOf(r.textAlign, r.direction === "rtl"),
            indent: parseFloat(r.textIndent) > 1 ? "indented" : "none",
            paragraphSpacingEm:
                Math.round((parseFloat(r.marginBottom) / fontSizePx) * 100) /
                100,
        };
    });
}

/** The Paragraph tab's name for a computed text-align, which Bloom writes as start/end. */
function alignmentOf(textAlign: string, rightToLeft: boolean): Alignment {
    switch (textAlign) {
        case "center":
            return "center";
        case "justify":
            return "justify";
        case "right":
            return "right";
        case "left":
            return "left";
        case "end":
            return rightToLeft ? "left" : "right";
        default: // "start"
            return rightToLeft ? "right" : "left";
    }
}

/** The formatting of one text box, and the page it is on. */
export interface IBookTextBoxFormatting extends ITextBoxFormatting {
    /** The page list's caption for the page, e.g. "1", or its label for a page with no number. */
    pageCaption: string;
}

/**
 * The formatting of every visible text box on each of these pages, visiting the pages in turn
 * with goToPage. The Edit tab is left showing the last of them. This is how a test checks that a
 * style change reached every box of that style in the book, not only the ones on the page it was
 * made on.
 */
export async function getTextBoxFormattingOnPages(
    page: Page,
    pages: IBookPage[],
): Promise<IBookTextBoxFormatting[]> {
    const result: IBookTextBoxFormatting[] = [];
    for (const bookPage of pages) {
        await goToPage(page, bookPage.id);
        for (const box of await getTextBoxFormatting(page))
            result.push({ ...box, pageCaption: bookPage.caption });
    }
    return result;
}
