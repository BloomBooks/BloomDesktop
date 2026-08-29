import { afterEach, describe, expect, it, vi } from "vitest";

// The actions redraw the grid through the table library, which belongs to neither what these
// tests are about nor to a test environment without a browser, so it is replaced here.
vi.mock("../js/tableEditing", () => ({ RerenderTables: () => {} }));

// The names a menu offers come from the language Bloom's user interface runs in. The tests
// name that language themselves, so that they do not depend on the machine they run on.
const userInterface = vi.hoisted(() => ({ locale: "en" }));
vi.mock("../../lib/localizationManager/localizationManager", () => ({
    default: { getCurrentUILocale: () => userInterface.locale },
}));

import {
    getFirstDayOfWeekOfGrid,
    getMonthNamesToOffer,
    getWeekdayNamesToOffer,
    getYearOfGrid,
    getYearsToOffer,
    setFirstDayOfWeekOfGrid,
    setYearOfGrid,
} from "./calendarGridActions";
import { buildCalendarGridTable } from "./buildCalendarGridTable";
import {
    ICalendarMonthLayout,
    kCalendarFirstDayAttribute,
    kCalendarYearAttribute,
} from "./layOutCalendarMonthPage";

/** March of the given year, the month every grid these tests build is for. */
function marchOf(year: number, firstDayOfWeek: number): ICalendarMonthLayout {
    return { year, month: 2, firstDayOfWeek, showNeighborDays: false };
}

/**
 * A Wall Calendar grid page: the year and the first day of the week are page elements, so the
 * built grid's own copies of them are taken off again.
 */
function makeWallCalendarPage(options: {
    year: string;
    firstDayOfWeek: string;
}): { page: HTMLElement; grid: HTMLElement } {
    const page = document.createElement("div");
    page.className = "bloom-page";
    const grid = document.createElement("div");
    grid.className = "bloom-table";
    page.appendChild(grid);
    buildCalendarGridTable(grid, marchOf(2027, 0));
    grid.removeAttribute(kCalendarYearAttribute);
    grid.removeAttribute(kCalendarFirstDayAttribute);
    const year = document.createElement("div");
    year.setAttribute("data-book", "calendarYear");
    year.textContent = options.year;
    page.appendChild(year);
    const firstDay = document.createElement("div");
    firstDay.setAttribute("data-book", "calendarFirstDayOfWeek");
    firstDay.textContent = options.firstDayOfWeek;
    page.appendChild(firstDay);
    return { page, grid };
}

/** A grid on a canvas: it carries its own year and first day of the week. */
function makeCanvasGridPage(options: {
    year: string;
    firstDayOfWeek: string;
}): { page: HTMLElement; grid: HTMLElement } {
    const page = document.createElement("div");
    page.className = "bloom-page";
    const canvasElement = document.createElement("div");
    canvasElement.className = "bloom-canvas-element";
    const grid = document.createElement("div");
    grid.className = "bloom-table";
    canvasElement.appendChild(grid);
    buildCalendarGridTable(
        grid,
        marchOf(
            parseInt(options.year, 10),
            parseInt(options.firstDayOfWeek, 10),
        ),
    );
    page.appendChild(canvasElement);
    return { page, grid };
}

/** The year of the page element a Wall Calendar page keeps the book's year in. */
function yearOnPage(page: HTMLElement): string {
    return (
        page.querySelector<HTMLElement>("[data-book='calendarYear']")
            ?.textContent ?? ""
    );
}

/** Where the week starts, as the page element of a Wall Calendar page holds it. */
function firstDayOnPage(page: HTMLElement): string {
    return (
        page.querySelector<HTMLElement>("[data-book='calendarFirstDayOfWeek']")
            ?.textContent ?? ""
    );
}

describe("setYearOfGrid", () => {
    it("writes the table attribute of a grid on a canvas", () => {
        const { grid } = makeCanvasGridPage({
            year: "2027",
            firstDayOfWeek: "0",
        });
        expect(getYearOfGrid(grid)).toBe(2027);

        setYearOfGrid(grid, 2031);

        expect(grid.getAttribute(kCalendarYearAttribute)).toBe("2031");
        expect(getYearOfGrid(grid)).toBe(2031);
    });

    it("writes the page element of a grid on a Wall Calendar page", () => {
        const { page, grid } = makeWallCalendarPage({
            year: "2027",
            firstDayOfWeek: "0",
        });
        expect(getYearOfGrid(grid)).toBe(2027);

        setYearOfGrid(grid, 2031);

        expect(yearOnPage(page)).toBe("2031");
        expect(grid.hasAttribute(kCalendarYearAttribute)).toBe(false);
        expect(getYearOfGrid(grid)).toBe(2031);
    });

    it("gives an unconfigured Wall Calendar page its first year", () => {
        const { page, grid } = makeWallCalendarPage({
            year: "",
            firstDayOfWeek: "0",
        });
        expect(getYearOfGrid(grid)).toBeUndefined();

        setYearOfGrid(grid, 2029);

        expect(yearOnPage(page)).toBe("2029");
        expect(getYearOfGrid(grid)).toBe(2029);
    });
});

