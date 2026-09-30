// A folio's table of contents page: the one page of the Folio template, which names the books the
// folio publishes. Its text box lists them one title per line, and a list too long for the page
// flows on into pages Bloom adds after it (flow text) and takes away again when the list gets
// shorter. A book deleted from the collection drops out of the list, and the bubble beside the
// list names it. In the PDF, every title gets the number of its book's first page, on whichever
// page of the list it falls.
//
// To keep this fast, the list's text is made very large, so that six short titles are more than
// the page holds.
//
// Folio needs a Pro subscription and, until flow text is ready, the flow-text experimental
// feature. The first test checks the subscription half of that gate: the Bloom starts with the
// feature on and the collection on the Basic tier, then gets Pro (enableFlowTextFeature). The
// feature half is not checked here, because experimental features are given to Bloom when it is
// launched (a worker-scoped option), so the other state would need a Bloom of its own.
//
// The tests are serial because they share the books and the folio the second one builds.

import * as fs from "node:fs";
import * as Path from "node:path";
import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import {
    editablePageFrame,
    getContentPages,
    getPages,
    getShownPageId,
    goToPage,
    selectFactoryTemplate,
    type IBookPage,
} from "../helpers/bookMaking";
import { selectBook } from "../helpers/collection";
import {
    chooseFolioBooks,
    makeFolio,
    makeFolioTestBook,
} from "../helpers/folio";
import {
    enableFlowTextFeature,
    getBookChains,
    kFlowTextFeatures,
    runPendingReflow,
    waitForReflowIdle,
} from "../helpers/flowText";
import {
    chooseFontSize,
    closeFormatDialog,
    getFormatDialogPlacement,
    openFormatDialog,
    showFormatDialogTab,
} from "../helpers/formatDialog";
import { readPdf } from "../helpers/pdf";
import { makePdfInPublishTab } from "../helpers/pdfPublish";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "folio-toc", languages: ["en"] },
    experimentalFeatures: kFlowTextFeatures,
});
test.describe.configure({ mode: "serial" });

// Short titles, one line each, so that the flow moves whole entries. At this size the page holds
// five of them, so the sixth goes on to a page Bloom adds.
const kTitles = ["Ant", "Bee", "Cat", "Dog", "Eel", "Fox"];
const kListFontSizePt = 60;
const kListBox = ".bloom-folio-toc-list .bloom-editable.bloom-content1";

let folioFolder = "";
let tocPageId = "";
// The folio's pages before it held any books.
let pagesBeforeBooks: IBookPage[] = [];

/** Every run of white space as one space, and the flow's zero-width overflow mark taken out. */
function normalize(text: string): string {
    return text.replace(/‌/g, "").replace(/\s+/g, " ").trim();
}

/**
 * The text a list of these titles shows, without white space: each title, then the "##" that
 * stands in for its page number until the PDF is made.
 */
function listText(titles: string[]): string {
    return titles
        .map((title) => `${title}##`)
        .join("")
        .replace(/\s/g, "");
}

/** The text of these boxes, one after the other, without white space. */
function boxesText(texts: string[]): string {
    return normalize(texts.join("")).replace(/\s/g, "");
}

/**
 * The text of the chain the list's box starts, box by box, and the ids of the pages those boxes
 * are on. Fails when the list is not in exactly one chain.
 */
async function getListChain(
    page: Page,
): Promise<{ texts: string[]; pageIds: string[] }> {
    const chains = (await getBookChains(page)).filter(
        (chain) => chain.groups[0]?.pageId === tocPageId,
    );
    expect(
        chains,
        "the list's box should start exactly one chain of boxes",
    ).toHaveLength(1);
    return {
        texts: chains[0].groups.map((g) => normalize(g.textByLang.en ?? "")),
        pageIds: chains[0].groups.map((g) => g.pageId),
    };
}

