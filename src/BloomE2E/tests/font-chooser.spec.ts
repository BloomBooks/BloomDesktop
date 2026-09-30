// The font chooser's marks: beside each font, whether Bloom may use it in a published book. Fonts
// Bloom may use get a check mark and stay enabled; the others are dimmed, with a grey exclamation
// mark (the metadata forbids embedding, or Microsoft supplied the font for this computer alone) or a
// grey question mark (Bloom cannot tell), yet can still be chosen. Hovering a mark opens a pane that
// says why, summarizes the font, and, for a font Bloom may not use or cannot judge, offers an icon
// that shows the raw metadata. The chosen font's mark shows in the closed chooser too, in colour.
// Automates the manual test "New Font Chooser" (Test Case ID 358).
//
// The chooser is the same component in the Format dialog and in the collection Settings dialog's
// Book Making tab. This file drives it in the Format dialog; the Settings dialog is a WinForms
// surface CDP cannot reach (AUTOMATION-DEBT.md), so the card's Settings route is on its manual
// portion.
//
// Which fonts a machine has varies, so the file asks Bloom for a font of each kind at run time,
// preferring the ones the manual test names, and fails plainly when a kind is missing.
//
// The tests are serial: each starts from the state the one before it left behind.

import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    getFontFamilyInGroup,
    goToPage,
    makeBookFromTemplate,
} from "../helpers/bookMaking";
import {
    chooseFont,
    closeFontInformationPane,
    getChosenFont,
    getFontListItems,
    getFontMetadata,
    hoverChosenFontMark,
    hoverFontMarkInList,
    isFontListOpen,
    openFontList,
    pickFont,
    showFontDetails,
    type FontChooserColor,
    type FontMark,
    type IFontInformation,
    type IFontMetadata,
} from "../helpers/fontChooser";
import {
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
} from "../helpers/formatDialog";

test.use({
    collectionSpec: { name: "font-chooser", languages: ["en"] },
});

test.describe.configure({ mode: "serial" });

/** The one text box on the page this file builds: the box under the picture. */
const TEXT_BOX = ".bloom-translationGroup";
const LANGUAGE = "en";

/** What the pane says at the top for each kind of font (FontInformationPane.tsx, in English). */
const MESSAGE = {
    usable: "The metadata inside this font indicates that it is legal to use for all Bloom purposes.",
    microsoft:
        "This is a font supplied by Microsoft for use on your computer alone. Microsoft does not allow its fonts to be used freely on the web or distributed in eBooks. Please use a different font.",
    notEmbeddable:
        "The metadata inside this font tells us that it may not be embedded for free in ebooks and the web. Please use a different font.",
    unknown:
        "Bloom cannot determine what rules govern the use of this font. Please read the license and make sure it allows embedding in ebooks and the web. Before publishing to bloomlibrary.org, you will probably have to make a special request to the Bloom team to investigate this font so that we can make sure we won't get in trouble for hosting it.",
};

/** The fonts this run uses, one of each kind, chosen from what the machine has. */
let usableFont: string;
let microsoftFont: string;
let notEmbeddableFont: string;
let unknownFont: string;

/**
 * Assert that the pane summarizes the font the way its metadata says: its name, its styles and
 * version when it has them, and a link for every URL the metadata carries.
 */
const expectPaneToSummarize = (pane: IFontInformation, font: IFontMetadata) => {
    expect(pane.name).toBe(font.name);
    if (font.variants?.length)
        expect(pane.text).toContain(font.variants.join(", "));
    if (font.version) expect(pane.text).toContain(font.version);
    const expectedHrefs = [
        font.designerURL,
        font.manufacturerURL,
        font.licenseURL,
    ].filter((url): url is string => !!url);
    expect(pane.links.map((l) => l.href).sort()).toEqual(
        [...expectedHrefs].sort(),
    );
};

