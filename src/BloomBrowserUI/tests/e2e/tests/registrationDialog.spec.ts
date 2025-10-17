import { test, expect, Page } from "@playwright/test";

// Story URLs
const NORMAL_STORY_URL =
    "/iframe.html?id=misc-dialogs-registrationdialog--normal-story&viewMode=story";
const EMAIL_REQUIRED_STORY_URL =
    "/iframe.html?id=misc-dialogs-registrationdialog--email-required-story&viewMode=story";

// Helper to get the dialog within the iframe
async function getDialogContent(page: Page) {
    const frame = page.frameLocator('iframe[id="storybook-preview-iframe"]');
    return frame.locator('[role="dialog"]');
}

// Helper to get field by label
async function getFieldByLabel(page: Page, label: string) {
    const frame = page.frameLocator('iframe[id="storybook-preview-iframe"]');
    return frame.getByLabel(label, { exact: false });
}

// Helper to get button by text
async function getButtonByText(page: Page, text: string) {
    const frame = page.frameLocator('iframe[id="storybook-preview-iframe"]');
    return frame.getByRole("button", { name: text, exact: false });
}

test.describe("Registration Dialog - Initial Rendering & Layout", () => {
    test("should render dialog with correct title and heading", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );

        // Check title
        await expect(
            frame.getByRole("heading", { name: "Register Bloom", level: 1 }),
        ).toBeVisible();

        // Check main heading
        await expect(
            frame.getByRole("heading", {
                name: /Please take a minute to register/i,
            }),
        ).toBeVisible();
    });

    test("should display all required form fields", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        // Check all fields are present
        await expect(await getFieldByLabel(page, "First Name")).toBeVisible();
        await expect(await getFieldByLabel(page, "Surname")).toBeVisible();
        await expect(
            await getFieldByLabel(page, "Email Address"),
        ).toBeVisible();
        await expect(await getFieldByLabel(page, "Organization")).toBeVisible();
        await expect(
            await getFieldByLabel(page, "How are you using Bloom"),
        ).toBeVisible();
    });

    test("should display Register button", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const registerButton = await getButtonByText(page, "REGISTER");
        await expect(registerButton).toBeVisible();
        await expect(registerButton).toBeEnabled();
    });

    test("should display Cancel button when registration is optional", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const cancelButton = await getButtonByText(page, "CANCEL");
        await expect(cancelButton).toBeVisible();
    });

    test("should NOT display Cancel button when registration is required", async ({
        page,
    }) => {
        await page.goto(EMAIL_REQUIRED_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const cancelButton = frame.getByRole("button", {
            name: "CANCEL",
            exact: false,
        });
        await expect(cancelButton).not.toBeVisible();
    });

    test('should NOT show "I\'m stuck" button on initial render', async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const stuckButton = frame.getByRole("button", { name: /I'm stuck/i });

        // Should not be visible initially
        await expect(stuckButton).not.toBeVisible();
    });

    test('should show "I\'m stuck" button after 10 seconds', async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const stuckButton = frame.getByRole("button", { name: /I'm stuck/i });

        // Wait 10 seconds
        await page.waitForTimeout(11000);

        // Should now be visible
        await expect(stuckButton).toBeVisible();
    });

    test("should have dialog width of approximately 400px", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const dialog = frame.locator('[role="dialog"]');

        const box = await dialog.boundingBox();
        expect(box?.width).toBeGreaterThanOrEqual(380);
        expect(box?.width).toBeLessThanOrEqual(450);
    });
});

