// The book tooling module for Wall Calendar books, registered under the bookTooling meta
// value "calendar" that the Wall Calendar template puts in every book made from it.
//
// The template ships twelve month-grid pages with no day numbers and no year. This module
// asks the user for the year and the first day of the week once, when they first open the
// book, and then fills each grid in as they arrive at its page. It never writes to a page
// other than the one the user is looking at: the year and the first day of the week are
// data-book values, so writing them into the page the user is on puts them in the book's
// data-div when Bloom saves that page, and Bloom's own machinery then hands them to every
// other page as it is opened.

import { getAsync, postJson, postString } from "../../utils/bloomApi";
import { getEditablePageBundleExports } from "../js/workspaceFrames";
import { registerBookTooling } from "../bookTooling/bookToolingRegistry";
import {
    captureCalendarName,
    ICalendarSettings,
    emptyCalendarSettings,
    normalizeCalendarSettings,
    reconcileCalendarName,
} from "./calendarNames";
import {
    kCalendarColumnCount,
    kCalendarMonthAttribute,
    kCalendarWeekdayAttribute,
} from "./layOutCalendarMonthPage";
import {
    getCalendarGrids,
    layOutGridIfNeeded,
    writeEditableText,
} from "./calendarGrids";
import {
    closeCalendarSetupDialogIfOpen,
    showCalendarSetupDialog,
} from "./CalendarSetupDialog";

/** The bookTooling meta value that brings this module into play. */
export const kCalendarBookToolingName = "calendar";

const kCalendarSettingsApi = "calendarSettings";

// What the collection's configuration.txt says, as of the last time we read or wrote it.
let savedSettings: ICalendarSettings = emptyCalendarSettings();

// The year and first day of the week the user chose in the setup dialog, waiting to be
// written into the first grid page we see. They are not saved anywhere until then: putting
// them on a page is what gets them into the book.
let settingsToWriteIntoTheNextGridPage:
    | { year: number; firstDayOfWeek: number }
    | undefined;

// The grid pages the user has already opened since this book was opened. The first open of a
// page is the one where the collection's names win over the book's; see reconcileCalendarName.
const gridPagesOpenedThisSession = new Set<string>();

/** The month one grid is for, or undefined if it says nothing usable. */
function getMonthOfGrid(grid: HTMLElement): number | undefined {
    const attribute = grid.getAttribute(kCalendarMonthAttribute);
    if (attribute === null) return undefined;
    const month = parseInt(attribute, 10);
    return Number.isNaN(month) ? undefined : month;
}

/** The bloom-editable holding the book's own language, which is the one the user types in. */
function getVernacularEditable(group: Element | null): HTMLElement | null {
    return (
        group?.querySelector<HTMLElement>(".bloom-editable.bloom-content1") ??
        null
    );
}

function readEditableText(editable: HTMLElement | null): string {
    return editable?.textContent ?? "";
}

/**
 * The element holding the page's month name, in the book's own language. It is a text field of
 * the page, beside the grid rather than inside it, so the page is what to look in.
 */
function getMonthNameEditable(pageElement: HTMLElement): HTMLElement | null {
    return getVernacularEditable(
        pageElement.querySelector(".calendarMonthName"),
    );
}

/**
 * The weekday name element of one grid for each of the seven days, indexed by the day itself
 * (0 = Sunday), whatever order the header cells are currently in.
 *
 * A grid that has been laid out records the weekday on each header cell. One that has not is
 * still in the Sunday-first order the template ships.
 */
function getWeekdayNameEditables(grid: HTMLElement): Array<HTMLElement | null> {
    const cells = Array.from(
        grid.querySelectorAll<HTMLElement>(`[${kCalendarWeekdayAttribute}]`),
    );
    const editables: Array<HTMLElement | null> = new Array(
        kCalendarColumnCount,
    ).fill(null);
    if (cells.length === kCalendarColumnCount) {
        cells.forEach((cell) => {
            const weekday = parseInt(
                cell.getAttribute(kCalendarWeekdayAttribute)!,
                10,
            );
            editables[weekday] = getVernacularEditable(
                cell.querySelector(".bloom-translationGroup"),
            );
        });
        return editables;
    }
    // Not laid out yet: the header cells are the ones between the title row and the day
    // cells, still in the order the template ships them in.
    Array.from(grid.children)
        .filter(
            (child) =>
                child.classList.contains("bloom-cell") &&
                !child.classList.contains("bloom-skip") &&
                !child.classList.contains("calendarDayCell") &&
                !child.hasAttribute("data-span-x"),
        )
        .forEach((cell, weekday) => {
            if (weekday < kCalendarColumnCount) {
                editables[weekday] = getVernacularEditable(
                    cell.querySelector(".bloom-translationGroup"),
                );
            }
        });
    return editables;
}

