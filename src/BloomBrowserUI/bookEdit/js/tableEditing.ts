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
    kTableCellContentChangedEvent,
} from "bloom-table";
// Edit-only table styles (selection highlight, boundary hints). These must NOT
// reach published output, so they are loaded here in the editing context rather
// than via basePage.less. This injects into the page iframe (this module is part
// of editablePageBundle). The structural/read-time styles come from
// bloom-table.css, which basePage.less inlines so they ship everywhere.
import "bloom-table/bloom-table-edit.css";
import { SetupImagesInContainer } from "./bloomImages";
import { SetupVideoEditing } from "./bloomVideo";
import { post } from "../../utils/bloomApi";
import $ from "jquery";
import BloomField from "../bloomField/BloomField";
import { theOneCanvasElementManager } from "./canvasElementManager/CanvasElementManager";
import {
    activateLongPressFor,
    AddLanguageTags,
    attachToCkEditor,
} from "./bloomEditing";
import { observeTranslationGroupSizes } from "./translationGroupSizeMarking";
// calendarGrids deliberately imports nothing from this module, so a calendar grid can be laid
// out from here without the two importing each other. The layout code that would need
// RerenderTables is in calendarGridActions, which is not what this reaches for.
import { layOutGridAgain } from "../calendarSetup/calendarGrids";
import { kCalendarMonthAttribute } from "../calendarSetup/layOutCalendarMonthPage";

// The library's own "something changed in a table" notification. It fires on the
// page's document at the end of every operation that goes through the table's
// history (adding a row or column, changing a cell's content type, pasting or
// deleting a table, an undo or a redo), which is exactly when new cell content
// may need Bloom's wiring.
const kTableHistoryUpdatedEvent = "tableHistoryUpdated";

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

    // Video cells hold a bloom-videoContainer, which is what Bloom's video
    // tooling works on: the Sign Language tool records into it, the hover
    // controls play what it holds, and Bloom's publishing code collects the
    // videos it finds in one. The markup matches what origami's Video link
    // creates, minus the placeholder graphic, which the server is asked for
    // when a video cell is actually made (see wireBloomContentOfNewCells).
    replaceCellContentType(
        "video",
        "<div class='bloom-videoContainer bloom-leadingElement bloom-noVideoSelected'></div>",
        /bloom-videoContainer/,
        false,
    );

    setDefaultCellContentTypeId("text");
}

/**
 * Give the Bloom content of the cells inside `root` the wiring that Bloom
 * normally applies to a whole page as it loads, for the cells that have not had
 * it yet.
 *
 * The library builds cell content itself, and it does so long after the page
 * loaded: a new row or column, every cell of a nested table, a cell whose
 * content type the user changed, and the whole table again after an undo, which
 * restores the table's innerHTML. Bloom's own page-load pass (SetupElements in
 * bloomEditing.ts) is what makes a bloom-editable typable, gives it CKEditor,
 * its language name and long-press, and what wires an image cell's
 * bloom-canvas; content built after that pass has none of it, so until this
 * runs a cell in a new row cannot be typed in at all.
 *
 * Written to be safe to run again on the same cells: an editable that has been
 * through here has a contenteditable attribute, which is what identifies the
 * ones that still need the treatment.
 */
function wireBloomContentOfNewCells(root: HTMLElement): void {
    // `root` is a cell or a table, so everything below it that is a
    // bloom-editable belongs to some cell of some table.
    root.querySelectorAll<HTMLElement>(
        ".bloom-editable:not([contenteditable])",
    ).forEach((editable) => {
        // Must come before attachToCkEditor: CKEditor's inline mode decides
        // whether the editor is read-only from the element's contenteditable
        // state at the moment it attaches.
        editable.setAttribute("contenteditable", "true");
        BloomField.ManageField(editable);
        attachToCkEditor(editable);
        activateLongPressFor($(editable));
    });
    // Gives each cell the little tag naming the language it is in. Run on the
    // whole root because it only looks at editables that are contenteditable,
    // which the ones above have only just become.
    AddLanguageTags(root);
    // `root` itself is one of these when a cell has just become a picture cell.
    const imageCellSelector = ".bloom-cell[data-content-type='image']";
    const imageCells = Array.from(
        root.querySelectorAll<HTMLElement>(imageCellSelector),
    );
    if (root.matches(imageCellSelector)) imageCells.push(root);
    imageCells.forEach((cell) => {
        SetupImagesInContainer(cell);
        // A cell has exactly one picture of its own. The canvas element manager wired every
        // bloom-canvas it found when the page loaded, which was before this one existed, so
        // without this a click on the picture does nothing and the user has no way to reach
        // the menu that would let them choose what picture it shows.
        const bloomCanvas = cell.querySelector<HTMLElement>(".bloom-canvas");
        if (bloomCanvas)
            theOneCanvasElementManager.wireBloomCanvasAddedAfterPageLoad(
                bloomCanvas,
            );
    });
    observeImageCells(root);
    // A cell is usually far too small for the language name and the format cog that Bloom
    // draws inside a text box, and these cells were built after the page-load pass that
    // decides that for every other box on the page.
    observeTranslationGroupSizes(root);
    wireVideoContainersOfNewCells(root);
}

