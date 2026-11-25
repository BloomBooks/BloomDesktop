// In a Spanish collection, the copyright and license words Bloom writes on a book's credits page
// come out in Spanish. Automates the manual test "Spanish Collection Copyright" (Test Case ID 70).
//
// The manual test sets Windows to Spanish (Mexico) and makes two books in a Spanish collection: one
// from a Spanish book downloaded from Bloom Library, and one from Basic Book. A Windows set to
// Spanish gives a fresh Bloom a Spanish UI, so this test puts Bloom's UI in Spanish too, through
// the API. It does not change Windows: Bloom runs in the invariant culture (UserCulture.cs), and
// the language of the credits page's words is chosen from the collection's languages
// (BookData.GetLanguagePrioritiesForLocalizedTextOnPage), not from the Windows culture.
//
// The words checked are the ones Bloom generates, so it has to pick their language:
// - the license's description (data-derived="licenseDescription"), which Bloom tags with the
//   language it used;
// - on a derivative, the sentence about the original book, built from
//   EditTab.FrontMatter.FullOriginalCopyrightLicenseSentence.
// The copyright line itself ("Copyright © 2026, ...") is what the user typed or the original had,
// and has no words of Bloom's to translate.
//
// The derivative is made from "The Moon and the Cap" ("La luna y el gorro") in the Sample Shells
// Bloom installs (CC BY), a shell with Spanish text, rather than from the card's downloaded book, so
// the run needs no network. What a derivative's
// credits page says is worked out in Bloom from the original's copyright and license, wherever the
// original came from.
//
// A collection made with only Spanish has Spanish as Language 1 and Language 2, which is what
// Bloom's own new-collection wizard writes for it.

import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import { apiGetJson } from "../helpers/api";
import {
    editablePageFrame,
    makeBookFromSourceBook,
    makeBookFromTemplate,
    waitForEditablePage,
} from "../helpers/bookMaking";
import {
    expectOriginalCopyrightSentence,
    goToCreditsPage,
} from "../helpers/creditsPage";
import {
    getCurrentUiLanguageTag,
    setUiStateViaApi,
} from "../helpers/uiLanguage";
import { switchTab } from "../helpers/workspace";

test.use({
    collectionSpec: { name: "spanish-collection-copyright", languages: ["es"] },
});

test.describe.configure({ mode: "serial" });

const SHELL_COLLECTION = "Sample Shells";
// The shell's folder name. A Spanish collection lists it by its Spanish title, "La luna y el gorro";
// the sentence about the original names it by the original's own title, this one.
const SHELL_TITLE = "The Moon and the Cap";
const SHELL_COPYRIGHT_HOLDER = "Pratham Books";
const FULL_SENTENCE_ID =
    "EditTab.FrontMatter.FullOriginalCopyrightLicenseSentence";

/** A localized string in one language, and the language Bloom actually found it in. */
async function getStringInLanguage(
    page: Page,
    key: string,
    langId: string,
): Promise<{ text: string; langFound: string }> {
    const query = new URLSearchParams({ key, langId });
    return apiGetJson(page, `i18n/getStringInLang?${query}`);
}

/** The words of a format string before its first placeholder, e.g. "This book is an adaptation of the original, ". */
function beforeFirstPlaceholder(format: string): string {
    return format.slice(0, format.indexOf("{0}")).trim();
}

/**
 * The license description on the credits page the Edit tab is showing, and the language Bloom
 * tagged it with. Waits for Bloom to have written one.
 */
async function getLicenseDescription(
    page: Page,
): Promise<{ text: string; lang: string }> {
    await waitForEditablePage(page);
    const description = editablePageFrame(page)
        .locator('.bloom-page [data-derived="licenseDescription"]')
        .first();
    let last = { text: "", lang: "" };
    await expect
        .poll(
            async () => {
                last = await description
                    .evaluate((element) => ({
                        text: (element as HTMLElement).innerText.trim(),
                        lang: element.getAttribute("lang") ?? "",
                    }))
                    .catch(() => ({ text: "", lang: "" }));
                return last.text.length > 0;
            },
            {
                timeout: 30000,
                message: "The credits page showed no license description.",
            },
        )
        .toBe(true);
    return last;
}

test.describe("copyright and license words in a Spanish collection", () => {
    test("a derivative's credits page is in Spanish [Test Case ID 70]", async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        // What a Windows set to Spanish (Mexico) gives a fresh Bloom. Bloom's Spanish is es-ES.
        if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "es-ES")
            await setUiStateViaApi(bloomApp, "es-ES", false);
        const page = bloomApp.page;
        await switchTab(page, "collection");
        await makeBookFromSourceBook(page, SHELL_COLLECTION, SHELL_TITLE);
        await goToCreditsPage(page);

        const spanish = await getStringInLanguage(page, FULL_SENTENCE_ID, "es");
        expect(
            spanish.langFound,
            `Bloom has no Spanish for ${FULL_SENTENCE_ID}, so this test cannot tell Spanish from English.`,
        ).toMatch(/^es/);
        const english = await getStringInLanguage(page, FULL_SENTENCE_ID, "en");
        const spanishStart = beforeFirstPlaceholder(spanish.text);
        expect(spanishStart).not.toBe(beforeFirstPlaceholder(english.text));

        const sentence = await expectOriginalCopyrightSentence(
            page,
            { hasHintBubble: true },
            "The derivative's credits page did not show the sentence about the original book.",
        );
        expect(
            sentence.text,
            "The sentence about the original book is not in Spanish",
        ).toMatch(new RegExp(`^${escapeRegExp(spanishStart)}`));
        expect(sentence.text).toContain(SHELL_TITLE);
        expect(sentence.text).toContain(SHELL_COPYRIGHT_HOLDER);

        const license = await getLicenseDescription(page);
        expect(
            license.lang,
            `The license description is not in Spanish: "${license.text}"`,
        ).toMatch(/^es/);
    });

    test("a Basic Book's credits page is in Spanish [Test Case ID 70]", async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        const page = bloomApp.page;
        await makeBookFromTemplate(page, "Basic Book");
        await goToCreditsPage(page);
        const license = await getLicenseDescription(page);
        expect(
            license.lang,
            `The license description is not in Spanish: "${license.text}"`,
        ).toMatch(/^es/);
        await expectOriginalCopyrightSentence(
            page,
            { text: "" },
            "A book made from Basic Book showed a sentence about an original book.",
        );
    });
});

function escapeRegExp(text: string): string {
    return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
