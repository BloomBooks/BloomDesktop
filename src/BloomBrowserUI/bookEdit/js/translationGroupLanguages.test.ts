import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
    languageClassesOf,
    languagesShownBySetting,
    setUpBoxesBuiltAfterLoad,
    showLanguagesInTranslationGroup,
} from "./translationGroupLanguages";
import { insertInlineImage } from "./inlineImages";
import { ckeditableSelector } from "../../utils/shared";
import { addLanguageTagsToBoxes } from "./bloomEditing";

// The steps of setUpBoxesBuiltAfterLoad that need the real editing page (CKEditor, the toolbox,
// jQuery handlers on a live page). The inline picture step is left real.
vi.mock("./bloomEditing", () => ({
    activateLongPressFor: vi.fn(),
    addEditKeyHandlersToBoxes: vi.fn(),
    addLanguageTagsToBoxes: vi.fn(),
    addPlaceholdersFromLabels: vi.fn(),
    addPlainTextPasteHandler: vi.fn(),
    attachToCkEditor: vi.fn(),
    configureElementsForTools: vi.fn(),
    keepDataTextUpToDate: vi.fn(),
    matchGroupFontSizesToLanguage1: vi.fn(),
}));
vi.mock("../bloomField/BloomField", async (importOriginal) => {
    const actual =
        await importOriginal<typeof import("../bloomField/BloomField")>();
    return {
        ...actual,
        default: { ...actual.default, ManageField: vi.fn() },
    };
});

// A group as it is once the page has been set up for editing: its boxes are typable and carry
// what CKEditor adds to a box it manages.
function makeGroup(boxesHtml: string): HTMLElement {
    const group = document.createElement("div");
    group.className = "bloom-translationGroup";
    group.innerHTML = boxesHtml;
    document.body.appendChild(group);
    return group;
}

function langsOf(group: HTMLElement): string[] {
    return Array.from(
        group.querySelectorAll<HTMLElement>(":scope > .bloom-editable"),
    ).map((box) => box.getAttribute("lang")!);
}

function shownLangsOf(group: HTMLElement): string[] {
    return Array.from(
        group.querySelectorAll<HTMLElement>(
            ":scope > .bloom-editable.bloom-visibility-code-on",
        ),
    ).map((box) => box.getAttribute("lang")!);
}

const kEditedEnglishBox =
    "<div class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on cke_editable cke_editable_inline' " +
    "lang='en' id='i123' dir='ltr' contenteditable='true' role='textbox' aria-label='Rich Text Editor' " +
    "title='Rich Text Editor, editor1' spellcheck='false' tabindex='0' data-languagetipcontent='English' " +
    "data-book='bookTitle' data-placeholder='Title'><p>Hello</p></div>";

beforeEach(() => {
    document.body.innerHTML = "";
});

