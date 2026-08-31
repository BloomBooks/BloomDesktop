// Recognising a canvas element that is the content of a table cell.
//
// A cell of a bloom-table holds content of one type, which the bloom-table
// library builds from a template Bloom registers (see
// ensureLibraryConfiguredForBloom in tableEditing.ts). The template for a picture cell is a bloom-canvas holding
// one background-image canvas element, so a cell's picture is a canvas element
// and the user can select it and get the canvas context toolbar.
//
// The control resolution asks this module whether the selected element is such a
// cell's content, because that decides which controls the element gets: see
// getControlConfiguration in canvasControlResolution.ts.
//
// Keep this dependency-light; it is used from both the toolbox and the page iframe.
import { getCurrentContentTypeId } from "bloom-table";
import { kCalendarMonthAttribute } from "../../calendarSetup/layOutCalendarMonthPage";
import {
    kBackgroundImageClass,
    kBloomCanvasSelector,
} from "./canvasElementConstants";

const kCellSelector = ".bloom-cell";

/** The bloom-table content type id of a cell that holds a picture. */
const kPictureContentTypeId = "image";

/**
 * The table cell whose content `canvasElement` is, or undefined when the element
 * is not the content of a cell.
 *
 * Two things have to be true, and both matter. The element must be the background
 * image of its bloom-canvas, and that bloom-canvas must be the cell's own content:
 * the cell's child, or its grandchild, because a calendar day cell keeps what it
 * holds inside a wrapper (calendarDayCellContents) so the day number and the
 * content can sit one above the other. A canvas element the user has added on top
 * of a cell's picture is in the same bloom-canvas and inside the same cell, but it
 * is something within the content rather than the content itself, and the ordinary
 * canvas commands do apply to it.
 */
export const getTableCellOfCellContent = (
    canvasElement: HTMLElement,
): HTMLElement | undefined => {
    if (!canvasElement.classList.contains(kBackgroundImageClass)) {
        return undefined;
    }

    const cell = canvasElement.closest<HTMLElement>(kCellSelector);
    if (!cell) {
        return undefined;
    }

    const bloomCanvas =
        canvasElement.closest<HTMLElement>(kBloomCanvasSelector);
    const holderOfTheCanvas = bloomCanvas?.parentElement;
    if (!holderOfTheCanvas) {
        return undefined;
    }
    if (
        holderOfTheCanvas === cell ||
        holderOfTheCanvas.parentElement === cell
    ) {
        return cell;
    }
    return undefined;
};

/**
 * The canvas element that is `cell`'s content, or undefined when the cell holds no
 * such content. This is the inverse of getTableCellOfCellContent, and it applies the
 * same rule: the element must be the background image of a bloom-canvas that is the
 * cell's own content, so a canvas element the user added on top of the picture, or
 * the picture of a nested table's cell, is not it.
 */
export const getCellContentCanvasElement = (
    cell: HTMLElement,
): HTMLElement | undefined => {
    const candidates = cell.querySelectorAll<HTMLElement>(
        `${kBloomCanvasSelector} .${kBackgroundImageClass}`,
    );
    return Array.from(candidates).find(
        (candidate) => getTableCellOfCellContent(candidate) === cell,
    );
};

/** True when `cell` belongs to a calendar month grid. */
export const isCellOfCalendarGrid = (cell: HTMLElement): boolean =>
    !!cell.closest(`.bloom-table[${kCalendarMonthAttribute}]`);

/**
 * True when Bloom builds the menu for `cell` itself, rather than letting the
 * bloom-table library open its own Cell menu.
 *
 * Only one cell needs this: a picture in a calendar month grid. Its menu has to
 * offer the image commands, which the library knows nothing about, beside the cell's
 * own items, which only the library can supply. So Bloom renders one menu built from
 * both: see cellContentControls in canvasElementControlRegistry.ts for the "..."
 * button's route and setCellMenuOpenHandler in tableEditing.ts for the right-click's.
 * Every other cell, in a calendar grid or a plain table, keeps the library's menu.
 */
export const bloomBuildsMenuForCell = (cell: HTMLElement): boolean =>
    getCurrentContentTypeId(cell) === kPictureContentTypeId &&
    isCellOfCalendarGrid(cell);
