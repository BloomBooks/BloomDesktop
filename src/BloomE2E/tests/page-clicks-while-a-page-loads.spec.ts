// A page clicked in the page list while another page is still loading is remembered, and the Edit
// tab goes there once the page that was loading is ready (BL-17173). The quick-click tests in
// all-pages-typing-after-a-page-change.spec.ts reach that only when a click happens to land during a
// load; these make sure one does, in two cases: leaving the Edit tab while a click is remembered,
// and a click made while the page after a save that renamed the book's folder (because its title
// changed) is loading.
// Each holds back the first page's report that it has loaded (clickPageWhileAnotherLoads), so the
// second click lands while that page is still loading on any machine, and checks that Bloom
// remembered it.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    getEditTabState,
    getPages,
    getShownPageId,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForBloomToHaveTyping,
    waitForEditTabSettled,
} from "../helpers/bookMaking";
import { readBook } from "../helpers/bookHtml";
import { failedRequestsUnder } from "../helpers/failedRequests";
import { chooseImageFile, getImagePlacement } from "../helpers/images";
import { clickPageWhileAnotherLoads } from "../helpers/pageList";
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
    // is remembered; then the user leaves the Edit tab before that page has loaded.
    await clickPageWhileAnotherLoads(
        page,
        textPages[1].id,
        textPages[2].id,
        async () => {
            await switchTab(page, "collection");
            expect(
                (await getEditTabState(page)).pageToGoToOnceLoaded,
                "Leaving the Edit tab should forget the remembered click.",
            ).toBe("");
        },
    );

    // Coming back, the Edit tab settles on a page, rather than staying stuck on its way to one.
    await switchTab(page, "edit");
    await waitForEditTabSettled(page);
    const shown = (await getShownPageId(page))!;

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
    const title = "How Clocks Tick";
    await typeInGroup(page, ".bookTitle", "en", title);
    expect(
        fs.existsSync(oldFolder),
        "test setup: typing a title does not rename the folder by itself",
    ).toBe(true);
    const failed = await failedRequestsUnder(page, oldFolder, async () => {
        // THE ACTION UNDER TEST: the first click saves the cover, which renames the book's folder
        // for its title, and starts loading a page; the second arrives while that page loads.
        await clickPageWhileAnotherLoads(page, picturePages[0].id, target.id);

        await waitForPageToBeShown(page, target.id);
        await waitForEditTabSettled(page);
    });

    const newFolder = Path.join(Path.dirname(oldFolder), title);
    expect(
        fs.existsSync(newFolder) && !fs.existsSync(oldFolder),
        "The save should have renamed the book's folder for its title, or this tests nothing.",
    ).toBe(true);
    expect(
        await getShownPageId(page),
        "The Edit tab should end on the page clicked last.",
    ).toBe(target.id);
    expect(
        decodeURI(editablePageFrame(page).url()),
        "The page clicked last should have been loaded from the book's renamed folder.",
    ).toContain(`/${title}/`);
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
