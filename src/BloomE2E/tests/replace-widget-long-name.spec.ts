// Replacing the widget on a widget page with one whose name is more than 50 characters long, in a
// book whose title is more than 50 characters long, works: the page shows the new widget.
//
// Test Case ID 537 ("Replace Widget Long Name"), from BL-15477. Bloom shortens a widget's name to
// 50 characters for the folder it unpacks the widget into, under the book's activities folder
// (WidgetHelper.GetShortWidgetName, BL-15307). Choosing a widget whose shortened name is already
// taken unpacks it beside the old one, with a number added, and compares the two folders; in 6.2
// that comparison looked for the old folder under the long name and failed, so the replacement
// never happened. That needs the new widget to have different content from the old one, as these
// do. The bug was in choosing a .wdgt file; a widget chosen by its .html file gets its name
// shortened before it is packed, so the test replaces it both ways.

import * as fs from "node:fs";
import * as Path from "node:path";
import type { Page } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import { bookHtmlPath } from "../helpers/bookHtml";
import {
    addPage,
    editablePageFrame,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
} from "../helpers/bookMaking";
import { findBookFolderById, readBookId } from "../helpers/collection";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import {
    chooseWidgetOnPage,
    getWidgetSrcOnPage,
    getWidgetText,
    replaceWidgetOnPage,
    writeWidgetFile,
    writeWidgetFolder,
} from "../helpers/widgets";

test.use({
    collectionSpec: {
        name: "replace-widget-long-name",
        languages: ["en"],
        // HTML5 widgets need a subscription that includes them.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

const kBookTitle =
    "TC-537 A book whose title runs to well over fifty characters";
/** Every widget the test chooses has this name, so that each takes the shortened name's folder. */
const kWidgetName =
    "TC-537 A widget whose name runs to well over fifty characters";
/** The folder that Bloom unpacks a widget named kWidgetName into: its first 50 characters. */
const kShortWidgetName = kWidgetName.slice(0, 50).trim();

const kFirstText = "TC-537 the widget first chosen";
const kWdgtText = "TC-537 the widget chosen as a .wdgt file";
const kHtmlText = "TC-537 the widget chosen by its .html file";

/** The widget's page, relative to the book's folder, decoded from the frame's URL-encoded src. */
async function getWidgetPageOnPage(page: Page): Promise<string> {
    return decodeURI(await getWidgetSrcOnPage(page));
}

/**
 * Check that a replacing widget's frame `src` (URL-encoded) is a folder of the book's activities
 * that Bloom named from the widget's shortened name, with digits added to keep it apart from the
 * folder of the widget it replaced, and not from the widget's full name.
 */
function expectInShortenedNameFolder(src: string): void {
    const widgetPage = decodeURI(src);
    expect(widgetPage).toMatch(/^activities\/[^/]+\/index\.html$/);
    const folder = widgetPage.split("/")[1];
    expect(
        folder.startsWith(kShortWidgetName) &&
            /^\d+$/.test(folder.slice(kShortWidgetName.length)),
        `The new widget's folder, "${folder}", should be the shortened name "${kShortWidgetName}" with digits added.`,
    ).toBe(true);
}

/**
 * Wait until the widget in the page's widget box shows `text`. Its frame loads the new widget's
 * document after its src changes, so an early read can find the replaced widget's text.
 */
async function expectShownWidgetText(page: Page, text: string): Promise<void> {
    await expect
        .poll(
            () =>
                getWidgetText(
                    editablePageFrame(page).frameLocator(
                        ".bloom-widgetContainer iframe",
                    ),
                ),
            {
                timeout: 30000,
                message: `The widget box should show the widget whose text is "${text}".`,
            },
        )
        .toBe(text);
}

test("replacing a widget with one whose name is over 50 characters, in a book whose title is too, works [Test Case ID 537]", async ({
    bloomApp,
    step,
}, testInfo) => {
    test.setTimeout(600000);
    const page = bloomApp.page;
    expect(
        kBookTitle.length,
        "Sanity check: the book's title should be over 50 characters",
    ).toBeGreaterThan(50);
    expect(
        kWidgetName.length,
        "Sanity check: the widget's name should be over 50 characters",
    ).toBeGreaterThan(50);

    const made = await step(
        "Make a book with a long title and a widget on a Widget Page",
        async () => {
            // Read before the title changes, which renames the book's folder.
            const bookId = readBookId(
                await makeBookFromTemplate(page, "Basic Book"),
            );
            await typeInGroup(page, ".bookTitle", "en", kBookTitle);
            await addPage(page, "Widget Page");
            await addPage(page, "Just Text", 1, "Basic Book");
            const contentPages = await getContentPages(page);
            const widgetPage = contentPages.at(-2)!;
            const textPage = contentPages.at(-1)!;
            await goToPage(page, widgetPage.id);
            await chooseWidgetOnPage(
                page,
                writeWidgetFile(
                    testInfo.outputPath("first"),
                    kWidgetName,
                    kFirstText,
                ),
            );
            expect(
                await getWidgetPageOnPage(page),
                "Sanity check: the first widget should be in the folder of its shortened name",
            ).toBe(`activities/${kShortWidgetName}/index.html`);
            await expectShownWidgetText(page, kFirstText);
            return {
                bookId,
                widgetPageId: widgetPage.id,
                textPageId: textPage.id,
            };
        },
    );
    if (!made) throw new Error("The book was never made.");

    await step(
        "Replace the widget with a .wdgt file of the same long name",
        async () => {
            const src = await replaceWidgetOnPage(
                page,
                writeWidgetFile(
                    testInfo.outputPath("wdgt"),
                    kWidgetName,
                    kWdgtText,
                ),
            );
            expectInShortenedNameFolder(src);
            await expectShownWidgetText(page, kWdgtText);
        },
    );

    const lastSrc = await step(
        "Replace it with a widget folder of the same long name, by its .html file",
        async () => {
            const src = await replaceWidgetOnPage(
                page,
                writeWidgetFolder(
                    testInfo.outputPath("html"),
                    kWidgetName,
                    kHtmlText,
                ),
            );
            expectInShortenedNameFolder(src);
            await expectShownWidgetText(page, kHtmlText);
            return src;
        },
    );
    if (!lastSrc) throw new Error("The last widget was never chosen.");

    await step("Check the saved book shows the last widget", async () => {
        // Leaving the page saves it.
        await goToPage(page, made.textPageId);
        const book = findBookFolderById(bloomApp.collectionDir, made.bookId);
        const html = fs.readFileSync(bookHtmlPath(book), "utf8");
        expect(
            html,
            "Sanity check: the saved book should have the long title",
        ).toContain(kBookTitle);
        const savedSrc = await page.evaluate(
            ({ source, pageId }) =>
                new DOMParser()
                    .parseFromString(source, "text/html")
                    .getElementById(pageId)
                    ?.querySelector(".bloom-widgetContainer iframe")
                    ?.getAttribute("src") ?? "",
            { source: html, pageId: made.widgetPageId },
        );
        expect(savedSrc).toBe(lastSrc);
        const widgetIndex = Path.join(book, decodeURI(savedSrc));
        expect(fs.readFileSync(widgetIndex, "utf8")).toContain(kHtmlText);
    });
});
