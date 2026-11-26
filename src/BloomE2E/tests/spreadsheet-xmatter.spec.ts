// A book's front matter goes through a spreadsheet as book data, not as pages, so it lands where
// the pack of the book it is imported into puts it. A book made under the Paper Saver pack, whose
// credits page is the inside of the front cover, is exported with More > Export to Spreadsheet...;
// the collection then changes to the Traditional pack, whose credits page is the back of the title
// page, and the spreadsheet is imported with More > Import Content from Spreadsheet... into a new
// blank book. The new book has its title, its copyright on its own credits page after the title
// page, and the content page with its text and picture.
//
// Automates the manual test "Spreadsheet XMatter Handling" (Test Case ID 637). Its Notion card has
// no steps of its own; the steps are the ones of its source, TC487 in BloomBooks/bloom-test-cases.
//
// The case changes the pack in the Settings dialog and lets Bloom restart; this test rewrites the
// collection's settings and restarts Bloom (restartWithCollectionSettings), since the dialog is
// tested by change-xmatter-pack.spec.ts. It enters a subscription code for the spreadsheet
// commands the same way (collectionSpec). The case finds a picture of a car with the image
// toolbox's search, which needs the Art Of Reading collection; this test puts a picture from its
// fixtures on the page, as choosing a file does.
//
// Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to the
// front, under --dont-disturb too, so the test does not run where it would take a developer's
// keyboard; see "Export to Spreadsheet opens the spreadsheet" in AUTOMATION-DEBT.md.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page } from "@playwright/test";
import { expect, test, type IBloomApp } from "../fixtures/bloomTest";
import { launchWithDontDisturb } from "../fixtures/launchBloom";
import {
    addPage,
    editablePageFrame,
    editBook,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    type IBookPage,
} from "../helpers/bookMaking";
import { bookHtmlPath, readXmatterPackOfBook } from "../helpers/bookHtml";
import {
    findBookFolderById,
    readBookId,
    selectBook,
} from "../helpers/collection";
import {
    kEnterpriseSubscriptionCode,
    restartWithCollectionSettings,
} from "../helpers/collectionSettings";
import { setCopyrightHolder } from "../helpers/copyrightAndLicense";
import {
    goToCreditsPage,
    openCopyrightDialogFromCreditsPage,
} from "../helpers/creditsPage";
import { chooseImageFile } from "../helpers/images";
import {
    exportSelectedBookToSpreadsheet,
    importSpreadsheetIntoSelectedBook,
} from "../helpers/spreadsheet";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

const kLanguages = ["en"];