/**
 * Choose a font from the list and assert that it reached the text box, that the closed chooser shows
 * it with the given mark in the given colour, and that hovering that mark shows the given message.
 */
const chooseFontAndExpectItsMark = async (
    page: Parameters<typeof chooseFont>[0],
    fontName: string,
    mark: FontMark,
    markColor: FontChooserColor,
    message: string,
) => {
    if (!(await isFontListOpen(page))) await openFontList(page);
    await chooseFont(page, fontName);
    expect(await getFontFamilyInGroup(page, TEXT_BOX, LANGUAGE)).toBe(fontName);
    const chosen = await getChosenFont(page);
    expect(chosen.name).toBe(fontName);
    expect(chosen.mark).toBe(mark);
    expect(chosen.markColor).toBe(markColor);
    const pane = await hoverChosenFontMark(page);
    expect(pane.message).toBe(message);
    expectPaneToSummarize(pane, await getFontMetadata(page, fontName));
    await closeFontInformationPane(page);
};

test.describe("Font chooser", () => {
    test("builds a book, opens the Format dialog for a text box, and finds a font of each kind", async ({
        page,
    }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
        await addPage(page, "Basic Text & Image");
        const [textPage] = await getContentPages(page);
        await goToPage(page, textPage.id);

        // The manual test names Andika, Times New Roman, Baskerville Old Face and Euclid. A clean
        // Windows machine has neither of the last two, so bloom-testing-inputs ships Alef (which
        // Bloom calls unsuitable) and Luciole (unknown) for the nightly runner to install; prefer
        // those, then the card's fonts, then any font of the kind.
        usableFont = await pickFont(page, "usable", ["Andika"]);
        microsoftFont = await pickFont(page, "microsoft", ["Times New Roman"]);
        notEmbeddableFont = await pickFont(page, "not-embeddable", [
            "Alef",
            "Baskerville Old Face",
        ]);
        unknownFont = await pickFont(page, "unknown", ["Luciole", "Euclid"]);

        await clickInGroup(page, TEXT_BOX, LANGUAGE);
        await scrollFormatGearIntoView(page);
        await openFormatDialog(page);
        // The font chooser is on the Characters tab; the dialog opens on Style Name.
        await showFormatDialogTab(page, "characters");
    });

    test("fonts Bloom may use have a check mark and are enabled [Test Case ID 358]", async ({
        page,
    }) => {
        await openFontList(page);
        const items = await getFontListItems(page);
        const checked = items.filter((i) => i.mark === "check");
        expect(
            checked.length,
            "No font in the list has a check mark.",
        ).toBeGreaterThan(0);
        for (const item of checked) {
            expect(item.markColor, `${item.name}'s check mark`).toBe(
                "bloom-blue",
            );
            expect(item.textColor, `${item.name} looks dimmed`).toBe("black");
            expect(item.enabled, `${item.name} is disabled`).toBe(true);
        }
        expect(items.find((i) => i.name === usableFont)?.mark).toBe("check");
    });

    test("fonts Bloom may not use, or cannot judge, are dimmed with a grey mark but stay enabled [Test Case ID 358]", async ({
        page,
    }) => {
        const chosen = (await getChosenFont(page)).name;
        const items = await getFontListItems(page);
        const byName = (name: string) => {
            const item = items.find((i) => i.name === name);
            expect(item, `The list has no row for ${name}.`).toBeDefined();
            return item!;
        };
        expect(byName(microsoftFont).mark).toBe("exclamation");
        expect(byName(notEmbeddableFont).mark).toBe("exclamation");
        expect(byName(unknownFont).mark).toBe("question");
        // Every unmarked font is dimmed, except the chosen one, whose row is drawn as the chooser
        // draws it.
        for (const item of items.filter(
            (i) => i.mark !== "check" && i.name !== chosen,
        )) {
            expect(item.markColor, `${item.name}'s mark`).toBe("grey");
            expect(item.textColor, `${item.name}'s name`).toBe("grey");
            expect(item.enabled, `${item.name} is disabled`).toBe(true);
        }
    });

    test("hovering a check mark says the font is legal for all Bloom purposes, with a summary and links [Test Case ID 358]", async ({
        page,
    }) => {
        const pane = await hoverFontMarkInList(page, usableFont);
        expect(pane.message).toBe(MESSAGE.usable);
        expect(pane.hasDetailsIcon).toBe(false);
        expectPaneToSummarize(pane, await getFontMetadata(page, usableFont));
        expect(pane.links.length, "The summary has no link.").toBeGreaterThan(
            0,
        );
        for (const link of pane.links)
            expect(link.href, `${link.text} links to ${link.href}`).toMatch(
                /^https?:\/\/[^/]+\.[^/]+/,
            );
        await closeFontInformationPane(page);
    });

    test("hovering a Microsoft font's mark says it may be used on this computer alone [Test Case ID 358]", async ({
        page,
    }) => {
        const pane = await hoverFontMarkInList(page, microsoftFont);
        expect(pane.message).toBe(MESSAGE.microsoft);
        expect(pane.hasDetailsIcon).toBe(true);
        expectPaneToSummarize(pane, await getFontMetadata(page, microsoftFont));
        await closeFontInformationPane(page);
    });

    test("hovering the mark of a font whose metadata forbids embedding says so [Test Case ID 358]", async ({
        page,
    }) => {
        const pane = await hoverFontMarkInList(page, notEmbeddableFont);
        expect(pane.message).toBe(MESSAGE.notEmbeddable);
        expect(pane.hasDetailsIcon).toBe(true);
        expectPaneToSummarize(
            pane,
            await getFontMetadata(page, notEmbeddableFont),
        );
        await closeFontInformationPane(page);
    });

    test("hovering a question mark says Bloom cannot determine the font's rules [Test Case ID 358]", async ({
        page,
    }) => {
        const pane = await hoverFontMarkInList(page, unknownFont);
        expect(pane.message).toBe(MESSAGE.unknown);
        expect(pane.hasDetailsIcon).toBe(true);
        expectPaneToSummarize(pane, await getFontMetadata(page, unknownFont));
        // The pane stays open for the next test, which uses its information icon.
    });

    test("the information icon shows the font's raw metadata [Test Case ID 358]", async ({
        page,
    }) => {
        const details = await showFontDetails(page);
        const font = await getFontMetadata(page, unknownFont);
        expect(details).toContain(`name: ${font.name}`);
        expect(details).toContain(`license: ${font.license}`);
        expect(details).toContain(`licenseURL: ${font.licenseURL}`);
        expect(details).toContain(`copyright:`);
        expect(details).toContain(`fsType:`);
        expect(details).toContain(
            `determinedSuitability: ${font.determinedSuitability}`,
        );
        expect(details).toContain(
            `determinedSuitabilityNotes: ${font.determinedSuitabilityNotes}`,
        );
        await closeFontInformationPane(page);
    });

    test("a dimmed Microsoft font can still be chosen, and the chooser then shows a red exclamation mark [Test Case ID 358]", async ({
        page,
    }) => {
        await chooseFontAndExpectItsMark(
            page,
            microsoftFont,
            "exclamation",
            "red",
            MESSAGE.microsoft,
        );
    });

    test("a font Bloom cannot judge can be chosen, and the chooser then shows a gold question mark [Test Case ID 358]", async ({
        page,
    }) => {
        await chooseFontAndExpectItsMark(
            page,
            unknownFont,
            "question",
            "gold",
            MESSAGE.unknown,
        );
    });

    test("a font Bloom may use, once chosen, shows its check mark in the chooser [Test Case ID 358]", async ({
        page,
    }) => {
        await chooseFontAndExpectItsMark(
            page,
            usableFont,
            "check",
            "bloom-blue",
            MESSAGE.usable,
        );
    });
});
