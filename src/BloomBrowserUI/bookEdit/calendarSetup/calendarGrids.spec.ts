import { describe, expect, it } from "vitest";
import { buildCalendarGridTable } from "./buildCalendarGridTable";
import {
    addBookLanguageEditablesToGrid,
    getCalendarGrids,
    layOutGridIfNeeded,
    resolveGridLayout,
    writeSavedWeekdayNamesIntoGrid,
} from "./calendarGrids";
import {
    ICalendarMonthLayout,
    kCalendarFirstDayAttribute,
    kCalendarMonthAttribute,
    kCalendarNeighborDaysAttribute,
    kCalendarWeekdayAttribute,
    kCalendarYearAttribute,
} from "./layOutCalendarMonthPage";

const kSundayFirst = 0;
const kMondayFirst = 1;

/** March 2027, the month these tests build unless they say otherwise. */
const kMarch2027: ICalendarMonthLayout = {
    year: 2027,
    month: 2,
    firstDayOfWeek: kSundayFirst,
    showNeighborDays: false,
};

/** A page element holding one grid, built the way a canvas grid is. */
function makePageWithBuiltGrid(layout: ICalendarMonthLayout): {
    page: HTMLElement;
    grid: HTMLElement;
} {
    const page = document.createElement("div");
    page.className = "bloom-page";
    const grid = document.createElement("div");
    grid.className = "bloom-table";
    page.appendChild(grid);
    buildCalendarGridTable(grid, layout);
    return { page, grid };
}

/** A page holding a table that carries only the month, as a Wall Calendar page does. */
function makeWallCalendarPage(options: {
    month: number;
    year?: string;
    firstDayOfWeek?: string;
}): { page: HTMLElement; grid: HTMLElement } {
    const page = document.createElement("div");
    page.className = "bloom-page";
    const grid = document.createElement("div");
    grid.className = "bloom-table";
    grid.setAttribute(kCalendarMonthAttribute, String(options.month));
    page.appendChild(grid);
    if (options.year !== undefined) {
        const year = document.createElement("div");
        year.setAttribute("data-book", "calendarYear");
        year.textContent = options.year;
        page.appendChild(year);
    }
    if (options.firstDayOfWeek !== undefined) {
        const firstDay = document.createElement("div");
        firstDay.setAttribute("data-book", "calendarFirstDayOfWeek");
        firstDay.textContent = options.firstDayOfWeek;
        page.appendChild(firstDay);
    }
    return { page, grid };
}

describe("getCalendarGrids", () => {
    it("finds every table that says which month it is for", () => {
        const container = document.createElement("div");
        const first = makePageWithBuiltGrid(kMarch2027);
        const second = makePageWithBuiltGrid({ ...kMarch2027, month: 5 });
        const plainTable = document.createElement("div");
        plainTable.className = "bloom-table";
        container.append(first.page, second.page, plainTable);

        const grids = getCalendarGrids(container);

        expect(grids).toHaveLength(2);
        expect(grids).toContain(first.grid);
        expect(grids).toContain(second.grid);
        expect(grids).not.toContain(plainTable);
    });

    it("finds a grid when the container holds the page", () => {
        const body = document.createElement("div");
        const { page, grid } = makeWallCalendarPage({ month: 4 });
        body.appendChild(page);

        expect(getCalendarGrids(body)).toEqual([grid]);
    });

    it("ignores a month written on the page element", () => {
        const page = document.createElement("div");
        page.className = "bloom-page";
        page.setAttribute(kCalendarMonthAttribute, "4");
        const table = document.createElement("div");
        table.className = "bloom-table";
        page.appendChild(table);

        expect(getCalendarGrids(page)).toEqual([]);
        expect(table.hasAttribute(kCalendarMonthAttribute)).toBe(false);
    });
});

describe("resolveGridLayout", () => {
    it("takes the year and the first day of the week from the page when the table has none", () => {
        const { page, grid } = makeWallCalendarPage({
            month: 2,
            year: "2027",
            firstDayOfWeek: "1",
        });

        expect(resolveGridLayout(grid, page)).toEqual({
            year: 2027,
            month: 2,
            firstDayOfWeek: kMondayFirst,
            showNeighborDays: false,
        });
    });

    it("prefers what the table itself says over what the page says", () => {
        const { page, grid } = makeWallCalendarPage({
            month: 2,
            year: "2027",
            firstDayOfWeek: "1",
        });
        grid.setAttribute(kCalendarYearAttribute, "2030");
        grid.setAttribute(kCalendarFirstDayAttribute, "0");
        grid.setAttribute(kCalendarNeighborDaysAttribute, "true");

        expect(resolveGridLayout(grid, page)).toEqual({
            year: 2030,
            month: 2,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: true,
        });
    });

    it("says nothing when the book has no year yet", () => {
        const { page, grid } = makeWallCalendarPage({
            month: 2,
            firstDayOfWeek: "0",
        });

        expect(resolveGridLayout(grid, page)).toBeUndefined();
    });

    it("starts the week on Sunday when the book has not said where it starts", () => {
        // One pick of a year is enough to configure a new calendar, so a book that has a year
        // and nothing else is laid out, Sunday first.
        const { page, grid } = makeWallCalendarPage({
            month: 2,
            year: "2027",
        });

        expect(resolveGridLayout(grid, page)).toEqual({
            year: 2027,
            month: 2,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: false,
        });
    });

    it("says nothing when the book has neither a year nor a first day of the week", () => {
        const { page, grid } = makeWallCalendarPage({ month: 2 });

        expect(resolveGridLayout(grid, page)).toBeUndefined();
    });
});

