// Tests of when the toolbox's markup pass (pageEditingMarkup.ts) records the caret: only when
// something in it may rewrite the box. Recording the caret inserts a CKEditor bookmark that splits
// the text node the user is typing in (BL-16717). The pass needs CKEditor to save and restore the
// selection, so markupSelectionPreservation is replaced by a stand-in that records none.
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ITool, ToolBox } from "./toolbox";

const selection = vi.hoisted(() => ({
    save: vi.fn((_box: HTMLElement, _boxMightBeRewritten: boolean) => ({
        bookmarks: undefined,
    })),
}));

vi.mock("./markupSelectionPreservation", () => ({
    boxParticipatesInMarkup: () => true,
    saveSelectionForMarkup: selection.save,
    restoreSelectionAfterMarkup: () => undefined,
    restoreAndResaveSelectionForMarkup: (_box: HTMLElement, saved: object) =>
        saved,
}));

import { configurePageEditingHandlers } from "./pageEditingMarkup";

// Each test advances the timers until its markup pass has run, so none is left pending.
afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    selection.save.mockClear();
    document.body.innerHTML = "";
});

/**
 * A page with one box holding paragraphText, and the markup handlers on it, with runningTool
 * running. The pass looks for the page in the iframe whose id is "page"; the page itself stays in
 * this document, and the iframe's window reports this document's selection.
 */
function setUpPage(
    paragraphText: string,
    runningTool: Partial<ITool> | undefined,
): { box: HTMLElement; paragraph: HTMLElement } {
    document.body.innerHTML =
        '<iframe id="page"></iframe>' +
        '<div class="bloom-page" id="pageDiv"><div class="bloom-editable" id="box"><p id="p1"></p></div></div>';
    const pageFrame = document.getElementById("page") as HTMLIFrameElement;
    vi.spyOn(pageFrame.contentWindow!, "getSelection").mockImplementation(() =>
        window.getSelection(),
    );
    const pageDiv = document.getElementById("pageDiv")!;
    const box = document.getElementById("box")!;
    const paragraph = document.getElementById("p1")!;
    paragraph.textContent = paragraphText;
    configurePageEditingHandlers(pageDiv, {
        getRunningTool: () => runningTool,
    } as unknown as ToolBox);
    return { box, paragraph };
}

/** A key going down and up in box with the caret in paragraph, which schedules a markup pass. */
function typeKey(box: HTMLElement, paragraph: HTMLElement): void {
    window.getSelection()!.collapse(paragraph.firstChild, 3);
    box.dispatchEvent(
        new KeyboardEvent("keydown", { code: "KeyB", bubbles: true }),
    );
    box.dispatchEvent(
        new KeyboardEvent("keyup", { code: "KeyB", bubbles: true }),
    );
}

describe("pageEditingMarkup: when the markup pass records the caret", () => {
    it("records none for a tool whose synchronous markup only reads the box", () => {
        const updateMarkup = vi.fn();
        const { box, paragraph } = setUpPage("Hello world", {
            isUpdateMarkupAsync: () => false,
            updateMarkup,
        });
        vi.useFakeTimers();
        typeKey(box, paragraph);

        vi.advanceTimersByTime(500);

        expect(
            updateMarkup,
            "test setup: the pass should have run the tool's markup",
        ).toHaveBeenCalledTimes(1);
        expect(selection.save).toHaveBeenCalledTimes(1);
        expect(
            selection.save.mock.calls[0][1],
            "boxMightBeRewritten should be false",
        ).toBe(false);
    });

    it("records it for a tool whose markup is async, which may rewrite the box", () => {
        const { box, paragraph } = setUpPage("Hello world", {
            isUpdateMarkupAsync: () => true,
            updateMarkupAsync: async () => () => undefined,
        });
        vi.useFakeTimers();
        typeKey(box, paragraph);

        vi.advanceTimersByTime(500);

        expect(selection.save).toHaveBeenCalledTimes(1);
        expect(selection.save.mock.calls[0][1]).toBe(true);
    });

    it("records it with no tool when there is an nbsp to clean up", () => {
        const { box, paragraph } = setUpPage("Hello a b", undefined);
        vi.useFakeTimers();
        typeKey(box, paragraph);

        vi.advanceTimersByTime(500);

        expect(selection.save).toHaveBeenCalledTimes(1);
        expect(selection.save.mock.calls[0][1]).toBe(true);
    });
});
