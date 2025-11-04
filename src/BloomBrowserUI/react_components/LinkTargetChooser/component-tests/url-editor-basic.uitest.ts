/**
 * Tests for LinkTargetChooser - URLEditor Basic Functionality
 * Covers: URL input, button visibility and state
 * Run with: yarn test
 */

import { test, expect } from "../../component-tester/playwrightTest";
import { setupLinkTargetChooser, urlEditor } from "./test-helpers";

test.describe("LinkTargetChooser - URLEditor Basic Functionality", () => {
    test("URLEditor renders with all elements visible", async ({ page }) => {
        await setupLinkTargetChooser(page);

        // Verify all elements are present
        const input = await urlEditor.getInput();
        await expect(input).toBeVisible();

        const pasteButton = await urlEditor.getPasteButton();
        await expect(pasteButton).toBeVisible();

        const openButton = await urlEditor.getOpenButton();
        await expect(openButton).toBeVisible();
    });

    test("Can type URL into text field", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const testURL = "https://example.com";
        await urlEditor.setValue(testURL);

        const value = await urlEditor.getValue();
        expect(value).toBe(testURL);
    });

    test("Open button is disabled when URL is empty", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const openButton = await urlEditor.getOpenButton();
        await expect(openButton).toBeDisabled();
    });

    test("Open button is enabled when URL has value", async ({ page }) => {
        await setupLinkTargetChooser(page, {
            currentURL: "https://example.com",
        });

        const openButton = await urlEditor.getOpenButton();
        await expect(openButton).toBeEnabled();
    });

    test("Open button becomes enabled after typing URL", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const openButton = await urlEditor.getOpenButton();
        await expect(openButton).toBeDisabled();

        await urlEditor.setValue("https://example.com");
        await expect(openButton).toBeEnabled();
    });

    test("Open button posts URL to Bloom API", async ({ page }) => {
        await setupLinkTargetChooser(page);

        const testURL = "https://example.org/resource";
        await urlEditor.setValue(testURL);

        const openButton = await urlEditor.getOpenButton();
        await expect(openButton).toBeEnabled();

        const [request] = await Promise.all([
            page.waitForRequest(
                (req) =>
                    req.method() === "POST" &&
                    req.url().includes("/bloom/api/common/openUrl?"),
            ),
            openButton.click(),
        ]);

        const parsed = new URL(request.url());
        expect(parsed.searchParams.get("url")).toBe(testURL);
    });
});
