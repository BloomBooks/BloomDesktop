/**
 * Tests for Registration Dialog - Initial Rendering & Layout
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import type { Page } from "@playwright/test";
import {
    kInactivitySecondsBeforeShowingOptOut,
    type IRegistrationContentsProps,
    type RegistrationInfo,
} from "../registrationTypes";
import { setupRegistrationComponent } from "./setup";

const defaultProps: IRegistrationContentsProps = {
    initialInfo: {
        firstName: "John",
        surname: "Doe",
        email: "john.doe@example.com",
        organization: "SIL International",
        usingFor: "Creating literacy materials",
        hadEmailAlready: false,
    },
    mayChangeEmail: true,
    emailRequiredForTeamCollection: false,
    onSubmit: (updated) => console.log("Submitted:", updated),
};

async function waitForAndClickOptOutButton(page: Page) {
    await page.waitForTimeout(
        kInactivitySecondsBeforeShowingOptOut * 1000 + 1000,
    );
    const optOutButton = page.getByRole("button", {
        name: /stuck.*later/i,
    });
    await expect(optOutButton).toBeVisible();
    await optOutButton.click();
    await page.waitForTimeout(500);
}

test.describe("Registration Dialog - Initial Rendering & Layout", () => {
    test("Dialog renders correctly with all elements", async ({ page }) => {
        await setupRegistrationComponent(page, defaultProps);

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
    });

    test("Email Required mode displays correctly", async ({ page }) => {
        await setupRegistrationComponent(page, {
            ...defaultProps,
            emailRequiredForTeamCollection: true,
        });

        // Verify team collection warning message is displayed
        await expect(
            page.getByText(/team collection|requires.*email/i),
        ).toBeVisible();
    });

    test('"I\'m stuck" button appears after 10 seconds', async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: defaultProps.initialInfo,
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

    test("Opt-out button submits form with valid data", async ({ page }) => {
        let submittedData: RegistrationInfo | undefined = undefined;

        await setupRegistrationComponent(page, {
            initialInfo: defaultProps.initialInfo,
            onSubmit: (data) => {
                submittedData = data;
            },
        });

        await waitForAndClickOptOutButton(page);

        // Should submit current data
        expect(submittedData).toBeDefined();
        expect(submittedData!.firstName).toBe("John");
        expect(submittedData!.email).toBe("john.doe@example.com");
    });

    test("Opt-out button clears invalid email before submitting", async ({
        page,
    }) => {
        let submittedData: RegistrationInfo | undefined = undefined;

        await setupRegistrationComponent(page, {
            initialInfo: {
                ...defaultProps.initialInfo,
                email: "invalid-email",
            },
            onSubmit: (data) => {
                submittedData = data;
            },
        });

        await waitForAndClickOptOutButton(page);

        // Should clear the invalid email
        expect(submittedData!.email).toBe("");
        expect(submittedData!.firstName).toBe("John");
    });

    test("Opt-out button preserves valid email", async ({ page }) => {
        let submittedData: RegistrationInfo | undefined = undefined;

        await setupRegistrationComponent(page, {
            initialInfo: defaultProps.initialInfo,
            onSubmit: (data) => {
                submittedData = data;
            },
        });

        await waitForAndClickOptOutButton(page);

        // Should keep the valid email
        expect(submittedData!.email).toBe("john.doe@example.com");
    });

    test("Opt-out button works even with empty required fields", async ({
        page,
    }) => {
        let submittedData: RegistrationInfo | undefined = undefined;

        await setupRegistrationComponent(page, {
            initialInfo: {
                firstName: "",
                surname: "",
                email: "",
                organization: "",
                usingFor: "",
                hadEmailAlready: false,
            },
            onSubmit: (data) => {
                submittedData = data;
            },
        });

        await waitForAndClickOptOutButton(page);

        // Should submit even with empty fields
        expect(submittedData).toBeDefined();
        expect(submittedData!.firstName).toBe("");
    });
});
