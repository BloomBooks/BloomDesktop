// Tests for a table's structural operations as entries on the one undo stack (BL-6681, BL-16818).
//
// The bloom-table library is replaced by a fake history that behaves like the real one where it
// matters here: undoLast and redoLast act on the newest operation and announce themselves as
// "Undo <label>" / "Redo <label>", the way the real library's tableHistoryUpdated event reaches
// noteTableHistoryUpdate through tableEditing.ts.

import { describe, it, expect, beforeEach, vi } from "vitest";

const library = vi.hoisted(() => ({
    history: [] as string[],
    redoStack: [] as string[],
    // Set by the test so the fake can announce its undos to the code under test.
    announce: undefined as ((operation: string) => void) | undefined,
}));

vi.mock("bloom-table", () => ({
    tableHistoryManager: {
        canUndo: () => library.history.length > 0,
        getLastOperationLabel: () =>
            library.history[library.history.length - 1] ?? null,
        getNextRedoLabel: () =>
            library.redoStack[library.redoStack.length - 1] ?? null,
        undoLast: () => {
            const label = library.history.pop();
            if (!label) return false;
            library.redoStack.push(label);
            library.announce?.(`Undo ${label}`);
            return true;
        },
        redoLast: () => {
            const label = library.redoStack.pop();
            if (!label) return false;
            library.history.push(label);
            library.announce?.(`Redo ${label}`);
            return true;
        },
    },
}));

import { nextChangeOrder, resetChangeOrderForTests } from "./changeOrder";
import {
    noteTableHistoryUpdate,
    resetTableUndoForTests,
    tableCanUndo,
} from "./tableUndo";
import { UndoStack } from "./UndoStack";

describe("table undo on the one stack", () => {
    let stack: UndoStack;
    let ckeditorChangedAt: number;
    let ckeditorUndos: number;

    /** What the library does when the person makes a structural change: record and announce it. */
    function tableOperation(label: string): void {
        library.history.push(label);
        library.redoStack = [];
        noteTableHistoryUpdate(label, stack);
    }

    /** A keystroke in a cell, as CKEditor reports it on the shared sequence. */
    function typing(): void {
        ckeditorChangedAt = nextChangeOrder();
    }

    beforeEach(() => {
        library.history = [];
        library.redoStack = [];
        document.body.innerHTML = `<div class="marginBox"></div>`;
        resetChangeOrderForTests();
        resetTableUndoForTests();
        stack = new UndoStack();
        library.announce = (operation) =>
            noteTableHistoryUpdate(operation, stack);
        ckeditorChangedAt = 0;
        ckeditorUndos = 0;
        // CKEditor, as its legacy provider presents it: it has something to undo once there has
        // been typing, and reports when that was.
        stack.registerLegacyProvider({
            name: "ckeditor",
            canUndo: () => ckeditorChangedAt > 0,
            undo: () => {
                ckeditorUndos++;
                return true;
            },
            lastChangeOrder: () => ckeditorChangedAt,
        });
    });

    it("makes each operation the library records an entry that the library undoes and redoes", () => {
        tableOperation("Add Row");
        expect(stack.getEntryCount()).toBe(1);
        expect(stack.peekUndoLabel()).toBe("Add Row");

        stack.undo();
        expect(library.history).toEqual([]);
        // The library's announcement of that undo adds nothing to the stack.
        expect(stack.getEntryCount()).toBe(1);
        expect(stack.peekRedoLabel()).toBe("Add Row");

        stack.redo();
        expect(library.history).toEqual(["Add Row"]);
        expect(stack.getEntryCount()).toBe(1);
        expect(stack.peekUndoLabel()).toBe("Add Row");
    });

    it("undoes typing that came after a row before the row", () => {
        tableOperation("Add Row");
        typing();

        stack.undo();

        expect(ckeditorUndos).toBe(1);
        expect(library.history).toEqual(["Add Row"]);
    });

    it("undoes a row that came after typing before the typing", () => {
        typing();
        tableOperation("Add Row");

        stack.undo();

        expect(ckeditorUndos).toBe(0);
        expect(library.history).toEqual([]);
    });

    it("hands the turn to typing that came before a row once the row is undone", () => {
        typing();
        tableOperation("Add Row");
        stack.undo();
        expect(library.history).toEqual([]); // sanity check: the row went first

        stack.undo();

        expect(ckeditorUndos).toBe(1);
    });

    it("reaches an earlier row when CKEditor turns out to have nothing to undo", () => {
        tableOperation("Add Row");
        typing();
        stack.clearLegacyProviders();
        stack.registerLegacyProvider({
            name: "ckeditor",
            canUndo: () => true,
            undo: () => false,
            lastChangeOrder: () => ckeditorChangedAt,
        });

        stack.undo();

        expect(library.history).toEqual([]);
    });

    describe("in Change Layout mode", () => {
        beforeEach(() => {
            tableOperation("Add Row");
            document
                .querySelector(".marginBox")!
                .classList.add("origami-layout-mode");
        });

        it("leaves the table's history out of Undo", () => {
            expect(tableCanUndo()).toBe(false);
            expect(stack.canUndo()).toBe(false);

            stack.undo();

            expect(library.history).toEqual(["Add Row"]);
        });

        it("lets it back in once the mode is left", () => {
            document
                .querySelector(".marginBox")!
                .classList.remove("origami-layout-mode");

            expect(tableCanUndo()).toBe(true);
            stack.undo();
            expect(library.history).toEqual([]);
        });
    });

    describe("when the library's history and the stack's no longer match", () => {
        it("refuses, discarding the stack, after the library cleared its history", () => {
            tableOperation("Add Row");
            library.history = [];
            noteTableHistoryUpdate("Clear History", stack);

            expect(() => stack.undo()).toThrow(/no longer holds "Add Row"/);
            expect(stack.getEntryCount()).toBe(0);
        });

        it("refuses after something other than the stack undid a table operation", () => {
            tableOperation("Add Row");
            tableOperation("Add Column");
            library.history.pop();
            noteTableHistoryUpdate("Undo Add Column", stack);

            expect(() => stack.undo()).toThrow(/no longer holds/);
        });

        it("refuses when the library's newest operation is not the entry's", () => {
            tableOperation("Add Row");
            library.history = ["Delete Column"];

            expect(() => stack.undo()).toThrow(
                /no longer holds "Add Row".*holds "Delete Column"/,
            );
        });
    });

    it("adds nothing for an announcement that names no operation, or a detach", () => {
        noteTableHistoryUpdate(undefined, stack);
        noteTableHistoryUpdate("Detach Table", stack);

        expect(stack.getEntryCount()).toBe(0);
    });
});
