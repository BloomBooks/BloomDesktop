/**
 * Tests for Registration Dialog - Field Validation
 * Covers: required fields, input formats, length limits, whitespace handling
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import type { RegistrationInfo } from "../registrationTypes";
import {
    setupRegistrationComponent,
    clickRegisterButton,
    fillRegistrationForm,
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

test.describe("Registration Dialog - Required Fields Validation", () => {
    test("All required fields accept valid input", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        // Test First Name
        const firstNameField = await field.firstName.getElement();
        await firstNameField.fill("Alice");
        await expect(firstNameField).toHaveValue("Alice");
        await firstNameField.fill("Mary Jane");
        await expect(firstNameField).toHaveValue("Mary Jane");
        await firstNameField.fill("O'Brien-Smith");
        await expect(firstNameField).toHaveValue("O'Brien-Smith");

        // Test Surname
        const surnameField = await field.surname.getElement();
        await surnameField.fill("Smith");
        await expect(surnameField).toHaveValue("Smith");
        await surnameField.fill("Müller-O'Connor");
        await expect(surnameField).toHaveValue("Müller-O'Connor");

        // Test Organization
        const organizationField = await field.organization.getElement();
        await organizationField.fill("SIL International");
        await expect(organizationField).toHaveValue("SIL International");
        await organizationField.fill("SIL International (East Asia)");
        await expect(organizationField).toHaveValue(
            "SIL International (East Asia)",
        );

        // Test How are you using Bloom (accepts multiline)
        const usingForField = await field.usingFor.getElement();
        const multilineText =
            "Creating materials\nFor literacy\nIn multiple languages";
        await usingForField.fill(multilineText);
        await expect(usingForField).toHaveValue(multilineText);
    });

    test("All required fields show error when empty", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await field.firstName.clear();
        await field.surname.clear();
        await field.organization.clear();
        await field.usingFor.clear();

        await clickRegisterButton(page);

        expect(await field.firstName.markedInvalid).toBe(true);
        expect(await field.surname.markedInvalid).toBe(true);
        expect(await field.organization.markedInvalid).toBe(true);
        expect(await field.usingFor.markedInvalid).toBe(true);
    });

    test("Whitespace-only is invalid", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        const field2 = await field.firstName.getElement();
        await field2.fill("   ");

        await clickRegisterButton(page);

        expect(await field.firstName.markedInvalid).toBe(true);
    });
});

test.describe("Registration Dialog - Whitespace Trimming", () => {
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

test.describe("Registration Dialog - Single-Line vs Multiline Fields", () => {
    test("Handles pasted text with newlines in First Name", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: {
                ...emptyInfo,
                email: "test@example.com",
            },
        });

        // Simulate paste with newlines
        await field.firstName.fill("John\nDoe");

        // Material-UI TextField should convert newlines to spaces or strip them
        // Either the newline is converted to space or stripped - either is acceptable
        expect(await field.firstName.getValue()).not.toContain("\n");
    });

    test("Handles pasted text with newlines in Email", async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await field.email.fill("john@\nexample.com");

        expect(await field.email.getValue()).not.toContain("\n");
    });

    test("Handles pasted text with newlines in Organization", async ({
        page,
    }) => {
        await setupRegistrationComponent(page, {
            initialInfo: emptyInfo,
        });

        await field.organization.fill("SIL\nInternational");

        expect(await field.organization.getValue()).not.toContain("\n");
    });
});

test.describe("Registration Dialog - Multiline Field Handling", () => {
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
