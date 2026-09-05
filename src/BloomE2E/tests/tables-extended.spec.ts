// Tables, beyond the first things a person does with one.
//
// tables-core.spec.ts is the file to read first when tables break: it covers making a table,
// typing in it, changing its shape, saving it and publishing it. This file takes up everything
// else, and most of it is about contention. A table's cells sit inside a canvas element, and a
// canvas element already claims the mouse, the keyboard, a right-click and a floating toolbar of
// its own. So nearly every test here asks the same kind of question: when a person does something
// in a cell, does the cell get it, or does the element around the cell get it?
//
// One book, two pages, one Bloom, in order. The first page is an ordinary content page whose
// layout a person can change, so it can hold a table as one of the page's own sections; the second
// is a Canvas page holding a table that floats. Both are worth having, because the two paths give
// a table a different context: a section's table is laid out by the page, a canvas element's table
// is laid out by the element.
//
// Every step is a helper call. Nothing here knows a selector, an API route or a test id; when a
// step needs a new gesture, it goes in helpers/ and gets its explanation there.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import { readBook, waitForBookWithPageCount } from "../helpers/bookHtml";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
    setContentLanguages,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    deleteCanvasElement,
    dragCanvasElementCorner,
    dragCanvasElementSide,
    dragPaletteItemOntoCanvas,
    getCanvasElementCount,
    getCanvasElementToolbarRect,
    openCanvasTool,
} from "../helpers/canvasElements";
import { selectBook } from "../helpers/collection";
import { kProSubscriptionCode } from "../helpers/collectionSettings";
import { expectNoOverlap, expectSameRect } from "../helpers/geometry";
import { chooseImageFile } from "../helpers/images";
import { pressKey, pressKeyIn } from "../helpers/keys";
import {
    chooseSectionType,
    getSectionTypesOffered,
    setChangeLayoutMode,
    splitSection,
} from "../helpers/origami";
import {
    getPageIds,
    runPageMenuCommand,
    selectPage,
    waitForEditablePageReload,
    waitForPageCount,
} from "../helpers/pageThumbnails";
import { setPageSize } from "../helpers/pageSize";
import {
    cell,
    cellTextBox,
    cellVideoBox,
    clickCell,
    clickTableMenuCommand,
    dragAcrossCellText,
    dragBoundary,
    expectCellsTile,
    expectPictureInsideCell,
    getCellLanguages,
    getCellOverflowMarks,
    getCellParagraphCount,
    getCellText,
    getTableCount,
    getTableShape,
    closeAnyMenu,
    measureChrome,
    measureTable,
    openTableMenu,
    rightClickCell,
    setCellContentType,
    typeInCell,
    typeInCellKeyByKey,
    waitForNestedTableAttached,
    waitForTableAttached,
} from "../helpers/tables";
import {
    chooseVideoFile,
    getVideoSource,
    SHORT_VIDEO,
} from "../helpers/videos";
import { getZoom, setZoom, switchTab, undo } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "tables-extended",
        // French as well as English, so the two-languages test has a second language to turn on.
        languages: ["en", "fr"],
        subscriptionCode: kProSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test.describe.configure({ mode: "serial" });

const IMAGE_FILE = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
    "images",
    "bird.png",
);

// Text long enough that no cell of a two-column table can show it all.
const TOO_MUCH_TEXT =
    "This sentence is far longer than the cell it is typed into, and it goes on, and on, " +
    "and on, so that there can be no doubt that it does not fit in the space available.";

// The pages and the book folder, all set by the first test.
let sectionPage: IBookPage;
let canvasPage: IBookPage;
let bookFolder: string;

