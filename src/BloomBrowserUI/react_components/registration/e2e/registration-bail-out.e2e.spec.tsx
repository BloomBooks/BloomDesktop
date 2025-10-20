/**
 * Tests for Registration Dialog - Initial Rendering & Layout
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import { setTestComponent } from "../../component-tester/setTestComponent";
import type { RegistrationContentsProps } from "../registrationContents";

const defaultProps: RegistrationContentsProps = {
    info: {
        firstName: "John",
        surname: "Doe",
        email: "john.doe@example.com",
        organization: "SIL International",
        usingFor: "Creating literacy materials",
        hadEmailAlready: false,
    },
    onInfoChange: () => {},
    mayChangeEmail: true,
    emailRequiredForTeamCollection: false,
    onSubmit: (updated) => console.log("Submitted:", updated),
    onOptOut: (updated) => console.log("Opted out:", updated),
};

test.describe("Registration Dialog - Initial Rendering & Layout", () => {
    test("Dialog renders correctly with all elements", async ({ page }) => {
        await setTestComponent<RegistrationContentsProps>(
            page,
            "RegistrationContents",
            defaultProps,
        );

        // Verify heading
        await expect(
            page.getByText(/Please take a minute to register/),
        ).toBeVisible();

        // Verify all form fields are present
        await expect(
            page.getByRole("textbox", { name: "First Name" }),
        ).toBeVisible();
        await expect(
            page.getByRole("textbox", { name: "Surname" }),
        ).toBeVisible();
        await expect(
            page.getByRole("textbox", { name: "Email Address" }),
        ).toBeVisible();
        await expect(
            page.getByRole("textbox", { name: "Organization" }),
        ).toBeVisible();
        await expect(
            page.getByRole("textbox", {
                name: /How are you using|What will you|What are you/i,
            }),
        ).toBeVisible();

        // Verify Register button is present
        await expect(
            page.getByRole("button", { name: "Register" }),
        ).toBeVisible();
    });

    test("Email Required mode displays correctly", async ({ page }) => {
        await setTestComponent<RegistrationContentsProps>(
            page,
            "RegistrationContents",
            {
                ...defaultProps,
                emailRequiredForTeamCollection: true,
            },
        );

        // Verify team collection warning message is displayed
        await expect(
            page.getByText(/team collection|requires.*email/i),
        ).toBeVisible();

        // Verify Register button is still present
        await expect(
            page.getByRole("button", { name: "Register" }),
        ).toBeVisible();
    });

    test('"I\'m stuck" button appears after 10 seconds', async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: defaultProps.info,
        });

        // Verify "I'm stuck" button is NOT visible initially
        const optOutButton = page.getByRole("button", {
            name: /stuck.*later/i,
        });
        await expect(optOutButton).not.toBeVisible();

        // Wait 11 seconds and verify it appears
        await page.waitForTimeout(11000);
        await expect(optOutButton).toBeVisible();
    });
});
