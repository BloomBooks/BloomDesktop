// A calendar month grid: the Wall Calendar's "Calendar Setup" dialog, the grid pages of a Wall
// Calendar book, the round button over a grid and the menu it opens, and the little toolbar that
// appears below a text box too small to carry its own language name and format gear.
//
// A month grid is a bloom-table, so everything about its cells (clicking, typing, the Cell menu,
// content types) is done with helpers/tables.ts. This module holds what only a calendar grid has:
// a year, a month and a first day of the week, day numbers, and the menu that changes them.
//
// Where things live:
//  - The Calendar Setup dialog belongs to Bloom's own window (the shell `page`), because the
//    calendar tooling runs in the Edit tab's outer frame and asks once per book, not per page.
//  - The grid, its button and the button's menu are in the page frame. The button and the menu are
//    outside the .bloom-page, so they can never be saved into the book.
//
// The menus name months, years and weekdays by the words Bloom shows, in its UI language, because
// the rows carry no localization id of their own (they are the calendar's names, not UI strings).
// The suite runs Bloom in English, so a helper that picks "March" is picking what a person reads.

import { expect, type Locator, type Page } from "@playwright/test";
import { apiGetJson } from "./api";
import {
    editablePageFrame,
    getContentPages,
    getShownPageId,
    type IBookPage,
} from "./bookMaking";
import { realClick } from "./realClick";
import { cell } from "./tables";

/** The id of the Calendar Setup dialog's Year box. */
const SETUP_YEAR = "#calendar-setup-year";
/** The id of the Calendar Setup dialog's "First day of the week" list. */
const SETUP_FIRST_DAY = "#calendar-setup-first-day-of-week";
/** A month grid: a bloom-table that says which month it is for. */
const GRID = ".bloom-table[data-calendar-month]";
/** The button over a grid that sits directly on a page. Its host div covers the grid. */
const GRID_MENU_BUTTON = ".calendar-grid-menu-host button";
/** An open MUI menu, the grid menu or one of its submenus. */
const OPEN_MENU = ".MuiMenu-list:visible";
/** The toolbar below a text box too small for its in-box language name and format gear. */
const SMALL_BOX_TOOLBAR = "#small-translation-group-toolbar";
/** The class a translation group carries while it is too small for its in-box affordances. */
const TOO_SMALL_CLASS = "bloom-tooSmallForInBoxAffordances";

/** One day box of a month grid, as the page shows it. */
export interface ICalendarDay {
    /** The day number shown, e.g. "25", or "" for a box outside the month with no number. */
    number: string;
    /** True for a date of the previous or the next month, shown only with neighboring dates on. */
    neighbor: boolean;
    /** True when the day number is drawn fainter than a day of the grid's own month. */
    faded: boolean;
}

/** What the calendar grid on the page says it is for. */
export interface ICalendarGridState {
    /** 0 for January through 11 for December. */
    month: number;
    /**
     * The year and first day of the week (0 = Sunday) the grid was last laid out for, or undefined
     * while it has not been laid out: an unconfigured Wall Calendar has no year yet.
     */
    laidOutFor?: { year: number; firstDayOfWeek: number };
    /** The weekday of each header column, left to right, 0 for Sunday. */
    weekdayColumns: number[];
    /** The day boxes the grid shows, in reading order. Rows the month does not need are hidden and left out. */
    days: ICalendarDay[];
}

/** What the collection has learned about calendars, from its configuration.txt. */
export interface ICalendarSettings {
    /** Twelve month names, January first; "" for one nobody has typed. */
    monthNames: string[];
    /** Seven weekday names, Sunday first; "" for one nobody has typed. */
    dayNames: string[];
    /** The first day of the week the next new calendar is offered, 0 for Sunday. */
    firstDayOfWeek: number;
}

/** What the Calendar Setup dialog shows. */
export interface ICalendarSetupDialogState {
    title: string;
    year: string;
    /** The label of the weekday the "First day of the week" list shows, e.g. "Sunday". */
    firstDayOfWeek: string;
    okEnabled: boolean;
}