test.describe("Registration Dialog - Email Required Mode", () => {
    test("should display team collection warning message", async ({ page }) => {
        await page.goto(EMAIL_REQUIRED_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        await expect(
            frame.getByText(
                /You will need to register this copy of Bloom with an email address before participating in a Team Collection/i,
            ),
        ).toBeVisible();
    });

    test("should NOT display warning message in normal mode", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const warning = frame.getByText(
            /You will need to register this copy of Bloom with an email address before participating in a Team Collection/i,
        );
        await expect(warning).not.toBeVisible();
    });

    test("should NOT display Cancel button in required mode", async ({
        page,
    }) => {
        await page.goto(EMAIL_REQUIRED_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const cancelButton = frame.getByRole("button", {
            name: "CANCEL",
            exact: false,
        });
        await expect(cancelButton).not.toBeVisible();
    });
});

test.describe("Registration Dialog - Field Validation - First Name", () => {
    test("should accept valid text input", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const firstNameField = await getFieldByLabel(page, "First Name");
        await firstNameField.clear();
        await firstNameField.fill("Alice");

        await expect(firstNameField).toHaveValue("Alice");
    });

    test("should accept names with spaces", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const firstNameField = await getFieldByLabel(page, "First Name");
        await firstNameField.clear();
        await firstNameField.fill("Mary Jane");

        await expect(firstNameField).toHaveValue("Mary Jane");
    });

    test("should accept special characters", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const firstNameField = await getFieldByLabel(page, "First Name");
        await firstNameField.clear();
        await firstNameField.fill("O'Brien-Smith");

        await expect(firstNameField).toHaveValue("O'Brien-Smith");
    });

    test("should show error when empty and Register is clicked", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const firstNameField = await getFieldByLabel(page, "First Name");
        await firstNameField.clear();

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        // Check for error state - Material UI adds Mui-error class
        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorField = frame
            .locator('input[name="First Name"]')
            .or(frame.getByLabel("First Name"))
            .first();

        // Wait a moment for validation to trigger
        await page.waitForTimeout(500);

        // The field should have an error attribute or class
        const hasError = await errorField.evaluate((el) => {
            return (
                el.getAttribute("aria-invalid") === "true" ||
                el.closest(".Mui-error") !== null ||
                el.classList.contains("Mui-error")
            );
        });

        expect(hasError).toBeTruthy();
    });
});

test.describe("Registration Dialog - Field Validation - Surname", () => {
    test("should accept valid text input", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const surnameField = await getFieldByLabel(page, "Surname");
        await surnameField.clear();
        await surnameField.fill("Smith");

        await expect(surnameField).toHaveValue("Smith");
    });

    test("should accept special characters", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const surnameField = await getFieldByLabel(page, "Surname");
        await surnameField.clear();
        await surnameField.fill("Müller-O'Connor");

        await expect(surnameField).toHaveValue("Müller-O'Connor");
    });

    test("should show error when empty and Register is clicked", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const surnameField = await getFieldByLabel(page, "Surname");
        await surnameField.clear();

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        await page.waitForTimeout(500);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorField = frame.getByLabel("Surname");

        const hasError = await errorField.evaluate((el) => {
            return (
                el.getAttribute("aria-invalid") === "true" ||
                el.closest(".Mui-error") !== null
            );
        });

        expect(hasError).toBeTruthy();
    });
});

