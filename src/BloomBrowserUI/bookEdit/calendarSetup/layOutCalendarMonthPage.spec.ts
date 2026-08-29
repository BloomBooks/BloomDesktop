import { describe, expect, it } from "vitest";
import {
    calendarLayoutSignature,
    calendarTableNeedsLayout,
    computeEdgesH,
    computeEdgesV,
    dayNumbersForCells,
    dayRowCountForMonth,
    daysInMonth,
    getShowNeighborDays,
    ICalendarMonthLayout,
    kCalendarColumnCount,
    kCalendarDayRowCount,
    kCalendarLaidOutAttribute,
    kCalendarNeighborDaysAttribute,
    kCalendarWeekdayAttribute,
    layOutCalendarMonthPage,
    leadingBlankCellCount,
} from "./layOutCalendarMonthPage";

const kSundayFirst = 0;
const kMondayFirst = 1;
const kDayCellCount = kCalendarColumnCount * kCalendarDayRowCount;

// The weekday names the template ships, Sunday first, in English.
const kEnglishWeekdayNames = [
    "Sun",
    "Mon",
    "Tues",
    "Wed",
    "Thur",
    "Fri",
    "Sat",
];

/**
 * The bloom-table of a month-grid page as the Wall Calendar template ships it: the weekday
 * header row in Sunday-first order, then 42 day cells with no day numbers and no
 * calendarUnusedDay classes. The month name and the year are page content above the table, so
 * they are not in it at all. This mirrors the markup of the pug's `calendarDayCell` and
 * `calendarSeededField` mixins closely enough for the layout code, which only looks at the
 * cells, the day-number elements, and the table's attributes.
 */
function makeUnconfiguredGridTable(month: number): HTMLElement {
    const page = document.createElement("div");
    page.className = "bloom-page calendarMonthGrid";

    const weekdayCells = kEnglishWeekdayNames.map(
        (name) =>
            `<div class="bloom-cell" data-content-type="text">` +
            `<div class="bloom-translationGroup">` +
            `<div class="bloom-editable CalendarDayOfWeek-style bloom-content1" lang="xyz"><p></p></div>` +
            `<div class="bloom-editable CalendarDayOfWeek-style" lang="en"><p>${name}</p></div>` +
            `</div></div>`,
    );
    const dayCells = new Array(kDayCellCount)
        .fill(0)
        .map(
            () =>
                `<div class="bloom-cell calendarDayCell" data-content-type="text" data-pad="0">` +
                `<div class="calendarDayCellContents">` +
                `<div class="calendarDayNumber CalendarDayNumber-style bloom-styleable" tabindex="0"></div>` +
                `<div class="bloom-translationGroup">` +
                `<div class="bloom-editable CalendarDayNote-style bloom-content1" lang="xyz"><p></p></div>` +
                `</div></div></div>`,
        );

    page.innerHTML =
        `<div class="split-pane-component-inner">` +
        `<div class="bloom-table" data-calendar-month="${month}" data-column-widths="fill,fill,fill,fill,fill,fill,fill">` +
        weekdayCells.join("") +
        dayCells.join("") +
        `</div></div>`;
    return page.querySelector<HTMLElement>(".bloom-table")!;
}

function getDayCells(table: HTMLElement): HTMLElement[] {
    return Array.from(table.querySelectorAll<HTMLElement>(".calendarDayCell"));
}

/** The day number shown in each cell, with an empty string for a cell that shows none. */
function getShownDayNumbers(table: HTMLElement): string[] {
    return getDayCells(table).map(
        (cell) =>
            cell.querySelector<HTMLElement>(".calendarDayNumber")
                ?.textContent ?? "",
    );
}

/** The English weekday names in the order the header row now shows them. */
function getShownWeekdayNames(table: HTMLElement): string[] {
    return Array.from(
        table.querySelectorAll<HTMLElement>(`[${kCalendarWeekdayAttribute}]`),
    ).map((cell) => cell.querySelector("[lang='en'] p")?.textContent ?? "");
}

function getEdges(table: HTMLElement, which: "v" | "h") {
    return JSON.parse(table.getAttribute(`data-edges-${which}`)!);
}

/**
 * Is this edge entry a drawn line, rather than an explicit "no line"? An entry names its
 * painting side (west/east or north/south); the line is drawn when either side has one.
 */