/** The element holding the year, which the data-div keeps the same on every grid page. */
function getYearElement(pageElement: HTMLElement): HTMLElement | null {
    return pageElement.querySelector<HTMLElement>("[data-book='calendarYear']");
}

/** The hidden element holding the first day of the week, likewise kept in step by the data-div. */
function getFirstDayOfWeekElement(
    pageElement: HTMLElement,
): HTMLElement | null {
    return pageElement.querySelector<HTMLElement>(
        "[data-book='calendarFirstDayOfWeek']",
    );
}

/** A whole number written in an element, or undefined if it holds nothing usable. */
function readNumber(element: HTMLElement | null): number | undefined {
    const text = element?.textContent?.trim();
    if (!text) return undefined;
    const value = parseInt(text, 10);
    return Number.isNaN(value) ? undefined : value;
}

/** Read the collection's calendar settings, and remember them as this session's baseline. */
async function loadSettings(): Promise<void> {
    const result = await getAsync(kCalendarSettingsApi);
    savedSettings = normalizeCalendarSettings(result.data);
}

/** Write the collection's calendar settings back, so the next calendar book starts from them. */
function saveSettings(): void {
    postJson(kCalendarSettingsApi, savedSettings);
}

/**
 * The id of the book's first month-grid page.
 *
 * The page list itself says nothing about calendars, so we look at the content pages in turn
 * and take the first whose markup carries the month attribute. In a book made from the Wall
 * Calendar template that is the second content page, after January's picture page.
 */
async function findFirstGridPageId(): Promise<string | undefined> {
    const pageList = await getAsync("pageList/pages");
    const contentPages = (
        (pageList.data?.pages ?? []) as Array<{
            key: string;
            isXMatter: boolean;
        }>
    ).filter((page) => !page.isXMatter);
    for (const page of contentPages) {
        const pageContent = await getAsync(
            `pageList/pageContent?page-id=${encodeURIComponent(page.key)}`,
        );
        if (
            String(pageContent.data?.content ?? "").includes(
                kCalendarMonthAttribute,
            )
        ) {
            return page.key;
        }
    }
    return undefined;
}

// The page we mean to take the user to once the page they are on has finished opening.
let pageToGoToWhenTheCurrentOneHasSettled: string | undefined;

/**
 * Take the user to the first month grid, if we decided to and are not already there.
 *
 * The waiting matters. Our hooks run from the page iframe's own setup code, and Bloom is not
 * told the page has loaded until that setup finishes. Asking it to change pages before then
 * leaves it navigating to a page it has stopped tracking: the edit area goes blank and stays
 * blank, and further requests are dropped, because EditingModel ignores a page selection that
 * arrives while it is between pages.
 */
async function goToTheFirstGridPageIfWeShould(
    currentPageId: string,
): Promise<void> {
    const pageId = pageToGoToWhenTheCurrentOneHasSettled;
    pageToGoToWhenTheCurrentOneHasSettled = undefined;
    if (!pageId || pageId === currentPageId) return;
    if (!(await waitUntilBloomAgreesThisPageIsSelected(currentPageId))) return;
    // editView/jumpToPage, not pageList/pageClicked: while a modal dialog is (or was
    // moments ago) up, the page list is disabled, and PageThumbnailList.PageClicked drops
    // a click silently. jumpToPage saves the current page and navigates regardless.
    postString("editView/jumpToPage", pageId);
}

/**
 * Wait until Bloom names the given page as the selected one, which is how we know it has
 * finished opening it. Gives up after ten seconds and says so, rather than risk the blank
 * edit area described above.
 */
