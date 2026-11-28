// Drive and read Publish: Web — the stepper that decides whether a book may go to
// BloomLibrary.org: the Confirm Metadata step with its "Missing Title"/"Missing Copyright"
// warnings, the Agreements step, and the Upload step's buttons.
//
// The screen is a MUI vertical stepper, so a step that is not active shows no content at all.
// That is why "have the Agreements appeared?" is a real question with a real answer here, and why
// several of these helpers poll: what the screen shows follows libraryPublish/getBookInfo, which
// is fetched after the screen mounts and re-fetched whenever the copyright is saved.

import { expect, type Locator, type Page } from "@playwright/test";
import { apiGetJson, apiPost, getUiString } from "./api";
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
 * Click the Upload Book button. Under --e2e Bloom uploads only to the sandbox and refuses
 * bloomlibrary.org itself (see LibraryPublishApi.RefuseUploadWhileRunningE2eTests), and a test that
 * has only a pretended login (e2e/loginState) has no real session to upload with; what such a test
 * sees is what the screen does before an upload would happen, such as the template warning below.
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
 * Answer No to the template warning. There is deliberately no helper for Yes here: this helper
 * serves tests that only pretend to be logged in, which have no real session to upload with, and
 * what the manual test is really checking — that a template is allowed through the copyright rule —
 * is already settled by the warning appearing at all. A test that signs in for real and means to
 * upload drives the Yes path itself.
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

/**
 * The Web screen's Summary box, the summary a book has on BloomLibrary.org. Found by its id, which
 * is not localized. See LibraryPublishSteps.tsx.
 */
export function librarySummaryBox(page: Page): Locator {
    return page.locator('textarea[id="book summary"]');
}

/**
 * The book's summary as Bloom holds it: the summary that the Web screen shows and uploads, which
 * is also the one the ePUB's Book Metadata dialog edits.
 */
export async function getBookSummary(page: Page): Promise<string> {
    return (
        (
            await apiGetJson<{ summary: string | null }>(
                page,
                "libraryPublish/getBookInfo",
            )
        ).summary ?? ""
    );
}

/**
 * Upload the book Publish: Web is showing, for real, and wait until Bloom says it is on Bloom
 * Library. Returns the names of the files the upload sent, in the order it sent them.
 *
 * The user must be signed in for real (helpers/bloomLibraryAccount.ts) and the agreements ticked.
 * When the book is already on Bloom Library, as it is for every upload after the first, Bloom asks
 * whether the two are the same book; this answers "Yes, I want to update this book", which is what
 * a person re-uploading their own book does.
 *
 * Which files were sent comes from the Upload Progress box: Bloom asks the server which of the
 * book's files are new or changed, and writes "Uploading <file name>" there for each one it then
 * sends (BloomS3Client.PutFileOnS3); the server copies the others from the previous upload. The box
 * is cleared when Upload Book is clicked, so it holds this upload's lines only. The line names the
 * file without its folder, so "audio/x.mp3" comes back as "x.mp3".
 */
export async function uploadBookForReal(
    page: Page,
    timeoutMs = 300000,
): Promise<string[]> {
    const steps = page.getByTestId("publish-to-web-steps");
    // The last step's "View on Bloom Library" link, which is the screen's sign that the upload
    // finished. Its href is the book's page; after an earlier upload it keeps that, but the step is
    // collapsed (hidden) from the moment a new upload starts until that one finishes.
    const doneLink = steps.locator('a[href*="/book/"]').first();
    const progressLog = page.getByTestId("progress-box-log");
    const uploadingFormat = await getUiString(
        page,
        "PublishTab.Upload.UploadingStatus",
        "Uploading {0}",
    );
    const [before, after] = uploadingFormat.split("{0}");

    await clickUploadBook(page);
    await expect(
        doneLink,
        "The previous upload's link stayed showing after Upload Book was clicked.",
    ).toBeHidden({ timeout: 30000 });

    // A re-upload asks the "same book?" question once Bloom has found the copy on the server, which
    // takes a round trip after "Checking for existing copy on server..." shows; a first upload goes
    // straight on. So answer the question whenever it comes, until the upload is done.
    const sameBook = page.getByRole("radio", {
        name: "Same book radio button",
    });
    const uploadLabel = await getUiString(page, "Common.Upload", "Upload");
    let answered = false;
    try {
        await expect
            .poll(
                async () => {
                    if (!answered && (await sameBook.isVisible())) {
                        await sameBook.check();
                        await page
                            .getByRole("dialog")
                            .getByRole("button", {
                                name: uploadLabel,
                                exact: true,
                            })
                            .click();
                        answered = true;
                    }
                    return doneLink.isVisible();
                },
                { timeout: timeoutMs, intervals: [1000] },
            )
            .toBe(true);
    } catch {
        throw new Error(
            `The upload did not finish within ${Math.round(timeoutMs / 1000)}s. Upload Progress said:\n` +
                (await progressLog.innerText()),
        );
    }

    return (await progressLog.innerText())
        .split(/\r?\n/)
        .map((line) => line.trim())
        .filter((line) => line.startsWith(before) && line.endsWith(after))
        .map((line) =>
            line.substring(before.length, line.length - after.length),
        );
}
