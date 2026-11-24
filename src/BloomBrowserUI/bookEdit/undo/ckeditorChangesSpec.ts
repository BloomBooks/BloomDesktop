// Tests for which CKEditor an undo of typing goes to (getCkeditorToUndo in ckeditorChanges.ts).
// Typing is undone in the box it was typed in, which need not be the box with the focus: "add a
// row, type in it, click another cell" leaves the typing in the new row's editor (BL-16900).

import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { resetChangeOrderForTests } from "./changeOrder";
import {
    getCkeditorToUndo,
    ICkeditorChangeSource,
    resetCkeditorChangesForTests,
    trackCkeditorChanges,
} from "./ckeditorChanges";
import { UndoStack } from "./UndoStack";

describe("getCkeditorToUndo", () => {
    // Enough of a CKEditor for trackCkeditorChanges and getCkeditorToUndo: its "change" listeners,
    // whether it has anything to undo, and the box it edits.
    function makeEditor(): ICkeditorChangeSource & {
        canUndo: boolean;
        box: HTMLElement;
        typed(): void;
    } {
        type Listener = (evt: { data?: { name?: string } }) => void;
        const changeListeners: Listener[] = [];
        const box = document.createElement("div");
        box.className = "bloom-editable";
        document.body.appendChild(box);
        const editor = {
            canUndo: false,
            box,
            element: { $: box },
            undoManager: { undoable: () => editor.canUndo },
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

    beforeEach(() => {
        resetCkeditorChangesForTests();
        resetChangeOrderForTests();
    });
    afterEach(() => {
        document.body.innerHTML = "";
    });

    it("goes to the box typed in last, though another box has the focus", () => {
        const newRow = makeEditor();
        const otherCell = makeEditor();
        newRow.typed();

        expect(getCkeditorToUndo(otherCell)).toBe(newRow);
    });

    it("goes to the focused box once the box typed in last has nothing more to undo", () => {
        const first = makeEditor();
        const second = makeEditor();
        first.typed();
        second.typed();
        // Sanity check: the last typing is the second box's.
        expect(getCkeditorToUndo(first)).toBe(second);

        second.canUndo = false;

        expect(getCkeditorToUndo(first)).toBe(first);
    });

    it("passes over a box that has gone from the page", () => {
        const removedCell = makeEditor();
        const focused = makeEditor();
        focused.canUndo = true;
        removedCell.typed();

        removedCell.box.remove();

        expect(getCkeditorToUndo(focused)).toBe(focused);
    });

    it("finds nothing when no box has anything to undo", () => {
        const focused = makeEditor();

        expect(getCkeditorToUndo(focused)).toBeUndefined();
        expect(getCkeditorToUndo(undefined)).toBeUndefined();
    });
});