/** What the toolbar below a small text box offers. */
export interface ISmallTextBoxToolbar {
    showing: boolean;
    hasFormatButton: boolean;
    hasCellMenuButton: boolean;
    /** The language name the toolbar shows, "" when it shows none. */
    languageName: string;
}

// ── The Calendar Setup dialog ───────────────────────────────────────────

/** The Calendar Setup dialog's panel, in Bloom's own window. */
export function calendarSetupDialog(page: Page): Locator {
    return page.locator(`.MuiDialog-paper:has(${SETUP_YEAR})`);
}

/**
 * Wait for the Calendar Setup dialog, which Bloom shows when a Wall Calendar book that has no year
 * yet is opened, and read what it shows. Throws, saying so, when it never comes up.
 */
export async function waitForCalendarSetupDialog(
    page: Page,
    timeoutMs = 60000,
): Promise<ICalendarSetupDialogState> {
    const dialog = calendarSetupDialog(page);
    await dialog.waitFor({ state: "visible", timeout: timeoutMs }).catch(() => {
        throw new Error(
            `The Calendar Setup dialog never appeared (waited ${timeoutMs / 1000}s). Bloom ` +
                `shows it only for a Wall Calendar book with no year, in a collection it can ` +
                `write to.`,
        );
    });
    return getCalendarSetupDialogState(page);
}

/** What the Calendar Setup dialog shows just now. The dialog must be open. */
export async function getCalendarSetupDialogState(
    page: Page,
): Promise<ICalendarSetupDialogState> {
    const dialog = calendarSetupDialog(page);
    return {
        title: (
            await dialog
                .locator("h1, h2, .MuiDialogTitle-root")
                .first()
                .innerText()
        ).trim(),
        year: await dialog.locator(SETUP_YEAR).inputValue(),
        firstDayOfWeek: (
            await dialog.locator(SETUP_FIRST_DAY).innerText()
        ).trim(),
        okEnabled: await calendarSetupOkButton(page).isEnabled(),
    };
}

/** The Calendar Setup dialog's OK button, for a test that waits on its state. */
export function calendarSetupOkButton(page: Page): Locator {
    return calendarSetupDialog(page).getByRole("button", {
        name: "OK",
        exact: true,
    });
}

/** Type into the Calendar Setup dialog's Year box, replacing what it holds. */
export async function typeCalendarSetupYear(
    page: Page,
    year: string,
): Promise<void> {
    const box = calendarSetupDialog(page).locator(SETUP_YEAR);
    await box.fill(year);
    await expect(box).toHaveValue(year);
}

/**
 * Choose the first day of the week in the Calendar Setup dialog, by the name the list shows, e.g.
 * "Monday". The list is a MUI select, which opens its choices in a popup of their own.
 */
export async function chooseCalendarSetupFirstDayOfWeek(
    page: Page,
    dayName: string,
): Promise<void> {
    const dialog = calendarSetupDialog(page);
    await dialog.locator(SETUP_FIRST_DAY).click();
    const list = page.locator("[role='listbox']:visible");
    await list.waitFor({ state: "visible", timeout: 30000 });
    const choice = list.getByRole("option", { name: dayName, exact: true });
    if ((await choice.count()) === 0) {
        const offered = await list.getByRole("option").allInnerTexts();
        throw new Error(
            `The "First day of the week" list has no "${dayName}". It offers: ${offered.join(", ")}.`,
        );
    }
    await choice.click();
    await list.waitFor({ state: "hidden", timeout: 30000 });
    await expect(dialog.locator(SETUP_FIRST_DAY)).toHaveText(dayName);
}

/** Click OK in the Calendar Setup dialog and wait for it to close. */
export async function clickCalendarSetupOk(page: Page): Promise<void> {
    await clickCalendarSetupButton(page, "OK");
}

/** Click Cancel in the Calendar Setup dialog and wait for it to close. */
export async function clickCalendarSetupCancel(page: Page): Promise<void> {
    await clickCalendarSetupButton(page, "Cancel");
}

