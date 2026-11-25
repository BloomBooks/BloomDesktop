// Tests for Ctrl+Z and Ctrl+Y reaching the one undo stack ahead of CKEditor (undoKeys.ts). The
// inline image tests (inlineImageInteractions.test.ts) cover picture changes through the same keys.

import { describe, it, expect, beforeEach, afterEach, vi, Mock } from "vitest";

// There is no toolbox frame in these tests. A test that needs the reader tools to have a markup
// tool selected sets readerToolsHandleUndoKeys.
const toolboxStub = vi.hoisted(() => ({ readerToolsHandleUndoKeys: false }));
vi.mock("../js/workspaceFrames", async (importOriginal) => ({
    ...(await importOriginal<typeof import("../js/workspaceFrames")>()),
    getToolboxBundleExports: () => ({
        readerToolsHandleUndoKeys: () => toolboxStub.readerToolsHandleUndoKeys,
    }),
}));

// The CKEditor an undo of typing would go to, as getCkeditorToUndo would choose it. A test of
// typing sets it.
const ckeditorStub = vi.hoisted(() => ({
    editorToUndo: undefined as { element: { $: HTMLElement } } | undefined,
}));
vi.mock("./ckeditorChanges", async (importOriginal) => ({
    ...(await importOriginal<typeof import("./ckeditorChanges")>()),
    getCkeditorToUndo: () => ckeditorStub.editorToUndo,
}));

import { nextChangeOrder, resetChangeOrderForTests } from "./changeOrder";
import { installUndoKeys } from "./undoKeys";
import { theOneUndoStack } from "./UndoStack";

/** Press Ctrl and `key` in `target`, and return the keydown event so a test can see if it was claimed. */
function pressCtrl(target: Element, key: string): KeyboardEvent {
    const event = new KeyboardEvent("keydown", {
        key,
        ctrlKey: true,
        bubbles: true,
        cancelable: true,
    });
    target.dispatchEvent(event);
    return event;
}