function isLine(entry: {
    west?: { weight: number; style: string };
    east?: { weight: number; style: string };
    north?: { weight: number; style: string };
    south?: { weight: number; style: string };
}): boolean {
    return [entry.west, entry.east, entry.north, entry.south].some(
        (side) => side?.weight === 1 && side?.style === "solid",
    );
}

describe("daysInMonth", () => {
    it("knows the ordinary month lengths", () => {
        expect(daysInMonth(2027, 0)).toBe(31);
        expect(daysInMonth(2027, 3)).toBe(30);
        expect(daysInMonth(2027, 1)).toBe(28);
    });
    it("knows February of a leap year has 29 days", () => {
        expect(daysInMonth(2028, 1)).toBe(29);
        expect(daysInMonth(2100, 1)).toBe(28); // a century that is not a leap year
    });
});

describe("leadingBlankCellCount", () => {
    it("is the weekday of the first of the month when weeks start on Sunday", () => {
        // 1 January 2027 is a Friday.
        expect(new Date(2027, 0, 1).getDay()).toBe(5);
        expect(
            leadingBlankCellCount({
                year: 2027,
                month: 0,
                firstDayOfWeek: kSundayFirst,
                showNeighborDays: false,
            }),
        ).toBe(5);
    });
    it("shifts when the week starts on some other day", () => {
        expect(
            leadingBlankCellCount({
                year: 2027,
                month: 0,
                firstDayOfWeek: kMondayFirst,
                showNeighborDays: false,
            }),
        ).toBe(4);
        // 1 February 2027 is a Monday, so a Monday-first week has no leading blanks.
        expect(
            leadingBlankCellCount({
                year: 2027,
                month: 1,
                firstDayOfWeek: kMondayFirst,
                showNeighborDays: false,
            }),
        ).toBe(0);
    });
});

describe("layOutCalendarMonthPage for January 2027", () => {
    // This is the month the template's 'extra' Calendar Month Grid page is built for, so what
    // this produces is what that page's pug block produces.
    const layout: ICalendarMonthLayout = {
        year: 2027,
        month: 0,
        firstDayOfWeek: kSundayFirst,
        showNeighborDays: false,
    };
    const table = makeUnconfiguredGridTable(0);
    layOutCalendarMonthPage(table, layout);

    it("leaves the first five cells empty and starts the month on the Friday", () => {
        const shown = getShownDayNumbers(table);
        expect(shown.slice(0, 5)).toEqual(["", "", "", "", ""]);
        expect(shown[5]).toBe("1");
        expect(shown[35]).toBe("31");
    });

    it("removes the day-number element from the cells that hold no day", () => {
        const cells = getDayCells(table);
        expect(cells[0].querySelector(".calendarDayNumber")).toBeNull();
        expect(cells[36].querySelector(".calendarDayNumber")).toBeNull();
        expect(cells[5].querySelector(".calendarDayNumber")).not.toBeNull();
    });

    it("marks the cells before day 1 and past the end of the month as unused", () => {
        const unused = getDayCells(table).map((cell) =>
            cell.classList.contains("calendarUnusedDay"),
        );
        expect(unused.slice(0, 5)).toEqual([true, true, true, true, true]);
        expect(unused[5]).toBe(false);
        expect(unused[35]).toBe(false);
        expect(unused.slice(36)).toEqual([true, true, true, true, true, true]);
    });

    it("keeps the note field of every cell", () => {
        expect(table.querySelectorAll(".CalendarDayNote-style").length).toBe(
            kDayCellCount,
        );
    });

    it("draws the full left and right perimeter, with interior lines only around days", () => {
        const edgesV = getEdges(table, "v");
        expect(edgesV.length).toBe(1 + kCalendarDayRowCount);
        expect(edgesV[0].every((e) => !isLine(e))).toBe(true); // the weekday row
        // The first day row holds only days 1 and 2, in the last two columns: the
        // perimeter is drawn at both ends, and interior lines only touch the days.
        expect(edgesV[1].map(isLine)).toEqual([
            true,
            false,
            false,
            false,
            false,
            true,
            true,
            true,
        ]);
        for (let row = 2; row <= 5; row++) {
            expect(edgesV[row].every((e) => isLine(e))).toBe(true);
        }
        // The last row holds only day 31, in the first column.
        expect(edgesV[6].map(isLine)).toEqual([
            true,
            true,
            false,
            false,
            false,
            false,
            false,
            true,
        ]);
    });

    it("draws the full top and bottom perimeter, with interior lines only around days", () => {
        const edgesH = getEdges(table, "h");
        expect(edgesH.length).toBe(2 + kCalendarDayRowCount);
        // Nothing is drawn above the weekday row.
        expect(edgesH[0].every((e) => !isLine(e))).toBe(true);
        // The top and the bottom of the day area are the perimeter, so every column
        // gets a line even where the cell below or above holds no day.
        expect(edgesH[1].every((e) => isLine(e))).toBe(true);
        for (let boundary = 2; boundary <= 6; boundary++) {
            expect(edgesH[boundary].every((e) => isLine(e))).toBe(true);
        }
        expect(edgesH[7].every((e) => isLine(e))).toBe(true);
    });

    it("writes the grid tracks and records what it laid the page out for", () => {
        expect(table.getAttribute("data-row-heights")).toBe(
            "hug,fill,fill,fill,fill,fill,fill",
        );
        expect(table.getAttribute("style")).toContain(
            "grid-template-rows: minmax(20px,max-content) minmax(20px,1fr)",
        );
        expect(table.getAttribute(kCalendarLaidOutAttribute)).toBe("2027,0,0");
    });
});

