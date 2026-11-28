// A book keeps its branding when it is uploaded to Bloom Library and updated there, and Bloom makes
// the user acknowledge it when an update would change the branding. Automates the manual test
// "Update Without Losing Branding" (Test Case ID 110).
//
// The collection has the Test subscription (kEnterpriseSubscriptionCode), whose branding puts a
// butterfly on the credits page and the covers. The test uploads a book to dev.bloomlibrary.org for
// real, adds a page and uploads it again as an update of the same book, then adds another page,
// clears the Subscription Code in the Settings dialog (which restarts Bloom on the Default branding)
// and uploads a third time. After each upload it reads the copy the upload left on the sandbox,
// which is what Bloom Library shows and what its "Download for Editing" hands to Bloom: the pages
// it holds, the subscription its meta.json names, and whether its HTML carries the butterfly.
//
// The manual case opens the first upload with Download for Editing in a new Bloom. That goes
// through the system browser and a bloom:// link, which a test cannot drive (see
// bloomLibraryDownload.ts), and what the download does is the subject of Test Case ID 109. So this
// test checks the files that the download would fetch, not a Bloom opened on them.
//
// Like upload-only-changed-files.spec.ts, it signs in to the sandbox for real as the shared test
// account, gives its book a title with a prefix of its own and a part unique to the run, and
// deletes only its own books (see findBooksUploadedBy). The account's password comes from
// BLOOM_E2E_TESTER_EMAIL_BLORG_PASSWORD: without it the test skips locally and fails on CI.

