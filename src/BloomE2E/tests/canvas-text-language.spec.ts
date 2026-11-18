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
    getCanvasElementTextBoxes,
    selectCanvasElement,
    typeInCanvasElement,
    type ICanvasTextBox,
} from "../helpers/canvasElements";
import { kEnterpriseSubscriptionCode } from "../helpers/collectionSettings";
import { choosePageLayout } from "../helpers/customCover";
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