async function waitUntilBloomAgreesThisPageIsSelected(
    pageId: string,
): Promise<boolean> {
    const deadline = Date.now() + 10000;
    while (Date.now() < deadline) {
        const pageList = await getAsync("pageList/pages");
        if (pageList.data?.selectedPageId === pageId) return true;
        await new Promise((resolve) => window.setTimeout(resolve, 200));
    }
    console.warn(
        `calendarTooling: Bloom never said page ${pageId} was selected, so we are staying on it`,
    );
    return false;
}

/**
 * The book has been opened. If it has no year yet it has never been set up, so ask for the
 * year and the first day of the week and take the user to the first month grid.
 */
async function onBookOpened(bookDom: Document | undefined): Promise<void> {
    // A dialog still waiting on the previous book has to go: its book is gone, and while it
    // is up the page list and the workspace tabs are disabled.
    closeCalendarSetupDialogIfOpen();
    gridPagesOpenedThisSession.clear();
    settingsToWriteIntoTheNextGridPage = undefined;
    await loadSettings();

    const year = await getYearOfBook(bookDom);
    if (year) return; // already set up
    if (!(await theUserCanSetThisBookUp())) return;

    const chosen = await showCalendarSetupDialog(savedSettings.firstDayOfWeek);
    if (!chosen) return; // The user cancelled; we will ask again next time the book is opened.

    settingsToWriteIntoTheNextGridPage = chosen;
    // Remember the choice for the next calendar book made in this collection. The year is
    // deliberately not remembered: each book is for whatever year the user says.
    if (savedSettings.firstDayOfWeek !== chosen.firstDayOfWeek) {
        savedSettings.firstDayOfWeek = chosen.firstDayOfWeek;
        saveSettings();
    }
    pageToGoToWhenTheCurrentOneHasSettled = await findFirstGridPageId();
}

/**
 * Is this a book the user can actually set up as their calendar?
 *
 * The Wall Calendar template itself is a calendar book with no year, and so is any calendar in
 * a collection the user cannot write to. Asking for a year there would be asking a question
 * whose answer we could not keep, so we say nothing and leave the book alone.
 */
async function theUserCanSetThisBookUp(): Promise<boolean> {
    const info = await getAsync("app/selectedBookInfo");
    return !!info.data?.saveable && !info.data?.isTemplate;
}

/**
 * The year the book is for, from its data-div, or undefined if it has none yet. The book's
 * own .htm is the direct answer; the API is there for the case where we could not read it.
 */
async function getYearOfBook(
    bookDom: Document | undefined,
): Promise<number | undefined> {
    if (bookDom) {
        const element = bookDom.querySelector<HTMLElement>(
            "#bloomDataDiv [data-book='calendarYear']",
        );
        return readNumber(element);
    }
    const result = await getAsync(
        "editView/getDataBookValue?dataBook=calendarYear&lang=*",
    );
    const text = String(result.data?.content ?? "").trim();
    const year = parseInt(text, 10);
    return Number.isNaN(year) ? undefined : year;
}

/**
 * A page has been opened. On a month-grid page: write in anything the setup dialog is waiting
 * to hand over, lay the grid out if it is not already laid out for this year and first day of
 * the week, and reconcile the month and weekday names with the collection's.
 */
async function onPageOpened(pageElement: HTMLElement): Promise<void> {
    const pageId = pageElement.getAttribute("id") ?? "";
    const grids = getCalendarGrids(pageElement);
    if (grids.length === 0) {
        await goToTheFirstGridPageIfWeShould(pageId);
        return;
    }

    // Bloom's page setup will focus the first empty text box a moment from now, which on a
    // grid page lands in the month name and pops its source bubble over the title. This
    // attribute tells it to focus nothing. The template ships it on its grid pages; setting
    // it here as well covers books made before it did. It does not survive a save (a page
    // element's own attributes come from the book), so every open sets it again.
    pageElement.setAttribute("data-bloom-no-auto-focus", "true");

    if (settingsToWriteIntoTheNextGridPage) {
        const yearElement = getYearElement(pageElement);
        const firstDayElement = getFirstDayOfWeekElement(pageElement);
        if (yearElement && firstDayElement) {
            yearElement.textContent = String(
                settingsToWriteIntoTheNextGridPage.year,
            );
            firstDayElement.textContent = String(
                settingsToWriteIntoTheNextGridPage.firstDayOfWeek,
            );
            settingsToWriteIntoTheNextGridPage = undefined;
        }
    }

    // The first open is a property of the page, not of each grid on it: it is what tells us
    // the user has not yet had a chance to type a name on this page in this session.
    const isFirstOpen = !gridPagesOpenedThisSession.has(pageId);
    gridPagesOpenedThisSession.add(pageId);

    let settingsChanged = false;

    grids.forEach((grid) => {
        // The page iframe usually lays a grid out as the page loads, so there is normally
        // nothing to do here. The exception is the page we have just written the year into
        // above, which the iframe saw before it had one.
        if (layOutGridIfNeeded(grid, pageElement)) {
            // The table library reads the borders we have just rewritten when it renders, and
            // it renders when it attaches, so this is how we ask it to draw them.
            getEditablePageBundleExports()?.rerenderTables(grid);
        }
        if (reconcileWeekdayNamesOfGrid(grid, isFirstOpen)) {
            settingsChanged = true;
        }
    });
    if (reconcileMonthNameOfPage(pageElement, grids, isFirstOpen)) {
        settingsChanged = true;
    }

    if (settingsChanged) saveSettings();
    await goToTheFirstGridPageIfWeShould(pageId);
}