async function clickCalendarSetupButton(
    page: Page,
    name: "OK" | "Cancel",
): Promise<void> {
    const dialog = calendarSetupDialog(page);
    const button = dialog.getByRole("button", { name, exact: true });
    await expect(
        button,
        `The Calendar Setup dialog's ${name} button is disabled.`,
    ).toBeEnabled();
    await button.click();
    await dialog.waitFor({ state: "hidden", timeout: 30000 });
}

/** Answer the Calendar Setup dialog with a year and a first day of the week, and click OK. */
export async function answerCalendarSetup(
    page: Page,
    year: number,
    firstDayName: string,
): Promise<void> {
    await waitForCalendarSetupDialog(page);
    await typeCalendarSetupYear(page, String(year));
    await chooseCalendarSetupFirstDayOfWeek(page, firstDayName);
    await clickCalendarSetupOk(page);
}

// ── The grid pages of a Wall Calendar book ──────────────────────────────

/**
 * The month-grid pages of the selected book, January first. A Wall Calendar book alternates a
 * picture page and a grid page for each month; this finds the grids by what is on them, a table
 * that says which month it is for, rather than by where they sit.
 */
export async function getCalendarGridPages(page: Page): Promise<IBookPage[]> {
    const grids: IBookPage[] = [];
    for (const bookPage of await getContentPages(page)) {
        const content = await apiGetJson<{ content?: string }>(
            page,
            `pageList/pageContent?page-id=${encodeURIComponent(bookPage.id)}`,
        );
        if (String(content.content ?? "").includes("data-calendar-month"))
            grids.push(bookPage);
    }
    return grids;
}

/**
 * Wait until Bloom is showing the book's first month grid, which it goes to by itself once the
 * Calendar Setup dialog is answered, and return the book's month-grid pages, January first.
 */
export async function waitForFirstMonthGridShown(
    page: Page,
): Promise<IBookPage[]> {
    const grids = await getCalendarGridPages(page);
    if (grids.length === 0)
        throw new Error("The book being edited has no month-grid pages.");
    await expect
        .poll(async () => getShownPageId(page), {
            timeout: 60000,
            message:
                "Bloom never went to the book's first month grid by itself.",
        })
        .toBe(grids[0].id);
    return grids;
}

/**
 * The day numbers a grid should show, box by box, with neighboring dates off: `leading` empty
 * boxes, the days 1 to `days`, then empty boxes to the end of the last row. Compare it with
 * dayNumbersOf.
 */
export function monthDayNumbers(leading: number, days: number): string[] {
    const boxes: string[] = new Array(leading).fill("");
    for (let day = 1; day <= days; day++) boxes.push(String(day));
    while (boxes.length % 7 !== 0) boxes.push("");
    return boxes;
}

/** The day numbers a grid shows, box by box, "" for a box with none: what a person reads off it. */
export function dayNumbersOf(grid: ICalendarGridState): string[] {
    return grid.days.map((day) => day.number);
}

/** The month grid on the page being edited, counting from 0 when a page has more than one. */
export function calendarGrid(page: Page, gridIndex = 0): Locator {
    return editablePageFrame(page).locator(GRID).nth(gridIndex);
}

/** The page being edited, whole, for a picture of a calendar page. */
export function calendarPageShown(page: Page): Locator {
    return editablePageFrame(page).locator(".bloom-page").first();
}

/**
 * The index a table has among the tables of the page being edited, for the table helpers, which
 * take one. A month grid is the only table on a Wall Calendar grid page, but a grid on a canvas can
 * share its page with other tables.
 */
export async function getCalendarGridTableIndex(
    page: Page,
    gridIndex = 0,
): Promise<number> {
    const grid = calendarGrid(page, gridIndex);
    await grid.waitFor({ state: "attached", timeout: 30000 });
    return grid.evaluate((element) =>
        Array.from(
            element.ownerDocument.querySelectorAll(".bloom-table"),
        ).indexOf(element),
    );
}

