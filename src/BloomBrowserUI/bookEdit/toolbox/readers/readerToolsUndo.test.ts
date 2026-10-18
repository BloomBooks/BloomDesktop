// The reader tools' markup and the page history. doMarkup reads the boxes and paints highlights;
// it does not rewrite a box. So after typing, opening a tool, changing the level or stage, and the
// focus leaving a box make no undo step, join none and end nothing: undo of the typing still works,
// redo still works, and the highlights follow the text that undo and redo put back.
import {
    afterEach,
    beforeEach,
    describe,
    expect,
    it,
    vi,
    type Mock,
} from "vitest";
import $ from "jquery";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../../pageHistory/pageHistoryTypes";
import {
    getHighlightTexts,
    installHighlightPolyfill,
} from "../../test/highlightTestSupport";

const mocks = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
// The history that the code under test finds, with doOutsideHistory doing what the real
// one does when there is a history.
vi.mock("../../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => mocks.history,
    doOutsideHistory: <T>(fn: () => T): T =>
        mocks.history ? mocks.history.doOutsideHistory(fn) : fn(),
    clearUndoAndRedo: (reason: string) =>
        mocks.history?.clearUndoAndRedo(reason),
}));

import { MarkupType, ReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { DecodableReaderTool } from "./decodableReader/decodableReaderTool";
import {
    kSentenceTooLongHighlight,
    kWordNotDecodableHighlight,
    theOneReaderHighlightManager,
} from "./readerHighlights";

const kEarlierKey = "EditTab.Toolbox.ComicTool.Options.ElementType";
const kTypingKey = "EditTab.Undo.Typing";
const kTextBefore = "One two three.";
const kTextTyped = "One two three four.";
// What the stand-in CKEditor's getData() gives: what a rewrite of the box would write.
const kCkEditorData = "<p>Something else.</p>";

let history: PageHistory;
let onUnmarkedChange: Mock<(whatChanged: string) => void>;
let model: ReaderToolsModel;
let box: HTMLDivElement;
let paragraph: HTMLParagraphElement;
let getData: Mock<() => string>;

beforeEach(async () => {
    installHighlightPolyfill(window);
    // With no page frame, the model looks for boxes in this document.
    document.body.innerHTML = `<div class="bloom-page"><div class="bloom-translationGroup"><div class="bloom-editable bloom-content1" lang="en"><p>${kTextBefore}</p></div></div><div id="other">before</div></div>`;
    box = document.querySelector(".bloom-editable")!;
    paragraph = box.querySelector("p")!;
    // A stand-in for the box's CKEditor, whose getData() differs from the box's html, so that a
    // rewrite of the box from it would show. It tracks no filling character.
    getData = vi.fn(() => kCkEditorData);
    (box as unknown as { bloomCkEditor: object }).bloomCkEditor = {
        getData,
        getSelection: () => undefined,
        editable: () => ({
            getCustomData: () => undefined,
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
    // The model that the focusout handler finds (getTheOneReaderToolsModel).
    (top as unknown as Record<string, unknown>)[
        "__bloomReaderToolsModelHolder"
    ] = { model, ownerWindow: window, ownerDocument: document };

    onUnmarkedChange = vi.fn();
    history = createPageHistory({
        root: document.querySelector(".bloom-page")!,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange,
    });
    mocks.history = history;
    await history.doTransaction(kEarlierKey, () => {
        document.getElementById("other")!.textContent = "typed";
    });
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
});

afterEach(() => {
    vi.useRealTimers();
    history.dispose();
    mocks.history = undefined;
    delete (top as unknown as Record<string, unknown>)[
        "__bloomReaderToolsModelHolder"
    ];
    document.body.innerHTML = "";
});

/**
 * Type in the box as one undo step, with the markup pass that follows typing joined to it, as
 * pageEditingMarkup.ts does it: the text changes, then the tool's updateMarkup runs.
 */
async function typeInTheBox(): Promise<Text> {
    const textNode = paragraph.firstChild as Text;
    await history.doTransaction(kTypingKey, () => {
        textNode.data = kTextTyped;
        history.addToLatestTransaction(() => model.doMarkup());
    });
    expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
    return textNode;
}

/**
 * Fail unless the box still holds the same nodes with the typed text, nothing was recorded outside
 * a transaction, and the box was never read through CKEditor's getData().
 */
function expectBoxNotRewritten(textNode: Text): void {
    expect(paragraph.parentNode).toBe(box);
    expect(paragraph.firstChild).toBe(textNode);
    expect(box.innerHTML).toBe(`<p>${kTextTyped}</p>`);
    expect(getData).not.toHaveBeenCalled();
    expect(onUnmarkedChange).not.toHaveBeenCalled();
}

/** Undo the typing and redo it, checking the text, the steps and the highlights after each. */
async function expectTypingUndoesAndRedoes(
    highlightAfterTyping: string,
    highlightName: string,
): Promise<void> {
    expect(getHighlightTexts(window, highlightName)).toEqual([
        highlightAfterTyping,
    ]);

    await history.undo();
    // What onAfterUndoOrRedo makes the toolbox do (toolboxBootstrap's repaintReaderHighlights).
    model.repaintHighlights();
    expect(box.innerHTML).toBe(`<p>${kTextBefore}</p>`);
    expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
    expect(history.redoDescriptionL10nKey).toBe(kTypingKey);

    await history.redo();
    model.repaintHighlights();
    expect(box.innerHTML).toBe(`<p>${kTextTyped}</p>`);
    expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
    expect(history.redoDescriptionL10nKey).toBeUndefined();
    expect(getHighlightTexts(window, highlightName)).toEqual([
        highlightAfterTyping,
    ]);
    expect(onUnmarkedChange).not.toHaveBeenCalled();
}

describe("The reader tools' markup and the page history", () => {
    it("opening a tool after typing writes nothing, and the typing still undoes and redoes", async () => {
        const textNode = await typeInTheBox();

        // As when the tool opens from a click in the toolbox.
        const click = history.beginTransaction(kGenericUndoDescriptionL10nKey);
        expect(model.setMarkupType(2)).toBe(true);
        expect(click.end()).toBe(false);

        expectBoxNotRewritten(textNode);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
        // Level 1 allows three words in a sentence.
        await expectTypingUndoesAndRedoes(
            kTextTyped,
            kSentenceTooLongHighlight,
        );
        // Undone, the sentence has three words and is not marked.
        await history.undo();
        model.repaintHighlights();
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual(
            [],
        );
    });

    it("changing the level after typing writes nothing, and the typing still undoes and redoes", async () => {
        model.currentMarkupType = MarkupType.Leveled;
        const textNode = await typeInTheBox();
        // sanity check: at level 1 the typed sentence is too long
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual([
            kTextTyped,
        ]);

        const click = history.beginTransaction(kGenericUndoDescriptionL10nKey);
        model.setLevelNumber(2, true);
        expect(click.end()).toBe(false);
        expect(model.levelNumber).toBe(2);

        expectBoxNotRewritten(textNode);
        // Level 2 allows five words, so nothing is marked now.
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual(
            [],
        );
        await history.undo();
        model.repaintHighlights();
        expect(box.innerHTML).toBe(`<p>${kTextBefore}</p>`);
        await history.redo();
        model.repaintHighlights();
        expect(box.innerHTML).toBe(`<p>${kTextTyped}</p>`);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("changing the stage after typing writes nothing, and the typing still undoes and redoes", async () => {
        model.currentMarkupType = MarkupType.Decodable;
        model.stageGraphemes = model.getKnownGraphemes(1);
        const textNode = await typeInTheBox();
        // sanity check: "four" needs letters of stage 2
        expect(getHighlightTexts(window, kWordNotDecodableHighlight)).toEqual([
            "four",
        ]);

        // setStageNumber marks up half a second after the click, outside any transaction.
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
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
        await history.undo();
        model.repaintHighlights();
        expect(box.innerHTML).toBe(`<p>${kTextBefore}</p>`);
        await history.redo();
        model.repaintHighlights();
        expect(box.innerHTML).toBe(`<p>${kTextTyped}</p>`);
        expect(onUnmarkedChange).not.toHaveBeenCalled();
    });

    it("the focus leaving the box after typing writes nothing, and the typing still undoes and redoes", async () => {
        model.currentMarkupType = MarkupType.Leveled;
        new DecodableReaderTool().setupReaderKeyAndFocusHandlers(document.body);
        const textNode = await typeInTheBox();
        // Start from no highlights, so that what the focusout paints shows.
        theOneReaderHighlightManager.clearAll(document.body);
        // sanity check
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual(
            [],
        );

        // As when the user clicks in the toolbox: outside any transaction of the page's. jQuery
        // makes its focusout from a blur, which jsdom does not raise here, so trigger it.
        $(box).trigger("focusout");

        expectBoxNotRewritten(textNode);
        expect(history.undoDescriptionL10nKey).toBe(kTypingKey);
        await expectTypingUndoesAndRedoes(
            kTextTyped,
            kSentenceTooLongHighlight,
        );
    });

    it("removes markup that an earlier Bloom saved without recording it", async () => {
        paragraph.innerHTML =
            '<span data-segment="sentence"><span data-segment="word">One</span> two three.</span>';
        // That change is not the user's either.
        history.clearUndoAndRedo("a test");
        onUnmarkedChange.mockClear();
        await history.doTransaction(kEarlierKey, () => {
            document.getElementById("other")!.textContent = "again";
        });

        expect(model.setMarkupType(2)).toBe(true);

        expect(paragraph.innerHTML).toBe("One two three.");
        expect(onUnmarkedChange).not.toHaveBeenCalled();
        expect(history.undoDescriptionL10nKey).toBe(kEarlierKey);
        await history.undo();
        expect(document.getElementById("other")!.textContent).toBe("typed");
        expect(paragraph.innerHTML).toBe("One two three.");
    });
});
