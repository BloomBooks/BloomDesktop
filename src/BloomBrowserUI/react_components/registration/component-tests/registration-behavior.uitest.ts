/**
 * Tests for Registration Dialog - Form Submission, Focus, and Pre-population
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import type { RegistrationInfo } from "../registrationTypes";
import { setupRegistrationComponent, clickRegisterButton } from "./common";

const emptyInfo: RegistrationInfo = {
    firstName: "",
    surname: "",
    email: "",
    organization: "",
    usingFor: "",
    hadEmailAlready: false,
};

test.describe("Registration Dialog - Form Submission", () => {
    test("Dialog does not close with invalid data and shows all field errors", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Clear all required fields to ensure they're empty
        const firstNameField = page.getByRole("textbox", {
            name: "First Name",
        });
        const surnameField = page.getByRole("textbox", { name: "Surname" });
        const organizationField = page.getByRole("textbox", {
            name: "Organization",
        });
        const usingForField = page.getByRole("textbox", {
            name: /How are you using|What will you|What are you/i,
        });

        await firstNameField.clear();
        await surnameField.clear();
        await organizationField.clear();
        await usingForField.clear();

        await clickRegisterButton(page);

        await page.waitForTimeout(1000);

        // Verify the form is still visible (not closed)
        const registerButton = page.getByRole("button", { name: "Register" });
        await expect(registerButton).toBeVisible();
        await expect(firstNameField).toBeVisible();

        // Verify all 4 required fields show errors
        await expect(firstNameField).toHaveAttribute("aria-invalid", "true");
        await expect(surnameField).toHaveAttribute("aria-invalid", "true");
        await expect(organizationField).toHaveAttribute("aria-invalid", "true");
        await expect(usingForField).toHaveAttribute("aria-invalid", "true");
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
        const firstNameField = page.getByRole("textbox", {
            name: "First Name",
        });
        await expect(firstNameField).toBeFocused();
    });

    test("Tab moves through fields in correct order", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await page.waitForTimeout(500);

        // Start at First Name (should be focused)
        const firstNameField = page.getByRole("textbox", {
            name: "First Name",
        });
        await firstNameField.focus();
        await expect(firstNameField).toBeFocused();

        // Press Tab -> Surname
        await page.keyboard.press("Tab");
        const surnameField = page.getByRole("textbox", { name: "Surname" });
        await expect(surnameField).toBeFocused();

        // Press Tab -> Email
        await page.keyboard.press("Tab");
        const emailField = page.getByRole("textbox", { name: "Email Address" });
        await expect(emailField).toBeFocused();

        // Press Tab -> Organization
        await page.keyboard.press("Tab");
        const organizationField = page.getByRole("textbox", {
            name: "Organization",
        });
        await expect(organizationField).toBeFocused();

        // Press Tab -> How are you using
        await page.keyboard.press("Tab");
        const usingForField = page.getByRole("textbox", {
            name: /How are you using|What will you|What are you/i,
        });
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
        const firstNameField = page.getByRole("textbox", {
            name: "First Name",
        });
        await expect(firstNameField).toHaveValue("John");

        const surnameField = page.getByRole("textbox", { name: "Surname" });
        await expect(surnameField).toHaveValue("Doe");

        const emailField = page.getByRole("textbox", { name: "Email Address" });
        await expect(emailField).toHaveValue("john@example.com");

        const organizationField = page.getByRole("textbox", {
            name: "Organization",
        });
        await expect(organizationField).toHaveValue("SIL International");

        const usingForField = page.getByRole("textbox", {
            name: /How are you using|What will you|What are you/i,
        });
        await expect(usingForField).toHaveValue("Creating literacy materials");

        // Verify no fields show error states initially
        await expect(firstNameField).not.toHaveAttribute(
            "aria-invalid",
            "true",
        );
        await expect(surnameField).not.toHaveAttribute("aria-invalid", "true");
        await expect(emailField).not.toHaveAttribute("aria-invalid", "true");
        await expect(organizationField).not.toHaveAttribute(
            "aria-invalid",
            "true",
        );
        await expect(usingForField).not.toHaveAttribute("aria-invalid", "true");
    });
});
