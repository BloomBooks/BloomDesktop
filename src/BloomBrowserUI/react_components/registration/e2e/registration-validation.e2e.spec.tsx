/**
 * Tests for registration form validation.
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import { setTestComponent } from "../../component-tester/setTestComponent";
import type { RegistrationContentsProps } from "../registrationContents";

test.describe("registration with invalid email", () => {
    test("flags invalid email on initial load", async ({ page }) => {
        const info = {
            firstName: "John",
            surname: "Doe",
            email: "invalid-email",
            organization: "Test Organization",
            usingFor: "Testing purposes",
            hadEmailAlready: false,
        };

        await setTestComponent<RegistrationContentsProps>(
            page,
            "RegistrationContents",
            {
                info: info,
                onInfoChange: () => {},
                mayChangeEmail: true,
                emailRequiredForTeamCollection: false,
                registrationIsOptional: true,
                showOptOut: true,
                onSubmit: (updated) => console.log("Submitted:", updated),
                onOptOut: (updated) => console.log("Opted out:", updated),
            },
        );

        const emailField = page.getByRole("textbox", { name: "Email Address" });
        await expect(emailField).toHaveValue("invalid-email");
        await expect(emailField).toHaveAttribute("aria-invalid", "true");
    });
});
