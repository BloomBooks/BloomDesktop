// Turns one unconfigured (or previously configured) month-grid bloom-table into the grid for
// a particular month of a particular year. The table is what it works on, so the same code
// serves a page of the Wall Calendar template and a grid a user has put on a canvas.
//
// The grid is the weekday header row and the six rows of day cells, and nothing else. The
// month name and the year are ordinary page elements above the table, not part of it, so a
// book can put them wherever it likes and this module never touches them.
//
// The template (src/content/templates/template books/Wall Calendar/Wall Calendar.pug) ships
// twelve of these grids with no day numbers, uniform borders, and the weekday names in
// Sunday-first order. It also ships one fully configured 'extra' page for January 2027, which
// is what Add Page offers to other books. What this module produces for January 2027 with
// Sunday as the first day of the week, and without the dates of the neighboring months, is
// that page's grid: the pug block and this file compute the same thing, one at build time and
// one as the user arrives at a page.
//
// Nothing here touches the network or anything outside the one bloom-table, so it is a plain
// DOM transform that a test can run in jsdom.

/** Columns in the grid: one per day of the week. */
export const kCalendarColumnCount = 7;

/** Rows of day cells in the template. Six is always enough: 6 leading blanks plus 31 days is 37 cells. */
export const kCalendarDayRowCount = 6;

/**
 * Rows of day cells this particular month needs. Many months fit in five rows, and a
 * 28-day February that starts on the first day of the week fits in four. The grid shows only
 * this many rows: the leftover template rows are hidden, and because every day row is a
 * 'fill' row, the height they would have taken spreads over the rows that remain.
 */
export function dayRowCountForMonth(layout: ICalendarMonthLayout): number {
    const cellsUsed =
        leadingBlankCellCount(layout) + daysInMonth(layout.year, layout.month);
    return Math.ceil(cellsUsed / kCalendarColumnCount);
}

/**
 * Where a grid page records the year and first day of the week it was last laid out for.
 * This lives on the page's .bloom-table, not on the .bloom-page element itself: when Bloom
 * saves a page it keeps the page element's own attributes from the book DOM and takes only
 * its children from the browser, so an attribute added to the page root would be lost on
 * save and the page would be laid out again on every open.
 */
export const kCalendarLaidOutAttribute = "data-calendar-laid-out";

/** Where a weekday header cell records which day of the week it holds, 0 = Sunday. */
export const kCalendarWeekdayAttribute = "data-calendar-weekday";

/** The attribute a grid table carries saying which month of the year it is, "0" to "11". */
export const kCalendarMonthAttribute = "data-calendar-month";

/** The attribute a grid table on a canvas carries saying which year it is for. */
export const kCalendarYearAttribute = "data-calendar-year";

/** The attribute a grid table on a canvas carries saying where its week starts, "0" = Sunday. */
export const kCalendarFirstDayAttribute = "data-calendar-first-day";

/**
 * The attribute saying whether this grid shows the dates of the previous and the next month
 * in the cells around its own month. "true" turns them on; anything else leaves them off.
 */
export const kCalendarNeighborDaysAttribute = "data-calendar-neighbor-days";

/** Whether this grid shows the dates of the neighboring months. */
export function getShowNeighborDays(table: HTMLElement): boolean {
    return table.getAttribute(kCalendarNeighborDaysAttribute) === "true";
}

/** One side of one boundary, in the shape bloom-table stores in data-edges-v/h. */
interface IBorderSpec {
    weight: number;
    style: string;
    color: string;
}

/**
 * One vertical boundary, split into its two sides: 'west' is painted by the cell to the
 * left as its right border, 'east' by the cell to the right as its left border. Naming the
 * side matters: when a boundary is given as one plain spec, bloom-table picks which
 * neighbor paints it per cell, and neighbors paint on opposite sides of the shared grid
 * line, so a line whose segments alternate painters shows one-pixel steps.
 */
interface IEdgeV {
    west: IBorderSpec;
    east: IBorderSpec;
}

/** One horizontal boundary, split the same way: 'north' is the cell above's bottom border,
 * 'south' the cell below's top border. */
interface IEdgeH {
    north: IBorderSpec;
    south: IBorderSpec;
}

