/**
 * Tests for Registration Dialog - User Interaction and Navigation
 * Covers: form submission, focus management, keyboard navigation, accessibility
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import type { RegistrationInfo } from "../registrationTypes";
import {
    setupRegistrationComponent,
    clickRegisterButton,
    field,
    getRegisterButton,
} from "./test-helpers";

const emptyInfo: RegistrationInfo = {
    firstName: "",
    surname: "",
    email: "",
    organization: "",
    usingFor: "",
    hadEmailAlready: false,
};

test.describe("Registration Dialog - Form Submission", () => {
    test("If all fields are empty, the register button does not submit", async ({
        page,
    }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await clickRegisterButton(page);

        // clicking the register button with any invalid fields should not send the POST
        expect(receiver.wasCalled()).toBe(false);
    });

    test("If all fields are empty, required fields are marked as needing attention", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await clickRegisterButton(page);

        // what are the required fields? Make sure each shows error state
        expect(await field.firstName.markedInvalid).toBe(true);
        expect(await field.surname.markedInvalid).toBe(true);
        // Email is optional by default, so it should NOT be marked as invalid when empty
        expect(await field.email.markedInvalid).toBe(false);
        expect(await field.organization.markedInvalid).toBe(true);
        expect(await field.usingFor.markedInvalid).toBe(true);
    });
});

test.describe("Registration Dialog - Focus Management", () => {
    test("First Name has initial focus", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                firstName: "John",
                surname: "Doe",
                email: "john@example.com",
                organization: "SIL",
                usingFor: "Testing",
                hadEmailAlready: false,
            },
        });

        // Wait for the field to receive focus
        const firstNameField = await field.firstName.getElement();
        await expect(firstNameField).toBeFocused();
    });
});

test.describe("Registration Dialog - Keyboard Navigation", () => {
    test("Tab moves through fields in correct order", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Start at First Name (should be focused)
        const firstNameField = await field.firstName.getElement();
        await firstNameField.focus();
        await expect(firstNameField).toBeFocused();

        // Press Tab -> Surname
        await page.keyboard.press("Tab");
        const surnameField = await field.surname.getElement();
        await expect(surnameField).toBeFocused();

        // Press Tab -> Email
        await page.keyboard.press("Tab");
        const emailField = await field.email.getElement();
        await expect(emailField).toBeFocused();

        // Press Tab -> Organization
        await page.keyboard.press("Tab");
        const organizationField = await field.organization.getElement();
        await expect(organizationField).toBeFocused();

        // Press Tab -> How are you using
        await page.keyboard.press("Tab");
        const usingForField = await field.usingFor.getElement();
        await expect(usingForField).toBeFocused();
    });
});

test.describe("Registration Dialog - Data Pre-population", () => {
    test("Pre-populated fields show no errors", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                firstName: "John",
                surname: "Doe",
                email: "john@example.com",
                organization: "SIL International",
                usingFor: "Creating literacy materials",
                hadEmailAlready: false,
            },
        });

        // Check that fields have pre-populated values
        const firstNameField = await field.firstName.getElement();
        await expect(firstNameField).toHaveValue("John");

        expect(await field.surname.getValue()).toBe("Doe");

        const emailField = await field.email.getElement();
        await expect(emailField).toHaveValue("john@example.com");

        const organizationField = await field.organization.getElement();
        await expect(organizationField).toHaveValue("SIL International");

        const usingForField = await field.usingFor.getElement();
        await expect(usingForField).toHaveValue("Creating literacy materials");

        // Verify no fields show error states initially
        expect(await field.firstName.markedInvalid).toBe(false);
        expect(await field.surname.markedInvalid).toBe(false);
        expect(await field.email.markedInvalid).toBe(false);
        expect(await field.organization.markedInvalid).toBe(false);
        expect(await field.usingFor.markedInvalid).toBe(false);
    });
});

test.describe("Registration Dialog - Accessibility", () => {
    test("All fields have proper labels", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // All fields should be accessible by their labels
        await expect(await field.firstName.getElement()).toBeVisible();
        await expect(await field.surname.getElement()).toBeVisible();
        await expect(await field.email.getElement()).toBeVisible();
        await expect(await field.organization.getElement()).toBeVisible();
        await expect(await field.usingFor.getElement()).toBeVisible();
    });

    test("Very long text doesn't break layout", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const longText = "A".repeat(200);
        const field2 = await field.firstName.getElement();

        await field2.fill(longText);
        await expect(field2).toHaveValue(longText);

        // Verify form is still visible
        await expect(getRegisterButton(page)).toBeVisible();
    });
});
