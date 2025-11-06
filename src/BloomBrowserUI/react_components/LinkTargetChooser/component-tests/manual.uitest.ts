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

test.describe("Manual Interactive Testing", () => {
    // this is what you get if you just say "./mock.sh"
    test("default", async ({ page }) => {
        // Set a very long timeout (1 hour)
        test.setTimeout(0);

        // Set up the component with all the mocks
        await setupLinkTargetChooser(page, {
            currentURL: "",
        });

        // Log a message to the console
        console.log("\n===========================================");
        console.log("Interactive mode started");
        console.log("Close the browser or press Ctrl+C here, when done.");
        console.log("===========================================\n");

        // Keep the browser open until manually closed
        // The test will end when you close the browser window
        await page.waitForEvent("close");
    });
});
