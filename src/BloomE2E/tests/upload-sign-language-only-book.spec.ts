// A book whose only content is a sign language video, with no text on any page, can be uploaded to
// Bloom Library. Automates the manual test "Upload SL-Only Book" (Test Case ID 592), which came from
// BL-10004, where Bloom refused to upload such a book because it found no language with text.
//
// The test makes a book from the Sign Language template in a collection with a sign language, gives
// it one "Just Video" page with a video in it and no other content page, and uploads it to
// dev.bloomlibrary.org for real. It checks that the Web screen offers Upload Book, that the upload
// finishes and sends the video, and that the sandbox then has the book, uploaded as a sign language
// book in the collection's sign language. The book has a title, as every uploaded book must
// (BloomLibraryPublishModel.IsTitleOKToPublish); "no text" in the case means no text on its pages.
//
// Like the other real-upload tests, it signs in to the sandbox as the shared test account, so its
// book's title has a prefix of its own, and it deletes only books with that prefix (see
// findBooksUploadedBy). Each run's title is unique, and a run clears away only this test's books
// that are an hour old, so that two runs at once leave each other's book alone. The account's
// password comes from BLOOM_E2E_TESTER_EMAIL_BLORG_PASSWORD: without it the test skips locally and
// fails on CI.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import {
    acceptAllAgreements,
    clickToFixMissingItem,
    expectUploadStepButtons,
    openPublishToWeb,
    uploadBookForReal,
} from "../helpers/libraryPublish";
import {
    signBloomIntoLibraryForReal,
    skipIfNoLibraryPassword,
} from "../helpers/bloomLibraryAccount";
import {
    deleteBooksUploadedBy,
    findBooksUploadedBy,
    getFeaturesOfUploadedBook,
    getLatestUploadBaseUrl,
    type IBloomLibraryLogin,
} from "../helpers/bloomLibraryServer";
import {
    chooseVideoFile,
    getVideoFileName,
    SHORT_VIDEO,
    videoBoxes,
} from "../helpers/videos";

const kSignLanguageTemplate = "Sign Language";
const kSignLanguage = "ase";

test.use({
    collectionSpec: {
        name: "upload-sign-language-only-book",
        languages: ["en"],
        signLanguage: kSignLanguage,
    },
});

// The book's title starts with a prefix that no other real-upload test's books start with, and
// ends with a part of this run's own.
const TITLE_PREFIX = "Upload Sign Language Only";
const BOOK_TITLE = `${TITLE_PREFIX} ${Date.now().toString(36)}`;
// A run deletes this test's books that are at least this old, which only a crashed run leaves.
const STALE_BOOK_MINUTES = 60;

let login: IBloomLibraryLogin | undefined;

test.describe("uploading a sign-language-only book to dev.bloomlibrary.org", () => {
    test.afterAll(async () => {
        if (login) await deleteBooksUploadedBy(login, BOOK_TITLE);
    });

    test("a book with only a sign language video and no text uploads [Test Case ID 592]", async ({
        page,
    }) => {
        skipIfNoLibraryPassword();
        // One real upload with a video re-encoding before it.
        test.setTimeout(600000);

        login = await signBloomIntoLibraryForReal(page);
        // Clear away what crashed earlier runs left, leaving any run going on now alone.
        await deleteBooksUploadedBy(login, TITLE_PREFIX, STALE_BOOK_MINUTES);

        // ---- A book whose one content page is a video ----------------------------------------------
        await makeBookFromTemplate(page, kSignLanguageTemplate);
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        expect(
            await getContentPages(page),
            "sanity check: a book made from a template starts with no content page",
        ).toEqual([]);

        await addPage(page, "Just Video", 1, kSignLanguageTemplate);
        const contentPages = await getContentPages(page);
        expect(
            contentPages.length,
            'adding a "Just Video" page should give the book its one content page',
        ).toBe(1);
        await goToPage(page, contentPages[0].id);
        // Bloom uploads a book with no language of text only when no content page has a text box
        // (Book.HasOnlyPictureOnlyPages); an image description box does not count.
        await expect(
            editablePageFrame(page).locator(
                ".bloom-page .bloom-translationGroup:not(.bloom-imageDescription)",
            ),
            "sanity check: the Just Video page should have no text box",
        ).toHaveCount(0);
        await chooseVideoFile(page, videoBoxes(page).first(), SHORT_VIDEO);
        const video = await getVideoFileName(videoBoxes(page).first());
        await goToPage(page, (await getPages(page))[0].id);

        // A copyright, which an upload needs, through Publish: Web's "Click to fix".
        await openPublishToWeb(page);
        await clickToFixMissingItem(page, "Copyright");
        await setCopyrightHolder(page, "Bloom Automated Test");

        // ---- Bloom lets it be uploaded, and the upload goes through --------------------------------
        await openPublishToWeb(page);
        await acceptAllAgreements(page);
        await expectUploadStepButtons(
            page,
            { signIn: "absent", uploadBook: "enabled", signOut: "enabled" },
            "Publish: Web should let a signed-in user upload a book whose only content is a sign language video.",
        );
        const sent = await uploadBookForReal(page);
        expect(
            sent,
            `The upload should have sent the book's video (${video}). It sent: ${sent.join(", ")}`,
        ).toContain(video);

        // ---- The sandbox has it, as a sign language book -------------------------------------------
        const books = await findBooksUploadedBy(login, BOOK_TITLE);
        expect(
            books.map((book) => book.title),
            "The sandbox should have the one book this run uploaded.",
        ).toEqual([BOOK_TITLE]);
        const features = await getFeaturesOfUploadedBook(
            await getLatestUploadBaseUrl(books[0]),
        );
        expect(
            features,
            `The uploaded book should be marked as a sign language book in ${kSignLanguage}. Its features: ${features.join(", ")}`,
        ).toEqual(
            expect.arrayContaining([
                "signLanguage",
                `signLanguage:${kSignLanguage}`,
            ]),
        );
    });
});