describe("setFirstDayOfWeekOfGrid", () => {
    it("writes the table attribute of a grid on a canvas", () => {
        const { grid } = makeCanvasGridPage({
            year: "2027",
            firstDayOfWeek: "0",
        });
        expect(getFirstDayOfWeekOfGrid(grid)).toBe(0);

        setFirstDayOfWeekOfGrid(grid, 1);

        expect(grid.getAttribute(kCalendarFirstDayAttribute)).toBe("1");
        expect(getFirstDayOfWeekOfGrid(grid)).toBe(1);
    });

    it("writes the page element of a grid on a Wall Calendar page", () => {
        const { page, grid } = makeWallCalendarPage({
            year: "2027",
            firstDayOfWeek: "0",
        });

        setFirstDayOfWeekOfGrid(grid, 1);

        expect(firstDayOnPage(page)).toBe("1");
        expect(grid.hasAttribute(kCalendarFirstDayAttribute)).toBe(false);
        expect(getFirstDayOfWeekOfGrid(grid)).toBe(1);
    });
});

describe("getFirstDayOfWeekOfGrid", () => {
    it("is Sunday for a grid whose book has a year but has not said where the week starts", () => {
        const { grid } = makeWallCalendarPage({
            year: "2027",
            firstDayOfWeek: "",
        });
        // Sanity check: nothing has written a first day of the week down anywhere.
        expect(firstDayOnPage(grid.closest(".bloom-page")!)).toBe("");
        expect(grid.hasAttribute(kCalendarFirstDayAttribute)).toBe(false);

        expect(getFirstDayOfWeekOfGrid(grid)).toBe(0);
    });

    it("is nothing for a grid whose book has no year yet", () => {
        const { grid } = makeWallCalendarPage({
            year: "",
            firstDayOfWeek: "",
        });

        expect(getFirstDayOfWeekOfGrid(grid)).toBeUndefined();
    });
});

describe("getYearsToOffer", () => {
    it("offers this year and the two after it", () => {
        const { grid } = makeCanvasGridPage({
            year: String(new Date().getFullYear()),
            firstDayOfWeek: "0",
        });
        const thisYear = new Date().getFullYear();

        expect(getYearsToOffer(grid)).toEqual([
            thisYear,
            thisYear + 1,
            thisYear + 2,
        ]);
    });

    it("also offers a year the grid is already for, in order", () => {
        const thisYear = new Date().getFullYear();
        const { grid } = makeCanvasGridPage({
            year: String(thisYear - 3),
            firstDayOfWeek: "0",
        });

        expect(getYearsToOffer(grid)).toEqual([
            thisYear - 3,
            thisYear,
            thisYear + 1,
            thisYear + 2,
        ]);
    });

    it("offers only the three years when the book has named none", () => {
        const { grid } = makeWallCalendarPage({
            year: "",
            firstDayOfWeek: "0",
        });
        const thisYear = new Date().getFullYear();

        expect(getYearsToOffer(grid)).toEqual([
            thisYear,
            thisYear + 1,
            thisYear + 2,
        ]);
    });
});

describe("the names a menu offers", () => {
    afterEach(() => {
        userInterface.locale = "en";
    });

    it("names the months in the user interface language", () => {
        expect(getMonthNamesToOffer()).toEqual([
            "January",
            "February",
            "March",
            "April",
            "May",
            "June",
            "July",
            "August",
            "September",
            "October",
            "November",
            "December",
        ]);
    });

    it("names the days of the week in the user interface language, Sunday first", () => {
        expect(getWeekdayNamesToOffer()).toEqual([
            "Sunday",
            "Monday",
            "Tuesday",
            "Wednesday",
            "Thursday",
            "Friday",
            "Saturday",
        ]);
    });

    it("names the months in another user interface language", () => {
        userInterface.locale = "fr";

        expect(getMonthNamesToOffer()[0]).toBe("janvier");
    });

    it("falls back to English when the language tag is unusable", () => {
        // A tag Intl cannot make a format for: it throws a RangeError, and the menu must
        // still have names in it.
        userInterface.locale = "not a language tag";

        expect(getMonthNamesToOffer()[0]).toBe("January");
        expect(getWeekdayNamesToOffer()[0]).toBe("Sunday");
    });
});