const kLine: IBorderSpec = { weight: 1, style: "solid", color: "#000" };
const kNone: IBorderSpec = { weight: 0, style: "none", color: "#000" };
const kNoEdgeV: IEdgeV = { west: kNone, east: kNone };
const kNoEdgeH: IEdgeH = { north: kNone, south: kNone };

/** What a month grid has to be laid out: which month, and where the week starts. */
export interface ICalendarMonthLayout {
    year: number;
    /** 0 for January through 11 for December. */
    month: number;
    /** 0 for Sunday through 6 for Saturday. */
    firstDayOfWeek: number;
    /**
     * Whether the cells around the month hold the dates of the previous and the next month,
     * rather than being left empty and outside the drawn borders.
     */
    showNeighborDays: boolean;
}

/**
 * The value a laid-out page carries in kCalendarLaidOutAttribute. Comparing this with what
 * the page already has is how the tooling knows whether it has to lay the page out again.
 */
export function calendarLayoutSignature(layout: ICalendarMonthLayout): string {
    return `${layout.year},${layout.firstDayOfWeek},${
        layout.showNeighborDays ? 1 : 0
    }`;
}

/**
 * Whether a grid page's table has to be laid out (again) for the given layout. Usually the
 * recorded signature answers this. The row-count comparison catches a table whose structure
 * does not match what this build lays out, such as a page a build without the hidden-rows
 * behavior laid out with all six rows: the declared rows are wrong for the month, so the
 * page gets laid out again. A row the user has resized keeps its token count, so a resize
 * alone never triggers this.
 */
export function calendarTableNeedsLayout(
    table: HTMLElement,
    layout: ICalendarMonthLayout,
): boolean {
    if (
        table.getAttribute(kCalendarLaidOutAttribute) !==
        calendarLayoutSignature(layout)
    ) {
        return true;
    }
    const declaredRowCount = (table.getAttribute("data-row-heights") || "")
        .split(",")
        .filter((token) => token.trim() !== "").length;
    return declaredRowCount !== calendarRowHeights(layout).length;
}

/** How many days the given month of the given year has. Day 0 of the next month is this one's last. */
export function daysInMonth(year: number, month: number): number {
    return new Date(year, month + 1, 0).getDate();
}

/**
 * How many empty cells come before day 1, given where the week starts. For a month whose
 * first day IS the first day of the week this is 0, and the month fills the grid from the
 * top left corner.
 */
export function leadingBlankCellCount(layout: ICalendarMonthLayout): number {
    const weekdayOfFirstDay = new Date(layout.year, layout.month, 1).getDay();
    return (weekdayOfFirstDay - layout.firstDayOfWeek + 7) % 7;
}

/** What one day cell holds after a layout. */
export interface ICalendarCellDay {
    /** The number to show in the cell, or 0 for a cell that shows nothing. */
    day: number;
    /** True when that number is a date of the previous or the next month. */
    isNeighbor: boolean;
}

/**
 * What each of the 42 cells holds. Without the neighbor dates this is the month's own days,
 * with 0 for every cell before day 1 and past the end of the month.
 *
 * With the neighbor dates, the cells before day 1 hold the last days of the previous month,
 * and the cells after the last day, as far as the end of the last row this month shows, hold
 * the first days of the next month. Cells in the rows this month does not show stay empty:
 * those rows are hidden, so a date in them would be a date nobody can see.
 */
export function cellDaysForCells(
    layout: ICalendarMonthLayout,
): ICalendarCellDay[] {
    const leading = leadingBlankCellCount(layout);
    const lastDay = daysInMonth(layout.year, layout.month);
    // Day 0 of this month is the last day of the previous one, whatever year that is in.
    const lastDayOfPreviousMonth = new Date(
        layout.year,
        layout.month,
        0,
    ).getDate();
    const shownCellCount = dayRowCountForMonth(layout) * kCalendarColumnCount;
    const cells: ICalendarCellDay[] = [];
    for (
        let cell = 0;
        cell < kCalendarColumnCount * kCalendarDayRowCount;
        cell++
    ) {
        const day = cell - leading + 1;
        if (day >= 1 && day <= lastDay) {
            cells.push({ day, isNeighbor: false });
        } else if (!layout.showNeighborDays || cell >= shownCellCount) {
            cells.push({ day: 0, isNeighbor: false });
        } else if (day < 1) {
            cells.push({
                day: lastDayOfPreviousMonth + day,
                isNeighbor: true,
            });
        } else {
            cells.push({ day: day - lastDay, isNeighbor: true });
        }
    }
    return cells;
}

