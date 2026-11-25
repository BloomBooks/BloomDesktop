import {
    addColumn,
    addRow,
    attachTable,
    defaultCellContentsForEachType,
    detachTable,
    getTableInfo,
    registerCellContentType,
    removeTableEditingMarkup,
    setColumnWidth,
    setDefaultCellContentTypeId,
    setGetLocalizations,
    setRowHeight,
    setCellMenuOpenHandler,
    setHostCellSettings,
    setHostMenuSectionProvider,
    setStructuralChromeGate,
    kTableCellContentChangedEvent,
} from "bloom-table";
// Edit-only table styles (selection highlight, boundary hints). These must NOT
// reach published output, so they are loaded here in the editing context rather
// than via basePage.less. This injects into the page iframe (this module is part
// of editablePageBundle). The structural/read-time styles come from
// bloom-table.css, which basePage.less inlines so they ship everywhere.
import "bloom-table/bloom-table-edit.css";
import "./tableEditing.less";
import { SetupImagesInContainer } from "./bloomImages";
import { SetupVideoEditing } from "./bloomVideo";
import {
    languageClassesOf,
    setUpBoxesBuiltAfterLoad,
} from "./translationGroupLanguages";
import { get, post } from "../../utils/bloomApi";
import OverflowChecker from "../OverflowChecker/OverflowChecker";
import {
    addRequestPageContentDelay,
    removeRequestPageContentDelay,
} from "./pageContentDelays";
import { theOneCanvasElementManager } from "./canvasElementManager/CanvasElementManager";
import {
    refreshTableFeatureStatus,
    tablesMayBeRestructured,
} from "./tableFeature";
import { noteTableHistoryUpdate } from "../undo/tableUndo";
import { ILanguageNameValues } from "../toolbox/canvas/canvasControlTypes";
import {
    cellLanguagesOfCollection,
    ICellLanguage,
    showSettingInGroup,
    textGroupOfCell,
} from "./tableCellLanguages";
import { TableCellLanguagesSection } from "./TableCellLanguagesSection";
import { createElement } from "react";
import { getLocalizations } from "../../lib/localizationManager/getLocalizations";
import { tableInTextMayShowItsButtons } from "./tablesInText";

// The library's own "something changed in a table" notification. It fires on the
// page's document at the end of every operation that goes through the table's
// history (adding a row or column, changing a cell's content type, pasting or
// deleting a table, an undo or a redo), which is exactly when new cell content
// may need Bloom's wiring.
const kTableHistoryUpdatedEvent = "tableHistoryUpdated";

let tableLibrarySetUp = false;

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

/**
 * Set the bloom-table library up for this page: register Bloom's cell content
 * types, install Bloom's hooks, and have it show its UI in the user's UI language.
 * Done when the first table is attached, so a page without tables costs nothing.
 */
