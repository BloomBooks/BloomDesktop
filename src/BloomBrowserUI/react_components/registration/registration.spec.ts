import { test, expect } from "@playwright/test";

// Base URL for Storybook
const STORYBOOK_BASE_URL = "http://localhost:58886";

// Story URLs for Registration Dialog
const NORMAL_STORY_URL = `${STORYBOOK_BASE_URL}/iframe.html?id=misc-dialogs-registrationdialog--normal-story&viewMode=story`;
const EMAIL_REQUIRED_STORY_URL = `${STORYBOOK_BASE_URL}/iframe.html?id=misc-dialogs-registrationdialog--email-required-story&viewMode=story`;

test.describe("Registration Dialog", () => {
    test("seed - Navigate to Normal Dialog story", async ({ page }) => {
        // Navigate to the Storybook story for the normal registration dialog
        await page.goto(NORMAL_STORY_URL);

        // Wait for the iframe to load
        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );

        // Wait longer for story to load
        await page.waitForTimeout(2000);

        // Verify the dialog is visible
        await expect(
            frame.getByRole("heading", { name: "Register Bloom", level: 1 }),
        ).toBeVisible({ timeout: 10000 });

        // Verify all form fields are present
        await expect(frame.getByLabel("First Name")).toBeVisible();
        await expect(frame.getByLabel("Surname")).toBeVisible();
        await expect(frame.getByLabel("Email Address")).toBeVisible();
        await expect(frame.getByLabel("Organization")).toBeVisible();
        await expect(frame.getByLabel(/How are you using/i)).toBeVisible();

        // Verify buttons
        await expect(
            frame.getByRole("button", { name: /REGISTER/i }),
        ).toBeVisible();
        await expect(
            frame.getByRole("button", { name: /CANCEL/i }),
        ).toBeVisible();
    });

    test("seed - Navigate to Email Required story", async ({ page }) => {
        // Navigate to the email required variant
        await page.goto(EMAIL_REQUIRED_STORY_URL);

        // Wait for the iframe to load
        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );

        // Wait longer for story to load
        await page.waitForTimeout(2000);

        // Verify the dialog is visible with team collection warning
        await expect(
            frame.getByRole("heading", { name: "Register Bloom", level: 1 }),
        ).toBeVisible({ timeout: 10000 });
        await expect(frame.getByText(/Team Collection/i)).toBeVisible();

        // Verify Cancel button is NOT present in required mode
        await expect(
            frame.getByRole("button", { name: /CANCEL/i }),
        ).not.toBeVisible();
    });
});
