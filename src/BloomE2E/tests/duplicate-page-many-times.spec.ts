// "Duplicate Page Many Times..." from a page's menu: OK adds as many copies as asked for, Cancel
// adds none, and the workspace tabs are locked while the dialog is open.
//
// The dialog used to be a WinForms window CDP could not reach, which is why the Duplicate Page test
// (Test Case ID 349) left this step to its manual portion (Test Case ID 810). It now opens inside
// the Edit tab (duplicateManyDialog.tsx; BL-13502), so it can be driven. Automates "Duplicate Page
// Many Times" (Test Case ID 844), split from 810.
//
// The tests are serial because each one works on the book the first one builds.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    getPages,
    makeBookFromTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import {
    cancelDuplicatePageManyTimes,
    finishDuplicatePageManyTimes,
    openDuplicatePageManyTimes,
} from "../helpers/pageList";
import { getTabs } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "duplicate-page-many-times", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

let source: IBookPage;

test.describe("Duplicate Page Many Times", () => {
    test("builds a book with one content page", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        [source] = await getContentPages(page);
    });

    test("locks the workspace tabs while open, and Cancel adds nothing [Test Case ID 844]", async ({
        page,
    }) => {
        const before = (await getPages(page)).length;
        const dialog = await openDuplicatePageManyTimes(page, source.id);
        expect(
            (await getTabs(page)).navigationLocked,
            "The workspace tabs should be locked while the dialog is open.",
        ).toBe(true);

        await cancelDuplicatePageManyTimes(dialog);

        await expect
            .poll(async () => (await getTabs(page)).navigationLocked, {
                message: "Cancel should unlock the workspace tabs.",
            })
            .toBe(false);
        expect((await getPages(page)).length).toBe(before);
    });

    test("OK adds the number of copies asked for, right after the page [Test Case ID 844]", async ({
        page,
    }) => {
        const dialog = await openDuplicatePageManyTimes(page, source.id);
        await finishDuplicatePageManyTimes(page, dialog, 3);

        await expect
            .poll(async () => (await getTabs(page)).navigationLocked, {
                message: "OK should unlock the workspace tabs.",
            })
            .toBe(false);
        const contentPages = await getContentPages(page);
        expect(contentPages).toHaveLength(4);
        expect(
            contentPages[0].id,
            "The copies come after the page they were made from.",
        ).toBe(source.id);
    });
});