describe("layOutCalendarMonthPage for other months", () => {
    it("gives February 2028 twenty-nine days", () => {
        const table = makeUnconfiguredGridTable(1);
        layOutCalendarMonthPage(table, {
            year: 2028,
            month: 1,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: false,
        });
        const shown = getShownDayNumbers(table);
        // 1 February 2028 is a Tuesday.
        expect(new Date(2028, 1, 1).getDay()).toBe(2);
        expect(shown.slice(0, 2)).toEqual(["", ""]);
        expect(shown[2]).toBe("1");
        expect(shown[30]).toBe("29");
        expect(shown[31]).toBe("");
        expect(
            getDayCells(table)[31].classList.contains("calendarUnusedDay"),
        ).toBe(true);
    });

    it("fills the grid from the first cell when the month starts on the first day of the week", () => {
        // 1 February 2027 is a Monday, and the week here starts on Monday.
        const table = makeUnconfiguredGridTable(1);
        layOutCalendarMonthPage(table, {
            year: 2027,
            month: 1,
            firstDayOfWeek: kMondayFirst,
            showNeighborDays: false,
        });
        const shown = getShownDayNumbers(table);
        expect(shown[0]).toBe("1");
        expect(shown[27]).toBe("28");
        expect(shown[28]).toBe("");
        // With no leading blanks, the whole top row is boxed.
        expect(getEdges(table, "v")[1].every(isLine)).toBe(true);
    });

    it("fits a 31-day month that starts in the last column into six rows", () => {
        // 1 May 2027 is a Saturday, the last column of a Sunday-first week: 6 blanks + 31 days.
        expect(new Date(2027, 4, 1).getDay()).toBe(6);
        const table = makeUnconfiguredGridTable(4);
        layOutCalendarMonthPage(table, {
            year: 2027,
            month: 4,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: false,
        });
        const shown = getShownDayNumbers(table);
        expect(shown[6]).toBe("1");
        expect(shown[36]).toBe("31");
        expect(shown[37]).toBe("");
        // The last row holds days 30 and 31: interior lines stop after them, and the
        // right end of the perimeter closes the row.
        expect(getEdges(table, "v")[6].map(isLine)).toEqual([
            true,
            true,
            true,
            false,
            false,
            false,
            false,
            true,
        ]);
        // The bottom of the day area is the perimeter, so it runs the whole width.
        expect(getEdges(table, "h")[7].every(isLine)).toBe(true);
    });
});

describe("dayRowCountForMonth", () => {
    it("gives a month only the rows it needs", () => {
        // January 2027, Sunday first: 5 leading blanks + 31 days = 36 cells.
        expect(
            dayRowCountForMonth({
                year: 2027,
                month: 0,
                firstDayOfWeek: kSundayFirst,
                showNeighborDays: false,
            }),
        ).toBe(6);
        // March 2027 starts on a Monday: 1 leading blank + 31 days = 32 cells.
        expect(
            dayRowCountForMonth({
                year: 2027,
                month: 2,
                firstDayOfWeek: kSundayFirst,
                showNeighborDays: false,
            }),
        ).toBe(5);
        // February 2027 starts on the first day of a Monday-first week: 28 cells exactly.
        expect(
            dayRowCountForMonth({
                year: 2027,
                month: 1,
                firstDayOfWeek: kMondayFirst,
                showNeighborDays: false,
            }),
        ).toBe(4);
    });
});