/**
 * The day of the month each of the 42 cells holds, or 0 for a cell that holds no day of this
 * month. The leading zeros are the cells before day 1; the trailing zeros are the cells past
 * the end of the month.
 */
export function dayNumbersForCells(layout: ICalendarMonthLayout): number[] {
    return cellDaysForCells({ ...layout, showNeighborDays: false }).map(
        (cell) => cell.day,
    );
}

/**
 * The first and last cells inside the box the grid draws.
 *
 * Without the neighbor dates that is the cells holding day 1 and the last day of the month:
 * the blanks before day 1 and the cells past the end of the month are both left outside the
 * box, with no borders. With the neighbor dates every cell of every shown row holds a date,
 * so the box is the whole rectangle of those rows.
 */
function boxedCellRange(layout: ICalendarMonthLayout): {
    first: number;
    last: number;
} {
    if (layout.showNeighborDays) {
        return {
            first: 0,
            last: dayRowCountForMonth(layout) * kCalendarColumnCount - 1,
        };
    }
    const first = leadingBlankCellCount(layout);
    return {
        first,
        last: first + daysInMonth(layout.year, layout.month) - 1,
    };
}

/**
 * data-edges-v for the whole table: one entry per row the table shows, each holding the
 * eight boundaries between and around the seven columns (entry 0 is the left edge of the
 * table, entry 7 the right). The weekday row has no vertical lines at all. The day area
 * always gets its full left and right perimeter. An interior boundary is drawn only when a
 * cell holding a day touches it, so the blanks before day 1 and the cells past the end of
 * the month have no lines between them.
 *
 * Every drawn boundary names its painting side, and every segment of one boundary uses the
 * same side, so the line stays straight (see IEdgeV): the left perimeter is painted by the
 * first column's left borders, and every other boundary by the column to its left as right
 * borders.
 */
export function computeEdgesV(layout: ICalendarMonthLayout): IEdgeV[][] {
    const boxed = boxedCellRange(layout);
    const isBoxed = (cell: number) => cell >= boxed.first && cell <= boxed.last;
    const rows: IEdgeV[][] = [
        new Array(kCalendarColumnCount + 1).fill(kNoEdgeV), // the weekday row
    ];
    for (let row = 0; row < dayRowCountForMonth(layout); row++) {
        const boundaries: IEdgeV[] = [];
        for (let boundary = 0; boundary <= kCalendarColumnCount; boundary++) {
            if (boundary === 0) {
                boundaries.push({ west: kNone, east: kLine });
                continue;
            }
            const cellToTheLeft = row * kCalendarColumnCount + boundary - 1;
            const cellToTheRight = row * kCalendarColumnCount + boundary;
            const drawn =
                boundary === kCalendarColumnCount ||
                isBoxed(cellToTheLeft) ||
                isBoxed(cellToTheRight);
            boundaries.push({ west: drawn ? kLine : kNone, east: kNone });
        }
        rows.push(boundaries);
    }
    return rows;
}

/**
 * data-edges-h for the whole table: one boundary of seven entries per row edge, one entry
 * per column (boundary 0 is the top of the table, the last one its bottom). Nothing is drawn
 * above or below the weekday row, except that the top of the day area and the bottom of the
 * last shown row always get their full perimeter line. An interior boundary is drawn in a
 * column only when a cell holding a day sits above or below it.
 *
 * Every drawn boundary names its painting side, and every segment of one boundary uses the
 * same side, so the line stays straight (see IEdgeV): the bottom perimeter is painted by the
 * last row's bottom borders, and every other boundary by the row below it as top borders.
 */
