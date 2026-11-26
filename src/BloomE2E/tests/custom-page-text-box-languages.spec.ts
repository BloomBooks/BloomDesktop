// On a custom page, each text box shows the language Text Box Properties set it to, whether the book
// is trilingual or bilingual, while a box left on Normal follows the book. The same holds for a box
// on another kind of page laid out through Change Layout.
// Automates the manual test "Custom Page Language Display Control" (Test Case ID 65).
//
// The custom page has four text boxes: one left on Normal, and one each set to Language 1, 2 and 3.
// The Normal box is what shows that the book really shows two or three languages; the others must
// each show their one language whatever the book shows, Language 3 included when the book is
// bilingual. After leaving the page and coming back, Text Box Properties must open on each box's
// own choice.
//
// The book always shows Language 1 here. A box set to Language 1 in a book that hides Language 1 is
// BL-16787, which this test does not cover.
//
// On a Debug build this test fails while BL-17169 is open: each time Text Box Properties opens,
// a Debug build reports that one of the dialog's strings cannot be localized, and the fixture
// fails a test during which Bloom reports a problem. A Release build, which the nightly runs,
// reports nothing.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    editablePageFrame,
    expectLanguagesShownInGroup,
    getContentLanguages,
    getContentPages,
    getLanguagesShownIn,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
} from "../helpers/bookMaking";
import {
    chooseSectionType,
    findEmptySection,
    sections,
    setChangeLayoutMode,
    splitSection,
} from "../helpers/origami";
import {
    chooseTextBoxLanguage,
    closeTextBoxProperties,
    getCheckedTextBoxLanguage,
    openTextBoxProperties,
    setTextBoxLanguage,
    type TextBoxLanguage,
} from "../helpers/textBoxProperties";
import type { Page } from "@playwright/test";

// Language 1, 2 and 3 of the collection, in that order.
const L1 = "en";
const L2 = "fr";
const L3 = "es";
const TRILINGUAL = [L1, L2, L3];
const BILINGUAL = [L1, L2];

test.use({
    collectionSpec: {
        name: "custom-page-text-box-languages",
        languages: TRILINGUAL,
    },
});

// The custom page's text boxes, top to bottom, with the choice each gets in Text Box Properties and
// the language it then shows alone ("normal" shows whatever the book shows).
const BOXES: { choice: TextBoxLanguage; language?: string }[] = [
    { choice: "normal" },
    { choice: "L1", language: L1 },
    { choice: "L2", language: L2 },
    { choice: "L3", language: L3 },
];

// The one text box of a Just Text page.
const JUST_TEXT_GROUP = ".split-pane-component-inner .bloom-translationGroup";

/** The text boxes of the page being edited, in document order. */
const textBoxes = (page: Page) =>
    editablePageFrame(page).locator(
        ".split-pane-component-inner > .bloom-translationGroup",
    );

/**
 * Wait until each text box of the custom page shows what BOXES says, given the languages the book
 * shows. Fails with a message that names the box and `when`.
 */
async function expectCustomPageBoxes(
    page: Page,
    bookLanguages: string[],
    when: string,
): Promise<void> {
    await expect(
        textBoxes(page),
        `The custom page should have ${BOXES.length} text boxes ${when}.`,
    ).toHaveCount(BOXES.length);
    for (const [index, { choice, language }] of BOXES.entries()) {
        const expected = language ? [language] : bookLanguages;
        await expect
            .poll(
                async () =>
                    (
                        await getLanguagesShownIn(textBoxes(page).nth(index))
                    ).sort(),
                {
                    timeout: 30000,
                    message:
                        `Text box ${index + 1}, set to ${choice}, should show ` +
                        `${expected.join(", ")} ${when}.`,
                },
            )
            .toEqual([...expected].sort());
        // getLanguagesShownIn looks only at each language's own box, so a group hidden as a
        // whole would still count as showing its languages.
        await expect(
            textBoxes(page).nth(index),
            `Text box ${index + 1}, set to ${choice}, should be shown ${when}.`,
        ).toBeVisible();
    }
}

