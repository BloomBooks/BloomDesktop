// Tests for the workspace-frame half of undoing a canvas element deletion (BL-6681 Stage 2b).

import { describe, it, expect, beforeEach, vi } from "vitest";
import { UndoStack } from "./UndoStack";
import type { ICanvasElementDeletionRecord } from "./canvasElementDeletion";

const page = {
    restoreDeletedCanvasElement: vi.fn(),
    redeleteCanvasElement: vi.fn(),
};
let pageAvailable = true;
vi.mock("../js/workspaceFrames", () => ({
    getEditablePageBundleExports: () => (pageAvailable ? page : null),
}));

import { recordCanvasElementDeletion } from "./canvasElementDeletionEntry";

const record: ICanvasElementDeletionRecord = {
    pageId: "page-1",
    canvasIndex: 0,
    elementIndex: 1,
    elementHtml: "<div class='bloom-canvas-element'></div>",
    bubbleSpecs: [null, null],
};

describe("recordCanvasElementDeletion", () => {
    let stack: UndoStack;
    beforeEach(() => {
        vi.clearAllMocks();
        pageAvailable = true;
        stack = new UndoStack();
        stack.setCurrentPageId("page-1");
    });

    it("pushes one page-scoped entry the Undo button can see", () => {
        recordCanvasElementDeletion(record, stack);
        expect(stack.getEntryCount()).toBe(1);
        expect(stack.canUndo()).toBe(true);
        expect(stack.peekUndoLabel()).toBe("Delete canvas element");
        // Page-scoped: it does not survive leaving the page.
        stack.clearPageScopedEntries();
        expect(stack.canUndo()).toBe(false);
    });

    it("undoes by asking the page frame to restore, and redoes by asking it to delete again", () => {
        recordCanvasElementDeletion(record, stack);
        stack.undo();
        expect(page.restoreDeletedCanvasElement).toHaveBeenCalledWith(record);
        expect(stack.canRedo()).toBe(true);
        stack.redo();
        expect(page.redeleteCanvasElement).toHaveBeenCalledWith(record);
        expect(stack.canUndo()).toBe(true);
    });

    it("fails the undo cleanly, keeping the entry, when there is no page frame", () => {
        recordCanvasElementDeletion(record, stack);
        pageAvailable = false;
        expect(() => stack.undo()).toThrow(/no page frame/);
        expect(stack.peekUndoLabel()).toBe("Delete canvas element");
    });
});