test.use({
    collectionSpec: {
        name: "spreadsheet-xmatter",
        languages: kLanguages,
        // The spreadsheet commands need a Local Community subscription or better. The collection
        // starts on Factory, the pack the Settings dialog calls Paper Saver.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

test.skip(
    launchWithDontDisturb(),
    "Bloom opens the exported spreadsheet in the machine's spreadsheet program and brings it to " +
        "the front, which would take the keyboard. Run with BLOOM_E2E_DONT_DISTURB=0.",
);

const kTitle = "XMatter Book";
const kHolder = "Tester";
const kText = "This is a car";
const kPicture = "bird.png";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

/** The front matter pages of the selected book, before its first content page. */
async function frontMatterPages(page: Page): Promise<IBookPage[]> {
    const pages = await getPages(page);
    const firstContent = pages.findIndex((p) => p.isContentPage);
    return pages.slice(0, firstContent < 0 ? pages.length : firstContent);
}

/** What the credits page the Edit tab is showing says, and which page of its pack it is. */
interface ICreditsShown {
    /** The page's data-xmatter-page. */
    xmatterPage: string;
    /** Whether the page is one of the cover's sides (class "cover"). */
    isCover: boolean;
    /** The copyright line, e.g. "Copyright © 2026, Tester". */
    copyright: string;
}

async function readCreditsShown(page: Page): Promise<ICreditsShown> {
    return editablePageFrame(page)
        .locator(".bloom-page")
        .first()
        .evaluate((element) => ({
            xmatterPage: element.getAttribute("data-xmatter-page") ?? "",
            isCover: element.classList.contains("cover"),
            copyright: (
                (
                    element.querySelector(
                        '.licenseAndCopyrightBlock [data-derived="copyright"]',
                    ) as HTMLElement | null
                )?.innerText ?? ""
            ).trim(),
        }));
}

/**
 * Wait until the credits page shows a copyright line that names kHolder, and return it. With
 * `exactly`, the line must be that, year and all.
 */
async function expectCreditsNameHolder(
    page: Page,
    message: string,
    exactly?: string,
): Promise<ICreditsShown> {
    let last: ICreditsShown | undefined;
    const poll = expect.poll(
        async () => {
            last = await readCreditsShown(page).catch(() => undefined);
            return last?.copyright ?? "";
        },
        { timeout: 30000, message },
    );
    if (exactly !== undefined) await poll.toBe(exactly);
    else await poll.toMatch(new RegExp(`^Copyright © \\d{4},? ${kHolder}$`));
    return last!;
}

/**
 * The size of the picture in the file at `path` and a SHA-1 of its pixels, decoded by the
 * browser: equal for two files that show the same picture even when their metadata differs, as
 * the export trims it. `page` is used only to decode it.
 */
async function picturePixels(page: Page, path: string): Promise<string> {
    return page.evaluate(async (base64) => {
        const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
        const bitmap = await createImageBitmap(new Blob([bytes]));
        const canvas = new OffscreenCanvas(bitmap.width, bitmap.height);
        const context = canvas.getContext("2d")!;
        context.drawImage(bitmap, 0, 0);
        const data = context.getImageData(
            0,
            0,
            bitmap.width,
            bitmap.height,
        ).data;
        const digest = new Uint8Array(
            await crypto.subtle.digest("SHA-1", data),
        );
        const hash = Array.from(digest, (b) =>
            b.toString(16).padStart(2, "0"),
        ).join("");
        return `${bitmap.width}x${bitmap.height} ${hash}`;
    }, fs.readFileSync(path).toString("base64"));
}

/**
 * The dialogs and page captions this test finds are in English, and a freshly launched Bloom
 * takes its UI language from the operating system.
 */
async function useEnglishUi(bloomApp: IBloomApp): Promise<void> {
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
        await setUiStateViaApi(bloomApp, "en", false);
}

/** The text of the selected book's content pages and the pictures on them, from its saved file. */
async function readContentPages(
    page: Page,
    bookFolder: string,
): Promise<{ texts: string[]; pictures: string[] }[]> {
    const html = fs.readFileSync(bookHtmlPath(bookFolder), "utf8");
    return page.evaluate((source) => {
        const dom = new DOMParser().parseFromString(source, "text/html");
        return Array.from(
            dom.querySelectorAll(
                ".bloom-page:not(.bloom-frontMatter):not(.bloom-backMatter)",
            ),
        ).map((p) => ({
            texts: Array.from(
                p.querySelectorAll<HTMLElement>(
                    '.bloom-translationGroup .bloom-editable[lang="en"]',
                ),
            )
                .map((box) =>
                    (box.textContent ?? "").replace(/\s+/g, " ").trim(),
                )
                .filter((text) => text !== ""),
            pictures: Array.from(
                p.querySelectorAll<HTMLImageElement>(".bloom-canvas img"),
            )
                .map((img) => decodeURIComponent(img.getAttribute("src") ?? ""))
                .filter((src) => src !== "" && src !== "placeHolder.png"),
        }));
    }, html);
}

test("front matter from a Paper Saver book lands on a Traditional book's own pages through a spreadsheet [Test Case ID 637]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    await useEnglishUi(bloomApp);
    let page = bloomApp.page;

    const originalId = await step(
        "Make a Paper Saver book with a title, a copyright and a page of text and picture",
        async () => {
            // Bloom renames the folder after the title when it saves, so keep the id to find it.
            const bookId = readBookId(
                await makeBookFromTemplate(page, "Basic Book"),
            );
            await typeInGroup(page, ".bookTitle", "en", kTitle);
            await addPage(page, "Basic Text & Image");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await chooseImageFile(page, fixtureImage(kPicture));
            await typeInGroup(page, ".bloom-translationGroup", "en", kText);
            await goToCreditsPage(page);
            await openCopyrightDialogFromCreditsPage(page);
            await setCopyrightHolder(page, kHolder);
            return bookId;
        },
    );
    if (!originalId) throw new Error("The book was not made.");
    const originalFolder = () =>
        findBookFolderById(bloomApp.collectionDir, originalId);

    const originalCopyright = await step(
        "Check Paper Saver puts the credits on the inside of the front cover",
        async () => {
            const front = await frontMatterPages(page);
            const credits = await expectCreditsNameHolder(
                page,
                "the credits page should show the copyright just set",
            );
            expect(credits.xmatterPage).toBe("credits");
            expect(
                credits.isCover,
                "Paper Saver's credits page should be a side of the cover",
            ).toBe(true);
            expect(
                front.map((p) => p.caption).indexOf("Credits Page"),
                `the credits page should come straight after the front cover; front matter: ${front.map((p) => p.caption).join(", ")}`,
            ).toBe(1);
            // Leaving the Edit tab saves the book.
            await switchTab(page, "collection");
            expect(readXmatterPackOfBook(originalFolder())).toBe("Factory");
            return credits.copyright;
        },
    );
    if (!originalCopyright) throw new Error("The copyright was not read.");

    const exportFolder = testInfo.outputPath("spreadsheets");
    fs.mkdirSync(exportFolder, { recursive: true });
    const spreadsheet = await step("Export it to a spreadsheet", async () => {
        await switchTab(page, "collection");
        await selectBook(page, originalFolder());
        return exportSelectedBookToSpreadsheet(page, exportFolder);
    });
    if (!spreadsheet) throw new Error("The book was not exported.");
    const originalPages = await readContentPages(page, originalFolder());

    await step("Check Bloom reported no problem before the restart", () => {
        // A restart starts the record of problems afresh.
        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem while making and exporting the book",
        ).toEqual([]);
    });

    const restarted = await step(
        "Change the collection to the Traditional pack",
        async () =>
            restartWithCollectionSettings(bloomApp, {
                languages: kLanguages,
                xmatterPack: "Traditional",
                subscriptionCode: kEnterpriseSubscriptionCode,
            }),
    );
    if (!restarted) throw new Error("Bloom did not restart.");
    page = restarted;
    await useEnglishUi(bloomApp);
    // Changing the UI language can reopen the collection in a new page.
    page = bloomApp.page;

    const newBookId = await step("Make a new blank book", async () => {
        await switchTab(page, "collection");
        return readBookId(await makeBookFromTemplate(page, "Basic Book"));
    });
    if (!newBookId) throw new Error("No new book was made.");

    const progress = await step(
        "Import the spreadsheet into the new book",
        async () => {
            await switchTab(page, "collection");
            await selectBook(
                page,
                findBookFolderById(bloomApp.collectionDir, newBookId),
            );
            return importSpreadsheetIntoSelectedBook(page, spreadsheet);
        },
    );
    if (!progress) throw new Error("The spreadsheet was not imported.");

    await step("Check the import reported no problem", () => {
        expect(
            [...progress.errors, ...progress.warnings],
            `The import said:\n${progress.messages.join("\n")}`,
        ).toEqual([]);
        expect(
            bloomApp.takeNonFatalProblems(),
            "Bloom should report no problem during the round trip",
        ).toEqual([]);
    });

    await step(
        "Check the new book has the title, the page, and the credits on the back of the title page",
        async () => {
            const imported = findBookFolderById(
                bloomApp.collectionDir,
                newBookId,
            );
            await editBook(page, imported);
            expect(readXmatterPackOfBook(imported)).toBe("Traditional");

            const front = await frontMatterPages(page);
            const captions = front.map((p) => p.caption);
            expect(
                captions.indexOf("Credits Page"),
                `the credits page should come straight after the title page; front matter: ${captions.join(", ")}`,
            ).toBe(captions.indexOf("Title Page") + 1);
            expect(captions.indexOf("Title Page")).toBeGreaterThan(0);

            await goToCreditsPage(page);
            const credits = await expectCreditsNameHolder(
                page,
                "the new book's credits page should show the copyright from the spreadsheet",
                originalCopyright,
            );
            expect(credits.xmatterPage).toBe("credits");
            expect(
                credits.isCover,
                "Traditional's credits page should be an inside page, not a side of the cover",
            ).toBe(false);

            await goToPage(page, front[0].id);
            await expect(
                editablePageFrame(page).locator(
                    '.bookTitle .bloom-editable[lang="en"]',
                ),
                "the new book's cover should have the title from the spreadsheet",
            ).toHaveText(kTitle, { timeout: 30000, useInnerText: true });

            const contentPages = await readContentPages(page, imported);
            expect(contentPages.map((p) => p.texts)).toEqual([[kText]]);
            expect(
                contentPages[0].pictures,
                "the content page should have the picture",
            ).toHaveLength(1);
            const picture = Path.join(imported, contentPages[0].pictures[0]);
            expect(
                fs.existsSync(picture),
                "the picture's file should be in the new book",
            ).toBe(true);
            expect(
                await picturePixels(page, picture),
                "the new book's picture should show the original's picture",
            ).toBe(
                await picturePixels(
                    page,
                    Path.join(originalFolder(), originalPages[0].pictures[0]),
                ),
            );
        },
    );
});
