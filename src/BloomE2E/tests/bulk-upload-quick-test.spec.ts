// Bulk upload a small collection to dev.bloomlibrary.org for real, and check the three things the
// manual "Bulk Upload Quick Test" watches: a collection with no bookshelf is refused, a first
// upload sends every book, an unchanged re-upload skips them all, and changing one book updates
// only that one. Automates Notion test case 211.
//
// This is the first e2e test that signs in to Bloom Library for real and uploads for real. It can
// do so because a test's Bloom keeps its login in a settings folder of its own (bloomApp.userSettingsDir),
// so the sign-in, and the upload's child Bloom that reads it back, never touch the developer's own
// Bloom. Everything goes to the sandbox, dev.bloomlibrary.org; Bloom refuses under --e2e to upload
// to production at all. The test deletes what it uploads, and any the account had left from before,
// in an afterAll — see helpers/bloomLibraryServer.ts.
//
// What is NOT covered here, and stays on a manual portion of the card: confirming each uploaded
// book on the website against the screenshot on its front cover, which is a human visual check.
//
// The account's password comes from BLOOM_E2E_TESTER_EMAIL_BLORG_PASSWORD (see
// helpers/bloomLibraryAccount.ts): without it the test skips locally and fails on CI.

import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import { selectBook } from "../helpers/collection";
import { switchTab } from "../helpers/workspace";
import {
    acceptAllAgreements,
    clickToFixMissingItem,
    openPublishToWeb,
} from "../helpers/libraryPublish";
import {
    TEST_ENTERPRISE_SUBSCRIPTION_CODE,
    TEST_BOOKSHELVES,
    restartWithCollectionSettings,
} from "../helpers/collectionSettings";
import {
    signBloomIntoLibraryForReal,
    skipIfNoLibraryPassword,
    TEST_ACCOUNT_EMAIL,
} from "../helpers/bloomLibraryAccount";
import {
    deleteAllBooksUploadedBy,
    findBooksUploadedBy,
    type IBloomLibraryLogin,
} from "../helpers/bloomLibraryServer";
import {
    clearBulkUploadLog,
    startCollectionUpload,
    uploadCollectionExpectingBookshelfWarning,
    waitForBulkUploadResult,
} from "../helpers/bulkUpload";

// A collection under the Test enterprise subscription (bulk upload needs an enterprise tier), with
// no bookshelf yet: the first thing the card checks is that a bookshelf-less collection is refused.
test.use({
    collectionSpec: {
        name: "bulk-upload-quick-test",
        languages: ["en"],
        subscriptionCode: TEST_ENTERPRISE_SUBSCRIPTION_CODE,
    },
});

test.describe.configure({ mode: "serial" });

// Four books, as the manual test uses. Each title is distinctive so the server records are easy to
// tell apart in a failure message.
const BOOK_TITLES = [
    "Bulk Upload Quick Test Book 1",
    "Bulk Upload Quick Test Book 2",
    "Bulk Upload Quick Test Book 3",
    "Bulk Upload Quick Test Book 4",
];
const COPYRIGHT_HOLDER = "Bloom Automated Test";
const BOOKSHELF = TEST_BOOKSHELVES[0];

// The login the test signs in with, kept so afterAll can delete what was uploaded.
let login: IBloomLibraryLogin | undefined;
// The folder of each book the test made, in BOOK_TITLES order. Bloom names a book's folder after
// its title, but only after a save, so the folders are looked up rather than assumed.
const bookFolders: string[] = [];

/**
 * Make one book that is ready to upload: a Basic Book with a title, a content page with a word of
 * text (so it has a language to publish), and a copyright. Leaves it saved and returns its folder.
 * Setup, not the behavior under test, so it takes the fast route to each piece.
 */
async function makeUploadableBook(page: Page, title: string): Promise<string> {
    await switchTab(page, "collection");
    await makeBookFromTemplate(page, "Basic Book"); // lands on the cover in the Edit tab
    await typeInGroup(page, ".bookTitle", "en", title);
    await addPage(page, "Just Text");
    const [contentPage] = await getContentPages(page);
    await goToPage(page, contentPage.id);
    await typeInGroup(page, ".bloom-translationGroup", "en", "Hello.");
    // Leave the content page (back to the cover) so Bloom saves what was typed.
    await goToPage(page, (await getPages(page))[0].id);
    // Give it a copyright through the real dialog, reached from Publish: Web's "Click to fix" on the
    // Missing Copyright warning (the direct copyright API deadlocks; see helpers/copyrightAndLicense.ts).
    await openPublishToWeb(page);
    await clickToFixMissingItem(page, "Copyright");
    await setCopyrightHolder(page, COPYRIGHT_HOLDER);
    return findBookFolder(page, title);
}

