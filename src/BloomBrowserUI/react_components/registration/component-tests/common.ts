import { Page, expect } from "@playwright/test";
import { setTestComponent } from "../../component-tester/setTestComponent";
import {
    IRegistrationContentsProps,
    kInactivitySecondsBeforeShowingOptOut,
    RegistrationInfo,
} from "../registrationContents";
import {
    preparePostReceiver,
    PostReceiver,
} from "../../component-tester/apiInterceptors";

// Field name constants for registration form
export const field = {
    firstName: "First Name",
    surname: "Surname",
    email: "Email Address",
    organization: "Organization",
    usingFor: /How are you using|What will you|What are you/i,
};

// returns a receiver object that you can use to check if the post was called
// and await to get the posted registration info
export async function setupRegistrationComponent(
    page: Page,
    props: IRegistrationContentsProps,
): Promise<PostReceiver<RegistrationInfo>> {
    const receiver = preparePostReceiver<RegistrationInfo>(
        page,
        "**/bloom/api/registration/userInfo",
    );

    await setTestComponent<IRegistrationContentsProps>(
        page,
        "../registration/registrationContents",
        "RegistrationContents",
        props,
    );

    return receiver;
}

export async function clickRegisterButton(page: Page) {
    await page.getByRole("button", { name: "Register" }).click();
}

export async function fillRegistrationForm(
    page: Page,
    info: {
        firstName: string;
        surname: string;
        email: string;
        organization: string;
        usingFor: string;
    },
) {
    await page
        .getByRole("textbox", { name: field.firstName })
        .fill(info.firstName);
    await page.getByRole("textbox", { name: field.surname }).fill(info.surname);
    await page.getByRole("textbox", { name: field.email }).fill(info.email);
    await page
        .getByRole("textbox", { name: field.organization })
        .fill(info.organization);
    await page
        .getByRole("textbox", { name: field.usingFor })
        .fill(info.usingFor);
}

export async function waitForAndClickOptOutButton(page: Page) {
    await page.waitForTimeout(
        kInactivitySecondsBeforeShowingOptOut * 1000 + 1000,
    );
    const optOutButton = page.getByRole("button", {
        name: /stuck.*later/i,
    });
    await expect(optOutButton).toBeVisible();
    await optOutButton.click();
    await page.waitForTimeout(500);
}

export async function getMarkedInvalid(
    page: Page,
    fieldName: string | RegExp,
): Promise<boolean> {
    const field = page.getByRole("textbox", { name: fieldName });
    const ariaInvalid = await field.getAttribute("aria-invalid");
    return ariaInvalid === "true";
}
