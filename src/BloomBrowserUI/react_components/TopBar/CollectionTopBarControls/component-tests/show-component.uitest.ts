/**
 * Interactive manual testing for CollectionTopBarControls.
 * Requires a running Bloom backend (uses live API + websocket traffic).
 * Run with: ./show.sh or ./show.sh with-bloom-backend
 */
import { Page, test } from "../../../component-tester/playwrightTest";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

const routeTopBarStatus = async (
    page: Page,
    status: string,
    showReloadButton: boolean = false,
) => {
    await page.route("**/*", (route) => {
        const url = route.request().url();
        if (url.includes("teamCollection/topBarStatus")) {
            return route.fulfill({
                status: 200,
                contentType: "application/json",
                body: JSON.stringify({
                    status,
                    showReloadButton,
                }),
            });
        }
        return route.continue();
    });
};

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "Nominal", true);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("new-stuff", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "NewStuff", true);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("error", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "Error", true);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("clobber-pending", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "ClobberPending", true);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("disconnected", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "Disconnected", false);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("none", async ({ page }) => {
        test.setTimeout(0);

        await routeTopBarStatus(page, "None", false);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });

    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });
});