test("each text box on a custom page shows the language Text Box Properties set it to, in a trilingual and a bilingual book [Test Case ID 65]", async ({
    page,
    step,
}) => {
    test.setTimeout(420000);

    const customPage = await step(
        "Make a trilingual book with a custom page holding four text boxes",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Custom");
            const [added] = await getContentPages(page);
            await goToPage(page, added.id);
            await setContentLanguages(page, TRILINGUAL);
            expect(
                (await getContentLanguages(page)).sort(),
                "test setup: the book should show all three languages",
            ).toEqual([...TRILINGUAL].sort());

            // Origami offers no "+" on an empty section, so each new section is made a text box
            // before the next split.
            await setChangeLayoutMode(page, true);
            await chooseSectionType(page, "text", 0);
            for (let split = 1; split < BOXES.length; split++) {
                await splitSection(page, "bottom", 0);
                await chooseSectionType(
                    page,
                    "text",
                    await findEmptySection(page),
                );
            }
            await expect(
                sections(page),
                "test setup: the custom page should have a section for each text box",
            ).toHaveCount(BOXES.length);
            await setChangeLayoutMode(page, false);

            await expect(textBoxes(page)).toHaveCount(BOXES.length);
            for (let index = 0; index < BOXES.length; index++)
                expect(
                    (
                        await getLanguagesShownIn(textBoxes(page).nth(index))
                    ).sort(),
                    `test setup: new text box ${index + 1} should show all three languages`,
                ).toEqual([...TRILINGUAL].sort());
            return added;
        },
    );
    if (!customPage) throw new Error("The book has no custom page.");

    await step(
        "Set the text boxes to Language 1, 2 and 3 in Text Box Properties",
        async () => {
            await setChangeLayoutMode(page, true);
            for (const [index, { choice }] of BOXES.entries())
                if (choice !== "normal")
                    await chooseTextBoxLanguage(page, choice, index);
            await setChangeLayoutMode(page, false);
            await expectCustomPageBoxes(
                page,
                TRILINGUAL,
                "in the trilingual book",
            );
        },
    );

    const otherPage = await step(
        "Add a Just Text page, then come back to the custom page",
        async () => {
            const before = new Set(
                (await getContentPages(page)).map((p) => p.id),
            );
            await addPage(page, "Just Text");
            const added = (await getContentPages(page)).find(
                (p) => !before.has(p.id),
            );
            if (!added) throw new Error("Adding a Just Text page added none.");
            await goToPage(page, customPage.id);
            await expectCustomPageBoxes(
                page,
                TRILINGUAL,
                "after the book left the custom page and came back",
            );
            await expectDialogChoices(page);
            return added;
        },
    );
    if (!otherPage) throw new Error("The book has no Just Text page.");

    await step("Make the book bilingual", async () => {
        await setContentLanguages(page, BILINGUAL);
        expect(
            (await getContentLanguages(page)).sort(),
            "The book should show only Language 1 and 2 now",
        ).toEqual([...BILINGUAL].sort());
        await expectCustomPageBoxes(page, BILINGUAL, "in the bilingual book");
        await expectDialogChoices(page);
    });

    await step(
        "On the Just Text page, set the text box to Language 3 through Change Layout",
        async () => {
            await goToPage(page, otherPage.id);
            await expectLanguagesShownInGroup(
                page,
                JUST_TEXT_GROUP,
                BILINGUAL,
                "test setup: the Just Text page's box, on Normal, should show the bilingual book's two languages",
            );
            await setTextBoxLanguage(page, "L3");
            await expectLanguagesShownInGroup(
                page,
                JUST_TEXT_GROUP,
                [L3],
                "The Just Text page's box, set to Language 3, should show Language 3 alone in the bilingual book",
            );
            await goToPage(page, customPage.id);
            await goToPage(page, otherPage.id);
            await expectLanguagesShownInGroup(
                page,
                JUST_TEXT_GROUP,
                [L3],
                "After the book left the Just Text page and came back, its box should still show Language 3 alone",
            );
            expect(
                (await getContentLanguages(page)).sort(),
                "Setting one text box's language should not change which languages the book shows",
            ).toEqual([...BILINGUAL].sort());
        },
    );

    await step(
        "Make the book trilingual again and go back to the custom page",
        async () => {
            await setContentLanguages(page, TRILINGUAL);
            await expectLanguagesShownInGroup(
                page,
                JUST_TEXT_GROUP,
                [L3],
                "The Just Text page's box, set to Language 3, should still show Language 3 alone in the trilingual book",
            );
            await goToPage(page, customPage.id);
            await expectCustomPageBoxes(
                page,
                TRILINGUAL,
                "once the book is trilingual again",
            );
        },
    );
});

/**
 * Open each custom-page text box's Text Box Properties in turn and check that it has that box's
 * choice selected. In Change Layout mode the boxes do not show what they are set to, so the dialog
 * is what tells a box set to Language 1 from one on Normal.
 */
async function expectDialogChoices(page: Page): Promise<void> {
    await setChangeLayoutMode(page, true);
    for (const [index, { choice }] of BOXES.entries()) {
        await openTextBoxProperties(page, index);
        expect(
            await getCheckedTextBoxLanguage(page),
            `Text Box Properties of text box ${index + 1} should open on ${choice}`,
        ).toBe(choice);
        await closeTextBoxProperties(page);
    }
    await setChangeLayoutMode(page, false);
}