test("without Pro, the Folio template can be read about but no book is made from it", async ({
    page,
}) => {
    const makeBook = page.getByRole("button", {
        name: "Make a book using this source",
    });
    // A control that needs a higher tier is not disabled but made inert, with the subscription
    // badge beside it (RequiresSubscriptionAdjacentIconWrapper), so that is what is checked.
    const inertAround = makeBook.locator("xpath=ancestor::*[@inert]");
    const badge = page.getByTitle(
        'This feature requires a Bloom subscription tier of at least "Pro".',
    );
    await selectFactoryTemplate(page, "Folio");
    await expect(makeBook).toBeVisible({ timeout: 30000 });
    await expect(
        inertAround,
        "on the Basic tier the button cannot be clicked",
    ).toHaveCount(1, { timeout: 30000 });
    await expect(
        badge,
        "the subscription badge shows beside the button and on the template's thumbnail",
    ).toHaveCount(2);

    await enableFlowTextFeature(page);
    await selectFactoryTemplate(page, "Basic Book");
    await selectFactoryTemplate(page, "Folio");
    await expect(
        inertAround,
        "on the Pro tier the button can be clicked",
    ).toHaveCount(0, { timeout: 30000 });
    await expect(makeBook).toBeEnabled();
});

test("a list too long for the page flows on into a page Bloom adds after it", async ({
    page,
}) => {
    test.setTimeout(900000);
    for (const title of kTitles)
        await makeFolioTestBook(page, {
            title,
            pageTexts: [`${title}, page one.`],
        });
    folioFolder = await makeFolio(page, "Folio Under Test");
    await selectBook(page, folioFolder);
    await expect(
        page.locator('.book-button img[src*="bloom-enterprise-badge"]'),
        "the subscription badge is on the Folio template's thumbnail, not on the folio made from it",
    ).toHaveCount(1, { timeout: 30000 });
    await switchTab(page, "edit");
    const contentPages = await getContentPages(page);
    expect(
        contentPages,
        "a new folio has one content page, its table of contents",
    ).toHaveLength(1);
    tocPageId = contentPages[0].id;
    pagesBeforeBooks = await getPages(page);

    await goToPage(page, tocPageId);
    const frame = editablePageFrame(page);
    await expect(
        frame.getByText("Page numbers will be added when you make the PDF."),
    ).toBeVisible({ timeout: 30000 });

    // Make the list's text so large that six titles do not fit, before there are any.
    const listBox = frame.locator(kListBox);
    await listBox.click();
    // The Format dialog formats the box that has the focus, and the page opens with it elsewhere.
    await expect(
        listBox,
        "clicking the list's box should focus it",
    ).toBeFocused({
        timeout: 15000,
    });
    // There is one format gear, which Bloom moves to the box with the focus; the page opened with
    // the heading focused, so wait for the gear to reach the list's box before clicking it.
    await expect
        .poll(
            async () => {
                const gear = (await getFormatDialogPlacement(page)).gear;
                const box = await listBox.evaluate((e) => {
                    const r = e.getBoundingClientRect();
                    return { top: r.top, bottom: r.bottom };
                });
                // The gear sits at the bottom left of its box; the heading's is just above the
                // list's box, so "near the list's box" is not close enough.
                return (
                    !!gear &&
                    gear.top > (box.top + box.bottom) / 2 &&
                    gear.bottom <= box.bottom + 40
                );
            },
            {
                timeout: 15000,
                message: "The format gear never moved to the list's box.",
            },
        )
        .toBe(true);
    await openFormatDialog(page);
    await showFormatDialogTab(page, "characters");
    await chooseFontSize(page, kListFontSizePt);
    await closeFormatDialog(page);
    expect(
        (await getPages(page)).length,
        "an empty list needs no more pages",
    ).toBe(pagesBeforeBooks.length);

    await chooseFolioBooks(page, tocPageId, kTitles, { stay: true });
    expect(
        await getShownPageId(page),
        "Bloom stays on the table of contents page while it adds pages for the list",
    ).toBe(tocPageId);

    const pages = await getPages(page);
    const tocIndex = pages.findIndex((p) => p.id === tocPageId);
    const added = pages.filter(
        (p) => !pagesBeforeBooks.some((before) => before.id === p.id),
    );
    expect(added, "one page is added for the rest of the list").toHaveLength(1);
    expect(pages[tocIndex + 1].id, "the added page comes right after").toBe(
        added[0].id,
    );

    const chain = await getListChain(page);
    expect(chain.pageIds).toEqual([tocPageId, added[0].id]);
    expect(
        chain.texts.every((text) => text.length > 0),
        `both boxes hold some of the list: ${JSON.stringify(chain.texts)}`,
    ).toBe(true);
    expect(
        boxesText(chain.texts),
        "every title is there once, in order, across the two boxes",
    ).toBe(listText(kTitles));

    // Bloom writes the table of contents; typing in it changes nothing.
    const box = editablePageFrame(page).locator(kListBox);
    const before = await box.innerHTML();
    await box.click();
    await page.keyboard.type("xyz");
    await page.keyboard.press("Enter");
    await page.keyboard.press("Backspace");
    expect(
        await box.innerHTML(),
        "the table of contents cannot be typed in",
    ).toBe(before);

    // The added page's box shows the list in the table of contents' own style, and is locked too.
    await goToPage(page, added[0].id);
    const addedBox = editablePageFrame(page).locator(
        ".bloom-translationGroup > .bloom-editable.bloom-content1",
    );
    await expect(addedBox).toHaveClass(/\bTableOfContents-style\b/, {
        timeout: 30000,
    });
    await expect(addedBox).toHaveClass(/\bbloom-locked\b/);

    await goToPage(page, pages[0].id);
});