import type { Locator, Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    findBookFolder,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import { getUiString } from "../helpers/api";
import { selectBook } from "../helpers/collection";
import {
    kEnterpriseSubscriptionCode,
    openCollectionSettings,
    restartFromCollectionSettings,
    showCollectionSettingsPage,
    typeSubscriptionCode,
} from "../helpers/collectionSettings";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import {
    acceptAllAgreements,
    clickToFixMissingItem,
    openPublishToWeb,
    uploadBookForReal,
} from "../helpers/libraryPublish";
import {
    signBloomIntoLibraryForReal,
    skipIfNoLibraryPassword,
} from "../helpers/bloomLibraryAccount";
import {
    deleteBooksUploadedBy,
    fetchUploadedBookHtml,
    findBooksUploadedBy,
    getLatestUploadBaseUrl,
    getSubscriptionOfUploadedBook,
    type IBloomLibraryLogin,
} from "../helpers/bloomLibraryServer";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "upload-keeps-branding",
        languages: ["en"],
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

// The book's title starts with a prefix that no other real-upload test's books start with, and
// ends with a part of this run's own.
const TITLE_PREFIX = "Upload Keeps Branding";
const BOOK_TITLE = `${TITLE_PREFIX} ${Date.now().toString(36)}`;
// A run deletes this test's books that are at least this old, which only a crashed run leaves.
const STALE_BOOK_MINUTES = 60;

// The subscription descriptor of kEnterpriseSubscriptionCode, and the one a collection without a
// code has.
const TEST_SUBSCRIPTION = "Test";
const DEFAULT_SUBSCRIPTION = "Default";
// The picture the Test branding puts on the credits page and the covers
// (src/content/branding/Test/branding.json). No other branding uses it.
const TEST_BRANDING_PICTURE = "butterfly.png";

// The text of the page each update adds, by which the uploaded copy shows it has the page.
const FIRST_NEW_PAGE_TEXT = "The page added before the first update.";
const SECOND_NEW_PAGE_TEXT = "The page added before the second update.";

let login: IBloomLibraryLogin | undefined;

/** The part of libraryPublish/getUploadCollisionInfo's answer that this test reads. */
interface ICollisionInfo {
    oldSubscriptionDescriptor?: string;
    newSubscriptionDescriptor?: string;
}

/** What the copy of the book on the sandbox holds after an upload. */
interface IUploadedCopy {
    subscription: string | undefined;
    hasTestBranding: boolean;
    hasFirstNewPage: boolean;
    hasSecondNewPage: boolean;
}

/** Read the copy that the latest upload of the book left on the sandbox. */
async function readUploadedCopy(): Promise<IUploadedCopy> {
    const books = await findBooksUploadedBy(login!, BOOK_TITLE);
    expect(
        books.length,
        `The sandbox should hold exactly one book titled "${BOOK_TITLE}": an update must not add another.`,
    ).toBe(1);
    const baseUrl = await getLatestUploadBaseUrl(books[0]);
    const html = await fetchUploadedBookHtml(baseUrl);
    return {
        subscription: await getSubscriptionOfUploadedBook(baseUrl),
        hasTestBranding: html.includes(TEST_BRANDING_PICTURE),
        hasFirstNewPage: html.includes(FIRST_NEW_PAGE_TEXT),
        hasSecondNewPage: html.includes(SECOND_NEW_PAGE_TEXT),
    };
}

/**
 * Wait for the book's record on the sandbox to name the Test subscription. The "same book?" dialog
 * compares the book's subscription with the one on the record, which an upload does not set: only
 * the harvester does, once it has processed an upload (BL-17234). Until then the dialog sees no
 * subscription on Bloom Library and never shows its acknowledgement box, so each update waits for
 * the record to name one, as it would by the time a person makes another change.
 */
async function waitForRecordToNameSubscription(): Promise<void> {
    await expect
        .poll(
            async () =>
                (await findBooksUploadedBy(login!, BOOK_TITLE))[0]
                    ?.subscription,
            {
                timeout: 300000,
                intervals: [5000],
                message:
                    "The book's record on the sandbox never named the Test subscription; has the sandbox's harvester stopped? (BL-17234)",
            },
        )
        .toBe(TEST_SUBSCRIPTION);
}

/** Add a "Just Text" page after the current one and type `text` on it. */
async function addTextPage(page: Page, text: string): Promise<void> {
    await switchTab(page, "edit");
    // The Edit tab drops an added page while it is still loading the page it opens on.
    await waitForEditTabSettled(page);
    const before = (await getContentPages(page)).map((p) => p.id);
    await addPage(page, "Just Text");
    const added = (await getContentPages(page)).find(
        (p) => !before.includes(p.id),
    );
    expect(
        added,
        "Adding a Just Text page added no content page.",
    ).toBeTruthy();
    await goToPage(page, added!.id);
    await typeInGroup(page, ".bloom-translationGroup", "en", text);
}

/**
 * The check box in the "same book?" dialog that acknowledges a change of subscription, found by
 * the part of its localized label before the first subscription's name ('The subscription was "'),
 * so that it is found whichever two subscriptions the label names.
 */
async function subscriptionChangeBox(
    page: Page,
    dialog: Locator,
): Promise<Locator> {
    const label = await getUiString(
        page,
        "PublishTab.UploadCollisionDialog.ChangeSubscription",
        'The subscription was "{0}" but is now "{1}". This may change logos and other material. Check this box if this is what you want.',
    );
    return dialog.getByRole("checkbox", { name: label.split("{0}")[0] });
}

/** The Upload button of the "same book?" dialog. */
async function dialogUploadButton(
    page: Page,
    dialog: Locator,
): Promise<Locator> {
    return dialog.getByRole("button", {
        name: await getUiString(page, "Common.Upload", "Upload"),
        exact: true,
    });
}

test.describe("updating a branded book on dev.bloomlibrary.org", () => {
    test.afterAll(async () => {
        if (login) await deleteBooksUploadedBy(login, BOOK_TITLE);
    });

    test("an update keeps the branding, and changing it must be acknowledged [Test Case ID 110]", async ({
        page,
        bloomApp,
    }) => {
        skipIfNoLibraryPassword();
        // Three real uploads, each with a thumbnail to make, and a restart.
        test.setTimeout(900000);

        login = await signBloomIntoLibraryForReal(page);
        // Clear away what crashed earlier runs left, leaving any run going on now alone.
        await deleteBooksUploadedBy(login, TITLE_PREFIX, STALE_BOOK_MINUTES);

        // ---- A book with the Test branding, uploaded ------------------------------------------------
        await makeBookFromTemplate(page, "Basic Book");
        await typeInGroup(page, ".bookTitle", "en", BOOK_TITLE);
        // Leaving Edit for Publish saves the book, which names its folder after the title.
        await openPublishToWeb(page);
        await clickToFixMissingItem(page, "Copyright");
        await setCopyrightHolder(page, "Bloom Automated Test");
        await acceptAllAgreements(page);
        await uploadBookForReal(page);
        const bookFolder = await findBookFolder(page, BOOK_TITLE);

        expect(
            await readUploadedCopy(),
            "The first upload should carry the Test branding.",
        ).toEqual({
            subscription: TEST_SUBSCRIPTION,
            hasTestBranding: true,
            hasFirstNewPage: false,
            hasSecondNewPage: false,
        });

        // ---- A page added, uploaded as an update: the branding stays -------------------------------
        await addTextPage(page, FIRST_NEW_PAGE_TEXT);
        // Without it the dialog would show no acknowledgement box whatever the subscriptions.
        await waitForRecordToNameSubscription();
        await openPublishToWeb(page);
        await acceptAllAgreements(page);
        let askedSameBook = false;
        await uploadBookForReal(page, undefined, async (dialog) => {
            askedSameBook = true;
            await expect(
                await subscriptionChangeBox(page, dialog),
                "With the subscription unchanged, the dialog should not ask to acknowledge a change.",
            ).toHaveCount(0);
        });
        expect(
            askedSameBook,
            "Uploading the book again should ask whether it is an update of the book on Bloom Library.",
        ).toBe(true);

        expect(
            await readUploadedCopy(),
            "After the first update, the book should have the new page and still carry the Test branding.",
        ).toEqual({
            subscription: TEST_SUBSCRIPTION,
            hasTestBranding: true,
            hasFirstNewPage: true,
            hasSecondNewPage: false,
        });

        // ---- Another page, the Subscription Code cleared, uploaded as an update --------------------
        await addTextPage(page, SECOND_NEW_PAGE_TEXT);
        await switchTab(page, "collection");
        await openCollectionSettings(page);
        await showCollectionSettingsPage(page, "Subscription");
        await typeSubscriptionCode(page, "");
        // Clearing the code changes the branding, which needs a restart; this fails if OK does not
        // offer one.
        const reopened = await restartFromCollectionSettings(bloomApp);
        await waitForRecordToNameSubscription();
        await selectBook(reopened, bookFolder);
        await openPublishToWeb(reopened);
        await acceptAllAgreements(reopened);
        // What Bloom tells the "same book?" dialog about the two subscriptions, so that a missing
        // acknowledgement box can be told apart from a book that Bloom never moved to Default.
        // The dialog asks for it once; asking again from the test makes Bloom answer 503.
        const collisionInfos: ICollisionInfo[] = [];
        reopened.on("response", async (response) => {
            if (
                response.url().includes("libraryPublish/getUploadCollisionInfo")
            )
                collisionInfos.push(await response.json().catch(() => ({})));
        });
        askedSameBook = false;
        await uploadBookForReal(reopened, undefined, async (dialog) => {
            askedSameBook = true;
            const acknowledge = await subscriptionChangeBox(reopened, dialog);
            const upload = await dialogUploadButton(reopened, dialog);
            // The listener records the answer only once it has parsed it, so wait for it.
            await expect
                .poll(
                    () => {
                        const info = collisionInfos[collisionInfos.length - 1];
                        return {
                            old: info?.oldSubscriptionDescriptor,
                            new: info?.newSubscriptionDescriptor,
                        };
                    },
                    {
                        message:
                            "Bloom should tell the dialog that the subscription on Bloom Library is Test and the book's is now Default.",
                    },
                )
                .toEqual({ old: TEST_SUBSCRIPTION, new: DEFAULT_SUBSCRIPTION });
            await expect(
                acknowledge,
                `The dialog should ask to acknowledge that the subscription changes from "${TEST_SUBSCRIPTION}" to "${DEFAULT_SUBSCRIPTION}".`,
            ).toBeVisible();
            await expect(acknowledge).not.toBeChecked();
            await expect(
                upload,
                "Upload should stay disabled until the change of subscription is acknowledged.",
            ).toBeDisabled();
            await acknowledge.check();
            await expect(
                upload,
                "Upload should be enabled once the change of subscription is acknowledged.",
            ).toBeEnabled();
        });
        expect(
            askedSameBook,
            "Uploading the book again should ask whether it is an update of the book on Bloom Library.",
        ).toBe(true);

        expect(
            await readUploadedCopy(),
            "After the second update, the book should have both new pages and no longer carry the Test branding.",
        ).toEqual({
            subscription: DEFAULT_SUBSCRIPTION,
            hasTestBranding: false,
            hasFirstNewPage: true,
            hasSecondNewPage: true,
        });
    });
});