// Test Case ID to be assigned later.
test.describe("more ways to use a table", () => {
    test("makes a table out of a page section, through Change Layout", async ({
        page,
        step,
    }) => {
        // This test also pays for launching Bloom and making the book.
        test.setTimeout(300000);
        await step("Make a book and add a text page", async () => {
            bookFolder = await makeBookFromTemplate(page, "Basic Book");
            // A page whose layout a person is allowed to change; that is what offers the list of
            // section types a table can be chosen from.
            await addPage(page, "Just Text");
            [sectionPage] = await getContentPages(page);
            await goToPage(page, sectionPage.id);
        });

        await step("Split the page in Change Layout mode", async () => {
            await setChangeLayoutMode(page, true);
            // Origami offers the list of types only for a section with nothing in it yet, so make
            // one: split the page's text section and take the empty half. That is also how a
            // person adds a table to a page that already has text on it.
            await splitSection(page, "right", 0);
        });

        await step("Choose Table for the empty half", async () => {
            const offered = await getSectionTypesOffered(page, 1);
            expect(
                offered,
                "An empty page section should offer Table among the things it can hold.",
            ).toContain("table");
            await chooseSectionType(page, "table", 1);
            await setChangeLayoutMode(page, false);
            await waitForTableAttached(page);
        });

        await step("Check the new table, and type in it", async () => {
            const shape = await getTableShape(page);
            expect(
                { rows: shape.rows, columns: shape.columns },
                "A new table should be two rows by two columns, however it was made.",
            ).toEqual({ rows: 2, columns: 2 });
            await expectCellsTile(page);
            await clickCell(page, 0, 0);
            await typeInCell(page, 0, 0, "en", "Origami");
        });
    });

    test("keeps the table when its section is split in two", async ({
        page,
        step,
    }) => {
        const before = await measureTable(page);

        await step("Split the table's own section in two", async () => {
            await setChangeLayoutMode(page, true);
            await splitSection(page, "right", 1);
            await setChangeLayoutMode(page, false);
            await waitForTableAttached(page);
        });

        await step("Check the table survived the split", async () => {
            expect(
                await getTableCount(page),
                "Splitting the section should have left the one table, not copied or dropped it.",
            ).toBe(1);
            expect(
                await getCellText(page, 0, 0, "en"),
                "The table should still hold what was typed in it before the split.",
            ).toBe("Origami");
        });

        await step("Check the table is narrower than it was", async () => {
            const after = await measureTable(page);
            expect(
                after.rect.width < before.rect.width,
                `The table should be narrower now that it shares the page with a new section ` +
                    `(it was ${Math.round(before.rect.width)}px wide and is now ` +
                    `${Math.round(after.rect.width)}px).`,
            ).toBe(true);
        });
    });

    // The cells keep the widths they had before the split, so they now stick out of the table's
    // own box and the right-hand column is cut off by the edge of the section: a two-column table
    // 132 pixels wide inside a table box the split left 127 pixels wide. Nothing re-fits the table
    // to the space it has. Marked fixme rather than weakened, because a table that does not fit
    // its section is the thing a person sees.
    test.fixme(
        "re-fits the table to a section that has been made narrower",
        async ({ page }) => {
            await expectCellsTile(page);
        },
    );

    test("adds a table to a canvas page", async ({ page, step }) => {
        await step("Add a Canvas page", async () => {
            const before = (await getContentPages(page)).map((p) => p.id);
            await addPage(page, "Canvas");
            canvasPage = (await getContentPages(page)).find(
                (p) => !before.includes(p.id),
            )!;
            await goToPage(page, canvasPage.id);
        });

        await step("Drag the Table icon onto the canvas", async () => {
            await openCanvasTool(page);
            await dragPaletteItemOntoCanvas(page, "table");
            await waitForTableAttached(page);
        });

        await step("Type in the new table", async () => {
            await typeInCell(page, 0, 0, "en", "Apple");
            await expectCellsTile(page);
        });
    });

    test("marks text that does not fit a cell, without growing the table", async ({
        page,
        step,
    }) => {
        const before = await measureTable(page);

        await step("Type more into a cell than it can hold", async () => {
            // Key by key, because Bloom marks a box as overflowing from a keyup or a paste
            // (OverflowChecker.AddOverflowHandlers), so text put in without keystrokes is never
            // measured and the mark never appears.
            await typeInCellKeyByKey(page, 0, 1, "en", TOO_MUCH_TEXT);
        });

        await step(
            "Check the cell is marked, and the table did not grow",
            async () => {
                expect(
                    await getCellOverflowMarks(page, 0, 1),
                    "Text too big for its cell should be marked as overflowing.",
                ).not.toEqual([]);
                const after = await measureTable(page);
                expectSameRect(
                    after.rect,
                    before.rect,
                    "the table after too much text was typed in a cell",
                );
            },
        );

        await step("Empty the cell again", async () => {
            // So that the tests that follow are not working around a cell full of text.
            await typeInCell(page, 0, 1, "en", "");
        });
    });

    test("gives a cell's keyboard to the cell, not to the canvas element", async ({
        page,
        step,
    }) => {
        const elementsBefore = await getCanvasElementCount(page);
        const rectBefore = (await measureTable(page)).rect;

        await step("Press Backspace and Delete in an empty cell", async () => {
            // An empty cell: Backspace and Delete there have nothing of their own to do,
            // which is exactly when the canvas element around it might take them as
            // "delete me".
            const box = await cellTextBox(page, 1, 1, "en");
            await pressKeyIn(
                box,
                "Backspace",
                "the empty cell at row 1, column 1",
            );
            await pressKey(page, "Delete");
            expect(
                await getTableCount(page),
                "Backspace and Delete in an empty cell should leave the table alone.",
            ).toBe(1);
            expect(
                await getCanvasElementCount(page),
                "Backspace and Delete in an empty cell should leave the canvas elements " +
                    "alone.",
            ).toBe(elementsBefore);
        });

        await step("Press the arrow keys in the cell", async () => {
            // The arrow keys move a selected canvas element. With the caret in a cell they belong
            // to the caret, so the table must not move.
            for (const key of [
                "ArrowRight",
                "ArrowDown",
                "ArrowLeft",
                "ArrowUp",
            ])
                await pressKey(page, key);
            expectSameRect(
                (await measureTable(page)).rect,
                rectBefore,
                "the table after the arrow keys were pressed in a cell",
            );
        });

        await step("Press Ctrl+C and Ctrl+V in the cell", async () => {
            // Ctrl+C then Ctrl+V with the caret in a cell copies and pastes text. What it must
            // not do is copy and paste the canvas element the cell is in.
            await pressKey(page, "Control+c");
            await pressKey(page, "Control+v");
            expect(
                await getCanvasElementCount(page),
                "Ctrl+C and Ctrl+V with the caret in a cell should not copy the canvas element.",
            ).toBe(elementsBefore);
            expect(
                await getTableCount(page),
                "Ctrl+C and Ctrl+V with the caret in a cell should not copy the table.",
            ).toBe(1);
        });
    });

    test("starts a new paragraph when Enter is pressed in a cell", async ({
        page,
        step,
    }) => {
        await step("Type a word in a cell", async () => {
            await typeInCell(page, 1, 1, "en", "One");
            expect(
                await getCellParagraphCount(page, 1, 1, "en"),
                "A cell's text box should start as one paragraph.",
            ).toBe(1);
        });

        await step("Press Enter there", async () => {
            const box = await cellTextBox(page, 1, 1, "en");
            await pressKeyIn(box, "Enter", "the cell at row 1, column 1");
            await expect
                .poll(async () => getCellParagraphCount(page, 1, 1, "en"), {
                    message:
                        "Enter in a cell should start a second paragraph in that cell.",
                })
                .toBe(2);
            await expect(
                box,
                "The caret should still be in the cell that was typed in.",
            ).toBeFocused();
        });
    });

    // Dragging the mouse across the words in a cell selects nothing: window.getSelection() is
    // empty afterwards. The press does not reach the cell's text box. On a canvas page the drawing
    // surface lies over the table, so the canvas element's own handler gets the press
    // (CanvasElementPointerInteractions.onMouseDown), and what that handler does with it is place a
    // caret at the point and collapse it, which is a click rather than the start of a selection.
    // This case appeared to pass earlier for a bad reason: the typing helper used to clear a box
    // with select-all and Delete, and that selection was still what the page reported. Marked
    // fixme rather than weakened, because selecting a few words with the mouse is how a person
    // edits text.
    test.fixme(
        "selects text when the mouse is dragged across a cell",
        async ({ page }) => {
            const rectBefore = (await measureTable(page)).rect;
            const selected = await dragAcrossCellText(page, 0, 0, "en");
            expect(
                selected,
                `Dragging across the text in a cell should select it, but the page reports ` +
                    `"${selected}" selected.`,
            ).toContain("Apple");
            expectSameRect(
                (await measureTable(page)).rect,
                rectBefore,
                "the table after a drag across the text in a cell",
            );
        },
    );

    // The two sets of controls are drawn in the same place at the bottom-left of the table: the
    // canvas element's toolbar covers the table pill and the "Add row at the bottom edge" button
    // (measured overlap: the toolbar at x=428 y=761 w=24 h=28 over the pill at x=404 y=757 w=34
    // h=20 and the add button at x=446 y=757 w=36 h=20). This is why the other tests in this suite
    // add a row through the row menu instead of that button. Marked fixme rather than weakened,
    // because both sets of controls have to be clickable.
    test.fixme(
        "shows the table's chrome and the element's toolbar without them colliding",
        async ({ page }) => {
            await clickCell(page, 0, 0);
            const chrome = await measureChrome(page);
            expect(
                chrome.length,
                "Clicking a cell should show the table's own chrome: the pills and the add buttons.",
            ).toBeGreaterThan(0);
            const pieces = [
                ...chrome,
                {
                    name: "the canvas element's toolbar",
                    rect: await getCanvasElementToolbarRect(page),
                },
            ];
            expectNoOverlap(
                pieces,
                "the table's chrome and the canvas element's toolbar",
            );
        },
    );

    test("undoes a row that was added through the row menu", async ({
        page,
        step,
    }) => {
        await step("Type in a cell, key by key", async () => {
            // Key by key, because undoing typing is the subject of the case that follows this
            // one: CKEditor's undo stack is built from key events, and the quick way of filling a
            // box in raises none.
            await typeInCellKeyByKey(page, 1, 0, "en", "Banana");
        });

        await step("Add a row from the row menu", async () => {
            await clickCell(page, 0, 0);
            await openTableMenu(page, "row", 1);
            await clickTableMenuCommand(page, "Add Row Below");
            await expect
                .poll(async () => (await getTableShape(page)).rows, {
                    message: "The row menu should have added a row.",
                })
                .toBe(3);
        });

        await step("Undo, and check the typing is still there", async () => {
            await undo(page);
            await expect
                .poll(async () => (await getTableShape(page)).rows, {
                    message:
                        "The first undo should have taken the row back off.",
                })
                .toBe(2);
            expect(
                await getCellText(page, 1, 0, "en"),
                "Taking the row back off should have left the text that was typed before it.",
            ).toBe("Banana");
        });
    });

    // Undo cannot reach past a table's own structural change. Text undo is CKEditor's, and each
    // text box keeps its own stack; adding and then undoing a row rebuilds the table's markup, so
    // every box in it is a new one with an empty stack. Asked straight after the row undo, with
    // the cursor back in the cell that was typed in, the page reports no CKEditor instance with
    // anything undoable: {"tableCanUndo":false,"origamiCanUndo":false,"imageCanUndo":false,
    // "ckeditorCanUndo":false,"undoable":false,"activeLang":"en"}. So the typing that came before
    // the row can never be taken back, while a person pressing Ctrl+Z twice expects it to be.
    // Marked fixme rather than weakened.
    test.fixme(
        "undoes the typing that came before the row",
        async ({ page }) => {
            await clickCell(page, 1, 0);
            await undo(page);
            await expect
                .poll(async () => getCellText(page, 1, 0, "en"), {
                    message:
                        "The second undo should have taken back the typing that came before the row.",
                })
                .not.toBe("Banana");
        },
    );

    test("re-tiles the cells when the whole table is made wider", async ({
        page,
        step,
    }) => {
        await step("Put a picture in one of the cells", async () => {
            await setCellContentType(page, 1, 1, "image");
            await chooseImageFile(page, IMAGE_FILE, await cell(page, 1, 1));
            // Putting a picture in a cell leaves the canvas element's drawing surface over the
            // table, and it then takes every press (see the note in tables-core.spec.ts).
            // Rebuilding the page is what clears it.
            await reloadPageBeingEdited(page);
            await waitForTableAttached(page);
        });

        await step("Drag the table's side handle outwards", async () => {
            await clickCell(page, 0, 0);
            // The east side handle, not a corner: Bloom hides the corner handles for a canvas
            // element that holds text, which a table does (see the case that follows).
            const { before, after } = await dragCanvasElementSide(
                page,
                "e",
                60,
            );
            expect(
                after.width > before.width,
                `Dragging the east handle out should have made the table wider, but it went ` +
                    `from ${Math.round(before.width)} to ${Math.round(after.width)} wide.`,
            ).toBe(true);
        });

        await step(
            "Check the cells re-tiled and the picture stayed in its cell",
            async () => {
                await expectCellsTile(page);
                await expectPictureInsideCell(page, 1, 1);
            },
        );
    });

    // A table cannot be made taller by dragging. Bloom decides which handles a canvas element gets
    // from what is inside it: an element holding a visible text box gets the east and west side
    // handles and no corner handles at all (`has-text` in editMode.less, set in
    // CanvasElementSelectionUi.ts), and every cell of a table holds one. So the south handle is
    // offered only when the element's height is not automatic, and the corner handles, which are
    // the ones that change both measurements at once, are hidden: the se handle is in the markup
    // but never visible. A person resizing a table can therefore change its width and not its
    // height. Marked fixme rather than weakened, because dragging a table's corner is the ordinary
    // way to resize a table.
    test.fixme(
        "re-tiles the cells when the table is resized by a corner",
        async ({ page }) => {
            await clickCell(page, 0, 0);
            const { before, after } = await dragCanvasElementCorner(
                page,
                "se",
                60,
                40,
            );
            expect(
                after.width > before.width && after.height > before.height,
                `Dragging the bottom right corner out should have made the table bigger, but it went ` +
                    `from ${Math.round(before.width)}x${Math.round(before.height)} to ` +
                    `${Math.round(after.width)}x${Math.round(after.height)}.`,
            ).toBe(true);
            await expectCellsTile(page);
            await expectPictureInsideCell(page, 1, 1);
        },
    );

    test("keeps the table workable at 150% zoom", async ({ page, step }) => {
        const wasAt = (await getZoom(page)).zoom;
        await step("Zoom the view to 150%", async () => {
            await setZoom(page, 150);
            await waitForTableAttached(page);
        });
        try {
            await step("Check the table's chrome still comes up", async () => {
                await clickCell(page, 0, 0);
                const chrome = await measureChrome(page);
                expect(
                    chrome.length,
                    "The table's chrome should still come up when the view is zoomed.",
                ).toBeGreaterThan(0);
                expectNoOverlap(chrome, "the table's chrome at 150% zoom");
            });

            await step("Drag a column boundary while zoomed", async () => {
                // The chrome is positioned against the table by the browser, so the test of
                // whether it followed the zoom is whether it still works: a boundary drag aimed
                // by the cell's own edge has to land on the boundary.
                const { before, after } = await dragBoundary(
                    page,
                    "column",
                    0,
                    -40,
                );
                expect(
                    after.columnWidths.join(",") !==
                        before.columnWidths.join(","),
                    "Dragging a column boundary at 150% zoom should have changed the recorded " +
                        "widths.",
                ).toBe(true);
                await expectCellsTile(page);
            });
        } finally {
            await step("Put the zoom back", async () => {
                await setZoom(page, wasAt);
                await waitForTableAttached(page);
            });
        }
    });

    test("takes a video in a cell", async ({ page, step }) => {
        test.setTimeout(300000);
        await step("Turn a cell into a video cell", async () => {
            await setCellContentType(page, 1, 0, "video");
        });

        await step("Import a video into it", async () => {
            const box = await cellVideoBox(page, 1, 0);
            await chooseVideoFile(page, box, SHORT_VIDEO);
            const source = await getVideoSource(box);
            expect(
                source,
                "The cell should hold the video that was imported.",
            ).toContain(".mp4");
            await expectCellsTile(page);
        });

        await step(
            "Rebuild the page, and check the video is still there",
            async () => {
                await reloadPageBeingEdited(page);
                await waitForTableAttached(page);
                expect(
                    await getVideoSource(await cellVideoBox(page, 1, 0)),
                    "The video should still be in the cell after the page is rebuilt.",
                ).toContain(".mp4");
            },
        );
    });

    test("holds a table inside a cell", async ({ page, step }) => {
        await step("Turn a cell into a table", async () => {
            await setCellContentType(page, 0, 1, "table");
            await expect
                .poll(async () => getTableCount(page), {
                    message:
                        "Choosing the table content type should have made a table inside the " +
                        "cell.",
                })
                .toBe(2);
            // The inner table comes second in the document, so it is table 1, and the library
            // rather than Bloom is what attached it.
            await waitForNestedTableAttached(page, 1);
        });

        await step("Check the inner table, and type in it", async () => {
            const inner = await getTableShape(page, 1);
            expect(
                { rows: inner.rows, columns: inner.columns },
                "A table inside a cell should start two by two, like any other.",
            ).toEqual({ rows: 2, columns: 2 });
            await typeInCell(page, 0, 0, "en", "Inner", 1);
            await expectCellsTile(page, 1);
        });

        await step(
            "Rebuild the page, and check the inner table kept its text",
            async () => {
                await reloadPageBeingEdited(page);
                await waitForNestedTableAttached(page, 1);
                expect(
                    await getCellText(page, 0, 0, "en", 1),
                    "The inner table's text should survive the page being rebuilt.",
                ).toBe("Inner");
            },
        );
    });

    test("answers a right-click in any kind of cell with the Cell menu alone", async ({
        page,
        step,
    }) => {
        // One kind of cell at a time: text, picture, video, and a cell holding a table. Each has
        // its own contender for the right-click -- Bloom's text menu, its picture menu, the video
        // menu -- and the table's own Cell menu has to win in every case.
        for (const [row, column, what] of [
            [0, 0, "a text cell"],
            [1, 1, "a picture cell"],
            [1, 0, "a video cell"],
            [0, 1, "a cell holding a table"],
        ] as const) {
            await step(`Right-click ${what}`, async () => {
                const menus = await rightClickCell(page, row, column);
                expect(
                    {
                        cell: menus.cell,
                        canvasElement: menus.canvasElement,
                        bloomText: menus.bloomText,
                    },
                    `A right-click in ${what} should open the table's Cell menu and nothing ` +
                        `else.`,
                ).toEqual({
                    cell: true,
                    canvasElement: false,
                    bloomText: false,
                });
                await closeAnyMenu(page);
            });
        }

        // There is no further case here for a right-click on the table's own edge, because a table
        // on a canvas page has no edge: it is drawn exactly over its canvas element (measured: both
        // at 193,458 and 201 by 220), and two pixels inside that boundary the topmost thing is the
        // drawing surface, which answers a right-click with no menu at all. The element's own menu
        // is reached from its toolbar instead, which the tests above do.
        await closeAnyMenu(page);
    });

    test("shows every content language in every text cell", async ({
        page,
        step,
    }) => {
        await step("Turn on a second content language", async () => {
            await setContentLanguages(page, ["en", "fr"]);
            await waitForTableAttached(page);
            expect(
                await getCellLanguages(page, 0, 0),
                "A text cell should show a box for each of the book's content languages.",
            ).toEqual(["en", "fr"]);
        });

        await step("Type in both boxes of one cell", async () => {
            // Both languages are typed here rather than leaning on what an earlier test left in
            // the cell: the tests between have rebuilt this page several times, and the question
            // being asked is only whether the two boxes of one cell are separate.
            await typeInCell(page, 0, 0, "en", "Apple");
            await typeInCell(page, 0, 0, "fr", "Pomme");
            expect(
                await getCellText(page, 0, 0, "fr"),
                "The French box should hold what was typed into it.",
            ).toBe("Pomme");
            expect(
                await getCellText(page, 0, 0, "en"),
                "Typing in the French box should not have touched the English one.",
            ).toBe("Apple");
        });

        await step("Go back to one content language", async () => {
            await setContentLanguages(page, ["en"]);
            await waitForTableAttached(page);
        });
    });

    test("keeps all 54 cells tiled when the page turns landscape", async ({
        page,
        step,
    }) => {
        await step("Add the Alphabet Book page", async () => {
            const before = (await getContentPages(page)).map((p) => p.id);
            await addPage(page, "Alphabet Book");
            const alphabet = (await getContentPages(page)).find(
                (p) => !before.includes(p.id),
            )!;
            await goToPage(page, alphabet.id);
            await waitForTableAttached(page);
        });

        await step("Put a letter and a picture in its cells", async () => {
            await typeInCell(page, 0, 0, "en", "A");
            await setCellContentType(page, 0, 1, "image");
            await chooseImageFile(page, IMAGE_FILE, await cell(page, 0, 1));
        });

        await step("Turn the page landscape", async () => {
            await setPageSize(page, "A5Landscape");
            await waitForTableAttached(page);
        });

        await step(
            "Check all 54 cells still tile, with their contents",
            async () => {
                const shape = await getTableShape(page);
                expect(
                    { rows: shape.rows, columns: shape.columns },
                    "Turning the page landscape should not change the table's shape.",
                ).toEqual({ rows: 9, columns: 6 });
                await expectCellsTile(page);
                await expectPictureInsideCell(page, 0, 1);
                expect(
                    await getCellText(page, 0, 0, "en"),
                    "The letter typed before the page turned should still be there.",
                ).toBe("A");
            },
        );

        await step("Turn the page back to portrait", async () => {
            await setPageSize(page, "A5Portrait");
            await waitForTableAttached(page);
            await expectCellsTile(page);
        });
    });

    test("copies a page with a table into another book", async ({
        page,
        bloomApp,
        step,
    }) => {
        test.setTimeout(300000);
        const sourcePage = await step(
            "Read the page that is about to be copied",
            async () => {
                await goToPage(page, canvasPage.id);
                const source = await readBook(page, bookFolder);
                const found = source.pages.find((p) => p.id === canvasPage.id)!;
                expect(
                    found.tables.length,
                    "The page about to be copied should have its table and the table inside it.",
                ).toBeGreaterThan(1);
                return found;
            },
        );
        if (!sourcePage) throw new Error("The source page was never read.");

        const otherBefore = await step(
            "Make a second book to paste into",
            async () => {
                // The destination is made first, before anything is copied. What Copy Page puts
                // on the clipboard does not survive making a book and adding a page to it, and
                // the failure is a Paste Page command that never comes enabled, which reads as a
                // copy that did not happen. So: build the destination, then copy, then paste.
                //
                // A second book rather than the same one, so the question is a table crossing
                // from one book to another rather than a table copied beside itself.
                await switchTab(page, "collection");
                const madeBook = await makeBookFromTemplate(page, "Basic Book");
                // A new Basic Book has no numbered pages at all: every page the template offers
                // is marked "extra", so Bloom copies the front matter and leaves the first real
                // page to the person. Paste Page is a command on a page's own menu, so give the
                // book a page to paste into.
                await addPage(page, "Just Text");
                // Leaving the Edit tab is what makes Bloom write the book out, so go out and come
                // back before reading the page the paste is aimed at.
                await switchTab(page, "collection");
                return {
                    folder: madeBook,
                    contents: await waitForBookWithPageCount(page, madeBook, 1),
                };
            },
        );
        if (!otherBefore) throw new Error("The second book was never made.");
        const otherBook = otherBefore.folder;

        await step("Copy the page with the table on it", async () => {
            await selectBook(page, bookFolder);
            await switchTab(page, "edit");
            await goToPage(page, canvasPage.id);
            await selectPage(page, canvasPage.id);
            await runPageMenuCommand(page, canvasPage.id, "Copy Page");
            // Copy Page saves the book first, which reloads the page; pasting before that
            // finishes does nothing at all.
            await waitForEditablePageReload(page, canvasPage.id);
        });

        await step("Paste it into the other book", async () => {
            await switchTab(page, "collection");
            await selectBook(page, otherBook);
            await switchTab(page, "edit");
            const otherThumbnails = (await getPageIds(page)).length;
            await selectPage(page, otherBefore.contents.pages[0].id);
            await runPageMenuCommand(
                page,
                otherBefore.contents.pages[0].id,
                "Paste Page",
            );
            await waitForPageCount(page, otherThumbnails + 1);
            await switchTab(page, "collection");
        });

        const pasted = await step(
            "Check the pasted page describes the same tables",
            async () => {
                const otherAfter = await waitForBookWithPageCount(
                    page,
                    otherBook,
                    otherBefore.contents.pages.length + 1,
                );
                const found = otherAfter.pages.find(
                    (p) =>
                        !otherBefore.contents.pages.some(
                            (was) => was.id === p.id,
                        ),
                )!;
                expect(
                    found.tables.map((t) => ({
                        rows: t.rows,
                        columns: t.columns,
                        cellContentTypes: t.cellContentTypes,
                    })),
                    "The pasted page's tables should be described exactly as the original's are.",
                ).toEqual(
                    sourcePage.tables.map((t) => ({
                        rows: t.rows,
                        columns: t.columns,
                        cellContentTypes: t.cellContentTypes,
                    })),
                );
                expect(
                    found.editingArtifacts,
                    "The pasted page should carry no editing-only table markup.",
                ).toEqual([]);
                return found;
            },
        );
        if (!pasted) throw new Error("The pasted page was never found.");

        await step("Check the pasted table works in its new book", async () => {
            // It has to be a working table, not a picture of one.
            await selectBook(page, otherBook);
            await switchTab(page, "edit");
            await goToPage(page, pasted.id);
            await waitForTableAttached(page);
            await clickCell(page, 0, 0);
            expect(
                (await measureChrome(page)).length,
                "Clicking a cell of the pasted table should raise the table's own chrome.",
            ).toBeGreaterThan(0);
        });

        await step("Go back to the book this file works in", async () => {
            await switchTab(page, "collection");
            await selectBook(page, bookFolder);
            await switchTab(page, "edit");
            expect(
                bloomApp.collectionDir.length,
                "The collection folder should be known.",
            ).toBeGreaterThan(0);
        });
    });

    test("deletes the table, leaving nothing of it in the book", async ({
        page,
        step,
    }) => {
        await step("Delete the table's canvas element", async () => {
            await goToPage(page, canvasPage.id);
            await waitForTableAttached(page);
            await clickCell(page, 0, 0);
            await deleteCanvasElement(page);
            await expect
                .poll(async () => getTableCount(page), {
                    message:
                        "Deleting the table's canvas element should have taken the table off " +
                        "the page.",
                })
                .toBe(0);
        });

        await step(
            "Leave the page, and check the book keeps nothing of the table",
            async () => {
                // Leaving the page is what makes Bloom save it.
                await goToPage(page, sectionPage.id);
                const saved = await readBook(page, bookFolder);
                const wasTheCanvasPage = saved.pages.find(
                    (p) => p.id === canvasPage.id,
                )!;
                expect(
                    wasTheCanvasPage.tables,
                    "The saved page should describe no table at all.",
                ).toEqual([]);
                expect(
                    wasTheCanvasPage.editingArtifacts,
                    "Deleting a table should leave none of its editing markup behind.",
                ).toEqual([]);
            },
        );

        await step("Go back to the Collection tab", async () => {
            // Leave Bloom where the next file expects to find it.
            await switchTab(page, "collection");
        });
    });
});
