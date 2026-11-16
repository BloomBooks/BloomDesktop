// Choosing which languages a table's text cells show (BL-17105). Each of the table's menus (Cell,
// Row, Column, Table) has a Languages section with one check box per collection language,
// labelled "L1 - English" and so on. A table cell does not follow the toolbar's language choice:
// a new cell shows L1 only, whatever the toolbar says, until the user ticks other languages.
//
// The tests are serial and share one book: each starts from the state the one before it left, so
// the first failure is the one to read. Tables are a Pro-tier feature behind the "tables"
// experiment; see tables-core.spec.ts for why the collection is given both this way.

import { expect, test } from "../fixtures/bloomTest";
import { reloadPageBeingEdited } from "../helpers/bookMaking";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    clickAddButton,
    clickCell,
    closeAnyMenu,
    getCellLanguages,
    getMenuCellLanguages,
    makeBookWithTableOnCanvasPage,
    openTableMenu,
    rightClickCell,
    toggleMenuCellLanguage,
    waitForTableAttached,
} from "../helpers/tables";
import { undo } from "../helpers/undo";

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
            for (let row = 0; row < 2; row++)
                for (let column = 0; column < 2; column++)
                    expect(
                        await getCellLanguages(page, row, column),
                        `The new cell at row ${row}, column ${column} should show L1 only, ` +
                            `although the toolbar makes the book bilingual.`,
                    ).toEqual(L1_ONLY);
        });

        await step(
            "Check the Column menu lists the three languages",
            async () => {
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
                expect(await getCellLanguages(page, 0, 0)).toEqual(L1_AND_L2);
                expect(await getCellLanguages(page, 1, 0)).toEqual(L1_AND_L2);
                expect(await getCellLanguages(page, 0, 1)).toEqual(L1_ONLY);
                expect(await getCellLanguages(page, 1, 1)).toEqual(L1_ONLY);
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

    test("the Cell menu changes one cell, and Undo puts it back", async ({
        page,
    }) => {
        await rightClickCell(page, 0, 1);
        await toggleMenuCellLanguage(page, "L3");
        await closeAnyMenu(page);

        expect(await getCellLanguages(page, 0, 1)).toEqual(["en", "es"]);
        expect(
            await getCellLanguages(page, 1, 1),
            "Only the cell whose menu was used should change.",
        ).toEqual(L1_ONLY);

        await undo(page);

        expect(await getCellLanguages(page, 0, 1)).toEqual(L1_ONLY);
    });
});
