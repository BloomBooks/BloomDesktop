// A text box that Text Box Properties sets to one of the collection's languages shows that
// language alone, even in a book that shows only Language 1. Each language is chosen in turn, the
// way the case does it, and the page has to show the new one alone each time.
// Automates the manual test "Text Box Language Switching" (Test Case ID 87).
//
// After each choice the test also leaves the page and comes back, to see that the page kept it,
// and opens the dialog again, which must have that choice selected. In this book Normal would also
// show Language 1 alone, so the dialog is what tells the L1 choice from Normal. And the choice is
// the text box's alone: the book must go on showing only Language 1.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    expectLanguagesShownInGroup,
    getContentLanguages,
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
            expect(
                await getContentLanguages(page),
                "test setup: the book should show only Language 1",
            ).toEqual([L1]);
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
    const otherPage = (await getPages(page)).find((p) => p.id !== textPage.id);
    if (!otherPage) throw new Error("The book has no page but the text page.");

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

        await step(
            "Leave the page, come back, and open Text Box Properties again",
            async () => {
                await goToPage(page, otherPage.id);
                await goToPage(page, textPage.id);
                await expectLanguagesShownInGroup(
                    page,
                    GROUP,
                    [language],
                    `After the book left the page and came back, the text box set to ${choice} should still show the ${language} box alone`,
                );
                expect(
                    await getContentLanguages(page),
                    `Setting the text box to ${choice} should not change which languages the book shows`,
                ).toEqual([L1]);
                await setChangeLayoutMode(page, true);
                await openTextBoxProperties(page);
                expect(
                    await getCheckedTextBoxLanguage(page),
                    "Text Box Properties should open again on the language the box was set to",
                ).toBe(choice);
                await closeTextBoxProperties(page);
                await setChangeLayoutMode(page, false);
            },
        );
    }
});
