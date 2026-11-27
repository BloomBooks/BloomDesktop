// An ePUB carries the fonts its text needs, so that a reader shows the book in them: Bloom copies
// each font file into the ePUB's fonts folder and declares it in css/fonts.css with an @font-face
// rule (EpubMaker.EmbedFonts). It embeds only the fonts and faces that the published text is drawn
// in: not the font of a language that has no text in the book, not the font of a style that no
// text uses, and a bold or italic face only where some text is bold or italic.
//
// Automates the manual test "ePUB embedded fonts" (Test Case ID 164). The collection gives each of
// its three languages a font of its own, and the book has text in Language 1 and Language 2 only.
// Four ePUBs are made from it: one of plain text, one after some Language 1 text is marked bold
// and some italic, one after a style that uses the Language 3 font is defined but left unused, and
// one after the book gets Language 3 text as well. The first ePUB shows that the Language 3 font
// stays out, and that nothing else in the book asks for a bold or italic face. The second shows
// that marking text adds exactly the faces it needs. The third shows that the unused style adds no
// font, since it is the only change from the second. The fourth shows that Bloom does embed the
// Language 3 font once text uses it, so that the first and third show something.
//
// Language 1 and Language 2 use Andika and ABeeZee, which ship with Bloom (DistFiles/fonts), so
// every machine has them; Andika has bold and italic faces. Language 3 needs a third font that
// Bloom may embed. The nightly runner has Luciole for that (bloom-testing-inputs fonts/); another
// machine uses any such font it has.
//
// The text is marked by selecting a word and pressing Ctrl+B or Ctrl+I. Pressing Ctrl+B at the
// caret and then typing leaves CKEditor's zero-width filler character in the box, and with it
// there the page never stops sending itself to Bloom (BL-17186).
//
// The ePUB is read from the ePUB screen's preview, which stages the very files that Save... zips.
// Save... itself opens the operating system's save dialog, which no test can answer.

import { type Page } from "@playwright/test";
import * as fs from "fs";
import * as Path from "path";
import { expect, test } from "../fixtures/bloomTest";
import {
    addPage,
    clickInGroup,
    getContentPages,
    goToPage,
    makeBookFromTemplate,
    setContentLanguages,
    typeInGroup,
    waitForSnapshotStreamIdle,
} from "../helpers/bookMaking";
import {
    kEnterpriseSubscriptionCode,
    restartWithCollectionSettings,
    type ICollectionSettings,
} from "../helpers/collectionSettings";
import {
    getEpubPreviewContentFolder,
    openEpubPublishScreen,
    readEpubPreviewContentFile,
    readEpubPreviewPackage,
    showEpubPreview,
} from "../helpers/epubPublish";
import { apiGetJson } from "../helpers/api";
import { getFontsMetadata, type IFontMetadata } from "../helpers/fontChooser";
import {
    applyStyleById,
    chooseFont,
    closeFormatDialog,
    createStyle,
    getStyleIdShown,
    openFormatDialog,
    scrollFormatGearIntoView,
    showFormatDialogTab,
} from "../helpers/formatDialog";
import { switchTab } from "../helpers/workspace";

const L1 = "en";
const L2 = "fr";
const L3 = "es";
const L1_FONT = "Andika";
const L2_FONT = "ABeeZee";
/**
 * Fonts for Language 3, in the order the test prefers them: the one the nightly runner has, then
 * SIL fonts that a developer's machine often has.
 */
const PREFERRED_L3_FONTS = ["Luciole", "Charis", "Charis SIL", "Doulos SIL"];

const GROUP = ".bloom-translationGroup";
const TEXT_BEFORE_BOLD = "Plain words ";
const BOLD_TEXT = "strong";
const ITALIC_TEXT = "slanted";
const L1_TEXT = `${TEXT_BEFORE_BOLD}${BOLD_TEXT} and ${ITALIC_TEXT}`;
const L2_TEXT = "Des mots simples";
const L3_TEXT = "Palabras sencillas";
/** The style the test defines and leaves unused. Style names are letters only. */
const UNUSED_STYLE = "Unused";
const NORMAL_STYLE = "normal";

const SETTINGS: ICollectionSettings = {
    languages: [L1, L2, L3],
    // Publishing an ePUB needs Pro.
    subscriptionCode: kEnterpriseSubscriptionCode,
    fonts: [L1_FONT, L2_FONT],
};

test.use({
    collectionSpec: { name: "epub-embedded-fonts", ...SETTINGS },
});

/** One @font-face rule of the ePUB's css/fonts.css. */
interface IFontFace {
    family: string;
    weight: string;
    style: string;
    /** The font file the rule points to, by its path in the ePUB's content folder. */
    file: string;
}

