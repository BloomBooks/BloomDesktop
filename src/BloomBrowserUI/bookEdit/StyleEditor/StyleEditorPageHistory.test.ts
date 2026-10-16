// The Format dialog's page changes go outside the page history (see cleanupAfterStyleChange in
// StyleEditor.ts): a style change is neither an undo step nor clears the history. These run
// against a real page history. OverflowChecker's refit needs real layout, so a stand-in makes the
// kind of change it makes: it sets the box's height.
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPageHistory } from "../pageHistory/pageHistory";
import { createEditTimeMarkupFilter } from "../pageHistory/editTimeMarkup";
import {
    kGenericUndoDescriptionL10nKey,
    type PageHistory,
} from "../pageHistory/pageHistoryTypes";

const access = vi.hoisted(() => ({
    history: undefined as PageHistory | undefined,
}));

vi.mock("../pageHistory/pageHistoryAccess", () => ({
    currentPageHistory: () => access.history,
    doOutsideHistoryIfAny: <T>(fn: () => T): T =>
        access.history ? access.history.doOutsideHistory(fn) : fn(),
}));

vi.mock("../OverflowChecker/OverflowChecker", () => ({
    default: {
        AdjustSizeAndMarkSelfOverflow: (editable: HTMLElement) => {
            editable.style.height = "120px";
        },
        CheckPageAncestorOverflow: () => undefined,
    },
}));

import StyleEditor from "./StyleEditor";

let page: HTMLElement;
let box: HTMLElement;
let editor: StyleEditor;

beforeEach(() => {
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-translationGroup" id="group">' +
        '<div class="bloom-editable normal-style bloom-visibility-code-on bloom-content1" id="box" lang="xyz" contenteditable="true"><p>Some text</p></div>' +
        "</div>" +
        '<img id="img" src="a.png">';
    document.body.appendChild(page);
    box = page.querySelector<HTMLElement>("#box")!;
    access.history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
        onUnmarkedChange: () => {
            throw new Error(
                "The Format dialog changed the page outside any transaction, joining the latest step.",
            );
        },
    });
    editor = new StyleEditor("");
    editor.boxBeingEdited = box;
    // The dialog's labels ask the localization manager; they are not on the page.
    vi.spyOn(editor, "updateLabelsWithStyleName").mockImplementation(
        () => undefined,
    );
    vi.spyOn(editor, "getParagraphTabDescription").mockImplementation(
        () => undefined,
    );
});

afterEach(() => {
    access.history?.dispose();
    access.history = undefined;
    page.remove();
    document
        .querySelectorAll('style[title="userModifiedStyles"]')
        .forEach((style) => style.remove());
    vi.restoreAllMocks();
});

/** Make one step and undo it, so that there is a redo that a recorded change would clear. */
async function stepThenUndo(history: PageHistory): Promise<void> {
    await history.doTransaction("A", () =>
        page.querySelector("#img")!.setAttribute("src", "b.png"),
    );
    await history.undo();
    expect(history.redoDescriptionL10nKey, "test setup").toBe("A");
}

describe("the Format dialog and the page history", () => {
    it("makes no undo step and keeps redo when a style change refits the page", async () => {
        const history = access.history!;
        await stepThenUndo(history);

        // A click on a control of the dialog, which is in the page frame, opens a transaction.
        const click = history.beginTransaction(kGenericUndoDescriptionL10nKey);
        // What changeSize does.
        editor.changeSizeInternal("40pt", true);
        editor.cleanupAfterStyleChange();
        expect(box.style.height, "test setup: the refit ran").toBe("120px");
        expect(click.end()).toBe(false);

        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe("A");
    });

    it("makes no undo step and keeps redo when choosing a style changes the boxes' class", async () => {
        const history = access.history!;
        await stepThenUndo(history);

        const click = history.beginTransaction(kGenericUndoDescriptionL10nKey);
        // What selectStyle and createStyle do to the boxes.
        (
            StyleEditor as unknown as {
                SetStyleNameForElement(
                    target: HTMLElement,
                    style: string,
                ): void;
            }
        ).SetStyleNameForElement(box, "Heading1-style");
        expect(box.classList.contains("Heading1-style"), "test setup").toBe(
            true,
        );
        expect(box.classList.contains("normal-style"), "test setup").toBe(
            false,
        );
        expect(click.end()).toBe(false);

        expect(history.undoDescriptionL10nKey).toBeUndefined();
        expect(history.redoDescriptionL10nKey).toBe("A");
    });
});
