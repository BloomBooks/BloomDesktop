/**
 * Tests for LinkTargetChooser - More Menu Functionality
 * Covers: More menu button, Back menu item functionality
 * Run with: yarn test
 */

import { test, expect } from "../../component-tester/playwrightTest";
import { setupLinkTargetChooser } from "./test-helpers";

test.describe("LinkTargetChooser - More Menu Functionality", () => {
    test("More menu button is visible", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const moreButton = page.getByTestId("url-editor-more-menu-button");
        await expect(moreButton).toBeVisible();
    });

    test("Clicking More button opens menu", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const moreButton = page.getByTestId("url-editor-more-menu-button");
        await moreButton.click();

        const menu = page.getByTestId("url-editor-more-menu");
        await expect(menu).toBeVisible();
    });

    test("More menu contains Back menu item", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const moreButton = page.getByTestId("url-editor-more-menu-button");
        await moreButton.click();

        const backMenuItem = page.getByTestId("url-editor-back-menu-item");
        await expect(backMenuItem).toBeVisible();
        await expect(backMenuItem).toHaveText("Back");
    });

    test("Clicking Back menu item sets URL to /back", async ({ page }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "https://example.com",
        });

        // Open the More menu
        const moreButton = page.getByTestId("url-editor-more-menu-button");
        await moreButton.click();

        // Click the Back menu item
        const backMenuItem = page.getByTestId("url-editor-back-menu-item");
        await backMenuItem.click();

        // Verify the URL was changed to /back
        const value = await context.urlEditor.getValue();
        expect(value).toBe("/back");
    });

    test("Menu closes after clicking Back menu item", async ({ page }) => {
        await setupLinkTargetChooser(page);

        // Open the More menu
        const moreButton = page.getByTestId("url-editor-more-menu-button");
        await moreButton.click();

        const menu = page.getByTestId("url-editor-more-menu");
        await expect(menu).toBeVisible();

        // Click the Back menu item
        const backMenuItem = page.getByTestId("url-editor-back-menu-item");
        await backMenuItem.click();

        // Verify the menu is closed
        await expect(menu).not.toBeVisible();
    });

    test("Back menu item clears book and page selection", async ({ page }) => {
        const context = await setupLinkTargetChooser(page, {
            currentURL: "/book/book1#2",
        });

        // Verify initial URL is set
        let value = await context.urlEditor.getValue();
        expect(value).toBe("/book/book1#2");

        // Open the More menu and click Back
        const moreButton = page.getByTestId("url-editor-more-menu-button");
        await moreButton.click();

        const backMenuItem = page.getByTestId("url-editor-back-menu-item");
        await backMenuItem.click();

        // Verify the URL changed to /back
        value = await context.urlEditor.getValue();
        expect(value).toBe("/back");
    });
});
