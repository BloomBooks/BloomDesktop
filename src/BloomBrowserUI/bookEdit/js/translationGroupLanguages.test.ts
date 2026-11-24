import { beforeEach, describe, expect, it, vi } from "vitest";
import {
    setUpBoxesBuiltAfterLoad,
    showLanguagesInTranslationGroup,
} from "./translationGroupLanguages";
import { insertInlineImage } from "./inlineImages";
import { addLanguageTagsToBoxes } from "./bloomEditing";
import OverflowChecker from "../OverflowChecker/OverflowChecker";

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
        const group = makeGroup(
            "<div class='bloom-editable bloom-visibility-code-on' lang='en'><p>a</p></div>" +
                "<div class='bloom-editable bloom-visibility-code-off' lang='fr'><p>b</p></div>",
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

    // Page load checks the boxes it shows for overflow as they are typed in; a box added later, or
    // hidden at load and shown now, must be checked too.
    it("checks the boxes added and the boxes shown again for overflow as they are typed in", () => {
        const group = makeGroup(
            "<div class='bloom-editable bloom-visibility-code-on' lang='en'><p>a</p></div>" +
                "<div class='bloom-editable bloom-visibility-code-off' lang='fr'><p>b</p></div>",
        );
        const { added, shown } = showLanguagesInTranslationGroup(group, [
            "fr",
            "es",
        ]);
        // sanity check: an es box was added and the fr box shown again
        expect(added.map((box) => box.getAttribute("lang"))).toEqual(["es"]);
        expect(shown.map((box) => box.getAttribute("lang"))).toEqual(["fr"]);
        const addTypingHandlers = vi
            .spyOn(OverflowChecker, "AddTypingHandlers")
            .mockImplementation(() => {});

        setUpBoxesBuiltAfterLoad(added, shown);

        expect(addTypingHandlers).toHaveBeenCalledTimes(1);
        expect(addTypingHandlers.mock.calls[0][0]).toEqual([
            ...added,
            ...shown,
        ]);
        addTypingHandlers.mockRestore();
    });
});
