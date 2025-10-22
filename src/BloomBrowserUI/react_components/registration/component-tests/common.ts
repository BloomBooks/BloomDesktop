import { Page, expect, Locator } from "@playwright/test";
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

// Field helper type for registration form
type FieldHelper = {
    name: string | RegExp;
    getElement: () => Promise<Locator>;
    getValue: () => Promise<string>;
    fill: (value: string) => Promise<void>;
    clear: () => Promise<void>;
    markedInvalid: Promise<boolean>;
};

// Field name constants for registration form
let currentPage: Page | undefined;

function createFieldHelper(name: string | RegExp): FieldHelper {
    return {
        name,
        getElement: async () => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            return currentPage.getByRole("textbox", { name });
        },
        getValue: async () => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            return currentPage.getByRole("textbox", { name }).inputValue();
        },
        fill: async (value: string) => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            await currentPage.getByRole("textbox", { name }).fill(value);
        },
        clear: async () => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            await currentPage.getByRole("textbox", { name }).clear();
        },
        get markedInvalid(): Promise<boolean> {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            return (async () => {
                const element = currentPage.getByRole("textbox", { name });
                const ariaInvalid = await element.getAttribute("aria-invalid");
                return ariaInvalid === "true";
            })();
        },
    };
}

export const field = {
    firstName: createFieldHelper("First Name"),
    surname: createFieldHelper("Surname"),
    email: createFieldHelper("Email Address"),
    organization: createFieldHelper("Organization"),
    usingFor: createFieldHelper(
        /How are you using|What will you|What are you/i,
    ),
};

// returns a receiver object that you can use to check if the post was called
// and await to get the posted registration info
export async function setupRegistrationComponent(
    page: Page,
    props: IRegistrationContentsProps,
): Promise<PostReceiver<RegistrationInfo>> {
    currentPage = page;

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

export function getRegisterButton(page: Page) {
    return page.getByRole("button", { name: "Register" });
}

export function getOptOutButton(page: Page) {
    return page.getByRole("button", { name: /stuck.*later/i });
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
    await (await field.firstName.getElement()).fill(info.firstName);
    await (await field.surname.getElement()).fill(info.surname);
    await (await field.email.getElement()).fill(info.email);
    await (await field.organization.getElement()).fill(info.organization);
    await (await field.usingFor.getElement()).fill(info.usingFor);
}

export async function waitForAndClickOptOutButton(page: Page) {
    await page.waitForTimeout(
        kInactivitySecondsBeforeShowingOptOut * 1000 + 1000,
    );
    const optOutButton = getOptOutButton(page);
    await expect(optOutButton).toBeVisible();
    await optOutButton.click();
    await page.waitForTimeout(500);
}

export async function getMarkedInvalid(
    page: Page,
    fieldHelper: FieldHelper,
): Promise<boolean> {
    const field = page.getByRole("textbox", { name: fieldHelper.name });
    const ariaInvalid = await field.getAttribute("aria-invalid");
    return ariaInvalid === "true";
}