export function computeEdgesH(layout: ICalendarMonthLayout): IEdgeH[][] {
    const boxed = boxedCellRange(layout);
    const isBoxed = (cell: number) => cell >= boxed.first && cell <= boxed.last;
    const dayRows = dayRowCountForMonth(layout);
    const boundaries: IEdgeH[][] = [
        new Array(kCalendarColumnCount).fill(kNoEdgeH), // the top of the weekday row
    ];
    for (let boundary = 0; boundary <= dayRows; boundary++) {
        if (boundary === dayRows) {
            boundaries.push(
                new Array(kCalendarColumnCount).fill({
                    north: kLine,
                    south: kNone,
                }),
            );
            continue;
        }
        const columns: IEdgeH[] = [];
        for (let column = 0; column < kCalendarColumnCount; column++) {
            const cellAbove = (boundary - 1) * kCalendarColumnCount + column;
            const cellBelow = boundary * kCalendarColumnCount + column;
            const drawn =
                boundary === 0 || isBoxed(cellAbove) || isBoxed(cellBelow);
            columns.push({ north: kNone, south: drawn ? kLine : kNone });
        }
        boundaries.push(columns);
    }
    return boundaries;
}

/**
 * The tokens for the table's data-row-heights: the weekday row, which hugs its names, and one
 * 'fill' entry per day row the month shows. bloom-table's render() rebuilds grid-template-rows
 * from these on every render, so they, not the inline style, are what really sizes the rows.
 */
export function calendarRowHeights(layout: ICalendarMonthLayout): string[] {
    return [
        "hug",
        ...new Array<string>(dayRowCountForMonth(layout)).fill("fill"),
    ];
}

/**
 * The grid tracks the table lays itself out with: what bloom-table's render() computes for
 * the 'fill' and 'hug' tokens of calendarRowHeights. Written inline so the page has its
 * shape before any JavaScript runs (the Add Page thumbnail, publish previews).
 */
export function calendarGridStyle(layout: ICalendarMonthLayout): string {
    const columns = new Array(kCalendarColumnCount)
        .fill("minmax(60px,1fr)")
        .join(" ");
    const rows = calendarRowHeights(layout)
        .map((token) =>
            token === "hug" ? "minmax(20px,max-content)" : "minmax(20px,1fr)",
        )
        .join(" ");
    return (
        `grid-template-columns: ${columns}; ` + `grid-template-rows: ${rows};`
    );
}

/**
 * The seven header cells holding the weekday names, in the order they currently appear. The
 * table holds nothing but these and the day cells, so they are the direct-child cells that
 * are not day cells.
 */
function getWeekdayCells(table: HTMLElement): HTMLElement[] {
    return Array.from(table.children).filter(
        (child) =>
            child.classList.contains("bloom-cell") &&
            !child.classList.contains("calendarDayCell"),
    ) as HTMLElement[];
}

/** The forty-two day cells, in reading order. */
function getDayCells(table: HTMLElement): HTMLElement[] {
    return Array.from(table.querySelectorAll<HTMLElement>(".calendarDayCell"));
}

/**
 * Put the weekday header cells in the order the chosen first day of the week asks for, by
 * moving the existing elements. Moving rather than rewriting is what lets a name the user
 * typed travel with its own weekday.
 *
 * A cell records which weekday it holds, so that laying the page out again for a different
 * first day rotates from the right starting point. The template ships the cells in
 * Sunday-first order with no such record, which is what the first pass assumes.
 */
export function rotateWeekdayCells(
    table: HTMLElement,
    firstDayOfWeek: number,
): void {
    const cells = getWeekdayCells(table);
    if (cells.length !== kCalendarColumnCount) {
        throw new Error(
            `layOutCalendarMonthPage: expected ${kCalendarColumnCount} weekday cells but found ${cells.length}`,
        );
    }
    // Which weekday each cell holds now. Sunday-first is the order the template ships in.
    const weekdayOfCell = cells.map((cell, index) => {
        const recorded = cell.getAttribute(kCalendarWeekdayAttribute);
        return recorded === null ? index : parseInt(recorded, 10);
    });
    const cellForWeekday = new Map<number, HTMLElement>();
    cells.forEach((cell, index) =>
        cellForWeekday.set(weekdayOfCell[index], cell),
    );

    // The weekday cells are the first row of the table, so putting each one back in turn
    // before the first day cell restores that position.
    const firstDayCell = table.querySelector(".calendarDayCell");
    for (let column = 0; column < kCalendarColumnCount; column++) {
        const weekday = (firstDayOfWeek + column) % kCalendarColumnCount;
        const cell = cellForWeekday.get(weekday);
        if (!cell) {
            throw new Error(
                `layOutCalendarMonthPage: no weekday header cell holds weekday ${weekday}`,
            );
        }
        cell.setAttribute(kCalendarWeekdayAttribute, String(weekday));
        table.insertBefore(cell, firstDayCell);
    }
}

