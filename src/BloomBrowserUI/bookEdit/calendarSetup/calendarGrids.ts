// Finding the calendar month grids on the page the user is looking at, working out what each
// one should be laid out as, and writing text into a field of one.
//
// Both bundles use this. The page iframe uses it as a page loads, so a grid is laid out before
// the table library first draws it; the workspace frame's calendarTooling uses it to find the
// grids whose weekday names it reconciles with the collection's. Nothing here touches the
// network, so a test can run it in jsdom.
//
// A page can hold more than one grid: a Wall Calendar page has one, and a page of any other
// book has one for each grid the user has dropped onto a canvas. Nothing here ever looks at a
// page other than the one it is given.

import { kWeekdaySeedNames } from "./calendarSeedNames";
import {
    calendarTableNeedsLayout,
    getShowNeighborDays,
    ICalendarMonthLayout,
    kCalendarFirstDayAttribute,
    kCalendarMonthAttribute,
    kCalendarWeekdayAttribute,
    kCalendarYearAttribute,
    layOutCalendarMonthPage,
} from "./layOutCalendarMonthPage";

/**
 * Every calendar month grid inside `container`.
 *
 * A grid is a .bloom-table that says which month it is for. The month lives on the table
 * itself and nowhere else: the Wall Calendar template writes it there, and so does
 * buildCalendarGridTable for a grid the user puts on a canvas.
 */
export function getCalendarGrids(container: HTMLElement): HTMLElement[] {
    return Array.from(
        container.querySelectorAll<HTMLElement>(
            `.bloom-table[${kCalendarMonthAttribute}]`,
        ),
    );
}

/** Where a grid's week starts when neither the table nor the page says: Sunday. */
export const kDefaultFirstDayOfWeek = 0;

/**
 * What this grid should be laid out as, or undefined if the book has not said yet.
 *
 * A grid on a canvas carries its own year and first day of the week, because it stands on its
 * own. A grid page of a Wall Calendar takes both from the page, where the data-div puts the
 * values the whole book shares.
 *
 * The year is the one value the book must state: an unconfigured Wall Calendar, whose user has
 * not yet been asked for a year, has none, and that is what undefined means. A first day of the
 * week nobody has stated is Sunday, so that choosing a year is enough to make a new calendar
 * show its dates.
 */
export function resolveGridLayout(
    table: HTMLElement,
    pageElement: HTMLElement,
): ICalendarMonthLayout | undefined {
    const month = readNumberAttribute(table, kCalendarMonthAttribute);
    if (month === undefined) return undefined;
    const year = resolveGridYear(table, pageElement);
    if (year === undefined) return undefined;
    return {
        year,
        month,
        firstDayOfWeek:
            resolveGridFirstDayOfWeek(table, pageElement) ??
            kDefaultFirstDayOfWeek,
        showNeighborDays: getShowNeighborDays(table),
    };
}

/** The data-book element of a Wall Calendar page that holds the year of the whole book. */
export const kCalendarYearDataBookSelector = "[data-book='calendarYear']";

/** The data-book element of a Wall Calendar page that holds where the week starts. */
export const kCalendarFirstDayDataBookSelector =
    "[data-book='calendarFirstDayOfWeek']";

/**
 * The year this grid is for, or undefined if neither the table nor the page says.
 *
 * The table wins, because a grid on a canvas stands on its own; a Wall Calendar page grid
 * carries no year of its own and takes the one the data-div has put on the page.
 */
export function resolveGridYear(
    table: HTMLElement,
    pageElement: HTMLElement,
): number | undefined {
    return (
        readNumberAttribute(table, kCalendarYearAttribute) ??
        readNumber(
            pageElement.querySelector<HTMLElement>(
                kCalendarYearDataBookSelector,
            ),
        )
    );
}

/**
 * Which day this grid's week starts on, 0 for Sunday, or undefined if nothing says. The table
 * wins over the page for the same reason it does for the year.
 */
export function resolveGridFirstDayOfWeek(
    table: HTMLElement,
    pageElement: HTMLElement,
): number | undefined {
    return (
        readNumberAttribute(table, kCalendarFirstDayAttribute) ??
        readNumber(
            pageElement.querySelector<HTMLElement>(
                kCalendarFirstDayDataBookSelector,
            ),
        )
    );
}

/**
 * Lay this grid out if it is not already laid out the way the book now asks for. Says whether
 * it did, because the table library has to be asked to draw a grid whose borders have changed.
 */
export function layOutGridIfNeeded(
    table: HTMLElement,
    pageElement: HTMLElement,
): boolean {
    const layout = resolveGridLayout(table, pageElement);
    if (!layout) return false;
    if (!calendarTableNeedsLayout(table, layout)) return false;
    layOutCalendarMonthPage(table, layout);
    return true;
}

/**
 * Give every translationGroup of a grid an editable in the book's own language.
 *
 * A grid comes out of buildCalendarGridTable holding only the five seed-language editables.
 * None of them is marked as the book's language and none is marked visible, so the user sees
 * an empty grid and cannot type in it. This adds the missing editable to each group, as a new
 * table cell gets one (see ensureContentTypesRegistered in tableEditing.ts).
 *
 * The text of a weekday editable is the name the collection last saved for that day; failing
 * that, the seed name of the day when the book's language is one of the seed languages;
 * failing that, nothing. A day note starts out empty.
 *
 * If the book's language is itself a seed language, the group already has an editable in it,
 * and that one is marked rather than a second one added.
 */
