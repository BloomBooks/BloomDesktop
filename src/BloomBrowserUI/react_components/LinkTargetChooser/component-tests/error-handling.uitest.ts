/**
 * Tests for LinkTargetChooser - Error Handling
 * Covers: Missing book/page detection and error display
 * Run with: yarn test
 */

import { test, expect } from "../../component-tester/playwrightTest";
import {
    setupLinkTargetChooser,
    urlEditor,
    errorDisplay,
    dialog,
    bookList,
} from "./test-helpers";

test.describe("LinkTargetChooser - Error Handling for Missing Books/Pages", () => {
    test("Shows error when URL points to missing book", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/nonexistent-book",
        });

        await bookList.waitForBooksToLoad();

        // Wait for the error message to appear
        const errorMsgElement = await errorDisplay.getErrorMessage();
        await errorMsgElement.waitFor({ state: "visible", timeout: 1000 });

        // Error message should be visible
        const isErrorVisible = await errorDisplay.isVisible();
        expect(isErrorVisible).toBe(true);

        // Get the error text
        const errorText = await errorMsgElement.textContent();
        expect(errorText).toContain("Book not found");
        expect(errorText).toContain("nonexistent-book");

        // OK button should be disabled
        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeDisabled();
    });

    test("Shows error when URL points to missing page in valid book", async ({
        page,
    }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book1#999", // book1 only has 5 pages (0-4)
        });

        // Wait for the error message to appear
        const errorMsgElement = await errorDisplay.getErrorMessage();
        await errorMsgElement.waitFor({ state: "visible", timeout: 1000 });

        // Error message should be visible
        const isErrorVisible = await errorDisplay.isVisible();
        expect(isErrorVisible).toBe(true);

        // Get the error text
        const errorText = await errorMsgElement.textContent();
        expect(errorText).toContain("Page 999 not found");
        expect(errorText).toContain("First Book");

        // OK button should be disabled
        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeDisabled();
    });

    test("No error when URL points to valid book", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book2",
        });

        // Wait for books to load
        await bookList.waitForBooksToLoad();

        // Error should not be visible
        const isErrorVisible = await errorDisplay.isVisible();
        expect(isErrorVisible).toBe(false);

        // OK button should be enabled
        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("No error when URL points to valid book and page", async ({
        page,
    }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book3#2",
        });

        await bookList.waitForBooksToLoad();

        const isErrorVisible = await errorDisplay.isVisible();
        expect(isErrorVisible).toBe(false);

        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("No error when URL points to cover page", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book1#cover",
        });

        await bookList.waitForBooksToLoad();

        const isErrorVisible = await errorDisplay.isVisible();
        expect(isErrorVisible).toBe(false);

        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("Error clears when valid book is selected after missing book", async ({
        page,
    }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "/book/missing-book",
        });

        // Wait for the error message to appear
        const errorMsgElement = await errorDisplay.getErrorMessage();
        await errorMsgElement.waitFor({ state: "visible", timeout: 1000 });

        // Verify error appears
        let isErrorVisible = await errorDisplay.isVisible();
        expect(isErrorVisible).toBe(true);

        // Now type a valid URL
        await urlEditor.setValue("/book/book1");

        // Wait for error to disappear
        await errorMsgElement.waitFor({ state: "hidden", timeout: 1000 });
        isErrorVisible = await errorDisplay.isVisible();
        expect(isErrorVisible).toBe(false);

        // OK button should be enabled
        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });

    test("No error for external URLs", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "https://example.com",
        });

        await bookList.waitForBooksToLoad();

        const isErrorVisible = await errorDisplay.isVisible();
        expect(isErrorVisible).toBe(false);

        const okButton = await dialog.getOKButton();
        await expect(okButton).toBeEnabled();
    });
});
