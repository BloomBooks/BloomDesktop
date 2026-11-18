// Drive and measure a table on a Bloom page.
//
// A table is a canvas element (see canvasElements.ts) holding a CSS grid. There are no row
// elements: every cell is a direct child of the table, in row-major order, and the number of rows
// and columns is recorded in the table's own `data-row-heights` and `data-column-widths`. So a
// cell's row and column are arithmetic, and this file does that arithmetic once so that no test
// has to.
//
// All the editing chrome comes from the bloom-table library, which appends it to the page's BODY
// rather than putting it inside the table, and tags every piece `data-table-overlay`. That is
// deliberate on the library's side and it is what makes the chrome findable here: the "+" buttons
// and the row, column and table pills carry stable attributes and roles, so nothing below matches
// on an English label.
//
// The two exceptions, both noted where they occur:
//
//  - The library's own menu commands ("Delete Row" and the rest) are identified by their aria-label,
//    which is English because the library does not localize. That IS the library's contract today,
//    so a union type below lists the labels this suite uses; a typo is then a compile error rather
//    than a timeout.
//  - A row or column boundary has no element at all. The library hit-tests the pointer against the
//    cell's own edge, within five pixels, so a resize here is a drag aimed by geometry.
//
// Measurement follows the rule in geometry.ts: relative, never absolute. Nothing here returns or
// asserts a pixel size as a number a test compares with a constant.

import {
    expect,
    type Frame,
    type Locator,
    type Page,
    type Request,
} from "@playwright/test";
import { holdBackAnswers, type IHeldBackAnswers } from "./api";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
} from "./bookMaking";
import { kTableEditingMarkupSelectors } from "./bookHtml";
import {
    dragPaletteItemOntoCanvas,
    getCanvasRect,
    openCanvasTool,
} from "./canvasElements";
import {
    bottom,
    describeRect,
    expectInside,
    type IRect,
    kEdgeTolerance,
    right,
    sameEdge,
} from "./geometry";
import { realClick } from "./realClick";
import { undo } from "./undo";

// How long a table helper waits for the page to show something. Everything waited for here is
// done by the page's own script in a few milliseconds, so a wait that runs out is a failure, and a
// long timeout only delays the report of it.
const kTableWaitMs = 5000;

/** What a cell holds, as the table records it in `data-content-type`. */
export type CellContentType = "text" | "image" | "video" | "table";

/**
 * The library's menu commands, by the aria-label each carries. English, because the bloom-table
 * library does not localize its own menus yet; this list is what makes that visible rather than
 * scattered through the specs. (AUTOMATION-DEBT.md: "bloom-table menu labels are English only".)
 */
export type TableMenuCommand =
    | "Add Row Above"
    | "Add Row Below"
    | "Move Row Up"
    | "Duplicate Row"
    | "Delete Row"
    | "Add Column Left"
    | "Add Column Right"
    | "Move Left"
    | "Duplicate Column"
    | "Delete Column"
    | "Copy properties"
    | "Paste properties"
    | "Paint format"
    | "Copy Table"
    | "Cut Table"
    | "Delete Table";

/** Which edge's "+" button to press, by the aria-label the library gives it. */
const ADD_BUTTON_LABEL = {
    row: "Add row at the bottom edge",
    column: "Add column at the right edge",
} as const;

/** The table's own shape, as the table element records it. */
export interface ITableShape {
    rows: number;
    columns: number;
    /** One entry per column: "fill", "hug", "fit", or a px measure once a boundary has been dragged. */
    columnWidths: string[];
    /** One entry per row, in the same vocabulary. */
    rowHeights: string[];
}

/** One cell: where it is in the grid, what it holds, and where it is drawn. */
export interface ICellInfo {
    row: number;
    column: number;
    contentType: CellContentType | "";
    rect: IRect;
    /** How many columns the cell spans; more than 1 means it has been merged rightwards. */
    spanX: number;
    /** How many rows the cell spans. */
    spanY: number;
}

/** A table as measured on screen: its own rect and its cells'. */
export interface ITableMeasurement {
    rect: IRect;
    cells: ICellInfo[];
    shape: ITableShape;
}

/** Which menus are showing. A right-click should raise exactly one of them. */
export interface IOpenMenus {
    /** The table library's Cell menu, from a right-click in a cell. */
    cell: boolean;
    row: boolean;
    column: boolean;
    table: boolean;
    /** The canvas element's own "..." menu, with Duplicate and Delete. */
    canvasElement: boolean;
    /** Bloom's text menu, which a right-click in ordinary text raises. */
    bloomText: boolean;
}

// ── Making a table ──────────────────────────────────────────────────────

/**
 * Make a Basic Book with one Canvas page, show `contentLanguages` on the toolbar, and drag a new
 * table onto the canvas, into its upper left so that the table and the toolbar Bloom draws under a
 * selected element both stay on the page. Returns when Bloom has attached the table, with the
 * Canvas page showing.
 */
export async function makeBookWithTableOnCanvasPage(
    page: Page,
    contentLanguages: string[],
): Promise<void> {
    await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Canvas");
    const [canvasPage] = await getContentPages(page);
    await goToPage(page, canvasPage.id);
    await setContentLanguages(page, contentLanguages);
    await openCanvasTool(page);
    await dragPaletteItemOntoCanvas(page, "table", {
        xFraction: 0.3,
        yFraction: 0.25,
    });
    await waitForTableAttached(page);
}

// ── Finding tables and cells ────────────────────────────────────────────

/** Every table on the page being edited, in document order. */
export function tables(page: Page): Locator {
    return editablePageFrame(page).locator(".bloom-table");
}

/** How many tables the page being edited holds. */
export async function getTableCount(page: Page): Promise<number> {
    return tables(page).count();
}

/** The nth table on the page being edited, counting from 0. */
export function table(page: Page, tableIndex = 0): Locator {
    return tables(page).nth(tableIndex);
}

/**
 * Wait until Bloom has attached its editing behaviour to the table. Until it has, a click on a
 * cell raises no chrome, and a test that clicked too early fails on the chrome rather than on
 * whatever it was measuring.
 */
export async function waitForTableAttached(
    page: Page,
    tableIndex = 0,
): Promise<void> {
    await expect(
        table(page, tableIndex),
        `Bloom never attached its table editing to table ${tableIndex}.`,
    ).toHaveAttribute("data-table-attached", "1", { timeout: kTableWaitMs });
}

/**
 * Wait until the table library has wired the table at `tableIndex`, which for a table inside a
 * cell is what has to be waited for instead of `waitForTableAttached`.
 *
 * Bloom's own marker, `data-table-attached`, goes only on the tables Bloom attaches itself, and it
 * attaches the top-level ones: a table inside a cell is created and attached by the library (see
 * the note on imageCellObserver in tableEditing.ts), so it never carries the marker however long a
 * test waits. What it does carry, once the library has it, is the column and row size lists the
 * library writes.
 */
export async function waitForNestedTableAttached(
    page: Page,
    tableIndex: number,
): Promise<void> {
    const target = table(page, tableIndex);
    await expect
        .poll(
            async () => {
                await target.waitFor({
                    state: "attached",
                    timeout: kTableWaitMs,
                });
                return target.evaluate(
                    (element) =>
                        !!element.getAttribute("data-column-widths") &&
                        !!element.getAttribute("data-row-heights"),
                );
            },
            {
                timeout: kTableWaitMs,
                message:
                    `The table library never wired table ${tableIndex}: it has no column or row ` +
                    `sizes of its own.`,
            },
        )
        .toBe(true);
}

/** The table's shape, read from the attributes the table itself carries. */
export async function getTableShape(
    page: Page,
    tableIndex = 0,
): Promise<ITableShape> {
    const target = table(page, tableIndex);
    await target.waitFor({ state: "attached", timeout: kTableWaitMs });
    return target.evaluate((element) => {
        // An empty attribute means no columns yet, and "".split(",") would wrongly say one.
        const list = (name: string) => {
            const value = element.getAttribute(name) ?? "";
            return value === "" ? [] : value.split(",");
        };
        const columnWidths = list("data-column-widths");
        const rowHeights = list("data-row-heights");
        return {
            rows: rowHeights.length,
            columns: columnWidths.length,
            columnWidths,
            rowHeights,
        };
    });
}

/**
 * One cell of a table, by its row and column, counting from 0. Throws a message naming the table's
 * shape when the row or column is outside it, which is a far more useful failure than a timeout on
 * an empty locator.
 */
