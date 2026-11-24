// A page clicked in the page list while another page is still loading is remembered, and the Edit
// tab goes there once the page that was loading is ready (BL-17173). These tests cover the two
// ways that can go wrong outside a plain run of quick clicks, which
// all-pages-typing-after-a-page-change.spec.ts covers: leaving the Edit tab while a click is
// remembered, and a click made while Bloom renames the book's folder because its title changed.

import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import type { Page, Response } from "@playwright/test";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForBloomToHaveTyping,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import { readBook } from "../helpers/bookHtml";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import { clickPageThumbnail } from "../helpers/pageList";
import { waitForPageToBeShown } from "../helpers/pageView";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "page-clicks-while-loading", languages: ["en"] },
});

const kGroup = ".bloom-translationGroup";

const fixtureImage = (name: string) =>
    Path.resolve(
        Path.dirname(fileURLToPath(import.meta.url)),
        "..",
        "fixtures",
        "images",
        name,
    );

/**
 * Record every request that failed and named `folder` while `body` runs, and return them. The
 * listener is removed afterwards, because the page outlives the test.
 */
async function failedRequestsUnder(
    page: Page,
    folder: string,
    body: () => Promise<void>,
): Promise<string[]> {
    const folderName = Path.basename(folder);
    const failed: string[] = [];
    const listener = (response: Response) => {
        const url = decodeURI(response.url());
        if (response.status() >= 400 && url.includes(folderName))
            failed.push(`${response.status()} ${url}`);
    };
    page.on("response", listener);
    try {
        await body();
    } finally {
        page.off("response", listener);
    }
    return failed;
}

test("leaving the Edit tab while a page click is remembered leaves the tab working [BL-17173]", async ({
    page,
}) => {
    test.setTimeout(300000);
    const bookFolder = await makeBookFromTemplate(page, "Basic Book");
    await addPage(page, "Just Text", 3);
    const textPages = (await getPages(page)).filter((p) => p.isContentPage);
    expect(
        textPages.length,
        "test setup: the book should have at least three content pages",
    ).toBeGreaterThanOrEqual(3);
    await goToPage(page, textPages[0].id);

    // THE ACTION UNDER TEST: the second click arrives while the first page is still loading, so it
    // is remembered; then the user leaves the Edit tab at once.
    await clickPageThumbnail(page, textPages[1].id);
    await clickPageThumbnail(page, textPages[2].id);
    await switchTab(page, "collection");

    // Coming back, the Edit tab settles on a page, rather than staying stuck on its way to one.
    await switchTab(page, "edit");
    await waitForEditTabSettled(page);
    const shown = await getShownPageId(page);
    expect(
        shown,
        "The Edit tab should be showing a page after coming back to it.",
    ).toBeDefined();

    // And it still saves: typing on that page reaches the book once the page is left.
    const typed = "Typed after coming back";
    await typeInGroup(page, kGroup, "en", typed);
    await waitForBloomToHaveTyping(page, typed);
    const other = textPages.find((p) => p.id !== shown)!;
    await goToPage(page, other.id);
    await expect
        .poll(
            async () =>
                (await readBook(page, bookFolder)).pages.find(
                    (p) => p.id === shown,
                )?.text ?? "",
            {
                message:
                    "The page typed on after coming back to the Edit tab was never saved.",
            },
        )
        .toContain(typed);
});

test("a page clicked while the book's folder is renamed for its new title is where the Edit tab ends up [BL-17173]", async ({
    page,
}) => {
    test.setTimeout(300000);
    const oldFolder = await makeBookFromTemplate(page, "Basic Book");
    expect(
        Path.basename(oldFolder),
        "test setup: a book with no title yet has a placeholder folder name",
    ).toMatch(/^Book/);
    await addPage(page, "Image in Middle", 2);
    const pages = await getPages(page);
    const picturePages = pages.filter((p) => p.isContentPage).slice(-2);
    expect(
        picturePages.length,
        "test setup: the book should have two pages with a picture",
    ).toBe(2);
    const target = picturePages[1];
    // A real picture, which is looked for in the book's folder; a new page shows a placeholder.
    await goToPage(page, target.id);
    await chooseImageFile(page, fixtureImage("bird.png"));
    await goToPage(page, pages[0].id);
    await typeInGroup(page, ".bookTitle", "en", "How Clocks Tick");
    const failed = await failedRequestsUnder(page, oldFolder, async () => {
        // THE ACTION UNDER TEST: the first click saves the cover, which renames the book's folder
        // for its title, and starts loading a page; the second arrives while that page loads.
        await clickPageThumbnail(page, picturePages[0].id);
        await clickPageThumbnail(page, target.id);

        await waitForPageToBeShown(page, target.id);
        await waitForEditTabSettled(page);
    });

    expect(
        await getShownPageId(page),
        "The Edit tab should end on the page clicked last.",
    ).toBe(target.id);
    await expect
        .poll(
            async () => {
                const placement = await getImagePlacement(page);
                return placement.loaded ? placement.fileName : "(not loaded)";
            },
            {
                timeout: 30000,
                message:
                    "The picture on the page clicked last should show, from the book's renamed folder.",
            },
        )
        .toBe("bird.png");
    expect(
        failed,
        "Bloom asked for something under the book's old folder after renaming it",
    ).toEqual([]);
});
