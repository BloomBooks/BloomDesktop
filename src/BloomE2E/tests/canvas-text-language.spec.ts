// Choosing a language for a canvas element's text, on the Language submenu of its "..." menu
// (BL-17105). The submenu offers only on a page with a custom layout, so the test gives the cover
// one and drops a Text Block onto it. A Text Block starts with a box for Language 1 alone, so
// choosing another language makes Bloom add a box for it after the page has loaded; that box has to
// be set up the way page load sets up every box (setUpBoxesBuiltAfterLoad in
// translationGroupLanguages.ts): a person can type in it, it shows its language tag, a
// right-to-left language reads right to left, and what is typed is kept.
//
// Language 2 is Arabic, written right to left, so the right-to-left box is one added after load:
// a Text Block's first box is Language 1's, which page load sets up.
//
// Automates "Canvas Text Language Choice Adds a Box That Takes Typing" (Test Case ID 860).

import { expect, test } from "../fixtures/bloomTest";
import {
    getPages,
    goToPage,
    makeBookFromTemplate,
    reloadPageBeingEdited,
} from "../helpers/bookMaking";
import {
    chooseCanvasElementLanguage,
    dragPaletteItemOntoCanvas,
    fillCanvasElementTextPastTheCanvas,
    getActiveCanvasElementRect,
    getCanvasElementTextBoxes,
    getCanvasRect,
    selectCanvasElement,
    typeInCanvasElement,
    type ICanvasTextBox,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { choosePageLayout } from "../helpers/customCover";
import { bottom, kEdgeTolerance } from "../helpers/geometry";
import { expectTextBoxMarkedAsOverflowing } from "../helpers/overflowMarks";
import { watchForScriptErrors } from "../helpers/scriptErrors";

test.use({
    collectionSpec: {
        name: "canvas-text-language",
        languages: ["en", "ar", "fr"],
        rightToLeftLanguages: ["ar"],
        // A custom cover layout is a subscription feature.
        subscriptionCode: kEnterpriseSubscriptionCode,
    },
});

/**
 * The Text Block's boxes once Arabic and French have been chosen and typed in: each takes typing
 * (a CKEditor is attached) and shows its language tag, Arabic reads right to left, and French,
 * chosen last, is the one showing.
 */
const kBoxesAfterTyping: ICanvasTextBox[] = [
    {
        language: "en",
        shown: false,
        hasEditor: true,
        languageTag: "English",
        text: "",
    },
    {
        language: "ar",
        shown: false,
        dir: "rtl",
        hasEditor: true,
        languageTag: "Arabic",
        text: "Arabic text",
    },
    {
        language: "fr",
        shown: true,
        hasEditor: true,
        languageTag: "français",
        text: "French text",
    },
];

test("choosing a language that a text block has no box for adds one that takes typing and is kept [Test Case ID 860]", async ({
    page,
}) => {
    test.setTimeout(300000);
    await makeBookFromTemplate(page, "Basic Book");
    const cover = (await getPages(page))[0];
    await goToPage(page, cover.id);
    await choosePageLayout(page, "Custom");
    const textBlock = await dragPaletteItemOntoCanvas(page, "none");

    // Sanity check what the rest stands on: the new Text Block has Language 1's box and no other.
    expect(
        (await getCanvasElementTextBoxes(page, textBlock)).map(
            (box) => box.language,
        ),
    ).toEqual(["en"]);

    const errors = watchForScriptErrors(page);
    try {
        // THE ACTION UNDER TEST: choose languages the element has no box for, and type in each.
        await selectCanvasElement(page, textBlock);
        await chooseCanvasElementLanguage(page, "Arabic", "ar");
        await typeInCanvasElement(page, "Arabic text");
        await selectCanvasElement(page, textBlock);
        await chooseCanvasElementLanguage(page, "French", "fr");
        await typeInCanvasElement(page, "French text");

        expect(await getCanvasElementTextBoxes(page, textBlock)).toEqual(
            kBoxesAfterTyping,
        );
        expect(
            errors.errors(),
            "Choosing a language raised a script error.",
        ).toEqual([]);
    } finally {
        errors.stop();
    }

    // Leaving the page saves it; coming back loads what was saved.
    await reloadPageBeingEdited(page);
    expect(await getCanvasElementTextBoxes(page, textBlock)).toEqual(
        kBoxesAfterTyping,
    );
});

// Bloom checks a text box as it is typed in, and for a box on a canvas element that check is also
// what grows the element to fit the text. So the box a language choice adds has to be checked as
// it is typed in, like the box the page loaded with: the element grows with its text until it
// reaches the bottom of the canvas, and then the box is marked as overflowing.
test("typing more than fits into the box a language choice adds grows the text block and marks the box", async ({
    page,
    step,
}) => {
    test.setTimeout(300000);
    const textBlock = await step(
        "Make a book whose cover has a custom layout with a Text Block on it",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await goToPage(page, (await getPages(page))[0].id);
            await choosePageLayout(page, "Custom");
            return dragPaletteItemOntoCanvas(page, "none");
        },
    );
    if (textBlock === undefined) throw new Error("No Text Block was made.");
    await step(
        "Choose French, which the Text Block has no box for",
        async () => {
            await selectCanvasElement(page, textBlock);
            await chooseCanvasElementLanguage(page, "French", "fr");
        },
    );
    const box = await step(
        "Type more lines into the French box than the canvas has room for",
        async () => fillCanvasElementTextPastTheCanvas(page, textBlock, "fr"),
    );
    if (box === undefined) throw new Error("Nothing was typed.");
    await step(
        "Check the Text Block grew to the bottom of the canvas, and the box is marked",
        async () => {
            // How far the Text Block's bottom is from the canvas's: it grows to that and no further.
            const pastTheCanvasBy = async () =>
                bottom(await getActiveCanvasElementRect(page)) -
                bottom(await getCanvasRect(page));
            await expect
                .poll(pastTheCanvasBy, {
                    message:
                        "The Text Block should have grown with its text to the bottom of the canvas.",
                })
                .toBeGreaterThan(-kEdgeTolerance);
            expect(
                await pastTheCanvasBy(),
                "The Text Block should stop growing at the bottom of the canvas.",
            ).toBeLessThan(kEdgeTolerance);
            await expectTextBoxMarkedAsOverflowing(
                page,
                box,
                "the Text Block's French box",
                // A canvas element's box shows no language label.
                false,
            );
        },
    );
});
