import { beforeEach, describe, expect, it } from "vitest";
import {
    getCkeditorChangeOrder,
    getTableChangeOrder,
    noteCkeditorChange,
    noteTableHistoryUpdate,
    resetUndoOrderingForTests,
    shouldUndoGoToTable,
} from "./undoOrdering";

describe("shouldUndoGoToTable", () => {
    it("says no when the table has nothing to undo", () => {
        expect(
            shouldUndoGoToTable({
                tableCanUndo: false,
                ckeditorCanUndo: true,
                // Even with the table as the more recent of the two, an empty history cannot
                // answer an Undo.
                tableChangeOrder: 2,
                ckeditorChangeOrder: 1,
            }),
        ).toBe(false);
    });

    it("says yes when only the table has something to undo", () => {
        expect(
            shouldUndoGoToTable({
                tableCanUndo: true,
                ckeditorCanUndo: false,
                tableChangeOrder: 1,
                ckeditorChangeOrder: 0,
            }),
        ).toBe(true);
    });

    it("says yes when CKEditor changed but has nothing to undo", () => {
        // A box whose typing has already been undone reports nothing undoable, however recently
        // it changed. The table is then the only stack that can answer.
        expect(
            shouldUndoGoToTable({
                tableCanUndo: true,
                ckeditorCanUndo: false,
                tableChangeOrder: 1,
                ckeditorChangeOrder: 5,
            }),
        ).toBe(true);
    });

    it("says yes when the row came after the typing", () => {
        expect(
            shouldUndoGoToTable({
                tableCanUndo: true,
                ckeditorCanUndo: true,
                tableChangeOrder: 2,
                ckeditorChangeOrder: 1,
            }),
        ).toBe(true);
    });

    it("says no when the typing came after the row", () => {
        // The case this whole module exists for: add a row, then type. Both stacks hold
        // something, and the typing is what the person means to take back.
        expect(
            shouldUndoGoToTable({
                tableCanUndo: true,
                ckeditorCanUndo: true,
                tableChangeOrder: 1,
                ckeditorChangeOrder: 2,
            }),
        ).toBe(false);
    });

    it("says no when neither has ever changed", () => {
        expect(
            shouldUndoGoToTable({
                tableCanUndo: false,
                ckeditorCanUndo: false,
                tableChangeOrder: 0,
                ckeditorChangeOrder: 0,
            }),
        ).toBe(false);
    });
});

describe("the recorded order of the two stacks", () => {
    beforeEach(() => {
        resetUndoOrderingForTests();
    });

    it("starts with neither having changed", () => {
        expect({
            table: getTableChangeOrder(),
            ckeditor: getCkeditorChangeOrder(),
        }).toEqual({ table: 0, ckeditor: 0 });
    });

    it("puts each change after the one before it, whichever stack it is on", () => {
        noteTableHistoryUpdate("Add Row");
        expect(
            getTableChangeOrder(),
            "The table's change should have come after CKEditor's non-existent one.",
        ).toBeGreaterThan(getCkeditorChangeOrder());

        noteCkeditorChange();
        expect(
            getCkeditorChangeOrder(),
            "Typing after adding a row should be recorded as the later of the two.",
        ).toBeGreaterThan(getTableChangeOrder());

        noteTableHistoryUpdate("Add Row");
        expect(
            getTableChangeOrder(),
            "Adding a second row should put the table back in front.",
        ).toBeGreaterThan(getCkeditorChangeOrder());
    });

    it("routes Undo to the typing after a row is added and then typed in", () => {
        // The two calls the real code makes, in the order the page makes them.
        noteTableHistoryUpdate("Add Row");
        noteCkeditorChange();
        expect(
            shouldUndoGoToTable({
                tableCanUndo: true,
                ckeditorCanUndo: true,
                tableChangeOrder: getTableChangeOrder(),
                ckeditorChangeOrder: getCkeditorChangeOrder(),
            }),
        ).toBe(false);
    });

    it("routes Undo to the typing once the row added after it has been undone", () => {
        // Add a row, type, add a second row, then Undo takes the second row back off.
        noteTableHistoryUpdate("Add Row");
        noteCkeditorChange();
        const typing = getCkeditorChangeOrder();
        noteTableHistoryUpdate("Add Row");
        expect(
            getTableChangeOrder(),
            "Sanity check: before the Undo, the second row is the latest change.",
        ).toBeGreaterThan(typing);

        noteTableHistoryUpdate("Undo Add Row");

        expect(
            getTableChangeOrder(),
            "After the Undo, what is left of the table's history (the first row) came before the typing.",
        ).toBeLessThan(typing);
        expect(
            shouldUndoGoToTable({
                tableCanUndo: true,
                ckeditorCanUndo: true,
                tableChangeOrder: getTableChangeOrder(),
                ckeditorChangeOrder: getCkeditorChangeOrder(),
            }),
            "The next Undo should take back the typing, not the first row.",
        ).toBe(false);
    });

    it("puts a redone operation back in front", () => {
        noteTableHistoryUpdate("Add Row");
        noteTableHistoryUpdate("Undo Add Row");
        noteCkeditorChange();
        noteTableHistoryUpdate("Redo Add Row");
        expect(getTableChangeOrder()).toBeGreaterThan(getCkeditorChangeOrder());
    });

    it("forgets the table's history when the library clears it", () => {
        noteTableHistoryUpdate("Add Row");
        noteTableHistoryUpdate("Clear History");
        expect(getTableChangeOrder()).toBe(0);
    });
});
