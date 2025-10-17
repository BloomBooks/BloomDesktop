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
import { setTestComponent } from "../../component-tester/setTestComponent";
import type { RegistrationContentsProps } from "../registrationContents";

test.describe("registration contents", () => {
    test("renders the registration form", async ({ page }) => {
        await page.goto("/");
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
        await setTestComponent<RegistrationContentsProps>(
            page,
            "RegistrationContents",
            {
                info: {
                    firstName: "",
                    surname: "",
                    email: "",
                    organization: "",
                    usingFor: "",
                    hadEmailAlready: false,
                },
                onInfoChange: () => {},
                mayChangeEmail: true,
                emailRequiredForTeamCollection: false,
                registrationIsOptional: true,
                showOptOut: true,
                onSubmit: (updated) => console.log("Submitted:", updated),
                onOptOut: (updated) => console.log("Opted out:", updated),
            },
        );

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
