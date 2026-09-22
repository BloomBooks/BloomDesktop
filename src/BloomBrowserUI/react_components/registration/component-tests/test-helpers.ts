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
// The delay the component waits before offering the opt-out button, plus a buffer for the machine
// running the suite. The buffer was 2000ms, which was not enough on a loaded machine: the last
// worker to start would miss the button by a fraction of a second, so one of these tests failed
// about once per full-suite run and passed when run on its own. This is a wait for a state, not a
// sleep, so a longer buffer costs a passing run nothing.
// John approved this timeout on 2026-09-03, as AGENTS.md asks.
export const kTestOptOutTimeoutMs = kTestOptOutDelaySeconds * 1000 + 8000;

// Field helper type for registration form
type FieldHelper = {
    name: string;
    getElement: () => Promise<Locator>;
    getValue: () => Promise<string>;
    fill: (value: string) => Promise<void>;
    clear: () => Promise<void>;
    expectMarkedInvalid: (expected: boolean) => Promise<void>;
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
        // Asserts whether the field is showing its error state (aria-invalid on the input).
        // This deliberately retries rather than reading the attribute once: React 18's
        // createRoot commits asynchronously, so a state change made by (say) a blur handler
        // is not guaranteed to be in the DOM by the time the next Playwright command runs. A
        // one-shot getAttribute races that re-render and fails intermittently. The retry uses
        // Playwright's configured expect timeout, so there is no hand-rolled wait.
        expectMarkedInvalid: async (expected: boolean) => {
            if (!currentPage) {
                throw new Error(
                    "Page not initialized. Call setupRegistrationComponent first.",
                );
            }
            const inputElement = currentPage
                .getByTestId(testId)
                .locator("input,textarea")
                .first();
            // aria-invalid is absent (not "false") when the field is valid in some MUI
            // versions, so compare the normalized boolean rather than the raw attribute.
            await expect
                .poll(async () => {
                    const ariaInvalid =
                        await inputElement.getAttribute("aria-invalid");
                    return ariaInvalid === "true";
                })
                .toBe(expected);
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
