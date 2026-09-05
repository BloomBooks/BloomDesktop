// Making and editing a table on a canvas page: the path a person takes the first time, from
// dragging the Table icon out of the Canvas tool to seeing the finished table in a BloomPUB
// preview. Automates the test case "Tables: create and edit a table on a canvas page"
// (Its Test Case ID in the test inventory is to be assigned later.)
//
// This is the file to read first when tables break. It covers only the steps that have to work for
// the feature to be usable at all; the content types, the layout paths and the contention between a
// cell's own behaviour and the canvas element's are in tables-extended.spec.ts.
//
// Two things about the setup are load-bearing:
//
//  - Tables are a Pro-tier feature behind the "tables" experiment, so the collection is given both.
//    The tier comes from a real subscription code written into the .bloomCollection, because Bloom
//    reads the tier as it opens the collection and then keeps it; the experiment comes from an
//    --e2e environment variable rather than the saved setting, because the saved setting lives in a
//    user.config the developer's own Bloom shares. See kProSubscriptionCode in
//    helpers/collectionSettings.ts and ExperimentalFeatures.cs.
//  - The tests are serial and share one book: each starts from the state the one before it left.
//    So a failure part way through leaves the later tests failing on setup, and the first failure
//    is the one to read.
//
// Every assertion about how the table looks is relative: cells tile each other, widths sum, rects
// lie inside rects. Nothing here compares a size with a pixel count, because the suite runs at
// whatever window size the machine has. See helpers/geometry.ts.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    type IBookPage,
} from "../helpers/bookMaking";
import { waitForBookWithPageCount } from "../helpers/bookHtml";
import {
    duplicateCanvasElement,
    dragPaletteItemOntoCanvas,
    getCanvasElementCount,
    getCanvasRect,
    openCanvasTool,
} from "../helpers/canvasElements";
import { bottom, right } from "../helpers/geometry";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import {
    getFeatureStatus,
    kProSubscriptionCode,
} from "../helpers/collectionSettings";
import {
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    cell,
    clickAddButton,
    countCellPictures,
    countDrawingSurfacesInCells,
    dragBoundary,
    dragTableBy,
    clickCell,
    clickTableMenuCommand,
    expectCellsTile,
    expectFormatGearInsideCell,
    expectPictureInsideCell,
    expectTableInsideCanvas,
    expectTablesRenderAsGrids,
    getCellText,
    getTableCount,
    getTableShape,
    measureTable,
    openTableMenu,
    rightClickCell,
    setCellContentType,
    typeInCell,
    waitForTableAttached,
} from "../helpers/tables";
import { switchTab, undo } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "tables-core",
        languages: ["en"],
        subscriptionCode: kProSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test.describe.configure({ mode: "serial" });

// The picture a cell gets. Small, and shipped with the suite, so the test needs nothing from
// outside this folder.
const IMAGE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// The canvas page the table goes on, and the book folder, both set by the first test.
let canvasPage: IBookPage;
let bookFolder: string;