describe("showLanguagesInTranslationGroup", () => {
    it("shows the languages asked for and hides the rest, adding no box when each has one", () => {
        const group = makeGroup(
            "<div class='bloom-editable bloom-visibility-code-on' lang='en'><p>a</p></div>" +
                "<div class='bloom-editable' lang='fr'><p>b</p></div>",
        );

        const { added, shown } = showLanguagesInTranslationGroup(group, ["fr"]);

        expect(added).toEqual([]);
        expect(shown).toEqual([group.querySelector("[lang='fr']")]);
        expect(shownLangsOf(group)).toEqual(["fr"]);
        expect(
            group
                .querySelector("[lang='en']")!
                .classList.contains("bloom-visibility-code-off"),
        ).toBe(true);
    });

    it("adds a box for a missing language from an edited box, without that box's language or editing", () => {
        const group = makeGroup(kEditedEnglishBox);

        const { added, shown } = showLanguagesInTranslationGroup(group, [
            "en",
            "ar",
        ]);

        expect(added.length).toBe(1);
        // An added box is set up, tag included, by setUpBoxesBuiltAfterLoad, so it is not also
        // among the boxes shown again.
        expect(shown).toEqual([]);
        const box = added[0];
        expect(box.getAttribute("lang")).toBe("ar");
        expect(box.innerHTML).toBe("<p></p>");
        // What every box of the group shares stays.
        expect(box.classList.contains("normal-style")).toBe(true);
        expect(box.getAttribute("data-book")).toBe("bookTitle");
        expect(box.getAttribute("data-placeholder")).toBe("Title");
        // What belongs to the English box's language or to its editing does not.
        for (const attribute of [
            "id",
            "dir",
            "contenteditable",
            "role",
            "aria-label",
            "title",
            "spellcheck",
            "tabindex",
            "data-languagetipcontent",
        ]) {
            expect(box.hasAttribute(attribute), attribute).toBe(false);
        }
        expect(
            Array.from(box.classList).filter(
                (name) =>
                    name.startsWith("cke_") || name.startsWith("bloom-content"),
            ),
        ).toEqual([]);
        expect(shownLangsOf(group)).toEqual(["en", "ar"]);
    });

    it("adds a box from one with a Talking Book recording without claiming that recording", () => {
        const group = makeGroup(
            "<div class='bloom-editable normal-style bloom-content1 bloom-visibility-code-on audio-sentence bloom-postAudioSplit ui-audioCurrent' " +
                "lang='en' id='i0a8b' data-audiorecordingmode='TextBox' data-duration='2.5' " +
                "data-audiorecordingendtimes='1.2 2.5'><p>Hello there.</p></div>",
        );
        const source = group.querySelector("[lang='en']")!;
        // sanity check: the source box carries every piece of recording state
        expect(source.getAttribute("data-audiorecordingmode")).toBe("TextBox");
        expect(source.classList.contains("audio-sentence")).toBe(true);

        const {
            added: [box],
        } = showLanguagesInTranslationGroup(group, ["fr"]);

        for (const attribute of [
            "id",
            "data-audiorecordingmode",
            "data-duration",
            "data-audiorecordingendtimes",
        ]) {
            expect(box.hasAttribute(attribute), attribute).toBe(false);
        }
        for (const name of [
            "audio-sentence",
            "bloom-postAudioSplit",
            "ui-audioCurrent",
        ]) {
            expect(box.classList.contains(name), name).toBe(false);
        }
        expect(box.classList.contains("normal-style")).toBe(true);
        expect(box.innerHTML).toBe("<p></p>");
        // the source box keeps its recording
        expect(source.getAttribute("data-duration")).toBe("2.5");
    });

    it("prefers the group's z box as the one to copy", () => {
        const group = makeGroup(
            kEditedEnglishBox +
                "<div class='bloom-editable Bubble-style' lang='z'><p></p></div>",
        );

        const {
            added: [box],
        } = showLanguagesInTranslationGroup(group, ["fr"]);

        expect(box.classList.contains("Bubble-style")).toBe(true);
        expect(box.hasAttribute("data-book")).toBe(false);
    });

    it("puts a new box among the others in the order given, and last when its language is not in it", () => {
        const group = makeGroup(
            "<div class='bloom-editable' lang='tpi'><p></p></div>" +
                "<div class='bloom-editable' lang='es'><p></p></div>",
        );

        showLanguagesInTranslationGroup(group, ["en"], ["tpi", "en", "es"]);
        expect(langsOf(group)).toEqual(["tpi", "en", "es"]);

        showLanguagesInTranslationGroup(group, ["de"], ["tpi", "en", "es"]);
        expect(langsOf(group)).toEqual(["tpi", "en", "es", "de"]);
    });

    // A table menu calls this for every cell; a box already showing as asked must not be touched,
    // since every class change makes the browser work out the page's styles again.
    it("changes no box that already shows or hides as asked", () => {
        // Each box has the classes page load gives it (in the tests' page settings, vitest.setup.ts,
        // English is the toolbar's first language and French its third), so none needs changing.
        const group = makeGroup(
            "<div class='bloom-editable bloom-content1 bloom-visibility-code-on' lang='en'><p>a</p></div>" +
                "<div class='bloom-editable bloom-content3 bloom-contentNational2 bloom-visibility-code-off' lang='fr'><p>b</p></div>",
        );
        const observer = new MutationObserver(() => {});
        observer.observe(group, { attributes: true, subtree: true });

        showLanguagesInTranslationGroup(group, ["en"]);

        expect(observer.takeRecords()).toEqual([]);
        // sanity check: the observer does see a change when one is needed
        showLanguagesInTranslationGroup(group, ["fr"]);
        expect(observer.takeRecords().length).toBeGreaterThan(0);
        expect(shownLangsOf(group)).toEqual(["fr"]);
        observer.disconnect();
    });

    it("adds one box for a language asked for twice", () => {
        const group = makeGroup(
            "<div class='bloom-editable' lang='tpi'><p></p></div>",
        );

        const { added } = showLanguagesInTranslationGroup(group, ["en", "en"]);

        expect(added.length).toBe(1);
        expect(langsOf(group)).toEqual(["tpi", "en"]);
    });

    // Tagging a box measures it, so tagging here would lay the page out once per group when a
    // table menu changes every cell: about 3 s for 30 cells on a slow computer.
    it("tags no box itself", () => {
        const group = makeGroup(
            "<div class='bloom-editable bloom-visibility-code-on' lang='en'><p>a</p></div>" +
                "<div class='bloom-editable bloom-visibility-code-off' lang='fr'><p>b</p></div>",
        );
        const tag = vi.mocked(addLanguageTagsToBoxes);
        tag.mockClear();

        showLanguagesInTranslationGroup(group, ["fr", "es"]);

        expect(tag).not.toHaveBeenCalled();
    });
});

