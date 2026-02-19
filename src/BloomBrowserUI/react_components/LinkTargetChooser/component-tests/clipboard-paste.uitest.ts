/**
 * Tests for LinkTargetChooser - Clipboard Paste Functionality
 * Covers: Paste button behavior with various clipboard content formats
 * Run with: yarn test
 */

import { test, expect } from "../../component-tester/playwrightTest";
import { prepareGetResponse } from "../../component-tester/apiInterceptors";
import { setupLinkTargetChooser } from "./test-helpers";

test.describe("LinkTargetChooser - Clipboard Paste", () => {
    test("Paste button pastes URL from clipboard - plain string response", async ({
        page,
    }) => {
        const clipboardURL = "https://supabase.com/";

        const context = await setupLinkTargetChooser(page);

        // Mock the clipboard API endpoint - plain string response
        // This is called AFTER setup so it overrides the default empty clipboard
        prepareGetResponse(page, "**/bloom/api/common/clipboardText", {
            data: clipboardURL,
        });

        // Verify URL field is initially empty
        const initialValue = await context.urlEditor.getValue();
        expect(initialValue).toBe("");

        // Click the paste button
        const pasteButton = await context.urlEditor.getPasteButton();
        await pasteButton.click();

        // Wait a moment for the async operation to complete
        await page.waitForTimeout(100);

        // Verify the URL was pasted
        const value = await context.urlEditor.getValue();
        expect(value).toBe(clipboardURL);
    });

    test("Paste button pastes URL from clipboard - direct string response", async ({
        page,
    }) => {
        const clipboardURL = "https://example.org/page";

        const context = await setupLinkTargetChooser(page);

        // Mock the clipboard API endpoint - direct string response (no data wrapper)
        prepareGetResponse(
            page,
            "**/bloom/api/common/clipboardText",
            clipboardURL,
        );

        // Click the paste button
        const pasteButton = await context.urlEditor.getPasteButton();
        await pasteButton.click();

        // Wait for the async operation
        await page.waitForTimeout(100);

        // Verify the URL was pasted
        const value = await context.urlEditor.getValue();
        expect(value).toBe(clipboardURL);
    });

    test("Paste button does nothing when clipboard is empty", async ({
        page,
    }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "https://initial.com",
        });

        // Mock empty clipboard (override default)
        prepareGetResponse(page, "**/bloom/api/common/clipboardText", {
            data: "",
        });

        const initialValue = await context.urlEditor.getValue();

        // Click the paste button
        const pasteButton = await context.urlEditor.getPasteButton();
        await pasteButton.click();

        await page.waitForTimeout(100);

        // URL should remain unchanged
        const value = await context.urlEditor.getValue();
        expect(value).toBe(initialValue);
    });

    test("Paste button overwrites existing URL", async ({ page }) => {
        const clipboardURL = "https://newurl.com/path";

        const context = await setupLinkTargetChooser(page, {
            currentURL: "https://oldurl.com",
        });

        prepareGetResponse(page, "**/bloom/api/common/clipboardText", {
            data: clipboardURL,
        });

        // Verify initial URL
        const initialValue = await context.urlEditor.getValue();
        expect(initialValue).toBe("https://oldurl.com");

        // Click paste
        const pasteButton = await context.urlEditor.getPasteButton();
        await pasteButton.click();

        await page.waitForTimeout(100);

        // Verify URL was replaced
        const value = await context.urlEditor.getValue();
        expect(value).toBe(clipboardURL);
    });

    test("Paste button enables open button after pasting valid URL", async ({
        page,
    }) => {
        const clipboardURL = "https://google.com";

        const context = await setupLinkTargetChooser(page);

        prepareGetResponse(page, "**/bloom/api/common/clipboardText", {
            data: clipboardURL,
        });

        // Verify open button is initially disabled
        const openButton = await context.urlEditor.getOpenButton();
        await expect(openButton).toBeDisabled();

        // Click paste
        const pasteButton = await context.urlEditor.getPasteButton();
        await pasteButton.click();

        await page.waitForTimeout(100);

        // Open button should now be enabled
        await expect(openButton).toBeEnabled();
    });

    test("Paste button handles clipboard with whitespace", async ({ page }) => {
        const clipboardURL = "  https://example.com/page  ";

        const context = await setupLinkTargetChooser(page);

        prepareGetResponse(page, "**/bloom/api/common/clipboardText", {
            data: clipboardURL,
        });

        const pasteButton = await context.urlEditor.getPasteButton();
        await pasteButton.click();

        await page.waitForTimeout(100);

        // Should paste the URL as-is (trimming happens when checking if open button should be enabled)
        const value = await context.urlEditor.getValue();
        expect(value).toBe(clipboardURL);
    });
});
