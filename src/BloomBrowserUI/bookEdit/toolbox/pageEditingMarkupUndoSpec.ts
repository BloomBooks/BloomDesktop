// Tests of the toolbox's markup pass (pageEditingMarkup.ts) run as a whole: that it joins the undo
// step of the typing that scheduled it, even when the user's next press holds a transaction open
// by the time it runs (docs/undo/APPROACHES.md section 5.3), and that it records the caret only
// when something in it may rewrite the box. The pass needs CKEditor to save and restore the
// selection, so markupSelectionPreservation is replaced by a stand-in that records none.
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ITool, ToolBox } from "./toolbox";
import type { PageHistory } from "../pageHistory/pageHistoryTypes";

const frames = vi.hoisted(() => ({
    pageFrameExports: undefined as
        | { pageHistory: () => PageHistory }
        | undefined,
}));

const selection = vi.hoisted(() => ({
    save: vi.fn((_box: HTMLElement, _boxMightBeRewritten: boolean) => ({
        bookmarks: undefined,
    })),
}));

vi.mock("../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../js/workspaceFrames")>()),
    getEditablePageBundleExports: () => frames.pageFrameExports ?? null,
}));

vi.mock("./markupSelectionPreservation", () => ({
    boxParticipatesInMarkup: () => true,
    saveSelectionForMarkup: selection.save,
    restoreSelectionAfterMarkup: () => undefined,
    restoreAndResaveSelectionForMarkup: (_box: HTMLElement, saved: object) =>
        saved,
}));

import {
    cancelPendingMarkupUpdates,
    configurePageEditingHandlers,
} from "./pageEditingMarkup";
import { createPageHistory } from "../pageHistory/pageHistory";
import { kGenericUndoDescriptionL10nKey } from "../pageHistory/pageHistoryTypes";

let history: PageHistory | undefined;

afterEach(() => {
    cancelPendingMarkupUpdates();
    vi.useRealTimers();
    vi.restoreAllMocks();
    selection.save.mockClear();
    history?.dispose();
    history = undefined;
    frames.pageFrameExports = undefined;
    document.body.innerHTML = "";
});

/**
 * A page with one box holding paragraphText, and the markup handlers on it, with runningTool
 * running. The pass looks for the page in the iframe whose id is "page";
 * the page itself stays in this document, and the iframe's window reports this document's
 * selection.
 */
function setUpPage(
    paragraphText: string,
    runningTool: Partial<ITool> | undefined,
): { pageDiv: HTMLElement; box: HTMLElement; paragraph: HTMLElement } {
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
    return { pageDiv, box, paragraph };
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

describe("pageEditingMarkup: the markup pass and the page history", () => {
    it("joins the typing's step when the user's next press holds a transaction open, which then makes no step", async () => {
        const { pageDiv, box, paragraph } = setUpPage("Hello", undefined);
        const unmarked = vi.fn();
        const pageHistory = createPageHistory({
            root: pageDiv,
            onUnmarkedChange: unmarked,
        });
        history = pageHistory;
        frames.pageFrameExports = { pageHistory: () => pageHistory };

        // The typing: its step leaves an nbsp between two letters, which the pass turns into a
        // space by rewriting the box.
        await pageHistory.doTransaction("Test.Typing", () => {
            (paragraph.firstChild as Text).data = "Hello a\u00A0b";
        });
        vi.useFakeTimers();
        typeKey(box, paragraph);
        // The user's next press, in the toolbox or the page, opens the stand-in.
        const press = pageHistory.beginTransaction(
            kGenericUndoDescriptionL10nKey,
        );

        vi.advanceTimersByTime(500);

        expect(
            box.textContent,
            "test setup: the pass should have replaced the nbsp",
        ).toBe("Hello a b");
        expect(press.end(), "the press changed nothing").toBe(false);
        expect(unmarked).not.toHaveBeenCalled();
        expect(pageHistory.undoDescriptionL10nKey).toBe("Test.Typing");
        await pageHistory.undo();
        expect(
            box.textContent,
            "one undo should take back the typing and the pass",
        ).toBe("Hello");
        expect(box.firstChild, "the same paragraph is back").toBe(paragraph);
        expect(pageHistory.undoDescriptionL10nKey).toBeUndefined();
    });

    it("keeps redo when the Ctrl of a Ctrl+Y goes down in a box whose paragraph undo left split", async () => {
        const { pageDiv, box, paragraph } = setUpPage("waf", undefined);
        paragraph.append("fle");
        expect(
            paragraph.childNodes.length,
            "test setup: the paragraph's text should be in two text nodes",
        ).toBe(2);
        const pageHistory = createPageHistory({ root: pageDiv });
        history = pageHistory;
        frames.pageFrameExports = { pageHistory: () => pageHistory };
        await pageHistory.doTransaction("Test.Edit", () =>
            box.setAttribute("lang", "fr"),
        );
        await pageHistory.undo();
        expect(pageHistory.redoDescriptionL10nKey).toBe("Test.Edit");
        vi.useFakeTimers();

        box.dispatchEvent(
            new KeyboardEvent("keydown", {
                key: "Control",
                code: "ControlLeft",
                ctrlKey: true,
                bubbles: true,
            }),
        );
        box.dispatchEvent(
            new KeyboardEvent("keyup", {
                key: "Control",
                code: "ControlLeft",
                bubbles: true,
            }),
        );

        expect(
            paragraph.childNodes.length,
            "a modifier on its own merges nothing",
        ).toBe(2);
        expect(
            vi.getTimerCount(),
            "the modifier's keyup schedules no markup pass",
        ).toBe(0);
        expect(pageHistory.redoDescriptionL10nKey).toBe("Test.Edit");
        await pageHistory.redo();
        expect(box.getAttribute("lang")).toBe("fr");

        box.dispatchEvent(
            new KeyboardEvent("keydown", {
                key: "ArrowLeft",
                code: "ArrowLeft",
                bubbles: true,
            }),
        );
        expect(
            paragraph.childNodes.length,
            "any other key, an arrow key included, still repairs the split",
        ).toBe(1);
        expect(paragraph.textContent).toBe("waffle");
    });
});

describe("pageEditingMarkup: when the markup pass records the caret", () => {
    // Recording the caret inserts a CKEditor bookmark that splits the text node the user is
    // typing in (BL-16717), so the pass records it only when something may rewrite the box.
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
        const { box, paragraph } = setUpPage("Hello a\u00A0b", undefined);
        vi.useFakeTimers();
        typeKey(box, paragraph);

        vi.advanceTimersByTime(500);

        expect(selection.save).toHaveBeenCalledTimes(1);
        expect(selection.save.mock.calls[0][1]).toBe(true);
    });
});
