/**
 * Tests for Registration Dialog - Form Submission, Focus, and Pre-population
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import type { RegistrationInfo } from "../registrationTypes";
import {
    setupRegistrationComponent,
    clickRegisterButton,
    field,
} from "./common";

const emptyInfo: RegistrationInfo = {
    firstName: "",
    surname: "",
    email: "",
    organization: "",
    usingFor: "",
    hadEmailAlready: false,
};

test.describe("Registration Dialog - register button behavior", () => {
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

test.describe("Registration Dialog - Field Focus & Tab Order", () => {
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

        // Wait for component to render and focus
        await page.waitForTimeout(1000);

        // Get the focused element
        const firstNameField = await field.firstName.getElement();
        await expect(firstNameField).toBeFocused();
    });

    test("Tab moves through fields in correct order", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await page.waitForTimeout(500);

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

        const surnameField = await field.surname.getElement();
        await expect(surnameField).toHaveValue("Doe");

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
