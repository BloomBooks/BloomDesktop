// Bulk upload a small collection to dev.bloomlibrary.org for real, and check what the manual "Bulk
// Upload Quick Test" watches: a collection with no bookshelf is refused, a first upload sends every
// book, an unchanged re-upload skips them all, changing one book updates only that one, and moving
// the collection to another bookshelf updates them all and puts them on the new shelf with the
// collection's current front/back matter. Automates Notion test case 211, whose last steps absorbed
// the retired "Bulk Upload Across Bookshelves Applies Xmatter" (test case 212).
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
import type { IBloomApp } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { readXmatterPackOfBook } from "../helpers/bookHtml";
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
    type ICollectionSettings,
} from "../helpers/collectionSettings";
import {
    signBloomIntoLibraryForReal,
    skipIfNoLibraryPassword,
    TEST_ACCOUNT_EMAIL,
} from "../helpers/bloomLibraryAccount";
import {
    deleteAllBooksUploadedBy,
    findBooksUploadedBy,
    getXmatterPackOfBookOnServer,
    type IBloomLibraryLogin,
} from "../helpers/bloomLibraryServer";
import {
    uploadCollection,
    uploadCollectionExpectingBookshelfWarning,
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
// The collection goes on the first shelf, then moves to the second.
const [FIRST_BOOKSHELF, SECOND_BOOKSHELF] = TEST_BOOKSHELVES;
// The front/back matter pack the collection starts with (the fixture's default) and the one it
// moves to along with the bookshelf, so the last upload has to bring every book up to date.
const FIRST_XMATTER_PACK = "Factory";
const SECOND_XMATTER_PACK = "Traditional";

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

/**
 * Give the collection these settings (on top of its language and subscription), the way a person
 * does in the Settings dialog, and come back signed in with the first book selected, ready to
 * upload. The restart signs Bloom out of the login it kept only in memory, so this signs in again
 * for the upload's child process to read from the settings folder. Returns the new shell page.
 */
async function restartReadyToUpload(
    bloomApp: IBloomApp,
    settings: Pick<ICollectionSettings, "bookshelf" | "xmatterPack">,
): Promise<Page> {
    await restartWithCollectionSettings(bloomApp, {
        languages: ["en"],
        subscriptionCode: TEST_ENTERPRISE_SUBSCRIPTION_CODE,
        ...settings,
    });
    const page = bloomApp.page;
    login = await signBloomIntoLibraryForReal(page);
    await selectBook(page, bookFolders[0]);
    return page;
}

/**
 * Check the four books on the sandbox, which is the card's "on Blorg, the books are on the bookshelf
 * you set and carry the collection's xmatter": all four are there under the test account, each on
 * this bookshelf, and each uploaded with this front/back matter pack. (The record's branding is not
 * checked: the server fills that field in some time after the upload finishes, so a check right
 * after the tally is flaky.)
 */
async function expectBooksOnServer(
    account: IBloomLibraryLogin,
    bookshelf: string,
    xmatterPack: string,
): Promise<void> {
    const onServer = await findBooksUploadedBy(account);
    expect(
        onServer.map((b) => b.title).sort(),
        `dev.bloomlibrary.org should list the four uploaded books for ${TEST_ACCOUNT_EMAIL}.`,
    ).toEqual([...BOOK_TITLES].sort());
    for (const book of onServer) {
        // "On", not "only on": after the move below, the sandbox still lists the first shelf as
        // well (seen 2026-09-04). Bloom sends only the current shelf's tag, dropping any earlier
        // bookshelf tag (BookUpload.UploadBookAsync), so it is the server that keeps the old
        // one when a re-upload lands. Whether that is meant is an open question for the library
        // team; this checks what the card asks for, that the books sit on the new shelf.
        expect(
            book.bookshelves,
            `${book.title} should be on the ${bookshelf} bookshelf.`,
        ).toContain(bookshelf);
        expect(
            await getXmatterPackOfBookOnServer(book),
            `${book.title} should have been uploaded with the collection's ${xmatterPack} front/back matter.`,
        ).toBe(xmatterPack);
    }
}

test.describe("bulk uploading a collection to dev.bloomlibrary.org", () => {
    test.afterAll(async () => {
        // Delete everything the account has on the sandbox, whether this run uploaded it or a
        // crashed earlier run did, so dev.bloomlibrary.org is left clean.
        if (login) await deleteAllBooksUploadedBy(login);
    });

    test("bulk upload: refused without a bookshelf, then new / skipped / updated / moved to another bookshelf [Test Case ID 211]", async ({
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
        const pageAfterRestart = await restartReadyToUpload(bloomApp, {
            bookshelf: FIRST_BOOKSHELF,
        });
        const firstUpload = await uploadCollection(
            pageAfterRestart,
            bloomApp.collectionDir,
        );
        expect(
            firstUpload,
            `The first bulk upload should have sent all four books as new. Log:\n${firstUpload.log}`,
        ).toMatchObject({ newBooks: 4, updated: 0, skipped: 0 });
        // The four books really are on the sandbox now, on the shelf and with the pack the
        // collection had.
        await expectBooksOnServer(login!, FIRST_BOOKSHELF, FIRST_XMATTER_PACK);

        // ---- Upload again with nothing changed: four skipped ------------------------------------
        const secondUpload = await uploadCollection(
            pageAfterRestart,
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
        const thirdUpload = await uploadCollection(
            pageAfterRestart,
            bloomApp.collectionDir,
        );
        expect(
            thirdUpload,
            `After changing one book, only that book should be updated. Log:\n${thirdUpload.log}`,
        ).toMatchObject({ newBooks: 0, updated: 1, skipped: 3 });

        // ---- Move the collection to another bookshelf, with another front/back matter pack:
        //      all four updated, and each lands on the new shelf with the new pack --------------
        // The pack changes along with the shelf because that is what the card's xmatter check needs:
        // books nobody has opened since the pack changed must still go up with the collection's
        // current pack, which bulk upload gets by bringing each book up to date before it hashes it
        // (BulkUploader.UploadBookInternal). The shelf alone would already make every book count as
        // changed, since the hash a skip is decided on covers the collection file too.
        const pageAfterMove = await restartReadyToUpload(bloomApp, {
            bookshelf: SECOND_BOOKSHELF,
            xmatterPack: SECOND_XMATTER_PACK,
        });
        // Sanity check: the books nobody selected still carry the old pack on disk, so the new pack
        // on the server can only come from the upload bringing them up to date. (Selecting the first
        // book, above, may already have brought that one up to date, so it is left out.)
        for (const folder of bookFolders.slice(1))
            expect(
                readXmatterPackOfBook(folder),
                `${folder} should still have the old front/back matter until the upload brings it up to date.`,
            ).toBe(FIRST_XMATTER_PACK);
        const fourthUpload = await uploadCollection(
            pageAfterMove,
            bloomApp.collectionDir,
        );
        expect(
            fourthUpload,
            `Moving the collection to another bookshelf should update all four books. Log:\n${fourthUpload.log}`,
        ).toMatchObject({ newBooks: 0, updated: 4, skipped: 0 });
        await expectBooksOnServer(
            login!,
            SECOND_BOOKSHELF,
            SECOND_XMATTER_PACK,
        );
    });
});
