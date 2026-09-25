import { beforeEach, describe, expect, it } from "vitest";
import {
    getCkeditorChangeOrder,
    getTableChangeOrder,
    noteCkeditorChange,
    noteTableChange,
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
        noteTableChange();
        expect(
            getTableChangeOrder(),
            "The table's change should have come after CKEditor's non-existent one.",
        ).toBeGreaterThan(getCkeditorChangeOrder());

        noteCkeditorChange();
        expect(
            getCkeditorChangeOrder(),
            "Typing after adding a row should be recorded as the later of the two.",
        ).toBeGreaterThan(getTableChangeOrder());

        noteTableChange();
        expect(
            getTableChangeOrder(),
            "Adding a second row should put the table back in front.",
        ).toBeGreaterThan(getCkeditorChangeOrder());
    });

    it("routes Undo to the typing after a row is added and then typed in", () => {
        // The two calls the real code makes, in the order the page makes them.
        noteTableChange();
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
});
