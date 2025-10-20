/**
 * Tests for Registration Dialog - Email Field Validation
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import { setTestComponent } from "../../component-tester/setTestComponent";
import type { RegistrationInfo } from "../registrationContents";

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
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const emailField = page.getByRole("textbox", {
            name: "Email Address",
        });

        await expect(emailField).toHaveValue("");
        await expect(emailField).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Email field loads with pre-populated valid email", async ({
        page,
    }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
    test("Accepts standard email format", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await field.fill("user@domain.com");
        await expect(field).toHaveValue("user@domain.com");
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Accepts email with plus sign (for aliases)", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await field.fill("user+test@domain.com");
        await expect(field).toHaveValue("user+test@domain.com");
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Accepts email with subdomain", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await field.fill("user@mail.example.com");
        await expect(field).toHaveValue("user@mail.example.com");
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Accepts email with dots in username", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await field.fill("first.last@domain.com");
        await expect(field).toHaveValue("first.last@domain.com");
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Accepts email with numbers", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await field.fill("user123@domain456.com");
        await expect(field).toHaveValue("user123@domain456.com");
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Accepts email with hyphens", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await field.fill("user-name@my-domain.com");
        await expect(field).toHaveValue("user-name@my-domain.com");
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });

    test("Accepts email with underscores", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });

        await field.fill("user_name@domain.com");
        await expect(field).toHaveValue("user_name@domain.com");
        await expect(field).not.toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Email Field - Invalid Formats", () => {
    test("Shows error for email without @ symbol", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });
        const registerButton = page.getByRole("button", { name: "Register" });

        await field.fill("notanemail");
        await expect(field).toHaveValue("notanemail");
        await registerButton.click();
        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });

    test("Shows error for email missing domain", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });
        const registerButton = page.getByRole("button", { name: "Register" });

        await field.fill("user@");
        await expect(field).toHaveValue("user@");
        await registerButton.click();
        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });

    test("Shows error for email missing username", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });
        const registerButton = page.getByRole("button", { name: "Register" });

        await field.fill("@domain.com");
        await expect(field).toHaveValue("@domain.com");
        await registerButton.click();
        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });

    test("Shows error for email with spaces", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });
        const registerButton = page.getByRole("button", { name: "Register" });

        await field.fill("user name@domain.com");
        await expect(field).toHaveValue("user name@domain.com");
        await registerButton.click();
        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });

    test("Shows error for email with double @", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });
        const registerButton = page.getByRole("button", { name: "Register" });

        await field.fill("user@@domain.com");
        await expect(field).toHaveValue("user@@domain.com");
        await registerButton.click();
        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });

    test("Shows error for email missing TLD", async ({ page }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: emptyInfo,
        });

        const field = page.getByRole("textbox", { name: "Email Address" });
        const registerButton = page.getByRole("button", { name: "Register" });

        await field.fill("user@domain");
        await expect(field).toHaveValue("user@domain");
        await registerButton.click();
        await page.waitForTimeout(500);
        await expect(field).toHaveAttribute("aria-invalid", "true");
    });
});

test.describe("Registration Dialog - Email Field - Typing Behavior", () => {
    test("Error clears when typing valid email after invalid", async ({
        page,
    }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
        await setTestComponent(page, "StatefulRegistrationContents", {
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
