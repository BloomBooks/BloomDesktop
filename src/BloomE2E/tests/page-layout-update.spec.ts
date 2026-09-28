// When Bloom brings a book's pages up to date for this version (BookProcessor's page pass), and
// when it does not.
//
// A book records, in its pageLayoutUpdateLevel meta, whether its pages have been brought up to
// date. Changing the page size or the theme sets it back to 0, but does not run the update then:
// Bloom waits until something needs the whole book, such as choosing a Publish tool. Books made
// from Bloom's own templates and Sample Shells start up to date. If the update fails, Bloom says so
// and leaves the book marked as needing it, and the next Publish tool tries again. Each test watches for the
// update's progress dialog while choosing a Publish tool, and reads the level from the saved book.

import { expect, test } from "../fixtures/bloomTest";
import {
    makeBookFromSampleShell,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import { waitForPageLayoutUpdateLevel } from "../helpers/bookHtml";
import { setBookTheme } from "../helpers/bookSettings";
import { setPageSize } from "../helpers/pageSize";
import {
    isPublishDestinationShowing,
    selectPublishDestination,
} from "../helpers/publish";
import {
    choosePublishToolAndSeeIfItUpdated,
    closeUpdateBookDialog,
    failNextPageUpdateAt,
    waitForUpdateBookDialogToReportAProblem,
} from "../helpers/updateBookDialog";

test.use({
    collectionSpec: { name: "page-layout-update", languages: ["en"] },
});

// The level this Bloom writes when a book's pages are up to date (BookStorage.kPageLayoutUpdateLevel).
const UP_TO_DATE = "1";
// The level a layout change leaves behind.
const NEEDS_UPDATE = "0";

test("a page size change makes the first Publish tool update the book, and the next one does not [Test Case ID 834]", async ({
    page,
}) => {
    const book = await makeBookFromTemplate(page, "Basic Book");
    await waitForPageLayoutUpdateLevel(book, UP_TO_DATE);

    await setPageSize(page, "A5Landscape");
    await waitForPageLayoutUpdateLevel(book, NEEDS_UPDATE);

    expect(
        await choosePublishToolAndSeeIfItUpdated(page, "PDF & Print"),
        "PDF & Print should have brought the pages up to date first",
    ).toBe(true);
    await waitForPageLayoutUpdateLevel(book, UP_TO_DATE);

    expect(
        await choosePublishToolAndSeeIfItUpdated(page, "Web"),
        "Web should not update pages that PDF & Print just brought up to date",
    ).toBe(false);
});

test("a theme change makes the next Publish tool update the book [Test Case ID 834]", async ({
    page,
}) => {
    const book = await makeBookFromTemplate(page, "Basic Book");
    await waitForPageLayoutUpdateLevel(book, UP_TO_DATE);

    await setBookTheme(page, "rounded-border-ebook");
    await waitForPageLayoutUpdateLevel(book, NEEDS_UPDATE);

    expect(
        await choosePublishToolAndSeeIfItUpdated(page, "PDF & Print"),
        "PDF & Print should have brought the pages up to date first",
    ).toBe(true);
    await waitForPageLayoutUpdateLevel(book, UP_TO_DATE);
});

test("a new book from Basic Book needs no update [Test Case ID 834]", async ({
    page,
}) => {
    const book = await makeBookFromTemplate(page, "Basic Book");
    await waitForPageLayoutUpdateLevel(book, UP_TO_DATE);

    expect(
        await choosePublishToolAndSeeIfItUpdated(page, "PDF & Print"),
        "a book made from one of Bloom's templates should already be up to date",
    ).toBe(false);
});

test("a new book from The Moon and the Cap needs no update [Test Case ID 834]", async ({
    page,
}) => {
    const book = await makeBookFromSampleShell(page, "The Moon and the Cap");
    await waitForPageLayoutUpdateLevel(book, UP_TO_DATE);

    expect(
        await choosePublishToolAndSeeIfItUpdated(page, "PDF & Print"),
        "a book made from one of Bloom's Sample Shells should already be up to date",
    ).toBe(false);
});

test("a failed update is reported, the tool still opens, and the next tool tries again [Test Case ID 834]", async ({
    page,
}) => {
    const book = await makeBookFromTemplate(page, "Basic Book");
    await setPageSize(page, "A5Landscape");
    await waitForPageLayoutUpdateLevel(book, NEEDS_UPDATE);

    await failNextPageUpdateAt(page, 2);
    await selectPublishDestination(page, "PDF & Print");
    const report = await waitForUpdateBookDialogToReportAProblem(page);
    expect(report).toContain("Simulated failure updating page 2");
    expect(
        report,
        "once the update has failed, the dialog should not still ask the user to wait",
    ).not.toContain("Please wait");
    expect(
        await isPublishDestinationShowing(page, "PDF & Print"),
        "the tool should wait until the user has read the problem and closed the dialog",
    ).toBe(false);

    await closeUpdateBookDialog(page);
    await expect
        .poll(() => isPublishDestinationShowing(page, "PDF & Print"), {
            message: "closing the dialog should open the tool the user chose",
        })
        .toBe(true);
    await waitForPageLayoutUpdateLevel(book, NEEDS_UPDATE);

    expect(
        await choosePublishToolAndSeeIfItUpdated(page, "Web"),
        "the next Publish tool should try the failed update again",
    ).toBe(true);
    await waitForPageLayoutUpdateLevel(book, UP_TO_DATE);
});
