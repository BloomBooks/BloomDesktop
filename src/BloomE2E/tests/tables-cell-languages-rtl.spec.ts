// A right-to-left language ticked for a table's text cell (BL-17105). The cell gets a new box for
// that language at once, without a page load, and the box has to run right to left from the start:
// Bloom's page-load code is what normally gives a box its direction, and it has not run.
//
// A file of its own because the collection needs a right-to-left language, and a collection is
// fixed for a whole file. See tables-cell-languages.spec.ts for the rest of the feature, and
// tables-core.spec.ts for why the collection is given its subscription and experiment this way.

import { expect, test } from "../fixtures/bloomTest";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    closeAnyMenu,
    getCellLanguages,
    getCellTextDirection,
    makeBookWithTableOnCanvasPage,
    rightClickCell,
    toggleMenuCellLanguage,
    typeInCell,
} from "../helpers/tables";

test.use({
    collectionSpec: {
        name: "tables-cell-languages-rtl",
        languages: ["en", "ar"],
        rightToLeftLanguages: ["ar"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
    experimentalFeatures: ["tables"],
});

test("a right-to-left language ticked for a cell runs right to left before the page is loaded again", async ({
    page,
    step,
}) => {
    // This test also pays for launching Bloom and making the book.
    test.setTimeout(300000);
    await step("Make a book with a table on a Canvas page", async () => {
        await makeBookWithTableOnCanvasPage(page, ["en"]);
        expect(await getCellLanguages(page, 0, 0)).toEqual(["en"]);
        expect(await getCellTextDirection(page, 0, 0, "en")).toBe("ltr");
    });

    await step("Tick L2, which is Arabic, in a cell's menu", async () => {
        await rightClickCell(page, 0, 0);
        await toggleMenuCellLanguage(page, "L2");
        await closeAnyMenu(page);
        expect(await getCellLanguages(page, 0, 0)).toEqual(["en", "ar"]);
    });

    await step("Check the Arabic box runs right to left", async () => {
        expect(
            await getCellTextDirection(page, 0, 0, "ar"),
            "The new Arabic box should run right to left as soon as it appears.",
        ).toBe("rtl");
        expect(
            await getCellTextDirection(page, 0, 0, "en"),
            "The English box beside it should still run left to right.",
        ).toBe("ltr");
        // A box a person can type in, not only one that is drawn.
        await typeInCell(page, 0, 0, "ar", "تفاحة");
    });
});
