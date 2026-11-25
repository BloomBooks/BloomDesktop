// A book's copyright stays put, and a derivative of it can either keep the original's copyright and
// license as its own or add its own while the original's still shows. Automates the manual test
// "Copyright Persistence" (Test Case ID 81), which grew out of BL-7518 (a copyright set from the
// credits page was lost on leaving the book) and BL-7381 (a derivative's "Not a translation or new
// version", which drops the second set of copyright and license).
//
// The manual test uploads the original to dev.bloomlibrary.org and downloads it into another
// collection to make the derivative from. This test makes the derivative from the original where it
// is, with the collection tab's "Make a book using this source", which is the same command a person
// uses on a downloaded book. What a derivative takes from the original's copyright happens in that
// command, so the round trip through Bloom Library adds nothing this test checks; uploading is
// tested by the upload cases.
//
// "Kept" is checked the way BL-7518 found the bug: select another book on the Collections tab,
// select this one again, and look at its credits page in the Edit tab, which Bloom then loads from
// the book's file.
//
// The tests are serial: each starts from the books the one before it left behind, which is how the
// manual test reads too.

import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import {
    editBook,
    editablePageFrame,
    makeBookFromBookInCollection,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    copyrightAndLicenseDialog,
    setNotATranslation,
} from "../helpers/copyrightAndLicense";
import {
    expectOriginalCopyrightSentence,
    goToCreditsPage,
    openCopyrightDialogFromCreditsPage,
} from "../helpers/creditsPage";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";

