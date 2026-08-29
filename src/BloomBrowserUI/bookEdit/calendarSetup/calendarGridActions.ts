// What the user can do to a calendar month grid: choose the month and the year it is for, say
// which day its week starts on, and say whether it shows the dates of the neighboring months.
//
// Two menus offer these: the little button that appears over a grid sitting directly on a page
// (CalendarGridMenu.tsx), and the Calendar section of the context menu of a canvas element
// holding a grid (the calendarMonth, calendarYear, calendarFirstDayOfWeek and
// calendarShowNeighborDays controls of canvasControlRegistry.ts). Both call the functions here,
// so the two menus do the same thing.

import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { RerenderTables } from "../js/tableEditing";
import {
    kCalendarFirstDayDataBookSelector,
    kCalendarYearDataBookSelector,
    kDefaultFirstDayOfWeek,
    resolveGridFirstDayOfWeek,
    resolveGridLayout,
    resolveGridYear,
} from "./calendarGrids";
import {
    getShowNeighborDays,
    kCalendarColumnCount,
    kCalendarFirstDayAttribute,
    kCalendarMonthAttribute,
    kCalendarNeighborDaysAttribute,
    kCalendarYearAttribute,
    layOutCalendarMonthPage,
} from "./layOutCalendarMonthPage";

/** The number of months a menu offers. */
export const kMonthCount = 12;

/** The number of years a menu offers, counting from the current one. */
export const kYearCount = 3;

/** The month grid inside a canvas element, or null if it holds something else. */
export function getGridOfCanvasElement(
    canvasElement: HTMLElement,
): HTMLElement | null {
    return canvasElement.querySelector<HTMLElement>(
        `.bloom-table[${kCalendarMonthAttribute}]`,
    );
}

/** The month this grid is for, or January if it somehow says nothing usable. */
export function getMonthOfGrid(grid: HTMLElement): number {
    const month = parseInt(
        grid.getAttribute(kCalendarMonthAttribute) ?? "",
        10,
    );
    return Number.isNaN(month) ? 0 : month;
}

/**
 * The year this grid is for, or undefined if the book has not said yet. An unconfigured Wall
 * Calendar has no year, which is why its grids show no day numbers.
 */
export function getYearOfGrid(grid: HTMLElement): number | undefined {
    const pageElement = grid.closest<HTMLElement>(".bloom-page");
    return pageElement ? resolveGridYear(grid, pageElement) : undefined;
}

/**
 * Which day this grid's week starts on, 0 for Sunday, or undefined if nothing says yet.
 *
 * A grid the book has given a year to is laid out Sunday first until the user says otherwise,
 * so that is the day the menu must show the check on, even though nothing has written it down.
 */
export function getFirstDayOfWeekOfGrid(grid: HTMLElement): number | undefined {
    const pageElement = grid.closest<HTMLElement>(".bloom-page");
    if (!pageElement) return undefined;
    const day = resolveGridFirstDayOfWeek(grid, pageElement);
    if (day !== undefined) return day;
    return resolveGridYear(grid, pageElement) === undefined
        ? undefined
        : kDefaultFirstDayOfWeek;
}

/** Whether this grid shows the dates of the months on either side of it. */
export function getGridShowsNeighborDays(grid: HTMLElement): boolean {
    return getShowNeighborDays(grid);
}

/**
 * Make this grid the grid of another month.
 *
 * This changes the dates and nothing else. The name of the month is a text field of the page,
 * outside the grid, and belongs to the user: they may have written a title of their own there,
 * so we never overwrite it.
 */
export function setMonthOfGrid(grid: HTMLElement, month: number): void {
    grid.setAttribute(kCalendarMonthAttribute, String(month));
    relayOutTheGrid(grid);
}

/**
 * Make this grid the grid of another year.
 *
 * Where the year is written decides how much of the book it changes, and that is what the user
 * expects of each kind of grid. A grid on a canvas stands on its own, so the year goes on the
 * table. A Wall Calendar page grid shares one year with the whole book, so the year goes into
 * the page's data-book element and Bloom's data-div carries it to the other pages.
 */
export function setYearOfGrid(grid: HTMLElement, year: number): void {
    writeGridSetting(
        grid,
        kCalendarYearAttribute,
        kCalendarYearDataBookSelector,
        year,
    );
}