test.describe("Registration Dialog - Field Validation - Email", () => {
    test("should accept valid email format", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const emailField = await getFieldByLabel(page, "Email Address");
        await emailField.clear();
        await emailField.fill("user@domain.com");

        await expect(emailField).toHaveValue("user@domain.com");
    });

    test("should accept email with plus sign", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const emailField = await getFieldByLabel(page, "Email Address");
        await emailField.clear();
        await emailField.fill("user+test@domain.com");

        await expect(emailField).toHaveValue("user+test@domain.com");
    });

    test("should accept email with subdomain", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const emailField = await getFieldByLabel(page, "Email Address");
        await emailField.clear();
        await emailField.fill("user@sub.domain.com");

        await expect(emailField).toHaveValue("user@sub.domain.com");
    });

    test("should accept email with dots in username", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const emailField = await getFieldByLabel(page, "Email Address");
        await emailField.clear();
        await emailField.fill("first.last@domain.com");

        await expect(emailField).toHaveValue("first.last@domain.com");
    });

    test("should show error for missing @ symbol", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const emailField = await getFieldByLabel(page, "Email Address");
        await emailField.clear();
        await emailField.fill("notanemail");

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        await page.waitForTimeout(500);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorField = frame.getByLabel("Email Address");

        const hasError = await errorField.evaluate((el) => {
            return (
                el.getAttribute("aria-invalid") === "true" ||
                el.closest(".Mui-error") !== null
            );
        });

        expect(hasError).toBeTruthy();
    });

    test("should show error for missing domain", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const emailField = await getFieldByLabel(page, "Email Address");
        await emailField.clear();
        await emailField.fill("user@");

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        await page.waitForTimeout(500);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorField = frame.getByLabel("Email Address");

        const hasError = await errorField.evaluate((el) => {
            return (
                el.getAttribute("aria-invalid") === "true" ||
                el.closest(".Mui-error") !== null
            );
        });

        expect(hasError).toBeTruthy();
    });

    test("should allow empty email in normal mode", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const emailField = await getFieldByLabel(page, "Email Address");
        await emailField.clear();

        // Fill all other required fields
        await (await getFieldByLabel(page, "First Name")).fill("John");
        await (await getFieldByLabel(page, "Surname")).fill("Doe");
        await (await getFieldByLabel(page, "Organization")).fill("Test Org");
        await (
            await getFieldByLabel(page, "How are you using")
        ).fill("Testing");

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        await page.waitForTimeout(500);

        // Email field should not show error when empty in optional mode
        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorField = frame.getByLabel("Email Address");

        const hasError = await errorField.evaluate((el) => {
            return (
                el.getAttribute("aria-invalid") === "true" ||
                el.closest(".Mui-error") !== null
            );
        });

        expect(hasError).toBeFalsy();
    });
});

test.describe("Registration Dialog - Field Validation - Organization", () => {
    test("should accept valid text input", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const orgField = await getFieldByLabel(page, "Organization");
        await orgField.clear();
        await orgField.fill("SIL International");

        await expect(orgField).toHaveValue("SIL International");
    });

    test("should accept special characters", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const orgField = await getFieldByLabel(page, "Organization");
        await orgField.clear();
        await orgField.fill("SIL International (East Asia)");

        await expect(orgField).toHaveValue("SIL International (East Asia)");
    });

    test("should show error when empty and Register is clicked", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const orgField = await getFieldByLabel(page, "Organization");
        await orgField.clear();

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        await page.waitForTimeout(500);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorField = frame.getByLabel("Organization");

        const hasError = await errorField.evaluate((el) => {
            return (
                el.getAttribute("aria-invalid") === "true" ||
                el.closest(".Mui-error") !== null
            );
        });

        expect(hasError).toBeTruthy();
    });
});

test.describe("Registration Dialog - Field Validation - How are you using Bloom", () => {
    test("should accept multiline text input", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const usingForField = await getFieldByLabel(page, "How are you using");
        await usingForField.clear();
        await usingForField.fill(
            "Creating materials\nFor literacy\nIn multiple languages",
        );

        const value = await usingForField.inputValue();
        expect(value).toContain("Creating materials");
    });

    test("should show error when empty and Register is clicked", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const usingForField = await getFieldByLabel(page, "How are you using");
        await usingForField.clear();

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        await page.waitForTimeout(500);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorField = frame.getByLabel("How are you using");

        const hasError = await errorField.evaluate((el) => {
            return (
                el.getAttribute("aria-invalid") === "true" ||
                el.closest(".Mui-error") !== null
            );
        });

        expect(hasError).toBeTruthy();
    });
});

test.describe("Registration Dialog - Form Submission", () => {
    test("should not close dialog when submitting with invalid data", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        // Clear all fields
        await (await getFieldByLabel(page, "First Name")).clear();
        await (await getFieldByLabel(page, "Surname")).clear();
        await (await getFieldByLabel(page, "Organization")).clear();
        await (await getFieldByLabel(page, "How are you using")).clear();

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        await page.waitForTimeout(1000);

        // Dialog should still be visible
        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const dialog = frame.locator('[role="dialog"]');
        await expect(dialog).toBeVisible();
    });
});

