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

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    setContentLanguages,
    waitForPageToSettle,
} from "../helpers/bookMaking";
import {
    getCanvasRect,
    isCanvasMarkedAsOverflowed,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { bottom } from "../helpers/geometry";
import {
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    beginBoundaryDrag,
    clickCell,
    clickTableMenuCommand,
    dragBoundary,
    dragTableBy,
    endBoundaryDrag,
    expectOverflowMarksMatchWhatFits,
    expectTableSettlesUnmarked,
    getCellLanguages,
    getCellOverflowMarks,
    getCellTextBottom,
    getCellTextClipping,
    getCellTextCutOffIn,
    getTableShape,
    makeBookWithTableOnCanvasPage,
    measureTable,
    moveBoundaryDragTo,
    openTableMenu,
    setRowSize,
    typeInCell,
    waitForCellOverflowMarks,
    waitForTableAttached,
} from "../helpers/tables";
import { undo } from "../helpers/undo";
import { getZoom, setZoom, switchTab } from "../helpers/workspace";

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

/**
 * Make a book whose Canvas page holds a new table, showing English and French, and type
 * `english` and a French word into the cell this file fills.
 */
async function makeBookWithTwoLanguageTable(
    page: Page,
    english: string,
): Promise<void> {
    // The table has to be made while only English shows: a table made while two languages show
    // gets cells that show only the first. Showing French afterwards gives every text cell a box
    // for each of the book's content languages.
    await makeBookWithTableOnCanvasPage(page, ["en"]);
    await setContentLanguages(page, ["en", "fr"]);
    await waitForTableAttached(page);
    expect(await getCellLanguages(page, ROW, COLUMN)).toEqual(["en", "fr"]);
    // Text to see: an empty box draws nothing, cut off or not. French first: English that takes
    // several lines can push the French box out of the cell, where a click cannot reach it.
    await typeInCell(page, ROW, COLUMN, "fr", "Français");
    await typeInCell(page, ROW, COLUMN, "en", english);
}

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
                await makeBookWithTwoLanguageTable(page, "English");
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

// The marks have to be right at every moment, not only once a drag is over: while the mouse button
// is still down on a boundary, and straight after an undo or a row is added or deleted. These
// tests share a second book, whose English text is a sentence, so that it takes more lines in a
// narrower column.
test.describe("overflow marks follow a table as it changes", () => {
    // Enough short words to wrap onto more lines in any narrower column.
    const SENTENCE = "one two three four five six seven eight nine ten";
    // Room left in a row that hugs its text and is then fixed by a drag.
    const ROOM_TO_SPARE = 10;

    /**
     * Hold the mouse button down on a boundary, move it by `tooSmallAt`, check that the cell is
     * marked, move it back to where it started, check that the mark has gone, and only then let
     * go of the button.
     */
    async function expectMarkFollowsDrag(
        page: Page,
        what: "row" | "column",
        index: number,
        tooSmallAt: number,
    ): Promise<void> {
        await waitForCellOverflowMarks(page, ROW, COLUMN, false);
        const drag = await beginBoundaryDrag(page, what, index);
        try {
            await moveBoundaryDragTo(page, drag, tooSmallAt);
            await waitForCellOverflowMarks(page, ROW, COLUMN, true);
            await moveBoundaryDragTo(page, drag, 0);
            await waitForCellOverflowMarks(page, ROW, COLUMN, false);
        } finally {
            await endBoundaryDrag(page);
        }
    }

    test("the mark follows a row's boundary while the mouse button is still down", async ({
        page,
        step,
    }) => {
        // This test also pays for making the second book.
        test.setTimeout(300000);
        await step("Make a second book with a table", async () => {
            await makeBookWithTwoLanguageTable(page, SENTENCE);
        });
        await step(
            "Give the row a fixed height with a little room to spare",
            async () => {
                await setRowSize(page, ROW, "Hug");
                await dragBoundary(page, "row", ROW, ROOM_TO_SPARE);
            },
        );
        await step(
            "Drag the row's boundary up and back without letting go",
            async () => {
                await expectMarkFollowsDrag(page, "row", ROW, SHRINK_ROW_BY);
            },
        );
    });

    test("the mark follows a column's boundary while the mouse button is still down", async ({
        page,
        step,
    }) => {
        const narrowBy = await step(
            "Work out how far leaves the column a third as wide",
            async () => {
                const measured = await measureTable(page);
                const width = measured.cells.find(
                    (c) => c.row === ROW && c.column === COLUMN,
                )!.rect.width;
                return -Math.round((width * 2) / 3);
            },
        );
        if (narrowBy === undefined)
            throw new Error("The column was never measured.");
        await step(
            "Drag the column's boundary left and back without letting go",
            async () => {
                // The row keeps its height, so the sentence wraps onto more lines than it has
                // room for.
                await expectMarkFollowsDrag(page, "column", COLUMN, narrowBy);
            },
        );
    });

    test("the marks are right after each Undo", async ({ page, step }) => {
        // The row's recorded height at each stage, to tell what an undo put back.
        const rowHeight = async () =>
            (await getTableShape(page)).rowHeights[ROW];
        const withRoom = await rowHeight();
        const tooShort = await step(
            "Drag the row short, then tall again",
            async () => {
                await dragBoundary(page, "row", ROW, SHRINK_ROW_BY);
                await waitForCellOverflowMarks(page, ROW, COLUMN, true);
                const short = await rowHeight();
                await dragBoundary(page, "row", ROW, GROW_ROW_BY);
                await waitForCellOverflowMarks(page, ROW, COLUMN, false);
                return short;
            },
        );
        await step("Undo: the row is short again, and marked", async () => {
            await undo(page);
            await expect
                .poll(rowHeight, {
                    message:
                        "The undo should have made the row too short again.",
                })
                .toBe(tooShort);
            await waitForCellOverflowMarks(page, ROW, COLUMN, true);
        });
        await step("Undo again: the row has room, and no mark", async () => {
            await undo(page);
            await expect
                .poll(rowHeight, {
                    message:
                        "The second undo should have given the row its room back.",
                })
                .toBe(withRoom);
            await waitForCellOverflowMarks(page, ROW, COLUMN, false);
        });
    });

    // The table keeps its height, so a row added to it takes room from the rows that grow to
    // fill it, and deleting that row gives the room back. Neither is a drag, and neither changes
    // the text, so the marks are right afterwards only if the operation itself has them checked.
    test("the marks are right after a row is added and after one is deleted", async ({
        page,
        step,
    }) => {
        const sentenceCell = { row: ROW, column: COLUMN };
        const rowsBefore = (await getTableShape(page)).rows;
        await step("Let both rows share the table's height", async () => {
            await setRowSize(page, ROW, "Grow");
            expect(
                await expectOverflowMarksMatchWhatFits(page),
                "With the rows sharing the table, only the cell holding the sentence should be too small.",
            ).toEqual([sentenceCell]);
        });
        await step("Add a row above the row that overflows", async () => {
            await clickCell(page, ROW, 0);
            await openTableMenu(page, "row", ROW);
            await clickTableMenuCommand(page, "Add Row Above");
            await expect
                .poll(async () => (await getTableShape(page)).rows)
                .toBe(rowsBefore + 1);
            const marked = await expectOverflowMarksMatchWhatFits(page);
            expect(
                marked,
                "The cell that overflowed is one row lower now, and should still be marked.",
            ).toContainEqual({ row: ROW + 1, column: COLUMN });
            expect(
                marked.length,
                "The new row should have left other cells too small as well, or this test checks nothing.",
            ).toBeGreaterThan(1);
        });
        await step("Delete the new row", async () => {
            await openTableMenu(page, "row", ROW);
            await clickTableMenuCommand(page, "Delete Row");
            await expect
                .poll(async () => (await getTableShape(page)).rows)
                .toBe(rowsBefore);
            expect(
                await expectOverflowMarksMatchWhatFits(page),
                "With the new row gone, only the cell holding the sentence should be too small again.",
            ).toEqual([sentenceCell]);
        });
    });

    test("a BloomPUB cuts off text that does not fit its cell", async ({
        page,
        step,
    }) => {
        await step(
            "Drag the row too short for the second language",
            async () => {
                // Short enough that the French box is left reaching past the cell. A row a little
                // too short only makes the English box shrink, and that box then scrolls.
                await dragBoundary(page, "row", ROW, SHRINK_ROW_BY);
                await waitForCellOverflowMarks(page, ROW, COLUMN, true);
                expect(
                    (await getCellTextClipping(page, ROW, COLUMN, "fr"))
                        .reachesPastCell,
                    "The row should leave the second language without room, or this test checks nothing.",
                ).toBe(true);
            },
        );
        const player = await step("Open the BloomPUB preview", async () => {
            await openPublishDestination(page, "BloomPUB");
            return showBloomPubPreview(page);
        });
        if (!player) throw new Error("The BloomPUB preview never opened.");
        await step("Check the cell cuts off its text there too", async () => {
            const cutOff = await getCellTextCutOffIn(player, ROW, COLUMN);
            expect(
                cutOff.reachesPastCell,
                "The published row should be too short for its text, or this test checks nothing.",
            ).toBe(true);
            expect(
                cutOff.cellCutsItOff,
                "The published cell should cut off the text that does not fit it.",
            ).toBe(true);
        });
        await step("Go back to the Collection tab", async () => {
            await switchTab(page, "collection");
        });
    });
});

// Where a canvas element is decides whether its text runs past the picture it is on, so Bloom checks
// the text again when the element is moved with the mouse, as soon as the button is let go.
test.describe("moving a canvas element checks its text again", () => {
    // The table's last row, whose text is taken past the bottom of the picture.
    const LAST_ROW = 1;
    // How far past the picture's bottom edge the last row's text is taken.
    const PAST_THE_PICTURE_BY = 40;
    // A zoom at which the whole of a Canvas page's picture fits the nightly window.
    const ZOOM_TO_SEE_THE_WHOLE_PICTURE = 60;

    test("a table moved back inside the picture loses the marks of the rows that ran past it", async ({
        page,
        step,
    }) => {
        // This test also pays for making a third book.
        test.setTimeout(300000);
        await step(
            "Make a book with a table, showing two languages",
            async () => {
                await makeBookWithTwoLanguageTable(page, "English");
                await typeInCell(page, LAST_ROW, COLUMN, "fr", "Français");
            },
        );
        // At the nightly window size the bottom of the picture is below the window at 100%, and a
        // drag cannot take the table past what the window shows. Zoomed out, the whole picture shows.
        const zoomWas = (await getZoom(page)).zoom;
        await step("Zoom out until the whole picture shows", async () => {
            await setZoom(page, ZOOM_TO_SEE_THE_WHOLE_PICTURE);
        });
        try {
            await moveTableBackInsideThePicture();
        } finally {
            await setZoom(page, zoomWas);
        }

        async function moveTableBackInsideThePicture(): Promise<void> {
            await step(
                "Move the table down until its last row's text runs past the bottom of the picture",
                async () => {
                    // Moving the table, not dragging the row: with two languages showing, a
                    // cell's text boxes keep their own height, so a taller row does not take the
                    // text any lower.
                    const canvasBottom = bottom(await getCanvasRect(page));
                    const textBottom = await getCellTextBottom(
                        page,
                        LAST_ROW,
                        COLUMN,
                    );
                    await dragTableBy(
                        page,
                        0,
                        0,
                        canvasBottom - textBottom + PAST_THE_PICTURE_BY,
                    );
                    expect(
                        await getCellTextBottom(page, LAST_ROW, COLUMN),
                        "The last row's text should now reach past the picture, or this tests nothing.",
                    ).toBeGreaterThan(bottom(await getCanvasRect(page)));
                },
            );
            await step(
                "Check the text past the picture is marked, and the picture too",
                async () => {
                    await waitForCellOverflowMarks(
                        page,
                        LAST_ROW,
                        COLUMN,
                        true,
                    );
                    expect(
                        await getCellOverflowMarks(page, LAST_ROW, COLUMN),
                    ).toContain("thisOverflowingParent");
                    expect(await isCanvasMarkedAsOverflowed(page)).toBe(true);
                    // Nothing else is going to clear them: only the move below should.
                    await waitForPageToSettle(page);
                    expect(
                        await getCellOverflowMarks(page, LAST_ROW, COLUMN),
                        "The marks should stay until the table is moved.",
                    ).toContain("thisOverflowingParent");
                },
            );
            await step(
                "Move the table up until all of it is inside the picture",
                async () => {
                    const measured = await measureTable(page);
                    const lowestCell = Math.max(
                        ...measured.cells.map((c) => bottom(c.rect)),
                    );
                    const canvasRect = await getCanvasRect(page);
                    const upBy =
                        lowestCell - bottom(canvasRect) + PAST_THE_PICTURE_BY;
                    expect(
                        measured.rect.y - upBy,
                        "There should be room above the table to move it up that far, or this tests nothing.",
                    ).toBeGreaterThan(canvasRect.y);
                    await dragTableBy(page, 0, 0, -upBy);
                    const after = await measureTable(page);
                    expect(
                        Math.max(...after.cells.map((c) => bottom(c.rect))),
                        "Every row should now be inside the picture.",
                    ).toBeLessThan(bottom(await getCanvasRect(page)));
                },
            );
            await step(
                "Check the marks went when the mouse was let go",
                async () => {
                    await waitForCellOverflowMarks(
                        page,
                        LAST_ROW,
                        COLUMN,
                        false,
                    );
                    await expect
                        .poll(() => isCanvasMarkedAsOverflowed(page), {
                            message:
                                "The picture should no longer be marked as overflowed.",
                        })
                        .toBe(false);
                },
            );
        }
    });
});