// A collection of Tok Pisin, English and Spanish, whose toolbar ticks English, then Spanish, so
// that the toolbar's first language is not the collection's.
function useBilingualToolbarWithEnglishFirst(): void {
    (globalThis as unknown as { GetSettings: unknown }).GetSettings = () => ({
        languageForNewTextBoxes: "en",
        currentCollectionLanguage2: "es",
        collectionLanguage1: "tpi",
        metadataLanguage1: "en",
        metadataLanguage2: "es",
    });
}

describe("the classes page load gives a box in each language", () => {
    let savedGetSettings: unknown;
    beforeEach(() => {
        savedGetSettings = (globalThis as unknown as { GetSettings: unknown })
            .GetSettings;
        useBilingualToolbarWithEnglishFirst();
    });
    afterEach(() => {
        (globalThis as unknown as { GetSettings: unknown }).GetSettings =
            savedGetSettings;
    });

    it("numbers the toolbar's languages and marks the collection's", () => {
        expect(languageClassesOf("en")).toEqual([
            "bloom-content1",
            "bloom-contentNational1",
        ]);
        expect(languageClassesOf("es")).toEqual([
            "bloom-content2",
            "bloom-contentNational2",
        ]);
        expect(languageClassesOf("tpi")).toEqual(["bloom-contentCL1"]);
        expect(languageClassesOf("fr")).toEqual([]);
    });

    // Page load attaches the rich-text editor only to boxes that ckeditableSelector matches, which
    // goes by these classes; a CL1 box whose language the toolbar leaves out has only
    // bloom-contentCL1.
    it("marks a box in the collection's first language so that page load gives it the rich-text editor", () => {
        const box = document.createElement("div");
        box.setAttribute("contenteditable", "true");
        box.className = ["bloom-editable", ...languageClassesOf("tpi")].join(
            " ",
        );
        // sanity check: the toolbar leaves this language out, so it has no bloom-content1/2/3
        expect(box.className).not.toMatch(/bloom-content\d/);

        expect(box.matches(ckeditableSelector)).toBe(true);
    });

    it("reads a setting split at commas and spaces only, as C# does", () => {
        expect(languagesShownBySetting("CL1, N1")).toEqual(["tpi", "en"]);
        // A tab is not a separator, so this is one (unknown) tag, as C# reads it.
        expect(languagesShownBySetting("CL1\tN1")).toEqual(["CL1\tN1"]);
    });

    it("gives a box it adds the classes of its language, not those of the box it was made from", () => {
        const group = makeGroup(kEditedEnglishBox);
        // sanity check: the box copied from is the toolbar's first language
        expect(
            group
                .querySelector("[lang='en']")!
                .classList.contains("bloom-content1"),
        ).toBe(true);

        const { added } = showLanguagesInTranslationGroup(group, ["en", "tpi"]);

        expect(added.map((box) => box.getAttribute("lang"))).toEqual(["tpi"]);
        expect(
            Array.from(added[0].classList).filter((name) =>
                name.startsWith("bloom-content"),
            ),
        ).toEqual(["bloom-contentCL1"]);
    });

    it("shows for each setting the languages page load would", () => {
        expect(languagesShownBySetting(null)).toEqual(["en", "es"]);
        expect(languagesShownBySetting("auto")).toEqual(["en", "es"]);
        expect(languagesShownBySetting("V")).toEqual(["en"]);
        expect(languagesShownBySetting("CL1")).toEqual(["tpi"]);
        expect(languagesShownBySetting("CL1,N1")).toEqual(["tpi", "en"]);
        expect(languagesShownBySetting("CL2 CL3")).toEqual(["en", "es"]);
        expect(languagesShownBySetting("L2")).toEqual(["es"]);
        // L3 names no language here, since the toolbar ticks only two.
        expect(languagesShownBySetting("L3")).toEqual([]);
        expect(languagesShownBySetting("fr")).toEqual(["fr"]);
    });
});

