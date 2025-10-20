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

test.describe("Registration Dialog - Field Validation - First Name", () => {
    test("First Name accepts valid input", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "First Name" });

        // Test simple name
        await field.fill("Alice");
        await expect(field).toHaveValue("Alice");

        // Test name with space
        await field.fill("Mary Jane");
        await expect(field).toHaveValue("Mary Jane");

        // Test name with special characters
        await field.fill("O'Brien-Smith");
        await expect(field).toHaveValue("O'Brien-Smith");
    });

    test("First Name shows error when empty", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "First Name" });
        await field.clear();

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Field Validation - Surname", () => {
    test("Surname accepts valid input", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Surname" });

        await field.fill("Smith");
        await expect(field).toHaveValue("Smith");

        await field.fill("Müller-O'Connor");
        await expect(field).toHaveValue("Müller-O'Connor");
    });

    test("Surname shows error when empty", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Surname" });
        await field.clear();

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Field Validation - Organization", () => {
    test("Organization accepts valid input", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Organization" });

        await field.fill("SIL International");
        await expect(field).toHaveValue("SIL International");

        await field.fill("SIL International (East Asia)");
        await expect(field).toHaveValue("SIL International (East Asia)");
    });

    test("Organization shows error when empty", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Organization" });
        await field.clear();

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Field Validation - How are you using Bloom", () => {
    test("Accepts multiline text", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", {
            name: /How are you using|What will you|What are you/i,
        });

        const multilineText =
            "Creating materials\nFor literacy\nIn multiple languages";
        await field.fill(multilineText);
        await expect(field).toHaveValue(multilineText);
    });

    test("Shows error when empty", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", {
            name: /How are you using|What will you|What are you/i,
        });
        await field.clear();

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
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
