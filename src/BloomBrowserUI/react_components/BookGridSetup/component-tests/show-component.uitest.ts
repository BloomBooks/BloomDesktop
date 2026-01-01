/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open indefinitely
 * so you can interact with it manually.
 *
 * Preferred manual debugging: `yarn scope [exportName]` (uses scope-harness.tsx).
 * To run this Playwright-driven manual suite: (cd ../component-tester && ./show-component.sh BookGridSetup [test-name])
 */
import { test } from "../../component-tester/playwrightTest";
import { setupBookGridSetupComponent, createTestBook } from "./test-helpers";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        const sourceBooks = [
            createTestBook("book1", "The Moon Book"),
            createTestBook("book2", "Counting Fun"),
            createTestBook("book3", "Animal Friends"),
            createTestBook("book4", "Story Builders"),
        ];

        await setupBookGridSetupComponent(page, {
            sourceBooks,
            links: [],
        });

        await page.waitForEvent("close");
    });

    test("with-preselected-links", async ({ page }) => {
        test.setTimeout(0);

        const moonBook = createTestBook("book1", "The Moon Book");
        const countingFun = createTestBook("book2", "Counting Fun");
        const animalFriends = createTestBook("book3", "Animal Friends");

        await setupBookGridSetupComponent(page, {
            sourceBooks: [moonBook, countingFun, animalFriends],
            links: [{ book: moonBook }, { book: countingFun }],
        });

        await page.waitForEvent("close");
    });

    // This is similar to what you get if you run `yarn scope --backend` (or `yarn scope:bloom`).
    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=BookGridSetup");

        await page.waitForEvent("close");
    });
});