// Test Case ID to be assigned later.
test.describe("a table on a canvas page", () => {
    test("appears when the Table icon is dragged onto the canvas", async ({
        page,
        step,
    }) => {
        // This test also pays for launching Bloom and making the book.
        test.setTimeout(300000);
        await step("Check that this collection may use tables", async () => {
            // Sanity check the two gates on tables before anything depends on them, so that a
            // missing Table icon later is not mistaken for a broken palette. `enabled` is the
            // subscription tier and `visible` the experiment, so a failure here says which of the
            // two is missing.
            const tableFeature = await getFeatureStatus(page, "table");
            expect(
                {
                    enabled: tableFeature.enabled,
                    visible: tableFeature.visible,
                },
                `Tables need the Pro tier (enabled) and the "tables" experiment (visible); ` +
                    `Bloom says they are not both in place, so nothing below can work. Bloom's ` +
                    `whole answer: ${JSON.stringify(tableFeature)}`,
            ).toEqual({ enabled: true, visible: true });
        });

        await step("Make a book and add a Canvas page", async () => {
            bookFolder = await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Canvas");
            [canvasPage] = await getContentPages(page);
            await goToPage(page, canvasPage.id);
        });

        await step("Check the Canvas page starts out empty", async () => {
            // So that a later failure cannot be blamed on a page that already had something on it.
            await openCanvasTool(page);
            expect(
                await getCanvasElementCount(page),
                "The Canvas page should start with just its background image.",
            ).toBe(1);
            expect(
                await getTableCount(page),
                "The Canvas page should start with no table.",
            ).toBe(0);
        });

        await step("Drag the Table icon onto the canvas", async () => {
            // Into the upper left of the canvas rather than the middle, which is where a drop
            // lands by default. A later test duplicates this table, and the copy is placed below
            // and to the right of it; from the middle of the page that copy falls off the right
            // edge and is clipped.
            await dragPaletteItemOntoCanvas(page, "table", {
                xFraction: 0.3,
                yFraction: 0.25,
            });
            expect(
                await getTableCount(page),
                "Dragging the Table icon onto the canvas should have made one table.",
            ).toBe(1);
            await waitForTableAttached(page);
            await expectTableInsideCanvas(page);
        });

        await step(
            "Check the new table is two rows by two columns",
            async () => {
                const shape = await getTableShape(page);
                expect(
                    { rows: shape.rows, columns: shape.columns },
                    "A new table should be two rows by two columns.",
                ).toEqual({ rows: 2, columns: 2 });
                await expectCellsTile(page);
            },
        );

        await step("Check every cell is a text box", async () => {
            for (let row = 0; row < 2; row++)
                for (let column = 0; column < 2; column++) {
                    const target = await cell(page, row, column);
                    await expect(
                        target,
                        `The cell at row ${row}, column ${column} should hold text.`,
                    ).toHaveAttribute("data-content-type", "text");
                    await expect(
                        target.locator('.bloom-editable[lang="en"]'),
                        `The cell at row ${row}, column ${column} should have an English text ` +
                            `box.`,
                    ).toHaveCount(1);
                }
        });
    });

    test("takes typing in a cell, with the format gear inside the cell", async ({
        page,
        step,
    }) => {
        await step("Type in the first cell", async () => {
            await typeInCell(page, 0, 0, "en", "Apple");
            expect(
                await getCellText(page, 0, 0, "en"),
                "The cell should hold what was typed in it.",
            ).toBe("Apple");
        });

        await step("Check the format gear sits inside the cell", async () => {
            // The gear belongs to the cell, not to the page: a gear drawn outside its cell is how
            // a person sees that Bloom has placed it against the wrong box.
            await expectFormatGearInsideCell(page, 0, 0);
        });
    });

    // The table's "+" button for a new row sits just under its bottom edge, which is exactly
    // where Bloom puts the toolbar of the selected canvas element, so the button is underneath
    // the toolbar's Duplicate and Delete buttons and cannot be pressed. Worse, a press there
    // lands on Delete and takes the whole table away. The right-edge "+" for a new column is
    // clear of the toolbar, and the test below presses that one, so this is about the bottom
    // button alone. Reported in the branch's own review; nothing in the test is wrong.
    test.fixme("gains a row from the bottom + button", async ({ page }) => {
        await clickCell(page, 0, 0);
        const grown = await clickAddButton(page, "row");
        expect(grown.rows, "The + button should have added a third row.").toBe(
            3,
        );
        await expectCellsTile(page);
    });

    test("gains a column from the right + button, and a row and back from the row menu", async ({
        page,
        step,
    }) => {
        await step(
            "Add a column with the + button on the right edge",
            async () => {
                await clickCell(page, 0, 0);
                const widened = await clickAddButton(page, "column");
                expect(
                    widened.columns,
                    "The + button on the right edge should have added a third column.",
                ).toBe(3);
                await expectCellsTile(page);
            },
        );

        await step("Delete that column from the column menu", async () => {
            await openTableMenu(page, "column", 2);
            await clickTableMenuCommand(page, "Delete Column");
            await expect
                .poll(async () => (await getTableShape(page)).columns, {
                    message:
                        "Delete Column should have taken the table back to two columns.",
                })
                .toBe(2);
        });

        await step("Add a row from the row menu", async () => {
            // The row menu, because the bottom "+" is unreachable (see the fixme above).
            await openTableMenu(page, "row", 1);
            await clickTableMenuCommand(page, "Add Row Below");
            await expect
                .poll(async () => (await getTableShape(page)).rows, {
                    message: "Add Row Below should have made a third row.",
                })
                .toBe(3);
            await expectCellsTile(page);
        });

        await step("Type in the new row", async () => {
            // A row that cannot be typed in is not a row.
            await typeInCell(page, 2, 0, "en", "Cherry");
            expect(await getCellText(page, 2, 0, "en")).toBe("Cherry");
        });

        await step("Delete that row from the row menu", async () => {
            await openTableMenu(page, "row", 2);
            await clickTableMenuCommand(page, "Delete Row");
            await expect
                .poll(async () => (await getTableShape(page)).rows, {
                    message:
                        "Delete Row should have taken the table back to two rows.",
                })
                .toBe(2);
            await expectCellsTile(page);
            expect(
                await getCellText(page, 0, 0, "en"),
                "Deleting the third row should have left the first row's text alone.",
            ).toBe("Apple");
        });
    });

    test("undoes a row addition", async ({ page, step }) => {
        await step("Add a row from the row menu", async () => {
            await clickCell(page, 0, 0);
            await openTableMenu(page, "row", 1);
            await clickTableMenuCommand(page, "Add Row Below");
            await expect
                .poll(async () => (await getTableShape(page)).rows, {
                    message: "Sanity check: the row was added.",
                })
                .toBe(3);
        });

        await step("Undo, and check the row has gone", async () => {
            await undo(page);
            await expect
                .poll(async () => (await getTableShape(page)).rows, {
                    message: "Undo should have taken the added row away again.",
                })
                .toBe(2);
            await expectCellsTile(page);
        });
    });

    test("resizes a column when its boundary is dragged", async ({
        page,
        step,
    }) => {
        const before = await step("Measure the columns", async () => {
            await clickCell(page, 0, 0);
            const measurement = await measureTable(page);
            expect(
                measurement.shape.columnWidths.every(
                    (width) => width === "fill",
                ),
                `A new table's columns should both fill the space, but they are ` +
                    `${measurement.shape.columnWidths.join(", ")}.`,
            ).toBe(true);
            return measurement;
        });
        if (!before) throw new Error("The table was never measured.");
        const leftBefore = before.cells.find(
            (c) => c.row === 0 && c.column === 0,
        )!;
        const rightBefore = before.cells.find(
            (c) => c.row === 0 && c.column === 1,
        )!;

        await step("Drag the boundary between them to the left", async () => {
            // A quarter of the left column: far enough to be unmistakable, and nowhere near
            // either edge, where the drag would meet a minimum width.
            const distance = -Math.round(leftBefore.rect.width / 4);
            await dragBoundary(page, "column", 0, distance);
        });

        const after = await measureTable(page);
        const leftAfter = after.cells.find(
            (c) => c.row === 0 && c.column === 0,
        )!;
        const rightAfter = after.cells.find(
            (c) => c.row === 0 && c.column === 1,
        )!;

        await step(
            "Check one column gained what the other lost, and the table kept its width",
            async () => {
                expect(
                    leftAfter.rect.width,
                    "Dragging the boundary left should have narrowed the left column.",
                ).toBeLessThan(leftBefore.rect.width);
                expect(
                    rightAfter.rect.width,
                    "Dragging the boundary left should have widened the right column.",
                ).toBeGreaterThan(rightBefore.rect.width);
                // What the left column lost, the right column gained: the table is not resized by
                // a boundary drag, only divided differently.
                const lost = leftBefore.rect.width - leftAfter.rect.width;
                const gained = rightAfter.rect.width - rightBefore.rect.width;
                expect(
                    Math.abs(lost - gained),
                    `The left column lost ${Math.round(lost)}px but the right column gained ` +
                        `${Math.round(gained)}px; a boundary drag should move the boundary, not ` +
                        `resize the table.`,
                ).toBeLessThanOrEqual(2);
                expect(
                    Math.abs(right(after.rect) - right(before.rect)),
                    "The table itself should not have changed width.",
                ).toBeLessThanOrEqual(2);
                // A dragged column is no longer sharing the space equally, so the table records a
                // size for it rather than "fill".
                expect(
                    after.shape.columnWidths.some((width) => /px$/.test(width)),
                    `After a boundary drag the table should record a fixed column width, but it ` +
                        `records ${after.shape.columnWidths.join(", ")}.`,
                ).toBe(true);
                await expectCellsTile(page);
            },
        );
    });

    test("turns a text cell into a picture cell and takes a picture", async ({
        page,
        step,
    }) => {
        await step(
            "Right-click a cell and check only the Cell menu opens",
            async () => {
                const menus = await rightClickCell(page, 1, 1);
                expect(
                    menus.cell,
                    "Right-clicking a cell should open the table's Cell menu.",
                ).toBe(true);
                // The canvas element around the table and Bloom's own text menu both listen for
                // this same right-click. Two menus at once, or the wrong one, is the failure
                // worth catching.
                expect(
                    {
                        canvasElement: menus.canvasElement,
                        bloomText: menus.bloomText,
                    },
                    "Right-clicking a cell should open the Cell menu and nothing else.",
                ).toEqual({ canvasElement: false, bloomText: false });
            },
        );

        await step("Turn the cell into a picture cell", async () => {
            await setCellContentType(page, 1, 1, "image");
            await expect(
                (await cell(page, 1, 1)).locator(".bloom-imageContainer img"),
                "A picture cell should hold a picture box.",
            ).toHaveCount(1);
        });

        await step("Choose a picture for the cell", async () => {
            const target = await cell(page, 1, 1);
            await chooseImageFile(page, IMAGE_FILE, target);
            const placement = await getImagePlacement(page, target);
            expect(
                placement.fileName,
                "The cell should show the picture that was chosen.",
            ).toBe("bird.png");
        });

        await step("Check the picture stays inside its cell", async () => {
            const measurement = await measureTable(page);
            const pictureCell = measurement.cells.find(
                (c) => c.row === 1 && c.column === 1,
            )!;
            expect(
                pictureCell.contentType,
                "The cell should record that it holds a picture.",
            ).toBe("image");
            await expectCellsTile(page);
            // The picture has to stay in its cell. Nothing here says how big it should be.
            await expectPictureInsideCell(page, 1, 1);
        });

        await step(
            "Rebuild the page, to clear the drawing surface",
            async () => {
                // Putting a picture in a cell leaves the canvas element's drawing surface
                // (canvas.comical-generated) over the table, and it takes every press from then on:
                // the first press on a cell reaches the cell, every one after that reaches the
                // surface, and no cell can be selected again. Pressing elsewhere on the page does not
                // clear it. So the page is rebuilt here, which does, and the tests that follow start
                // from a table that answers a click. Reported in the branch's own review; a person
                // hits it too, and for them there is no test to rebuild the page.
                await reloadPageBeingEdited(page);
                await waitForTableAttached(page);
            },
        );
    });

    test("duplicates the whole table as one canvas element", async ({
        page,
        step,
    }) => {
        await step("Duplicate the table's canvas element", async () => {
            await clickCell(page, 0, 0);
            await duplicateCanvasElement(page);
            await expect
                .poll(async () => getTableCount(page), {
                    message:
                        "Duplicating the table element should have made a second table.",
                })
                .toBe(2);
            await waitForTableAttached(page, 1);
        });

        await step(
            "Move the copy so that both tables are wholly on the page",
            async () => {
                // Bloom places a copy below and to the right of the original, which for a table wide
                // enough to matter puts part of it past the right edge of the page, where it is
                // clipped. So the copy is moved under the original, and clamped to the canvas in case
                // there is not room for the gap.
                const canvasRect = await getCanvasRect(page);
                const original = (await measureTable(page, 0)).rect;
                const copy = (await measureTable(page, 1)).rect;
                const gap = 8;
                const x = Math.max(
                    canvasRect.x + gap,
                    Math.min(original.x, right(canvasRect) - copy.width - gap),
                );
                const y = Math.max(
                    canvasRect.y + gap,
                    Math.min(
                        bottom(original) + gap,
                        bottom(canvasRect) - copy.height - gap,
                    ),
                );
                await dragTableBy(page, 1, x - copy.x, y - copy.y);
            },
        );

        await step("Check neither table is clipped by the page", async () => {
            // A table hanging over the page boundary cannot be seen or clicked in the part that
            // is cut off, and every measurement of it afterwards is of what survived.
            await expectTableInsideCanvas(page, 0);
            await expectTableInsideCanvas(page, 1);
        });

        await step(
            "Check the copy has the original's shape and text",
            async () => {
                const copy = await getTableShape(page, 1);
                expect(
                    { rows: copy.rows, columns: copy.columns },
                    "The copy should have the same shape as the original.",
                ).toEqual({ rows: 2, columns: 2 });
                expect(
                    await getCellText(page, 0, 0, "en", 1),
                    "The copy should carry the original's text.",
                ).toBe("Apple");
                await expectCellsTile(page, 1);
            },
        );
    });

    // The copy is drawn as a table and holds the original's text, but it is not a working table:
    // no structural command reaches it. Duplicating the canvas element clones the table's markup,
    // including the data-table-attached="1" that Bloom writes on a table it has wired up, and
    // attachSingleTable in tableEditing.ts returns early on a table that already carries it. So
    // attachTable is never called for the copy, the table library never registers it, and every
    // command in the copy's own menus does nothing, logging "TableHistoryManager: Attempted to add
    // history entry for a detached or null table". Marked fixme rather than weakened: the copy
    // should behave like any other table.
    test.fixme(
        "the duplicated table takes commands of its own",
        async ({ page }) => {
            await clickCell(page, 0, 0, 1);
            await openTableMenu(page, "row", 1, 1);
            await clickTableMenuCommand(page, "Add Row Below");
            const rowsOfEach = async () => ({
                original: (await getTableShape(page, 0)).rows,
                copy: (await getTableShape(page, 1)).rows,
            });
            // Both tables, because the row menu serves whichever table is current, and a command that
            // went to the wrong one is the failure worth seeing.
            await expect
                .poll(rowsOfEach, {
                    message:
                        "The copy's own row menu should have added a row to the copy alone.",
                })
                .toEqual({ original: 2, copy: 3 });
            await openTableMenu(page, "row", 2, 1);
            await clickTableMenuCommand(page, "Delete Row");
            await expect
                .poll(rowsOfEach, {
                    message:
                        "Deleting the copy's third row should have left both tables at two rows.",
                })
                .toEqual({ original: 2, copy: 2 });
        },
    );

    test("saves both tables to the book, with no editing markup", async ({
        page,
        step,
    }) => {
        const saved = await step(
            "Leave the page, so that Bloom saves the book",
            async () => {
                const pages = await getContentPages(page);
                const other = pages.find((p) => p.id !== canvasPage.id);
                await goToPage(page, other ? other.id : canvasPage.id);
                const book = await waitForBookWithPageCount(
                    page,
                    bookFolder,
                    pages.length,
                );
                return book.pages.find((p) => p.id === canvasPage.id)!;
            },
        );
        if (!saved) throw new Error("The book was never read back.");

        await step("Check both tables were saved as they stood", async () => {
            expect(
                saved.tables.length,
                "The saved page should describe both tables.",
            ).toBe(2);
            for (const description of saved.tables) {
                expect(
                    { rows: description.rows, columns: description.columns },
                    "Each saved table should be two rows by two columns.",
                ).toEqual({ rows: 2, columns: 2 });
                expect(
                    /px/.test(description.columnWidths),
                    `The saved table should keep the dragged column width, but records ` +
                        `"${description.columnWidths}".`,
                ).toBe(true);
            }
            expect(
                saved.tables.flatMap((t) => t.imageSources),
                "One cell in each table should hold the picture.",
            ).toEqual(["bird.png", "bird.png"]);
            expect(
                saved.tables.flatMap((t) => t.cellContentTypes).sort(),
                "Each table should have three text cells and one picture cell.",
            ).toEqual([
                "image",
                "image",
                "text",
                "text",
                "text",
                "text",
                "text",
                "text",
            ]);
        });

        await step("Check no editing markup reached the book", async () => {
            // The editing chrome must never be written into the book: it would confuse a later
            // load and reach the reader.
            expect(
                saved.editingArtifacts,
                "The saved page should carry no editing-only table markup.",
            ).toEqual([]);
        });

        await step(
            "Come back to the page and check the table still takes commands",
            async () => {
                await goToPage(page, canvasPage.id);
                await waitForTableAttached(page);
                await clickCell(page, 0, 0);
                // Through the row menu, because Bloom's toolbar for the selected canvas element
                // covers the bottom "+" button (see the fixme earlier in this file).
                await openTableMenu(page, "row", 1);
                await clickTableMenuCommand(page, "Add Row Below");
                await expect
                    .poll(async () => (await getTableShape(page)).rows, {
                        message:
                            "A table should be editable again after leaving the page and coming " +
                            "back.",
                    })
                    .toBe(3);
                await undo(page);
                await expect
                    .poll(async () => (await getTableShape(page)).rows)
                    .toBe(2);
            },
        );
    });

    test("adds the ready-made Alphabet Book page", async ({ page, step }) => {
        await step("Add the Alphabet Book page", async () => {
            // Which page is new, rather than the first content page: this book already has the
            // canvas page, and Bloom inserts the new one after whichever page is showing.
            const before = (await getContentPages(page)).map((p) => p.id);
            await addPage(page, "Alphabet Book");
            const alphabet = (await getContentPages(page)).find(
                (p) => !before.includes(p.id),
            )!;
            await goToPage(page, alphabet.id);
            await waitForTableAttached(page);
        });

        await step("Check its table is nine rows by six columns", async () => {
            const shape = await getTableShape(page);
            expect(
                { rows: shape.rows, columns: shape.columns },
                "The Alphabet Book page should hold a nine by six table.",
            ).toEqual({ rows: 9, columns: 6 });
            await expectCellsTile(page);
        });

        await step("Type in one of its 54 cells", async () => {
            // A page of 54 cells is only useful if they can be typed in.
            await typeInCell(page, 0, 0, "en", "A");
            expect(await getCellText(page, 0, 0, "en")).toBe("A");
        });
    });

    test("renders the tables as grids in a BloomPUB preview", async ({
        page,
        step,
    }) => {
        const player = await step("Open the BloomPUB preview", async () => {
            await openPublishDestination(page, "BloomPUB");
            return showBloomPubPreview(page);
        });
        if (!player) throw new Error("The BloomPUB preview never opened.");

        await step("Check both tables draw as grids", async () => {
            // The canvas page's own two tables. bloom-player builds only the pages near the one
            // it is showing, so the Alphabet Book page's table need not be in the preview's DOM
            // at all; the Edit tab tests above cover that table.
            await expectTablesRenderAsGrids(player, 2, canvasPage.id);
        });

        await step("Go back to the Collection tab", async () => {
            // Leave Bloom where the next file expects to find it.
            await switchTab(page, "collection");
        });
    });

    // Each table has one picture cell, and in the reader's copy only the original's shows its
    // picture. The copy's cell records data-content-type="image" and its bloom-canvas is empty:
    // publishing turns the img a picture cell holds into a background image in the cell's style
    // attribute, and for the duplicated table that never happens, so the reader loses the picture.
    // Marked fixme rather than weakened: a duplicated table's picture belongs in the book.
    test.fixme(
        "shows both tables' pictures in a BloomPUB preview",
        async ({ page }) => {
            await openPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            expect(
                await countCellPictures(player),
                "Both picture cells should show their picture in the reader's copy.",
            ).toEqual({ pictureCells: 2, showingAPicture: 2 });
            await switchTab(page, "collection");
        },
    );

    // The picture cell in the reader's copy still holds the canvas Bloom draws speech bubbles on,
    // classed comical-generated comical-editing. That surface exists only for the editor, and
    // Bloom's own publishing takes it out elsewhere, so a cell keeping it is editing markup that
    // has reached the reader. Marked fixme rather than weakened.
    test.fixme(
        "leaves no drawing surface in a BloomPUB preview",
        async ({ page }) => {
            await openPublishDestination(page, "BloomPUB");
            const player = await showBloomPubPreview(page);
            expect(
                await countDrawingSurfacesInCells(player),
                "The reader's copy should carry none of Bloom's drawing surfaces.",
            ).toBe(0);
            await switchTab(page, "collection");
        },
    );
});
