import {
    addColumn,
    addRow,
    attachTable,
    defaultCellContentsForEachType,
    detachTable,
    getTableInfo,
    registerCellContentType,
    setColumnWidth,
    setDefaultCellContentTypeId,
    setRowHeight,
    unregisterCellContentType,
    kTableCellContentChangedEvent,
} from "bloom-table";
// Edit-only table styles (selection highlight, boundary hints). These must NOT
// reach published output, so they are loaded here in the editing context rather
// than via basePage.less. This injects into the page iframe (this module is part
// of editablePageBundle). The structural/read-time styles come from
// bloom-table.css, which basePage.less inlines so they ship everywhere.
import "bloom-table/bloom-table-edit.css";
import { SetupImagesInContainer } from "./bloomImages";
import BloomField from "../bloomField/BloomField";
import { theOneCanvasElementManager } from "./canvasElementManager/CanvasElementManager";

let contentTypesRegistered = false;

/**
 * Replace one of the library's built-in cell content types with the Bloom
 * equivalent, keeping the library's own name and icon so the cell menu still
 * looks and reads the way the library intends.
 */
function replaceCellContentType(
    id: string,
    templateHtml: string,
    regexToIdentify: RegExp,
    makeDefault: boolean,
): void {
    const libraryType = defaultCellContentsForEachType.find((c) => c.id === id);
    if (!libraryType) {
        throw new Error(
            `bloom-table has no cell content type "${id}" to replace`,
        );
    }
    registerCellContentType(
        { ...libraryType, templateHtml, regexToIdentify },
        { makeDefault },
    );
}

/** Register Bloom-specific cell content types with the bloom-table library. */
function ensureContentTypesRegistered(): void {
    if (contentTypesRegistered) return;
    contentTypesRegistered = true;

    // Text cells hold a bloom-translationGroup rather than the library's bare
    // contenteditable, so text in a table participates in Bloom's multilingual
    // system and its styles. The group gets one bloom-editable of its own here,
    // in the language new text boxes use, because nothing else would give the
    // cell one until the page is next loaded: TranslationGroupManager (C#) adds
    // the editables for the book's other languages then. Without that first
    // editable a new cell has nothing to type in, and so no format button and no
    // language name either. This is what a text canvas element does too; see
    // makeTranslationGroup in CanvasElementFactories.ts.
    const languageForNewText = GetSettings().languageForNewTextBoxes;
    replaceCellContentType(
        "text",
        "<div class='bloom-translationGroup bloom-trailingElement normal-style'>" +
            `<div class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on' lang='${languageForNewText}'><p></p></div>` +
            "</div>",
        /bloom-translationGroup/,
        true,
    );

    // Image cells hold a bloom-canvas, so Bloom's image tooling (choose image,
    // crop, image description, canvas elements) works inside a cell. The markup
    // matches what origami's Image link creates.
    replaceCellContentType(
        "image",
        "<div class='bloom-canvas bloom-has-canvas-element bloom-leadingElement'>" +
            "<div class='bloom-canvas-element bloom-backgroundImage' style='width:100%;height:100%;'>" +
            "<div class='bloom-imageContainer'><img src='placeHolder.png'/></div>" +
            "</div></div>",
        /bloom-canvas/,
        false,
    );

    // The library's video cell is a plain HTML5 <video> pointing at a sample on
    // the web. Bloom video needs a bloom-videoContainer and the Sign Language
    // tool, so offering the library's version would only produce a broken cell.
    // A cell can still hold a nested table, which the library's own template
    // builds out of cells of the default (text) type registered above.
    unregisterCellContentType("video");

    setDefaultCellContentTypeId("text");
}

/** Handle a cell's content being (re)initialised. Attached via SetupTableEditing. */
function onTableCellContentChanged(e: Event): void {
    const custom = e as CustomEvent<{
        cell: HTMLElement;
        contentType: string;
    }>;
    const { cell, contentType } = custom.detail;
    if (contentType === "text") {
        // Wire any bloom-editable divs C# may have already populated.
        // If the translationGroup is empty, bloom-editables will appear on next page load.
        cell.querySelectorAll<HTMLElement>(".bloom-editable").forEach(
            (editable) => BloomField.ManageField(editable),
        );
    } else if (contentType === "image") {
        SetupImagesInContainer(cell);
        const table = cell.closest<HTMLElement>(".bloom-table");
        const bloomCanvas = cell.querySelector<HTMLElement>(".bloom-canvas");
        if (table && bloomCanvas) {
            imageCellObservers.get(table)?.observe(bloomCanvas);
        }
    }
}

// The ResizeObserver watching the image cells of each attached table, so we can
// disconnect it when the table is detached.
const imageCellObservers = new Map<HTMLElement, ResizeObserver>();

// True while we wait for the next animation frame to refit the pictures, so that
// resizing a whole column produces one refit rather than one per cell.
let refitIsPending = false;

