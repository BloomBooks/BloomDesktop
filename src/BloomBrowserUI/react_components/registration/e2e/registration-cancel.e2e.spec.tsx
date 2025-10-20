/**
 * Tests for Registration Dialog - Cancel Button
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import { setTestComponent } from "../../component-tester/setTestComponent";
import type { RegistrationInfo } from "../registrationContents";

const defaultInfo: RegistrationInfo = {
    firstName: "John",
    surname: "Doe",
    email: "john@example.com",
    organization: "SIL",
    usingFor: "Testing",
    hadEmailAlready: false,
};

test.describe("Registration Dialog - Cancel Button", () => {
    test("Cancel button appears when registration is optional", async ({
        page,
    }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: defaultInfo,
            registrationIsOptional: true,
        });

        // Verify Cancel button is visible
        const cancelButton = page.getByRole("button", { name: /cancel/i });
        await expect(cancelButton).toBeVisible();
    });

    test("Cancel button does NOT appear when registration is required", async ({
        page,
    }) => {
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: defaultInfo,
            registrationIsOptional: false,
        });

        // Verify Cancel button is NOT visible
        const cancelButton = page.getByRole("button", { name: /cancel/i });
        await expect(cancelButton).not.toBeVisible();
    });

    test("Cancel button appears in normal mode but not in email-required mode", async ({
        page,
    }) => {
        // Test normal mode first
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: defaultInfo,
            registrationIsOptional: true,
            emailRequiredForTeamCollection: false,
        });

        let cancelButton = page.getByRole("button", { name: /cancel/i });
        await expect(cancelButton).toBeVisible();

        // Test email-required mode
        await setTestComponent(page, "StatefulRegistrationContents", {
            initialInfo: defaultInfo,
            registrationIsOptional: false,
            emailRequiredForTeamCollection: true,
        });

        cancelButton = page.getByRole("button", { name: /cancel/i });
        await expect(cancelButton).not.toBeVisible();
    });
});