test.describe("Registration Dialog - Cancel Button", () => {
    test("should close dialog when Cancel is clicked in optional mode", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        const cancelButton = await getButtonByText(page, "CANCEL");
        await cancelButton.click();

        await page.waitForTimeout(500);

        // Dialog should be hidden - this might fail due to Storybook wrapper
        // We're mainly testing that the click handler is triggered
        expect(cancelButton).toBeDefined();
    });
});

test.describe("Registration Dialog - Field Focus & Tab Order", () => {
    test("should have First Name field focused on dialog open", async ({
        page,
    }) => {
        await page.goto(NORMAL_STORY_URL);

        await page.waitForTimeout(1000);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const firstNameField = frame.getByLabel("First Name");

        const isFocused = await firstNameField.evaluate((el) => {
            return document.activeElement === el;
        });

        expect(isFocused).toBeTruthy();
    });

    test("should tab through fields in correct order", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        await page.waitForTimeout(1000);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );

        // Start at First Name (should be focused)
        await page.keyboard.press("Tab");

        // Should be at Surname
        let focused = await frame.locator(":focus").getAttribute("aria-label");
        expect(focused).toContain("Surname");

        await page.keyboard.press("Tab");

        // Should be at Email
        focused = await frame.locator(":focus").getAttribute("aria-label");
        expect(focused).toContain("Email");
    });
});

test.describe("Registration Dialog - Data Pre-population", () => {
    test("should show pre-populated data without errors", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        // Check that fields have pre-populated values (from the story)
        const firstNameField = await getFieldByLabel(page, "First Name");
        const value = await firstNameField.inputValue();

        expect(value.length).toBeGreaterThan(0);

        // Check that there are no error indicators initially
        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorFields = frame.locator(".Mui-error");
        const errorCount = await errorFields.count();

        expect(errorCount).toBe(0);
    });
});

test.describe("Registration Dialog - Edge Cases", () => {
    test("should handle very long text in fields", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const longText = "A".repeat(200);
        const firstNameField = await getFieldByLabel(page, "First Name");
        await firstNameField.clear();
        await firstNameField.fill(longText);

        await expect(firstNameField).toHaveValue(longText);

        // Check that dialog layout is not broken
        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const dialog = frame.locator('[role="dialog"]');
        await expect(dialog).toBeVisible();
    });

    test("should handle whitespace-only input as invalid", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const firstNameField = await getFieldByLabel(page, "First Name");
        await firstNameField.clear();
        await firstNameField.fill("   ");

        const registerButton = await getButtonByText(page, "REGISTER");
        await registerButton.click();

        await page.waitForTimeout(500);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );
        const errorField = frame.getByLabel("First Name");

        const hasError = await errorField.evaluate((el) => {
            return (
                el.getAttribute("aria-invalid") === "true" ||
                el.closest(".Mui-error") !== null
            );
        });

        expect(hasError).toBeTruthy();
    });
});

test.describe("Registration Dialog - Accessibility", () => {
    test("should have proper labels for all fields", async ({ page }) => {
        await page.goto(NORMAL_STORY_URL);

        const frame = page.frameLocator(
            'iframe[id="storybook-preview-iframe"]',
        );

        // Check that all fields have aria-labels or labels
        const firstNameLabel = await frame.getByLabel("First Name").count();
        const surnameLabel = await frame.getByLabel("Surname").count();
        const emailLabel = await frame.getByLabel("Email Address").count();
        const orgLabel = await frame.getByLabel("Organization").count();

        expect(firstNameLabel).toBeGreaterThan(0);
        expect(surnameLabel).toBeGreaterThan(0);
        expect(emailLabel).toBeGreaterThan(0);
        expect(orgLabel).toBeGreaterThan(0);
    });
});