// The video containers this has already wired. SetupVideoEditing adds a click
// handler to the container itself (the one that opens the Sign Language tool)
// without any guard against being called twice, and we run over a whole table
// again after every table operation, so remembering what we have done is what
// keeps one click from opening the tool several times.
const wiredVideoContainers = new WeakSet<HTMLElement>();

/**
 * Give the video container of every video cell within `root` the wiring Bloom's
 * page-load pass would have given it: the play/pause/replay controls that appear
 * on hover, and the click that opens the Sign Language tool to record into it.
 */
function wireVideoContainersOfNewCells(root: HTMLElement): void {
    const containers = Array.from(
        // `root` is a cell or a table, so every video container below it is the
        // content of some cell.
        root.querySelectorAll<HTMLElement>(".bloom-videoContainer"),
    ).filter((container) => !wiredVideoContainers.has(container));
    if (containers.length === 0) return;
    containers.forEach((container) => {
        wiredVideoContainers.add(container);
        // SetupVideoEditing looks for containers inside what it is given, so it
        // gets the cell rather than the container itself.
        SetupVideoEditing(container.parentElement!);
    });
    // The placeholder graphic is not among the files Bloom copies into every
    // book, so the server has to be asked to put this book's copy in place;
    // origami's Video link does the same. Without it the placeholder is missing
    // when the book is opened by anything but Bloom itself.
    post("edit/pageControls/requestVideoPlaceHolder");
}

/** Handle a cell's content being (re)initialised. Attached via SetupTableEditing. */
function onTableCellContentChanged(e: Event): void {
    const custom = e as CustomEvent<{
        cell: HTMLElement;
        contentType: string;
    }>;
    const { cell } = custom.detail;
    // The cell may now hold a text box, a picture, or a whole nested table
    // whose own cells the library filled with Bloom's default cell content, so
    // rather than switch on the content type we wire whatever is in there.
    wireBloomContentOfNewCells(cell);
    relayOutTheCalendarGridOfCell(cell);
}

/**
 * If this cell is a day of a calendar month grid, lay that grid out again.
 *
 * The library rebuilds a cell from its content type's own template when the type changes, and
 * that takes with it the wrapper the calendar keeps the day number in. Laying the grid out
 * again puts both back around whatever the library has just built, so a cell the user has
 * turned into a picture shows its date again straight away rather than at whatever they happen
 * to do next.
 *
 * The layout call is the one that does not ask whether it is needed: nothing the signature is
 * made of has changed, so the grid still says it is up to date.
 */
function relayOutTheCalendarGridOfCell(cell: HTMLElement): void {
    if (!cell.classList.contains("calendarDayCell")) return;
    const grid = cell.closest<HTMLElement>(
        `.bloom-table[${kCalendarMonthAttribute}]`,
    );
    const pageElement = grid?.closest<HTMLElement>(".bloom-page");
    if (!grid || !pageElement) return;
    // The library dispatches this event once its own history entry has closed, saying handlers
    // may safely run further table operations, so the redraw can happen here and now.
    if (layOutGridAgain(grid, pageElement)) RerenderTables(grid);
}

/** Handle the library finishing a table operation. Attached via SetupTableEditing. */
function onTableHistoryUpdated(e: Event): void {
    const table = (
        e as CustomEvent<{ table?: HTMLElement }>
    ).detail?.table?.closest<HTMLElement>(".bloom-table");
    // Deleting a table takes it out of the document, so there may be nothing to
    // wire; and an operation on a nested table hands us that table, while an
    // undo can have restored the cells of the table that holds it. Wiring from
    // the outermost table covers both.
    if (table?.isConnected) wireBloomContentOfNewCells(outermostTable(table));
}

/** The table that holds `table`, and holds no other table itself. */
function outermostTable(table: HTMLElement): HTMLElement {
    let outermost = table;
    let parent = outermost.parentElement?.closest<HTMLElement>(".bloom-table");
    while (parent) {
        outermost = parent;
        parent = outermost.parentElement?.closest<HTMLElement>(".bloom-table");
    }
    return outermost;
}

