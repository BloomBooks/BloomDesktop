// Text that does not fit a table cell (BL-17110). A row whose height was set by dragging its
// boundary keeps that height whatever is put in it, so a second language in a short row has no
// room. Bloom then cuts the text off at the edge of the cell's content area, as it does in a
// split-pane text box, rather than letting it hang over the row below, and marks the cell red.
// When the row is given room again (Grow, Hug, or a drag), the mark goes. Text only has to fit its
// cell: rows dragged taller than the table's own box are no reason for a mark.
//
// The tests are serial and share one book: each starts from the state the one before it left, so
// the first failure is the one to read. Tables are a Pro-tier feature behind the "tables"
// experiment; see tables-core.spec.ts for why the collection is given both this way.

import { expect, test } from "../fixtures/bloomTest";
import { setContentLanguages } from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    dragBoundary,
    expectTableSettlesUnmarked,
    getCellLanguages,
    getCellTextClipping,
    makeBookWithTableOnCanvasPage,
    measureTable,
    setRowSize,
    typeInCell,
    waitForCellOverflowMarks,
    waitForTableAttached,
} from "../helpers/tables";

test.use({
    collectionSpec: {
        name: "tables-cell-overflow",
        languages: ["en", "fr"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test.describe.configure({ mode: "serial" });

// The cell this file fills: the top row, so that what overflows it would land on the row below.
const ROW = 0;
const COLUMN = 1;

// Far enough to leave a two-language cell too short for both languages, and too short for nothing
// else: a new table's rows are taller than this by a good margin, and one line of text still fits
// in what is left.
const SHRINK_ROW_BY = -50;
// Far enough to give the two languages their room back.
const GROW_ROW_BY = 120;

test.describe("text that does not fit a table cell", () => {
    test("a short row cuts off a second language and marks the cell", async ({
        page,
        step,
    }) => {
        // This test also pays for launching Bloom and making the book.
        test.setTimeout(300000);
        await step(
            "Make a book with a table, showing two languages",
            async () => {
                await makeBookWithTableOnCanvasPage(page, ["en"]);
                // Every text cell shows each of the book's content languages.
                await setContentLanguages(page, ["en", "fr"]);
                await waitForTableAttached(page);
                expect(await getCellLanguages(page, ROW, COLUMN)).toEqual([
                    "en",
                    "fr",
                ]);
                // Text to see: an empty box draws nothing, cut off or not.
                await typeInCell(page, ROW, COLUMN, "en", "English");
                await typeInCell(page, ROW, COLUMN, "fr", "Français");
            },
        );

        await step("Check the new table's row has room for both", async () => {
            await waitForCellOverflowMarks(page, ROW, COLUMN, false);
            expect(
                (await getCellTextClipping(page, ROW, COLUMN, "fr"))
                    .reachesPastCell,
                "A new table's row should be tall enough for two languages.",
            ).toBe(false);
        });

        await step("Drag the row's bottom boundary up", async () => {
            await dragBoundary(page, "row", ROW, SHRINK_ROW_BY);
        });

        await step(
            "Check L2 is cut off at the cell's edge, and the cell is marked",
            async () => {
                await waitForCellOverflowMarks(page, ROW, COLUMN, true);
                const clipping = await getCellTextClipping(
                    page,
                    ROW,
                    COLUMN,
                    "fr",
                );
                expect(
                    clipping.reachesPastCell,
                    "The short row should leave L2 without room, or this test checks nothing.",
                ).toBe(true);
                expect(
                    clipping.partPastCellShows,
                    "The part of L2 past the cell's edge should be cut off, not drawn over the row below.",
                ).toBe(false);
            },
        );
    });

    test("choosing Grow for the row clears the mark", async ({ page }) => {
        await setRowSize(page, ROW, "Grow");
        await waitForCellOverflowMarks(page, ROW, COLUMN, false);
        expect(
            (await getCellTextClipping(page, ROW, COLUMN, "fr"))
                .reachesPastCell,
        ).toBe(false);
    });

    test("dragging the row's boundary down again clears the mark", async ({
        page,
    }) => {
        await dragBoundary(page, "row", ROW, SHRINK_ROW_BY);
        await waitForCellOverflowMarks(page, ROW, COLUMN, true);

        await dragBoundary(page, "row", ROW, GROW_ROW_BY);
        await waitForCellOverflowMarks(page, ROW, COLUMN, false);
    });

    test("choosing Hug for the row clears the mark", async ({ page }) => {
        await dragBoundary(page, "row", ROW, -GROW_ROW_BY);
        await waitForCellOverflowMarks(page, ROW, COLUMN, true);

        await setRowSize(page, ROW, "Hug");
        await waitForCellOverflowMarks(page, ROW, COLUMN, false);
        expect(
            (await getCellTextClipping(page, ROW, COLUMN, "fr"))
                .reachesPastCell,
        ).toBe(false);
    });

    test("rows dragged taller than the table's box are not marked when their text fits", async ({
        page,
    }) => {
        const lastRow = 1;
        await typeInCell(page, lastRow, COLUMN, "fr", "Français");

        // Both rows fixed, as dragging leaves them. A Hug row would be squeezed to its minimum
        // once the rows below it run past the table's box (BL-17111), and then its text really
        // would not fit.
        await dragBoundary(page, "row", ROW, GROW_ROW_BY);
        await waitForCellOverflowMarks(page, ROW, COLUMN, false);
        // The last row's bottom boundary is the table's bottom edge.
        await dragBoundary(page, "row", lastRow, 2 * GROW_ROW_BY);
        const measured = await measureTable(page);
        const lastCell = measured.cells.find(
            (c) => c.row === lastRow && c.column === COLUMN,
        )!;
        expect(
            lastCell.rect.y + lastCell.rect.height,
            "The last row should now run past the table's own box, or this tests nothing.",
        ).toBeGreaterThan(measured.rect.y + measured.rect.height);

        await expectTableSettlesUnmarked(page);
    });
});
