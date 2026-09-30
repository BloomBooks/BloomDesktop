// A folio (the Folio template) publishes other books of its collection as one PDF. By default the
// PDF is each book's own PDF glued together, after the folio's own front pages and before its
// back matter: every book keeps its own styles and credits, the page numbers run on through the
// whole folio, and a blank page is added wherever a book would otherwise start on the other side
// from the one it starts on when printed alone.
//
// The folio under test gets its books the way a person gives them: click Choose Books… beside the
// list on its table of contents page and choose them in the dialog, which offers no folios. A folio
// has exactly one table of contents page, so Add Page does not offer another. Folio needs a Pro
// subscription and, until flow text is ready, the flow-text experimental feature: the Bloom is
// launched with the feature, and its collection given the Pro tier after each launch
// (enableFlowTextFeature), since there is no real Pro subscription code for tests.
// The second folio, which holds an A4 book, is setup for the refusal test, so it gets its books by
// the fast route instead: written into its page while Bloom is stopped (setFolioBooks). The PDF
// is read with the Ghostscript Bloom ships (helpers/pdf.ts).
//
// The tests are serial because they share the books and folios the first one builds.

import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import {
    closeAddPageDialog,
    getAddPageDialogGroups,
    openAddPageDialog,
} from "../helpers/addPageDialog";
import {
    editablePageFrame,
    getContentPages,
    goToPage,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import { enableFlowTextFeature, kFlowTextFeatures } from "../helpers/flowText";
import {
    chooseFolioBooks,
    makeFolio,
    makeFolioTestBook,
    readBookId,
    readFolioOptions,
    setFolioBooks,
    setFolioOptionsInBookSettings,
    type IFolioTestBook,
} from "../helpers/folio";
import { countPixels, readPdf, type IPdfPage } from "../helpers/pdf";
import { makePdfInPublishTab } from "../helpers/pdfPublish";
import { selectPublishDestination } from "../helpers/publish";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: {
        name: "folio-pdf",
        languages: ["en"],
    },
    experimentalFeatures: kFlowTextFeatures,
});
test.describe.configure({ mode: "serial" });

// Two content pages make a Basic Book of seven pages, an odd number, so the book after it needs a
// blank page to start on a right-hand page as it does when printed alone.
const oddBook: IFolioTestBook = {
    title: "Odd Book",
    pageTexts: ["Odd book page one.", "Odd book page two."],
};
const styledBook: IFolioTestBook = {
    title: "Styled Book",
    pageTexts: ["Big text."],
    fontSizePt: 28,
    withImage: true,
    copyrightHolder: "Folio Test Holder",
};
const a4Book: IFolioTestBook = {
    title: "A4 Book",
    pageTexts: ["An A4 page."],
    layoutId: "A4Portrait",
};

let folioFolder = "";
let folioWithA4BookFolder = "";

// Page sizes in points. The PDF maker rounds A5 (148 × 210 mm) to whole points.
const A5 = { widthPt: 420, heightPt: 595 };
// A booklet of A5 pages is printed two to a side on A4 landscape.
const A4Landscape = { widthPt: 842, heightPt: 595 };

/** Fail unless the page is this size, to within a point. */
function expectSize(
    page: IPdfPage,
    size: { widthPt: number; heightPt: number },
    what: string,
) {
    expect(
        Math.abs(page.widthPt - size.widthPt),
        `${what}: width ${page.widthPt}`,
    ).toBeLessThan(1);
    expect(
        Math.abs(page.heightPt - size.heightPt),
        `${what}: height ${page.heightPt}`,
    ).toBeLessThan(1);
}

/**
 * The index of the first page, at or after `from`, whose text includes `text`, ignoring white
 * space (see IPdfPage.compactText).
 */
function indexOfPageWith(pages: IPdfPage[], text: string, from = 0): number {
    const compact = text.replace(/\s/g, "");
    const index = pages.findIndex(
        (p, i) => i >= from && p.compactText.includes(compact),
    );
    expect(
        index,
        `No page from ${from + 1} on has "${text}".`,
    ).toBeGreaterThanOrEqual(0);
    return index;
}

/** The number printed at the foot of a content page, which is the page's last run of text. */
function printedNumber(page: IPdfPage): number {
    return Number(page.texts[page.texts.length - 1].text);
}