test("in the PDF, every title on both pages of the list gets its page number", async ({
    bloomApp,
}) => {
    const page = bloomApp.page;
    await switchTab(page, "collection");
    await selectBook(page, folioFolder);
    const pages = readPdf(await makePdfInPublishTab(page, "Simple"));
    const toc = pages.findIndex((p) =>
        p.compactText.includes("TableofContents"),
    );
    expect(
        toc,
        "the PDF has the table of contents page",
    ).toBeGreaterThanOrEqual(0);
    const listPages = [pages[toc].compactText, pages[toc + 1].compactText];
    for (const title of kTitles) {
        const entry = new RegExp(`${title.replace(/\s/g, "")}\\d+`);
        expect(
            listPages.some((text) => entry.test(text)),
            `"${title}" and its page number, on the table of contents: ${JSON.stringify(listPages)}`,
        ).toBe(true);
    }
    expect(
        kTitles.some((title) =>
            new RegExp(`${title.replace(/\s/g, "")}\\d+`).test(listPages[1]),
        ),
        "the page after the table of contents carries the rest of the list",
    ).toBe(true);
});

test("when the list gets short enough for the page, the added page goes", async ({
    bloomApp,
}) => {
    const page = bloomApp.page;
    await switchTab(page, "edit");
    await chooseFolioBooks(page, tocPageId, [], {
        remove: [kTitles[1], kTitles[2]],
    });
    // Leaving the page started the refit; wait for it, or run it if it is still waiting.
    await runPendingReflow(page);
    await waitForReflowIdle(page);
    await expect
        .poll(async () => (await getPages(page)).map((p) => p.id), {
            timeout: 60000,
            message: "the folio is back to the pages it started with",
        })
        .toEqual(pagesBeforeBooks.map((p) => p.id));
    await goToPage(page, tocPageId);
    expect(
        boxesText([
            await editablePageFrame(page).locator(kListBox).innerText(),
        ]),
    ).toBe(listText([kTitles[0], ...kTitles.slice(3)]));
    await goToPage(page, (await getPages(page))[0].id);
});

test("a book deleted from the collection drops out of the list, and the bubble names it", async ({
    bloomApp,
}) => {
    const deleted = kTitles[5];
    // Deleting through the Collections tab asks for confirmation in a WinForms dialog, which a
    // test cannot answer, so the book goes while Bloom is stopped.
    await bloomApp.restart(() =>
        fs.rmSync(Path.join(bloomApp.collectionDir, deleted), {
            recursive: true,
        }),
    );
    const page = bloomApp.page;
    await enableFlowTextFeature(page);
    folioFolder = Path.join(bloomApp.collectionDir, Path.basename(folioFolder));
    await selectBook(page, folioFolder);
    await switchTab(page, "edit");
    await goToPage(page, tocPageId);
    await expect(
        editablePageFrame(page).getByText(
            `"${deleted}" is no longer in this collection.`,
        ),
    ).toBeVisible({ timeout: 30000 });
    // "Reflow now", as a person might click, must not bring the book's old list back.
    await runPendingReflow(page);
    // The list fits the page now, so it is all in the box being edited; the saved book is behind.
    const box = editablePageFrame(page).locator(kListBox);
    await expect
        .poll(async () => boxesText([await box.innerText()]), {
            timeout: 30000,
            message: `"${deleted}" is gone from the list`,
        })
        .toBe(listText([kTitles[0], kTitles[3], kTitles[4]]));
    await goToPage(page, (await getPages(page))[0].id);
});
