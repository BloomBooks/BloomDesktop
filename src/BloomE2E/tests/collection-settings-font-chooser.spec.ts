// The font chooser on the Languages page of the Collection Settings dialog, where each language
// picks its Default Font. It is the same component as the Format dialog's, which
// font-chooser.spec.ts covers in full; this file checks that it behaves the same in the Settings
// dialog: the marks and dimming in its list, the information pane opening in front of the dialog
// with the same messages, and the coloured mark of a dimmed font once chosen.
// Automates the manual test "New Font Chooser [Manual portion]" (Test Case ID 811).
//
// Which fonts a machine has varies, so the test asks Bloom for a font of each kind at run time,
// preferring the ones the manual test names, and fails plainly when a kind is missing.

import { expect, test } from "../fixtures/bloomTest";
import { getUiString } from "../helpers/api";
import {
    cancelCollectionSettings,
    collectionSettingsFontChooser,
    getCollectionSettingsLanguageFont,
    openCollectionSettings,
    showCollectionSettingsPage,
} from "../helpers/collectionSettings";
import {
    chooseFont,
    closeFontInformationPane,
    getChosenFont,
    getFontListItems,
    hoverChosenFontMark,
    hoverFontMarkInList,
    isFontInformationPaneInFront,
    isFontListOpen,
    openFontList,
    pickFont,
    waitForFontsInList,
    type FontChooserColor,
    type FontMark,
    type IFontChooserPlace,
} from "../helpers/fontChooser";
import type { Page } from "@playwright/test";

test.use({
    collectionSpec: { name: "settings-font-chooser", languages: ["en"] },
});

const kLanguage1 = "Written Language 1";

/**
 * The message at the top of the information pane for each kind of font, in the UI language Bloom
 * is running in (FontInformationPane.tsx).
 */
async function getPaneMessages(page: Page) {
    return {
        usable: await getUiString(
            page,
            "FontInformationPane.FontOkay",
            "The metadata inside this font indicates that it is legal to use for all Bloom purposes.",
        ),
        microsoft: await getUiString(
            page,
            "PublishTab.FontProblem.Microsoft",
            "This is a font supplied by Microsoft for use on your computer alone. Microsoft does not allow its fonts to be used freely on the web or distributed in eBooks. Please use a different font.",
        ),
        unknown: await getUiString(
            page,
            "FontInformationPane.FontUnknown",
            "Bloom cannot determine what rules govern the use of this font. Please read the license and make sure it allows embedding in ebooks and the web. Before publishing to bloomlibrary.org, you will probably have to make a special request to the Bloom team to investigate this font so that we can make sure we won't get in trouble for hosting it.",
        ),
    };
}

/**
 * Hover the mark beside a font in the open list, and assert that the pane opens in front of the
 * dialog with the given message. Closes the pane again.
 */
async function expectListMarkPane(
    page: Page,
    place: IFontChooserPlace,
    fontName: string,
    message: string,
): Promise<void> {
    const pane = await hoverFontMarkInList(page, fontName, place);
    expect(pane.name).toBe(fontName);
    expect(pane.message, `the pane for ${fontName}`).toBe(message);
    expect(
        await isFontInformationPaneInFront(page, place),
        `the pane for ${fontName} is hidden behind the Settings dialog`,
    ).toBe(true);
    await closeFontInformationPane(page, place);
}

/**
 * Choose a font from the list, and assert that the closed chooser shows it with the given mark in
 * the given colour, and that hovering that mark opens the pane in front with the given message.
 */
