/**
 * Tests for Registration Dialog - Field Validation
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import type { RegistrationInfo } from "../registrationTypes";
import {
    setupRegistrationComponent,
    clickRegisterButton,
    fillRegistrationForm,
    getMarkedInvalid,
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

test.describe("Registration Dialog - Field Validation - Required Fields", () => {
    test("All required fields accept valid input", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Test First Name
        const firstNameField = page.getByRole("textbox", {
            name: field.firstName,
        });
        await firstNameField.fill("Alice");
        await expect(firstNameField).toHaveValue("Alice");
        await firstNameField.fill("Mary Jane");
        await expect(firstNameField).toHaveValue("Mary Jane");
        await firstNameField.fill("O'Brien-Smith");
        await expect(firstNameField).toHaveValue("O'Brien-Smith");

        // Test Surname
        const surnameField = page.getByRole("textbox", { name: field.surname });
        await surnameField.fill("Smith");
        await expect(surnameField).toHaveValue("Smith");
        await surnameField.fill("Müller-O'Connor");
        await expect(surnameField).toHaveValue("Müller-O'Connor");

        // Test Organization
        const organizationField = page.getByRole("textbox", {
            name: field.organization,
        });
        await organizationField.fill("SIL International");
        await expect(organizationField).toHaveValue("SIL International");
        await organizationField.fill("SIL International (East Asia)");
        await expect(organizationField).toHaveValue(
            "SIL International (East Asia)",
        );

        // Test How are you using Bloom (accepts multiline)
        const usingForField = page.getByRole("textbox", {
            name: field.usingFor,
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
            name: field.firstName,
        });
        const surnameField = page.getByRole("textbox", { name: field.surname });
        const organizationField = page.getByRole("textbox", {
            name: field.organization,
        });
        const usingForField = page.getByRole("textbox", {
            name: field.usingFor,
        });

        await firstNameField.clear();
        await surnameField.clear();
        await organizationField.clear();
        await usingForField.clear();

        await clickRegisterButton(page);

        await page.waitForTimeout(500);
        expect(await getMarkedInvalid(page, field.firstName)).toBe(true);
        expect(await getMarkedInvalid(page, field.surname)).toBe(true);
        expect(await getMarkedInvalid(page, field.organization)).toBe(true);
        expect(await getMarkedInvalid(page, field.usingFor)).toBe(true);
    });
});

test.describe("Registration Dialog - Edge Cases", () => {
    test("Very long text doesn't break layout", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const longText = "A".repeat(200);
        const field2 = page.getByRole("textbox", { name: field.firstName });

        await field2.fill(longText);
        await expect(field2).toHaveValue(longText);

        // Verify form is still visible
        await expect(
            page.getByRole("button", { name: "Register" }),
        ).toBeVisible();
    });

    test("Whitespace-only is invalid", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field2 = page.getByRole("textbox", { name: field.firstName });
        await field2.fill("   ");

        await clickRegisterButton(page);

        await page.waitForTimeout(500);
        expect(await getMarkedInvalid(page, field.firstName)).toBe(true);
    });
});

test.describe("Registration Dialog - Accessibility", () => {
    test("All fields have proper labels", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // All fields should be accessible by their labels
        await expect(
            page.getByRole("textbox", { name: field.firstName }),
        ).toBeVisible();
        await expect(
            page.getByRole("textbox", { name: field.surname }),
        ).toBeVisible();
        await expect(
            page.getByRole("textbox", { name: field.email }),
        ).toBeVisible();
        await expect(
            page.getByRole("textbox", { name: field.organization }),
        ).toBeVisible();
        await expect(
            page.getByRole("textbox", {
                name: field.usingFor,
            }),
        ).toBeVisible();
    });
});

test.describe("Registration Dialog - Field Trimming", () => {
    test("Trims leading and trailing whitespace from all fields on submit", async ({
        page,
    }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Fill fields with leading/trailing whitespace
        await fillRegistrationForm(page, {
            firstName: "  John  ",
            surname: "  Doe  ",
            email: "  john@example.com  ",
            organization: "  SIL  ",
            usingFor: "  Testing  ",
        });

        await clickRegisterButton(page);

        // Wait for and verify submitted data is trimmed
        const submittedData = await receiver.getData();
        expect(submittedData.firstName).toBe("John");
        expect(submittedData.surname).toBe("Doe");
        expect(submittedData.email).toBe("john@example.com");
        expect(submittedData.organization).toBe("SIL");
        expect(submittedData.usingFor).toBe("Testing");
    });

    test("Preserves internal spaces in fields", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await fillRegistrationForm(page, {
            firstName: "Mary Jane",
            surname: "Van Der Berg",
            email: "test@example.com",
            organization: "SIL International",
            usingFor: "Creating materials",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        expect(submittedData.firstName).toBe("Mary Jane");
        expect(submittedData.surname).toBe("Van Der Berg");
        expect(submittedData.organization).toBe("SIL International");
    });

    test("Trims multiline field while preserving internal newlines", async ({
        page,
    }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await fillRegistrationForm(page, {
            firstName: "John",
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: "  \nCreating materials\nFor literacy\n  ",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // Should trim outer whitespace but keep internal newlines
        expect(submittedData.usingFor).toBe("Creating materials\nFor literacy");
    });
});

test.describe("Registration Dialog - Newlines in Single-Line Fields", () => {
    test("Handles pasted text with newlines in First Name", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                ...emptyInfo,
                email: "test@example.com",
            },
        });

        const fieldInput = page.getByRole("textbox", { name: field.firstName });
        // Simulate paste with newlines
        await fieldInput.fill("John\nDoe");

        // Material-UI TextField should convert newlines to spaces or strip them
        const value = await fieldInput.inputValue();
        // Either the newline is converted to space or stripped - either is acceptable
        expect(value).not.toContain("\n");
    });

    test("Handles pasted text with newlines in Email", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const fieldInput = page.getByRole("textbox", { name: field.email });
        await fieldInput.fill("john@\nexample.com");

        const value = await fieldInput.inputValue();
        expect(value).not.toContain("\n");
    });

    test("Handles pasted text with newlines in Organization", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const fieldInput = page.getByRole("textbox", {
            name: field.organization,
        });
        await fieldInput.fill("SIL\nInternational");

        const value = await fieldInput.inputValue();
        expect(value).not.toContain("\n");
    });
});

test.describe("Registration Dialog - Special Characters", () => {
    test("Accepts emoji in name fields", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await fillRegistrationForm(page, {
            firstName: "😊 John",
            surname: "Doe 👍",
            email: "test@example.com",
            organization: "SIL",
            usingFor: "Testing",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        expect(submittedData.firstName).toBe("😊 John");
        expect(submittedData.surname).toBe("Doe 👍");
    });

    test("Accepts non-Latin scripts in all fields", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Chinese
        // Cyrillic
        // Email still needs to be valid
        // Arabic
        await fillRegistrationForm(page, {
            firstName: "李",
            surname: "明",
            email: "test@example.com",
            organization: "Организация",
            usingFor: "اختبار",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        expect(submittedData.firstName).toBe("李");
        expect(submittedData.surname).toBe("明");
        expect(submittedData.organization).toBe("Организация");
        expect(submittedData.usingFor).toBe("اختبار");
    });

    test("Handles tab characters in text", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await fillRegistrationForm(page, {
            firstName: "John\t",
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: "Testing",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // Verify it doesn't break submission
        expect(submittedData).toBeDefined();
        expect(submittedData.firstName).toBeTruthy();
    });

    test("HTML/script tags are treated as plain text", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const maliciousInput = "<script>alert('xss')</script>";
        await fillRegistrationForm(page, {
            firstName: maliciousInput,
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: "Testing",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // Should be stored as plain text, not executed
        expect(submittedData.firstName).toBe(maliciousInput);

        // Verify no script was executed (page should still be functional)
        await expect(
            page.getByRole("button", { name: "Register" }),
        ).toBeVisible();
    });
});

test.describe("Registration Dialog - JSON Escaping", () => {
    test("Handles backslashes in text fields", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const textWithBackslashes = "C:\\Users\\John\\Documents";
        await fillRegistrationForm(page, {
            firstName: textWithBackslashes,
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: "Testing",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // Backslashes should be preserved
        expect(submittedData.firstName).toBe(textWithBackslashes);
    });

    test("Handles double quotes in text fields", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const textWithQuotes = 'John "Johnny" Doe';
        await fillRegistrationForm(page, {
            firstName: "Test",
            surname: "User",
            email: "test@example.com",
            organization: textWithQuotes,
            usingFor: "Testing",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // Double quotes should be preserved
        expect(submittedData.organization).toBe(textWithQuotes);
    });

    test("Handles single quotes and apostrophes", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await fillRegistrationForm(page, {
            firstName: "O'Brien",
            surname: "D'Angelo",
            email: "test@example.com",
            organization: "SIL",
            usingFor: "Testing 'special' characters",
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        expect(submittedData.firstName).toBe("O'Brien");
        expect(submittedData.surname).toBe("D'Angelo");
        expect(submittedData.usingFor).toBe("Testing 'special' characters");
    });

    test("Handles combination of special JSON characters", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const complexText = "Path: \"C:\\Program Files\\Test\"\nWith: 'quotes'";
        await fillRegistrationForm(page, {
            firstName: "John",
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: complexText,
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // All special characters should be preserved
        expect(submittedData.usingFor).toBe(complexText);
    });

    test("Handles null bytes and control characters", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Control characters: carriage return, tab, form feed
        const controlChars = "Line1\r\nLine2\tTabbed\fFormFeed";
        await fillRegistrationForm(page, {
            firstName: "John",
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: controlChars,
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // Control characters should be handled (may be normalized)
        expect(submittedData).toBeDefined();
        expect(submittedData.usingFor).toBeTruthy();
    });
});

test.describe("Registration Dialog - Multiline Field Edge Cases", () => {
    test("Handles excessive newlines in Using For field", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Many consecutive newlines
        const excessiveNewlines = "Line 1\n\n\n\n\n\n\n\nLine 2";
        await fillRegistrationForm(page, {
            firstName: "John",
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: excessiveNewlines,
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // Should preserve all newlines as entered
        expect(submittedData.usingFor).toBe(excessiveNewlines);
    });

    test("Handles very long single line in multiline field", async ({
        page,
    }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const longLine = "A".repeat(500);
        await fillRegistrationForm(page, {
            firstName: "John",
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: longLine,
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        expect(submittedData.usingFor).toBe(longLine);
    });

    test("Handles many short lines in multiline field", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const manyLines = Array(50)
            .fill("Line")
            .map((line, i) => `${line} ${i}`)
            .join("\n");

        await fillRegistrationForm(page, {
            firstName: "John",
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: manyLines,
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        expect(submittedData.usingFor).toBe(manyLines);
    });

    test("Handles mixed tabs and spaces in multiline field", async ({
        page,
    }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const mixedWhitespace = "Line 1\n\tIndented line\n    Also indented";
        await fillRegistrationForm(page, {
            firstName: "John",
            surname: "Doe",
            email: "test@example.com",
            organization: "SIL",
            usingFor: mixedWhitespace,
        });

        await clickRegisterButton(page);
        const submittedData = await receiver.getData();

        // Should preserve formatting
        expect(submittedData.usingFor).toBeTruthy();
        expect(submittedData.usingFor.includes("\t")).toBe(true);
    });
});