/**
 * Bring this page's month name and the one remembered for the collection into line, by the
 * rules in calendarNames.ts. Says whether the collection's settings have to change.
 *
 * The month name is a text field of the page, outside the grid, so a page has one of them
 * however many grids it holds. Which month it names is the month of the page's first grid.
 */
function reconcileMonthNameOfPage(
    pageElement: HTMLElement,
    grids: HTMLElement[],
    isFirstOpen: boolean,
): boolean {
    const month = getMonthOfGrid(grids[0]);
    if (month === undefined) return false;
    const editable = getMonthNameEditable(pageElement);
    const outcome = reconcileCalendarName(
        readEditableText(editable),
        savedSettings.monthNames[month] ?? "",
        isFirstOpen,
    );
    if (outcome.valueForPage !== undefined) {
        writeEditableText(editable, outcome.valueForPage);
    }
    if (outcome.valueForSettings === undefined) return false;
    savedSettings.monthNames[month] = outcome.valueForSettings;
    return true;
}

/**
 * Bring one grid's weekday names and the ones remembered for the collection into line, by the
 * rules in calendarNames.ts. Says whether the collection's settings have to change.
 */
function reconcileWeekdayNamesOfGrid(
    grid: HTMLElement,
    isFirstOpen: boolean,
): boolean {
    let settingsChanged = false;
    getWeekdayNameEditables(grid).forEach((editable, weekday) => {
        const outcome = reconcileCalendarName(
            readEditableText(editable),
            savedSettings.dayNames[weekday] ?? "",
            isFirstOpen,
        );
        if (outcome.valueForPage !== undefined) {
            writeEditableText(editable, outcome.valueForPage);
        }
        if (outcome.valueForSettings !== undefined) {
            savedSettings.dayNames[weekday] = outcome.valueForSettings;
            settingsChanged = true;
        }
    });
    return settingsChanged;
}

/**
 * The page is about to be saved. Anything the user has typed as a month or weekday name
 * becomes the collection's name for it, so the next book, and the pages they have not visited
 * yet, get it.
 */
function onPageSaved(pageElement: HTMLElement): void {
    const grids = getCalendarGrids(pageElement);
    if (grids.length === 0) return;
    let settingsChanged = false;

    // The month name belongs to the page, and names the month of the page's first grid.
    const month = getMonthOfGrid(grids[0]);
    if (month !== undefined) {
        const monthName = captureCalendarName(
            readEditableText(getMonthNameEditable(pageElement)),
            savedSettings.monthNames[month] ?? "",
        );
        if (monthName !== undefined) {
            savedSettings.monthNames[month] = monthName;
            settingsChanged = true;
        }
    }

    // The weekday names belong to each grid.
    grids.forEach((grid) => {
        getWeekdayNameEditables(grid).forEach((editable, weekday) => {
            const dayName = captureCalendarName(
                readEditableText(editable),
                savedSettings.dayNames[weekday] ?? "",
            );
            if (dayName !== undefined) {
                savedSettings.dayNames[weekday] = dayName;
                settingsChanged = true;
            }
        });
    });

    if (settingsChanged) saveSettings();
}

registerBookTooling(kCalendarBookToolingName, {
    onBookOpened,
    onPageOpened,
    onPageSaved,
});