describe("a month that does not need all six rows", () => {
    // March 2027, Sunday first: 1 leading blank + 31 days, so five rows hold it.
    const layout: ICalendarMonthLayout = {
        year: 2027,
        month: 2,
        firstDayOfWeek: kSundayFirst,
        showNeighborDays: false,
    };
    const table = makeUnconfiguredGridTable(2);
    layOutCalendarMonthPage(table, layout);

    it("hides the cells of the rows past the last one the month uses", () => {
        const hidden = getDayCells(table).map(
            (cell) => cell.style.display === "none",
        );
        expect(hidden.slice(0, 35).every((h) => !h)).toBe(true);
        expect(hidden.slice(35)).toEqual([
            true,
            true,
            true,
            true,
            true,
            true,
            true,
        ]);
    });

    it("declares the weekday row and only the five day rows the month shows", () => {
        expect(table.getAttribute("data-row-heights")).toBe(
            "hug,fill,fill,fill,fill,fill",
        );
        // The five day rows are all 'fill', so they share the height the hidden sixth
        // row would have taken.
        expect(table.getAttribute("style")).toContain(
            "grid-template-rows: minmax(20px,max-content) minmax(20px,1fr) minmax(20px,1fr) minmax(20px,1fr) minmax(20px,1fr) minmax(20px,1fr);",
        );
    });

    it("sizes the edge arrays to the shown rows", () => {
        expect(getEdges(table, "v").length).toBe(1 + 5);
        expect(getEdges(table, "h").length).toBe(2 + 5);
        // The bottom of the last shown row is the perimeter, so it runs the whole
        // width even though days 28 to 31 fill only its first four cells.
        expect(getEdges(table, "h")[6].every(isLine)).toBe(true);
        // The last row's interior vertical lines stop after day 31 (cell 31), and
        // the perimeter closes the row at the right.
        expect(getEdges(table, "v")[5].map(isLine)).toEqual([
            true,
            true,
            true,
            true,
            true,
            false,
            false,
            true,
        ]);
    });

    it("brings the sixth row back when a later year needs it", () => {
        // March 2031 starts on a Saturday: 6 leading blanks + 31 days = 37 cells.
        expect(new Date(2031, 2, 1).getDay()).toBe(6);
        layOutCalendarMonthPage(table, {
            year: 2031,
            month: 2,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: false,
        });
        const cells = getDayCells(table);
        expect(cells.every((cell) => cell.style.display !== "none")).toBe(true);
        expect(getShownDayNumbers(table)[36]).toBe("31");
        expect(table.getAttribute("data-row-heights")).toBe(
            "hug,fill,fill,fill,fill,fill,fill",
        );
    });
});

describe("weekday header rotation", () => {
    it("puts the chosen first day of the week first, carrying a typed name with it", () => {
        const table = makeUnconfiguredGridTable(0);
        // The user has typed their own name for Monday.
        const mondayCell = Array.from(
            table.querySelectorAll<HTMLElement>(".bloom-cell"),
        ).filter((cell) => cell.querySelector(".CalendarDayOfWeek-style"))[1];
        mondayCell.querySelector<HTMLElement>(
            ".bloom-content1 p",
        )!.textContent = "Mande";

        layOutCalendarMonthPage(table, {
            year: 2027,
            month: 0,
            firstDayOfWeek: kMondayFirst,
            showNeighborDays: false,
        });

        expect(getShownWeekdayNames(table)).toEqual([
            "Mon",
            "Tues",
            "Wed",
            "Thur",
            "Fri",
            "Sat",
            "Sun",
        ]);
        const firstHeaderCell = table.querySelector<HTMLElement>(
            `[${kCalendarWeekdayAttribute}]`,
        )!;
        expect(firstHeaderCell.getAttribute(kCalendarWeekdayAttribute)).toBe(
            "1",
        );
        expect(
            firstHeaderCell.querySelector<HTMLElement>(".bloom-content1")!
                .textContent,
        ).toBe("Mande");
    });

    it("rotates back correctly when the first day of the week changes again", () => {
        const table = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(table, {
            year: 2027,
            month: 0,
            firstDayOfWeek: kMondayFirst,
            showNeighborDays: false,
        });
        // Sanity check: it really did rotate before we ask it to rotate back.
        expect(getShownWeekdayNames(table)[0]).toBe("Mon");
        layOutCalendarMonthPage(table, {
            year: 2027,
            month: 0,
            firstDayOfWeek: 3,
            showNeighborDays: false,
        });
        expect(getShownWeekdayNames(table)).toEqual([
            "Wed",
            "Thur",
            "Fri",
            "Sat",
            "Sun",
            "Mon",
            "Tues",
        ]);
    });
});

