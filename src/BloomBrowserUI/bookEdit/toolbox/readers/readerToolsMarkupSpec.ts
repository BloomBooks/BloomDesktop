// The reader tools' markup reads the boxes and paints highlights; it does not rewrite a box. So
// after typing, opening a tool, changing the level or stage, and the focus leaving a box leave the
// box's nodes as they were.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import $ from "jquery";

// The Leveled Reader's markup asks Bloom for the text of the whole book, for its statistics.
vi.mock("../../../utils/bloomApi", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../../../utils/bloomApi")>()),
    get: vi.fn(),
}));

import {
    getHighlightTexts,
    installHighlightPolyfill,
} from "../../test/highlightTestSupport";
import { MarkupType, ReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { DecodableReaderTool } from "./decodableReader/decodableReaderTool";
import {
    kSentenceTooLongHighlight,
    kWordNotDecodableHighlight,
    theOneReaderHighlightManager,
} from "./readerHighlights";

const kTextBefore = "One two three.";
const kTextTyped = "One two three four.";
// What the stand-in CKEditor's getData() gives: what a rewrite of the box would write.
const kCkEditorData = "<p>Something else.</p>";

let model: ReaderToolsModel;
let box: HTMLDivElement;
let paragraph: HTMLParagraphElement;
let getData: ReturnType<typeof vi.fn>;
// The text node that the stand-in CKEditor says holds its filling character, if any.
let fillingCharNode: Node | undefined;

beforeEach(() => {
    installHighlightPolyfill(window);
    // With no page frame, the model looks for boxes in this document.
    document.body.innerHTML = `<div class="bloom-page"><div class="bloom-translationGroup"><div class="bloom-editable bloom-content1" lang="en"><p>${kTextBefore}</p></div></div></div>`;
    box = document.querySelector(".bloom-editable")!;
    paragraph = box.querySelector("p")!;
    // A stand-in for the box's CKEditor, whose getData() differs from the box's html, so that a
    // rewrite of the box from it would show.
    getData = vi.fn(() => kCkEditorData);
    fillingCharNode = undefined;
    (box as unknown as { bloomCkEditor: object }).bloomCkEditor = {
        getData,
        getSelection: () => undefined,
        editable: () => ({
            getCustomData: (key: string) =>
                key === "cke-fillingChar" && fillingCharNode
                    ? { $: fillingCharNode }
                    : undefined,
            removeCustomData: () => undefined,
        }),
    };

    model = new ReaderToolsModel();
    const synphony = new ReadersSynphonyWrapper();
    synphony.loadSettings({
        letters: "a b c d e f g h i j k l m n o p q r s t u v w x y z",
        stages: [
            { letters: "o n e t w h r", sightWords: "" },
            { letters: "f u", sightWords: "" },
        ],
        levels: [
            { maxWordsPerSentence: 3, thingsToRemember: [""] },
            { maxWordsPerSentence: 5, thingsToRemember: [""] },
        ],
    });
    model.synphony = synphony;
    model.wordListLoaded = true;
    model.setCkEditorLoaded();
    // The model that the focus and key handlers find (getTheOneReaderToolsModel).
    (top as unknown as Record<string, unknown>)[
        "__bloomReaderToolsModelHolder"
    ] = { model, ownerWindow: window, ownerDocument: document };
});

afterEach(() => {
    vi.useRealTimers();
    delete (top as unknown as Record<string, unknown>)[
        "__bloomReaderToolsModelHolder"
    ];
    document.body.innerHTML = "";
});

/** Type in the box, then run the markup pass that follows typing, as pageEditingMarkup.ts does. */
function typeInTheBox(): Text {
    const textNode = paragraph.firstChild as Text;
    textNode.data = kTextTyped;
    model.doMarkup();
    return textNode;
}

/** Fail unless the box still holds the same nodes with the typed text, never read through getData(). */
function expectBoxNotRewritten(textNode: Text): void {
    expect(paragraph.parentNode).toBe(box);
    expect(paragraph.firstChild).toBe(textNode);
    expect(box.innerHTML).toBe(`<p>${kTextTyped}</p>`);
    expect(getData).not.toHaveBeenCalled();
}

describe("The reader tools' markup leaves the boxes alone", () => {
    it("opening a tool after typing paints highlights and does not rewrite the box", () => {
        const textNode = typeInTheBox();

        expect(model.setMarkupType(2)).toBe(true);

        expectBoxNotRewritten(textNode);
        // Level 1 allows three words in a sentence.
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual([
            kTextTyped,
        ]);
    });

    it("changing the level after typing does not rewrite the box", () => {
        model.currentMarkupType = MarkupType.Leveled;
        const textNode = typeInTheBox();
        // sanity check: at level 1 the typed sentence is too long
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual([
            kTextTyped,
        ]);

        model.setLevelNumber(2, true);
        expect(model.levelNumber).toBe(2);

        expectBoxNotRewritten(textNode);
        // Level 2 allows five words, so nothing is marked now.
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual(
            [],
        );
    });

    it("changing the stage after typing does not rewrite the box", async () => {
        model.currentMarkupType = MarkupType.Decodable;
        model.stageGraphemes = model.getKnownGraphemes(1);
        const textNode = typeInTheBox();
        // sanity check: "four" needs letters of stage 2
        expect(getHighlightTexts(window, kWordNotDecodableHighlight)).toEqual([
            "four",
        ]);

        // setStageNumber marks up half a second after the click.
        vi.useFakeTimers({ toFake: ["setTimeout"] });
        const stageSet = model.setStageNumber(2, true);
        await vi.advanceTimersByTimeAsync(500);
        await stageSet;
        vi.useRealTimers();
        expect(model.stageNumber).toBe(2);

        expectBoxNotRewritten(textNode);
        expect(getHighlightTexts(window, kWordNotDecodableHighlight)).toEqual(
            [],
        );
    });

    it("the focus leaving the box after typing paints highlights and does not rewrite the box", () => {
        model.currentMarkupType = MarkupType.Leveled;
        new DecodableReaderTool().setupReaderKeyAndFocusHandlers(document.body);
        const textNode = typeInTheBox();
        // Start from no highlights, so that what the focusout paints shows.
        theOneReaderHighlightManager.clearAll(document.body);
        // sanity check
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual(
            [],
        );

        // jQuery makes its focusout from a blur, which jsdom does not raise here, so trigger it.
        $(box).trigger("focusout");

        expectBoxNotRewritten(textNode);
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual([
            kTextTyped,
        ]);
    });

    it("still removes markup that an earlier Bloom saved", () => {
        paragraph.innerHTML =
            '<span data-segment="sentence"><span data-segment="word">One</span> two three.</span>';

        expect(model.setMarkupType(2)).toBe(true);

        expect(paragraph.innerHTML).toBe("One two three.");
    });
});