/**
 * The wrapper inside a day cell that holds the day number and whatever else the cell shows.
 * The table library requires a cell to have exactly one child, so everything a day cell holds
 * goes inside this one element.
 *
 * A day cell has one from the template. It loses it when the user changes the cell's content
 * type: the library rebuilds the cell from that type's own template, which throws the wrapper
 * away and the day number with it. So when there is no wrapper we put one back around whatever
 * the library has just built. The caller then finds no day number and makes a new one, which is
 * how a cell the user has turned into a picture gets its date back and keeps the picture.
 *
 * This is a repair rather than a migration: it happens the next time the grid is laid out, so
 * a book already carrying a cell like this heals as the user arrives at it.
 */
function getOrMakeDayCellContents(cell: HTMLElement): HTMLElement {
    const existing = cell.querySelector<HTMLElement>(
        ".calendarDayCellContents",
    );
    if (existing) return existing;
    const contents = cell.ownerDocument.createElement("div");
    contents.className = "calendarDayCellContents";
    while (cell.firstChild) contents.appendChild(cell.firstChild);
    cell.appendChild(contents);
    return contents;
}

/**
 * Give one day cell the day it now holds. A cell with no day, before day 1 or past the end
 * of the month, loses its day-number element altogether and becomes a calendarUnusedDay, the
 * marker for a cell outside the drawn borders. A cell holding a date of the previous or the
 * next month keeps its number and is marked calendarNeighborDay instead, because it is inside
 * the drawn borders and the style sheet shows its number more faintly.
 *
 * The note field below the number is never touched, so a note the user has typed survives the
 * page being laid out again for a different year.
 */
function setDayOfCell(
    cell: HTMLElement,
    day: number,
    isNeighbor: boolean,
): void {
    cell.classList.toggle("calendarUnusedDay", day === 0);
    cell.classList.toggle("calendarNeighborDay", isNeighbor);
    const contents = getOrMakeDayCellContents(cell);
    let numberElement =
        contents.querySelector<HTMLElement>(".calendarDayNumber");
    if (day === 0) {
        numberElement?.remove();
        return;
    }
    if (!numberElement) {
        numberElement = cell.ownerDocument.createElement("div");
        numberElement.className =
            "calendarDayNumber CalendarDayNumber-style bloom-styleable";
        numberElement.setAttribute("tabindex", "0");
        contents.insertBefore(numberElement, contents.firstChild);
    }
    numberElement.textContent = String(day);
}

/**
 * Lay one month grid out for the given month, year, and first day of the week.
 *
 * Safe to run again on a table it has already laid out: the day numbers, the calendarUnusedDay
 * and calendarNeighborDay classes, the weekday order, and the table's borders are all
 * recomputed from the layout it is given, and everything the user can type into is left alone.
 */
export function layOutCalendarMonthPage(
    table: HTMLElement,
    layout: ICalendarMonthLayout,
): void {
    rotateWeekdayCells(table, layout.firstDayOfWeek);

    const days = cellDaysForCells(layout);
    const cells = getDayCells(table);
    if (cells.length !== days.length) {
        throw new Error(
            `layOutCalendarMonthPage: expected ${days.length} day cells but found ${cells.length}`,
        );
    }
    // The rows past the ones this month needs are hidden rather than removed, so a later
    // layout for a longer month (a different year, a different first day of the week) gets
    // its cells back, notes and all. A hidden cell is not placed by the grid, so the six
    // template rows shrink to the count data-row-heights declares below.
    const visibleCellCount = dayRowCountForMonth(layout) * kCalendarColumnCount;
    cells.forEach((cell, index) => {
        setDayOfCell(cell, days[index].day, days[index].isNeighbor);
        if (index >= visibleCellCount) {
            cell.style.display = "none";
        } else {
            cell.style.removeProperty("display");
        }
    });

    table.setAttribute(
        "data-row-heights",
        calendarRowHeights(layout).join(","),
    );
    table.setAttribute("data-edges-v", JSON.stringify(computeEdgesV(layout)));
    table.setAttribute("data-edges-h", JSON.stringify(computeEdgesH(layout)));
    table.setAttribute("style", calendarGridStyle(layout));

    table.setAttribute(
        kCalendarLaidOutAttribute,
        calendarLayoutSignature(layout),
    );
}
