/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open indefinitely
 * so you can interact with it manually.
 *
 * Run with: ./show.sh, ./show.sh with-preselected-links, etc.
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

    // this is what you get if you run ./show-with-bloom.sh
    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=BookGridSetup");

        await page.waitForEvent("close");
    });
});
