/**
 * Tests for Registration Dialog - Email Field Validation
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import type { RegistrationInfo } from "../registrationContents";
import {
    setupRegistrationComponent,
    clickRegisterButton,
    field,
} from "./test-helpers";

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

        const emailField = await field.email.getElement();

        await expect(emailField).toHaveValue("");
        expect(await field.email.markedInvalid).toBe(false);
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

        const emailField = await field.email.getElement();

        await expect(emailField).toHaveValue("john.doe@example.com");
        expect(await field.email.markedInvalid).toBe(false);
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

        const emailField = await field.email.getElement();

        await expect(emailField).toHaveValue("invalid-email");
        // Error should show immediately for pre-populated invalid email
        expect(await field.email.markedInvalid).toBe(true);
    });
});

test.describe("Registration Dialog - Email Field - Valid Formats", () => {
    test("Accepts various valid email formats", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const emailInput = await field.email.getElement();

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
            await emailInput.fill("invalid");
            await page.keyboard.press("Tab");

            // Now fill with valid email
            await emailInput.fill(email);
            await page.keyboard.press("Tab");
            await expect(emailInput).toHaveValue(email);
            expect(await field.email.markedInvalid).toBe(false);
        }
    });
});

test.describe("Registration Dialog - Email Field - Invalid Formats", () => {
    test("Shows errors for various invalid email formats", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const emailInput = await field.email.getElement();

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
            await emailInput.fill("valid@example.com");
            await page.keyboard.press("Tab");

            // Now fill with invalid email
            await emailInput.fill(email);
            await page.keyboard.press("Tab");
            await expect(emailInput).toHaveValue(email);
            expect(await field.email.markedInvalid).toBe(true);
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

        const emailInput = await field.email.getElement();

        // Enter invalid email and trigger validation
        await emailInput.fill("invalid");
        await clickRegisterButton(page);
        expect(await field.email.markedInvalid).toBe(true);

        // Now type a valid email
        await emailInput.fill("valid@email.com");
        // Error should clear immediately on valid input
        expect(await field.email.markedInvalid).toBe(false);
    });

    test("Can clear email field", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                ...validInfo,
                email: "test@example.com",
            },
        });

        const emailInput = await field.email.getElement();

        await expect(emailInput).toHaveValue("test@example.com");

        await field.email.clear();
        await expect(emailInput).toHaveValue("");
        // Empty email is valid in optional mode
        expect(await field.email.markedInvalid).toBe(false);
    });

    test("Can modify existing email", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                ...validInfo,
                email: "old@example.com",
            },
        });

        const emailInput = await field.email.getElement();

        await expect(emailInput).toHaveValue("old@example.com");

        await emailInput.fill("new@example.com");
        await expect(emailInput).toHaveValue("new@example.com");
        expect(await field.email.markedInvalid).toBe(false);
    });
});

test.describe("Registration Dialog - Email Field - Optional vs Required", () => {
    test("Email is optional in normal mode", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: validInfo,
            emailRequiredForTeamCollection: false,
        });

        await field.email.clear();

        await clickRegisterButton(page);

        // Email should NOT show error when it's optional
        expect(await field.email.markedInvalid).toBe(false);
    });

    test("Empty email is required when emailRequiredForTeamCollection is true", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: validInfo,
            emailRequiredForTeamCollection: true,
        });

        await field.email.clear();

        await clickRegisterButton(page);

        // Email should show error when required
        expect(await field.email.markedInvalid).toBe(true);
    });

    test("Valid email accepted when required for team collection", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: validInfo,
            emailRequiredForTeamCollection: true,
        });

        const emailField = await field.email.getElement();
        await emailField.fill("required@example.com");

        await clickRegisterButton(page);

        // Valid email should not show error even when required
        expect(await field.email.markedInvalid).toBe(false);
    });
});

test.describe("Registration Dialog - Email Field - Edge Cases", () => {
    test("Handles very long email addresses", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const emailInput = await field.email.getElement();

        const longEmail =
            "very.long.username.with.many.dots@subdomain.example.com";
        await emailInput.fill(longEmail);
        await expect(emailInput).toHaveValue(longEmail);
        expect(await field.email.markedInvalid).toBe(false);
    });

    test("Handles email with just whitespace", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const emailInput = await field.email.getElement();

        await emailInput.fill("   ");
        await clickRegisterButton(page);

        // Whitespace-only should be treated as empty (valid in optional mode)
        expect(await field.email.markedInvalid).toBe(false);
    });

    test("Trims whitespace from email before validation", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const emailInput = await field.email.getElement();

        // Email with leading/trailing spaces should still be valid
        await emailInput.fill("  user@example.com  ");
        // Should not show error as trimmed value is valid
        expect(await field.email.markedInvalid).toBe(false);
    });
});

test.describe("Registration Dialog - Email Case Sensitivity", () => {
    test("Preserves email capitalization on submit", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: validInfo,
        });

        await field.email.fill("John.Doe@Example.COM");

        await clickRegisterButton(page);
        const data = await receiver.getData();

        // Email should be preserved as-is (not lowercased)
        expect(data.email).toBe("John.Doe@Example.COM");
    });

    test("Validates email regardless of case", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: validInfo,
        });

        const emailField = await field.email.getElement();

        // All case variations should be valid
        const caseVariations = [
            "USER@EXAMPLE.COM",
            "user@example.com",
            "User@Example.Com",
            "uSeR@eXaMpLe.CoM",
        ];

        for (const email of caseVariations) {
            await emailField.fill(email);
            await page.keyboard.press("Tab");
            expect(await field.email.markedInvalid).toBe(false);
        }
    });

    test("Email Required mode displays correctly", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: validInfo,
            emailRequiredForTeamCollection: true,
        });

        // Verify team collection warning message is displayed
        await expect(
            page.getByText(/team collection|requires.*email/i),
        ).toBeVisible();
    });
});