/** Read what one month grid on the page being edited says it is for, and the days it shows. */
export async function getCalendarGrid(
    page: Page,
    gridIndex = 0,
): Promise<ICalendarGridState> {
    const grid = calendarGrid(page, gridIndex);
    await grid.waitFor({ state: "attached", timeout: 30000 });
    return grid.evaluate((element) => {
        const signature = element.getAttribute("data-calendar-laid-out");
        const [year, firstDayOfWeek] = (signature ?? "")
            .split(",")
            .map((part) => parseInt(part, 10));
        const shown = (cell: Element) =>
            getComputedStyle(cell as HTMLElement).display !== "none";
        const days = Array.from(element.querySelectorAll(".calendarDayCell"))
            .filter(shown)
            .map((cell) => {
                const numberElement =
                    cell.querySelector<HTMLElement>(".calendarDayNumber");
                return {
                    number: numberElement?.textContent?.trim() ?? "",
                    neighbor: cell.classList.contains("calendarNeighborDay"),
                    faded:
                        !!numberElement &&
                        parseFloat(getComputedStyle(numberElement).opacity) < 1,
                };
            });
        return {
            month: parseInt(
                element.getAttribute("data-calendar-month") ?? "-1",
                10,
            ),
            laidOutFor: signature ? { year, firstDayOfWeek } : undefined,
            weekdayColumns: Array.from(
                element.querySelectorAll("[data-calendar-weekday]"),
            ).map((cell) =>
                parseInt(
                    cell.getAttribute("data-calendar-weekday") ?? "-1",
                    10,
                ),
            ),
            days,
        };
    });
}

/**
 * Wait until the grid on the page being edited has been laid out for this year and first day of
 * the week, and return what it then shows. The calendar tooling lays a grid out as its page
 * arrives, a moment after the page itself is showing, and again after each change from its menu.
 */
export async function waitForCalendarGridLaidOut(
    page: Page,
    wanted: { year: number; month: number; firstDayOfWeek: number },
    gridIndex = 0,
): Promise<ICalendarGridState> {
    let state: ICalendarGridState | undefined;
    try {
        await expect
            .poll(
                async () => {
                    state = await getCalendarGrid(page, gridIndex);
                    return (
                        state.month === wanted.month &&
                        state.laidOutFor?.year === wanted.year &&
                        state.laidOutFor?.firstDayOfWeek ===
                            wanted.firstDayOfWeek
                    );
                },
                { timeout: 30000 },
            )
            .toBe(true);
    } catch {
        throw new Error(
            `The calendar grid was never laid out for month ${wanted.month} of ${wanted.year} ` +
                `with day ${wanted.firstDayOfWeek} first. It says month ${state?.month}, laid out ` +
                `for ${JSON.stringify(state?.laidOutFor ?? "nothing yet")}.`,
        );
    }
    return state!;
}

/**
 * The table row and column of the box that shows a day of the grid's own month (not a neighboring
 * month's date with the same number), for the table helpers: row 0 is the weekday row, so the
 * first row of days is row 1.
 */
export async function getDayCellPosition(
    page: Page,
    day: number,
    gridIndex = 0,
): Promise<{ row: number; column: number }> {
    const grid = calendarGrid(page, gridIndex);
    const position = await grid.evaluate((element, wantedDay) => {
        const cells = Array.from(element.children).filter((child) =>
            child.classList.contains("bloom-cell"),
        );
        const index = cells.findIndex(
            (cell) =>
                cell.classList.contains("calendarDayCell") &&
                !cell.classList.contains("calendarNeighborDay") &&
                cell
                    .querySelector(".calendarDayNumber")
                    ?.textContent?.trim() === String(wantedDay),
        );
        return index < 0
            ? undefined
            : { row: Math.floor(index / 7), column: index % 7 };
    }, day);
    if (!position)
        throw new Error(
            `The calendar grid shows no day ${day} of its own month.`,
        );
    return position;
}

/**
 * The name one weekday header shows in one language, found by the weekday (0 = Sunday) rather than
 * by its column, which depends on the first day of the week.
 */