// The one ResizeObserver watching every image cell on the page, so we can
// disconnect it when we tear table editing down. It is page-wide rather than
// per-table because a nested table's image cells need watching too, and the
// library creates and attaches nested tables itself.
let imageCellObserver: ResizeObserver | undefined;

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
 *
 * The page-wide call handles canvas elements the user placed inside a cell, which keep
 * their position relative to the picture. Each cell's background image then gets fitted
 * on its own, because the page-wide pass would preserve the offsets it was given before
 * the table laid itself out rather than working them out afresh.
 */
function refitImageCellPictures(): void {
    if (refitIsPending) return;
    refitIsPending = true;
    requestAnimationFrame(() => {
        refitIsPending = false;
        if (!theOneCanvasElementManager) return;
        theOneCanvasElementManager.adjustAfterContainerResize();
        document
            .querySelectorAll<HTMLElement>(
                // The second is a calendar day cell, which keeps everything it holds inside a
                // wrapper, so its picture is a grandchild. Both are the cell's own picture; a
                // bloom-canvas deeper than this belongs to a canvas element the user placed,
                // which adjustAfterContainerResize above has already dealt with.
                ".bloom-cell > .bloom-canvas, .bloom-cell > .calendarDayCellContents > .bloom-canvas",
            )
            .forEach((bloomCanvas) => {
                // A cell mid-layout has no area to fit anything to, and asking
                // would only start the picture off from meaningless numbers.
                if (!bloomCanvas.clientWidth || !bloomCanvas.clientHeight)
                    return;
                theOneCanvasElementManager.refitBackgroundImage(bloomCanvas);
            });
    });
}

/** Watch the image cells within `root`, so their pictures follow the cells' size. */
function observeImageCells(root: HTMLElement): void {
    if (!imageCellObserver) {
        imageCellObserver = new ResizeObserver(() => refitImageCellPictures());
    }
    // Every bloom-canvas below a table or a cell is the picture of some cell.
    root.querySelectorAll<HTMLElement>(".bloom-canvas").forEach(
        // Observing one that is already observed does nothing, so cells this
        // has already seen cost nothing on a later pass.
        (bloomCanvas) => imageCellObserver!.observe(bloomCanvas),
    );
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
    // The library dispatches this one on the document rather than on the table,
    // so it is the document that has to listen. Adding the same function again
    // is a no-op, which matters because SetupElements (and so this) runs again
    // on a subtree whenever a canvas element is added.
    container.ownerDocument.addEventListener(
        kTableHistoryUpdatedEvent,
        onTableHistoryUpdated,
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
 * Attach a single newly-created bloom-table that already holds all its cells and carries its
 * own row and column sizes: a calendar month grid, which buildCalendarGridTable has filled in
 * and laid out. Called from addCalendarGridCanvasElement in CanvasElementFactories.ts.
 *
 * Unlike AttachNewTableThatFillsItsSpace, this adds no rows or columns: the grid has its seven
 * columns and its day rows already, and asking for more would put them on the end of it.
 */
export function AttachNewCalendarGrid(tableDiv: HTMLElement): void {
    ensureContentTypesRegistered();
    attachSingleTable(tableDiv);
    // Our own code built these cells rather than the library, so they have had none of
    // Bloom's editing wiring. This is what makes the weekday names and the day notes
    // typable, and gives them CKEditor and their language tags.
    wireBloomContentOfNewCells(tableDiv);
}

/**
 * Draw `container` and every bloom-table within it again, after something outside the library
 * changed the attributes a table's appearance comes from (its borders, its row and column
 * sizes). The library renders a table when it attaches it and after each of its own
 * operations, and exports no renderer of its own, so re-attaching is how a caller asks for a
 * fresh render. Used by the calendar code, which rewrites a month grid's borders as the user
 * arrives at its page or changes what the grid shows; that caller hands us the grid itself,
 * which is why the container counts as one of the tables to draw.
 */
export function RerenderTables(container: HTMLElement): void {
    const tableDivs = Array.from(
        container.querySelectorAll<HTMLElement>(".bloom-table"),
    );
    if (container.classList.contains("bloom-table"))
        tableDivs.unshift(container);
    tableDivs.forEach((tableDiv) => {
        detachTable(tableDiv);
        attachTable(tableDiv);
    });
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
    container.ownerDocument.removeEventListener(
        kTableHistoryUpdatedEvent,
        onTableHistoryUpdated,
    );
    imageCellObserver?.disconnect();
    imageCellObserver = undefined;
    // Every table, not only the ones we attached: the library attaches a nested
    // table itself, both when the user makes one and when an undo rebuilds it,
    // so a nested table has no data-table-attached of ours to find it by.
    container
        .querySelectorAll<HTMLElement>(".bloom-table")
        .forEach((tableDiv) => {
            tableDiv.removeAttribute("data-table-attached");
            detachTable(tableDiv);
        });
}