/**
 * A face the ePUB is expected to declare, and the font file it must point to. The file is known for
 * the fonts that ship with Bloom (DistFiles/fonts) and left out for the Language 3 font, whose file
 * name depends on the machine.
 */
type ExpectedFace = Omit<IFontFace, "file"> & { file?: string };

const REGULAR_FACES: ExpectedFace[] = [
    {
        family: L1_FONT,
        weight: "normal",
        style: "normal",
        file: "fonts/Andika-Regular.woff2",
    },
    {
        family: L2_FONT,
        weight: "normal",
        style: "normal",
        file: "fonts/ABeeZee-Regular.woff2",
    },
];
const FACES_WITH_BOLD_AND_ITALIC: ExpectedFace[] = [
    ...REGULAR_FACES,
    {
        family: L1_FONT,
        weight: "bold",
        style: "normal",
        file: "fonts/Andika-Bold.woff2",
    },
    {
        family: L1_FONT,
        weight: "normal",
        style: "italic",
        file: "fonts/Andika-Italic.woff2",
    },
];

test("an ePUB embeds the fonts and faces its text uses, and no others [Test Case ID 164]", async ({
    bloomApp,
    step,
}) => {
    test.setTimeout(420000);

    const l3Font = await step(
        "Give each of the collection's three languages a font of its own",
        async () => {
            const font = await pickEmbeddableFont(bloomApp.page, [
                L1_FONT,
                L2_FONT,
            ]);
            await restartWithCollectionSettings(bloomApp, {
                ...SETTINGS,
                fonts: [L1_FONT, L2_FONT, font],
            });
            return font;
        },
    );
    if (!l3Font)
        throw new Error("The step that picks a Language 3 font returned none.");
    const page = bloomApp.page;

    const contentPageId = await step(
        "Make a book with plain text in Language 1 and Language 2, and none in Language 3",
        async () => {
            await makeBookFromTemplate(page, "Basic Book");
            await addPage(page, "Just Text", 1);
            await setContentLanguages(page, [L1, L2]);
            const [contentPage] = await getContentPages(page);
            await goToPage(page, contentPage.id);
            await typeInGroup(page, GROUP, L1, L1_TEXT);
            await typeInGroup(page, GROUP, L2, L2_TEXT);
            return contentPage.id;
        },
    );
    if (!contentPageId)
        throw new Error(
            "The step that makes the book returned no content page.",
        );

    await step(
        "The ePUB embeds the regular faces of the Language 1 and Language 2 fonts only",
        async () => {
            await expectEpubFontFaces(page, l3Font, REGULAR_FACES);
        },
    );

    await step("Mark some Language 1 text bold and some italic", async () => {
        await switchTab(page, "edit");
        await goToPage(page, contentPageId);
        // Select each word with the keyboard and press Ctrl+B or Ctrl+I, as a person marks text
        // already typed. The box holds L1_TEXT: the italic word last, the bold word after
        // TEXT_BEFORE_BOLD.
        const box = await clickInGroup(page, GROUP, L1);
        await box.press("End");
        await pressRepeatedly(page, "Shift+ArrowLeft", ITALIC_TEXT.length);
        await page.keyboard.press("Control+i");
        await page.keyboard.press("Home");
        await pressRepeatedly(page, "ArrowRight", TEXT_BEFORE_BOLD.length);
        await pressRepeatedly(page, "Shift+ArrowRight", BOLD_TEXT.length);
        await page.keyboard.press("Control+b");
        await expect(
            box.locator("strong"),
            "Ctrl+B should have made the selected word bold, and nothing else.",
        ).toHaveText([BOLD_TEXT]);
        await expect(
            box.locator("em"),
            "Ctrl+I should have made the selected word italic, and nothing else.",
        ).toHaveText([ITALIC_TEXT]);
        await expect(
            box,
            "Marking words bold and italic should leave the text as it was.",
        ).toHaveText(L1_TEXT);
        await waitForSnapshotStreamIdle(page);
    });

    await step(
        "The ePUB adds the bold and the italic face of the Language 1 font, and nothing else",
        async () => {
            await expectEpubFontFaces(page, l3Font, FACES_WITH_BOLD_AND_ITALIC);
        },
    );

    await step(
        "Define a style in the Language 3 font, and leave no text in it",
        async () => {
            await switchTab(page, "edit");
            await goToPage(page, contentPageId);
            await clickInGroup(page, GROUP, L1);
            await scrollFormatGearIntoView(page);
            await openFormatDialog(page);
            await showFormatDialogTab(page, "styleName");
            await createStyle(page, UNUSED_STYLE);
            await showFormatDialogTab(page, "characters");
            await chooseFont(page, l3Font);
            await showFormatDialogTab(page, "styleName");
            await applyStyleById(page, NORMAL_STYLE);
            expect(await getStyleIdShown(page)).toBe(NORMAL_STYLE);
            await closeFormatDialog(page);
        },
    );

    await step(
        "The ePUB embeds the same faces as before: the unused style's font stays out",
        async () => {
            await expectEpubFontFaces(page, l3Font, FACES_WITH_BOLD_AND_ITALIC);
            // The style itself is in the ePUB, with its font: what stays out is only the font.
            expect(
                await findEpubStyleFontFamilies(page, UNUSED_STYLE, L1),
                `The ePUB's pages should define the ${UNUSED_STYLE} style for ${L1} in ${l3Font}, ` +
                    "or leaving its font out proves nothing.",
            ).toContain(l3Font);
        },
    );

    await step("Give the book text in Language 3 too", async () => {
        await switchTab(page, "edit");
        await goToPage(page, contentPageId);
        await setContentLanguages(page, [L1, L2, L3]);
        await typeInGroup(page, GROUP, L3, L3_TEXT);
    });

    await step(
        "The ePUB now embeds the Language 3 font as well, so it was left out only for want of text",
        async () => {
            await expectEpubFontFaces(page, l3Font, [
                ...FACES_WITH_BOLD_AND_ITALIC,
                { family: l3Font, weight: "normal", style: "normal" },
            ]);
        },
    );
});