export async function getWeekdayName(
    page: Page,
    weekday: number,
    languageTag: string,
    gridIndex = 0,
): Promise<string> {
    const box = calendarGrid(page, gridIndex)
        .locator(
            `[data-calendar-weekday="${weekday}"] .bloom-editable[lang="${languageTag}"]`,
        )
        .first();
    await box.waitFor({ state: "attached", timeout: 30000 });
    return ((await box.textContent()) ?? "").trim();
}

/** The month name the page's title shows in one language. */
export async function getMonthName(
    page: Page,
    languageTag: string,
): Promise<string> {
    const box = editablePageFrame(page)
        .locator(`.calendarMonthName .bloom-editable[lang="${languageTag}"]`)
        .first();
    await box.waitFor({ state: "attached", timeout: 30000 });
    return ((await box.textContent()) ?? "").trim();
}

/** What the collection has learned about calendars, as Bloom reports it. */
export async function getCalendarSettings(
    page: Page,
): Promise<ICalendarSettings> {
    return apiGetJson<ICalendarSettings>(page, "calendarSettings");
}

// ── The grid's button and its menu ──────────────────────────────────────

/**
 * Point at the grid, which is what brings its round button out, click the button, and wait for its
 * menu. Returns the menu's rows as it labels them: "First Day of Week", then the grid's month
 * ("January"), its year ("2027"), and "Show Neighboring Month Dates".
 *
 * Only a grid that sits directly on a page has this button; a grid on a canvas has the same
 * commands in its canvas element menu instead (helpers/canvasElements.ts).
 */
export async function openCalendarGridMenu(
    page: Page,
    gridIndex = 0,
): Promise<string[]> {
    const grid = calendarGrid(page, gridIndex);
    await grid.waitFor({ state: "visible", timeout: 30000 });
    await grid.hover();
    const button = editablePageFrame(page)
        .locator(GRID_MENU_BUTTON)
        .nth(gridIndex);
    await expect(
        button,
        "Pointing at the calendar grid did not bring out its round button.",
    ).toBeVisible({ timeout: 15000 });
    await realClick(button);
    const menu = editablePageFrame(page).locator(OPEN_MENU).first();
    await menu.waitFor({ state: "visible", timeout: 15000 });
    return getOpenMenuLabels(page);
}

/** The labels of the rows of the menu opened last, top to bottom. */
export async function getOpenMenuLabels(page: Page): Promise<string[]> {
    const menu = editablePageFrame(page).locator(OPEN_MENU).last();
    await menu.waitFor({ state: "visible", timeout: 15000 });
    return (
        await menu.locator(":scope > li, :scope > div > li").allInnerTexts()
    ).map((t) => t.trim());
}

/** The menu opened last, and any submenu, for a picture of it. */
export function openMenuPanel(page: Page): Locator {
    return editablePageFrame(page).locator(".MuiMenu-paper:visible").last();
}

/**
 * The rows of one of the grid menu's submenus, opened by pointing at its row (e.g. "January"), and
 * which of them has the check mark. Leaves the submenu open.
 */
export async function getCalendarGridSubmenu(
    page: Page,
    rowLabel: string,
): Promise<{ choices: string[]; checked: string[] }> {
    await pointAtMenuRow(page, rowLabel);
    const submenu = editablePageFrame(page).locator(OPEN_MENU).last();
    const rows = submenu.locator("li");
    const choices = (await rows.allInnerTexts()).map((t) => t.trim());
    const checked = (
        await submenu
            .locator("li:has(svg[data-testid='CheckIcon'])")
            .allInnerTexts()
    ).map((t) => t.trim());
    return { choices, checked };
}

/**
 * Choose something from the grid menu: point at `rowLabel` to open its submenu, then click
 * `choice` there, e.g. ("January", "March"). The grid's month, year and first day are each a
 * submenu, labelled with the grid's current value. Opens the menu afresh, shutting it first if
 * it is open, and returns once the menu has closed.
 */
