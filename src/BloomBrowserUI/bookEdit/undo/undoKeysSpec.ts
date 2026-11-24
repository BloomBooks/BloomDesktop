// Tests for Ctrl+Z reaching the entries on the one undo stack ahead of CKEditor (undoKeys.ts).
// The inline image tests (inlineImageInteractions.test.ts) cover picture changes through the
// same keys, and Ctrl+Y.

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

import { nextChangeOrder, resetChangeOrderForTests } from "./changeOrder";
import { installUndoKeys } from "./undoKeys";
import { theOneUndoStack } from "./UndoStack";

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

    function pressCtrlZ(target: HTMLElement): KeyboardEvent {
        const event = new KeyboardEvent("keydown", {
            key: "z",
            ctrlKey: true,
            bubbles: true,
            cancelable: true,
        });
        target.dispatchEvent(event);
        return event;
    }

    beforeEach(() => {
        installUndoKeys(document);
        theOneUndoStack.clear();
        theOneUndoStack.clearLegacyProviders();
        resetChangeOrderForTests();
        toolboxStub.readerToolsHandleUndoKeys = false;
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

        const event = pressCtrlZ(editable);

        expect(undoAddRow).toHaveBeenCalledTimes(1);
        expect(event.defaultPrevented).toBe(true);
        expect(
            reachedTheEditable,
            "CKEditor listens on the editable, and must not also act on the key",
        ).not.toHaveBeenCalled();
    });

    it("undoes a table operation when the focus is outside any text block", () => {
        addRow();

        const event = pressCtrlZ(document.body);

        expect(undoAddRow).toHaveBeenCalledTimes(1);
        expect(event.defaultPrevented).toBe(true);
    });

    it("installs once per document, so one press undoes once", () => {
        installUndoKeys(document);
        addRow();

        pressCtrlZ(editable);

        expect(undoAddRow).toHaveBeenCalledTimes(1);
    });

    it("leaves the key alone when the stack has nothing to undo", () => {
        // Sanity check: nothing on the stack.
        expect(theOneUndoStack.getEntryForNextUndo()).toBeUndefined();

        const event = pressCtrlZ(editable);

        expect(event.defaultPrevented).toBe(false);
    });

    describe("with typing that CKEditor can undo", () => {
        // When CKEditor last recorded a change, on the sequence the stack's entries share.
        let typedAt: number;

        beforeEach(() => {
            typedAt = 0;
            // CKEditor as its legacy provider presents it (legacyUndoProviders.ts).
            theOneUndoStack.registerLegacyProvider({
                name: "ckeditor",
                canUndo: () => typedAt > 0,
                undo: () => true,
                lastChangeOrder: () => typedAt,
            });
        });

        it("leaves the key to CKEditor when the typing is newer than the stack's entry", () => {
            addRow();
            typedAt = nextChangeOrder();

            const event = pressCtrlZ(editable);

            expect(event.defaultPrevented).toBe(false);
            expect(undoAddRow).not.toHaveBeenCalled();
        });

        it("takes the key when the stack's entry is newer than the typing", () => {
            typedAt = nextChangeOrder();
            addRow();

            const event = pressCtrlZ(editable);

            expect(event.defaultPrevented).toBe(true);
            expect(undoAddRow).toHaveBeenCalledTimes(1);
        });
    });

    it("leaves the key to the reader tools in a text block while a markup tool is selected", () => {
        addRow();
        toolboxStub.readerToolsHandleUndoKeys = true;

        const event = pressCtrlZ(editable);

        expect(event.defaultPrevented).toBe(false);
        expect(undoAddRow).not.toHaveBeenCalled();
    });

    it("leaves the key to origami in Change Layout mode", () => {
        addRow();
        document
            .querySelector(".marginBox")!
            .classList.add("origami-layout-mode");

        const event = pressCtrlZ(editable);

        expect(event.defaultPrevented).toBe(false);
        expect(undoAddRow).not.toHaveBeenCalled();
    });
});
