/**
 * Tests for Registration Dialog - Field Validation
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import type { RegistrationInfo } from "../registrationContents";
import { setupRegistrationComponent } from "./setup";

const emptyInfo: RegistrationInfo = {
    firstName: "",
    surname: "",
    email: "",
    organization: "",
    usingFor: "",
    hadEmailAlready: false,
};

test.describe("Registration Dialog - Field Validation - Required Fields", () => {
    test("All required fields accept valid input", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Test First Name
        const firstNameField = page.getByRole("textbox", {
            name: "First Name",
        });
        await firstNameField.fill("Alice");
        await expect(firstNameField).toHaveValue("Alice");
        await firstNameField.fill("Mary Jane");
        await expect(firstNameField).toHaveValue("Mary Jane");
        await firstNameField.fill("O'Brien-Smith");
        await expect(firstNameField).toHaveValue("O'Brien-Smith");

        // Test Surname
        const surnameField = page.getByRole("textbox", { name: "Surname" });
        await surnameField.fill("Smith");
        await expect(surnameField).toHaveValue("Smith");
        await surnameField.fill("Müller-O'Connor");
        await expect(surnameField).toHaveValue("Müller-O'Connor");

        // Test Organization
        const organizationField = page.getByRole("textbox", {
            name: "Organization",
        });
        await organizationField.fill("SIL International");
        await expect(organizationField).toHaveValue("SIL International");
        await organizationField.fill("SIL International (East Asia)");
        await expect(organizationField).toHaveValue(
            "SIL International (East Asia)",
        );

        // Test How are you using Bloom (accepts multiline)
        const usingForField = page.getByRole("textbox", {
            name: /How are you using|What will you|What are you/i,
        });
        const multilineText =
            "Creating materials\nFor literacy\nIn multiple languages";
        await usingForField.fill(multilineText);
        await expect(usingForField).toHaveValue(multilineText);
    });

    test("All required fields show error when empty", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

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

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        await expect(firstNameField).toHaveAttribute("aria-invalid", "true");
        await expect(surnameField).toHaveAttribute("aria-invalid", "true");
        await expect(organizationField).toHaveAttribute("aria-invalid", "true");
        await expect(usingForField).toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Edge Cases", () => {
    test("Very long text doesn't break layout", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const longText = "A".repeat(200);
        const field = page.getByRole("textbox", { name: "First Name" });

        await field.fill(longText);
        await expect(field).toHaveValue(longText);

        // Verify form is still visible
        await expect(
            page.getByRole("button", { name: "Register" }),
        ).toBeVisible();
    });

    test("Whitespace-only is invalid", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "First Name" });
        await field.fill("   ");

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Accessibility", () => {
    test("All fields have proper labels", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // All fields should be accessible by their labels
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
});