export function addBookLanguageEditablesToGrid(
    grid: HTMLElement,
    languageCode: string,
    savedDayNames: string[],
): void {
    grid.querySelectorAll<HTMLElement>(".bloom-translationGroup").forEach(
        (group) => {
            const weekday = getWeekdayOfGroup(group);
            const text =
                weekday === undefined
                    ? ""
                    : nameOfWeekdayForANewGrid(
                          weekday,
                          languageCode,
                          savedDayNames,
                      );
            addBookLanguageEditableToGroup(group, languageCode, text);
        },
    );
}

/**
 * Write the weekday names the collection has saved into a grid, for the days it has a name
 * for. Used once the answer to the calendarSettings request has come back, which is after the
 * grid is already on the page.
 */
export function writeSavedWeekdayNamesIntoGrid(
    grid: HTMLElement,
    savedDayNames: string[],
): void {
    grid.querySelectorAll<HTMLElement>(
        `[${kCalendarWeekdayAttribute}]`,
    ).forEach((cell) => {
        const weekday = parseInt(
            cell.getAttribute(kCalendarWeekdayAttribute)!,
            10,
        );
        const name = savedDayNames[weekday] ?? "";
        if (!name) return;
        writeEditableText(
            cell.querySelector<HTMLElement>(".bloom-editable.bloom-content1"),
            name,
        );
    });
}

/** Which weekday this translationGroup names, or undefined if it is not a weekday name. */
function getWeekdayOfGroup(group: HTMLElement): number | undefined {
    const cell = group.closest<HTMLElement>(`[${kCalendarWeekdayAttribute}]`);
    if (!cell) return undefined;
    const weekday = parseInt(cell.getAttribute(kCalendarWeekdayAttribute)!, 10);
    return Number.isNaN(weekday) ? undefined : weekday;
}

/** What a new grid should show for one weekday, in the book's own language. */
function nameOfWeekdayForANewGrid(
    weekday: number,
    languageCode: string,
    savedDayNames: string[],
): string {
    return (
        savedDayNames[weekday] ||
        kWeekdaySeedNames[languageCode]?.[weekday] ||
        ""
    );
}

/**
 * Put an editable for `languageCode` in this group, or mark the one that is already there.
 *
 * The new editable deliberately gets no contenteditable attribute: that is what tells
 * wireBloomContentOfNewCells in tableEditing.ts that this editable still needs Bloom's
 * editing wiring, which is what makes it typable and gives it CKEditor.
 */
function addBookLanguageEditableToGroup(
    group: HTMLElement,
    languageCode: string,
    text: string,
): void {
    const existing = group.querySelector<HTMLElement>(
        `.bloom-editable[lang='${languageCode}']`,
    );
    const editable = existing ?? makeEditableLike(group, languageCode);
    editable.classList.add("bloom-content1", "bloom-visibility-code-on");
    // An editable that was already there holds the seed name of the day, which is what we
    // want unless the collection has a name of its own for it.
    if (!existing || text) writeEditableText(editable, text);
}

/** A new editable, styled as the group's other editables are, added to the group. */
function makeEditableLike(
    group: HTMLElement,
    languageCode: string,
): HTMLElement {
    const editable = group.ownerDocument.createElement("div");
    const model = group.querySelector<HTMLElement>(".bloom-editable");
    editable.className = model?.className ?? "bloom-editable";
    editable.setAttribute("lang", languageCode);
    editable.appendChild(group.ownerDocument.createElement("p"));
    group.appendChild(editable);
    return editable;
}

/** A whole number written in an attribute, or undefined if it holds nothing usable. */
function readNumberAttribute(
    element: HTMLElement,
    attributeName: string,
): number | undefined {
    const text = element.getAttribute(attributeName)?.trim();
    if (!text) return undefined;
    const value = parseInt(text, 10);
    return Number.isNaN(value) ? undefined : value;
}

/** A whole number written in an element, or undefined if it holds nothing usable. */
function readNumber(element: HTMLElement | null): number | undefined {
    const text = element?.textContent?.trim();
    if (!text) return undefined;
    const value = parseInt(text, 10);
    return Number.isNaN(value) ? undefined : value;
}

// The slice of a CKEditor instance this module touches. Each bloom-editable carries its
// instance as `bloomCkEditor` (set in BloomField.WireToCKEditor); the full CKEDITOR types
// live in the page iframe's bundle, and this module is in both bundles.
export interface IEditorOfEditable {
    status: string;
    setData(html: string): void;
    on(eventName: string, listener: () => void): { removeListener(): void };
}

/**
 * Put text into an editable, keeping the paragraph Bloom's editing code expects a
 * bloom-editable's text to live inside.
 *
 * The editable usually has a CKEditor instance already: Bloom attaches one to each editable
 * during page setup, before it tells the tooling the page is open. The editor snapshots the
 * content when it attaches, finishes starting up asynchronously, and then writes that
 * snapshot back into the element. A plain DOM write here would therefore show for a moment
 * and then be erased. So after the DOM write, which makes the text visible at once, we hand
 * the same content to the editor as soon as it is ready, and its write-back keeps it.
 */
export function writeEditableText(
    editable: HTMLElement | null,
    text: string,
): void {
    if (!editable) return;
    const paragraph = editable.querySelector("p");
    if (paragraph) paragraph.textContent = text;
    else editable.textContent = text;

    const editor = (editable as { bloomCkEditor?: IEditorOfEditable })
        .bloomCkEditor;
    if (!editor) return;
    const html = editable.innerHTML;
    if (editor.status === "ready") {
        editor.setData(html);
    } else {
        const listener = editor.on("instanceReady", () => {
            listener.removeListener();
            editor.setData(html);
        });
    }
}