/** Press `key` `times` times, each press going to the element that has the focus. */
async function pressRepeatedly(
    page: Page,
    key: string,
    times: number,
): Promise<void> {
    for (let i = 0; i < times; i++) await page.keyboard.press(key);
}

/**
 * The name of a font, other than those in `exclude`, that Bloom may embed in an ePUB and that has a
 * regular face: the first of PREFERRED_L3_FONTS that this machine has, otherwise the first such
 * font in alphabetical order. Bloom embeds a font whose license it judged fine ("ok") and one it
 * could not judge ("unknown"), and leaves out one it judged unsuitable
 * (PublishHelper.CheckFontsForEmbedding).
 *
 * Bloom reads each font's metadata in the background at startup, one font at a time, in the order
 * of fonts/names (FontsApi.GetAllFontMetadata), and fonts/metadata answers with the fonts read so
 * far. So before judging a font, this waits until Bloom has read past it: fonts/metadata lists that
 * font or one that comes after it in fonts/names. (A font of which Bloom finds no regular face gets
 * no metadata at all, so "lists that font" alone is not enough.) When a minute passes with no font
 * at or after it listed, Bloom has nothing to give for that font or any after it.
 */
async function pickEmbeddableFont(
    page: Page,
    exclude: string[],
): Promise<string> {
    const names = (await apiGetJson<{ fonts: string[] }>(page, "fonts/names"))
        .fonts;
    const isEmbeddable = (font: IFontMetadata): boolean =>
        !!font.name &&
        !exclude.includes(font.name) &&
        (font.determinedSuitability === "ok" ||
            font.determinedSuitability === "unknown") &&
        !!font.variants?.includes("regular");
    /**
     * fonts/metadata, once Bloom has read past the font at `index` in `names`; undefined when Bloom
     * lists no font at or after it within a minute, which leaves no metadata for any of them.
     */
    const metadataReadPast = async (
        index: number,
    ): Promise<IFontMetadata[] | undefined> => {
        let fonts: IFontMetadata[] = [];
        const readPast = () =>
            fonts.some((font) => names.indexOf(font.name) >= index);
        await expect
            .poll(
                async () => {
                    fonts = await getFontsMetadata(page);
                    return readPast();
                },
                { timeout: 60000 },
            )
            .toBe(true)
            .catch(() => undefined);
        return readPast() ? fonts : undefined;
    };

    for (const name of PREFERRED_L3_FONTS) {
        const index = names.indexOf(name);
        if (index < 0) continue;
        const font = (await metadataReadPast(index))?.find(
            (f) => f.name === name,
        );
        if (font && isEmbeddable(font)) return name;
    }
    // No preferred font: the first embeddable font in the order Bloom reads them. Once one is
    // listed, Bloom has read every font before it, so no earlier one can still appear.
    for (let index = 0; index < names.length; index++) {
        const fonts = await metadataReadPast(index);
        if (!fonts) break;
        const font = fonts.find((f) => f.name === names[index]);
        if (font && isEmbeddable(font)) return font.name;
    }
    throw new Error(
        `This machine has no font besides ${exclude.join(" and ")} that Bloom may embed in an ` +
            "ePUB, so the test cannot give Language 3 one.",
    );
}

