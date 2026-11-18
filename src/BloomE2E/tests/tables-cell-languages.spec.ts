// Choosing which languages a table's text cells show (BL-17105). Each of the table's menus (Cell,
// Row, Column, Table) has a Languages section with one check box per collection language,
// labelled "L1 - English" and so on. A table cell does not follow the toolbar's language choice:
// a new cell shows L1 only, whatever the toolbar says, until the user ticks other languages.
//
// The tests are serial and share one book: each starts from the state the one before it left, so
// the first failure is the one to read. Tables are a Pro-tier feature behind the "tables"
// experiment; see tables-core.spec.ts for why the collection is given both this way.

import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { getShownPageId, reloadPageBeingEdited } from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    openPublishDestination,
    showBloomPubPreview,
} from "../helpers/publish";
import {
    clickAddButton,
    clickCell,
    closeAnyMenu,
    copyAndPasteTableProperties,
    getCellLanguages,
    getCellLanguagesShownIn,
    getCellText,
    getMenuCellLanguages,
    getTableCellLanguages,
    getTableShape,
    makeBookWithTableOnCanvasPage,
    openTableMenu,
    paintFormatFromCellOntoCell,
    toggleCellLanguageInCellMenu,
    toggleMenuCellLanguage,
    typeInCell,
    waitForTableAttached,
} from "../helpers/tables";
import { clickUndoButton, undo } from "../helpers/undo";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "tables-cell-languages",
        languages: ["en", "fr", "es"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test.describe.configure({ mode: "serial" });

const L1_ONLY = ["en"];
const L1_AND_L2 = ["en", "fr"];

/** Fail, with `why` and the languages of every cell, unless every text cell of the table shows `languages`. */
async function expectEveryCellShows(
    page: Page,
    languages: string[],
    why: string,
): Promise<void> {
    // The expected grid comes from the table's shape, read separately, so a table that lost
    // cells, or had none, fails rather than matching itself.
    const { rows, columns } = await getTableShape(page);
    expect(
        rows * columns,
        "The table should have cells to check.",
    ).toBeGreaterThan(0);
    const expected = Array.from({ length: rows }, () =>
        Array.from({ length: columns }, () => languages),
    );
    expect(await getTableCellLanguages(page), why).toEqual(expected);
}

test.describe("the languages of a table's text cells", () => {
    test("a new table's cells show L1 only in a bilingual book", async ({
        page,
        step,
    }) => {
        // This test also pays for launching Bloom and making the book.
        test.setTimeout(300000);
        await step(
            "Make a bilingual book with a table on a Canvas page",
            async () => {
                await makeBookWithTableOnCanvasPage(page, ["en", "fr"]);
            },
        );

        await step("Check every cell shows English only", async () => {
            await expectEveryCellShows(
                page,
                L1_ONLY,
                "Every new cell should show L1 only, although the toolbar makes the book bilingual",
            );
        });

        await step(
            "Check the Column menu lists the three languages",
            async () => {
                // A column's menu is the menu of the column the selected cell is in.
                await clickCell(page, 0, 0);
                await openTableMenu(page, "column", 0);
                expect(await getMenuCellLanguages(page)).toEqual([
                    // Every cell shows only L1, so it may not be unticked.
                    { label: "L1 - English", state: "all", locked: true },
                    { label: "L2 - French", state: "none", locked: false },
                    { label: "L3 - Spanish", state: "none", locked: false },
                ]);
                await closeAnyMenu(page);
            },
        );
    });

    test("ticking L2 in a column's menu shows it in every cell of that column, after a reload too", async ({
        page,
        step,
    }) => {
        await step("Tick L2 in the first column's menu", async () => {
            await clickCell(page, 0, 0);
            await openTableMenu(page, "column", 0);
            await toggleMenuCellLanguage(page, "L2");
            expect(
                (await getMenuCellLanguages(page)).map((entry) => entry.state),
            ).toEqual(["all", "all", "none"]);
            await closeAnyMenu(page);
        });

        await step(
            "Check the first column shows L1 and L2, and the second is unchanged",
            async () => {
                expect(await getTableCellLanguages(page)).toEqual([
                    [L1_AND_L2, L1_ONLY],
                    [L1_AND_L2, L1_ONLY],
                ]);
            },
        );

        await step("Leave the page and come back", async () => {
            await reloadPageBeingEdited(page);
            await waitForTableAttached(page);
            expect(
                await getCellLanguages(page, 1, 0),
                "The saved page should still show L1 and L2 in the first column.",
            ).toEqual(L1_AND_L2);
            expect(await getCellLanguages(page, 1, 1)).toEqual(L1_ONLY);
        });

        await step(
            "Check the Table menu shows the first column's L2 as a dash",
            async () => {
                await openTableMenu(page, "table");
                expect(
                    (await getMenuCellLanguages(page)).map(
                        (entry) => entry.state,
                    ),
                    "Half the cells show L2, so its box should show a dash.",
                ).toEqual(["all", "some", "none"]);
                await closeAnyMenu(page);
            },
        );
    });

    test("the Cell menu changes one cell, and Undo puts it back", async ({
        page,
    }) => {
        await toggleCellLanguageInCellMenu(page, 0, 1, "L3");

        expect(await getCellLanguages(page, 0, 1)).toEqual(["en", "es"]);
        expect(
            await getCellLanguages(page, 1, 1),
            "Only the cell whose menu was used should change.",
        ).toEqual(L1_ONLY);

        await undo(page);

        expect(await getCellLanguages(page, 0, 1)).toEqual(L1_ONLY);
    });

    // A cell's languages are one of its settings, so they go wherever the table carries a cell's
    // settings: Paint format, and Copy properties with Paste properties. Each way starts from the
    // cell at row 0, column 0, which shows L1 and L2.
    const waysToCarryACellsSettings: {
        name: string;
        /** A cell that shows L1 only before, and should show L1 and L2 after. */
        target: { row: number; column: number };
        carry: (
            page: Page,
            target: { row: number; column: number },
        ) => Promise<void>;
    }[] = [
        {
            name: "Paint format",
            target: { row: 1, column: 1 },
            carry: (page, target) =>
                paintFormatFromCellOntoCell(
                    page,
                    { row: 0, column: 0 },
                    target,
                ),
        },
        {
            name: "Copy properties and Paste properties",
            target: { row: 0, column: 1 },
            carry: (page) => copyAndPasteTableProperties(page),
        },
    ];
    for (const way of waysToCarryACellsSettings) {
        test(`${way.name} carries a cell's languages to another cell`, async ({
            page,
        }) => {
            const { row, column } = way.target;
            expect(await getCellLanguages(page, 0, 0)).toEqual(L1_AND_L2);
            expect(await getCellLanguages(page, row, column)).toEqual(L1_ONLY);

            await way.carry(page, way.target);

            expect(await getCellLanguages(page, row, column)).toEqual(
                L1_AND_L2,
            );
            // The box it gained has to be one a person can type in.
            await typeInCell(page, row, column, "fr", "Poire");
            expect(await getCellText(page, row, column, "fr")).toBe("Poire");
        });
    }

    test("the Table menu turns a language on and off in every text cell, and keeps what was typed", async ({
        page,
        step,
    }) => {
        const everyCellShows = (languages: string[], why: string) =>
            expectEveryCellShows(page, languages, why);
        const toggleL2InTableMenu = async () => {
            await openTableMenu(page, "table");
            await toggleMenuCellLanguage(page, "L2");
            await closeAnyMenu(page);
        };

        await step("Untick L2 in the Table menu", async () => {
            // Pasting properties, in the test before this one, left every cell showing L2.
            await everyCellShows(L1_AND_L2, "Every cell should start with L2");
            await toggleL2InTableMenu();
            await everyCellShows(L1_ONLY, "Unticking L2 should hide it");
        });

        await step("Tick L2 in the Table menu and type in French", async () => {
            await toggleL2InTableMenu();
            await everyCellShows(L1_AND_L2, "Ticking L2 should show it");
            await typeInCell(page, 1, 0, "fr", "Pomme");
        });

        await step(
            "Untick L2, leave the page and come back, and tick it again",
            async () => {
                await toggleL2InTableMenu();
                await everyCellShows(L1_ONLY, "Unticking L2 should hide it");
                await reloadPageBeingEdited(page);
                await waitForTableAttached(page);
                await everyCellShows(
                    L1_ONLY,
                    "The saved page should still show L1 only",
                );
                await toggleL2InTableMenu();
                await everyCellShows(L1_AND_L2, "Ticking L2 should show it");
                expect(
                    await getCellText(page, 1, 0, "fr"),
                    "The French typed before L2 was unticked should still be in its box.",
                ).toBe("Pomme");
            },
        );
    });

    // Which languages a published table shows is decided twice over: by what Bloom writes into the
    // BloomPUB, and by bloom-player, which has its own reading of a text box's language setting.
    // The preview is bloom-player showing the BloomPUB, so it is where both can be seen.
    test("a BloomPUB preview shows each cell the languages chosen for it", async ({
        page,
        step,
    }) => {
        const canvasPageId = await step(
            "Give the first column L1 and L2, and the second L1 only",
            async () => {
                // The tests before this one left every cell showing L1 and L2, with French typed
                // in rows 0 and 1 of the second column, which this hides.
                await clickCell(page, 0, 1);
                await openTableMenu(page, "column", 1);
                await toggleMenuCellLanguage(page, "L2");
                await closeAnyMenu(page);
                await typeInCell(page, 0, 0, "en", "Apple");
                await typeInCell(page, 0, 0, "fr", "Pomme");
                await typeInCell(page, 0, 1, "en", "Pear");
                await typeInCell(page, 1, 1, "en", "Plum");
                expect(await getCellLanguages(page, 0, 0)).toEqual(L1_AND_L2);
                expect(await getCellLanguages(page, 0, 1)).toEqual(L1_ONLY);
                expect(await getCellLanguages(page, 1, 1)).toEqual(L1_ONLY);
                expect(
                    await getCellText(page, 0, 1, "fr"),
                    "The hidden French box should still hold text, or the preview has nothing " +
                        "to hide.",
                ).toBe("Poire");
                return await getShownPageId(page);
            },
        );

        const player = await step("Open the BloomPUB preview", async () => {
            await openPublishDestination(page, "BloomPUB");
            return showBloomPubPreview(page);
        });
        if (!player || !canvasPageId)
            throw new Error("The BloomPUB preview never opened.");

        await step("Check which languages each cell shows", async () => {
            await expect
                .poll(
                    async () =>
                        getCellLanguagesShownIn(player, canvasPageId, 0, 0),
                    {
                        message:
                            "A cell with L1 and L2 ticked should show both in the preview.",
                    },
                )
                .toEqual(L1_AND_L2);
            expect(
                await getCellLanguagesShownIn(player, canvasPageId, 0, 1),
                "A cell with only L1 ticked should show only L1 in the preview, though it " +
                    "holds French text and the first column shows French.",
            ).toEqual(L1_ONLY);
            expect(
                await getCellLanguagesShownIn(player, canvasPageId, 1, 1),
            ).toEqual(L1_ONLY);
        });

        await step("Go back to the Edit tab", async () => {
            await switchTab(page, "edit");
            await waitForTableAttached(page);
        });
    });

    // The tests from here on add rows. They come last because an added row leaves each row of the
    // table shorter, too short for a language box added afterwards to fit in its cell (the subject
    // of tables-cell-languages-after-new-row.spec.ts), and the tests above add language boxes. The BloomPUB test leaves the first column showing L1 and L2 and the
    // second L1 only.
    test("a row added to the table copies the languages of the row it was added beside", async ({
        page,
    }) => {
        // The "+" button copies its settings from the row of the selected cell.
        await clickCell(page, 1, 0);
        const shape = await clickAddButton(page, "row");
        expect(shape.rows).toBe(3);

        expect(await getCellLanguages(page, 2, 0)).toEqual(L1_AND_L2);
        expect(await getCellLanguages(page, 2, 1)).toEqual(L1_ONLY);
    });

    // Undoing a table operation rebuilds the table's cells, so the cells a language change was made
    // to are gone by the time its own Undo runs, and the change has to find the cells standing in
    // their places.
    test("Undo takes back a language change made before a row that was added and then undone", async ({
        page,
        step,
    }) => {
        const rowsBefore = (await getTableShape(page)).rows;

        await step("Tick L3 in one cell, then add a row", async () => {
            await toggleCellLanguageInCellMenu(page, 0, 1, "L3");
            expect(await getCellLanguages(page, 0, 1)).toEqual(["en", "es"]);
            await clickCell(page, 0, 0);
            const shape = await clickAddButton(page, "row");
            expect(shape.rows).toBe(rowsBefore + 1);
        });

        await step("Press Undo, and check the row goes", async () => {
            await clickUndoButton(page);
            await expect
                .poll(async () => (await getTableShape(page)).rows, {
                    message:
                        "The first Undo should have taken the new row away.",
                })
                .toBe(rowsBefore);
            expect(
                await getCellLanguages(page, 0, 1),
                "Undoing the row should have left the language change that came before it.",
            ).toEqual(["en", "es"]);
        });

        await step(
            "Press Undo again, and check the language goes",
            async () => {
                await clickUndoButton(page);
                await expect
                    .poll(async () => getCellLanguages(page, 0, 1), {
                        message:
                            "The second Undo should have taken back the language change.",
                    })
                    .toEqual(L1_ONLY);
            },
        );

        await step("Check Undo still works for a later change", async () => {
            await toggleCellLanguageInCellMenu(page, 1, 1, "L3");
            expect(await getCellLanguages(page, 1, 1)).toEqual(["en", "es"]);
            await clickUndoButton(page);
            await expect
                .poll(async () => getCellLanguages(page, 1, 1), {
                    message:
                        "Undo should still take back a language change made after the others " +
                        "were undone.",
                })
                .toEqual(L1_ONLY);
        });

        await step("Go back to the Collection tab", async () => {
            // Leave Bloom where the next file expects to find it.
            await switchTab(page, "collection");
        });
    });
});