describe("setUpBoxesBuiltAfterLoad", () => {
    // Inline pictures, like a table cell's languages, can be in a cell of a table made through
    // Change Layout, whose Languages menu adds boxes after the page has loaded.
    it("gives a box added to a group with an inline picture its copy of the picture", () => {
        const group = makeGroup(
            "<div class='bloom-editable bloom-visibility-code-on' lang='en'><p>Hello</p></div>",
        );
        const wrapper = insertInlineImage(group);
        const id = wrapper.getAttribute("data-bloom-inline-image-id");
        const picturesIn = (lang: string) =>
            Array.from(
                group.querySelectorAll<HTMLElement>(
                    `:scope > .bloom-editable[lang='${lang}'] [data-bloom-inline-image-id]`,
                ),
            ).map((picture) =>
                picture.getAttribute("data-bloom-inline-image-id"),
            );
        expect(
            id,
            "insertInlineImage should give the picture an id",
        ).toBeTruthy();
        expect(picturesIn("en")).toEqual([id]);
        expect(langsOf(group)).toEqual(["en"]);

        const { added } = showLanguagesInTranslationGroup(group, ["en", "fr"]);
        setUpBoxesBuiltAfterLoad(added);

        expect(langsOf(group)).toEqual(["en", "fr"]);
        expect(picturesIn("fr")).toEqual([id]);
        expect(picturesIn("en")).toEqual([id]);
    });

    it("tags the boxes added and the boxes shown again, of every group, in one pass", () => {
        const groups = [0, 1].map(() =>
            makeGroup(
                "<div class='bloom-editable bloom-visibility-code-on' lang='en'><p>a</p></div>" +
                    "<div class='bloom-editable bloom-visibility-code-off' lang='fr'><p>b</p></div>",
            ),
        );
        const results = groups.map((group) =>
            showLanguagesInTranslationGroup(group, ["fr", "es"]),
        );
        const added = results.flatMap((result) => result.added);
        const shown = results.flatMap((result) => result.shown);
        // sanity check: each group gained an es box and showed its fr box again
        expect(added.map((box) => box.getAttribute("lang"))).toEqual([
            "es",
            "es",
        ]);
        expect(shown.map((box) => box.getAttribute("lang"))).toEqual([
            "fr",
            "fr",
        ]);
        const tag = vi.mocked(addLanguageTagsToBoxes);
        tag.mockClear();

        setUpBoxesBuiltAfterLoad(added, shown);

        expect(tag).toHaveBeenCalledTimes(1);
        expect(tag.mock.calls[0][0].toArray()).toEqual([...added, ...shown]);
    });
});