/**
 * Make the ePUB (Preview on the ePUB screen) and expect its css/fonts.css to declare exactly the
 * `expected` faces, in any order, each pointing to its expected file where one is given; and expect
 * both the manifest and the staged fonts folder, which Save... zips, to hold exactly the font files
 * those faces point to. `l3Font` is named in the failure message, since it is the font that must
 * stay out.
 */
async function expectEpubFontFaces(
    page: Page,
    l3Font: string,
    expected: ExpectedFace[],
): Promise<void> {
    await openEpubPublishScreen(page);
    await showEpubPreview(page);
    const faces = parseFontFaces(
        await readEpubPreviewContentFile(page, "css/fonts.css"),
    );
    const key = (f: ExpectedFace) => `${f.family} ${f.weight} ${f.style}`;
    expect(
        faces.map(key).sort(),
        "The ePUB's fonts.css should declare these faces and no others; in particular nothing " +
            `in ${l3Font}, which no published text uses. It has: ${JSON.stringify(faces)}.`,
    ).toEqual(expected.map(key).sort());
    for (const face of expected.filter((f) => f.file))
        expect(
            faces.find((f) => key(f) === key(face))?.file,
            `The ${key(face)} face should point to its own font file.`,
        ).toBe(face.file);
    const filesOfFaces = [...new Set(faces.map((f) => f.file))].sort();
    expect(
        await readManifestFontFiles(page),
        "The ePUB's manifest should list exactly the font files that its fonts.css points to.",
    ).toEqual(filesOfFaces);
    expect(
        readStagedFontFiles(page),
        "The staged ePUB's fonts folder should hold exactly the font files that its fonts.css " +
            "points to.",
    ).toEqual(filesOfFaces);
}

/**
 * The files in the fonts folder of the ePUB the preview is showing, as Bloom staged them on disk,
 * by their paths in the content folder ("fonts/Andika-Regular.woff2"), sorted. None when the ePUB
 * has no fonts folder.
 */
function readStagedFontFiles(page: Page): string[] {
    const folder = Path.join(getEpubPreviewContentFolder(page), "fonts");
    if (!fs.existsSync(folder)) return [];
    return fs
        .readdirSync(folder)
        .map((name) => `fonts/${name}`)
        .sort();
}

/** The @font-face rules of a fonts.css that EpubMaker.AddFontFace wrote. */
function parseFontFaces(css: string): IFontFace[] {
    const rules = css.match(/@font-face\s*\{[^}]*\}/g) ?? [];
    return rules.map((rule) => {
        const read = (pattern: RegExp, what: string): string => {
            const match = rule.match(pattern);
            if (!match)
                throw new Error(
                    `An @font-face rule of the ePUB's fonts.css has no ${what}: ${rule}`,
                );
            return match[1].trim();
        };
        return {
            family: read(/font-family:\s*'([^']+)'/, "font-family"),
            weight: read(/font-weight:\s*([^;]+);/, "font-weight"),
            style: read(/font-style:\s*([^;]+);/, "font-style"),
            // fonts.css is in css/, beside fonts/, so its urls start with "../".
            file: read(/src:\s*url\('\.\.\/([^']+)'\)/, "src url"),
        };
    });
}

/**
 * The font families that the pages of the ePUB the preview is showing give the style `styleId` for
 * the language `lang`, from the rules `.<styleId>-style[lang="<lang>"] {...}` that Bloom copies
 * from the book's userModifiedStyles into each page's head. Quotes around a family are dropped.
 */
async function findEpubStyleFontFamilies(
    page: Page,
    styleId: string,
    lang: string,
): Promise<string[]> {
    const opf = await readEpubPreviewPackage(page);
    const pages = [...opf.matchAll(/<item\b[^>]*\bhref="([^"]+\.xhtml)"/g)].map(
        (match) => match[1],
    );
    const rule = new RegExp(
        `\\.${styleId}-style\\[lang="${lang}"\\]\\s*\\{[^}]*?font-family:\\s*([^;}!]+)`,
        "g",
    );
    const families = new Set<string>();
    for (const xhtml of pages)
        for (const match of (
            await readEpubPreviewContentFile(page, xhtml)
        ).matchAll(rule))
            families.add(match[1].trim().replace(/^["']|["']$/g, ""));
    return [...families];
}

/** The files of the ePUB's fonts folder, as its manifest (content.opf) lists them, sorted. */
async function readManifestFontFiles(page: Page): Promise<string[]> {
    const opf = await readEpubPreviewPackage(page);
    return [...opf.matchAll(/<item\b[^>]*\bhref="(fonts\/[^"]+)"/g)]
        .map((match) => match[1])
        .sort();
}