export async function chooseFromCalendarGridMenu(
    page: Page,
    rowLabel: string,
    choice: string,
    gridIndex = 0,
): Promise<void> {
    // Always from a freshly opened menu. The menu library renders a submenu inside the row that
    // opened it, so while one is open that row's accessible name runs on into every row of the
    // submenu, and the row can no longer be found by its own label.
    if (await editablePageFrame(page).locator(OPEN_MENU).first().isVisible())
        await closeCalendarGridMenu(page);
    await openCalendarGridMenu(page, gridIndex);
    await pointAtMenuRow(page, rowLabel);
    const submenu = editablePageFrame(page).locator(OPEN_MENU).last();
    const item = submenu.getByRole("menuitem", { name: choice, exact: true });
    if ((await item.count()) === 0) {
        const offered = (await submenu.locator("li").allInnerTexts()).map((t) =>
            t.trim(),
        );
        throw new Error(
            `The "${rowLabel}" submenu has no "${choice}". It offers: ${offered.join(", ")}.`,
        );
    }
    await item.click();
    await expect(
        editablePageFrame(page).locator(OPEN_MENU),
        `The calendar grid menu stayed open after choosing "${choice}".`,
    ).toHaveCount(0, { timeout: 15000 });
}

/**
 * Click the grid menu's "Show Neighboring Month Dates" row, which turns the neighboring months'
 * dates on or off. Opens the menu first if it is shut, and returns once the menu has closed.
 */
export async function toggleShowNeighboringMonthDates(
    page: Page,
    gridIndex = 0,
): Promise<void> {
    if (!(await editablePageFrame(page).locator(OPEN_MENU).first().isVisible()))
        await openCalendarGridMenu(page, gridIndex);
    await editablePageFrame(page)
        .locator(
            `${OPEN_MENU} li[data-testid="EditTab.CalendarGrid.ShowNeighboringMonthDates"]`,
        )
        .click();
    await expect(editablePageFrame(page).locator(OPEN_MENU)).toHaveCount(0, {
        timeout: 15000,
    });
}

/**
 * Whether the grid menu's "Show Neighboring Month Dates" row has its check mark. The menu must be
 * open.
 */
export async function isShowNeighboringMonthDatesChecked(
    page: Page,
): Promise<boolean> {
    const row = editablePageFrame(page).locator(
        `${OPEN_MENU} li[data-testid="EditTab.CalendarGrid.ShowNeighboringMonthDates"]`,
    );
    await row.waitFor({ state: "visible", timeout: 15000 });
    return (await row.locator("svg[data-testid='CheckIcon']").count()) > 0;
}

/**
 * Close the grid menu without choosing anything, the way Escape does.
 *
 * Not closeAnyMenu (helpers/tables.ts), which sends Escape to the first open menu: this menu's
 * submenus are drawn inside the row that opened them, and an Escape has to reach the innermost
 * open one to shut it before the menu around it can go, so this presses it on the last.
 */
export async function closeCalendarGridMenu(page: Page): Promise<void> {
    const menus = editablePageFrame(page).locator(OPEN_MENU);
    for (let attempt = 0; attempt < 3 && (await menus.count()) > 0; attempt++) {
        await menus
            .last()
            .press("Escape")
            .catch(() => undefined);
        await expect(menus)
            .toHaveCount(0, { timeout: 3000 })
            .catch(() => undefined);
    }
    await expect(menus, "The calendar grid menu would not close.").toHaveCount(
        0,
    );
}

/** Point at one row of the menu opened last, which opens that row's submenu, and wait for it. */
async function pointAtMenuRow(page: Page, rowLabel: string): Promise<void> {
    const frame = editablePageFrame(page);
    const menu = frame.locator(OPEN_MENU).first();
    await menu.waitFor({ state: "visible", timeout: 15000 });
    const row = menu
        .getByRole("menuitem", { name: rowLabel, exact: true })
        .first();
    if ((await row.count()) === 0) {
        const offered = await getOpenMenuLabels(page);
        throw new Error(
            `The menu has no "${rowLabel}" row. It offers: ${offered.join(", ")}.`,
        );
    }
    await row.hover();
    // The top menu and one submenu. Pointing again at a row whose submenu is already open keeps
    // that submenu, so this is a count to reach, not one more than before.
    await expect(
        frame.locator(OPEN_MENU),
        `Pointing at the "${rowLabel}" row did not open its submenu.`,
    ).toHaveCount(2, { timeout: 15000 });
}

