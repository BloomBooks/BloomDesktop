/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open indefinitely
 * so you can interact with it manually.
 *
 * Run with:
 *   (cd ../component-tester && ./show-component.sh color-picking [test-name])
 *
 * For an MCP-friendly browser-based harness (non-Playwright), run:
 *   ./scope.sh
 */
import { test } from "../../component-tester/playwrightTest";
import { setTestComponent } from "../../component-tester/setTestComponent";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        await setTestComponent(
            page,
            "../color-picking/component-tests/colorPickerManualHarness",
            "ColorPickerManualHarness",
            {},
        );

        await page.waitForEvent("close");
    });

    test("dialog", async ({ page }) => {
        test.setTimeout(0);

        await page.route(
            "**/settings/getCustomPaletteColors?palette=*",
            (route) =>
                route.fulfill({
                    status: 200,
                    contentType: "application/json",
                    body: "[]",
                }),
        );

        await setTestComponent(
            page,
            "../color-picking/component-tests/colorDisplayButtonTestHarness",
            "ColorDisplayButtonTestHarness",
            {},
        );

        await page.waitForEvent("close");
    });

    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=ColorSwatch");

        await page.waitForEvent("close");
    });
});
