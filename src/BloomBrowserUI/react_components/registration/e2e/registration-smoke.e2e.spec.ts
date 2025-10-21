/**
 * Smoke tests for the RegistrationContents component.
 *
 * These tests verify basic functionality of the registration form
 * by running against the dev server at http://127.0.0.1:5173/
 *
 * To test, cd to component-tester and run:
 *    yarn dev
 * In another terminal, run:
 *    yarn test
 */

import { expect, test } from "@playwright/test";
import { setupRegistrationComponent } from "./setup";

test.describe("registration contents", () => {
    test("renders the registration form", async ({ page }) => {
        await page.goto("/?component=RegistrationContents");
        await expect(
            page.getByRole("textbox", { name: "First Name" }),
        ).toBeVisible();
        await expect(
            page.getByRole("button", { name: "Register" }),
        ).toBeVisible();
        // Check for the heading with flexible matching (localization replaces {0} with Bloom)
        await expect(
            page.getByText(/Please take a minute to register/),
        ).toBeVisible();
    });

    test("renders with test component helper", async ({ page }) => {
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

        // Verify it renders correctly
        await expect(
            page.getByRole("textbox", { name: "First Name" }),
        ).toBeVisible();
        await expect(
            page.getByRole("button", { name: "Register" }),
        ).toBeVisible();
        await expect(
            page.getByText(/Please take a minute to register/),
        ).toBeVisible();
    });
});