test("make the books, and choose a folio's books on its table of contents page", async ({
    page,
    bloomApp,
}) => {
    test.setTimeout(900000);
    const oddFolder = await makeFolioTestBook(page, oddBook);
    await makeFolioTestBook(page, styledBook);
    const a4Folder = await makeFolioTestBook(page, a4Book);
    folioWithA4BookFolder = await makeFolio(page, "Folio With A4 Book");
    await bloomApp.restart(() =>
        setFolioBooks(folioWithA4BookFolder, [
            readBookId(oddFolder),
            readBookId(a4Folder),
        ]),
    );
    page = bloomApp.page;
    await enableFlowTextFeature(page);
    folioWithA4BookFolder = Path.join(
        bloomApp.collectionDir,
        Path.basename(folioWithA4BookFolder),
    );

    folioFolder = await makeFolio(page, "Folio Under Test");
    await selectBook(page, folioFolder);
    await switchTab(page, "edit");
    const [toc] = await getContentPages(page);
    const offered = await chooseFolioBooks(page, toc.id, [
        oddBook.title,
        styledBook.title,
    ]);
    expect(offered).toEqual(
        expect.arrayContaining([oddBook.title, styledBook.title, a4Book.title]),
    );
    expect(offered, "a folio cannot hold a folio, nor itself").not.toContain(
        "Folio With A4 Book",
    );
    expect(offered).not.toContain("Folio Under Test");

    // A folio has one table of contents page; a long list flows on into pages Bloom adds.
    await openAddPageDialog(page);
    const groups = await getAddPageDialogGroups(page);
    expect(
        groups.flatMap((g) => g.pages.map((p) => p.label)),
        "Add Page does not offer a second table of contents page",
    ).not.toContain("Table of Contents");
    await closeAddPageDialog(page);
    await switchTab(page, "collection");
});

test("a folio's PDF is its books glued together, on their own sides, numbered through", async ({
    bloomApp,
}) => {
    const page = bloomApp.page;
    await selectBook(page, folioFolder);
    const pdfPath = await makePdfInPublishTab(page, "Simple");
    const pages = readPdf(pdfPath);

    for (const [i, p] of pages.entries()) expectSize(p, A5, `page ${i + 1}`);

    // Each book starts with its own cover, whose first text is its title. The table of contents
    // page names the books too, so look after it.
    const oddCover = indexOfPageWith(
        pages,
        "Odd Book",
        indexOfPageWith(pages, "Styled Book") + 1,
    );
    const styledCover = indexOfPageWith(pages, "Styled Book", oddCover + 1);
    expect(oddCover % 2, "Odd Book's cover should be a right-hand page").toBe(
        0,
    );
    expect(
        styledCover % 2,
        "Styled Book's cover should be a right-hand page",
    ).toBe(0);
    expect(
        pages[styledCover - 1].text,
        "the page before Styled Book should be the blank that puts its cover on the right",
    ).toBe("");

    // Numbers run on from one book into the next instead of restarting.
    const oddFirst = indexOfPageWith(pages, "Odd book page one.");
    const oddSecond = indexOfPageWith(pages, "Odd book page two.");
    const styledFirst = indexOfPageWith(pages, "Big text.");
    expect(printedNumber(pages[oddSecond])).toBe(
        printedNumber(pages[oddFirst]) + 1,
    );
    expect(printedNumber(pages[styledFirst])).toBeGreaterThan(
        printedNumber(pages[oddSecond]),
    );

    // Styled Book keeps its own Format-dialog size, and its picture and its credits.
    const largestSize = (p: IPdfPage) =>
        Math.max(...p.texts.map((t) => t.fontSize));
    expect(
        largestSize(pages[styledFirst]),
        "Styled Book's 28 point text",
    ).toBeCloseTo(28, 0);
    expect(
        largestSize(pages[oddFirst]),
        "Odd Book's text, in the default size",
    ).toBeLessThan(20);
    const yellow = (r: number, g: number, b: number) =>
        r > 180 && g > 160 && b < 90;
    expect(
        countPixels(pdfPath, styledFirst + 1, yellow),
        "Styled Book's picture (a yellow bird, in a file named with a space and an ñ) should show",
    ).toBeGreaterThan(500);
    expect(countPixels(pdfPath, oddFirst + 1, yellow)).toBe(0);
    indexOfPageWith(pages, "Folio Test Holder", styledCover);

    // The table of contents lists the books with the first number printed in each.
    const toc = indexOfPageWith(pages, "Odd Book");
    expect(toc, "the table of contents comes before the books").toBeLessThan(
        oddCover,
    );
    expect(pages[toc].compactText).toContain(
        `OddBook${printedNumber(pages[oddFirst])}`,
    );
    expect(pages[toc].compactText).toContain(
        `StyledBook${printedNumber(pages[styledFirst])}`,
    );
});

test("a folio's booklet insides are imposed from the whole folio", async ({
    bloomApp,
}) => {
    const page = bloomApp.page;
    await selectBook(page, folioFolder);
    const pages = readPdf(await makePdfInPublishTab(page, "Booklet Insides"));
    expect(pages.length % 2, "a booklet has two sides to every sheet").toBe(0);
    for (const [i, p] of pages.entries())
        expectSize(p, A4Landscape, `side ${i + 1}`);
    // The books' own covers are inside pages of the folio, so they are in the booklet.
    expect(pages.some((p) => p.compactText.includes("OddBook"))).toBe(true);
    expect(pages.some((p) => p.compactText.includes("StyledBook"))).toBe(true);
});

