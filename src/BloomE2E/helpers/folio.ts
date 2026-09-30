// Make the books a folio test needs, and say which books a folio holds.
//
// A folio (the Folio template) publishes other books of its collection as one PDF. The books it
// holds, and their order, are named by the `data-folio-book-ids` of the `.bloom-folio-toc-list` on
// each of its table of contents pages, as bookInstanceIds separated by spaces.
//
// The books are made through the same helpers every test uses, so building them is journey
// coverage of making books too. `scripts/make-folio-test-collection.script.ts` uses this to build a
// collection on disk for checking a folio by hand in a live Bloom; the folio specs build what they
// need at run time.

import * as fs from "node:fs";
import * as os from "node:os";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, type Page } from "@playwright/test";
import {
    addPage,
    editablePageFrame,
    findBookFolder,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "./bookMaking";
import { bookHtmlPath } from "./bookHtml";
import { setCopyrightHolder } from "./copyrightAndLicense";
import {
    chooseFontSize,
    closeFormatDialog,
    openFormatDialog,
    showFormatDialogTab,
} from "./formatDialog";
import { enableFlowTextFeature, waitForReflowIdle } from "./flowText";
import { chooseImageFile } from "./images";
import { clickToFixMissingItem, openPublishToWeb } from "./libraryPublish";
import { setPageSize } from "./pageSize";
import { switchTab } from "./workspace";

const fixturesFolder = Path.resolve(
    Path.dirname(fileURLToPath(import.meta.url)),
    "..",
    "fixtures",
);

/** What makes a book of the folio test collection worth having. */
export interface IFolioTestBook {
    title: string;
    /** The text of each content page, one "Just Text" page per entry unless `withImage`. */
    pageTexts: string[];
    /** Put an image, whose file name has a space and a non-ASCII letter, on a text-and-image page. */
    withImage?: boolean;
    /** Give the book this copyright holder, so its credits page has something to show. */
    copyrightHolder?: string;
    /** Choose this font size in the Format dialog for the book's text, a book-level style. */
    fontSizePt?: number;
    /** The book's page size and orientation, e.g. "A4Portrait"; left out, the template's A5Portrait. */
    layoutId?: string;
}

/**
 * The books of the folio test collection. "Three Pages" has an odd number of pages, so the book
 * after it needs a blank page to start on a right-hand page. "Also A4" is another page size, which
 * no folio may hold.
 */
export const kFolioTestBooks: IFolioTestBook[] = [
    {
        title: "Three Pages",
        pageTexts: [
            "Page one of three.",
            "Page two of three.",
            "Page three of three.",
        ],
    },
    {
        title: "Big Print",
        pageTexts: ["This text is in 28 point type.", "So is this."],
        fontSizePt: 28,
    },
    {
        title: "Crédits Ñandú",
        pageTexts: ["A bird, with credits."],
        withImage: true,
        copyrightHolder: "Folio Test Holder",
    },
    {
        title: "Also A4",
        pageTexts: ["This book is A4."],
        layoutId: "A4Portrait",
    },
];

/**
 * Make one book of the folio test collection, leave it saved, and return its folder.
 */
export async function makeFolioTestBook(
    page: Page,
    book: IFolioTestBook,
): Promise<string> {
    await makeBookFromTemplate(page, "Basic Book"); // lands on the cover in the Edit tab
    await typeInGroup(page, ".bookTitle", "en", book.title);
    if (book.layoutId) await setPageSize(page, book.layoutId);
    for (const text of book.pageTexts) {
        await addPage(
            page,
            book.withImage ? "Basic Text & Image" : "Just Text",
            1,
            "Basic Book",
        );
        const contentPages = await getContentPages(page);
        await goToPage(page, contentPages[contentPages.length - 1].id);
        await typeInGroup(page, ".bloom-translationGroup", "en", text);
        if (book.fontSizePt) {
            await openFormatDialog(page);
            await showFormatDialogTab(page, "characters");
            await chooseFontSize(page, book.fontSizePt);
            await closeFormatDialog(page);
        }
        if (book.withImage) await chooseImageFile(page, imageWithAwkwardName());
    }
    // Leave the last page (back to the cover) so Bloom saves what was typed.
    await goToPage(page, (await getPages(page))[0].id);
    if (book.copyrightHolder) {
        // The real dialog, reached as a person reaches it from Publish: Web's "Click to fix".
        await openPublishToWeb(page);
        await clickToFixMissingItem(page, "Copyright");
        await setCopyrightHolder(page, book.copyrightHolder);
    }
    await switchTab(page, "collection");
    return findBookFolder(page, book.title);
}

/**
 * Make a folio from the Folio template with this title, leave it saved, and return its folder.
 * It holds no books until setFolioBooks gives it some. First gives the collection the Pro tier,
 * without which Bloom makes no folio; that lasts until Bloom restarts, so a test that restarts
 * Bloom and then makes a folio's PDF calls enableFlowTextFeature again. The Bloom must have been
 * launched with the flow-text experimental feature (kFlowTextFeatures).
 */
export async function makeFolio(page: Page, title: string): Promise<string> {
    await enableFlowTextFeature(page);
    await makeBookFromTemplate(page, "Folio");
    await typeInGroup(page, ".bookTitle", "en", title);
    await goToPage(page, (await getPages(page))[1].id);
    await switchTab(page, "collection");
    return findBookFolder(page, title);
}

/**
 * In the Edit tab, on the folio's table of contents page (the page with this id), click Choose
 * Books… in the bubble beside the text box that lists the books, and in the dialog that opens
 * remove the books titled in `options.remove` (default none), then add the books titled in
 * `titles`, in this order, then OK. Returns the titles the dialog offered before any were added.
 *
 * Choosing rewrites the text box as one line per book, and a list too long for the page flows on
 * into pages Bloom adds, so this waits for the flow to settle. Then it leaves the page so that
 * Bloom saves it, unless `options.stay`, for a test that looks at where Bloom left it.
 */
export async function chooseFolioBooks(
    page: Page,
    tocPageId: string,
    titles: string[],
    options: { remove?: string[]; stay?: boolean } = {},
): Promise<string[]> {
    await goToPage(page, tocPageId);
    await editablePageFrame(page)
        .locator("button.bloom-folio-toc-choose-books")
        .click();
    const dialog = page.locator(".MuiDialog-root", {
        hasText: "Choose Books for Folio",
    });
    await expect(dialog).toBeVisible({ timeout: 30000 });
    const sources = dialog.locator('[data-testid^="source-book-"]');
    await expect(sources.first()).toBeVisible({ timeout: 30000 });
    const offered = (await sources.allInnerTexts()).map((t) => t.trim());
    for (const title of options.remove ?? []) {
        const chosen = dialog
            .locator('[data-testid^="target-book-"]')
            .filter({ hasText: title })
            .first();
        await chosen.hover();
        await chosen.getByTestId("remove-book-button").click();
        await expect(
            dialog
                .locator('[data-testid^="target-book-"]')
                .filter({ hasText: title }),
        ).toHaveCount(0);
    }
    for (const title of titles) {
        await sources.filter({ hasText: title }).first().click();
        await dialog.getByRole("button", { name: /Add Book/ }).click();
    }
    await dialog.getByRole("button", { name: "OK", exact: true }).click();
    await expect(dialog).toHaveCount(0, { timeout: 30000 });
    await waitForReflowIdle(page);
    // Leave the page so Bloom saves it.
    if (!options.stay) await goToPage(page, (await getPages(page))[0].id);
    return offered;
}

/** The folio options Book Settings shows in its Folio section, by the values they save. */
export interface IFolioOptions {
    xmatter?: "eachBook" | "folioOnly";
    pageNumbers?: "continuous" | "eachBook";
    addBlankPages?: boolean;
    showTableOfContents?: boolean;
}

/**
 * Open Book and Page Settings at its Folio section by `open` (by default, the Edit tab's top-bar
 * button and then the section's name), set these options, and click OK. The controls are found
 * by the settings path each saves to (publish.folio.*), which Config-R puts on them as their name.
 */
export async function setFolioOptionsInBookSettings(
    page: Page,
    options: IFolioOptions,
    open?: () => Promise<void>,
): Promise<void> {
    const dialog = page.locator(".MuiDialog-root", {
        hasText: "Book and Page Settings",
    });
    if (open) await open();
    else {
        await page
            .getByRole("button", { name: /Book and Page Settings/ })
            .click();
        await expect(dialog).toBeVisible({ timeout: 30000 });
        await dialog.getByText("Folio", { exact: true }).first().click();
    }
    await expect(dialog).toBeVisible({ timeout: 30000 });
    await expect(
        dialog.locator('[id="mui-component-select-publish.folio.xmatter"]'),
        "the dialog should be showing its Folio section",
    ).toBeVisible({ timeout: 30000 });
    for (const key of ["xmatter", "pageNumbers"] as const) {
        const value = options[key];
        if (value === undefined) continue;
        await dialog
            .locator(`[id="mui-component-select-publish.folio.${key}"]`)
            .click();
        await page
            .locator(`[role="listbox"] [role="option"][data-value="${value}"]`)
            .click();
        await expect(
            dialog.locator(`input[name="publish.folio.${key}"]`),
        ).toHaveValue(value);
    }
    for (const key of ["addBlankPages", "showTableOfContents"] as const) {
        const value = options[key];
        if (value === undefined) continue;
        const box = dialog.locator(`input[name="publish.folio.${key}"]`);
        if ((await box.isChecked()) !== value) await box.click();
        await expect(box).toBeChecked({ checked: value });
    }
    await dialog.getByRole("button", { name: "OK", exact: true }).click();
    await expect(dialog).toHaveCount(0, { timeout: 30000 });
}

/** The folio options saved in a book folder's publish-settings.json. */
export function readFolioOptions(bookFolder: string): IFolioOptions {
    const settings = JSON.parse(
        fs.readFileSync(Path.join(bookFolder, "publish-settings.json"), "utf8"),
    ) as { folio: IFolioOptions };
    return settings.folio;
}

/** The bookInstanceId of the book in this folder, from its meta.json. */
export function readBookId(bookFolder: string): string {
    const meta = JSON.parse(
        fs.readFileSync(Path.join(bookFolder, "meta.json"), "utf8"),
    ) as { bookInstanceId: string };
    return meta.bookInstanceId;
}

/**
 * Make the folio's first table of contents page list exactly these books, in this order, by
 * rewriting the folio's .htm: the list's data-folio-book-ids. Only while no Bloom has the
 * collection open (see bloomApp.restart): Bloom keeps the book in memory and would write over
 * the change. The list's text is left alone; making the PDF rewrites it.
 */
export function setFolioBooks(folioFolder: string, bookIds: string[]): void {
    const htmPath = bookHtmlPath(folioFolder);
    const html = fs.readFileSync(htmPath, "utf8");
    const listPattern =
        /(class="[^"]*bloom-folio-toc-list[^"]*"[^>]*?data-folio-book-ids=")[^"]*(")/;
    if (!listPattern.test(html))
        throw new Error(
            `${htmPath} has no table of contents list (.bloom-folio-toc-list), so it is not a folio.`,
        );
    fs.writeFileSync(
        htmPath,
        html.replace(listPattern, `$1${bookIds.join(" ")}$2`),
    );
}

/** The books a folio's table of contents lists name, in order, from its saved .htm. */
export function readFolioBookIds(folioFolder: string): string[] {
    const html = fs.readFileSync(bookHtmlPath(folioFolder), "utf8");
    return [...html.matchAll(/data-folio-book-ids="([^"]*)"/g)].flatMap((m) =>
        m[1].split(" ").filter((id) => id),
    );
}

/**
 * A copy of the test image under a name with a space and a non-ASCII letter, which is what
 * breaks an image path that is built or escaped carelessly.
 */
function imageWithAwkwardName(): string {
    const folder = fs.mkdtempSync(Path.join(os.tmpdir(), "folio-image-"));
    const path = Path.join(folder, "a bird ñ.png");
    fs.copyFileSync(Path.join(fixturesFolder, "images", "bird.png"), path);
    return path;
}
