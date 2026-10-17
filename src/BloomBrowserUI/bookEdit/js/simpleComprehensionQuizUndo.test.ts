// Tests that marking a quiz choice as a correct answer, by a click on its checkbox, is an undo
// step that says so.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// Code in the page frame finds the page history through the page frame's bundle exports; give it
// this test's.
const pageFrame = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));
vi.mock("./workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("./workspaceFrames")>()),
    getEditablePageBundleExports: () =>
        pageFrame.history ? { pageHistory: () => pageFrame.history } : null,
}));

// These imports deliberately come after the vi.mock call above, so that the module graph they
// pull in gets the stubbed module.
import { initChoiceWidgetsForEditing } from "./simpleComprehensionQuiz";
import { createPageHistory } from "../pageHistory/pageHistory";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../pageHistory/pageHistoryTypes";

let history: PageHistory;

beforeEach(() => {
    document.body.innerHTML = `
        <div class="bloom-page simple-comprehension-quiz" id="page">
            <div class="checkbox-and-textbox-choice" id="choice"><input type="checkbox" id="checkbox"></div>
        </div>`;
    // initChoiceWidgetsForEditing does its work only when the page has the edit mode stylesheet,
    // which jsdom does not load.
    vi.spyOn(document, "styleSheets", "get").mockReturnValue([
        { href: "file:///editMode.css" },
    ] as unknown as StyleSheetList);
    initChoiceWidgetsForEditing();
    history = createPageHistory({ root: document.getElementById("page")! });
    pageFrame.history = history;
});

afterEach(() => {
    pageFrame.history = undefined;
    history.dispose();
    vi.restoreAllMocks();
    document.body.innerHTML = "";
});

describe("a click on a quiz choice's checkbox", () => {
    it("is an undo step that marks the choice as a correct answer", async () => {
        const choice = document.getElementById("choice")!;
        expect(choice.classList.contains("correct-answer")).toBe(false);
        // The transaction with the general description that the click opens (pageHistoryInput.ts).
        const standIn = history.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );
        document.getElementById("checkbox")!.click();
        // A MutationObserver delivers its records in a microtask.
        await Promise.resolve();
        standIn.end();

        expect(choice.classList.contains("correct-answer")).toBe(true);
        expect(history.undoDescriptionL10nKey).toBe(
            "EditTab.Undo.Quiz.MarkCorrectAnswer",
        );
    });
});
