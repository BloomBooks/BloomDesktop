import { Page } from "playwright/test";
import { setTestComponent } from "../../component-tester/setTestComponent";
import {
    IRegistrationContentsProps,
    kInactivitySecondsBeforeShowingOptOut,
    RegistrationInfo,
} from "../registrationContents";
import { preparePostReceiver } from "../../component-tester/apiInterceptors";

// returns a receiver function that you can await to get the posted registration info
// if onSubmit is not provided in props
export async function setupRegistrationComponent(
    page: Page,
    props: IRegistrationContentsProps,
) {
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

    return receiver!;
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
        .getByRole("textbox", { name: "First Name" })
        .fill(info.firstName);
    await page.getByRole("textbox", { name: "Surname" }).fill(info.surname);
    await page.getByRole("textbox", { name: "Email Address" }).fill(info.email);
    await page
        .getByRole("textbox", { name: "Organization" })
        .fill(info.organization);
    await page
        .getByRole("textbox", { name: /How are you using/i })
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
