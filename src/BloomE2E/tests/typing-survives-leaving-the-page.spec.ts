// Text typed on a page reaches the book file whichever way the user leaves the page: clicking
// another page, a page-list command, moving a page, leaving the Edit tab, or quitting Bloom.
//
// Every one of those saves the page from the copy the browser sent Bloom after the typing settled
// (BL-13502; see src/BloomExe/Edit/SavingWithoutReloading.md), not by asking the browser at the
// moment of leaving. So each case waits until Bloom has the typing (waitForBloomToHaveTyping), as a
// person's pause before moving on would, and then checks the saved file.
//
// The quit cases automate "Quit Without Losing Changes" (Test Case ID 659). The others are a new
// card; its Test Case ID goes in their titles once the card exists.
//
// The tests are serial because each one works on the book the first one builds.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    typeInGroup,
    waitForBloomToHaveTyping,
    type IBookPage,
} from "../helpers/bookMaking";
import { readBook } from "../helpers/bookHtml";
import { selectBook } from "../helpers/collection";
import {
    duplicatePageWithContextMenu,
    movePageToSlotOf,
} from "../helpers/pageList";
import { selectPage } from "../helpers/pageThumbnails";
import { switchTab } from "../helpers/workspace";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "typing-survives", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// The book every test works on, and its content pages in their original order.
let bookFolder: string;
let pages: IBookPage[];

/** Show `pageId` and type `text` into its first text box, then wait until Bloom has it. */
async function typeOnPage(page: Page, pageId: string, text: string) {
    await goToPage(page, pageId);
    await typeInGroup(page, ".bloom-translationGroup", "en", text);
    await waitForBloomToHaveTyping(page, text);
}

/** Wait until the saved book's page `pageId` holds `text`. Bloom writes as the page is left. */
async function expectSavedPageToContain(
    page: Page,
    pageId: string,
    text: string,
) {
    await expect
        .poll(
            async () =>
                (await readBook(page, bookFolder)).pages.find(
                    (p) => p.id === pageId,
                )?.text ?? "(no such page in the saved book)",
            {
                timeout: 30000,
                message: `The saved book's page ${pageId} never held "${text}".`,
            },
        )
        .toContain(text);
}

test.describe("typing survives leaving the page", () => {
    test("builds a book with four content pages", async ({ page }) => {
        test.setTimeout(300000);
        bookFolder = await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image", 4);
        pages = await getContentPages(page);
        expect(pages, "test setup: four content pages").toHaveLength(4);
    });

    test("clicking another page's thumbnail", async ({ page }) => {
        await typeOnPage(page, pages[0].id, "typed before clicking away");
        await selectPage(page, pages[1].id);
        await expectSavedPageToContain(
            page,
            pages[0].id,
            "typed before clicking away",
        );
    });

    test("duplicating the page from its menu keeps the typing on the page and its copy", async ({
        page,
    }) => {
        await typeOnPage(page, pages[1].id, "typed before duplicating");
        const copy = await duplicatePageWithContextMenu(page, pages[1].id);
        await expectSavedPageToContain(
            page,
            pages[1].id,
            "typed before duplicating",
        );
        await expectSavedPageToContain(
            page,
            copy.id,
            "typed before duplicating",
        );
    });

    test("moving another page", async ({ page }) => {
        await typeOnPage(page, pages[2].id, "typed before a page moved");
        await movePageToSlotOf(page, pages[3].id, pages[0].id);
        await expectSavedPageToContain(
            page,
            pages[2].id,
            "typed before a page moved",
        );
    });

    test("leaving the Edit tab", async ({ page }) => {
        await typeOnPage(page, pages[3].id, "typed before leaving the tab");
        await switchTab(page, "collection");
        await expectSavedPageToContain(
            page,
            pages[3].id,
            "typed before leaving the tab",
        );
    });

    test("quitting Bloom from the Edit tab [Test Case ID 659]", async ({
        page,
        bloomApp,
    }) => {
        test.setTimeout(300000);
        await selectBook(page, bookFolder);
        await switchTab(page, "edit");
        await typeOnPage(page, pages[0].id, "typed before quitting");
        const afterQuit = await bloomApp.quitAndRestart();
        await expectSavedPageToContain(
            afterQuit,
            pages[0].id,
            "typed before quitting",
        );
    });

    test("quitting Bloom from the Collections and Publish tabs [Test Case ID 659]", async ({
        page,
        bloomApp,
    }) => {
        // Nothing to save on these tabs; the point is that Bloom quits cleanly from each and the
        // book is as it was. quitAndRestart throws if Bloom does not exit.
        test.setTimeout(300000);
        const before = await readBook(page, bookFolder);
        await switchTab(page, "collection");
        let shell = await bloomApp.quitAndRestart();
        await selectBook(shell, bookFolder);
        await switchTab(shell, "publish");
        shell = await bloomApp.quitAndRestart();
        expect((await readBook(shell, bookFolder)).pages).toEqual(before.pages);
    });
});