describe("laying a page out again", () => {
    it("recomputes the day numbers and unused cells for a new year, keeping typed notes", () => {
        const table = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(table, {
            year: 2027,
            month: 0,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: false,
        });
        // The user has written a note in the cell holding 1 January 2027.
        const noteCell = getDayCells(table)[5];
        noteCell.querySelector<HTMLElement>(
            ".CalendarDayNote-style p",
        )!.textContent = "New Year";
        // Sanity check: the cell we are about to move the month off really does hold day 1.
        expect(
            noteCell.querySelector<HTMLElement>(".calendarDayNumber")!
                .textContent,
        ).toBe("1");

        // 1 January 2028 is a Saturday.
        layOutCalendarMonthPage(table, {
            year: 2028,
            month: 0,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: false,
        });

        const shown = getShownDayNumbers(table);
        expect(shown[5]).toBe("");
        expect(shown[6]).toBe("1");
        expect(shown[36]).toBe("31");
        expect(
            getDayCells(table)[37].classList.contains("calendarUnusedDay"),
        ).toBe(true);
        // A cell that was inside the month and now is not stops being unused when it is
        // inside again, and its note stays where the user put it either way.
        expect(
            noteCell.querySelector(".CalendarDayNote-style p")!.textContent,
        ).toBe("New Year");
        expect(table.getAttribute(kCalendarLaidOutAttribute)).toBe("2028,0,0");
    });

    it("clears calendarUnusedDay from a cell that the new month reaches", () => {
        const table = makeUnconfiguredGridTable(1);
        // February 2027 is 28 days starting on a Monday: cell 29 onwards is unused.
        layOutCalendarMonthPage(table, {
            year: 2027,
            month: 1,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: false,
        });
        expect(
            getDayCells(table)[30].classList.contains("calendarUnusedDay"),
        ).toBe(true);
        // February 2028 is 29 days starting on a Tuesday, so cell 30 holds day 29.
        layOutCalendarMonthPage(table, {
            year: 2028,
            month: 1,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: false,
        });
        expect(
            getDayCells(table)[30].classList.contains("calendarUnusedDay"),
        ).toBe(false);
    });
});

describe("the edge arrays", () => {
    const layout: ICalendarMonthLayout = {
        year: 2027,
        month: 0,
        firstDayOfWeek: kSundayFirst,
        showNeighborDays: false,
    };
    it("have the shapes bloom-table expects", () => {
        const edgesV = computeEdgesV(layout);
        expect(edgesV.length).toBe(1 + kCalendarDayRowCount);
        edgesV.forEach((row) =>
            expect(row.length).toBe(kCalendarColumnCount + 1),
        );
        const edgesH = computeEdgesH(layout);
        expect(edgesH.length).toBe(2 + kCalendarDayRowCount);
        edgesH.forEach((boundary) =>
            expect(boundary.length).toBe(kCalendarColumnCount),
        );
    });
    it("gives every one of the forty-two cells a day or a blank", () => {
        expect(dayNumbersForCells(layout).length).toBe(kDayCellCount);
        expect(dayNumbersForCells(layout).filter((d) => d > 0).length).toBe(31);
    });
});

