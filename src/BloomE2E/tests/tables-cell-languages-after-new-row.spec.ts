// A language ticked for a table's text cell after a row was added to the table (BL-17105). The
// box the cell gains for that language has to take the caret with one click, as every other box
// in the table does, or a person cannot type in it.
//
// A file of its own because tables-cell-languages.spec.ts shares one book among its tests, and a
// row added early in that book would put every later test of a new box in the state this one
// checks. See tables-core.spec.ts for why the collection is given its subscription and experiment
// this way.

import { expect, test } from "../fixtures/bloomTest";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    clickAddButton,
    clickCell,
    closeAnyMenu,
    getCellLanguages,
    getCellText,
    getTableShape,
    makeBookWithTableOnCanvasPage,
    rightClickCell,
    toggleMenuCellLanguage,
    typeInCell,
} from "../helpers/tables";

test.use({
    collectionSpec: {
        name: "tables-cell-languages-after-new-row",
        languages: ["en", "fr"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test("a language ticked for a cell after a row was added gives a box a person can type in", async ({
    page,
    step,
}) => {
    // Known failure, BL-17105: after a row is added with "+", a language box that a cell gains
    // afterwards (from the Cell menu, Paint format or Paste properties) does not take the caret
    // when clicked, though it is contenteditable and focus() from code works. On a table with no
    // added row the same steps work. Remove test.fail() when that is fixed.
    test.fail();
    // This test also pays for launching Bloom and making the book.
    test.setTimeout(300000);
    await step("Make a book with a table, and add a row", async () => {
        await makeBookWithTableOnCanvasPage(page, ["en"]);
        const rows = (await getTableShape(page)).rows;
        await clickCell(page, 1, 0);
        expect((await clickAddButton(page, "row")).rows).toBe(rows + 1);
    });

    await step(
        "Tick L2 in the menu of a cell that was there before",
        async () => {
            await rightClickCell(page, 1, 1);
            await toggleMenuCellLanguage(page, "L2");
            await closeAnyMenu(page);
            expect(await getCellLanguages(page, 1, 1)).toEqual(["en", "fr"]);
        },
    );

    await step("Click in the new French box and type", async () => {
        await typeInCell(page, 1, 1, "fr", "Poire");
        expect(await getCellText(page, 1, 1, "fr")).toBe("Poire");
    });
});
