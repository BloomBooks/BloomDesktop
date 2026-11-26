// In a Spanish collection, the copyright and license words Bloom writes on a book's credits page
// come out in Spanish. Automates the manual test "Spanish Collection Copyright" (Test Case ID 70).
//
// The manual test sets Windows to Spanish (Mexico) and makes two books in a Spanish collection: one
// from a Spanish book downloaded from Bloom Library, and one from Basic Book. A Windows set to
// Spanish gives a fresh Bloom a Spanish UI, so each test puts Bloom's UI in Spanish too, through
// the API. It does not change Windows: Bloom runs in the invariant culture (UserCulture.cs), and
// the language of the credits page's words is chosen from the collection's languages
// (BookData.GetLanguagePrioritiesForLocalizedTextOnPage), not from the Windows culture.
//
// The words checked are the ones Bloom generates, so it has to pick their language:
// - the license's description (data-derived="licenseDescription"), built from Palaso's
//   MetadataDisplay.Licenses.CreativeCommons strings;
// - on a derivative, the sentence about the original book, built from
//   EditTab.FrontMatter.FullOriginalCopyrightLicenseSentence.
// The Spanish and English of each are read from Bloom (i18n/getStringInLang) or from Palaso.xlf, so
// a change of wording in a translation does not break the test.
// The copyright line itself ("Copyright © 2026, ...") is what the user typed or the original had,
// and has no words of Bloom's to translate.
//
// The derivative is made from "The Moon and the Cap" ("La luna y el gorro") in the Sample Shells
// Bloom installs (CC BY), a shell with Spanish text, rather than from the card's downloaded book, so
// the run needs no network. What a derivative's credits page says is worked out in Bloom from the
// original's copyright and license, wherever the original came from.
//
// A collection made with only Spanish has Spanish as Language 1 and Language 2, which is what
// Bloom's own new-collection wizard writes for it.

import * as fs from "node:fs";
import * as Path from "node:path";
import { fileURLToPath } from "node:url";
import { expect, test } from "../fixtures/bloomTest";
import type { Page } from "@playwright/test";
import type { IBloomApp } from "../fixtures/bloomTest";
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

test.use({
    collectionSpec: { name: "spanish-collection-copyright", languages: ["es"] },
});

const SHELL_COLLECTION = "Sample Shells";
// The shell's folder name. A Spanish UI lists it by its Spanish title, "La luna y el gorro"; the
// sentence about the original names it by the original's own title, this one.
const SHELL_TITLE = "The Moon and the Cap";
const SHELL_COPYRIGHT_HOLDER = "Pratham Books";
const FULL_SENTENCE_ID =
    "EditTab.FrontMatter.FullOriginalCopyrightLicenseSentence";
// The part of every Creative Commons license's description that says to credit the author.
const ATTRIBUTION_ID =
    "MetadataDisplay.Licenses.CreativeCommons.AttributionRequired";

/**
 * Put Bloom's UI in Spanish, as a Windows set to Spanish (Mexico) does for a fresh Bloom, and
 * return the shell page (a new one when the language changed). Bloom's Spanish is es-ES.
 */
async function useSpanishUi(bloomApp: IBloomApp): Promise<Page> {
    if ((await getCurrentUiLanguageTag(bloomApp.page)) !== "es-ES")
        await setUiStateViaApi(bloomApp, "es-ES", false);
    return bloomApp.page;
}

/**
 * A localized string in Spanish and in English, failing when Bloom has no Spanish for it, since
 * then the test could not tell the two apart.
 */
async function getSpanishAndEnglish(
    page: Page,
    key: string,
): Promise<{ spanish: string; english: string }> {
    const get = (langId: string) =>
        apiGetJson<{ text: string; langFound: string }>(
            page,
            `i18n/getStringInLang?${new URLSearchParams({ key, langId })}`,
        );
    const spanish = await get("es");
    expect(
        spanish.langFound,
        `Bloom has no Spanish for ${key}, so this test cannot tell Spanish from English.`,
    ).toMatch(/^es/);
    const english = await get("en");
    expect(english.text).not.toBe(spanish.text);
    return { spanish: spanish.text, english: english.text };
}

/** A pattern matching the whole of a format string's output, whatever fills its placeholders. */
function formatPattern(format: string): RegExp {
    const escaped = format.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return new RegExp(`^${escaped.replace(/\\\{\d+\\\}/g, ".+")}$`);
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

/**
 * A Palaso string in Spanish and in English, read from DistFiles/localization/es/Palaso.xlf, which
 * holds both. Bloom's i18n API answers only for Bloom's own strings, not Palaso's.
 */
function getPalasoSpanishAndEnglish(key: string): {
    spanish: string;
    english: string;
} {
    const xlf = fs.readFileSync(
        Path.resolve(
            Path.dirname(fileURLToPath(import.meta.url)),
            "../../../DistFiles/localization/es/Palaso.xlf",
        ),
        "utf8",
    );
    const unit = xlf.match(
        new RegExp(`<trans-unit id="${key}"[^>]*>([\\s\\S]*?)</trans-unit>`),
    )?.[1];
    const english = unit?.match(/<source[^>]*>([^<]*)<\/source>/)?.[1];
    const spanish = unit?.match(/<target[^>]*>([^<]*)<\/target>/)?.[1];
    if (!english || !spanish)
        throw new Error(
            `Palaso.xlf has no Spanish for ${key}, so this test cannot tell Spanish from English.`,
        );
    return { spanish, english };
}

/** Check that the credits page's Creative Commons license description is in Spanish. */
async function expectSpanishLicenseDescription(page: Page): Promise<void> {
    const attribution = getPalasoSpanishAndEnglish(ATTRIBUTION_ID);
    const license = await getLicenseDescription(page);
    expect(
        license.lang,
        `The license description is not tagged Spanish: "${license.text}"`,
    ).toMatch(/^es/);
    expect(
        license.text,
        "The license description does not have Bloom's Spanish words",
    ).toContain(attribution.spanish);
    expect(license.text).not.toContain(attribution.english);
}

test.describe("copyright and license words in a Spanish collection", () => {
    test("a derivative's credits page is in Spanish [Test Case ID 70]", async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        const page = await useSpanishUi(bloomApp);
        await makeBookFromSourceBook(page, SHELL_COLLECTION, SHELL_TITLE);
        await goToCreditsPage(page);

        const fullSentence = await getSpanishAndEnglish(page, FULL_SENTENCE_ID);
        const sentence = await expectOriginalCopyrightSentence(
            page,
            { hasHintBubble: true },
            "The derivative's credits page did not show the sentence about the original book.",
        );
        expect(
            sentence.text,
            "The sentence about the original book is not Bloom's Spanish sentence",
        ).toMatch(formatPattern(fullSentence.spanish));
        expect(sentence.text).toContain(SHELL_TITLE);
        expect(sentence.text).toContain(SHELL_COPYRIGHT_HOLDER);

        await expectSpanishLicenseDescription(page);
    });

    test("a Basic Book's credits page is in Spanish [Test Case ID 70]", async ({
        bloomApp,
    }) => {
        test.setTimeout(300000);
        const page = await useSpanishUi(bloomApp);
        await makeBookFromTemplate(page, "Basic Book");
        await goToCreditsPage(page);
        await expectSpanishLicenseDescription(page);
        await expectOriginalCopyrightSentence(
            page,
            { text: "" },
            "A book made from Basic Book showed a sentence about an original book.",
        );
    });
});
