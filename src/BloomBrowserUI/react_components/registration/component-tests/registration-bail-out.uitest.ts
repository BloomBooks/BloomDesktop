/**
 * Tests for Registration Dialog - Initial Rendering & Layout
 * Run with: yarn test
 */

import { expect, test } from "@playwright/test";
import {
    kInactivitySecondsBeforeShowingOptOut,
    type IRegistrationContentsProps,
} from "../registrationTypes";
import {
    setupRegistrationComponent,
    waitForAndClickOptOutButton,
    getOptOutButton,
} from "./common";

const defaultProps: IRegistrationContentsProps = {
    initialInfo: {
        firstName: "John",
        surname: "Doe",
        email: "john.doe@example.com",
        organization: "SIL International",
        usingFor: "Creating literacy materials",
        hadEmailAlready: false,
    },
    mayChangeEmail: true,
    emailRequiredForTeamCollection: false,
};

test.describe("Registration Dialog - Initial Rendering & Layout", () => {
    test('"I\'m stuck" button appears after 10 seconds', async ({ page }) => {
        await setupRegistrationComponent(page, {
            initialInfo: defaultProps.initialInfo,
        });

        // Verify "I'm stuck" button is NOT visible initially
        const optOutButton = getOptOutButton(page);
        await expect(optOutButton).not.toBeVisible();

        // Wait for the button to appear (feature: shows after 10 seconds of inactivity)
        await expect(optOutButton).toBeVisible({
            timeout: (kInactivitySecondsBeforeShowingOptOut + 2) * 1000,
        });
    });

    test("Opt-out button submits form with valid data", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: defaultProps.initialInfo,
        });

        await waitForAndClickOptOutButton(page);

        // Should submit current data
        const submittedData = await receiver.getData();
        expect(submittedData).toBeDefined();
        expect(submittedData.firstName).toBe("John");
        expect(submittedData.email).toBe("john.doe@example.com");
    });

    test("Opt-out button clears invalid email before submitting", async ({
        page,
    }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: {
                ...defaultProps.initialInfo,
                email: "invalid-email",
            },
        });

        await waitForAndClickOptOutButton(page);

        // Should clear the invalid email
        const submittedData = await receiver.getData();
        expect(submittedData.email).toBe("");
        expect(submittedData.firstName).toBe("John");
    });

    test("Opt-out button preserves valid email", async ({ page }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: defaultProps.initialInfo,
        });

        await waitForAndClickOptOutButton(page);

        // Should keep the valid email
        const submittedData = await receiver.getData();
        expect(submittedData.email).toBe("john.doe@example.com");
    });

    test("Opt-out button works even with empty required fields", async ({
        page,
    }) => {
        const receiver = await setupRegistrationComponent(page, {
            initialInfo: {
                firstName: "",
                surname: "",
                email: "",
                organization: "",
                usingFor: "",
                hadEmailAlready: false,
            },
        });

        await waitForAndClickOptOutButton(page);

        // Should submit even with empty fields
        const submittedData = await receiver.getData();
        expect(submittedData).toBeDefined();
        expect(submittedData.firstName).toBe("");
    });
});