describe("calendarTableNeedsLayout", () => {
    // March 2027, Sunday first, needs five rows.
    const layout: ICalendarMonthLayout = {
        year: 2027,
        month: 2,
        firstDayOfWeek: kSundayFirst,
        showNeighborDays: false,
    };
    it("is false right after a layout, even when the user has resized a row", () => {
        const table = makeUnconfiguredGridTable(2);
        layOutCalendarMonthPage(table, layout);
        expect(calendarTableNeedsLayout(table, layout)).toBe(false);
        // A resize replaces one token with a length, keeping the token count.
        table.setAttribute(
            "data-row-heights",
            table.getAttribute("data-row-heights")!.replace(/fill$/, "31.2mm"),
        );
        expect(calendarTableNeedsLayout(table, layout)).toBe(false);
    });
    it("is true when the signature differs or the declared rows do not fit the month", () => {
        const table = makeUnconfiguredGridTable(2);
        layOutCalendarMonthPage(table, layout);
        expect(calendarTableNeedsLayout(table, { ...layout, year: 2028 })).toBe(
            true,
        );
        // A build without the hidden-rows behavior declared all six rows.
        table.setAttribute(
            "data-row-heights",
            "hug,hug," + "fill,".repeat(5) + "fill",
        );
        expect(calendarTableNeedsLayout(table, layout)).toBe(true);
    });
});

describe("calendarLayoutSignature", () => {
    it("changes with the year and with the first day of the week, not with the month", () => {
        const signature = calendarLayoutSignature({
            year: 2027,
            month: 0,
            firstDayOfWeek: 0,
            showNeighborDays: false,
        });
        expect(
            calendarLayoutSignature({
                year: 2027,
                month: 5,
                firstDayOfWeek: 0,
                showNeighborDays: false,
            }),
        ).toBe(signature);
        expect(
            calendarLayoutSignature({
                year: 2028,
                month: 0,
                firstDayOfWeek: 0,
                showNeighborDays: false,
            }),
        ).not.toBe(signature);
        expect(
            calendarLayoutSignature({
                year: 2027,
                month: 0,
                firstDayOfWeek: 1,
                showNeighborDays: false,
            }),
        ).not.toBe(signature);
    });
});

describe("getShowNeighborDays", () => {
    it("is true only for a table that says 'true'", () => {
        const table = makeUnconfiguredGridTable(0);
        expect(getShowNeighborDays(table)).toBe(false);
        table.setAttribute(kCalendarNeighborDaysAttribute, "true");
        expect(getShowNeighborDays(table)).toBe(true);
        table.setAttribute(kCalendarNeighborDaysAttribute, "false");
        expect(getShowNeighborDays(table)).toBe(false);
    });
});