describe("layOutGridIfNeeded", () => {
    it("does nothing to a grid that is already laid out for what the book asks", () => {
        const { page, grid } = makePageWithBuiltGrid(kMarch2027);

        expect(layOutGridIfNeeded(grid, page)).toBe(false);
    });

    it("lays a grid out again when the year changes", () => {
        const { page, grid } = makePageWithBuiltGrid(kMarch2027);
        // Sanity check: nothing to do until something changes.
        expect(layOutGridIfNeeded(grid, page)).toBe(false);
        grid.setAttribute(kCalendarYearAttribute, "2028");

        expect(layOutGridIfNeeded(grid, page)).toBe(true);
        expect(layOutGridIfNeeded(grid, page)).toBe(false);
    });

    it("does nothing to a grid whose book has not said what year it is", () => {
        const { page, grid } = makeWallCalendarPage({ month: 2 });

        expect(layOutGridIfNeeded(grid, page)).toBe(false);
    });
});

/** The editable of a cell in the book's own language, whose text the user sees. */
function getBookLanguageEditable(cell: Element): HTMLElement | null {
    return cell.querySelector<HTMLElement>(
        ".bloom-editable.bloom-content1.bloom-visibility-code-on",
    );
}

/** The weekday header cell of one day of the week, whatever order the cells are in. */
function getWeekdayCell(grid: HTMLElement, weekday: number): HTMLElement {
    const cell = grid.querySelector<HTMLElement>(
        `[${kCalendarWeekdayAttribute}='${weekday}']`,
    );
    if (!cell) throw new Error(`no header cell for weekday ${weekday}`);
    return cell;
}

describe("addBookLanguageEditablesToGrid", () => {
    it("gives every translationGroup a visible editable in the book's language", () => {
        const { grid } = makePageWithBuiltGrid(kMarch2027);
        // Sanity check: the builder leaves the grid with nothing the user can see or type in.
        expect(
            grid.querySelectorAll(".bloom-editable.bloom-visibility-code-on")
                .length,
        ).toBe(0);

        addBookLanguageEditablesToGrid(grid, "xkal", []);

        const groups = grid.querySelectorAll(".bloom-translationGroup");
        expect(groups.length).toBe(7 + 42);
        groups.forEach((group) => {
            const editable = getBookLanguageEditable(group);
            expect(editable).not.toBeNull();
            expect(editable!.getAttribute("lang")).toBe("xkal");
            // Left for tableEditing's wiring, which is what makes it typable.
            expect(editable!.hasAttribute("contenteditable")).toBe(false);
        });
    });

    it("leaves a day note empty and gives a weekday no name it does not have", () => {
        const { grid } = makePageWithBuiltGrid(kMarch2027);

        addBookLanguageEditablesToGrid(grid, "xkal", []);

        expect(
            getBookLanguageEditable(getWeekdayCell(grid, 0))!.textContent,
        ).toBe("");
        const dayCell = grid.querySelector(".calendarDayCell")!;
        expect(getBookLanguageEditable(dayCell)!.textContent).toBe("");
    });

    it("uses the seed name when the book's language is a seed language", () => {
        const { grid } = makePageWithBuiltGrid(kMarch2027);

        addBookLanguageEditablesToGrid(grid, "fr", []);

        const editable = getBookLanguageEditable(getWeekdayCell(grid, 1))!;
        expect(editable.textContent).toBe("lun.");
        // The seed editable itself is the one marked, rather than a second French one added.
        expect(
            getWeekdayCell(grid, 1).querySelectorAll(
                ".bloom-editable[lang='fr']",
            ).length,
        ).toBe(1);
    });

    it("prefers the name the collection has saved for the day", () => {
        const { grid } = makePageWithBuiltGrid(kMarch2027);
        const savedNames = ["", "Lundi de nous", "", "", "", "", ""];

        addBookLanguageEditablesToGrid(grid, "fr", savedNames);

        expect(
            getBookLanguageEditable(getWeekdayCell(grid, 1))!.textContent,
        ).toBe("Lundi de nous");
        // A day the collection has no name for keeps the seed name.
        expect(
            getBookLanguageEditable(getWeekdayCell(grid, 2))!.textContent,
        ).toBe("mar.");
    });
});

describe("writeSavedWeekdayNamesIntoGrid", () => {
    it("writes the days the collection has names for, and only those", () => {
        const { grid } = makePageWithBuiltGrid(kMarch2027);
        addBookLanguageEditablesToGrid(grid, "fr", []);
        // Sanity check: the seed name is what is there to start with.
        expect(
            getBookLanguageEditable(getWeekdayCell(grid, 3))!.textContent,
        ).toBe("mer.");

        writeSavedWeekdayNamesIntoGrid(grid, [
            "",
            "",
            "",
            "Mercredi",
            "",
            "",
            "",
        ]);

        expect(
            getBookLanguageEditable(getWeekdayCell(grid, 3))!.textContent,
        ).toBe("Mercredi");
        expect(
            getBookLanguageEditable(getWeekdayCell(grid, 4))!.textContent,
        ).toBe("jeu.");
    });
});
