// Drive and read Publish: Web — the stepper that decides whether a book may go to
// BloomLibrary.org: the Confirm Metadata step with its "Missing Title"/"Missing Copyright"
// warnings, the Agreements step, and the Upload step's buttons.
//
// The screen is a MUI vertical stepper, so a step that is not active shows no content at all.
// That is why "have the Agreements appeared?" is a real question with a real answer here, and why
// several of these helpers poll: what the screen shows follows libraryPublish/getBookInfo, which
// is fetched after the screen mounts and re-fetched whenever the copyright is saved.

import { expect, type Locator, type Page } from "@playwright/test";
import { apiPost } from "./api";
import { selectPublishDestination } from "./publish";

/** The two things a book must have before Bloom will let it be uploaded. */
export type UploadRequirement = "Title" | "Copyright";

// Each warning box carries its own test id, because the two look alike and a test has to click
// the right one's "Click to fix". See LibraryPublishSteps.tsx.
const WARNING_TEST_ID: Record<UploadRequirement, string> = {
    Title: "missing-title",
    Copyright: "missing-copyright",
};

/** Whether a button of the Upload step is there at all, and if so whether it can be clicked. */
export type ButtonState = "absent" | "enabled" | "disabled";

/** The buttons the Upload step offers. Which ones exist is itself the login gate under test. */
export interface IUploadStepButtons {
    /** "Sign in or sign up to BloomLibrary.org". Shown only while nobody is signed in. */
    signIn: ButtonState;
    /** "Upload Book". Shown only to a signed-in user, which is the gate this screen enforces. */
    uploadBook: ButtonState;
    /** "Sign out (email)". Shown only while somebody is signed in. */
    signOut: ButtonState;
}

/**
 * Go to the Publish tab, open its Web screen, and wait for the upload steps to be showing. A book
 * must already be selected.
 */
export async function openPublishToWeb(page: Page): Promise<void> {
    await selectPublishDestination(page, "Web");
    await page
        .getByTestId("publish-to-web-steps")
        .waitFor({ state: "visible", timeout: 60000 });
}

/**
 * Which required items the screen is currently complaining about, always Title before Copyright.
 * An empty array means the book's metadata is complete enough to upload.
 */
export async function getMissingRequirements(
    page: Page,
): Promise<UploadRequirement[]> {
    const shown = await Promise.all(
        (Object.keys(WARNING_TEST_ID) as UploadRequirement[]).map(
            async (requirement) =>
                (await page.getByTestId(WARNING_TEST_ID[requirement]).count()) >
                0
                    ? requirement
                    : undefined,
        ),
    );
    return shown.filter((r): r is UploadRequirement => !!r);
}

/**
 * Wait until the screen is complaining about exactly these requirements, and no others.
 *
 * Poll rather than read once: the warnings appear only after libraryPublish/getBookInfo answers,
 * and they go away only after the same call is repeated when the copyright is saved, so reading
 * the moment after an action races Bloom.
 */
export async function expectMissingRequirements(
    page: Page,
    expected: UploadRequirement[],
    message: string,
): Promise<void> {
    await expect
        .poll(async () => (await getMissingRequirements(page)).join(", "), {
            timeout: 30000,
            message,
        })
        .toBe(expected.join(", "));
}

/**
 * Click the "Click to fix" link of one warning, the way a user does. For Copyright this opens the
 * Copyright and License dialog (see helpers/copyrightAndLicense.ts); for Title it takes Bloom to
 * the Edit tab, showing the front cover, where the title is typed.
 *
 * This does not wait for what the click leads to, because the two lead somewhere different; the
 * caller waits for its own destination.
 */
export async function clickToFixMissingItem(
    page: Page,
    requirement: UploadRequirement,
): Promise<void> {
    const warning = page.getByTestId(WARNING_TEST_ID[requirement]);
    await warning.waitFor({ state: "visible", timeout: 30000 });
    await warning.getByRole("link").click();
}

/**
 * Whether the Agreements step is showing its three check boxes. The stepper collapses the content
 * of a step that is not active, so this is false until the book has both a title and a copyright.
 */
export async function areAgreementsShowing(page: Page): Promise<boolean> {
    return (await page.getByTestId("upload-agreement").count()) > 0;
}

/** Wait until the Agreements step has appeared (or, with false, until it has gone away). */
export async function expectAgreementsShowing(
    page: Page,
    showing: boolean,
    message: string,
): Promise<void> {
    await expect
        .poll(async () => areAgreementsShowing(page), {
            timeout: 30000,
            message,
        })
        .toBe(showing);
}

/**
 * Tick every agreement check box, which is what the user does before uploading. Returns when all
 * three are ticked.
 */
export async function acceptAllAgreements(page: Page): Promise<void> {
    const agreements = page.getByTestId("upload-agreement");
    await agreements.first().waitFor({ state: "visible", timeout: 30000 });
    const count = await agreements.count();
    if (count !== 3)
        throw new Error(
            `The Agreements step showed ${count} agreements; Bloom's upload screen has three.`,
        );
    for (let i = 0; i < count; i++) {
        const box = agreements.nth(i).locator('input[type="checkbox"]');
        if (!(await box.isChecked())) await box.click();
        await expect(box).toBeChecked({ timeout: 15000 });
    }
}

