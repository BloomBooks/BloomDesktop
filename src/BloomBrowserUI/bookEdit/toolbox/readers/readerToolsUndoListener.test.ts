// After an undo or redo, the page frame's listener (listenToPageHistory in bloomEditing.ts) has the
// toolbox (toolboxBootstrap's repaintReaderHighlights) paint the reader highlights again from the
// text that the undo or redo put back. readerToolsUndo.test.ts calls the model itself; this test
// goes through the real listener and the toolbox's real exports.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../../pageHistory/editTimeMarkup";
import type { PageHistory } from "../../pageHistory/pageHistoryTypes";
import {
    getHighlightTexts,
    installHighlightPolyfill,
} from "../../test/highlightTestSupport";

const frames = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
// The page frame's history, and the toolbox's exports as the toolbox bundle sets them on its
// window, which in this test is the page frame's window too.
vi.mock("../../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<object>()),
    getEditablePageBundleExports: () =>
        frames.history ? { pageHistory: () => frames.history } : null,
    getToolboxBundleExports: () =>
        (window as unknown as { toolboxBundle: unknown }).toolboxBundle,
}));
// Loading the toolbox bundle renders the toolbox and initializes it once the document is ready;
// this test needs only its exports.
vi.mock("../ToolboxRoot", () => ({ renderToolboxRoot: () => {} }));
vi.mock("../toolbox", async (importOriginal) => {
    const original = await importOriginal<typeof import("../toolbox")>();
    return {
        ...original,
        getTheOneToolbox: () => ({ initialize: () => {} }),
    };
});

import "../toolboxBootstrap";
import { listenToPageHistory } from "../../js/bloomEditing";
import { MarkupType, ReaderToolsModel } from "./readerToolsModel";
import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { kSentenceTooLongHighlight } from "./readerHighlights";

const kTextBefore = "One two three.";
const kTextTyped = "One two three four.";

let history: PageHistory;
let model: ReaderToolsModel;
let textNode: Text;

beforeEach(() => {
    installHighlightPolyfill(window);
    // With no page frame, the model looks for boxes in this document.
    document.body.innerHTML = `<div class="bloom-page"><div class="bloom-translationGroup"><div class="bloom-editable bloom-content1" lang="en"><p>${kTextBefore}</p></div></div></div>`;
    textNode = document.querySelector(".bloom-editable p")!.firstChild as Text;
    model = new ReaderToolsModel();
    const synphony = new ReadersSynphonyWrapper();
    synphony.loadSettings({
        letters: "a b c d e f g h i j k l m n o p q r s t u v w x y z",
        stages: [{ letters: "o n e t w h r", sightWords: "" }],
        // Three words in a sentence at most.
        levels: [{ maxWordsPerSentence: 3, thingsToRemember: [""] }],
    });
    model.synphony = synphony;
    model.wordListLoaded = true;
    model.setCkEditorLoaded();
    model.currentMarkupType = MarkupType.Leveled;
    // The model that the toolbox finds (getTheOneReaderToolsModel).
    (top as unknown as Record<string, unknown>)[
        "__bloomReaderToolsModelHolder"
    ] = { model, ownerWindow: window, ownerDocument: document };

    history = createPageHistory({
        root: document.querySelector(".bloom-page")!,
        filter: createEditTimeMarkupFilter(),
    });
    frames.history = history;
    listenToPageHistory(history);
});

afterEach(() => {
    history.dispose();
    frames.history = undefined;
    delete (top as unknown as Record<string, unknown>)[
        "__bloomReaderToolsModelHolder"
    ];
    document.body.innerHTML = "";
});

describe("the reader highlights after an undo or redo", () => {
    it("are painted again through the page frame's listener and the toolbox's exports", async () => {
        await history.doTransaction("Test.Typing", () => {
            textNode.data = kTextTyped;
            history.addToLatestTransaction(() => model.doMarkup());
        });
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual([
            kTextTyped,
        ]);

        await history.undo();

        expect(textNode.data).toBe(kTextBefore);
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual(
            [],
        );

        await history.redo();

        expect(textNode.data).toBe(kTextTyped);
        expect(getHighlightTexts(window, kSentenceTooLongHighlight)).toEqual([
            kTextTyped,
        ]);
        expect(history.redoDescriptionL10nKey).toBeUndefined();
    });
});