test.use({
    collectionSpec: { name: "copyright-persistence", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

// Copyright holders unusual enough that finding one on the page cannot be a coincidence.
const ORIGINAL_HOLDER = "Kestrel Story Press";
const DERIVATIVE_HOLDER = "Osprey Translation Team";

// The License tab's words for a Creative Commons license and for its "commercial" permission, as
// LicensePanel.tsx has them for a book.
const CREATIVE_COMMONS = "Everyone is allowed to:";
const ALLOW_COMMERCIAL = "use the book in a commercial way";
// The original's license, CC BY-NC, which is not the CC BY a new book starts with, so a derivative
// that kept its default license could not pass for one that took the original's: its URL on the
// credits page, and how the sentence about the original names it.
const ORIGINAL_LICENSE_URL_PART = "/licenses/by-nc/4.0";
const ORIGINAL_LICENSE_NAME = "CC BY-NC 4.0";
// The derivative's own license, CC BY.
const DERIVATIVE_LICENSE_URL_PART = "/licenses/by/4.0";

// Filled in as the tests go.
let originalFolder = "";
let otherFolder = "";
let derivativeFolder = "";
// The original's copyright and license as its credits page shows them.
let originalCredits: ICreditsCopyright | undefined;

/** What the credits page shows of the book's own copyright and license. */
interface ICreditsCopyright {
    /** The copyright line, e.g. "Copyright © 2026, Kestrel Story Press", trimmed. */
    copyright: string;
    /** The license's URL, e.g. "http://creativecommons.org/licenses/by/4.0/". */
    licenseUrl: string;
}

/** Read the book's own copyright and license off the credits page the Edit tab is showing. */
async function getCreditsCopyright(page: Page): Promise<ICreditsCopyright> {
    const block = editablePageFrame(page)
        .locator(".bloom-page .licenseAndCopyrightBlock")
        .first();
    return block.evaluate((element) => {
        const text = (selector: string) =>
            (
                (element.querySelector(selector) as HTMLElement | null)
                    ?.innerText ?? ""
            ).trim();
        return {
            copyright: text('[data-derived="copyright"]'),
            licenseUrl: text('[data-derived="licenseUrl"]'),
        };
    });
}

/**
 * Wait until the credits page shows a copyright line that names `holder` (and not `notHolder`, when
 * given), and, when `licenseUrlPart` is given, a license URL containing it. Saving the Copyright and
 * License dialog makes Bloom rebuild the page, so for a moment the old one is still showing.
 */
async function expectCreditsCopyright(
    page: Page,
    expected: { holder: string; notHolder?: string; licenseUrlPart?: string },
    message: string,
): Promise<ICreditsCopyright> {
    let last: ICreditsCopyright | undefined;
    const matches = () =>
        !!last &&
        last.copyright.includes(expected.holder) &&
        /Copyright ©/.test(last.copyright) &&
        (!expected.notHolder || !last.copyright.includes(expected.notHolder)) &&
        (!expected.licenseUrlPart ||
            last.licenseUrl.includes(expected.licenseUrlPart));
    try {
        await expect
            .poll(
                async () => {
                    last = await getCreditsCopyright(page).catch(
                        () => undefined,
                    );
                    return matches();
                },
                { timeout: 30000 },
            )
            .toBe(true);
    } catch {
        throw new Error(
            `${message}\nExpected: ${JSON.stringify(expected)}\nLast seen: ${JSON.stringify(last)}`,
        );
    }
    return last!;
}

/** Leave the book for another one and come back to its credits page, as BL-7518 did. */
async function leaveAndComeBack(page: Page, bookFolder: string): Promise<void> {
    await editBook(page, otherFolder);
    await editBook(page, bookFolder);
    await goToCreditsPage(page);
}

/** The Copyright Holder field of the open Copyright and License dialog. */
function holderField(page: Page) {
    return copyrightAndLicenseDialog(page).getByLabel("Copyright Holder");
}

/**
 * In the open Copyright and License dialog, give the book `holder` as its copyright holder and a
 * Creative Commons license that does or does not allow commercial use, and click OK. The holder
 * goes first: on a derivative, coming back to the Copyright tab from the License tab ticks "Not a
 * translation or new version" again (BL-17181). Returns once the dialog has closed.
 */
async function setCopyrightAndLicense(
    page: Page,
    holderName: string,
    allowCommercial: boolean,
): Promise<void> {
    const dialog = copyrightAndLicenseDialog(page);
    const holder = holderField(page);
    await expect(holder).toBeEnabled({ timeout: 15000 });
    await holder.fill(holderName);
    await expect(holder).toHaveValue(holderName);
    await dialog.getByRole("tab", { name: /license/i }).click();
    await dialog.getByRole("radio", { name: CREATIVE_COMMONS }).check();
    await dialog
        .getByRole("checkbox", { name: ALLOW_COMMERCIAL })
        .setChecked(allowCommercial);
    const ok = dialog.getByRole("button", { name: "OK", exact: true });
    await expect(ok).toBeEnabled({ timeout: 15000 });
    await ok.click();
    await expect(dialog).toHaveCount(0, { timeout: 30000 });
}

/** Close the open Copyright and License dialog with Cancel, changing nothing. */
async function cancelCopyrightDialog(page: Page): Promise<void> {
    await copyrightAndLicenseDialog(page)
        .getByRole("button", { name: "Cancel", exact: true })
        .click();
    await expect(copyrightAndLicenseDialog(page)).toHaveCount(0, {
        timeout: 30000,
    });
}

test.describe("copyright persistence, in a book and in its derivative", () => {
    test("a copyright set from the credits page stays after leaving the book and coming back [Test Case ID 81]", async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // The dialog's fields, buttons and license choices are found by their English wording, and a
        // freshly launched Bloom takes its UI language from the operating system.
        if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "en")
            await setUiStateViaApi(bloomApp, "en", false);
        const page = bloomApp.page;
        // The book to switch to; made first so the original is the book Bloom is showing.
        otherFolder = await makeBookFromTemplate(page, "Basic Book");
        originalFolder = await makeBookFromTemplate(page, "Basic Book");
        await goToCreditsPage(page);
        const before = await getCreditsCopyright(page);
        expect(
            before.copyright,
            "A new Basic Book already names the copyright holder the test is about to set",
        ).not.toContain(ORIGINAL_HOLDER);
        expect(
            before.licenseUrl,
            "A new Basic Book already has the license the test is about to give the original",
        ).not.toContain(ORIGINAL_LICENSE_URL_PART);

        // As BL-7518 did: fill in nothing else on the credits page, only the copyright and license.
        await openCopyrightDialogFromCreditsPage(page);
        await setCopyrightAndLicense(page, ORIGINAL_HOLDER, false);
        await expectCreditsCopyright(
            page,
            {
                holder: ORIGINAL_HOLDER,
                licenseUrlPart: ORIGINAL_LICENSE_URL_PART,
            },
            "After OK in the Copyright and License dialog, the credits page did not show the copyright and license.",
        );

        // THE ACTION UNDER TEST: select another book, then this one again.
        await leaveAndComeBack(page, originalFolder);
        originalCredits = await expectCreditsCopyright(
            page,
            {
                holder: ORIGINAL_HOLDER,
                licenseUrlPart: ORIGINAL_LICENSE_URL_PART,
            },
            "After selecting another book and coming back, the credits page lost the copyright (BL-7518).",
        );
        await openCopyrightDialogFromCreditsPage(page);
        await expect(
            holderField(page),
            "The Copyright and License dialog, opened again, did not show the copyright holder",
        ).toHaveValue(ORIGINAL_HOLDER);
        await cancelCopyrightDialog(page);
    });

    test("a derivative shows the original's copyright, and can use it as its own without change or addition [Test Case ID 81]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        derivativeFolder = await makeBookFromBookInCollection(
            page,
            originalFolder,
        );
        await goToCreditsPage(page);
        const sentence = await expectOriginalCopyrightSentence(
            page,
            { hasHintBubble: true },
            "The derivative's credits page did not show a sentence about the original book.",
        );
        expect(
            sentence.text,
            "The derivative's sentence about the original does not name the original's copyright holder",
        ).toContain(ORIGINAL_HOLDER);
        expect(
            sentence.text,
            "The derivative's sentence about the original does not name the original's license",
        ).toContain(ORIGINAL_LICENSE_NAME);
        expect(
            (await getCreditsCopyright(page)).copyright,
            "A new derivative already gives the original's copyright as its own",
        ).not.toContain(ORIGINAL_HOLDER);

        // THE ACTION UNDER TEST: "Not a translation or new version".
        await openCopyrightDialogFromCreditsPage(page);
        await expect(
            copyrightAndLicenseDialog(page),
            "The derivative's Copyright and License dialog does not offer the original's copyright and license",
        ).toContainText(ORIGINAL_HOLDER);
        await setNotATranslation(page, true);

        await expectCreditsCopyright(
            page,
            {
                holder: ORIGINAL_HOLDER,
                licenseUrlPart: ORIGINAL_LICENSE_URL_PART,
            },
            "With \"Not a translation or new version\" ticked, the credits page did not give the original's copyright as the book's own.",
        );
        expect(
            await getCreditsCopyright(page),
            `With "Not a translation or new version" ticked, the derivative's copyright and license are not the original's, unchanged`,
        ).toEqual(originalCredits);
        await expectOriginalCopyrightSentence(
            page,
            { text: "", hasHintBubble: false },
            'With "Not a translation or new version" ticked, the credits page still added a second copyright, about the original.',
        );

        await leaveAndComeBack(page, derivativeFolder);
        const kept = await expectCreditsCopyright(
            page,
            {
                holder: ORIGINAL_HOLDER,
                licenseUrlPart: ORIGINAL_LICENSE_URL_PART,
            },
            "After selecting another book and coming back, the derivative lost the original's copyright as its own.",
        );
        expect(
            kept,
            "After selecting another book and coming back, the derivative's copyright and license are not the original's, unchanged",
        ).toEqual(originalCredits);
        await expectOriginalCopyrightSentence(
            page,
            { text: "", hasHintBubble: false },
            "After selecting another book and coming back, the derivative showed a second copyright, about the original.",
        );
    });

    test("a derivative can have a copyright and license of its own, with the original's still showing [Test Case ID 81]", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await openCopyrightDialogFromCreditsPage(page);
        const dialog = copyrightAndLicenseDialog(page);
        await dialog
            .getByRole("checkbox", { name: "Not a translation or new version" })
            .setChecked(false);
        // THE ACTION UNDER TEST: a copyright holder and license of its own (CC BY, where the
        // original has CC BY-NC), and OK.
        await setCopyrightAndLicense(page, DERIVATIVE_HOLDER, true);

        const expectedOwn = {
            holder: DERIVATIVE_HOLDER,
            notHolder: ORIGINAL_HOLDER,
            licenseUrlPart: DERIVATIVE_LICENSE_URL_PART,
        };
        await expectCreditsCopyright(
            page,
            expectedOwn,
            "After giving the derivative a copyright and license of its own, the credits page did not show them.",
        );
        const sentence = await expectOriginalCopyrightSentence(
            page,
            { hasHintBubble: true },
            "After giving the derivative a copyright of its own, the credits page no longer showed the original's.",
        );
        expect(
            sentence.text,
            "The sentence about the original no longer names the original's copyright holder",
        ).toContain(ORIGINAL_HOLDER);
        expect(
            sentence.text,
            "The sentence about the original names the derivative's copyright holder",
        ).not.toContain(DERIVATIVE_HOLDER);
        expect(
            sentence.text,
            "The sentence about the original no longer names the original's license",
        ).toContain(ORIGINAL_LICENSE_NAME);

        await leaveAndComeBack(page, derivativeFolder);
        await expectCreditsCopyright(
            page,
            expectedOwn,
            "After selecting another book and coming back, the derivative lost its own copyright and license.",
        );
        const keptSentence = await expectOriginalCopyrightSentence(
            page,
            { hasHintBubble: true },
            "After selecting another book and coming back, the derivative no longer showed the original's copyright.",
        );
        expect(keptSentence.text).toBe(sentence.text);

        // The original book is left as it was.
        await editBook(page, originalFolder);
        await goToCreditsPage(page);
        const originalAfter = await expectCreditsCopyright(
            page,
            { holder: ORIGINAL_HOLDER, notHolder: DERIVATIVE_HOLDER },
            "Giving the derivative a copyright of its own changed the original book's copyright.",
        );
        expect(
            originalAfter,
            "Giving the derivative a copyright and license of its own changed the original book's",
        ).toEqual(originalCredits);
    });
});
