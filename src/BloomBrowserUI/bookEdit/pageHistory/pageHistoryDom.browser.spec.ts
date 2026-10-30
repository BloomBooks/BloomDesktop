// Restoring the selection after an undo or redo, in a real browser with Bloom's CKEditor 4.5.1
// (lib/ckeditor) on the box. Putting the selection in a box focuses the box, and CKEditor's focus
// listener runs inside the call that set the selection. When the focus leaves an inline editor's
// box, CKEditor saves its selection (editor.lockSelection); when the focus comes back, it selects
// that again (editor.unlockSelection(true)), which throws an IndexSizeError if the text has
// become shorter than the saved offset. These run in headless Edge (the "browser" vitest project).
import { afterEach, beforeAll, describe, expect, it } from "vitest";
import { createEditTimeMarkupFilter } from "./editTimeMarkup";
import { createPageHistory } from "./pageHistory";
import type { PageHistory } from "./pageHistoryTypes";

const kTypingKey = "EditTab.Undo.Typing";

let page: HTMLElement | undefined;
let outside: HTMLInputElement | undefined;
let editors: CKEDITOR.editor[] = [];
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

/** Attach an inline CKEditor to box, as the page frame does, once it is ready. */
async function attachCkEditor(box: HTMLElement): Promise<CKEDITOR.editor> {
    const ckeditor = CKEDITOR.inline(box.id);
    editors.push(ckeditor);
    await new Promise<void>((resolve) =>
        ckeditor.on("instanceReady", () => resolve()),
    );
    return ckeditor;
}

/** The selection that CKEditor locked when the focus left the box of ckeditor, if any. */
function lockedSelection(
    ckeditor: CKEDITOR.editor,
): CKEDITOR.dom.selection | undefined {
    return (
        ckeditor as unknown as {
            _: { savedSelection?: CKEDITOR.dom.selection };
        }
    )._.savedSelection;
}

/**
 * Put the caret in box at offset in text, and have CKEditor note it as the caret of its box, as
 * its keyup and mouseup listeners do.
 */
function placeCaret(
    ckeditor: CKEDITOR.editor,
    box: HTMLElement,
    text: Text,
    offset: number,
): void {
    box.focus();
    window.getSelection()!.collapse(text, offset);
    ckeditor.forceNextSelectionCheck();
    ckeditor.selectionChange(true);
}

/**
 * A page of two boxes with CKEditor on each, an input outside it that takes the focus as the
 * toolbox does, and a history with Bloom's filter.
 */
async function setUpTwoBoxes(): Promise<{
    box1: HTMLElement;
    box2: HTMLElement;
    editor1: CKEDITOR.editor;
    editor2: CKEDITOR.editor;
    text1: Text;
    text2: Text;
}> {
    errors = [];
    window.addEventListener("error", onError);
    page = document.createElement("div");
    page.className = "bloom-page";
    page.innerHTML =
        '<div class="bloom-editable" id="box1" contenteditable="true" lang="en"><p>Hello</p></div>' +
        '<div class="bloom-editable" id="box2" contenteditable="true" lang="en"><p>World</p></div>';
    document.body.appendChild(page);
    outside = document.createElement("input");
    document.body.appendChild(outside);
    const box1 = page.querySelector<HTMLElement>("#box1")!;
    const box2 = page.querySelector<HTMLElement>("#box2")!;
    const editor1 = await attachCkEditor(box1);
    const editor2 = await attachCkEditor(box2);
    // CKEditor sets each box's content again as it attaches, so these are the text nodes from now.
    const text1 = box1.querySelector("p")!.firstChild as Text;
    const text2 = box2.querySelector("p")!.firstChild as Text;
    // The filter leaves out the classes that CKEditor gives a box as it gains and loses the focus,
    // as in Bloom, so that the step holds only the box that it changed.
    history = createPageHistory({
        root: page,
        filter: createEditTimeMarkupFilter(),
    });
    return { box1, box2, editor1, editor2, text1, text2 };
}

afterEach(() => {
    window.removeEventListener("error", onError);
    history?.dispose();
    editors.forEach((editor) => editor.destroy());
    page?.remove();
    outside?.remove();
    history = undefined;
    editors = [];
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
        const ckeditor = await attachCkEditor(box);
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

    it("keeps the selection that CKEditor locked in a box that the undo did not change, so that the focus coming back to it with no click puts the caret where the user left it", async () => {
        const { box1, box2, editor1, text1, text2 } = await setUpTwoBoxes();
        box2.focus();
        window.getSelection()!.collapse(text2, 5);
        await history!.doTransaction(kTypingKey, () => {
            document.execCommand("insertText", false, " there");
        });
        expect(text2.data).toBe("World there");
        // The user puts the caret in the middle of the other box, then goes to the toolbox.
        placeCaret(editor1, box1, text1, 3);
        outside!.focus();
        expect(
            lockedSelection(editor1)?.getRanges()[0].startOffset,
            "test setup: CKEditor should have locked the caret in box1",
        ).toBe(3);

        await history!.undo();
        expect(text2.data).toBe("World");
        expect(document.activeElement).toBe(box2);

        // Tab, or a tool that gives the focus back, focuses box1 with no mouse press.
        box1.focus();
        expect(errors).toEqual([]);
        const selection = window.getSelection()!;
        expect(selection.focusNode).toBe(text1);
        expect(selection.focusOffset).toBe(3);
    });

    it("drops the selection that CKEditor locked in a box that the undo changed, though the restored selection is in another box", async () => {
        const { box1, box2, editor1, editor2, text1, text2 } =
            await setUpTwoBoxes();
        placeCaret(editor2, box2, text2, 0);
        await history!.doTransaction("Change", () => {
            text1.data = "Hello world";
        });
        placeCaret(editor1, box1, text1, 11);
        outside!.focus();
        expect(
            lockedSelection(editor1)?.getRanges()[0].startOffset,
            "test setup: CKEditor should have locked the caret at the end of box1",
        ).toBe(11);

        await history!.undo();
        expect(text1.data).toBe("Hello");
        expect(document.activeElement).toBe(box2);
        expect(lockedSelection(editor1)).toBeUndefined();

        box1.focus();
        expect(errors).toEqual([]);
    });
});