async function chooseFontAndExpectItsMark(
    page: Page,
    place: IFontChooserPlace,
    fontName: string,
    mark: FontMark,
    markColor: FontChooserColor,
    message: string,
): Promise<void> {
    if (!(await isFontListOpen(page, place))) await openFontList(page, place);
    await chooseFont(page, fontName, place);
    expect(await getCollectionSettingsLanguageFont(page, kLanguage1)).toBe(
        fontName,
    );
    const chosen = await getChosenFont(page, place);
    expect(chosen.name).toBe(fontName);
    expect(chosen.mark, `the chooser's mark for ${fontName}`).toBe(mark);
    expect(chosen.markColor, `the chooser's mark for ${fontName}`).toBe(
        markColor,
    );
    const pane = await hoverChosenFontMark(page, place);
    expect(pane.name).toBe(fontName);
    expect(pane.message, `the pane for the chosen ${fontName}`).toBe(message);
    expect(
        await isFontInformationPaneInFront(page, place),
        `the pane for the chosen ${fontName} is hidden behind the Settings dialog`,
    ).toBe(true);
    await closeFontInformationPane(page, place);
}

test("the Default Font chooser in Collection Settings shows the marks, information pane and coloured marks of the Format dialog's [Test Case ID 811]", async ({
    page,
}) => {
    // The manual test names Times New Roman; bloom-testing-inputs ships Luciole (which Bloom cannot
    // judge) for the nightly runner to install. Then any font of the kind.
    const usableFont = await pickFont(page, "usable", ["Andika"]);
    const microsoftFont = await pickFont(page, "microsoft", [
        "Times New Roman",
    ]);
    const unknownFont = await pickFont(page, "unknown", ["Luciole", "Euclid"]);
    const message = await getPaneMessages(page);

    await openCollectionSettings(page);
    await showCollectionSettingsPage(page, "Languages");
    const place = await collectionSettingsFontChooser(page, kLanguage1);
    const startingFont = await getCollectionSettingsLanguageFont(
        page,
        kLanguage1,
    );

    // The list: fonts Bloom may use have a check mark and are enabled; the rest are dimmed, with
    // a grey mark, but stay enabled so that they can be chosen.
    await openFontList(page, place);
    await waitForFontsInList(
        page,
        [usableFont, microsoftFont, unknownFont],
        place,
    );
    const items = await getFontListItems(page, place);
    const byName = (name: string) => {
        const item = items.find((i) => i.name === name);
        expect(item, `The list has no row for ${name}.`).toBeDefined();
        return item!;
    };
    expect(byName(usableFont).mark).toBe("check");
    expect(byName(microsoftFont).mark).toBe("exclamation");
    expect(byName(unknownFont).mark).toBe("question");
    for (const item of items.filter((i) => i.mark === "check")) {
        expect(item.markColor, `${item.name}'s check mark`).toBe("bloom-blue");
        expect(item.textColor, `${item.name} looks dimmed`).toBe("black");
        expect(item.enabled, `${item.name} is disabled`).toBe(true);
    }
    // The chosen font's row is drawn as the closed chooser draws it, so it is left out.
    const dimmed = items.filter(
        (i) => i.mark !== "check" && i.name !== startingFont,
    );
    expect(dimmed.length, "No font in the list is dimmed.").toBeGreaterThan(0);
    for (const item of dimmed) {
        expect(item.markColor, `${item.name}'s mark`).toBe("grey");
        expect(item.textColor, `${item.name}'s name`).toBe("grey");
        expect(item.enabled, `${item.name} is disabled`).toBe(true);
    }

    // Hovering a mark opens the information pane in front of the dialog, with the message the
    // Format dialog gives.
    await expectListMarkPane(page, place, usableFont, message.usable);
    await expectListMarkPane(page, place, microsoftFont, message.microsoft);
    await expectListMarkPane(page, place, unknownFont, message.unknown);

    // A dimmed font can be chosen, and the chooser then shows its mark in colour.
    await chooseFontAndExpectItsMark(
        page,
        place,
        microsoftFont,
        "exclamation",
        "red",
        message.microsoft,
    );
    await chooseFontAndExpectItsMark(
        page,
        place,
        unknownFont,
        "question",
        "gold",
        message.unknown,
    );
    await chooseFontAndExpectItsMark(
        page,
        place,
        usableFont,
        "check",
        "bloom-blue",
        message.usable,
    );

    await cancelCollectionSettings(page);
});
