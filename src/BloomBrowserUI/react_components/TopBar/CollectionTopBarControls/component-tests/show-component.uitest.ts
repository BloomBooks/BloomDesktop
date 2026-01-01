/**
 * Interactive manual testing for CollectionTopBarControls.
 * Preferred manual debugging: `yarn scope [exportName]` (uses scope-harness.tsx).
 * To run this Playwright-driven manual suite:
 *   (cd ../../../component-tester && ./show-component.sh TopBar/CollectionTopBarControls [test-name])
 */
import { Page, test } from "../../../component-tester/playwrightTest";
import { setTestComponent } from "../../../component-tester/setTestComponent";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

const routeTopBarStatus = async (page: Page, status: string) => {
    await page.route("**/*", (route) => {
        const url = route.request().url();
        if (url.includes("teamCollection/tcStatus")) {
            return route.fulfill({
                status: 200,
                contentType: "application/json",
                body: status,
            });
        }
        return route.continue();
    });
};

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "Nominal");

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("new-stuff", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "NewStuff");

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("error", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "Error");

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("disconnected", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "Disconnected");

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("none", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "None");

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("gallery-all-states", async ({ page }) => {
        test.setTimeout(0);

        await setTestComponent(
            page,
            "../TopBar/CollectionTopBarControls/component-tests/teamCollectionButtonGalleryTest",
            "TeamCollectionButtonGalleryTest",
            {},
        );

        await page.waitForEvent("close");
    });

    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });
});
