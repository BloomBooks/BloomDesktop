// A text box that Text Box Properties sets to one of the collection's languages shows that
// language alone, even in a book that shows only Language 1. Each language is chosen in turn, the
// way the case does it, and the page has to show the new one alone each time.
// Automates the manual test "Text Box Language Switching" (Test Case ID 87).
//
// The case also checks, after the first choice, that the page keeps it when the book leaves the
// page and comes back, and that the dialog opens again on the language chosen.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    expectLanguagesShownInGroup,
    getContentPages,
    getPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
} from "../helpers/bookMaking";
import { setChangeLayoutMode } from "../helpers/origami";
import {
    closeTextBoxProperties,
    getCheckedTextBoxLanguage,
    openTextBoxProperties,
    setTextBoxLanguage,
    type TextBoxLanguage,
} from "../helpers/textBoxProperties";

// L1, L2 and L3 of the collection, in that order.
const L1 = "en";
const L2 = "fr";
const L3 = "es";

test.use({
    collectionSpec: { name: "text-box-language", languages: [L1, L2, L3] },
});

// The one text group of a Just Text page.
const GROUP = ".marginBox .bloom-translationGroup";

// The order the case chooses the languages in, and the language each choice must show.
const CHOICES: { choice: TextBoxLanguage; language: string }[] = [
    { choice: "L3", language: L3 },
    { choice: "L2", language: L2 },
    { choice: "L1", language: L1 },
];

test("a text box set to one collection language in Text Box Properties shows that language alone [Test Case ID 87]", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);

    const textPage = await step(
        "Make a book that shows only Language 1, with a Just Text page",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text");
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await setContentLanguages(page, [L1]);
            await expectLanguagesShownInGroup(
                page,
                GROUP,
                [L1],
                "test setup: with only Language 1 shown, the text box should show Language 1 alone",
            );
            return contentPage;
        },
    );
    if (!textPage) throw new Error("The book has no content page.");

    for (const { choice, language } of CHOICES) {
        await step(
            `Set the text box to Collection Language ${choice.slice(1)} in Text Box Properties`,
            async () => {
                await setTextBoxLanguage(page, choice);
                await expectLanguagesShownInGroup(
                    page,
                    GROUP,
                    [language],
                    `With the text box set to ${choice}, the page should show the ${language} box alone`,
                );
            },
        );

        if (choice !== "L3") continue;
        await step(
            "Leave the page, come back, and open Text Box Properties again",
            async () => {
                const otherPage = (await getPages(page)).find(
                    (p) => p.id !== textPage.id,
                );
                if (!otherPage)
                    throw new Error("The book has no page but the text page.");
                await goToPage(page, otherPage.id);
                await goToPage(page, textPage.id);
                await expectLanguagesShownInGroup(
                    page,
                    GROUP,
                    [L3],
                    "After the book left the page and came back, the text box should still show the L3 box alone",
                );
                await setChangeLayoutMode(page, true);
                await openTextBoxProperties(page);
                expect(
                    await getCheckedTextBoxLanguage(page),
                    "Text Box Properties should open again on the language the box was set to",
                ).toBe("L3");
                await closeTextBoxProperties(page);
                await setChangeLayoutMode(page, false);
            },
        );
    }
});
