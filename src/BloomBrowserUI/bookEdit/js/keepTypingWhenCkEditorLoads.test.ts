import { describe, expect, it } from "vitest";
import {
    ILoadingCkEditor,
    keepTypingWhenCkEditorLoads,
} from "./keepTypingWhenCkEditorLoads";

/**
 * Remove the whitespace-only text nodes CKEditor's parser leaves out: those next to the start or
 * end of a block (here, a box or a paragraph) and those just before a line break, but not a space
 * between two inline elements.
 */
function dropBoundaryWhitespace(root: Node) {
    const isBoundary = (n: Node | null) =>
        !n || n.nodeName === "P" || n.nodeName === "DIV";
    for (const child of Array.from(root.childNodes)) {
        if (
            child.nodeType === Node.TEXT_NODE &&
            !/\S/.test(child.textContent!) &&
            (isBoundary(child.previousSibling) ||
                isBoundary(child.nextSibling) ||
                child.nextSibling?.nodeName === "BR")
        )
            child.remove();
        else dropBoundaryWhitespace(child);
    }
}

/**
 * Just enough of CKEditor's inline creation (CKEDITOR.inline in lib/ckeditor/ckeditor.js) to show
 * the problem: the editor copies the box's content when it is made, and when it has loaded it
 * writes its copy back into the box, from a "loaded" listener at priority 10000, and then fires
 * instanceReady. Listeners run in priority order, lowest first; the default is 10.
 */
class FakeInlineEditor implements ILoadingCkEditor {
    private data: string;
    private listeners: {
        eventName: string;
        listener: () => void;
        priority: number;
    }[] = [];

    /**
     * `dropWhitespaceNodes` imitates CKEditor's parser, which leaves out some text nodes of nothing
     * but whitespace, such as the line breaks between paragraphs in a book's HTML.
     */
    constructor(
        private element: HTMLElement,
        dropWhitespaceNodes = false,
    ) {
        this.data = element.innerHTML;
        this.on(
            "loaded",
            () => {
                // The real one replaces the box's nodes (editable.setHtml), which loses the caret.
                this.element.innerHTML = this.data;
                if (dropWhitespaceNodes) dropBoundaryWhitespace(this.element);
                window.getSelection()!.removeAllRanges();
                this.fire("instanceReady");
            },
            null,
            null,
            10000,
        );
    }

    public on(
        eventName: string,
        listener: () => void,
        _scope?: unknown,
        _listenerData?: unknown,
        priority = 10,
    ): void {
        this.listeners.push({ eventName, listener, priority });
    }

    public setData(data: string, options: null, internal: boolean): void {
        expect(internal, "only the internal form of setData is expected").toBe(
            true,
        );
        this.data = data;
    }

    public fire(eventName: string): void {
        this.listeners
            .filter((l) => l.eventName === eventName)
            .sort((a, b) => a.priority - b.priority)
            .forEach((l) => l.listener());
    }
}

function makeBox(html: string): HTMLElement {
    const box = document.createElement("div");
    box.setAttribute("contenteditable", "true");
    box.innerHTML = html;
    document.body.appendChild(box);
    return box;
}

function putCaret(node: Node, offset: number) {
    const range = document.createRange();
    range.setStart(node, offset);
    range.collapse(true);
    window.getSelection()!.removeAllRanges();
    window.getSelection()!.addRange(range);
}

/** Whether anything in the document still carries the caret marker. */
function markerLeft(): boolean {
    return !!document.querySelector("[data-bloom-caret-marker]");
}

