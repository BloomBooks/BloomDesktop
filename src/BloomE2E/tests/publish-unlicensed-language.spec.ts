// A book whose copyright holder has licensed it only for certain languages must not be published
// in any other, by any route. Automates the manual test "Block Unlicensed Language Publish"
// (Test Case ID 619).
//
// The manual test uses one of the Super Bible books, which carry a
// <meta name="bloom-licensed-content-id"> that Bloom looks up in the licensing spreadsheet
// (content-licenses.bloomlibrary.org, see LicenseChecker.cs). This test makes its own book and
// gives it a content id that no row of that spreadsheet names, so no language at all is licensed
// for it: the same answer Bloom gets for a Super Bible book in a language its publisher has not
// licensed. The check is the real one, against the real server, so the test needs the internet.
//
// A spreadsheet that came back empty or unreadable would also leave no language licensed, so the
// first test reads the spreadsheet itself and checks that it has rows and that none names this
// book's content id. The denial the later tests see is then the spreadsheet's answer.
//
// Not checked here: "publishing an Android App stops with a licensing message before building".
// The Apps screen's Prepare installs Reading App Builder before it gets as far as the license
// check; see AUTOMATION-DEBT.md, "The Apps screen installs Reading App Builder before it checks
// anything".

import { type Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { setBookMeta } from "../helpers/bookHtml";
import {
    addPage,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import {
    acceptAllAgreements,
    clickToFixMissingItem,
    clickUploadBook,
    expectMissingRequirements,
    expectUploadStepButtons,
    openPublishToWeb,
    pretendLoginState,
    stopPretendingAboutLogin,
} from "../helpers/libraryPublish";
import { readAndCloseMessageBox } from "../helpers/messageBox";
import { selectPublishDestination } from "../helpers/publish";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "unlicensed-language",
        languages: ["fr", "en"],
        // ePUB and Audio or Video need a subscription tier above Basic.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.describe.configure({ mode: "serial" });

// A content id that no row of the licensing spreadsheet names, so no language is licensed for it.
const UNLICENSED_CONTENT_ID = "bloom-e2e.tc619.unlicensed";
// The collection's first language, by the name the collection gives it. The message must name it.
const LANGUAGE_NAME = "French";
// The words of LicenseChecker.kUnlicenseLanguageMessage before the language name.
const NOT_LICENSED = "has not licensed it for publishing in";
// Where Bloom reads the licensing spreadsheet (LicenseChecker.GetProblemLanguages).
const LICENSE_SERVER = "https://content-licenses.bloomlibrary.org";
const TITLE = "Un livre sous licence";
const SIGNED_IN_EMAIL = "e2e-tester@example.com";

let bookFolder: string;

/** The licensing message, wherever on the shell page it shows. */
function licensingMessage(page: Page) {
    return page.getByText(NOT_LICENSED).first();
}

/** Wait for the licensing message to show and return its text. */
async function expectLicensingMessageShowing(
    page: Page,
    where: string,
): Promise<string> {
    await expect(
        licensingMessage(page),
        `${where} showed no message saying the book is not licensed for publishing.`,
    ).toBeVisible({ timeout: 120000 });
    return (await licensingMessage(page).innerText()).replace(/\s+/g, " ");
}

/**
 * Read the licensing spreadsheet the way Bloom does, and fail unless it has rows and none of them
 * licenses `contentId` for any language. A row's id matches as LicenseChecker.MatchingKey says:
 * exactly, or as a prefix when it ends in "*".
 */
async function expectSpreadsheetLicensesNothingFor(
    contentId: string,
): Promise<void> {
    const response = await fetch(LICENSE_SERVER);
    expect(
        response.ok,
        `The licensing spreadsheet at ${LICENSE_SERVER} answered ${response.status}.`,
    ).toBe(true);
    const { values } = (await response.json()) as { values?: unknown[][] };
    const rows = (values ?? []).filter(
        (row) =>
            typeof row[0] === "string" &&
            typeof row[1] === "string" &&
            row[0].trim() !== "" &&
            row[1].trim() !== "" &&
            row[0] !== "content-id",
    ) as string[][];
    expect(
        rows.length,
        "The licensing spreadsheet has no rows, so a denial would prove nothing about Bloom's check.",
    ).toBeGreaterThan(0);
    const matching = rows.filter(([id]) => {
        const key = id.trim();
        return key.endsWith("*")
            ? contentId.startsWith(key.slice(0, -1))
            : key === contentId;
    });
    expect(
        matching,
        `The licensing spreadsheet licenses ${contentId}, which the test needs to be unlicensed.`,
    ).toEqual([]);
}

/**
 * Fail if the screen is showing a preview of the book: its preview frame (DeviceAndControls) has
 * been given the book's address.
 */
async function expectNoPreview(page: Page, where: string): Promise<void> {
    const src =
        (await page.locator("#preview-iframe").getAttribute("src")) ?? "";
    expect(
        src === "" || src === "stopPreview",
        `${where} showed a preview of a book it may not publish (${src}).`,
    ).toBe(true);
}

/** Click PREVIEW on a screen that makes a preview of the publication. */
async function clickPreview(page: Page): Promise<void> {
    const preview = page.locator('[aria-label="refresh preview"]');
    await preview.waitFor({ state: "visible", timeout: 60000 });
    await preview.click();
}

/** Close the progress dialog that shows the licensing message, with its Close button. */
async function closeProgressDialog(page: Page): Promise<void> {
    const dialog = page.getByRole("dialog");
    await dialog.getByRole("button", { name: "Close", exact: true }).click();
    await expect(
        dialog,
        "The progress dialog did not close when its Close button was clicked.",
    ).toBeHidden();
}

test.describe("a book not licensed for the collection's language cannot be published", () => {
    test("make a book that carries a licensed-content id [Test Case ID 619]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(600000);
        await expectSpreadsheetLicensesNothingFor(UNLICENSED_CONTENT_ID);
        await makeBookFromTemplate(page, "Basic Book");
        const frontCover = (await getPages(page))[0];
        await goToPage(page, frontCover.id);
        await typeInGroup(page, ".bookTitle", "fr", TITLE);
        await addPage(page, "Just Text");
        const [contentPage] = await getContentPages(page);
        await goToPage(page, contentPage.id);
        await typeInGroup(
            page,
            ".bloom-translationGroup",
            "fr",
            "Au commencement était la Parole.",
        );
        // Bloom writes a page only when the book leaves it.
        await goToPage(page, frontCover.id);

        // A copyright, so that Publish: Web gets as far as its Upload button.
        await openPublishToWeb(page);
        await clickToFixMissingItem(page, "Copyright");
        await setCopyrightHolder(page, "Test Publisher");
        await expectMissingRequirements(
            page,
            [],
            "Publish: Web still wanted something before the book could be uploaded.",
        );

        // Bloom reads the content id only from the saved book, so add it while Bloom is stopped.
        await switchTab(page, "collection");
        // Bloom renamed the book's folder to match its title.
        bookFolder = await findBookFolder(page, TITLE);
        const shell = await bloomApp.restart(() =>
            setBookMeta(
                bookFolder,
                "bloom-licensed-content-id",
                UNLICENSED_CONTENT_ID,
            ),
        );
        await selectBook(shell, bookFolder);
    });

    test("PDF & Print shows a warning instead of making the PDF [Test Case ID 619]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);
        await selectBook(page, bookFolder);
        await selectPublishDestination(page, "PDF & Print");
        const printButton = page.getByRole("button", { name: "Print..." });
        await printButton.waitFor({ state: "visible", timeout: 60000 });

        for (const mode of ["Simple", "Booklet Cover", "Booklet Insides"]) {
            // THE ACTION UNDER TEST: choosing a PDF mode, which asks Bloom to make the PDF.
            await page.getByText(mode, { exact: true }).click();
            const box = await readAndCloseMessageBox(
                bloomApp.bloomPid,
                "Warning",
                120000,
            );
            expect(
                box,
                `Choosing "${mode}" showed no Warning box saying the book is not licensed.`,
            ).toBeDefined();
            const said = box!.texts.join(" ");
            expect(said, `The "${mode}" Warning box said: ${said}`).toContain(
                NOT_LICENSED,
            );
            expect(
                said,
                `The "${mode}" Warning box did not name the collection's language.`,
            ).toContain(LANGUAGE_NAME);
            await expect(
                printButton,
                `Print was offered for "${mode}" even though no PDF may be made.`,
            ).toBeDisabled();
        }
    });

    test("Web shows the licensing message instead of uploading [Test Case ID 619]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await selectBook(page, bookFolder);
        await openPublishToWeb(page);
        await acceptAllAgreements(page);
        await pretendLoginState(page, SIGNED_IN_EMAIL);
        try {
            await expectUploadStepButtons(
                page,
                { signIn: "absent", uploadBook: "enabled", signOut: "enabled" },
                "A signed-in user with the agreements ticked was not offered the Upload Book button.",
            );

            // Bloom answers the upload request only when the whole attempt is over, so waiting
            // for that answer lets the check below see everything the attempt wrote.
            const uploadAttemptOver = page.waitForResponse(
                (response) =>
                    /\/libraryPublish\/upload$/.test(
                        new URL(response.url()).pathname,
                    ),
                { timeout: 120000 },
            );
            // THE ACTION UNDER TEST: Upload Book.
            await clickUploadBook(page);
            const message = await expectLicensingMessageShowing(
                page,
                "Upload Book",
            );
            expect(
                message,
                "The upload's licensing message did not name the collection's language.",
            ).toContain(LANGUAGE_NAME);
            await uploadAttemptOver;
            // The check comes before anything is sent, and Bloom says nothing more after it.
            await expect(
                page.getByTestId("progress-box-log"),
                "Bloom went on to upload after saying the book is not licensed.",
            ).not.toContainText(
                /Uploading|Upload complete|bloomlibrary\.org\/book/i,
            );
        } finally {
            await stopPretendingAboutLogin(page);
        }
    });

    test("BloomPUB shows the licensing message instead of a preview, and every way to send it is closed [Test Case ID 619]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await selectBook(page, bookFolder);
        await selectPublishDestination(page, "BloomPUB");
        // THE ACTION UNDER TEST: PREVIEW, which makes the BloomPUB.
        await clickPreview(page);
        const message = await expectLicensingMessageShowing(page, "BloomPUB");
        expect(
            message,
            "The BloomPUB licensing message did not name the collection's language.",
        ).toContain(LANGUAGE_NAME);
        await closeProgressDialog(page);
        await expectNoPreview(page, "BloomPUB");

        // Each way of sending the book on is closed: to a file, over Wi-Fi, over a USB cable.
        const ways: [string, string][] = [
            ["Save BloomPUB File", "Save..."],
            ["Share over Wi-Fi", "Share"],
            ["Send over USB Cable", "Connect with USB cable"],
        ];
        for (const [way, button] of ways) {
            await page.getByText(way, { exact: true }).click();
            await expect(
                page.getByRole("button", { name: button, exact: true }),
                `"${way}" offered its "${button}" button for a book it may not publish.`,
            ).toBeDisabled();
        }
    });

    test("ePUB shows the licensing message instead of a preview [Test Case ID 619]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await selectBook(page, bookFolder);
        await selectPublishDestination(page, "ePUB");
        // THE ACTION UNDER TEST: PREVIEW, which makes the ePUB.
        await clickPreview(page);
        const message = await expectLicensingMessageShowing(page, "ePUB");
        expect(
            message,
            "The ePUB licensing message did not name the collection's language.",
        ).toContain(LANGUAGE_NAME);
        await closeProgressDialog(page);
        await expectNoPreview(page, "ePUB");
        await expect(
            page.getByRole("button", { name: "Save...", exact: true }),
            "ePUB offered Save for a book it may not publish.",
        ).toBeDisabled();
    });

    test("Audio or Video shows the licensing message instead of a preview [Test Case ID 619]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await selectBook(page, bookFolder);
        await selectPublishDestination(page, "Audio or Video");
        // THE ACTION UNDER TEST: PREVIEW, which makes the book that is recorded.
        await clickPreview(page);
        const message = await expectLicensingMessageShowing(
            page,
            "Audio or Video",
        );
        expect(
            message,
            "The Audio or Video licensing message did not name the collection's language.",
        ).toContain(LANGUAGE_NAME);
        await closeProgressDialog(page);
        await expectNoPreview(page, "Audio or Video");
        await expect(
            page.getByRole("button", { name: "Record", exact: true }),
            "Audio or Video offered Record for a book it may not publish.",
        ).toBeDisabled();
    });
});