export async function cell(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<Locator> {
    const shape = await getTableShape(page, tableIndex);
    if (row >= shape.rows || column >= shape.columns || row < 0 || column < 0)
        throw new Error(
            `Table ${tableIndex} has ${shape.rows} rows and ${shape.columns} columns, so there is ` +
                `no cell at row ${row}, column ${column}.`,
        );
    return table(page, tableIndex)
        .locator("> .bloom-cell")
        .nth(row * shape.columns + column);
}

/** What one cell holds, as the table records it. */
export async function getCellContentType(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<string> {
    const target = await cell(page, row, column, tableIndex);
    return (await target.getAttribute("data-content-type")) ?? "";
}

// ── Measuring ───────────────────────────────────────────────────────────

/**
 * Measure the table and every cell in it, in the coordinates Playwright's own mouse uses, so that
 * these rects can be compared with those of anything else on the page.
 *
 * One round trip rather than one per cell: measuring a large table's cells one at a time both takes longer and risks reading them at different moments, which for a grid
 * mid-relayout would produce a shape that never existed.
 */
export async function measureTable(
    page: Page,
    tableIndex = 0,
): Promise<ITableMeasurement> {
    const frameOffset = await pageFrameOffset(page);
    const measurement = await measureTableIn(
        editablePageFrame(page),
        tableIndex,
    );
    const shift = (rect: IRect): IRect => ({
        ...rect,
        x: rect.x + frameOffset.x,
        y: rect.y + frameOffset.y,
    });
    return {
        rect: shift(measurement.rect),
        shape: measurement.shape,
        cells: measurement.cells.map((c) => ({ ...c, rect: shift(c.rect) })),
    };
}

/**
 * Measure a table in any frame, in that frame's own coordinates. Use this for a table Playwright's
 * mouse will not touch: the one in the BloomPUB preview, say, where the question is only whether
 * the cells tile each other.
 */
export async function measureTableIn(
    frame: Frame,
    tableIndex = 0,
    tableSelector = ".bloom-table",
): Promise<ITableMeasurement> {
    const target = frame.locator(tableSelector).nth(tableIndex);
    await target.waitFor({ state: "attached", timeout: kTableWaitMs });
    const shape = await target.evaluate((element) => {
        const list = (name: string) => {
            const value = element.getAttribute(name) ?? "";
            return value === "" ? [] : value.split(",");
        };
        const columnWidths = list("data-column-widths");
        const rowHeights = list("data-row-heights");
        return {
            rows: rowHeights.length,
            columns: columnWidths.length,
            columnWidths,
            rowHeights,
        };
    });
    const measured = await target.evaluate((element, columns: number) => {
        const asRect = (box: DOMRect) => ({
            x: box.x,
            y: box.y,
            width: box.width,
            height: box.height,
        });
        const cells = [...element.querySelectorAll(":scope > .bloom-cell")].map(
            (cellElement, index) => ({
                row: columns > 0 ? Math.floor(index / columns) : 0,
                column: columns > 0 ? index % columns : index,
                contentType:
                    cellElement.getAttribute("data-content-type") ?? "",
                covered: cellElement.classList.contains("bloom-skip"),
                spanX: Number(cellElement.getAttribute("data-span-x") ?? "1"),
                spanY: Number(cellElement.getAttribute("data-span-y") ?? "1"),
                rect: asRect(cellElement.getBoundingClientRect()),
            }),
        );
        return { rect: asRect(element.getBoundingClientRect()), cells };
    }, shape.columns);
    return {
        rect: measured.rect,
        shape,
        cells: measured.cells
            .filter((c) => !c.covered)
            .map((c) => ({
                row: c.row,
                column: c.column,
                contentType: c.contentType as CellContentType | "",
                rect: c.rect,
                spanX: c.spanX,
                spanY: c.spanY,
            })),
    };
}

/**
 * Assert that the cells tile the table: each row's cells share a top and a height, each column's
 * share a left and a width, neighbours are separated by the same gap all through the grid, and
 * every cell lies inside the table.
 *
 * This is the geometric statement of "the table looks right" that survives any window size, zoom
 * or display scaling. It refuses to judge a grid with a merged cell, because a span breaks the
 * row-and-column arithmetic it rests on; assert on such a table's cells individually.
 */
export async function expectCellsTile(
    page: Page,
    tableIndex = 0,
): Promise<ITableMeasurement> {
    const measurement = await measureTable(page, tableIndex);
    expectTilingOf(measurement, tableIndex);
    return measurement;
}

/**
 * Assert that a measured table's cells tile it. Separated from expectCellsTile so that the same
 * judgement can be made of a table in any frame, however it was measured.
 */
export function expectTilingOf(
    measurement: ITableMeasurement,
    tableIndex = 0,
): void {
    const spanned = measurement.cells.filter((c) => c.spanX > 1 || c.spanY > 1);
    if (spanned.length > 0)
        throw new Error(
            `Table ${tableIndex} has ${spanned.length} merged cell(s), so its cells do not form a ` +
                `plain grid and expectCellsTile cannot judge it. Measure the cells you care about ` +
                `with measureTable instead.`,
        );
    const { rect, cells, shape } = measurement;
    expect(
        cells.length,
        `Table ${tableIndex} says it is ${shape.rows} by ${shape.columns}, so it should have ` +
            `${shape.rows * shape.columns} cells.`,
    ).toBe(shape.rows * shape.columns);

    const problems: string[] = [];
    const at = (row: number, column: number) =>
        cells.find((c) => c.row === row && c.column === column)!;

    for (let row = 0; row < shape.rows; row++)
        for (let column = 1; column < shape.columns; column++) {
            const first = at(row, 0);
            const here = at(row, column);
            if (!sameEdge(here.rect.y, first.rect.y))
                problems.push(
                    `row ${row}: cell ${column} starts at y=${Math.round(here.rect.y)} but ` +
                        `cell 0 starts at y=${Math.round(first.rect.y)}`,
                );
            if (!sameEdge(here.rect.height, first.rect.height))
                problems.push(
                    `row ${row}: cell ${column} is ${Math.round(here.rect.height)} high but ` +
                        `cell 0 is ${Math.round(first.rect.height)} high`,
                );
        }
    for (let column = 0; column < shape.columns; column++)
        for (let row = 1; row < shape.rows; row++) {
            const first = at(0, column);
            const here = at(row, column);
            if (!sameEdge(here.rect.x, first.rect.x))
                problems.push(
                    `column ${column}: row ${row} starts at x=${Math.round(here.rect.x)} but ` +
                        `row 0 starts at x=${Math.round(first.rect.x)}`,
                );
            if (!sameEdge(here.rect.width, first.rect.width))
                problems.push(
                    `column ${column}: row ${row} is ${Math.round(here.rect.width)} wide but ` +
                        `row 0 is ${Math.round(first.rect.width)} wide`,
                );
        }

    // The gap between neighbours is the grid's own gap, so it is the same everywhere. Taking the
    // first pair as the standard means this says nothing about what the gap should be, only that
    // it does not vary -- which is what a gap or an overlap in a tiling would show up as.
    const horizontalGaps: number[] = [];
    for (let row = 0; row < shape.rows; row++)
        for (let column = 1; column < shape.columns; column++)
            horizontalGaps.push(
                at(row, column).rect.x - right(at(row, column - 1).rect),
            );
    const verticalGaps: number[] = [];
    for (let column = 0; column < shape.columns; column++)
        for (let row = 1; row < shape.rows; row++)
            verticalGaps.push(
                at(row, column).rect.y - bottom(at(row - 1, column).rect),
            );
    for (const [name, gaps] of [
        ["side by side", horizontalGaps],
        ["one above the other", verticalGaps],
    ] as const) {
        if (gaps.length === 0) continue;
        const standard = gaps[0];
        if (standard < -kEdgeTolerance)
            problems.push(
                `cells ${name} overlap by ${Math.abs(Math.round(standard))}px`,
            );
        const uneven = gaps.filter((gap) => !sameEdge(gap, standard));
        if (uneven.length > 0)
            problems.push(
                `the gap between cells ${name} is ${Math.round(standard)}px in one place but ` +
                    `${uneven.map((g) => Math.round(g)).join(", ")}px elsewhere`,
            );
    }

    expect(
        problems,
        `Table ${tableIndex}'s ${shape.rows}x${shape.columns} cells do not tile it ` +
            `(${describeRect(rect)}):\n  ${problems.join("\n  ")}`,
    ).toEqual([]);

    for (const c of cells)
        expectInside(
            c.rect,
            rect,
            `the cell at row ${c.row}, column ${c.column}`,
            `table ${tableIndex}`,
        );
}

/**
 * Press one piece of the table's own chrome: an add button, a menu pill.
 *
 * These use Playwright's click rather than the raw mouse presses the rest of this file needs,
 * because the chrome is positioned against the table with CSS anchor positioning and repositioned
 * on an animation frame. Measuring it and then moving the mouse there races that: by the time the
 * press lands the button has moved, and the press goes to whatever is now underneath, silently
 * doing nothing (or, if the pointer lands on the page, taking the selection off the table, which
 * Bloom answers by deleting an empty canvas element). Playwright's click re-checks the target and
 * waits for it to stop moving.
 */
async function clickChrome(target: Locator, what: string): Promise<void> {
    await target.click({ timeout: kTableWaitMs }).catch((reason) => {
        throw new Error(`Could not press ${what}: ${reason}`);
    });
}

// ── Clicking and typing in cells ────────────────────────────────────────

/**
 * Click in the middle of a cell, the way a person does to put the caret in it or to select the
 * table, and wait until the table marks the cell selected.
 */
export async function clickCell(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<Locator> {
    const target = await cell(page, row, column, tableIndex);
    await target.waitFor({ state: "visible", timeout: kTableWaitMs });
    const box = await requireBox(
        target,
        `the cell at row ${row}, column ${column}`,
    );
    // A real press: Bloom lays a drawing surface over the page, which Playwright's own click
    // treats as something covering the cell and so refuses to click through.
    //
    // One press only: a cell that takes a second click to select is a bug a person would hit
    // (BL-16815). A press in the middle of a cell lands on the cell's own text box, and for that
    // one case the table library leaves the browser to move the focus and takes the selection
    // from the focusin that follows (see clickIsInOwnEditor in selection-highlight.ts).
    const at = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
    // A cell of a table inside a cell takes a double-click. The table library gives a single
    // press to the cell at the level of the table already selected, which is the outer cell, and
    // a double-click goes one level in (clickTargetCell and doubleClickTargetCell in the library).
    // A person gets into the inner table the same way.
    const isInNestedTable = await target.evaluate(
        (element) =>
            !!element
                .closest(".bloom-table")
                ?.parentElement?.closest(".bloom-table"),
    );
    await page.mouse.move(at.x, at.y);
    if (isInNestedTable) await page.mouse.dblclick(at.x, at.y);
    else {
        await page.mouse.down();
        await page.mouse.up();
    }
    // The polling runs here, not in the page: Bloom runs with --dont-disturb, so its window is
    // hidden and the browser throttles the page's timers to about once a minute. A setTimeout loop
    // inside the page turned a one-second wait into several minutes.
    try {
        await expect(target).toHaveClass(/\bcell--selected\b/, {
            timeout: 5000,
        });
    } catch {
        throw new Error(
            `One click on the cell at row ${row}, column ${column} did not select it. ` +
                (await describePressPoint(page, target, at)),
        );
    }
    return target;
}

/**
 * Say what a press at `at` (in Playwright's mouse coordinates) landed on, for the message of a
 * press that did not select the cell `target`. A cell that will not take a click is nearly always
 * a cell with something over it: Bloom's toolbar for the selected canvas element, a drawing
 * surface, or a piece of the table's own chrome that has been positioned across it.
 */
async function describePressPoint(
    page: Page,
    target: Locator,
    at: { x: number; y: number },
): Promise<string> {
    const box = await requireBox(target, "the cell");
    const inThePage = await target.evaluate(
        (element, fraction) => {
            // The page is zoomed, so the press point goes over as a fraction of the cell.
            const r = element.getBoundingClientRect();
            const describe = (node: Element | null) =>
                node
                    ? `${node.tagName.toLowerCase()}${node.id ? "#" + node.id : ""}` +
                      `.${node.className || "(no class)"}`
                    : "(nothing)";
            const doc = element.ownerDocument;
            return {
                atThePressPoint: describe(
                    doc.elementFromPoint(
                        r.left + fraction.x * r.width,
                        r.top + fraction.y * r.height,
                    ),
                ),
                selectedCells: doc.querySelectorAll(".cell--selected").length,
                focused: describe(doc.activeElement),
            };
        },
        { x: (at.x - box.x) / box.width, y: (at.y - box.y) / box.height },
    );
    // And what Bloom's own window has there. A press goes to the shell document first, so a
    // dialog it has left up, even an invisible one, takes the press and the page never sees it.
    // Over the page, the shell should report nothing but the page's iframe.
    const inTheShell = await page.evaluate((point) => {
        const node = document.elementFromPoint(point.x, point.y);
        return {
            atThePressPoint: node
                ? `${node.tagName.toLowerCase()}${node.id ? "#" + node.id : ""}` +
                  `.${node.className || "(no class)"}`
                : "(nothing)",
            dialogs: document.querySelectorAll(
                ".MuiDialog-root, .MuiModal-root, .MuiBackdrop-root",
            ).length,
        };
    }, at);
    return (
        `At the point pressed, in the page: ${inThePage.atThePressPoint}; ` +
        `in Bloom's window: ${inTheShell.atThePressPoint} ` +
        `(${inTheShell.dialogs} dialog or backdrop elements in the window). ` +
        `Cells marked selected in the page: ${inThePage.selectedCells}. ` +
        `Focus: ${inThePage.focused}.`
    );
}

/**
 * Press once in the middle of the picture box in a picture cell, the way a person does to get at
 * the picture, and wait until the table marks that cell selected and the picture is the selected
 * canvas element, which is what puts the picture's own toolbar (Choose image and the rest) below
 * the table. One press only, as in clickCell: a picture that takes a second press to answer is
 * the failure this is for.
 */
export async function clickCellPicture(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<Locator> {
    const target = await cell(page, row, column, tableIndex);
    // The picture box, not its <img>: until a picture is chosen the <img> holds the placeholder,
    // which is not displayed, and what a person sees and clicks is the box.
    const pictureBox = target.locator(".bloom-canvas").first();
    const box = await requireBox(
        pictureBox,
        `the picture box in the cell at row ${row}, column ${column}`,
    );
    const at = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
    // A real press, for the same reason as in clickCell.
    await page.mouse.move(at.x, at.y);
    await page.mouse.down();
    await page.mouse.up();
    try {
        await expect(target).toHaveClass(/cell--selected/, { timeout: 5000 });
    } catch {
        throw new Error(
            `Pressing the picture in the cell at row ${row}, column ${column} did not select ` +
                `that cell. ${await describePressPoint(page, target, at)}`,
        );
    }
    // Bloom marks the selected canvas element data-bloom-active. In a picture cell that is the
    // picture's own canvas element, inside the cell; the table's canvas element holds the cell.
    await expect(
        target.locator('.bloom-canvas-element[data-bloom-active="true"]'),
        `Pressing the picture in the cell at row ${row}, column ${column} selected the cell but ` +
            `not its picture, so the picture's toolbar is not on offer.`,
    ).toHaveCount(1, { timeout: 5000 });
    return target;
}

/**
 * Press once in the middle of one language's text box in a cell, the way a person does to start
 * typing there, and wait until the box has the focus. One press only, as in clickCell and
 * typeInCell: a text cell that ignores the first click is the failure this is for.
 */
export async function clickCellText(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    tableIndex = 0,
): Promise<Locator> {
    const target = await cell(page, row, column, tableIndex);
    const textBox = target
        .locator(`.bloom-editable[lang="${languageTag}"]`)
        .first();
    const box = await requireBox(
        textBox,
        `the "${languageTag}" text box in the cell at row ${row}, column ${column}`,
    );
    const at = { x: box.x + box.width / 2, y: box.y + box.height / 2 };
    // A real press, for the same reason as in clickCell.
    await page.mouse.move(at.x, at.y);
    await page.mouse.down();
    await page.mouse.up();
    try {
        await expect(textBox).toBeFocused({ timeout: 5000 });
    } catch {
        throw new Error(
            `One click on the "${languageTag}" text box in the cell at row ${row}, column ` +
                `${column} did not put the caret in it. ` +
                (await describePressPoint(page, target, at)),
        );
    }
    return textBox;
}

/** One language's text box inside a cell. */
export async function cellTextBox(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    tableIndex = 0,
): Promise<Locator> {
    const target = await cell(page, row, column, tableIndex);
    return target.locator(`.bloom-editable[lang="${languageTag}"]`).first();
}

/**
 * Put the caret in one language's box in a cell with one click (after the double-click into the
 * inner table, for a cell of a table inside a cell), and hand the box back. A box that takes a
 * second click to get the caret is a bug a person would hit (BL-16815).
 */
async function focusCellTextBox(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    tableIndex = 0,
): Promise<Locator> {
    const box = await cellTextBox(page, row, column, languageTag, tableIndex);
    await box.waitFor({ state: "visible", timeout: kTableWaitMs });
    // A single press in a table inside a cell goes to the outer cell; a person double-clicks to
    // get into the inner table first, which clickCell does.
    const isInNestedTable = await box.evaluate(
        (element) =>
            !!element
                .closest(".bloom-table")
                ?.parentElement?.closest(".bloom-table"),
    );
    if (isInNestedTable) await clickCell(page, row, column, tableIndex);
    await realClick(box);
    try {
        await expect(box).toBeFocused({ timeout: 5000 });
    } catch {
        const cellBox = await requireBox(box, "the text box");
        throw new Error(
            `One click on the "${languageTag}" box in the cell at row ${row}, column ${column} ` +
                `did not give it the focus. ` +
                (await describePressPoint(
                    page,
                    await cell(page, row, column, tableIndex),
                    {
                        x: cellBox.x + cellBox.width / 2,
                        y: cellBox.y + cellBox.height / 2,
                    },
                )),
        );
    }
    return box;
}

/**
 * Type text into one language's box in a cell, the way typeInGroup does for a text box on the page,
 * and confirm the cell holds it.
 *
 * As there, this raises input events but no key events, so it does not exercise anything that
 * listens for a key. Use keys.ts for that. (AUTOMATION-DEBT.md: "Typing in a text box raises no
 * key events".)
 */
export async function typeInCell(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    text: string,
    tableIndex = 0,
): Promise<void> {
    const box = await focusCellTextBox(
        page,
        row,
        column,
        languageTag,
        tableIndex,
    );
    await clearCellTextBox(box);
    if (text) await page.keyboard.insertText(text);
    await expect(box).toHaveText(text, { timeout: 15000 });
}

/**
 * Empty a cell's text box, and do nothing at all to one that is already empty.
 *
 * Select-all-and-delete in an empty box is what CKEditor answers by rebuilding the paragraph
 * inside it, and that rebuild replaces the element's contents a moment later, so text inserted
 * in between is thrown away and the box stays empty. Skipping the clearing when there is nothing
 * to clear takes that race away.
 */
async function clearCellTextBox(box: Locator): Promise<void> {
    if ((await box.innerText()).trim() === "") return;
    await box.press("Control+a");
    await box.press("Delete");
    await expect(
        box,
        "Select-all and Delete did not empty the cell's text box.",
    ).toHaveText("", { timeout: 15000 });
}

/**
 * Type into a cell one key at a time, rather than handing the box the whole string at once.
 *
 * Use this when the test's subject is something that only a real keystroke sets off. CKEditor
 * builds its undo stack from key events, and `page.keyboard.insertText` (what `typeInCell` uses,
 * because it is far quicker) raises none, so text typed that way cannot be undone.
 */
export async function typeInCellKeyByKey(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    text: string,
    tableIndex = 0,
): Promise<void> {
    const box = await focusCellTextBox(
        page,
        row,
        column,
        languageTag,
        tableIndex,
    );
    await clearCellTextBox(box);
    await box.pressSequentially(text, { delay: 30 });
    await expect(box).toHaveText(text, { timeout: 15000 });
}

/** What one language's box in a cell holds. */
export async function getCellText(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    tableIndex = 0,
): Promise<string> {
    const box = await cellTextBox(page, row, column, languageTag, tableIndex);
    return (await box.innerText()).trim();
}

/** The language tags of the text boxes a cell shows, in order. */
export async function getCellLanguages(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<string[]> {
    const target = await cell(page, row, column, tableIndex);
    return target
        .locator(".bloom-editable.bloom-visibility-code-on")
        .evaluateAll((boxes) =>
            boxes.map((box) => box.getAttribute("lang") ?? ""),
        );
}

/** The video box inside a cell, which is what the Sign Language tool imports into. */
export async function cellVideoBox(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<Locator> {
    const target = await cell(page, row, column, tableIndex);
    return target.locator(".bloom-videoContainer").first();
}

/**
 * How many paragraphs one language's box in a cell holds. Bloom starts a text box with one, and
 * Enter should make another, so this is how a test asks whether Enter went to the text rather than
 * to the table or the canvas element around it.
 */
export async function getCellParagraphCount(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    tableIndex = 0,
): Promise<number> {
    const box = await cellTextBox(page, row, column, languageTag, tableIndex);
    return box.locator("p").count();
}

// ── The chrome: "+" buttons, pills and menus ────────────────────────────

/**
 * Add a row at the bottom, or a column at the right, by pressing the "+" button on that edge, and
 * wait until the table's shape has grown. Returns the new shape.
 *
 * The button is hidden until the pointer comes near the table, so this brings the pointer to that
 * edge first, which is also what a person does.
 */
export async function clickAddButton(
    page: Page,
    what: "row" | "column",
    tableIndex = 0,
): Promise<ITableShape> {
    const before = await getTableShape(page, tableIndex);
    const tableRect = (await measureTable(page, tableIndex)).rect;
    // Just inside the far edge, in the middle of the other axis: near enough for the library's
    // proximity test, and not on a cell boundary, where the pointer would arm a resize instead.
    const hoverAt =
        what === "row"
            ? { x: tableRect.x + tableRect.width / 2, y: bottom(tableRect) - 8 }
            : {
                  x: right(tableRect) - 8,
                  y: tableRect.y + tableRect.height / 2,
              };
    const button = editablePageFrame(page).locator(
        `button[data-table-overlay="add-button"][aria-label="${ADD_BUTTON_LABEL[what]}"]`,
    );
    await page.mouse.move(hoverAt.x, hoverAt.y);
    await button.waitFor({ state: "visible", timeout: kTableWaitMs });
    await clickChrome(button, `the "add ${what}" button`);
    const grew = what === "row" ? "rows" : "columns";
    await expect
        .poll(async () => (await getTableShape(page, tableIndex))[grew], {
            timeout: kTableWaitMs,
            message:
                `Pressing the "+" button on the ${what === "row" ? "bottom" : "right"} edge did ` +
                `not add a ${what} (the table still has ${before[grew]} ${grew}).`,
        })
        .toBe(before[grew] + 1);
    return getTableShape(page, tableIndex);
}

/**
 * Open the menu for one row, one column, or the table as a whole, by clicking its "..." pill, and
 * wait until the menu is showing.
 *
 * The pills appear only while the pointer is near the row or column they belong to, so this hovers
 * the cell that identifies it first. For "table" the pill sits at the top left corner.
 *
 * `index` only decides where the pointer goes. The menu that opens acts on the row or column of
 * the table's selected cell (togglePillMenu in the table library), so a caller that means a
 * particular row or column selects a cell in it first, with clickCell.
 */
export async function openTableMenu(
    page: Page,
    which: "row" | "column" | "table",
    index = 0,
    tableIndex = 0,
): Promise<void> {
    const frame = editablePageFrame(page);
    const hoverCell =
        which === "row"
            ? await cell(page, index, 0, tableIndex)
            : await cell(page, 0, which === "column" ? index : 0, tableIndex);
    const box = await requireBox(
        hoverCell,
        `the cell that identifies the ${which}`,
    );
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    const pill = frame.locator(`button[data-btable-menu-pill="${which}"]`);
    await pill.waitFor({ state: "visible", timeout: kTableWaitMs });
    await clickChrome(pill, `the ${which} pill`);
    await frame
        .locator(`[data-btable-menu="${which}"]`)
        .waitFor({ state: "visible", timeout: kTableWaitMs });
}

/**
 * Click one command in whichever table menu is open, and wait until the menu has closed.
 *
 * Commands are found by their aria-label, which the library writes in English. See the note on
 * TableMenuCommand.
 */
export async function clickTableMenuCommand(
    page: Page,
    command: TableMenuCommand,
): Promise<void> {
    const frame = editablePageFrame(page);
    const menu = frame.locator("[data-btable-menu]:visible").first();
    await menu.waitFor({ state: "visible", timeout: kTableWaitMs });
    const item = menu.locator(`[role="menuitem"][aria-label="${command}"]`);
    if ((await item.count()) === 0) {
        const offered = await menu
            .locator('[role="menuitem"]')
            .evaluateAll((items) =>
                items.map((i) => i.getAttribute("aria-label") ?? ""),
            );
        throw new Error(
            `The open table menu has no "${command}" command. It offers: ` +
                `${offered.join(", ") || "(nothing)"}.`,
        );
    }
    await item.click();
    await menu.waitFor({ state: "hidden", timeout: kTableWaitMs });
}

/**
 * Right-click a cell and wait for the table's Cell menu. Returns which menus came up, so a caller
 * can assert that the Cell menu is the only one: the canvas element around the table and Bloom's
 * own text menu both listen for the same right-click, and two menus at once is the failure this
 * guards against.
 */
export async function rightClickCell(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<IOpenMenus> {
    await pressRightOnCell(page, row, column, tableIndex);
    await editablePageFrame(page)
        .locator('[data-btable-menu="cell"]')
        .waitFor({ state: "visible", timeout: kTableWaitMs })
        .catch(() => undefined);
    return getOpenMenus(page);
}

/**
 * Right-click a cell where the Cell menu is not expected, and report which menus came up.
 *
 * Separate from rightClickCell because that one waits half a minute for a Cell menu that is never
 * coming, which is thirty seconds added to every run. This waits only long enough for whatever
 * menu does open to appear.
 */
export async function rightClickCellExpectingNoCellMenu(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<IOpenMenus> {
    await pressRightOnCell(page, row, column, tableIndex);
    // Long enough for a menu raised by this click to have rendered, whichever menu it is.
    await editablePageFrame(page)
        .locator("[data-btable-menu]:visible, .MuiMenu-list:visible")
        .first()
        .waitFor({ state: "visible", timeout: 2000 })
        .catch(() => undefined);
    return getOpenMenus(page);
}

/**
 * Right-click the text of a cell, where the Cell menu is not expected, and report which menus
 * came up.
 *
 * The press lands on the paragraph itself rather than in the middle of the cell, because Bloom's
 * text context menu claims a right-click on a paragraph and a cell is usually much taller than
 * the line of text in it. That is the same rule an ordinary text box follows: the blank part of
 * a text box raises no text menu either.
 */
export async function rightClickCellTextExpectingNoCellMenu(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    tableIndex = 0,
): Promise<IOpenMenus> {
    const paragraph = (
        await cellTextBox(page, row, column, languageTag, tableIndex)
    )
        .locator("p")
        .first();
    const box = await requireBox(
        paragraph,
        `the text of the cell at row ${row}, column ${column}`,
    );
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.click(x, y, { button: "right" });
    // Long enough for a menu raised by this click to have rendered, whichever menu it is.
    await editablePageFrame(page)
        .locator("[data-btable-menu]:visible, .MuiMenu-list:visible")
        .first()
        .waitFor({ state: "visible", timeout: 2000 })
        .catch(() => undefined);
    return getOpenMenus(page);
}

/** Press the right mouse button in the middle of a cell. Waits for nothing. */
async function pressRightOnCell(
    page: Page,
    row: number,
    column: number,
    tableIndex: number,
): Promise<void> {
    const target = await cell(page, row, column, tableIndex);
    const box = await requireBox(
        target,
        `the cell at row ${row}, column ${column}`,
    );
    const x = box.x + box.width / 2;
    const y = box.y + box.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.click(x, y, { button: "right" });
}

/** Which menus are showing on the page being edited. */
export async function getOpenMenus(page: Page): Promise<IOpenMenus> {
    const frame = editablePageFrame(page);
    const showing = async (selector: string) =>
        (await frame.locator(selector).count()) > 0;
    // Bloom's text menu is the one MUI menu with a No Indent item. The canvas element's menu is
    // then any other visible MUI menu; the table's menus are not MUI menus at all.
    const bloomTextItem =
        'li[data-testid="EditTab.TextContextMenu.NoIndent"]:visible';
    return {
        cell: await showing('[data-btable-menu="cell"]:visible'),
        row: await showing('[data-btable-menu="row"]:visible'),
        column: await showing('[data-btable-menu="column"]:visible'),
        table: await showing('[data-btable-menu="table"]:visible'),
        canvasElement: await showing(
            `.MuiMenu-list:visible:not(:has(${bloomTextItem}))`,
        ),
        bloomText: await showing(bloomTextItem),
    };
}

/**
 * Close any table, cell or canvas element menu that is open, the way Escape does.
 *
 * Escape goes to the open menu itself as well as to the window: a MUI menu is in the page's
 * iframe, and a keystroke sent to the window reaches whatever holds the focus, which after a
 * mouse press on the page is often the shell document. A MUI menu that stays open keeps its
 * backdrop over the page, and the backdrop takes every press aimed at what is underneath.
 */
export async function closeAnyMenu(page: Page): Promise<void> {
    const frame = editablePageFrame(page);
    const anyMenu = frame
        .locator("[data-btable-menu]:visible, .MuiMenu-list:visible")
        .first();
    for (let attempt = 0; attempt < 3; attempt++) {
        const open = await getOpenMenus(page);
        if (!Object.values(open).some((isOpen) => isOpen)) return;
        await anyMenu.press("Escape").catch(() => undefined);
        await page.keyboard.press("Escape");
        await anyMenu
            .waitFor({ state: "hidden", timeout: 5000 })
            .catch(() => undefined);
    }
}

/**
 * The content types the Cell menu offers for a cell, and which one is on. Right-clicks the cell to
 * see them, and leaves the menu open so the caller can choose one.
 */
export async function getCellMenuContentTypes(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<string[]> {
    const menus = await rightClickCell(page, row, column, tableIndex);
    if (!menus.cell)
        throw new Error(
            `Right-clicking the cell at row ${row}, column ${column} did not open the Cell menu. ` +
                `Menus that did open: ${describeMenus(menus)}.`,
        );
    return editablePageFrame(page)
        .locator('[data-btable-menu="cell"] [data-ct-id]')
        .evaluateAll((buttons) =>
            buttons.map((b) => b.getAttribute("data-ct-id") ?? ""),
        );
}

/**
 * Change what a cell holds, through the Cell menu, and wait until the cell says it holds it.
 *
 * The content-type buttons carry `data-ct-id`, so this names no English label. Choosing a type
 * replaces the cell's contents, so anything typed in it is gone afterwards.
 */
export async function setCellContentType(
    page: Page,
    row: number,
    column: number,
    type: CellContentType,
    tableIndex = 0,
): Promise<void> {
    const offered = await getCellMenuContentTypes(
        page,
        row,
        column,
        tableIndex,
    );
    if (!offered.includes(type))
        throw new Error(
            `The Cell menu does not offer the "${type}" content type. It offers: ` +
                `${offered.join(", ") || "(nothing)"}.`,
        );
    const frame = editablePageFrame(page);
    await frame
        .locator(`[data-btable-menu="cell"] [data-ct-id="${type}"]`)
        .click();
    const target = await cell(page, row, column, tableIndex);
    await expect(
        target,
        `Choosing the "${type}" content type did not change what the cell at row ${row}, ` +
            `column ${column} holds.`,
    ).toHaveAttribute("data-content-type", type, { timeout: kTableWaitMs });
    await closeAnyMenu(page);
}

/** Where each piece of table chrome is drawn, for asserting that none of it covers another. */
export async function measureChrome(
    page: Page,
): Promise<{ name: string; rect: IRect }[]> {
    const frameOffset = await pageFrameOffset(page);
    const pieces = await editablePageFrame(page)
        .locator(
            'button[data-table-overlay="add-button"]:visible, button[data-btable-menu-pill]:visible',
        )
        .evaluateAll((elements) =>
            elements.map((element) => {
                const box = element.getBoundingClientRect();
                return {
                    name:
                        element.getAttribute("data-btable-menu-pill") !== null
                            ? `the ${element.getAttribute("data-btable-menu-pill")} pill`
                            : `the "${element.getAttribute("aria-label")}" button`,
                    rect: {
                        x: box.x,
                        y: box.y,
                        width: box.width,
                        height: box.height,
                    },
                };
            }),
        );
    return pieces.map((piece) => ({
        name: piece.name,
        rect: {
            ...piece.rect,
            x: piece.rect.x + frameOffset.x,
            y: piece.rect.y + frameOffset.y,
        },
    }));
}

// ── Moving a table on the canvas ────────────────────────────────────────

/**
 * Drag a table to somewhere else on the canvas, and wait until it has moved. `dx` and `dy` are how
 * far it should go, in the page's own pixels.
 *
 * The press is aimed two pixels inside the table's top edge, which is the table's own border and
 * grid gap rather than a cell: a press in a cell puts the caret in it, and the drag then selects
 * text instead of moving anything.
 *
 * It is aimed well along that edge rather than at the corner, because the canvas element's north
 * west resize handle sits over the corner and takes the press: a drag from there resizes the table
 * instead of moving it.
 */
const kCornerHandleReach = 24;

/**
 * Where along the top edge of the table at `tableIndex` (whose window rect is `rect`) a press
 * reaches that table, as an x in Playwright's mouse coordinates. Bloom places a duplicate so that
 * it overlaps the original, and a press on a stretch of edge that another canvas element covers
 * picks that element instead. So the press goes at the first point past the corner handle that no
 * other canvas element covers.
 */
async function findUncoveredPointAlongTopEdge(
    page: Page,
    tableIndex: number,
    rect: IRect,
): Promise<number> {
    const frameOffset = await pageFrameOffset(page);
    const others = await table(page, tableIndex).evaluate((element) => {
        const own = element.closest(".bloom-canvas-element");
        return Array.from(document.querySelectorAll(".bloom-canvas-element"))
            .filter(
                (other) =>
                    other !== own &&
                    !own?.contains(other) &&
                    !other.classList.contains("bloom-backgroundImage"),
            )
            .map((other) => {
                const box = other.getBoundingClientRect();
                return {
                    x: box.x,
                    y: box.y,
                    width: box.width,
                    height: box.height,
                };
            });
    });
    const y = rect.y + 2 - frameOffset.y;
    const isCovered = (x: number) =>
        others.some(
            (o) =>
                x >= o.x &&
                x <= o.x + o.width &&
                y >= o.y &&
                y <= o.y + o.height,
        );
    const first = Math.min(kCornerHandleReach, rect.width / 2);
    for (let along = first; along < rect.width - first; along += 4) {
        if (!isCovered(rect.x + along - frameOffset.x)) return rect.x + along;
    }
    throw new Error(
        `Every point along the top edge of table ${tableIndex} is covered by another canvas ` +
            `element, so there is nowhere to press to drag it.`,
    );
}

export async function dragTableBy(
    page: Page,
    tableIndex: number,
    dx: number,
    dy: number,
): Promise<{ before: IRect; after: IRect }> {
    const before = (await measureTable(page, tableIndex)).rect;
    const startX = await findUncoveredPointAlongTopEdge(
        page,
        tableIndex,
        before,
    );
    const startY = before.y + 2;
    await page.mouse.move(startX, startY);
    await page.mouse.down();
    // In steps: Bloom follows pointermove events, and a single jump gives it one, which its own
    // drag threshold can read as no drag at all.
    await page.mouse.move(startX + dx, startY + dy, { steps: 12 });
    await page.mouse.up();
    await expect
        .poll(
            async () => {
                const now = (await measureTable(page, tableIndex)).rect;
                return Math.abs(now.x - before.x) + Math.abs(now.y - before.y);
            },
            {
                timeout: kTableWaitMs,
                message:
                    `Dragging table ${tableIndex} by ${Math.round(dx)},${Math.round(dy)} did not ` +
                    `move it.`,
            },
        )
        .toBeGreaterThan(2);
    return { before, after: (await measureTable(page, tableIndex)).rect };
}

/**
 * Select the canvas element the table sits in, rather than a cell of it, and wait until Bloom
 * marks it active. That is what the element's own toolbar and "..." menu act on.
 *
 * The press is aimed two pixels inside the table's top left corner, which is the table's own
 * border and grid gap rather than a cell: a press in a cell selects the cell, and leaves whatever
 * was active before as the active canvas element -- which for a table holding a video cell is the
 * video's element, whose menu is a different menu entirely. That is the same point dragTableBy
 * and rightClickCanvasElementEdge use.
 */
export async function selectTableElement(
    page: Page,
    tableIndex = 0,
): Promise<void> {
    const rect = (await measureTable(page, tableIndex)).rect;
    await page.mouse.move(rect.x + 2, rect.y + 2);
    await page.mouse.down();
    await page.mouse.up();
    // The element that holds a table, told apart from any element inside one of its cells by the
    // fact that the table is inside it.
    const active = editablePageFrame(page).locator(
        '.bloom-canvas-element[data-bloom-active="true"]:has(.bloom-table)',
    );
    await expect
        .poll(async () => active.count(), {
            timeout: 15000,
            message:
                `Pressing the top left corner of table ${tableIndex} did not select the canvas ` +
                `element it sits in.`,
        })
        .toBeGreaterThan(0);
}

/**
 * Assert that a table is drawn wholly inside the canvas of the page it is on.
 *
 * A table that hangs over the page boundary is clipped, so part of it cannot be seen or clicked,
 * and every measurement taken of it afterwards is of the part that survived. Nothing here says
 * where on the canvas the table should be, only that all of it is on it.
 */
export async function expectTableInsideCanvas(
    page: Page,
    tableIndex = 0,
): Promise<void> {
    const canvasRect = await getCanvasRect(page);
    const tableRect = (await measureTable(page, tableIndex)).rect;
    expectInside(
        tableRect,
        canvasRect,
        `table ${tableIndex}`,
        "the page's canvas",
    );
}

// ── Resizing by dragging a boundary ─────────────────────────────────────

/**
 * Drag the right-hand boundary of one column, or the bottom boundary of one row, by `distance`
 * pixels, and wait until the table's recorded sizes change.
 *
 * There is no handle to grab. The library decides a press is a resize when it lands within about
 * five pixels of a cell's own right or bottom edge, so this aims two pixels inside that edge. That
 * is why this helper exists rather than a test doing it: the aim is a fact about the library, and
 * it belongs in one place. (AUTOMATION-DEBT.md: "Table row and column boundaries have no handle".)
 */
export async function dragBoundary(
    page: Page,
    what: "column" | "row",
    index: number,
    distance: number,
    tableIndex = 0,
): Promise<{ before: ITableShape; after: ITableShape }> {
    const before = await performBoundaryDrag(
        page,
        what,
        index,
        distance,
        tableIndex,
    );
    const sizes = what === "column" ? "columnWidths" : "rowHeights";
    await expect
        .poll(
            async () =>
                (await getTableShape(page, tableIndex))[sizes].join(","),
            {
                timeout: kTableWaitMs,
                message:
                    `Dragging the ${what} ${index} boundary by ${distance}px did not change the ` +
                    `table's recorded sizes (still ${before[sizes].join(",")}).`,
            },
        )
        .not.toBe(before[sizes].join(","));
    return { before, after: await getTableShape(page, tableIndex) };
}

/** How long expectBoundaryDragDoesNothing watches before it believes nothing is going to move. */
const NO_RESIZE_MS = 1500;

/**
 * Make the same gesture dragBoundary makes, and assert that the table does not resize: no
 * recorded size changes, and no cell changes width or height.
 *
 * This is a helper of its own rather than an option on dragBoundary because the waiting is the
 * opposite way round. dragBoundary waits for a change and fails if none comes; this watches for a
 * while and fails if one does, so that a resize which arrives a moment late still fails the test.
 */
export async function expectBoundaryDragDoesNothing(
    page: Page,
    what: "column" | "row",
    index: number,
    distance: number,
    tableIndex = 0,
): Promise<void> {
    const cellsBefore = (await measureTable(page, tableIndex)).cells;
    const before = await performBoundaryDrag(
        page,
        what,
        index,
        distance,
        tableIndex,
    );
    const sizes = what === "column" ? "columnWidths" : "rowHeights";
    const deadline = Date.now() + NO_RESIZE_MS;
    while (Date.now() < deadline) {
        expect(
            (await getTableShape(page, tableIndex))[sizes].join(","),
            `Dragging the ${what} ${index} boundary by ${distance}px changed the table's ` +
                `recorded sizes, and here it should do nothing at all.`,
        ).toBe(before[sizes].join(","));
    }
    // The recorded sizes are what the library writes down; the cells are what a person sees. A
    // drag that moved the boundary without recording it would still be a failure.
    const cellsAfter = (await measureTable(page, tableIndex)).cells;
    const describe = (cells: ICellInfo[]) =>
        cells
            .map(
                (c) =>
                    `(${c.row},${c.column}) ${Math.round(c.rect.width)}x` +
                    `${Math.round(c.rect.height)}`,
            )
            .join(" ");
    for (const was of cellsBefore) {
        const now = cellsAfter.find(
            (c) => c.row === was.row && c.column === was.column,
        );
        if (!now)
            throw new Error(
                `The cell at row ${was.row}, column ${was.column} disappeared during a drag ` +
                    `that should have done nothing.`,
            );
        expect(
            Math.abs(now.rect.width - was.rect.width) +
                Math.abs(now.rect.height - was.rect.height),
            `Dragging the ${what} ${index} boundary by ${distance}px resized the cells, and ` +
                `here it should do nothing at all. Before: ${describe(cellsBefore)}. After: ` +
                `${describe(cellsAfter)}.`,
        ).toBeLessThanOrEqual(2);
    }
}

/**
 * Press two pixels inside the right or bottom edge of a cell and drag `distance` pixels, which is
 * the gesture the library reads as a resize. Returns the table's shape as it was before the drag.
 * Says nothing about what the drag should achieve; the callers above do that.
 */
async function performBoundaryDrag(
    page: Page,
    what: "column" | "row",
    index: number,
    distance: number,
    tableIndex: number,
): Promise<ITableShape> {
    const before = await getTableShape(page, tableIndex);
    const target =
        what === "column"
            ? await cell(page, 0, index, tableIndex)
            : await cell(page, index, 0, tableIndex);
    const box = await requireBox(
        target,
        `the cell whose edge is the ${what} boundary`,
    );
    const from =
        what === "column"
            ? { x: right(box) - 2, y: box.y + box.height / 2 }
            : { x: box.x + box.width / 2, y: bottom(box) - 2 };
    const to =
        what === "column"
            ? { x: from.x + distance, y: from.y }
            : { x: from.x, y: from.y + distance };
    await page.mouse.move(from.x, from.y);
    await page.mouse.down();
    // In steps, and past the destination before settling on it: the library follows pointermove
    // events, and a single jump gives it one, which some of its own snapping treats as no drag.
    await page.mouse.move(to.x, to.y, { steps: 12 });
    await page.mouse.up();
    return before;
}

// ── Internals ───────────────────────────────────────────────────────────

/** How far the page's iframe is from the top left of the window Playwright's mouse works in. */
async function pageFrameOffset(page: Page): Promise<{ x: number; y: number }> {
    const element = await editablePageFrame(page).frameElement();
    const box = await element.boundingBox();
    if (!box)
        throw new Error(
            "The Edit tab's page iframe has no on-screen box, so nothing in it can be measured.",
        );
    return { x: box.x, y: box.y };
}

async function requireBox(locator: Locator, what: string): Promise<IRect> {
    await locator.waitFor({ state: "visible", timeout: kTableWaitMs });
    // Every box measured here is about to be pressed or hovered. In a window shorter than the
    // page (the nightly runner's, or a zoomed view), it can be below what is showing, where the
    // mouse lands on nothing. A person would scroll to it first.
    await locator.scrollIntoViewIfNeeded({ timeout: kTableWaitMs });
    const box = await locator.boundingBox({ timeout: kTableWaitMs });
    if (!box)
        throw new Error(
            `${what} is visible but has no on-screen box, so there is nowhere to measure or click.`,
        );
    return box;
}

function describeMenus(menus: IOpenMenus): string {
    const open = Object.entries(menus)
        .filter(([, isOpen]) => isOpen)
        .map(([name]) => name);
    return open.join(", ") || "none";
}

/**
 * Assert that the format gear Bloom shows for the text box that has the focus is drawn inside the
 * cell that box belongs to. A gear that escapes its cell is the visible sign that Bloom placed it
 * against the page rather than against the cell.
 *
 * Both rects come from the page frame, so no coordinate conversion is involved.
 */
export async function expectFormatGearInsideCell(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<void> {
    const target = await cell(page, row, column, tableIndex);
    const frame = editablePageFrame(page);
    const gear = frame.locator("#formatButton");
    await gear.waitFor({ state: "visible", timeout: kTableWaitMs });
    const rects = await frame.evaluate(
        ({ gearSelector }) => {
            const asRect = (element: Element | null) => {
                if (!element) return null;
                const box = element.getBoundingClientRect();
                return {
                    x: box.x,
                    y: box.y,
                    width: box.width,
                    height: box.height,
                };
            };
            const gearElement = document.querySelector(gearSelector);
            return {
                gear: asRect(gearElement),
                cell: asRect(gearElement?.closest(".bloom-cell") ?? null),
            };
        },
        { gearSelector: "#formatButton" },
    );
    expect(
        rects.gear,
        "Bloom is not showing a format gear, so no text box has the focus.",
    ).not.toBeNull();
    expect(
        rects.cell,
        `The format gear is not inside a table cell at all, so it does not belong to the cell at ` +
            `row ${row}, column ${column}.`,
    ).not.toBeNull();
    // Confirm the cell the gear sits in is the one asked about, before judging where it is drawn.
    await expect(
        target,
        `The format gear belongs to a different cell than row ${row}, column ${column}.`,
    ).toHaveClass(/cell--selected/, { timeout: 5000 });
    expectInside(
        rects.gear!,
        rects.cell!,
        "the format gear",
        `the cell at row ${row}, column ${column}`,
    );
}

/**
 * Assert that every table in `frame` is drawn as a grid whose cells tile it, and that none of the
 * editing-only markup has come along.
 *
 * Written for the BloomPUB preview, where the question is exactly this: the reader's copy has none
 * of Bloom's editing code, so the table has to look right from the saved markup and the read-time
 * CSS alone.
 */
export async function expectTablesRenderAsGrids(
    frame: Frame,
    expectedCount: number,
    withinPageId?: string,
): Promise<void> {
    // Scoped to one page when asked, because bloom-player builds only the pages near the one it is
    // showing: a count taken across the whole preview says how far the reader has swiped rather
    // than what the book holds.
    const tableSelector = withinPageId
        ? `[id="${withinPageId}"] .bloom-table`
        : ".bloom-table";
    const found = frame.locator(tableSelector);
    await expect
        .poll(async () => found.count(), {
            timeout: kTableWaitMs,
            message: "The preview never showed the expected number of tables.",
        })
        .toBe(expectedCount);
    for (let index = 0; index < expectedCount; index++) {
        const display = await found
            .nth(index)
            .evaluate((element) => getComputedStyle(element).display);
        expect(
            display,
            `Table ${index} in the preview is drawn as "${display}" rather than as a grid, so its ` +
                `cells will not be laid out.`,
        ).toBe("grid");
        expectTilingOf(
            await measureTableIn(frame, index, tableSelector),
            index,
        );
    }
    const artifacts = await frame
        .locator(
            "[data-table-attached], .cell--selected, .table--selected, .bloom-current-table, " +
                "[data-table-overlay], .bloom-sel-overlay",
        )
        .count();
    expect(
        artifacts,
        "The preview carries editing-only table markup, which means it reached the reader's copy " +
            "of the book.",
    ).toBe(0);
}

/**
 * How many picture cells a frame's tables hold, and how many of those actually show a picture.
 *
 * Both are asked at once because the interesting answer is the pair: a cell that records
 * `data-content-type="image"` and shows nothing is a picture that did not survive.
 *
 * Publishing turns the `img` a picture cell holds in the editor into a background image in the
 * cell's own style attribute, so this counts either as a picture showing. Written for the BloomPUB
 * preview, where that conversion has happened.
 */
export async function countCellPictures(
    frame: Frame,
): Promise<{ pictureCells: number; showingAPicture: number }> {
    return frame
        .locator('.bloom-cell[data-content-type="image"]')
        .evaluateAll((cells) => {
            const showsAPicture = (cellElement: Element) => {
                if (cellElement.querySelector("img[src]")) return true;
                return [...cellElement.querySelectorAll("*")].some(
                    (element) => {
                        const inline = (element as HTMLElement).style
                            .backgroundImage;
                        return !!inline && inline !== "none";
                    },
                );
            };
            return {
                pictureCells: cells.length,
                showingAPicture: cells.filter(showsAPicture).length,
            };
        });
}

/**
 * How many of Bloom's own drawing surfaces (the canvas it draws speech bubbles on, which carries
 * `comical-generated`) sit inside a table cell in this frame.
 *
 * In the reader's copy of a book this should be none: the surface exists only so that the editor
 * can draw on it.
 */
export async function countDrawingSurfacesInCells(
    frame: Frame,
): Promise<number> {
    return frame.locator(".bloom-cell canvas.comical-generated").count();
}

/**
 * Whether Bloom is marking the text in a cell as too big for the space it has, and which of its
 * three ways of saying so it used: `overflow` (the box overflows itself), `childOverflowingThis`
 * and `thisOverflowingParent` (the box overflows, or is overflowed by, what holds it). See
 * OverflowChecker.ts.
 */
export async function getCellOverflowMarks(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<string[]> {
    const target = await cell(page, row, column, tableIndex);
    return target.evaluate((element) => {
        const marks = [
            "overflow",
            "childOverflowingThis",
            "thisOverflowingParent",
        ];
        const found = new Set<string>();
        for (const mark of marks) {
            if (element.classList.contains(mark)) found.add(mark);
            if (element.querySelector(`.${mark}`)) found.add(mark);
        }
        return [...found];
    });
}

/**
 * Drag the mouse across the text in a cell, the way a person selects a word, and return what ends
 * up selected in the page.
 *
 * The drag is what is under test: inside a canvas element, a press and a drag is also how an
 * element is moved, so the question this asks is whether the press was taken as text selection
 * rather than as the start of a move.
 */
export async function dragAcrossCellText(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    tableIndex = 0,
): Promise<string> {
    const box = await cellTextBox(page, row, column, languageTag, tableIndex);
    // Across the words themselves, not across the box: a text box is wider and taller than its
    // text, and a drag through the empty part of it selects nothing. The words' own rectangle
    // comes from a range over the box's contents.
    const words = await box.evaluate((element) => {
        const range = element.ownerDocument.createRange();
        range.selectNodeContents(element);
        const first = range.getClientRects()[0];
        if (!first) return null;
        return {
            x: first.x,
            y: first.y,
            width: first.width,
            height: first.height,
        };
    });
    if (!words)
        throw new Error(
            `The "${languageTag}" box in the cell at row ${row}, column ${column} draws no text, ` +
                `so there is nothing to drag across. It holds ` +
                `"${(await box.innerText()).trim()}".`,
        );
    // The rectangle is in the page iframe's coordinates and the mouse works in the window's, so
    // the drag has to be shifted by where that iframe sits.
    const offset = await pageFrameOffset(page);
    const y = offset.y + words.y + words.height / 2;
    await page.mouse.move(offset.x + words.x + 1, y);
    await page.mouse.down();
    await page.mouse.move(offset.x + words.x + words.width - 1, y, {
        steps: 10,
    });
    await page.mouse.up();
    const frame = editablePageFrame(page);
    const selection = async () =>
        frame.evaluate(() => window.getSelection()?.toString() ?? "");
    // The selection is the browser's answer to the drag, and it arrives with the events rather
    // than with the mouse up. What it ends up as is the caller's to judge, so a drag that selects
    // nothing is returned as nothing rather than thrown here.
    await expect
        .poll(selection, { timeout: 10000 })
        .not.toBe("")
        .catch(() => undefined);
    return selection();
}

/**
 * Assert that the picture in a cell is drawn inside that cell. This is the geometric form of "the
 * picture did not spill out of its box", and it says nothing about how big the picture should be.
 */
export async function expectPictureInsideCell(
    page: Page,
    row: number,
    column: number,
    tableIndex = 0,
): Promise<void> {
    const target = await cell(page, row, column, tableIndex);
    const picture = target.locator(".bloom-imageContainer img").first();
    await picture.waitFor({ state: "visible", timeout: kTableWaitMs });
    const rects = await picture.evaluate((image) => {
        const asRect = (element: Element) => {
            const box = element.getBoundingClientRect();
            return { x: box.x, y: box.y, width: box.width, height: box.height };
        };
        const cellElement = image.closest(".bloom-cell");
        return {
            picture: asRect(image),
            cell: cellElement ? asRect(cellElement) : null,
        };
    });
    expect(
        rects.cell,
        `The picture at row ${row}, column ${column} is not inside a table cell at all.`,
    ).not.toBeNull();
    expectInside(
        rects.picture,
        rects.cell!,
        "the picture",
        `the cell at row ${row}, column ${column}`,
    );
}

/**
 * Add a row below the first one from the table's row menu, check the table gained it, then undo
 * and check the table is back to the rows it had. This is how a test shows that a table is still
 * wired to the table library after something rebuilt the page around it (leaving the page and
 * coming back, or leaving Change Layout mode): a table Bloom has not re-attached shows no menu.
 *
 * Through the row menu rather than the bottom "+" button, because the canvas element's toolbar
 * can cover that button on a table on a canvas page. `why` finishes the failure message, e.g.
 * "after leaving the page and coming back".
 */
export async function addRowFromMenuThenUndo(
    page: Page,
    why: string,
    tableIndex = 0,
): Promise<void> {
    const rowsBefore = (await getTableShape(page, tableIndex)).rows;
    await clickCell(page, 0, 0, tableIndex);
    await openTableMenu(page, "row", 0, tableIndex);
    await clickTableMenuCommand(page, "Add Row Below");
    await expect
        .poll(async () => (await getTableShape(page, tableIndex)).rows, {
            message: `The row menu's Add Row Below should have added a row ${why}.`,
        })
        .toBe(rowsBefore + 1);
    await undo(page);
    await expect
        .poll(async () => (await getTableShape(page, tableIndex)).rows, {
            message: `Undo should have taken away the row that was added ${why}.`,
        })
        .toBe(rowsBefore);
}

/**
 * Which of the table editing markup's selectors (kTableEditingMarkupSelectors in bookHtml.ts)
 * match something in the page being edited right now, chrome in the page's body included. A test
 * reads this before a save, to show that the markup the save must strip was really there.
 */
export async function getTableEditingMarkup(page: Page): Promise<string[]> {
    return editablePageFrame(page).evaluate(
        (selectors) =>
            selectors.filter(
                (selector) => document.querySelector(selector) !== null,
            ),
        kTableEditingMarkupSelectors,
    );
}

/** What the menus call a language a table cell can show (BL-17105). */
export type CellLanguageRole = "L1" | "L2" | "L3";

// The role each menu entry stands for, as TableCellLanguagesSection.tsx marks its row.
const kStoredRoleOfMenuRole: Record<CellLanguageRole, string> = {
    L1: "V",
    L2: "N1",
    L3: "N2",
};

/** One entry of a table menu's Languages submenu, as the user sees it. */
export interface IMenuCellLanguage {
    /** The whole label, e.g. "L1 - English". */
    label: string;
    /** Ticked in every text cell the menu covers, in none, or in only some (a dash). */
    state: "all" | "none" | "some";
    /** True when unticking it would leave some cell with no language, so the box is disabled. */
    locked: boolean;
}

// The Languages item is Bloom's own, drawn into the library's menu, and its check boxes are in a
// submenu that opens while the pointer is on the item. These attributes are the ones
// TableCellLanguagesSection.tsx puts on them for this purpose.
async function openLanguagesSubmenu(page: Page): Promise<Locator> {
    const frame = editablePageFrame(page);
    const item = frame
        .locator(
            "[data-btable-menu]:visible [data-table-cell-languages] [role='menuitem']",
        )
        .first();
    await item.waitFor({ state: "visible", timeout: kTableWaitMs });
    await item.hover({ timeout: kTableWaitMs });
    const submenu = frame
        .locator("[data-table-cell-languages-submenu]")
        .first();
    await submenu.waitFor({ state: "visible", timeout: kTableWaitMs });
    return submenu;
}

/**
 * The entries of the Languages submenu of whichever table menu is open (Cell, Row, Column or
 * Table), in the order the submenu lists them. Opens the submenu by pointing at its item, as a
 * person does, and leaves it open.
 */
export async function getMenuCellLanguages(
    page: Page,
): Promise<IMenuCellLanguage[]> {
    const section = await openLanguagesSubmenu(page);
    return section.locator("label[data-cell-language]").evaluateAll((rows) =>
        rows.map((row) => ({
            label: (row.textContent ?? "").trim(),
            state: row.getAttribute("data-cell-language-state") as
                | "all"
                | "none"
                | "some",
            locked: !!row.querySelector("input:disabled"),
        })),
    );
}

/**
 * Click the check box of one language in the open table menu's Languages submenu, as a person
 * does, and wait until the submenu shows the result. The menu and the submenu stay open.
 */
export async function toggleMenuCellLanguage(
    page: Page,
    role: CellLanguageRole,
): Promise<void> {
    const section = await openLanguagesSubmenu(page);
    const row = section.locator(
        `label[data-cell-language='${kStoredRoleOfMenuRole[role]}']`,
    );
    if ((await row.count()) === 0) {
        const labels = await section
            .locator("label[data-cell-language]")
            .allInnerTexts();
        throw new Error(
            `The open table menu's Languages submenu has no ${role}. It lists: ` +
                `${labels.join(", ") || "(nothing)"}.`,
        );
    }
    const before = await row.getAttribute("data-cell-language-state");
    await clickChrome(row.locator("input"), `the ${role} check box`);
    // Unticking takes the language out of every cell where it is not the only one, so the box
    // can end up "some" rather than "none"; any change of state means the click took.
    await expect
        .poll(async () => await row.getAttribute("data-cell-language-state"), {
            timeout: kTableWaitMs,
            message: `Clicking the ${role} check box did not change it from "${before}".`,
        })
        .not.toBe(before);
}

/**
 * Tick or untick one language in one cell's Cell menu, as a person does: right-click the cell,
 * click the language's check box in the Languages submenu, and close the menu.
 */
export async function toggleCellLanguageInCellMenu(
    page: Page,
    row: number,
    column: number,
    role: CellLanguageRole,
    tableIndex = 0,
): Promise<void> {
    await rightClickCell(page, row, column, tableIndex);
    await toggleMenuCellLanguage(page, role);
    await closeAnyMenu(page);
}

/**
 * The languages every text cell of a table shows, row by row: for each cell, the tags of its
 * shown boxes in the order they are drawn, as getCellLanguages gives one cell. Reads the whole
 * table at once, for a test that checks many cells.
 */
export async function getTableCellLanguages(
    page: Page,
    tableIndex = 0,
): Promise<string[][][]> {
    const table = editablePageFrame(page)
        .locator(".bloom-table")
        .nth(tableIndex);
    await table.waitFor({ state: "attached", timeout: kTableWaitMs });
    return table.evaluate((element) => {
        const widths = element.getAttribute("data-column-widths") ?? "";
        // An empty attribute means no columns yet.
        if (widths === "") return [];
        const columns = widths.split(",").length;
        const cells = Array.from(
            element.querySelectorAll(":scope > .bloom-cell"),
        ).map((cellElement) =>
            Array.from(
                cellElement.querySelectorAll(
                    ".bloom-editable.bloom-visibility-code-on",
                ),
            ).map((box) => box.getAttribute("lang") ?? ""),
        );
        const rows: string[][][] = [];
        for (let start = 0; start < cells.length; start += columns)
            rows.push(cells.slice(start, start + columns));
        return rows;
    });
}

/**
 * Copy one cell's format onto another with Paint format, as a person does: choose Paint format in
 * the source cell's Cell menu, press on the target cell, and press Escape to stop painting.
 * Returns when the table has left its painting mode.
 *
 * While painting, the table library marks the page's body `bloom-paint-format` and takes every
 * press on a cell as "paint this one", so this waits for the mark before the press and for its
 * removal afterwards.
 */
export async function paintFormatFromCellOntoCell(
    page: Page,
    from: { row: number; column: number },
    onto: { row: number; column: number },
    tableIndex = 0,
): Promise<void> {
    const menus = await rightClickCell(page, from.row, from.column, tableIndex);
    if (!menus.cell)
        throw new Error(
            `Right-clicking the cell at row ${from.row}, column ${from.column} did not open the ` +
                `Cell menu. Menus that did open: ${describeMenus(menus)}.`,
        );
    await clickTableMenuCommand(page, "Paint format");
    const body = editablePageFrame(page).locator("body");
    await expect(
        body,
        "Choosing Paint format did not put the table into its painting mode.",
    ).toHaveClass(/\bbloom-paint-format\b/, { timeout: kTableWaitMs });
    // A real press, for the same reason as in clickCell.
    await realClick(await cell(page, onto.row, onto.column, tableIndex));
    await body.press("Escape");
    await expect(
        body,
        "Escape did not end the table's painting mode.",
    ).not.toHaveClass(/\bbloom-paint-format\b/, { timeout: kTableWaitMs });
}

/**
 * Copy the properties of the table's first cell and paste them onto every cell of the table, with
 * Copy properties and then Paste properties in the Table menu, which is the only menu that has
 * them. Copy takes the first cell of the table; Paste gives every cell what was copied.
 */
export async function copyAndPasteTableProperties(
    page: Page,
    tableIndex = 0,
): Promise<void> {
    await openTableMenu(page, "table", 0, tableIndex);
    await clickTableMenuCommand(page, "Copy properties");
    await openTableMenu(page, "table", 0, tableIndex);
    await clickTableMenuCommand(page, "Paste properties");
}

/**
 * Which way the text of one language's box in a cell runs, "ltr" or "rtl", as the browser lays it
 * out. This is the computed direction, so it is what a person sees whichever of the attribute and
 * the stylesheet decided it.
 */
export async function getCellTextDirection(
    page: Page,
    row: number,
    column: number,
    languageTag: string,
    tableIndex = 0,
): Promise<string> {
    const box = await cellTextBox(page, row, column, languageTag, tableIndex);
    return box.evaluate((element) => getComputedStyle(element).direction);
}

/**
 * The languages whose text a reader sees in one cell of the first table of a page in `frame`, in
 * the order they are drawn. Written for the BloomPUB preview, whose frame is bloom-player showing
 * the book: a language counts only when its box is really drawn there, with a size, so one that
 * the player hides is left out however the saved page marks it.
 *
 * `pageId` scopes it to one page, because bloom-player builds only the pages near the one it is
 * showing. Waits until that page's table is there.
 */
export async function getCellLanguagesShownIn(
    frame: Frame,
    pageId: string,
    row: number,
    column: number,
): Promise<string[]> {
    const target = frame.locator(`[id="${pageId}"] .bloom-table`).first();
    await target.waitFor({ state: "attached", timeout: 30000 });
    return target.evaluate(
        (element, where) => {
            const columns = (
                element.getAttribute("data-column-widths") ?? ""
            ).split(",").length;
            const cells = Array.from(
                element.querySelectorAll(":scope > .bloom-cell"),
            );
            const cellElement = cells[where.row * columns + where.column];
            if (!cellElement)
                throw new Error(
                    `The table has ${cells.length} cells in ${columns} columns, so there is ` +
                        `no cell at row ${where.row}, column ${where.column}.`,
                );
            return Array.from(cellElement.querySelectorAll(".bloom-editable"))
                .filter((box) => {
                    const rect = box.getBoundingClientRect();
                    return (
                        (box as HTMLElement).checkVisibility({
                            visibilityProperty: true,
                        }) &&
                        rect.width > 0 &&
                        rect.height > 0
                    );
                })
                .map((box) => box.getAttribute("lang") ?? "");
        },
        { row, column },
    );
}

// ── What the table's own UI says ────────────────────────────────────────

/** One piece of wording the table's own UI is showing, and what it was read from. */
export interface ITableUiText {
    /** The wording itself. Empty for a control that has no name at all. */
    text: string;
    /** Where it was read: the element, and whether it is its text, tooltip or accessible name. */
    where: string;
}

/**
 * Everything a person can read on the table's own UI as it stands right now: the round menu
 * buttons, the "+" buttons, any open Row, Column, Table or Cell menu, and the badge that Paint
 * format puts above the table. For each element showing, this gives its text, its tooltip
 * (`title`) and its accessible name (`aria-label`), which is what a screen reader says.
 *
 * A button, menu command, tab or slider that has none of the three comes back with empty text, so
 * a caller can tell a control with no name from one that is absent.
 *
 * This reads only what is showing, so open the menu, or bring the pointer to the table, first.
 */
export async function getTableUiTexts(page: Page): Promise<ITableUiText[]> {
    return editablePageFrame(page).evaluate(() => {
        const found: { text: string; where: string }[] = [];
        const describe = (element: Element) => {
            const hints = [
                "role",
                "data-label-id",
                "data-btable-menu-pill",
                "data-btable-menu",
                "data-table-overlay",
                "data-scope",
                "data-ct-id",
                "type",
            ]
                .filter((name) => element.hasAttribute(name))
                .map((name) => `${name}="${element.getAttribute(name)}"`);
            return `<${[element.tagName.toLowerCase(), ...hints].join(" ")}>`;
        };
        // Everything the library draws around a table is inside an element it marks with
        // data-table-overlay. Start from the outermost of those, so nothing is read twice.
        const roots = Array.from(
            document.querySelectorAll("[data-table-overlay]"),
        ).filter(
            (element) =>
                !element.parentElement?.closest("[data-table-overlay]"),
        );
        for (const root of roots) {
            const elements = [root, ...Array.from(root.querySelectorAll("*"))];
            for (const element of elements) {
                if (!element.checkVisibility({ visibilityProperty: true }))
                    continue;
                // A drawing has no wording of its own.
                if (element.closest("svg")) continue;
                const names: string[] = [];
                for (const attribute of [
                    "aria-label",
                    "title",
                    "placeholder",
                ]) {
                    const value = element.getAttribute(attribute)?.trim();
                    if (value) {
                        names.push(value);
                        found.push({
                            text: value,
                            where: `${attribute} of ${describe(element)}`,
                        });
                    }
                }
                for (const node of Array.from(element.childNodes)) {
                    const text =
                        node.nodeType === Node.TEXT_NODE
                            ? (node.textContent ?? "").trim()
                            : "";
                    if (text)
                        found.push({
                            text,
                            where: `text of ${describe(element)}`,
                        });
                }
                const isControl = element.matches(
                    'button, input, select, [role="menuitem"], [role="menuitemradio"], [role="tab"], [role="radio"]',
                );
                if (
                    isControl &&
                    names.length === 0 &&
                    !(element.textContent ?? "").trim()
                )
                    found.push({ text: "", where: describe(element) });
            }
        }
        return found;
    });
}

/**
 * Bring the pointer onto a table, near its bottom edge, and wait until the table shows its "+"
 * buttons and its round menu buttons there, which is what a person sees on moving onto a table
 * they may change the shape of.
 *
 * Finds the buttons by what they are, not by their wording, so it works in any UI language.
 */
export async function showTableButtons(
    page: Page,
    tableIndex = 0,
): Promise<void> {
    await movePointerOntoTable(page, tableIndex);
    await expectTableButtonsShowing(page);
}

/**
 * Bring the pointer onto a table, just inside the bottom edge of its last row, and wait for nothing. Where the pointer
 * goes is where showTableButtons puts it, so the table would show its buttons there.
 */
export async function movePointerOntoTable(
    page: Page,
    tableIndex = 0,
): Promise<void> {
    // Aim by the cells, not by the table element: a table that fills a page section is taller
    // than its rows, and the table notices the pointer by its distance from the cells.
    const cells = (await measureTable(page, tableIndex)).cells;
    if (cells.length === 0)
        throw new Error(
            "The table has no cells, so there is nowhere on it to point.",
        );
    const left = Math.min(...cells.map((c) => c.rect.x));
    const rightEdge = Math.max(...cells.map((c) => right(c.rect)));
    const bottomEdge = Math.max(...cells.map((c) => bottom(c.rect)));
    await page.mouse.move((left + rightEdge) / 2, bottomEdge - 8);
}

/** How many of the table's "+" buttons and round menu buttons are showing right now. */
export async function getTableButtonsShowingCount(page: Page): Promise<number> {
    return editablePageFrame(page)
        .locator(
            'button[data-table-overlay="add-button"]:visible, button[data-btable-menu-pill]:visible',
        )
        .count();
}

/**
 * Wait until the table is showing a "+" button and a round menu button, without moving the
 * pointer. Fails if they do not come.
 */
export async function expectTableButtonsShowing(page: Page): Promise<void> {
    const frame = editablePageFrame(page);
    await expect(
        frame.locator('button[data-table-overlay="add-button"]:visible'),
        'The table did not show a "+" button with the pointer resting on it.',
    ).not.toHaveCount(0, { timeout: kTableWaitMs });
    await expect(
        frame.locator("button[data-btable-menu-pill]:visible"),
        "The table did not show a round menu button with the pointer resting on it.",
    ).not.toHaveCount(0, { timeout: kTableWaitMs });
}

/** The table menu that is open, whichever it is. */
function openTableMenuPanel(page: Page): Locator {
    return editablePageFrame(page)
        .locator("[data-btable-menu]:visible")
        .first();
}

/** The tabs above a menu's border controls, by the scope each one stands for. */
export type BorderScope = "all" | "outer" | "inner" | "brush";

/**
 * The border tabs the open table menu offers, in the order it shows them. A menu for something
 * with no inside edges (a single cell) offers fewer than four.
 */
export async function getBorderScopesOffered(
    page: Page,
): Promise<BorderScope[]> {
    return openTableMenuPanel(page)
        .locator('[role="tab"][data-scope]:visible')
        .evaluateAll((tabs) =>
            tabs.map((tab) => tab.getAttribute("data-scope") as BorderScope),
        );
}

/**
 * Click one of the border tabs (All, Outer, Inner, Border Brush) in the open table menu, and wait
 * until the menu marks it as the chosen one. The explanation under the tabs changes with it.
 */
export async function chooseBorderScope(
    page: Page,
    scope: BorderScope,
): Promise<void> {
    const tab = openTableMenuPanel(page).locator(
        `[role="tab"][data-scope="${scope}"]`,
    );
    await clickChrome(tab, `the "${scope}" border tab`);
    await expect(
        tab,
        `Clicking the "${scope}" border tab did not make it the chosen one.`,
    ).toHaveAttribute("aria-selected", "true", { timeout: kTableWaitMs });
}

/**
 * Click the control in the open table menu whose accessible name is `label`, such as the Size
 * control's "Fixed size" button, and leave the menu as the click leaves it.
 *
 * `label` is the wording in the UI language Bloom is running in; get it from getUiStrings
 * (helpers/uiLanguage.ts) by the string's id, so the test names no English.
 */
export async function clickTableMenuControl(
    page: Page,
    label: string,
): Promise<void> {
    const menu = openTableMenuPanel(page);
    const control = menu.locator(`[aria-label=${JSON.stringify(label)}]`);
    if ((await control.count()) === 0) {
        const named = await menu
            .locator("[aria-label]")
            .evaluateAll((items) =>
                items.map((item) => item.getAttribute("aria-label")),
            );
        throw new Error(
            `The open table menu has nothing named "${label}". It names: ${named.join(", ")}.`,
        );
    }
    await clickChrome(control.first(), `"${label}" in the table menu`);
}

/** The badge above the table that says format painting is on, and ends it when clicked. */
function paintFormatBadge(page: Page): Locator {
    return editablePageFrame(page).locator(
        '[data-table-overlay="paint-format-badge"]',
    );
}

/**
 * Choose Paint format from the open table menu and wait for the badge it puts above the table.
 * `commandLabel` is the command's wording in the UI language Bloom is running in (see
 * clickTableMenuControl). End it with endPaintFormat.
 */
export async function startPaintFormat(
    page: Page,
    commandLabel: string,
): Promise<void> {
    await clickTableMenuControl(page, commandLabel);
    await expect(
        paintFormatBadge(page),
        `Choosing "${commandLabel}" did not put the format painting badge above the table.`,
    ).toBeVisible({ timeout: kTableWaitMs });
}

/** End format painting by clicking its badge, and wait for the badge to go. */
export async function endPaintFormat(page: Page): Promise<void> {
    await clickChrome(paintFormatBadge(page), "the format painting badge");
    await expect(
        paintFormatBadge(page),
        "Clicking the format painting badge did not end format painting.",
    ).toHaveCount(0, { timeout: kTableWaitMs });
}

// ── The two answers a table waits for when a page opens ─────────────────

/**
 * The two things a page asks Bloom before a table shows its buttons: whether this user may change
 * the shape of tables ("permission", the features/status answer for tables), and the wording of
 * the table's UI in the UI language ("wording", the i18n/loadStrings answer for the table's
 * strings).
 */
export type TableStartupAnswer = "permission" | "wording";

const kFeatureStatusRequest = /\/bloom\/api\/features\/status\?/;
const kLoadStringsRequest = /\/bloom\/api\/i18n\/loadStrings/;
const isTablePermissionRequest = (request: Request) =>
    /[?&]featureName=table(&|$)/.test(request.url());
const isTableWordingRequest = (request: Request) =>
    (request.postData() ?? "").includes("EditTab.Table.Pill.");

/**
 * Hold back one of the two answers a table waits for (see TableStartupAnswer) on the pages opened
 * from now on, so a test can bring the pointer to a table before that answer has arrived. Open the
 * page with the table after calling this, then await `onlyTheseAreMissing()`: it returns once the
 * page has asked for the held-back answer and has had the other one, so the held-back answer is
 * all the table still lacks. `release()` lets Bloom's answer through; call it in a `finally`.
 */
export async function holdBackTableStartupAnswer(
    page: Page,
    which: TableStartupAnswer,
): Promise<IHeldBackAnswers> {
    return which === "permission"
        ? holdBackAnswers(
              page,
              kFeatureStatusRequest,
              isTablePermissionRequest,
              kLoadStringsRequest,
              isTableWordingRequest,
          )
        : holdBackAnswers(
              page,
              kLoadStringsRequest,
              isTableWordingRequest,
              kFeatureStatusRequest,
              isTablePermissionRequest,
          );
}

/**
 * Bring the pointer onto a table as movePointerOntoTable does, give the page the chance to draw
 * what that calls for, and return how many of the table's "+" buttons and round menu buttons are
 * showing. For the moment in which a table should be showing none.
 */
export async function countTableButtonsAfterMovingOntoTable(
    page: Page,
    tableIndex = 0,
): Promise<number> {
    await movePointerOntoTable(page, tableIndex);
    // Two frames: the library decides what to show in answer to the pointer, and the page then
    // draws it. Counting before that would find no buttons whatever the table meant to show.
    await editablePageFrame(page).evaluate(
        () =>
            new Promise<void>((resolve) =>
                requestAnimationFrame(() =>
                    requestAnimationFrame(() => resolve()),
                ),
            ),
    );
    return getTableButtonsShowingCount(page);
}
