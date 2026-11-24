// Tests for which CKEditor an undo of typing goes to (getCkeditorsToUndo in ckeditorChanges.ts).
// Typing is undone in the box it was typed in, which need not be the box with the focus: "add a
// row, type in it, click another cell" leaves the typing in the new row's editor (BL-16900).

import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { nextChangeOrder, resetChangeOrderForTests } from "./changeOrder";
import {
    getCkeditorsToUndo,
    getCkeditorToUndo,
    getCkeditorUndoOrder,
    ICkeditorChangeSource,
    resetCkeditorChangesForTests,
    trackCkeditorChanges,
    undoCkeditorTyping,
} from "./ckeditorChanges";
import { UndoStack } from "./UndoStack";

type FakeEditor = ICkeditorChangeSource & {
    canUndo: boolean;
    undos: number;
    box: HTMLElement;
    typed(): void;
};

describe("which CKEditor an undo of typing goes to", () => {
    const page = window as unknown as {
        CKEDITOR?: { currentInstance?: ICkeditorChangeSource | null };
    };

    // Enough of a CKEditor for trackCkeditorChanges and the choice: its "change" listeners,
    // whether it has anything to undo, its undo, and the box it edits.
    function makeEditor(): FakeEditor {
        type Listener = (evt: { data?: { name?: string } }) => void;
        const changeListeners: Listener[] = [];
        const box = document.createElement("div");
        box.className = "bloom-editable";
        document.body.appendChild(box);
        const editor: FakeEditor = {
            canUndo: false,
            undos: 0,
            box,
            element: { $: box },
            undoManager: {
                undoable: () => editor.canUndo,
                undo: () => {
                    editor.undos++;
                    return editor.canUndo;
                },
            },
            on(eventName: string, listener: Listener) {
                if (eventName === "change") changeListeners.push(listener);
            },
            // A keystroke as CKEditor records it: it now has something to undo, and says so.
            typed() {
                editor.canUndo = true;
                changeListeners.forEach((l) => l({}));
            },
        };
        trackCkeditorChanges(editor, new UndoStack());
        return editor;
    }

    function focus(editor: FakeEditor | undefined): void {
        page.CKEDITOR = { currentInstance: editor ?? null };
    }

    beforeEach(() => {
        resetCkeditorChangesForTests();
        resetChangeOrderForTests();
        focus(undefined);
    });
    afterEach(() => {
        delete page.CKEDITOR;
        document.body.innerHTML = "";
    });

    it("goes to the box typed in last when the focused box has nothing to undo", () => {
        const newRow = makeEditor();
        const otherCell = makeEditor();
        newRow.typed();
        focus(otherCell);

        expect(getCkeditorToUndo()).toBe(newRow);
    });

    // "Type in the first cell, add a row, type in it, click the first cell": the new row's typing is
    // the newer, though the focused first cell has typing of its own.
    it("goes to newer typing in another box ahead of older typing in the focused box", () => {
        const firstCell = makeEditor();
        const newRow = makeEditor();
        firstCell.typed();
        newRow.typed();
        focus(firstCell);

        expect(getCkeditorToUndo()).toBe(newRow);
    });

    // A first paste into a box is reported before CKEditor counts it as undoable, so it is not
    // in the record of changes; the focused box's own Undo still has to come first.
    it("goes to the focused box when it has something to undo of its own", () => {
        const typedEarlier = makeEditor();
        const pastedInto = makeEditor();
        typedEarlier.typed();
        pastedInto.canUndo = true; // the paste, which CKEditor reported before it was undoable
        focus(pastedInto);

        expect(getCkeditorToUndo()).toBe(pastedInto);
    });

    it("lists the boxes typed in after the focused one, the most recent first", () => {
        const first = makeEditor();
        const second = makeEditor();
        const focused = makeEditor();
        first.typed();
        second.typed();
        focused.typed();
        focus(focused);

        expect(getCkeditorsToUndo()).toEqual([focused, second, first]);
    });

    // CKEditor can claim an undo it cannot perform; the next box's typing must not be passed over
    // for an older change on the stack.
    it("undoes in the next box when the first finds nothing to restore", () => {
        const newRow = makeEditor();
        const claimsButCannot = makeEditor();
        newRow.typed();
        claimsButCannot.typed();
        claimsButCannot.undoManager!.undo = () => {
            claimsButCannot.undos++;
            return false;
        };

        expect(undoCkeditorTyping()).toBe(true);

        expect(claimsButCannot.undos).toBe(1);
        expect(newRow.undos).toBe(1);
    });

    // "Type in a cell, add a row, type in the new row, undo that typing": what is left in the first
    // cell came before the row, and the row must go first.
    it("reports when the typing it would undo was done, not the latest change anywhere", () => {
        const firstCell = makeEditor();
        const newRow = makeEditor();
        firstCell.typed();
        const typedInFirstCellAt = getCkeditorUndoOrder();
        nextChangeOrder(); // the row
        newRow.typed();
        // Sanity check: the new row's typing is what would go first, and is the newer.
        expect(getCkeditorUndoOrder()).toBeGreaterThan(typedInFirstCellAt);

        newRow.canUndo = false; // its typing undone

        expect(getCkeditorUndoOrder()).toBe(typedInFirstCellAt);
    });

    it("leaves alone typing done before the stack's newest entry", () => {
        const firstCell = makeEditor();
        firstCell.typed();
        const rowAddedAt = nextChangeOrder();

        expect(undoCkeditorTyping(rowAddedAt)).toBe(false);

        expect(firstCell.undos).toBe(0);
    });

    it("passes over a box that has gone from the page", () => {
        const removedCell = makeEditor();
        const typedEarlier = makeEditor();
        typedEarlier.typed();
        removedCell.typed();

        removedCell.box.remove();

        expect(getCkeditorToUndo()).toBe(typedEarlier);
    });

    it("finds nothing when no box has anything to undo", () => {
        focus(makeEditor());

        expect(getCkeditorToUndo()).toBeUndefined();
        expect(undoCkeditorTyping()).toBe(false);
    });

    it("finds nothing when CKEditor has not been loaded", () => {
        delete page.CKEDITOR;

        expect(getCkeditorToUndo()).toBeUndefined();
    });
});