test.describe("bulk uploading a collection to dev.bloomlibrary.org", () => {
    test.afterAll(async () => {
        // Delete everything the account has on the sandbox, whether this run uploaded it or a
        // crashed earlier run did, so dev.bloomlibrary.org is left clean.
        if (login) await deleteAllBooksUploadedBy(login);
    });

    test("bulk upload: refused without a bookshelf, then new / skipped / updated [Test Case ID 211]", async ({
        page,
        bloomApp,
    }) => {
        skipIfNoLibraryPassword();
        test.setTimeout(900000);

        // Clean the account first, so a crashed earlier run's books do not turn this run's "4 new"
        // into "some updated", and get the login this test signs in and cleans up with.
        login = await signBloomIntoLibraryForReal(page);
        await deleteAllBooksUploadedBy(login);

        // ---- Four uploadable books --------------------------------------------------------------
        for (const title of BOOK_TITLES)
            bookFolders.push(await makeUploadableBook(page, title));

        // ---- A collection with no bookshelf is refused ------------------------------------------
        await selectBook(page, bookFolders[0]);
        await openPublishToWeb(page);
        await acceptAllAgreements(page);
        const warning = await uploadCollectionExpectingBookshelfWarning(page);
        expect(
            warning,
            "The no-bookshelf refusal should name the bookshelf setting.",
        ).toContain("bookshelf");

        // ---- Set the bookshelf, then upload: four new books -------------------------------------
        await restartWithCollectionSettings(bloomApp, {
            languages: ["en"],
            subscriptionCode: TEST_ENTERPRISE_SUBSCRIPTION_CODE,
            bookshelf: BOOKSHELF,
        });
        const pageAfterRestart = bloomApp.page;
        // The restart signed Bloom out of the login it kept only in memory; sign in again for the
        // upload's child process to read from the settings folder.
        login = await signBloomIntoLibraryForReal(pageAfterRestart);

        await selectBook(pageAfterRestart, bookFolders[0]);
        await openPublishToWeb(pageAfterRestart);
        await acceptAllAgreements(pageAfterRestart);

        clearBulkUploadLog(bloomApp.collectionDir);
        await startCollectionUpload(pageAfterRestart);
        const firstUpload = await waitForBulkUploadResult(
            bloomApp.collectionDir,
        );
        expect(
            firstUpload,
            `The first bulk upload should have sent all four books as new. Log:\n${firstUpload.log}`,
        ).toMatchObject({ newBooks: 4, updated: 0, skipped: 0 });

        // The four books really are on the sandbox now, uploaded by the test account.
        const onServer = await findBooksUploadedBy(login);
        expect(
            onServer.map((b) => b.title).sort(),
            `dev.bloomlibrary.org should list the four uploaded books for ${TEST_ACCOUNT_EMAIL}.`,
        ).toEqual([...BOOK_TITLES].sort());
        for (const book of onServer)
            expect(
                book.tags,
                `${book.title} should carry the bookshelf tag it was uploaded under.`,
            ).toContain(`bookshelf:${BOOKSHELF}`);

        // ---- Upload again with nothing changed: four skipped ------------------------------------
        clearBulkUploadLog(bloomApp.collectionDir);
        await startCollectionUpload(pageAfterRestart);
        const secondUpload = await waitForBulkUploadResult(
            bloomApp.collectionDir,
        );
        expect(
            secondUpload,
            `An unchanged re-upload should skip all four books. Log:\n${secondUpload.log}`,
        ).toMatchObject({ newBooks: 0, updated: 0, skipped: 4 });

        // ---- Change one book, upload again: one updated, three skipped --------------------------
        await selectBook(pageAfterRestart, bookFolders[0]);
        await switchTab(pageAfterRestart, "edit");
        const [firstContentPage] = await getContentPages(pageAfterRestart);
        await goToPage(pageAfterRestart, firstContentPage.id);
        await typeInGroup(
            pageAfterRestart,
            ".bloom-translationGroup",
            "en",
            "Hello again.",
        );
        // Leave the page so Bloom saves the change before the upload reads the folder.
        await switchTab(pageAfterRestart, "collection");

        await openPublishToWeb(pageAfterRestart);
        await acceptAllAgreements(pageAfterRestart);
        clearBulkUploadLog(bloomApp.collectionDir);
        await startCollectionUpload(pageAfterRestart);
        const thirdUpload = await waitForBulkUploadResult(
            bloomApp.collectionDir,
        );
        expect(
            thirdUpload,
            `After changing one book, only that book should be updated. Log:\n${thirdUpload.log}`,
        ).toMatchObject({ newBooks: 0, updated: 1, skipped: 3 });
    });
});
