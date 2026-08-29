import { describe, expect, it } from "vitest";
import { buildCalendarGridTable } from "./buildCalendarGridTable";
import { kCalendarSeedLanguages, kWeekdaySeedNames } from "./calendarSeedNames";
import {
    calendarTableNeedsLayout,
    ICalendarMonthLayout,
    kCalendarColumnCount,
    kCalendarDayRowCount,
    kCalendarFirstDayAttribute,
    kCalendarMonthAttribute,
    kCalendarNeighborDaysAttribute,
    kCalendarYearAttribute,
} from "./layOutCalendarMonthPage";

const kSundayFirst = 0;

/** March 2027, the month the tests below build unless they say otherwise. */
const kMarch2027: ICalendarMonthLayout = {
    year: 2027,
    month: 2,
    firstDayOfWeek: kSundayFirst,
    showNeighborDays: false,
};

function buildTable(layout: ICalendarMonthLayout): HTMLElement {
    const table = document.createElement("div");
    table.className = "bloom-table";
    buildCalendarGridTable(table, layout);
    return table;
}

/** The header cells: the direct-child cells that are not day cells. */
function getWeekdayCells(table: HTMLElement): HTMLElement[] {
    return (Array.from(table.children) as HTMLElement[]).filter(
        (child) =>
            child.classList.contains("bloom-cell") &&
            !child.classList.contains("calendarDayCell"),
    );
}

describe("buildCalendarGridTable", () => {
    it("builds the seven weekday cells and the forty-two day cells, and nothing else", () => {
        const table = buildTable(kMarch2027);
        expect(getWeekdayCells(table).length).toBe(kCalendarColumnCount);
        expect(table.querySelectorAll(".calendarDayCell").length).toBe(
            kCalendarColumnCount * kCalendarDayRowCount,
        );
        expect(table.children.length).toBe(
            kCalendarColumnCount + kCalendarColumnCount * kCalendarDayRowCount,
        );
    });

    it("has no title row: no spanning cell and no skip cells", () => {
        const table = buildTable(kMarch2027);
        expect(table.querySelectorAll("[data-span-x]").length).toBe(0);
        expect(table.querySelectorAll(".bloom-skip").length).toBe(0);
        expect(table.querySelectorAll(".calendarTitleRowContents").length).toBe(
            0,
        );
    });

    it("holds no month name and no year, which are page content, not grid content", () => {
        const table = buildTable(kMarch2027);
        expect(table.querySelectorAll(".calendarMonthName").length).toBe(0);
        expect(table.querySelectorAll(".calendarYear").length).toBe(0);
        expect(table.querySelectorAll(".calendarFirstDayOfWeek").length).toBe(
            0,
        );
        expect(table.querySelectorAll(".CalendarMonth-style").length).toBe(0);
        // Sanity check: the year the grid is for is not lost, it is on the table.
        expect(table.getAttribute(kCalendarYearAttribute)).toBe("2027");
    });

    it("uses no data-book attribute anywhere, so the book's data-div never touches it", () => {
        const table = buildTable(kMarch2027);
        expect(table.querySelectorAll("[data-book]").length).toBe(0);
        expect(table.hasAttribute("data-book")).toBe(false);
    });

    it("seeds the weekday names in every seed language, Sunday first", () => {
        const table = buildTable(kMarch2027);
        const cells = getWeekdayCells(table);
        const englishNames = cells.map(
            (cell) =>
                cell.querySelector<HTMLElement>("[lang='en']")?.textContent ??
                "",
        );
        expect(englishNames).toEqual(kWeekdaySeedNames.en);
        const firstCellLanguages = Array.from(
            cells[0].querySelectorAll<HTMLElement>(".bloom-editable"),
        ).map((editable) => editable.getAttribute("lang"));
        expect(firstCellLanguages).toEqual(kCalendarSeedLanguages);
        expect(
            cells[0].querySelector<HTMLElement>("[lang='fr']")?.textContent,
        ).toBe(kWeekdaySeedNames.fr[0]);
    });

    it("records on the table what it was built for", () => {
        const table = buildTable({ ...kMarch2027, showNeighborDays: true });
        expect(table.getAttribute(kCalendarMonthAttribute)).toBe("2");
        expect(table.getAttribute(kCalendarYearAttribute)).toBe("2027");
        expect(table.getAttribute(kCalendarFirstDayAttribute)).toBe("0");
        expect(table.getAttribute(kCalendarNeighborDaysAttribute)).toBe("true");
        expect(table.getAttribute("data-column-widths")).toBe(
            "fill,fill,fill,fill,fill,fill,fill",
        );
    });

    it("leaves the neighbor-days attribute off when the option is off", () => {
        const table = buildTable(kMarch2027);
        expect(table.hasAttribute(kCalendarNeighborDaysAttribute)).toBe(false);
    });

    it("comes back already laid out for the month it was built for", () => {
        const table = buildTable(kMarch2027);
        expect(calendarTableNeedsLayout(table, kMarch2027)).toBe(false);
        // Sanity check: it really did lay the days out. 1 March 2027 is a Monday, so
        // day 1 is in the second cell of a Sunday-first week.
        expect(new Date(2027, 2, 1).getDay()).toBe(1);
        const numbers = Array.from(
            table.querySelectorAll<HTMLElement>(".calendarDayCell"),
        ).map(
            (cell) =>
                cell.querySelector<HTMLElement>(".calendarDayNumber")
                    ?.textContent ?? "",
        );
        expect(numbers[0]).toBe("");
        expect(numbers[1]).toBe("1");
        expect(numbers[31]).toBe("31");
        // March 2027 fits in five rows, so the table declares the weekday row and five.
        expect(table.getAttribute("data-row-heights")).toBe(
            "hug,fill,fill,fill,fill,fill",
        );
        // A different year is a different layout, so it would be laid out again.
        expect(
            calendarTableNeedsLayout(table, { ...kMarch2027, year: 2028 }),
        ).toBe(true);
    });
});
