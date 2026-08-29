// Builds a calendar month grid from nothing, so that a book which was not made from the Wall
// Calendar template can have one: the caller makes an empty .bloom-table div, and this fills
// it with the same structure the template's grid pages have and lays it out.
//
// The grid is the weekday header row and the 42 day cells, and nothing else. The month name
// and the year are not part of it: which month and year the grid is for lives in the table's
// own attributes, and whoever wants to show them writes them somewhere on the page.
//
// The markup here mirrors the calendarSeededField and calendarDayCell mixins of
// src/content/templates/template books/Wall Calendar/Wall Calendar.pug.
//
// Nothing here touches the network, so a test can run it in jsdom.

import { kCalendarSeedLanguages, kWeekdaySeedNames } from "./calendarSeedNames";
import {
    ICalendarMonthLayout,
    kCalendarColumnCount,
    kCalendarDayRowCount,
    kCalendarFirstDayAttribute,
    kCalendarMonthAttribute,
    kCalendarNeighborDaysAttribute,
    kCalendarYearAttribute,
    layOutCalendarMonthPage,
} from "./layOutCalendarMonthPage";

/** The column widths of a month grid: seven columns that share the width equally. */
const kCalendarColumnWidths = new Array<string>(kCalendarColumnCount)
    .fill("fill")
    .join(",");

/** Make one element, with its classes and attributes, in the table's own document. */
function makeElement(
    document: Document,
    className: string,
    attributes?: Record<string, string>,
): HTMLElement {
    const element = document.createElement("div");
    if (className) element.className = className;
    Object.entries(attributes ?? {}).forEach(([name, value]) =>
        element.setAttribute(name, value),
    );
    return element;
}

/**
 * One weekday header cell. It holds a translationGroup carrying the name of that day in each
 * of the five seed languages, as the pug's calendarSeededField mixin makes it. The book's own
 * language gets its empty editable later, from TranslationGroupManager, which copies the first
 * child and strips its text.
 */
function makeWeekdayCell(document: Document, weekday: number): HTMLElement {
    const cell = makeElement(document, "bloom-cell", {
        "data-content-type": "text",
    });
    const group = makeElement(document, "bloom-translationGroup");
    kCalendarSeedLanguages.forEach((languageCode) => {
        const editable = makeElement(
            document,
            "bloom-editable CalendarDayOfWeek-style",
            { lang: languageCode, contenteditable: "true" },
        );
        editable.textContent = kWeekdaySeedNames[languageCode][weekday];
        group.appendChild(editable);
    });
    cell.appendChild(group);
    return cell;
}

/**
 * One empty day cell: a day-number element for the layout code to fill in, and a notes field
 * under it. The day number is a plain, non-editable div, never inside a translationGroup, so
 * TranslationGroupManager leaves it alone; bloom-styleable and tabindex are what let the user
 * open the Format dialog on it.
 */
function makeDayCell(document: Document): HTMLElement {
    const cell = makeElement(document, "bloom-cell calendarDayCell", {
        "data-content-type": "text",
        "data-pad": "0",
    });
    const contents = makeElement(document, "calendarDayCellContents");
    contents.appendChild(
        makeElement(
            document,
            "calendarDayNumber CalendarDayNumber-style bloom-styleable",
            { tabindex: "0" },
        ),
    );
    const noteGroup = makeElement(
        document,
        "bloom-translationGroup CalendarDayNote-style",
        { "data-default-languages": "auto" },
    );
    noteGroup.appendChild(
        makeElement(document, "bloom-editable", {
            lang: "z",
            contenteditable: "true",
        }),
    );
    contents.appendChild(noteGroup);
    cell.appendChild(contents);
    return cell;
}

/**
 * Fill an empty .bloom-table with a month grid for the given month, and lay it out.
 *
 * The table ends up carrying which month, year, and first day of the week it is for, and
 * whether it shows the dates of the neighboring months, so that the menu the user opens on
 * it later can read back what it was built with.
 */
export function buildCalendarGridTable(
    tableDiv: HTMLElement,
    layout: ICalendarMonthLayout,
): void {
    const document = tableDiv.ownerDocument;
    tableDiv.textContent = "";
    tableDiv.setAttribute("data-column-widths", kCalendarColumnWidths);

    // The weekday row, Sunday first: the layout rotates it to the chosen first day of the week.
    for (let weekday = 0; weekday < kCalendarColumnCount; weekday++) {
        tableDiv.appendChild(makeWeekdayCell(document, weekday));
    }
    for (
        let dayCell = 0;
        dayCell < kCalendarColumnCount * kCalendarDayRowCount;
        dayCell++
    ) {
        tableDiv.appendChild(makeDayCell(document));
    }

    tableDiv.setAttribute(kCalendarMonthAttribute, String(layout.month));
    tableDiv.setAttribute(kCalendarYearAttribute, String(layout.year));
    tableDiv.setAttribute(
        kCalendarFirstDayAttribute,
        String(layout.firstDayOfWeek),
    );
    if (layout.showNeighborDays) {
        tableDiv.setAttribute(kCalendarNeighborDaysAttribute, "true");
    } else {
        tableDiv.removeAttribute(kCalendarNeighborDaysAttribute);
    }

    layOutCalendarMonthPage(tableDiv, layout);
}
