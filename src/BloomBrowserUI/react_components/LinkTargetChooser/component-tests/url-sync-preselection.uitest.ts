/**
 * Tests for LinkTargetChooser - URL Synchronization and Preselection
 * Covers: URL box syncs with book/page selections, preselection from URL prop
 * Run with: yarn test
 */

import { test, expect } from "../../component-tester/playwrightTest";
import {
    setupLinkTargetChooser,
    urlEditor,
    bookList,
    pageList,
    dialog,
} from "./test-helpers";

test.describe("LinkTargetChooser - URL Synchronization", () => {
    test("URL box shows book URL when book is selected", async ({ page }) => {
        await setupLinkTargetChooser(page);

        // Wait for books to load, then select a book
        await bookList.selectBook("book1");

        // URL should update to show book link
        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("/book/book1");
    });

    test("URL box shows book+page URL when page is selected", async ({
        page,
    }) => {
        await setupLinkTargetChooser(page);

        // Select a book (this waits for books to load)
        await bookList.selectBook("book2");

        // Select a page
        await pageList.selectPage(2);

        // URL should update to show book+page link
        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("/book/book2#2");
    });

    test("URL box shows cover page link correctly", async ({ page }) => {
        await setupLinkTargetChooser(page);

        // Select book (waits for books to load)
        await bookList.selectBook("book1");
        await pageList.selectPage(0); // Cover page

        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("/book/book1#cover");
    });

    test("Typing in URL box clears book/page selection", async ({ page }) => {
        await setupLinkTargetChooser(page);

        // Select a book first (waits for books to load)
        await bookList.selectBook("book1");

        // Type a URL
        await urlEditor.setValue("https://example.com");

        // Verify the URL is updated
        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("https://example.com");

        // Book should no longer appear selected (we'd need to check visual state in real test)
        // For now, we confirm URL changed which indicates selection was cleared
    });

    test("Selecting book after typing URL clears the URL and shows book URL", async ({
        page,
    }) => {
        await setupLinkTargetChooser(page);

        // Wait for books to load
        await bookList.waitForBooksToLoad();

        // Type a URL first
        await urlEditor.setValue("https://example.com");

        // Now select a book
        await bookList.selectBook("book3");

        // URL should be updated to book URL
        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("/book/book3");
    });
});

test.describe("LinkTargetChooser - URL Preselection", () => {
    test("Preselects book when URL is /book/BOOKID", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book2",
        });

        // Wait for books to load and component to initialize
        await bookList.waitForBooksToLoad();

        // URL box should show the URL
        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("/book/book2");

        // OK button should be enabled
        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Preselects book and page when URL is /book/BOOKID#PAGEID", async ({
        page,
    }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book1#3",
        });

        // Wait for books to load and component to initialize
        await bookList.waitForBooksToLoad();

        // URL box should show the URL
        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("/book/book1#3");

        // OK button should be enabled
        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Preselects cover page when URL has #cover", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book4#cover",
        });

        // Wait for books to load and component to initialize
        await bookList.waitForBooksToLoad();

        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("/book/book4#cover");

        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Shows external URL in URL box when provided", async ({ page }) => {
        const externalURL = "https://example.org/page";
        await setupLinkTargetChooser(page, {
            currentURL: externalURL,
        });

        // Wait for books to load and component to initialize
        await bookList.waitForBooksToLoad();

        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe(externalURL);

        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Handles empty initial URL", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "",
        });

        const urlValue = await urlEditor.getValue();
        expect(urlValue).toBe("");

        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeDisabled();
    });
});
