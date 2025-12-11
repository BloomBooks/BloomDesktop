/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open so you can interact with it.
 *
 * Run with: ./show.sh
 */
import { test } from "../../../component-tester/playwrightTest";
import { setTestComponent } from "../../../component-tester/setTestComponent";
import { prepareGetResponse } from "../../../component-tester/apiInterceptors";

declare const process: { env?: Record<string, string | undefined> };
const includeManualTests = process?.env?.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        const statePayload = {
            uiLanguageLabel: "English",
            zoom: 100,
            zoomEnabled: true,
            minZoom: 50,
            maxZoom: 300,
        };

        const statePatterns = [
            /.*\/bloom\/api\/workspace\/topRight\/state.*/,
            /.*\/bloom\/workspace\/topRight\/state.*/,
        ];

        statePatterns.forEach((pattern) =>
            prepareGetResponse(page, pattern, statePayload, { wrapBody: true }),
        );

        await page.route("**/bloom/**/workspace/topRight/*", async (route) => {
            console.log("intercept topRight", route.request().url());
            if (route.request().method() === "POST") {
                await route.fulfill({
                    status: 200,
                    contentType: "application/json",
                    body: JSON.stringify({ success: true }),
                });
                return;
            }
            await route.continue();
        });

        await page.route("**/bloom/**", async (route) => {
            console.log(
                "intercept bloom catch-all",
                route.request().method(),
                route.request().url(),
            );
            if (route.request().method() === "GET") {
                await route.fulfill({
                    status: 200,
                    contentType: "application/json",
                    body: JSON.stringify({ data: {} }),
                });
                return;
            }
            await route.fulfill({
                status: 200,
                contentType: "application/json",
                body: JSON.stringify({ success: true }),
            });
        });

        await setTestComponent(
            page,
            "../TopBar/workspaceTopRightControls/WorkspaceTopRightControls",
            "WorkspaceTopRightControls",
            {
                skipApi: true,
                initialState: statePayload,
            },
        );

        await page.waitForEvent("close");
    });

    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=WorkspaceTopRightControls");

        await page.waitForEvent("close");
    });
});