describe("the dates of the neighboring months", () => {
    // January 2027, Sunday first: five leading cells, and 42 shown cells because the month
    // needs all six rows, so there are six trailing cells as well.
    const januaryLayout: ICalendarMonthLayout = {
        year: 2027,
        month: 0,
        firstDayOfWeek: kSundayFirst,
        showNeighborDays: true,
    };

    it("fills the leading cells with the last days of the previous month", () => {
        // Sanity check the fixture: December 2026 has 31 days, and January 2027 starts on
        // a Friday, so five cells come before day 1.
        expect(daysInMonth(2026, 11)).toBe(31);
        expect(leadingBlankCellCount(januaryLayout)).toBe(5);

        const table = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(table, januaryLayout);
        const shown = getShownDayNumbers(table);
        expect(shown.slice(0, 5)).toEqual(["27", "28", "29", "30", "31"]);
        expect(shown[5]).toBe("1");
    });

    it("crosses the year boundary to reach December of the previous year", () => {
        // February 2027 needs no leading cells at all in a Monday-first week, so use a
        // January whose previous month is a 30-day one to prove nothing is hard-coded:
        // January 2028 starts on a Saturday, and December 2027 has 31 days.
        expect(new Date(2028, 0, 1).getDay()).toBe(6);
        expect(daysInMonth(2027, 11)).toBe(31);
        const table = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(table, {
            year: 2028,
            month: 0,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: true,
        });
        const shown = getShownDayNumbers(table);
        expect(shown.slice(0, 6)).toEqual(["26", "27", "28", "29", "30", "31"]);
        expect(shown[6]).toBe("1");
    });

    it("fills the trailing cells of the shown rows with the first days of the next month", () => {
        const table = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(table, januaryLayout);
        const shown = getShownDayNumbers(table);
        // Sanity check: day 31 of January really is in cell 35.
        expect(shown[35]).toBe("31");
        expect(shown.slice(36)).toEqual(["1", "2", "3", "4", "5", "6"]);
    });

    it("marks a neighbor date as such and not as an unused cell", () => {
        const table = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(table, januaryLayout);
        const cells = getDayCells(table);
        [0, 4, 36, 41].forEach((index) => {
            expect(cells[index].classList.contains("calendarNeighborDay")).toBe(
                true,
            );
            expect(cells[index].classList.contains("calendarUnusedDay")).toBe(
                false,
            );
        });
        // A cell of this month is neither.
        expect(cells[5].classList.contains("calendarNeighborDay")).toBe(false);
        expect(cells[5].classList.contains("calendarUnusedDay")).toBe(false);
    });

    it("leaves the rows the month does not show hidden and empty", () => {
        // March 2027, Sunday first, needs five rows: one leading cell, then days 1 to 31,
        // then three cells of April, then a hidden sixth row.
        const layout: ICalendarMonthLayout = {
            year: 2027,
            month: 2,
            firstDayOfWeek: kSundayFirst,
            showNeighborDays: true,
        };
        expect(dayRowCountForMonth(layout)).toBe(5);
        expect(daysInMonth(2027, 1)).toBe(28); // February 2027, the previous month

        const table = makeUnconfiguredGridTable(2);
        layOutCalendarMonthPage(table, layout);
        const shown = getShownDayNumbers(table);
        expect(shown[0]).toBe("28");
        expect(shown[1]).toBe("1");
        expect(shown[31]).toBe("31");
        expect(shown.slice(32, 35)).toEqual(["1", "2", "3"]);
        expect(shown.slice(35)).toEqual(["", "", "", "", "", "", ""]);

        const cells = getDayCells(table);
        expect(
            cells.slice(35).every((cell) => cell.style.display === "none"),
        ).toBe(true);
        expect(
            cells
                .slice(35)
                .every(
                    (cell) => !cell.classList.contains("calendarNeighborDay"),
                ),
        ).toBe(true);
        expect(table.getAttribute("data-row-heights")).toBe(
            "hug,fill,fill,fill,fill,fill",
        );
    });

    it("draws every boundary of the day area, because every shown cell holds a date", () => {
        const table = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(table, januaryLayout);
        const edgesV = getEdges(table, "v");
        expect(edgesV[0].every((e) => !isLine(e))).toBe(true); // the weekday row
        for (let row = 1; row < edgesV.length; row++) {
            expect(edgesV[row].every((e) => isLine(e))).toBe(true);
        }
        const edgesH = getEdges(table, "h");
        expect(edgesH[0].every((e) => !isLine(e))).toBe(true); // above the weekday row
        for (let boundary = 1; boundary < edgesH.length; boundary++) {
            expect(edgesH[boundary].every((e) => isLine(e))).toBe(true);
        }
    });

    it("puts the option into the signature, so a table laid out without it is laid out again", () => {
        const withNeighbors = calendarLayoutSignature(januaryLayout);
        const without = calendarLayoutSignature({
            ...januaryLayout,
            showNeighborDays: false,
        });
        expect(withNeighbors).toBe("2027,0,1");
        expect(without).toBe("2027,0,0");
        const table = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(table, januaryLayout);
        expect(calendarTableNeedsLayout(table, januaryLayout)).toBe(false);
        expect(
            calendarTableNeedsLayout(table, {
                ...januaryLayout,
                showNeighborDays: false,
            }),
        ).toBe(true);
    });

    it("gives back exactly the option-off layout when the option is turned off again", () => {
        const offLayout: ICalendarMonthLayout = {
            ...januaryLayout,
            showNeighborDays: false,
        };
        const first = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(first, offLayout);
        const expectedNumbers = getShownDayNumbers(first);
        const expectedEdgesV = first.getAttribute("data-edges-v");
        const expectedEdgesH = first.getAttribute("data-edges-h");
        // Sanity check: the option-off layout really does leave the first cells empty.
        expect(expectedNumbers[0]).toBe("");

        const second = makeUnconfiguredGridTable(0);
        layOutCalendarMonthPage(second, januaryLayout);
        // Sanity check: the option really was on before we turn it off.
        expect(getShownDayNumbers(second)[0]).toBe("27");
        layOutCalendarMonthPage(second, offLayout);

        expect(getShownDayNumbers(second)).toEqual(expectedNumbers);
        expect(second.getAttribute("data-edges-v")).toBe(expectedEdgesV);
        expect(second.getAttribute("data-edges-h")).toBe(expectedEdgesH);
        expect(
            getDayCells(second).some((cell) =>
                cell.classList.contains("calendarNeighborDay"),
            ),
        ).toBe(false);
        expect(
            getDayCells(second)[0].classList.contains("calendarUnusedDay"),
        ).toBe(true);
    });
});
