/**
 * Tests for Registration Dialog - Email Field Validation
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

const validInfo: RegistrationInfo = {
    firstName: "John",
    surname: "Doe",
    email: "",
    organization: "SIL International",
    usingFor: "Testing",
    hadEmailAlready: false,
};

test.describe("Registration Dialog - Email Field - Initial Load", () => {
    test("Email field loads empty without errors", async ({ page }) => {
        await setupRegistrationComponent(page, { initialInfo: emptyInfo });

        const emailField = page.getByRole("textbox", {
            name: "Email Address",
        });

        await expect(emailField).toHaveValue("");
        await expect(emailField).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Email field loads with pre-populated valid email", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                ...validInfo,
                email: "john.doe@example.com",
            },
        });

        const emailField = page.getByRole("textbox", {
            name: "Email Address",
        });

        await expect(emailField).toHaveValue("john.doe@example.com");
        await expect(emailField).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Email field shows error on load with invalid pre-populated email", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                ...validInfo,
                email: "invalid-email",
            },
        });

        const emailField = page.getByRole("textbox", {
            name: "Email Address",
        });

        await expect(emailField).toHaveValue("invalid-email");
        // Error should show immediately for pre-populated invalid email
        await expect(emailField).toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Email Field - Valid Formats", () => {
    test("Accepts various valid email formats", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        const validEmails = [
            "user@domain.com", // standard format
            "user+test@domain.com", // plus sign (for aliases)
            "user@mail.example.com", // subdomain
            "first.last@domain.com", // dots in username
            "user123@domain456.com", // numbers
            "user-name@my-domain.com", // hyphens
            "user_name@domain.com", // underscores
        ];

        for (const email of validEmails) {
            // Fill with invalid email first to ensure UI responds to valid input
            await field.fill("invalid");
            await page.keyboard.press("Tab");
            await page.waitForTimeout(200);

            // Now fill with valid email
            await field.fill(email);
            await page.keyboard.press("Tab");
            await page.waitForTimeout(200);
            await expect(field).toHaveValue(email);
            await expect(field).not.toHaveAttribute("aria-invalid", "true");
        }
    });
});

test.describe("Registration Dialog - Email Field - Invalid Formats", () => {
    test("Shows errors for various invalid email formats", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        const invalidEmails = [
            "notanemail", // without @ symbol
            "user@", // missing domain
            "@domain.com", // missing username
            "user name@domain.com", // with spaces
            "user@@domain.com", // double @
            "user@domain", // missing TLD
        ];

        for (const email of invalidEmails) {
            // Fill with valid email first to ensure UI responds to invalid input
            await field.fill("valid@example.com");
            await page.keyboard.press("Tab");
            await page.waitForTimeout(200);

            // Now fill with invalid email
            await field.fill(email);
            await page.keyboard.press("Tab");
            await page.waitForTimeout(200);
            await expect(field).toHaveValue(email);
            await expect(field).toHaveAttribute("aria-invalid", "true");
        }
    });
});

test.describe("Registration Dialog - Email Field - Typing Behavior", () => {
    test("Error clears when typing valid email after invalid", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });
        const registerButton = page.getByRole("button", { name: "Register" });

        // Enter invalid email and trigger validation
        await field.fill("invalid");
        await registerButton.click();
        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");

        // Now type a valid email
        await field.fill("valid@email.com");
        await page.waitForTimeout(500);
        // Error should clear
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Can clear email field", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                ...validInfo,
                email: "test@example.com",
            },
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await expect(field).toHaveValue("test@example.com");

        await field.clear();
        await expect(field).toHaveValue("");
        // Empty email is valid in optional mode
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Can modify existing email", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                ...validInfo,
                email: "old@example.com",
            },
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await expect(field).toHaveValue("old@example.com");

        await field.fill("new@example.com");
        await expect(field).toHaveValue("new@example.com");
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Email Field - Optional vs Required", () => {
    test("Email is optional in normal mode", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: validInfo,
            emailRequiredForTeamCollection: false,
        });

        const emailField = page.getByRole("textbox", {
            name: "Email Address",
        });
        await emailField.clear();

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        // Email should NOT show error when it's optional
        await expect(emailField).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Empty email shows error when required for team collection", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: validInfo,
            emailRequiredForTeamCollection: true,
        });

        const emailField = page.getByRole("textbox", {
            name: "Email Address",
        });
        await emailField.clear();

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        // Email should show error when required
        await expect(emailField).toHaveAttribute("aria-invalid", "true");
    });

    test("Valid email accepted when required for team collection", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: validInfo,
            emailRequiredForTeamCollection: true,
        });

        const emailField = page.getByRole("textbox", {
            name: "Email Address",
        });
        await emailField.fill("required@example.com");

        const registerButton = page.getByRole("button", { name: "Register" });
        await registerButton.click();

        await page.waitForTimeout(500);
        // Valid email should not show error even when required
        await expect(emailField).not.toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Email Field - Edge Cases", () => {
    test("Handles very long email addresses", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        const longEmail =
            "very.long.username.with.many.dots@subdomain.example.com";
        await field.fill(longEmail);
        await expect(field).toHaveValue(longEmail);
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Handles email with just whitespace", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });
        const registerButton = page.getByRole("button", { name: "Register" });

        await field.fill("   ");
        await registerButton.click();
        await page.waitForTimeout(500);

        // Whitespace-only should be treated as empty (valid in optional mode)
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Trims whitespace from email before validation", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        // Email with leading/trailing spaces should still be valid
        await field.fill("  user@example.com  ");
        await page.waitForTimeout(500);
        // Should not show error as trimmed value is valid
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });
});