describe("Ctrl+Z for the entries on the one undo stack", () => {
    let editable: HTMLElement;
    let undoAddRow: Mock<() => void>;

    /** A table operation, as tableUndo.ts records one. */
    function addRow(): void {
        theOneUndoStack.push({
            label: "Add Row",
            kind: "custom",
            undo: undoAddRow,
        });
    }

    beforeEach(() => {
        installUndoKeys(document);
        theOneUndoStack.clear();
        theOneUndoStack.clearLegacyProviders();
        resetChangeOrderForTests();
        toolboxStub.readerToolsHandleUndoKeys = false;
        ckeditorStub.editorToUndo = undefined;
        document.body.innerHTML = `<div class="marginBox"><div class="bloom-editable" lang="en"><p>Apple</p></div></div>`;
        editable = document.querySelector(".bloom-editable") as HTMLElement;
        undoAddRow = vi.fn<() => void>();
    });

    afterEach(() => {
        theOneUndoStack.clear();
        document.body.innerHTML = "";
    });

    // CKEditor in the cell knows nothing of the stack's entries, and does nothing with the key.
    it("undoes a table operation in a text block, before CKEditor sees the key", () => {
        addRow();
        const reachedTheEditable = vi.fn();
        editable.addEventListener("keydown", reachedTheEditable);
        // Sanity check: the row is what the next Undo would take back.
        expect(theOneUndoStack.peekUndoLabel()).toBe("Add Row");

        const event = pressCtrl(editable, "z");

        expect(undoAddRow).toHaveBeenCalledTimes(1);
        expect(event.defaultPrevented).toBe(true);
        expect(
            reachedTheEditable,
            "CKEditor listens on the editable, and must not also act on the key",
        ).not.toHaveBeenCalled();
    });

    it("undoes a table operation when the focus is outside any text block", () => {
        addRow();

        const event = pressCtrl(document.body, "z");

        expect(undoAddRow).toHaveBeenCalledTimes(1);
        expect(event.defaultPrevented).toBe(true);
    });

    it("installs once per document, so one press undoes once", () => {
        installUndoKeys(document);
        addRow();

        pressCtrl(editable, "z");

        expect(undoAddRow).toHaveBeenCalledTimes(1);
    });

    it("leaves the key alone when the stack has nothing to undo", () => {
        // Sanity check: nothing on the stack.
        expect(theOneUndoStack.getEntryForNextUndo()).toBeUndefined();

        const event = pressCtrl(editable, "z");

        expect(event.defaultPrevented).toBe(false);
    });

    describe("with typing that CKEditor can undo", () => {
        // When CKEditor last recorded a change, on the sequence the stack's entries share.
        let typedAt: number;
        let ckeditorUndos: number;

        // Type in `box`, which is then where an undo of typing goes.
        function typeIn(box: HTMLElement): void {
            typedAt = nextChangeOrder();
            ckeditorStub.editorToUndo = { element: { $: box } };
        }

        beforeEach(() => {
            typedAt = 0;
            ckeditorUndos = 0;
            // CKEditor as its legacy provider presents it (legacyUndoProviders.ts).
            theOneUndoStack.registerLegacyProvider({
                name: "ckeditor",
                canUndo: () => typedAt > 0,
                undo: () => {
                    ckeditorUndos++;
                    return true;
                },
                lastChangeOrder: () => typedAt,
            });
            editable.setAttribute("tabindex", "0");
            editable.focus();
        });

        it("leaves the key to CKEditor when the typing is newer than the stack's entry and in the focused box", () => {
            addRow();
            typeIn(editable);
            // Sanity check: the focus is in the box typed in.
            expect(document.activeElement).toBe(editable);

            const event = pressCtrl(editable, "z");

            expect(event.defaultPrevented).toBe(false);
            expect(undoAddRow).not.toHaveBeenCalled();
        });

        // "Add a row, type in it, click another cell, Ctrl+Z": the focused cell's CKEditor would do
        // nothing, and the row must not go with the typing in it (BL-16900).
        it("undoes typing in another box through the stack, not the older row", () => {
            const newRowCell = document.createElement("div");
            newRowCell.className = "bloom-editable";
            document.querySelector(".marginBox")!.appendChild(newRowCell);
            addRow();
            typeIn(newRowCell);

            const event = pressCtrl(editable, "z");

            expect(event.defaultPrevented).toBe(true);
            expect(ckeditorUndos).toBe(1);
            expect(undoAddRow).not.toHaveBeenCalled();
        });

        it("takes the key when the stack's entry is newer than the typing", () => {
            typedAt = nextChangeOrder();
            addRow();

            const event = pressCtrl(editable, "z");

            expect(event.defaultPrevented).toBe(true);
            expect(undoAddRow).toHaveBeenCalledTimes(1);
        });
    });

    it("leaves the key to the reader tools in a text block while a markup tool is selected", () => {
        addRow();
        toolboxStub.readerToolsHandleUndoKeys = true;

        const event = pressCtrl(editable, "z");

        expect(event.defaultPrevented).toBe(false);
        expect(undoAddRow).not.toHaveBeenCalled();
    });

    it("leaves the key to origami in Change Layout mode", () => {
        addRow();
        document
            .querySelector(".marginBox")!
            .classList.add("origami-layout-mode");

        const event = pressCtrl(editable, "z");

        expect(event.defaultPrevented).toBe(false);
        expect(undoAddRow).not.toHaveBeenCalled();
    });
});

describe("Ctrl+Y for the entries on the one undo stack", () => {
    // A document of its own, so the key handler can be installed with a redo the test watches.
    let doc: Document;
    let redo: Mock<() => void>;

    beforeEach(() => {
        theOneUndoStack.clear();
        theOneUndoStack.clearLegacyProviders();
        doc = document.implementation.createHTMLDocument("page");
        doc.body.innerHTML = `<div class="marginBox"><div class="bloom-editable" lang="en"></div><input></div>`;
        redo = vi.fn<() => void>();
        installUndoKeys(doc, redo);
        // An undone picture change, which the stack can redo.
        theOneUndoStack.push({
            label: "Change picture",
            kind: "custom",
            undo: () => {},
            redo: () => {},
        });
        void theOneUndoStack.undo();
        // Sanity check.
        expect(theOneUndoStack.canRedo()).toBe(true);
    });

    afterEach(() => theOneUndoStack.clear());

    it("redoes through the function the page gives it", () => {
        const event = pressCtrl(doc.querySelector(".bloom-editable")!, "y");

        expect(event.defaultPrevented).toBe(true);
        expect(redo).toHaveBeenCalledTimes(1);
    });

    it("leaves Ctrl+Y to a form field", () => {
        const event = pressCtrl(doc.querySelector("input")!, "y");

        expect(event.defaultPrevented).toBe(false);
        expect(redo).not.toHaveBeenCalled();
    });
});
