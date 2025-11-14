/**
 * Interactive manual testing mode using Playwright.
 * This opens a visible browser with the component and keeps it open indefinitely
 * so you can interact with it manually.
 *
 * Run with: ./show.sh
 */
import { test } from "../../component-tester/playwrightTest";
import { setupRegistrationComponent } from "./test-helpers";

const includeManualTests = process.env.PLAYWRIGHT_INCLUDE_MANUAL === "1";
const manualDescribe = includeManualTests ? test.describe : test.describe.skip;

manualDescribe("Manual Interactive Testing", () => {
    test("default", async ({ page }) => {
        test.setTimeout(0);

        await setupRegistrationComponent(page, {
            initialInfo: {
                firstName: "",
                surname: "",
                email: "",
                organization: "",
                usingFor: "",
                hadEmailAlready: false,
            },
            mayChangeEmail: true,
            emailRequiredForTeamCollection: false,
        });

        await page.waitForEvent("close");
    });

    test("with-existing-info", async ({ page }) => {
        test.setTimeout(0);

        await setupRegistrationComponent(page, {
            initialInfo: {
                firstName: "John",
                surname: "Smith",
                email: "john.smith@example.com",
                organization: "Test Organization",
                usingFor: "Testing purposes",
                hadEmailAlready: true,
            },
            mayChangeEmail: false,
            emailRequiredForTeamCollection: false,
        });

        await page.waitForEvent("close");
    });

    test("email-required", async ({ page }) => {
        test.setTimeout(0);

        await setupRegistrationComponent(page, {
            initialInfo: {
                firstName: "",
                surname: "",
                email: "",
                organization: "",
                usingFor: "",
                hadEmailAlready: false,
            },
            mayChangeEmail: true,
            emailRequiredForTeamCollection: true,
        });

        await page.waitForEvent("close");
    });

    test("with-bloom-backend", async ({ page }) => {
        test.setTimeout(0);

        await page.goto("/?component=RegistrationContents");

        await page.waitForEvent("close");
    });
});
