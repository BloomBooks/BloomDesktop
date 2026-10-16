// Restoring the selection after an undo or redo, in a real browser with Bloom's CKEditor 4.5.1
// (lib/ckeditor) on the box. Putting the selection in a box focuses the box, and CKEditor's focus
// listener runs inside the call that set the selection. When the focus leaves an inline editor's
// box, CKEditor saves its selection (editor.lockSelection); when the focus comes back, it selects
// that again (editor.unlockSelection(true)), which throws an IndexSizeError if the text has
// become shorter than the saved offset. These run in headless Edge (the "browser" vitest project).
import { afterEach, beforeAll, describe, expect, it } from "vitest";
import { createPageHistory } from "./pageHistory";
import type { PageHistory } from "./pageHistoryTypes";

const kTypingKey = "EditTab.Undo.Typing";

let page: HTMLElement | undefined;
let outside: HTMLInputElement | undefined;
let editor: CKEDITOR.editor | undefined;
let history: PageHistory | undefined;
let errors: unknown[];
function onError(event: ErrorEvent): void {
    errors.push(event.error ?? event.message);
    // Reported here, so that the test fails with its own message.
    event.preventDefault();
}

/** Load lib/ckeditor/ckeditor.js, as the page frame does, from the test's server. */
function loadCkEditor(): Promise<void> {
    (window as unknown as { CKEDITOR_BASEPATH: string }).CKEDITOR_BASEPATH =
        "/lib/ckeditor/";
    return new Promise<void>((resolve, reject) => {
        const script = document.createElement("script");
        script.src = "/lib/ckeditor/ckeditor.js";
        script.onload = () => resolve();
        script.onerror = () =>
            reject(new Error("test setup: ckeditor.js did not load"));
        document.head.appendChild(script);
    });
}

beforeAll(loadCkEditor);

afterEach(() => {
    window.removeEventListener("error", onError);
    history?.dispose();
    editor?.destroy();
    page?.remove();
    outside?.remove();
    history = undefined;
    editor = undefined;
    page = undefined;
    outside = undefined;
});

describe("restoring the selection after an undo", () => {
    it("drops the selection that CKEditor locked when the focus left the box, which the undo made too long for the text, and selects what the step saved", async () => {
        errors = [];
        window.addEventListener("error", onError);
        page = document.createElement("div");
        page.className = "bloom-page";
        page.innerHTML =
            '<div class="bloom-editable" id="box1" contenteditable="true" lang="en"><p>Hello</p></div>';
        document.body.appendChild(page);
        // Something outside the page that takes the focus, as the toolbox does.
        outside = document.createElement("input");
        document.body.appendChild(outside);
        const box = page.querySelector<HTMLElement>("#box1")!;
        const ckeditor = CKEDITOR.inline(box);
        editor = ckeditor;
        await new Promise<void>((resolve) =>
            ckeditor.on("instanceReady", () => resolve()),
        );
        // CKEditor sets the box's content again as it attaches, so this is the text node in the
        // box from now on.
        const text = box.querySelector("p")!.firstChild as Text;
        history = createPageHistory({ root: page });

        // The caret at the end of "Hello", then typing, after which CKEditor notes the caret, as
        // its keyup listener does.
        box.focus();
        window.getSelection()!.collapse(text, 5);
        await history.doTransaction(kTypingKey, () => {
            document.execCommand(
                "insertText",
                false,
                " world, typed at the end",
            );
        });
        expect(text.data).toBe("Hello world, typed at the end");
        ckeditor.selectionChange(true);

        // The focus goes to the toolbox, and CKEditor locks the caret at the end of the typing.
        outside.focus();
        const locked = (
            ckeditor as unknown as {
                _: { savedSelection?: CKEDITOR.dom.selection };
            }
        )._.savedSelection;
        expect(
            locked?.getRanges()[0].startOffset,
            "test setup: CKEditor should have locked the caret at the end of the typing",
        ).toBe(29);

        await history.undo();

        expect(text.data).toBe("Hello");
        expect(errors).toEqual([]);
        expect(history.redoDescriptionL10nKey).toBe(kTypingKey);
        // The caret is where it was before the typing, in the box that the restore focused, not
        // where CKEditor locked it.
        expect(document.activeElement).toBe(box);
        const selection = window.getSelection()!;
        expect(selection.focusNode).toBe(text);
        expect(selection.focusOffset).toBe(5);

        await history.redo();
        expect(text.data).toBe("Hello world, typed at the end");
        expect(errors).toEqual([]);
    });
});