// ── Day numbers and small text boxes ────────────────────────────────────

/**
 * Click a day's number, which a person cannot type in but can format: it takes the focus, and the
 * format gear appears beside it (helpers/formatDialog.ts takes it from there).
 */
export async function clickDayNumber(
    page: Page,
    day: number,
    gridIndex = 0,
): Promise<void> {
    const { row, column } = await getDayCellPosition(page, day, gridIndex);
    const number = (await dayCell(page, row, column, gridIndex)).locator(
        ".calendarDayNumber",
    );
    await realClick(number);
    await expect(
        number,
        `Clicking the number of day ${day} did not give it the focus.`,
    ).toBeFocused({
        timeout: 15000,
    });
}

/** The box of the table cell at this row and column of the grid, for the table helpers. */
async function dayCell(
    page: Page,
    row: number,
    column: number,
    gridIndex: number,
): Promise<Locator> {
    return cell(
        page,
        row,
        column,
        await getCalendarGridTableIndex(page, gridIndex),
    );
}

/**
 * Whether the text box of a day is marked too small to carry its own language name and format gear,
 * which is what hands both to the toolbar below it.
 */
export async function isDayNoteTooSmallForInBoxAffordances(
    page: Page,
    day: number,
    gridIndex = 0,
): Promise<boolean> {
    const { row, column } = await getDayCellPosition(page, day, gridIndex);
    const group = (await dayCell(page, row, column, gridIndex))
        .locator(".bloom-translationGroup")
        .first();
    await group.waitFor({ state: "attached", timeout: 30000 });
    return group.evaluate(
        (element, cls) => element.classList.contains(cls),
        TOO_SMALL_CLASS,
    );
}

/**
 * What the toolbar below a small text box offers, for the text box that has the focus. Waits for it
 * to appear, since Bloom places it a moment after the box takes the focus, and reports it as not
 * showing if it never does.
 */
export async function getSmallTextBoxToolbar(
    page: Page,
): Promise<ISmallTextBoxToolbar> {
    const toolbar = editablePageFrame(page).locator(SMALL_BOX_TOOLBAR);
    const bar = toolbar.locator(
        "[data-testid='small-translation-group-format-button']",
    );
    const showing = await bar
        .waitFor({ state: "visible", timeout: 15000 })
        .then(() => true)
        .catch(() => false);
    if (!showing)
        return {
            showing: false,
            hasFormatButton: false,
            hasCellMenuButton: false,
            languageName: "",
        };
    return {
        showing: true,
        hasFormatButton: true,
        hasCellMenuButton:
            (await toolbar
                .locator(
                    "[data-testid='small-translation-group-cell-menu-button']:visible",
                )
                .count()) > 0,
        languageName: (await toolbar.innerText()).trim(),
    };
}

/** The toolbar below a small text box, for a picture of it. */
export function smallTextBoxToolbar(page: Page): Locator {
    return editablePageFrame(page).locator(
        `${SMALL_BOX_TOOLBAR} .small-translation-group-toolbar-bar`,
    );
}

/**
 * Click the "..." button of the toolbar below a small text box in a table cell, which opens the
 * table's Cell menu, the one a right-click in the cell opens. Waits for the Cell menu.
 */
export async function openCellMenuFromSmallTextBoxToolbar(
    page: Page,
): Promise<void> {
    await realClick(
        editablePageFrame(page).locator(
            `${SMALL_BOX_TOOLBAR} [data-testid='small-translation-group-cell-menu-button']`,
        ),
    );
    await editablePageFrame(page)
        .locator('[data-btable-menu="cell"]')
        .waitFor({ state: "visible", timeout: 15000 });
}
