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
    // this is what you get if you just say "./show.sh"
    test("default", async ({ page }) => {
        // Set a very long timeout (1 hour)
        test.setTimeout(0);

        // Set up the component with all the mocks
        // Note: In Playwright test mode, we can't access the real system clipboard,
        // so we mock it with a sample URL that you can paste using the paste button.
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book10#44",
            currentBookBeingEditedId: "book3",
            clipboardText: "https://example.com", // Mock clipboard content
        });

        // Interactive mode started; keep browser open until manually closed

        // Keep the browser open until manually closed
        // The test will end when you close the browser window
        await page.waitForEvent("close");
    });

    test("page-only-url", async ({ page }) => {
        test.setTimeout(0);

        // Demonstrates hash-only URL (#5) auto-selecting the current book (book3)
        // and navigating to page 5. The URL stays as #5 (not converted to /book/book3#5)
        await setupLinkTargetChooser(page, {
            currentURL: "#5",
            currentBookBeingEditedId: "book3",
        });

        await page.waitForEvent("close");
    });

    test("hash-only-url-with-cover", async ({ page }) => {
        test.setTimeout(0);

        // Demonstrates hash-only URL (#cover) auto-selecting the current book (book2)
        // and showing the cover page. The URL stays as #cover (not converted to /book/book2)
        await setupLinkTargetChooser(page, {
            currentURL: "#cover",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });
    test("missing-book", async ({ page }) => {
        test.setTimeout(0);

        // Demonstrates hash-only URL (#cover) auto-selecting the current book (book2)
        // and showing the cover page. The URL stays as #cover (not converted to /book/book2)
        await setupLinkTargetChooser(page, {
            currentURL: "/book/999",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });

    test("book-path-url-simplified-to-hash", async ({ page }) => {
        test.setTimeout(0);

        // Demonstrates that a book-path URL (/book/book2#7) gets simplified to #7
        // when it refers to the current book (book2)
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book2#7",
            currentBookBeingEditedId: "book2",
        });

        await page.waitForEvent("close");
    });

    // this is what you get if you say "./show-with-bloom.sh"
    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        // With real backend, we don't set up mocks - just navigate to the component
        // The component will use real API calls to Bloom at localhost:8089
        await page.goto("/?component=LinkTargetChooserDialog");

        // Keep the browser open until manually closed
        await page.waitForEvent("close");
    });
});
