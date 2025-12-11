/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open so you can interact with it.
 *
 * Run with: ./show.sh
 */
import { test } from "../../../component-tester/playwrightTest";
import { setTestComponent } from "../../../component-tester/setTestComponent";
import { prepareGetResponse } from "../../../component-tester/apiInterceptors";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        const statePayload = {
            uiLanguageLabel: "English",
            showUnapprovedText:
                "Show translations which have not been approved yet",
            showUnapprovedChecked: false,
            zoom: 100,
            zoomEnabled: true,
            minZoom: 50,
            maxZoom: 300,
        };

        const statePatterns = [
            /.*\/bloom\/api\/workspace\/topRight\/state.*/,
            /.*\/bloom\/workspace\/topRight\/state.*/,
            /.*workspaceTopRight\?.*state.*/, // fallback safety
        ];

        statePatterns.forEach((pattern) =>
            prepareGetResponse(page, pattern, statePayload, { wrapBody: true }),
        );

        const languagePatterns = [
            /.*\/bloom\/api\/workspace\/topRight\/languages.*/,
            /.*\/bloom\/workspace\/topRight\/languages.*/,
        ];

        languagePatterns.forEach((pattern) =>
            prepareGetResponse(
                page,
                pattern,
                [
                    {
                        langTag: "en",
                        menuText: "English",
                        tooltip: "100% translated",
                        isCurrent: true,
                    },
                    {
                        langTag: "fr",
                        menuText: "Français",
                        tooltip: "80% translated",
                        isCurrent: false,
                    },
                ],
                { wrapBody: true },
            ),
        );

        const helpPatterns = [
            /.*\/bloom\/api\/workspace\/topRight\/helpItems.*/,
            /.*\/bloom\/workspace\/topRight\/helpItems.*/,
        ];

        helpPatterns.forEach((pattern) =>
            prepareGetResponse(
                page,
                pattern,
                [
                    {
                        id: "documentation",
                        text: "Documentation",
                        isSeparator: false,
                        enabled: true,
                    },
                    {
                        id: "dividerA",
                        text: "",
                        isSeparator: true,
                        enabled: false,
                    },
                    {
                        id: "aboutBloom",
                        text: "About Bloom",
                        isSeparator: false,
                        enabled: true,
                    },
                ],
                { wrapBody: true },
            ),
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
                initialLanguages: [
                    {
                        langTag: "en",
                        menuText: "English",
                        tooltip: "100% translated",
                        isCurrent: true,
                    },
                    {
                        langTag: "fr",
                        menuText: "Français",
                        tooltip: "80% translated",
                        isCurrent: false,
                    },
                ],
                initialHelpItems: [
                    {
                        id: "documentation",
                        text: "Documentation",
                        isSeparator: false,
                        enabled: true,
                    },
                    {
                        id: "dividerA",
                        text: "",
                        isSeparator: true,
                        enabled: false,
                    },
                    {
                        id: "aboutBloom",
                        text: "About Bloom",
                        isSeparator: false,
                        enabled: true,
                    },
                ],
            },
        );

        await page.waitForEvent("close");
    });

    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=TopBar/workspaceTopRightControls");

        await page.waitForEvent("close");
    });
});