describe("keepTypingWhenCkEditorLoads", () => {
    it("without it, the fake editor loses what was typed while it loaded (sanity check of the fake)", () => {
        const box = makeBox("<p>a</p>");
        const editor = new FakeInlineEditor(box);
        box.innerHTML = "<p>a typed</p>";
        editor.fire("loaded");
        expect(box.innerHTML).toBe("<p>a</p>");
    });

    it("keeps text typed while the editor loads", () => {
        const box = makeBox("<p>a</p>");
        const editor = new FakeInlineEditor(box);
        keepTypingWhenCkEditorLoads(editor, box);
        box.innerHTML = "<p>a typed</p><p>more</p>";
        expect(box.innerHTML).toBe("<p>a typed</p><p>more</p>");
        editor.fire("loaded");
        expect(box.innerHTML).toBe("<p>a typed</p><p>more</p>");
    });

    it("puts the caret back where it was, and leaves the markup as it was", () => {
        const box = makeBox("<p>a</p>");
        const editor = new FakeInlineEditor(box);
        keepTypingWhenCkEditorLoads(editor, box);
        box.innerHTML = "<p>first</p><p>second</p>";
        const secondText = box.childNodes[1].firstChild!;
        putCaret(secondText, 3);
        editor.fire("loaded");
        expect(box.innerHTML, "no marker left, text joined again").toBe(
            "<p>first</p><p>second</p>",
        );
        expect(box.childNodes[1].childNodes.length).toBe(1);
        const selection = window.getSelection()!;
        expect(selection.rangeCount).toBe(1);
        expect(
            selection.focusNode,
            "the caret should be in the new copy of the second paragraph's text",
        ).toBe(box.childNodes[1].firstChild);
        expect(selection.focusNode).not.toBe(secondText);
        expect(selection.focusOffset).toBe(3);
    });

    it("puts the caret back in the right paragraph when the rewrite drops whitespace between paragraphs", () => {
        const box = makeBox("\n<p>first</p>\n<p>second</p>\n<p>third</p>\n");
        const editor = new FakeInlineEditor(box, true);
        keepTypingWhenCkEditorLoads(editor, box);
        // Sanity check: the whitespace nodes are there to begin with.
        expect(box.childNodes.length).toBe(7);
        putCaret(box.childNodes[3].firstChild!, 2); // in "second"
        editor.fire("loaded");
        expect(box.childNodes.length, "the rewrite dropped them").toBe(3);
        const selection = window.getSelection()!;
        expect(selection.focusNode).toBe(box.childNodes[1].firstChild);
        expect(selection.focusNode!.textContent).toBe("second");
        expect(selection.focusOffset).toBe(2);
        expect(markerLeft()).toBe(false);
    });

    it("keeps a caret in a space between two sentence spans, which the rewrite keeps", () => {
        const box = makeBox(
            '\n<p><span class="audio-sentence">one</span> <span class="audio-sentence">two</span></p>\n',
        );
        const editor = new FakeInlineEditor(box, true);
        keepTypingWhenCkEditorLoads(editor, box);
        const space = box.childNodes[1].childNodes[1];
        expect(space.textContent, "test setup: the space").toBe(" ");
        putCaret(space, 0);
        editor.fire("loaded");
        const paragraph = box.childNodes[0];
        expect(paragraph.nodeName, "the rewrite dropped the line breaks").toBe(
            "P",
        );
        const selection = window.getSelection()!;
        expect(selection.focusNode).toBe(paragraph.childNodes[1]);
        expect(selection.focusNode!.textContent).toBe(" ");
        expect(selection.focusOffset).toBe(0);
        expect(markerLeft()).toBe(false);
    });

    it("keeps a caret after a line break when the rewrite drops the space before it", () => {
        const box = makeBox("<p><span>one</span> <br><span>two</span></p>");
        const editor = new FakeInlineEditor(box, true);
        keepTypingWhenCkEditorLoads(editor, box);
        const two = box.querySelectorAll("span")[1].firstChild!;
        putCaret(two, 1);
        editor.fire("loaded");
        expect(
            box.innerHTML,
            "the rewrite dropped the space before the line break",
        ).toBe("<p><span>one</span><br><span>two</span></p>");
        const selection = window.getSelection()!;
        expect(selection.focusNode).toBe(
            box.querySelectorAll("span")[1].firstChild,
        );
        expect(selection.focusNode!.textContent).toBe("two");
        expect(selection.focusOffset).toBe(1);
    });

    it("keeps a caret in an empty paragraph", () => {
        const box = makeBox("<p>a</p>");
        const editor = new FakeInlineEditor(box);
        keepTypingWhenCkEditorLoads(editor, box);
        box.innerHTML = "<p>a</p><p><br></p>";
        putCaret(box.childNodes[1], 0);
        editor.fire("loaded");
        expect(box.innerHTML).toBe("<p>a</p><p><br></p>");
        const selection = window.getSelection()!;
        expect(selection.focusNode).toBe(box.childNodes[1]);
        expect(selection.focusOffset).toBe(0);
    });

    it("puts a caret that sat between paragraphs at the end of the box, without a marker", () => {
        const box = makeBox("\n<p>first</p>\n<p>second</p>\n");
        const editor = new FakeInlineEditor(box, true);
        keepTypingWhenCkEditorLoads(editor, box);
        putCaret(box.childNodes[2], 1);
        editor.fire("loaded");
        expect(box.innerHTML, "no stray paragraph or marker").toBe(
            "<p>first</p><p>second</p>",
        );
        const selection = window.getSelection()!;
        expect(selection.focusNode).toBe(box);
        expect(selection.focusOffset).toBe(box.childNodes.length);
    });

    it("puts the caret at the end of the box if the editor's markup has no marker", () => {
        const box = makeBox("<p>a</p>");
        const editor = new FakeInlineEditor(box);
        keepTypingWhenCkEditorLoads(editor, box);
        box.innerHTML = "<p>first</p><p>second</p>";
        putCaret(box.childNodes[1].firstChild!, 3);
        // An editor whose copy did not take the refresh, so it writes back "<p>a</p>".
        editor.setData = () => {};
        editor.fire("loaded");
        const selection = window.getSelection()!;
        expect(box.innerHTML).toBe("<p>a</p>");
        expect(selection.focusNode).toBe(box);
        expect(selection.focusOffset).toBe(box.childNodes.length);
    });

    it("leaves the selection alone when the caret was not in the box", () => {
        const box = makeBox("<p>a</p>");
        const elsewhere = makeBox("<p>elsewhere</p>");
        const editor = new FakeInlineEditor(box);
        keepTypingWhenCkEditorLoads(editor, box);
        putCaret(elsewhere.firstChild!.firstChild!, 2);
        editor.fire("loaded");
        // The fake clears the selection; nothing should have put one back in the box.
        expect(window.getSelection()!.rangeCount).toBe(0);
        expect(markerLeft()).toBe(false);
        expect(elsewhere.innerHTML).toBe("<p>elsewhere</p>");
    });
});
