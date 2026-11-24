// A text box's language, set in Change Layout through Text Box Properties: a box set to one of the
// collection's languages shows only that language, and the book's other languages move into its
// source bubble; set back to Normal, it shows every language the book shows again.
// Automates the manual test "Text Box Language Layout" (Test Case ID 369).
//
// The book shows all three of the collection's languages, and the box has text in each.
//
// On a Debug build this test fails while BL-17169 is open: each time Text Box Properties opens,
// a Debug build reports that one of the dialog's strings cannot be localized, and the fixture
// fails a test during which Bloom reports a problem. A Release build, which the nightly runs,
// reports nothing.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
} from "../helpers/bookMaking";
import {
    setTextBoxLanguage,
    type TextBoxLanguageChoice,
} from "../helpers/origami";
import {
    getShownBoxes,
    getSourceBubbleTexts,
    type ILanguageText,
} from "../helpers/sourceBubbles";

// Language 1, 2 and 3 of the collection, in that order.
const LANGUAGES = ["en", "fr", "de"];

test.use({
    collectionSpec: { name: "text-box-language", languages: LANGUAGES },
});

// The one text box of a "Just Text" page.
const TEXT_BOX = ".split-pane-component-inner .bloom-translationGroup";

const TEXT: Record<string, string> = {
    en: "The dog runs.",
    fr: "Le chien court.",
    de: "Der Hund rennt.",
};

/** What a box or a bubble holding these languages shows, sorted by language. */
const textsOf = (langs: string[]): ILanguageText[] =>
    langs
        .map((lang) => ({ lang, text: TEXT[lang] }))
        .sort((a, b) => a.lang.localeCompare(b.lang));

const sorted = (texts: ILanguageText[]) =>
    [...texts].sort((a, b) => a.lang.localeCompare(b.lang));

test("a text box set to one language shows it alone and offers the others in its source bubble [Test Case ID 369]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);

    await step(
        "Make a book showing three languages, with text in each",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text");
            const [textPage] = await getContentPages(page);
            await goToPage(page, textPage.id);
            await setContentLanguages(page, LANGUAGES);
            for (const lang of LANGUAGES)
                await typeInGroup(page, TEXT_BOX, lang, TEXT[lang]);
            expect(
                sorted(await getShownBoxes(page, TEXT_BOX)),
                "The box does not show all three languages to start with.",
            ).toEqual(textsOf(LANGUAGES));
        },
    );

    const choices: [TextBoxLanguageChoice, string][] = [
        ["Selected Language 1", "en"],
        ["Collection Language 2", "fr"],
        ["Collection Language 3", "de"],
    ];
    for (const [choice, lang] of choices) {
        await step(`Set the text box to ${choice}`, async () => {
            await setTextBoxLanguage(page, choice);
        });
        await step(
            `Check the box shows only ${lang}, and the bubble the others`,
            async () => {
                expect(
                    await getShownBoxes(page, TEXT_BOX),
                    `A box set to ${choice} does not show ${lang} alone.`,
                ).toEqual(textsOf([lang]));
                expect(
                    await getSourceBubbleTexts(page, TEXT_BOX, lang),
                    `The source bubble of a box set to ${choice} does not offer the other two languages.`,
                ).toEqual(textsOf(LANGUAGES.filter((l) => l !== lang)));
            },
        );
    }

    await step("Set the text box back to Normal", async () => {
        await setTextBoxLanguage(page, "Normal");
    });
    await step(
        "Check the box shows all three languages and no source bubble",
        async () => {
            expect(
                sorted(await getShownBoxes(page, TEXT_BOX)),
                "A box set back to Normal does not show all three languages.",
            ).toEqual(textsOf(LANGUAGES));
            expect(
                await getSourceBubbleTexts(page, TEXT_BOX, LANGUAGES[0]),
                "A box showing every language with text still has a source bubble.",
            ).toEqual([]);
        },
    );
});