/**
 * What the Upload step is offering right now. A test reads this rather than looking for one
 * button, because "which buttons are there" is the answer to both "can I upload?" and "does it
 * want me to sign in first?".
 */
export async function getUploadStepButtons(
    page: Page,
): Promise<IUploadStepButtons> {
    const buttons = await page
        .getByTestId("upload-buttons")
        .locator("button")
        .evaluateAll((elements) =>
            elements.map((element) => ({
                text: (element.textContent ?? "").trim(),
                disabled: (element as HTMLButtonElement).disabled,
            })),
        );
    // The labels are English, as everywhere else in this suite (see AUTOMATION-DEBT.md on the
    // top bar). Each is matched by its start, because the sign-out button carries the user's
    // email and the upload button says which server it will upload to.
    const stateOf = (startsWith: string): ButtonState => {
        const button = buttons.find((b) => b.text.startsWith(startsWith));
        if (!button) return "absent";
        return button.disabled ? "disabled" : "enabled";
    };
    return {
        signIn: stateOf("Sign in"),
        uploadBook: stateOf("Upload Book"),
        signOut: stateOf("Sign out"),
    };
}

/**
 * The three button states as one comparable line, in a fixed order, so that comparing two of them
 * cannot depend on the order a caller happened to write the fields in.
 */
function describeUploadStepButtons(buttons: IUploadStepButtons): string {
    return (
        `signIn: ${buttons.signIn}, uploadBook: ${buttons.uploadBook}, ` +
        `signOut: ${buttons.signOut}`
    );
}

/** Wait until the Upload step is offering exactly these buttons in these states. */
export async function expectUploadStepButtons(
    page: Page,
    expected: IUploadStepButtons,
    message: string,
): Promise<void> {
    await expect
        .poll(
            async () =>
                describeUploadStepButtons(await getUploadStepButtons(page)),
            { timeout: 30000, message },
        )
        .toBe(describeUploadStepButtons(expected));
}

/**
 * Click the Upload Book button. This is the click that starts an upload, so a test may only do it
 * where it knows Bloom will stop and ask something first (see the template warning below); a book
 * that is ready to go would be uploaded to a real server.
 */
export async function clickUploadBook(page: Page): Promise<void> {
    await page
        .getByTestId("upload-buttons")
        .getByRole("button", { name: /^Upload Book/ })
        .click();
}

/**
 * Wait for the warning Bloom shows when the book being uploaded is a template — templates are the
 * exception to the copyright rule, so Bloom lets them through but checks that the user meant it —
 * and return what it says.
 */
export async function waitForTemplateUploadWarning(
    page: Page,
): Promise<string> {
    const warning = templateUploadWarning(page);
    await warning.waitFor({ state: "visible", timeout: 60000 });
    return (await warning.innerText()).trim();
}

/**
 * Answer No to the template warning. That is the only answer a test may give: Yes starts a real
 * upload to a real server, which no automated run should do (see AUTOMATION-DEBT.md), so there is
 * deliberately no helper for it.
 */
export async function declineTemplateUploadWarning(page: Page): Promise<void> {
    await templateUploadWarning(page)
        .getByRole("button", { name: "No", exact: true })
        .click();
    await expect(templateUploadWarning(page)).toHaveCount(0, {
        timeout: 30000,
    });
}

/** The template warning dialog, told apart from any other dialog by what it says. */
function templateUploadWarning(page: Page): Locator {
    return page
        .getByRole("dialog")
        .filter({ hasText: "seems to be a template" });
}

/**
 * Make Bloom report that this email is signed in to Bloom Library, or — with the empty string —
 * that nobody is, without touching the real login. Undo it with stopPretendingAboutLogin.
 *
 * A test cannot use the real thing in either direction: signing in opens an external browser and
 * needs real credentials, and signing out would sign the developer's own Bloom out, because the
 * login lives in machine-wide settings. So the e2e hook e2e/loginState pretends, which is enough
 * to test the gate — the upload screen offers Upload only to a signed-in user — while an actual
 * upload would still need a real login. See AUTOMATION-DEBT.md.
 */
export async function pretendLoginState(
    page: Page,
    email: string,
): Promise<void> {
    await postLoginState(page, email);
}

/**
 * Stop pretending: Bloom reports the real login state again. A test that pretended should end
 * this way, because the Bloom it is driving reads the developer's own machine-wide login, and the
 * top bar's account control shows whatever this reports.
 */
export async function stopPretendingAboutLogin(page: Page): Promise<void> {
    await postLoginState(page, null);
}

/** Post one of the hook's three states: an email, "" for signed out, or null for "stop pretending". */
async function postLoginState(page: Page, email: string | null): Promise<void> {
    await apiPost(
        page,
        "e2e/loginState",
        JSON.stringify({ email }),
        "application/json",
    );
}
