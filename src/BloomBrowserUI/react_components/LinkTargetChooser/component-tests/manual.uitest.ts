/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open indefinitely
 * so you can interact with it manually.
 *
 * Run with: yarn playwright test manual-interactive.spec.ts --headed
 * Or add a script to package.json for convenience
 */

import { test } from "../../component-tester/playwrightTest";
import { setupLinkTargetChooser } from "./test-helpers";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    // this is what you get if you just say "./mock.sh"
    test("default", async ({ page }) => {
        // Set a very long timeout (1 hour)
        test.setTimeout(0);

        // Set up the component with all the mocks
        await setupLinkTargetChooser(page, {
            currentURL: "/book/book10#44",
        });

        // Interactive mode started; keep browser open until manually closed

        // Keep the browser open until manually closed
        // The test will end when you close the browser window
        await page.waitForEvent("close");
    });
});
