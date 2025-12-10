/**
 * Interactive manual testing for CollectionTopBarControls.
 * Requires a running Bloom backend (uses live API + websocket traffic).
 * Run with: ./show.sh or ./show.sh with-bloom-backend
 */
import { test } from "../../component-tester/playwrightTest";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=CollectionTopBarControls");

        await page.waitForEvent("close");
    });
});
