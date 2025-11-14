/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open indefinitely
 * so you can interact with it manually.
 *
 * Run with: ./show.sh, ./show.sh page-only-url, etc.
 */
import { test } from "../../component-tester/playwrightTest";
import { setupLinkTargetChooser } from "./test-helpers";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "/book/book10#44",
            currentBookBeingEditedId: "book3",
            clipboardText: "https://example.com",
        });

        await page.waitForEvent("close");
    });

    test("page-only-url", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "#5",
            currentBookBeingEditedId: "book3",
        });

        await page.waitForEvent("close");
    });

    test("hash-only-url-with-cover", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "#cover",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });

    test("missing-book", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "/book/999",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });

    test("book-path-url-simplified-to-hash", async ({ page }) => {
        test.setTimeout(0);

        await setupLinkTargetChooser(page, {
            currentURL: "/book/book2#7",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });

    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=LinkTargetChooserDialog");

        await page.waitForEvent("close");
    });
});
