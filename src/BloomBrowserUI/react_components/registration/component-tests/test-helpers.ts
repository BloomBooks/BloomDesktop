import { Page, expect, Locator } from "../../component-tester/playwrightTest";
import { setTestComponent } from "../../component-tester/setTestComponent";
import {
    IRegistrationContentsProps,
    RegistrationInfo,
} from "../registrationContents";
import {
    preparePostReceiver,
    PostReceiver,
} from "../../component-tester/apiInterceptors";

// Test timing constants
export const kTestOptOutDelaySeconds = 2;
export const kTestOptOutTimeoutMs = kTestOptOutDelaySeconds * 1000 + 2000; // delay + buffer

// Field helper type for registration form
type FieldHelper = {
    name: string;
    getElement: () => Promise<Locator>;
    getValue: () => Promise<string>;
    fill: (value: string) => Promise<void>;
    clear: () => Promise<void>;
    markedInvalid: Promise<boolean>;
};

// Field name constants for registration form
let currentPage: Page | undefined;

function createFieldHelper(testId: string): FieldHelper {
    return {
        name: testId,
        getElement: async () => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            // Get the input/textarea element within the test-id container
            // Use .first() to handle multiline fields that have a hidden resize textarea
            return currentPage
                .getByTestId(testId)
                .locator("input,textarea")
                .first();
        },
        getValue: async () => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            return currentPage
                .getByTestId(testId)
                .locator("input,textarea")
                .first()
                .inputValue();
        },
        fill: async (value: string) => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            await currentPage
                .getByTestId(testId)
                .locator("input,textarea")
                .first()
                .fill(value);
        },
        clear: async () => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            await currentPage
                .getByTestId(testId)
                .locator("input,textarea")
                .first()
                .clear();
        },
        get markedInvalid(): Promise<boolean> {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            return (async () => {
                // Check aria-invalid on the actual input element
                const inputElement = currentPage
                    .getByTestId(testId)
                    .locator("input,textarea")
                    .first();
                const ariaInvalid =
                    await inputElement.getAttribute("aria-invalid");
                return ariaInvalid === "true";
            })();
        },
    };
}

export const field = {
    firstName: createFieldHelper("firstName"),
    surname: createFieldHelper("surname"),
    email: createFieldHelper("email"),
    organization: createFieldHelper("organization"),
    usingFor: createFieldHelper("usingFor"),
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

    // Use a faster delay for tests to speed them up, unless explicitly overridden
    const propsWithTestDelay: IRegistrationContentsProps = {
        optOutDelaySeconds: kTestOptOutDelaySeconds,
        ...props,
    };

    await setTestComponent<IRegistrationContentsProps>(
        page,
        "../registration/registrationContents",
        "RegistrationContents",
        propsWithTestDelay,
    );

    return receiver;
}

export async function clickRegisterButton(page: Page) {
    await page.getByTestId("registerButton").click();
}

export function getRegisterButton(page: Page) {
    return page.getByTestId("registerButton");
}

export function getOptOutButton(page: Page) {
    return page.getByTestId("optOutButton");
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
    const optOutButton = getOptOutButton(page);
    await expect(optOutButton).toBeVisible({
        timeout: kTestOptOutTimeoutMs,
    });
    await optOutButton.click();
}

export async function getMarkedInvalid(
    page: Page,
    fieldHelper: FieldHelper,
): Promise<boolean> {
    const field = page.getByTestId(fieldHelper.name);
    const ariaInvalid = await field.getAttribute("aria-invalid");
    return ariaInvalid === "true";
}