/** Wait for Bloom to show a message containing `text` in any of its windows, and return it. */
async function waitForMessage(page: Page, text: string): Promise<string> {
    const browser = page.context().browser()!;
    let found = "";
    await expect
        .poll(
            async () => {
                for (const p of browser.contexts().flatMap((c) => c.pages())) {
                    const body = await p
                        .evaluate(() => document.body?.innerText ?? "")
                        .catch(() => "");
                    if (body.includes(text)) found = body;
                }
                return found !== "";
            },
            {
                timeout: 120000,
                message: `Bloom never showed a message with "${text}".`,
            },
        )
        .toBe(true);
    return found;
}

test("a folio holding a book of another page size is refused, naming the book", async ({
    bloomApp,
}) => {
    const page = bloomApp.page;
    await switchTab(page, "collection");
    await selectBook(page, folioWithA4BookFolder);
    await makePdfInPublishTab(page, "Simple", 5000).catch(() => {
        // Expected: no PDF comes; the message below says why.
    });
    const message = await waitForMessage(
        page,
        "cannot make a PDF of this folio",
    );
    expect(message).toContain(
        '"A4 Book" is A4Portrait, but this folio is A5Portrait.',
    );
    await page.evaluate(() =>
        fetch("/bloom/api/common/closeReactDialog", { method: "POST" }),
    );
});

test("Book Settings can make a folio's books chapters and leave out its table of contents", async ({
    bloomApp,
}) => {
    const page = bloomApp.page;
    // Where Odd Book's first content page falls in its own PDF, to compare sides with below.
    await switchTab(page, "collection");
    await selectBook(page, Path.join(bloomApp.collectionDir, oddBook.title));
    const alone = readPdf(await makePdfInPublishTab(page, "Simple"));
    const aloneIndex = indexOfPageWith(alone, "Odd book page one.");

    await switchTab(page, "collection");
    await selectBook(page, folioFolder);
    // The table of contents bubble's Folio Settings… opens the same section; look, and cancel.
    await switchTab(page, "edit");
    const [firstToc] = await getContentPages(page);
    await goToPage(page, firstToc.id);
    await editablePageFrame(page)
        .locator("button.bloom-folio-toc-settings")
        .click();
    const settingsDialog = page.locator(".MuiDialog-root", {
        hasText: "Book and Page Settings",
    });
    await expect(
        settingsDialog.locator(
            '[id="mui-component-select-publish.folio.xmatter"]',
        ),
    ).toBeVisible({ timeout: 30000 });
    await settingsDialog
        .getByRole("button", { name: "Cancel", exact: true })
        .click();
    await expect(settingsDialog).toHaveCount(0, { timeout: 30000 });

    // Change the options from the PDF & Print screen, which must then drop the PDF it showed,
    // since that PDF was made with the old ones.
    await makePdfInPublishTab(page, "Simple");
    await expect(page.locator('iframe[src*=".pdf"]')).toHaveCount(1);
    await setFolioOptionsInBookSettings(
        page,
        { xmatter: "folioOnly", showTableOfContents: false },
        () => page.locator("#folio-settings").click(),
    );
    await expect(
        page.locator('iframe[src*=".pdf"]'),
        "the out-of-date PDF is gone",
    ).toHaveCount(0, {
        timeout: 30000,
    });
    await expect
        .poll(() => readFolioOptions(folioFolder), { timeout: 30000 })
        .toMatchObject({
            xmatter: "folioOnly",
            showTableOfContents: false,
            pageNumbers: "continuous",
            addBlankPages: true,
        });

    const pages = readPdf(await makePdfInPublishTab(page, "Simple"));
    const all = pages.map((p) => p.compactText).join("|");
    expect(
        all,
        "the books' own covers and title pages are left out",
    ).not.toContain("OddBook");
    expect(all).not.toContain("StyledBook");
    expect(all, "so are their credits pages").not.toContain("FolioTestHolder");
    const oddFirst = indexOfPageWith(pages, "Odd book page one.");
    indexOfPageWith(pages, "Big text.", oddFirst);
    expect(
        oddFirst % 2,
        "Odd Book's first page is on the side it has in Odd Book's own PDF",
    ).toBe(aloneIndex % 2);
});

test("the other publish screens say a folio publishes only as a PDF", async ({
    bloomApp,
}) => {
    const page = bloomApp.page;
    await switchTab(page, "collection");
    await selectBook(page, folioFolder);
    for (const destination of [
        "Web",
        "BloomPUB",
        "ePUB",
        "Audio or Video",
    ] as const) {
        await selectPublishDestination(page, destination);
        await expect(
            page.getByText("A folio can be published only as a PDF", {
                exact: false,
            }),
            `the ${destination} screen`,
        ).toBeVisible({ timeout: 30000 });
    }
});