/** Make this grid's week start on another day, 0 for Sunday. Written like the year. */
export function setFirstDayOfWeekOfGrid(grid: HTMLElement, day: number): void {
    writeGridSetting(
        grid,
        kCalendarFirstDayAttribute,
        kCalendarFirstDayDataBookSelector,
        day,
    );
}

/**
 * Write one number either onto the grid's table or into the page element that holds it for the
 * whole book, and lay the grid out again for what it now says.
 *
 * A grid that already carries the attribute keeps carrying it, whatever page it is on: it was
 * built to stand on its own and must go on doing so. So does a grid on a page that has no
 * element for this value, which is any page but a Wall Calendar grid page.
 */
function writeGridSetting(
    grid: HTMLElement,
    attributeName: string,
    dataBookSelector: string,
    value: number,
): void {
    const pageElement = grid.closest<HTMLElement>(".bloom-page");
    const dataBookElement =
        pageElement?.querySelector<HTMLElement>(dataBookSelector);
    const belongsToTheGridAlone =
        grid.hasAttribute(attributeName) ||
        !!grid.closest(".bloom-canvas-element") ||
        !dataBookElement;
    if (belongsToTheGridAlone) grid.setAttribute(attributeName, String(value));
    else dataBookElement!.textContent = String(value);
    relayOutTheGrid(grid);
}

/** Show or stop showing the dates of the months on either side of this one. */
export function setGridShowsNeighborDays(
    grid: HTMLElement,
    show: boolean,
): void {
    if (show) grid.setAttribute(kCalendarNeighborDaysAttribute, "true");
    else grid.removeAttribute(kCalendarNeighborDaysAttribute);
    relayOutTheGrid(grid);
}

/** Draw the grid again for what it now says it is, after a change from a menu. */
function relayOutTheGrid(grid: HTMLElement): void {
    const pageElement = grid.closest<HTMLElement>(".bloom-page");
    const layout = pageElement
        ? resolveGridLayout(grid, pageElement)
        : undefined;
    if (layout) layOutCalendarMonthPage(grid, layout);
    // The table library reads the borders the layout has just rewritten when it draws, and it
    // draws when it attaches, so this is how we ask it to draw them.
    RerenderTables(grid);
}

/**
 * The names to offer the twelve months by, in Bloom's current user interface language.
 *
 * These menus are part of Bloom's user interface, not of the book, so they name a month in the
 * language the user runs Bloom in. The collection's own names for the months stay where they
 * are content: in the grid itself, which the user writes and edits directly.
 */
export function getMonthNamesToOffer(): string[] {
    const format = makeDateFormat({ month: "long" });
    return Array.from({ length: kMonthCount }, (unused, month) =>
        // The 15th, so that no time zone can move the date into another month.
        format.format(new Date(2021, month, 15)),
    );
}

/**
 * The names to offer the seven days of the week by, Sunday first, in Bloom's current user
 * interface language. Sunday first because that is how the first day of the week is stored.
 *
 * Like the month names these name part of Bloom's user interface rather than the book, so they
 * follow the language the user runs Bloom in.
 */
export function getWeekdayNamesToOffer(): string[] {
    const format = makeDateFormat({ weekday: "long" });
    // The 15th of August 2021 was a Sunday, so day 0 of the week is the 15th.
    return Array.from({ length: kCalendarColumnCount }, (unused, day) =>
        format.format(new Date(2021, 7, 15 + day)),
    );
}

/**
 * A date format for Bloom's current user interface language, or for English if that language
 * is one Intl cannot make a format for. An unusable language tag throws a RangeError, and a
 * menu with no names in it is worse than a menu of English ones.
 */
function makeDateFormat(
    options: Intl.DateTimeFormatOptions,
): Intl.DateTimeFormat {
    try {
        return new Intl.DateTimeFormat(
            theOneLocalizationManager.getCurrentUILocale(),
            options,
        );
    } catch {
        return new Intl.DateTimeFormat("en", options);
    }
}

/**
 * The years to offer a grid: this year and the two after it, plus the year the grid is already
 * for when that is some other year, so that a menu always shows what the grid says it is.
 */
export function getYearsToOffer(grid: HTMLElement): number[] {
    const thisYear = new Date().getFullYear();
    const years = Array.from(
        { length: kYearCount },
        (unused, index) => thisYear + index,
    );
    const yearOfGrid = getYearOfGrid(grid);
    if (yearOfGrid !== undefined && !years.includes(yearOfGrid))
        years.push(yearOfGrid);
    return years.sort((a, b) => a - b);
}
