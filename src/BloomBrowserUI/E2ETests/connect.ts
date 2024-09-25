import { test as base, Browser, BrowserContext, Page } from "@playwright/test";

export const test = base.extend<{
    collectionBrowser: Browser;
    collectionContext: BrowserContext;
    collectionPage: Page;
    editPageListBrowser: Browser;
    editPageListContext: BrowserContext;
    editPageListPage: Page;
    editPageContentsBrowser: Browser;
    editPageContentsContext: BrowserContext;
    editPageContentsPage: Page;
}>({
    collectionBrowser: async ({ playwright }, use) => {
        const browser = await playwright.chromium.connectOverCDP(
            "http://127.0.0.1:9222"
        );
        await use(browser);
        await browser.close(); // will just disconnect
    },
    collectionContext: async ({ collectionBrowser }, use) => {
        await use(
            collectionBrowser.contexts()[0] ||
                (await collectionBrowser.newContext())
        );
    },
    collectionPage: async ({ collectionContext }, use) => {
        await use(
            collectionContext.pages()[0] || (await collectionContext.newPage())
        );
    },
    // now make a set for editPageList at 9223
    editPageListBrowser: async ({ playwright }, use) => {
        const browser = await playwright.chromium.connectOverCDP(
            "http://127.0.0.1:9223"
        );
        await use(browser);
        await browser.close(); // will just disconnect
    },
    editPageListContext: async ({ editPageListBrowser }, use) => {
        await use(
            editPageListBrowser.contexts()[0] ||
                (await editPageListBrowser.newContext())
        );
    },
    editPageListPage: async ({ editPageListContext }, use) => {
        await use(
            editPageListContext.pages()[0] ||
                (await editPageListContext.newPage())
        );
    },
    // now make a set for editPageContents at 9224
    editPageContentsBrowser: async ({ playwright }, use) => {
        const browser = await playwright.chromium.connectOverCDP(
            "http://127.0.0.1:9224"
        );

        await use(browser);
        await browser.close(); // will just disconnect
    },
    editPageContentsContext: async ({ editPageContentsBrowser }, use) => {
        await use(
            editPageContentsBrowser.contexts()[0] ||
                (await editPageContentsBrowser.newContext())
        );
    },
    editPageContentsPage: async ({ editPageContentsContext }, use) => {
        await use(
            editPageContentsContext.pages()[0] ||
                (await editPageContentsContext.newPage())
        );
    }
});

export { expect, Page } from "@playwright/test";
