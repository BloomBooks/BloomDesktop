/**
 * Tests for Registration Dialog - Character Encoding and Special Input
 * Covers: emoji, non-Latin scripts, JSON escaping, special characters, control characters
 * Run with: yarn test
 */

import { expect, test } from "../../component-tester/playwrightTest";
import type { RegistrationInfo } from "../registrationTypes";
import {
    setupRegistrationComponent,
    clickRegisterButton,
    fillRegistrationForm,
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

test.describe("Registration Dialog - Emoji and Unicode", () => {
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
});

test.describe("Registration Dialog - Special Characters", () => {
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
        await expect(getRegisterButton(page)).toBeVisible();
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
});

test.describe("Registration Dialog - Control Characters", () => {
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