/**
 * Refit the picture in every image cell to the cell that holds it.
 *
 * A cell picture is a bloom-canvas, and Bloom sizes the background image of a
 * bloom-canvas in pixels, from the size the canvas had when Bloom last looked at it.
 * In a table that size is stale twice over: the table lays itself out with JavaScript,
 * which on page load can run after Bloom has already fitted the pictures, and a row or
 * column resize changes a cell without Bloom hearing about it. A picture fitted to a
 * cell of zero width is invisible, which is what made the placeholder flowers disappear
 * on the second load of a page.
 */
function refitImageCellPictures(): void {
    if (refitIsPending) return;
    refitIsPending = true;
    requestAnimationFrame(() => {
        refitIsPending = false;
        theOneCanvasElementManager?.adjustAfterContainerResize();
    });
}

/** Watch the image cells of one table, so their pictures follow the cells' size. */
function observeImageCells(tableDiv: HTMLElement): void {
    const observer = new ResizeObserver(() => refitImageCellPictures());
    tableDiv
        .querySelectorAll<HTMLElement>(".bloom-cell .bloom-canvas")
        .forEach((bloomCanvas) => observer.observe(bloomCanvas));
    imageCellObservers.set(tableDiv, observer);
}

function attachSingleTable(tableDiv: HTMLElement): void {
    if (tableDiv.hasAttribute("data-table-attached")) return;
    tableDiv.setAttribute("data-table-attached", "1");
    attachTable(tableDiv);
    observeImageCells(tableDiv);
}

/**
 * Wire table editing for the whole page. Called from SetupElements in
 * bloomEditing.ts on every page load. Attaches the cell-content-changed
 * event listener to the container and calls attachTable on every bloom-table
 * found inside it.
 */
export function SetupTableEditing(container: HTMLElement): void {
    ensureContentTypesRegistered();
    container.addEventListener(
        kTableCellContentChangedEvent,
        onTableCellContentChanged,
    );
    container
        .querySelectorAll<HTMLElement>(".bloom-table")
        .forEach((tableDiv) => attachSingleTable(tableDiv));
}

/**
 * Attach a single newly-created bloom-table element, with the library's own
 * default sizing: columns that grow, rows that hug their text. Called from
 * makeTableFieldClickHandler in origami.ts, for the Table link in the page
 * layout controls. The page-level event listener installed by
 * SetupTableEditing will already be on the page body, so no new listener is
 * needed here.
 */
export function AttachNewTable(tableDiv: HTMLElement): void {
    ensureContentTypesRegistered();
    attachSingleTable(tableDiv);
}

// The size of a brand-new table. These match what attachTable gives a table
// that arrives with no rows or columns; we create the lines ourselves (see
// AttachNewTableThatFillsItsSpace), so the numbers have to live here too.
const kNewTableColumnCount = 2;
const kNewTableRowCount = 2;

/**
 * Attach a single newly-created bloom-table whose every row and column grows to
 * share the space the table is given. Called from addTableCanvasElement in
 * CanvasElementFactories.ts: a table in a canvas element sits in a box of a
 * fixed height, so rows that hug their text would leave the bottom of that box
 * empty. (The Table link in the page layout controls uses AttachNewTable
 * instead, and keeps the library's own defaults.)
 *
 * The rows and columns are made here rather than by attachTable, which only
 * makes them for a table that has neither the data-column-widths nor the
 * data-row-heights attribute. Setting the sizes before attachTable means its
 * first render already draws the growing rows, so nothing has to ask the
 * library to render again.
 */
export function AttachNewTableThatFillsItsSpace(tableDiv: HTMLElement): void {
    ensureContentTypesRegistered();
    tableDiv.setAttribute("data-column-widths", "");
    tableDiv.setAttribute("data-row-heights", "");
    for (let i = 0; i < kNewTableColumnCount; i++) {
        addColumn(tableDiv, true);
    }
    for (let i = 0; i < kNewTableRowCount; i++) {
        addRow(tableDiv, true);
    }
    // "fill" is what the table's own Size control calls "Grow".
    const info = getTableInfo(tableDiv);
    for (let column = 0; column < info.columnCount; column++) {
        setColumnWidth(tableDiv, column, "fill");
    }
    for (let row = 0; row < info.rowCount; row++) {
        setRowHeight(tableDiv, row, "fill");
    }
    attachSingleTable(tableDiv);
}

/**
 * Detach table editing from all bloom-table elements within `container`.
 * Called from removeEditingDebris in bloomEditing.ts before navigating away.
 */
export function TeardownTableEditing(container: HTMLElement): void {
    container.removeEventListener(
        kTableCellContentChangedEvent,
        onTableCellContentChanged,
    );
    container
        .querySelectorAll<HTMLElement>(".bloom-table[data-table-attached]")
        .forEach((tableDiv) => {
            tableDiv.removeAttribute("data-table-attached");
            imageCellObservers.get(tableDiv)?.disconnect();
            imageCellObservers.delete(tableDiv);
            detachTable(tableDiv);
        });
}