function ensureTableLibrarySetUp(): void {
    if (tableLibrarySetUp) return;
    tableLibrarySetUp = true;

    // Text cells hold a bloom-translationGroup rather than the library's bare
    // contenteditable, so text in a table participates in Bloom's multilingual
    // system and its styles. The group gets one bloom-editable of its own here,
    // in the collection's first language, because nothing else would give the
    // cell one until the page is next loaded: TranslationGroupManager (C#) adds
    // the editables for the book's other languages then. Without that first
    // editable a new cell has nothing to type in, and so no format button and no
    // language name either. This is what a text canvas element does too; see
    // makeTranslationGroup in CanvasElementFactories.ts.
    // The group shows the collection's first language only (data-default-languages CL1),
    // whatever the toolbar shows, until the user picks others in the table's menus; see
    // tableCellLanguages.ts. Its box has the classes page load would give it (languageClassesOf):
    // bloom-content1, for instance, only when that language is also the toolbar's first, which is
    // what the reader tools take bloom-content1 to mean.
    const collectionLanguage1 = GetSettings().collectionLanguage1;
    const languageClasses = languageClassesOf(collectionLanguage1).join(" ");
    replaceCellContentType(
        "text",
        "<div class='bloom-translationGroup bloom-trailingElement normal-style' data-default-languages='CL1'>" +
            `<div class='bloom-editable normal-style ${languageClasses} bloom-visibility-code-on' lang='${collectionLanguage1}'><p></p></div>` +
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
    installHostHooks();

    // The library asks the server for the translations of every string it shows,
    // the content type names registered above among them, and holds its UI back
    // until they arrive.
    void setGetLocalizations(getLocalizations);
}

/**
 * Tell the bloom-table library how much of a table's editing Bloom is offering.
 *
 * A book may hold a table that the user is not entitled to make: the subscription
 * is below Pro, or the table feature has stopped being visible since the table was
 * made. Bloom's rule for such a book is the one it uses for canvas elements —
 * localize, don't create — so the table stays attached and its text, language tags,
 * format gear, picture replacement and audio all keep working, while everything
 * that would create or restructure a table is withheld.
 *
 * Two hooks do that between them:
 * - The structural chrome gate withholds the "+" edge buttons, the row and column
 *   pills and the table pill.
 * - The cell menu open handler withholds the Cell menu, which is where content
 *   type, merge and split live. It is a separate hook because the gate deliberately
 *   leaves the Cell menu alone (a host that lays a table out itself still wants the
 *   cell formatting). Answering true means "the host opened a menu itself, open
 *   none": the library opens nothing and, since it does not stop the event, the
 *   right-click goes on to Bloom's own text context menu, which is what a
 *   right-click in a frozen table should show.
 */
function installHostHooks(): void {
    setStructuralChromeGate(
        (table) =>
            tablesMayBeRestructured() && tableInTextMayShowItsButtons(table),
    );
    setCellMenuOpenHandler(() => !tablesMayBeRestructured());
    // The Languages item of the Cell, Row, Column and Table menus, for the text cells the menu
    // acts on. A menu over no text cell has none.
    setHostMenuSectionProvider((_scope, cells) => {
        if (!cellLanguages) return undefined;
        const groups = cells
            .map(textGroupOfCell)
            .filter((group): group is HTMLElement => !!group);
        if (groups.length === 0) return undefined;
        return createElement(TableCellLanguagesSection, {
            groups,
            languages: cellLanguages,
        });
    });
    // A text cell's languages travel with the cell's other settings: into a new row or column
    // from the one it was inserted beside, and by Paint format and Copy / Paste properties.
    setHostCellSettings({
        // The setting is copied as stored, so a cell with none (it shows the toolbar's languages)
        // gives the cell it is copied to none either.
        snapshot: (cell) => {
            const group = textGroupOfCell(cell);
            if (!group) return undefined;
            return storedSettingOf(group);
        },
        apply: (cell, value) => {
            const group = textGroupOfCell(cell);
            // undefined: the cell copied from is not a text cell, so it has no languages to give.
            if (!group || value === undefined) return;
            // Pasting over many cells mostly gives a cell the setting it already has.
            if (storedSettingOf(group) === value) return;
            const setting = value === kNoSetting ? null : value;
            // Every operation that copies settings (a new row or column, Paste properties, Paint
            // format) is followed by the wiring pass, which sets up the boxes this adds and tags
            // the ones it shows again, all at once rather than once per cell. That is also the
            // only time new boxes can be set up: a new row gets its settings before it is in the
            // page, where a box's direction cannot be read.
            showSettingInGroup(group, setting);
        },
    });
}

// What setHostCellSettings carries for a text cell with no data-default-languages. The library
// uses undefined for a cell with nothing to copy, so "no setting" needs a value of its own.
const kNoSetting = "";

// A text cell's language setting as setHostCellSettings carries it.
function storedSettingOf(group: HTMLElement): string {
    return group.getAttribute("data-default-languages") ?? kNoSetting;
}

// The collection's languages, with their names, as the table's Languages menus list them. Asked for
// as the page loads, so that they are there by the time a table added later opens its menus; until
// they arrive, the menus have no Languages item. (Showing a cell's languages needs only their tags,
// which the page's settings already hold; see showSettingInGroup.)
let cellLanguages: ICellLanguage[] | undefined;
let cellLanguagesRequested = false;

// SetupTableEditing runs again whenever a canvas element is added; the languages cannot change
// within one page load (this module lives in the page iframe), so they are asked for once.
function requestCellLanguages(): void {
    if (cellLanguagesRequested) return;
    cellLanguagesRequested = true;
    get("settings/languageNames", (result) => {
        cellLanguages = cellLanguagesOfCollection(
            result.data as ILanguageNameValues,
        );
    });
}

/**
 * Give the Bloom content of the cells inside `roots` (cells or tables) the wiring
 * that Bloom normally applies to a whole page as it loads, for the cells that have
 * not had it yet. The boxes of all of them are set up in one pass, so the page is
 * measured once however many roots there are.
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
function wireBloomContentOfNewCells(allRoots: HTMLElement[]): void {
    // A table made in Change Layout mode is not editable there, like the
    // mode's text boxes, which origami makes not contenteditable. Leaving the
    // mode rebuilds the page, and the page-load pass wires these cells then.
    // A root inside another (a cell of a nested table, announced along with the cell that holds
    // it) is covered by that one, and would otherwise have its boxes set up twice.
    const roots = allRoots.filter(
        (root) =>
            !root.closest(".origami-layout-mode") &&
            !allRoots.some((other) => other !== root && other.contains(root)),
    );
    if (roots.length === 0) return;
    const within = (selector: string) =>
        roots.flatMap((root) =>
            Array.from(root.querySelectorAll<HTMLElement>(selector)),
        );
    // Each root is a cell or a table, so everything below it that is a
    // bloom-editable belongs to some cell of some table.
    const newEditables = within(".bloom-editable:not([contenteditable])");
    // Each new box gets what page load gives every box: editing, CKEditor, its language tag, its
    // group's inline pictures and its direction. Every other box in the roots that is showing gets
    // its language tag again, in the same pass: an operation can change a cell's width, which
    // decides whether its box has a tag, and a copied setting can show a box that was hidden.
    // (A hidden box needs no tag, and measuring one makes jQuery lay out the page for it alone.)
    const oldEditables = within(
        ".bloom-editable.bloom-visibility-code-on",
    ).filter((editable) => !newEditables.includes(editable));
    setUpBoxesBuiltAfterLoad(newEditables, oldEditables);
    // A root is itself one of these when a cell has just become a picture cell.
    const imageCellSelector = ".bloom-cell[data-content-type='image']";
    const imageCells = [
        ...within(imageCellSelector),
        ...roots.filter((root) => root.matches(imageCellSelector)),
    ];
    imageCells.forEach((cell) => {
        SetupImagesInContainer(cell);
        // A picture cell made after the page loaded also needs the drawing
        // surface every other picture got when canvas editing started;
        // without it the surface of the canvas the table sits on takes the
        // press on the picture.
        cell.querySelectorAll<HTMLElement>(":scope > .bloom-canvas").forEach(
            (bloomCanvas) =>
                theOneCanvasElementManager?.startEditingNewBloomCanvas(
                    bloomCanvas,
                ),
        );
    });
    roots.forEach((root) => {
        observeImageCells(root);
        wireVideoContainersOfNewCells(root);
    });
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

// Cells whose content the library has (re)built during the current operation, not yet wired. An
// operation announces each cell of a new row or column on its own; wiring them together lets the
// page be measured once for all of them rather than once per cell.
const cellsAwaitingWiring = new Set<HTMLElement>();

/** Handle a cell's content being (re)initialised. Attached via SetupTableEditing. */
function onTableCellContentChanged(e: Event): void {
    const custom = e as CustomEvent<{
        cell: HTMLElement;
        contentType: string;
    }>;
    // The cell may now hold a text box, a picture, or a whole nested table
    // whose own cells the library filled with Bloom's default cell content, so
    // rather than switch on the content type we wire whatever is in there.
    if (cellsAwaitingWiring.size === 0) {
        // The operation's history update wires the whole table, these cells included; if it has
        // not come by the end of this task, they are wired here.
        queueMicrotask(wireCellsAwaitingWiring);
    }
    cellsAwaitingWiring.add(custom.detail.cell);
}

// Wire the cells onTableCellContentChanged collected, together.
function wireCellsAwaitingWiring(): void {
    const cells = Array.from(cellsAwaitingWiring).filter(
        (cell) => cell.isConnected,
    );
    cellsAwaitingWiring.clear();
    wireBloomContentOfNewCells(cells);
}

/** Handle the library finishing a table operation. Attached via SetupTableEditing. */
function onTableHistoryUpdated(e: Event): void {
    const detail = (
        e as CustomEvent<{ table?: HTMLElement; operation?: string }>
    ).detail;
    const table = detail?.table?.closest<HTMLElement>(".bloom-table");
    // Deleting a table takes it out of the document, so there may be nothing to
    // wire; and an operation on a nested table hands us that table, while an
    // undo can have restored the cells of the table that holds it. Wiring from
    // the outermost table covers both.
    if (table?.isConnected) {
        const outermost = outermostTable(table);
        // Wiring the table covers the cells the operation announced in it.
        cellsAwaitingWiring.forEach((cell) => {
            if (outermost.contains(cell)) cellsAwaitingWiring.delete(cell);
        });
        wireBloomContentOfNewCells([outermost]);
        observeCellSizes(outermost);
        // An operation can change how much room a cell's text has without any cell changing
        // size (a row changed from Hug to Grow that already had that height), so the
        // overflow marks are checked here as well as when a cell resizes.
        checkTableTextAtNextFrame([outermost]);
    } else {
        stopObservingCellsNotInPage();
    }
    // After the wiring, not before: wiring attaches CKEditor to the cells the operation built,
    // and an attach can be reported as a CKEditor change. Recording the table's change last
    // leaves the table as the more recent of the two, which is what it is.
    noteTableHistoryUpdate(detail?.operation);
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
            .querySelectorAll<HTMLElement>(".bloom-cell > .bloom-canvas")
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

// The one ResizeObserver watching every table cell on the page, so the overflow marks follow
// a row or column while it is dragged. The library reports a resize only when the drag ends.
// Page-wide for the same reason as imageCellObserver.
let cellSizeObserver: ResizeObserver | undefined;

// The cells cellSizeObserver watches, so that it can stop watching the ones an operation or an
// undo took out of the page: an observer keeps every element it watches alive.
const cellsWhoseSizeIsObserved = new Set<HTMLElement>();

// The outermost tables whose text waits for the next animation frame to be checked, so that a
// drag that resizes a whole column, or an operation that also resizes cells, gets one check per
// frame rather than one per cell or per cause.
const tablesAwaitingOverflowCheck = new Set<HTMLElement>();

// The page-content delay that the overflow checks of tables hold, since a check can change the
// height of a canvas element or the padding of a text box, which are saved with the page.
const kTableOverflowCheckDelayId = "tableOverflowCheck";

// Whether the overflow checks of tables hold kTableOverflowCheckDelayId. A drag checks on every
// frame, and each release of the delay makes the page send C# a snapshot, so the delay is held
// from the first check until a frame passes with no check waiting.
let holdingTableOverflowCheckDelay = false;

/**
 * Update the overflow marks of every text block in `tables` and of the page around them, at the
 * next frame, together with any other tables already waiting. Called whenever a cell changes
 * size, which is on every frame of a row or column drag, so it uses AdjustSizeOrMarkOverflowOfAll
 * rather than AdjustSizeOrMarkOverflowSoon, whose one-second wait would leave the marks behind
 * the drag.
 */
function checkTableTextAtNextFrame(tables: HTMLElement[]): void {
    const wasIdle = tablesAwaitingOverflowCheck.size === 0;
    tables.forEach((table) =>
        tablesAwaitingOverflowCheck.add(outermostTable(table)),
    );
    if (!wasIdle || tablesAwaitingOverflowCheck.size === 0) return;
    if (!holdingTableOverflowCheckDelay) {
        holdingTableOverflowCheckDelay = true;
        addRequestPageContentDelay(kTableOverflowCheckDelayId);
    }
    requestAnimationFrame(() => {
        try {
            const waiting = Array.from(tablesAwaitingOverflowCheck).filter(
                (table) => table.isConnected,
            );
            tablesAwaitingOverflowCheck.clear();
            OverflowChecker.AdjustSizeOrMarkOverflowOfAll(
                waiting.flatMap((table) =>
                    Array.from(
                        table.querySelectorAll<HTMLElement>(
                            ".bloom-editable.bloom-visibility-code-on",
                        ),
                    ),
                ),
            );
        } finally {
            requestAnimationFrame(releaseTableOverflowCheckDelayIfIdle);
        }
    });
}

/** Release kTableOverflowCheckDelayId, unless another overflow check of a table is waiting. */
function releaseTableOverflowCheckDelayIfIdle(): void {
    if (!holdingTableOverflowCheckDelay || tablesAwaitingOverflowCheck.size > 0)
        return;
    holdingTableOverflowCheckDelay = false;
    removeRequestPageContentDelay(kTableOverflowCheckDelayId);
}

/** Check the text of the tables that hold the cells in `entries`, at the next frame. */
function onCellsResized(entries: ResizeObserverEntry[]): void {
    checkTableTextAtNextFrame(
        entries
            .map((entry) => entry.target.closest<HTMLElement>(".bloom-table"))
            .filter((table): table is HTMLElement => !!table),
    );
}

/** Stop watching the size of the cells that an operation or an undo took out of the page. */
function stopObservingCellsNotInPage(): void {
    cellsWhoseSizeIsObserved.forEach((cell) => {
        if (!cell.isConnected) {
            cellSizeObserver?.unobserve(cell);
            cellsWhoseSizeIsObserved.delete(cell);
        }
    });
}

/**
 * Watch the size of every cell within `root`, so its text is checked for overflow as it resizes,
 * and stop watching the cells that are no longer in the page.
 */
function observeCellSizes(root: HTMLElement): void {
    if (!cellSizeObserver) {
        cellSizeObserver = new ResizeObserver(onCellsResized);
    }
    const observer = cellSizeObserver;
    stopObservingCellsNotInPage();
    // Observing a cell that is already observed does nothing.
    root.querySelectorAll<HTMLElement>(".bloom-cell").forEach((cell) => {
        observer.observe(cell);
        cellsWhoseSizeIsObserved.add(cell);
    });
}

function attachSingleTable(tableDiv: HTMLElement): void {
    if (tableDiv.hasAttribute("data-table-attached")) return;
    tableDiv.setAttribute("data-table-attached", "1");
    ensureTableLibrarySetUp();
    attachTable(tableDiv);
    observeImageCells(tableDiv);
    observeCellSizes(tableDiv);
}

/**
 * Wire table editing for the whole page. Called from SetupElements in
 * bloomEditing.ts on every page load. Attaches the cell-content-changed
 * event listener to the container and calls attachTable on every bloom-table
 * found inside it.
 */
export function SetupTableEditing(container: HTMLElement): void {
    // Asked for once per page load and remembered, because the hooks that
    // ensureTableLibrarySetUp installs have to answer during an event they cannot await. Nothing waits for
    // it: until it arrives the answer is "no", which is the safe way round, since
    // it withholds chrome rather than showing chrome that then has to disappear.
    void refreshTableFeatureStatus();
    requestCellLanguages();
    // Both listeners go on the document, not on `container`. SetupElements (and
    // so this) runs again on a subtree whenever a canvas element is added, and a
    // listener on that subtree would hear a cell's event a second time on its
    // way up to the document, wiring the cell twice. Adding the same function to
    // the document again is a no-op. (The cell-content event is dispatched on
    // the cell and bubbles; the history event is dispatched on the document.)
    container.ownerDocument.addEventListener(
        kTableCellContentChangedEvent,
        onTableCellContentChanged,
    );
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
    attachSingleTable(tableDiv);
}

/**
 * Attach a newly-created bloom-table, with the library's own default sizing, and give its cells'
 * content the setup that page load gives every text box. Called by insertTableInText
 * (tablesInText.ts) for a table put into the text of a text box. A table made with the Table link
 * gets that setup when the page is loaded again on leaving Change Layout, and one in a canvas
 * element when the canvas element is set up, but nothing comes after Insert Table to do it.
 * A pasted table (pasteTableIntoText) can hold tables in its cells, and they are attached too, as
 * page load attaches every table.
 */
export function AttachNewTableAndSetUpItsCells(tableDiv: HTMLElement): void {
    attachSingleTable(tableDiv);
    tableDiv
        .querySelectorAll<HTMLElement>(".bloom-table")
        .forEach((nested) => attachSingleTable(nested));
    wireBloomContentOfNewCells([tableDiv]);
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
    // The rows and columns added below get Bloom's cell content.
    ensureTableLibrarySetUp();
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
 * Take table editing's markup out of `clonedPage`, the detached copy of the page that is about to
 * be saved, leaving the live tables as they are. A save happens whenever the page changes, so it
 * must not end Paint Format or Border Brush mode, which bloom-table's removeTableEditingArtifacts
 * does before stripping. So this uses the library's markup-only part, removeTableEditingMarkup,
 * and adds our own data-table-attached.
 */
export function removeTableEditingMarkupFromClone(
    clonedPage: HTMLElement,
): void {
    removeTableEditingMarkup(clonedPage);
    clonedPage
        .querySelectorAll<HTMLElement>(".bloom-table")
        .forEach((table) => table.removeAttribute("data-table-attached"));
}

/**
 * Detach table editing from all bloom-table elements within `container`.
 * The editor itself never needs this: a page is left by loading a new document, which takes the
 * listeners with it, and a save works on a clone (removeTableEditingMarkupFromClone). Tests use it
 * to start each test afresh.
 */
export function TeardownTableEditing(container: HTMLElement): void {
    container.ownerDocument.removeEventListener(
        kTableCellContentChangedEvent,
        onTableCellContentChanged,
    );
    container.ownerDocument.removeEventListener(
        kTableHistoryUpdatedEvent,
        onTableHistoryUpdated,
    );
    imageCellObserver?.disconnect();
    imageCellObserver = undefined;
    cellSizeObserver?.disconnect();
    cellSizeObserver = undefined;
    cellsWhoseSizeIsObserved.clear();
    tablesAwaitingOverflowCheck.clear();
    releaseTableOverflowCheckDelayIfIdle();
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
