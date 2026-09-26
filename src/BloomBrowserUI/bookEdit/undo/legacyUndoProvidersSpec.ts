// Tests for the adapters round Bloom's pre-existing undo mechanisms (BL-6681).
//
// What these pin is not the mechanisms themselves but the arbitration that workspaceRoot.handleUndo
// used to do inline, and which is easy to lose in a refactor: the order the four are consulted in,
// and the BL-16558 rule that an undo which rewrites an editable's innerHTML must be followed by a
// markup update, because it has just detached every highlight painted over that box.

import { describe, it, expect, beforeEach, vi } from "vitest";
import { UndoStack } from "./UndoStack";

// The providers reach the other frames through workspaceFrames; there are no frames in jsdom.
const page = {
    origamiCanUndo: vi.fn(() => false),
    origamiUndo: vi.fn(),
    imageOperationCanUndo: vi.fn(() => false),
    imageOperationUndo: vi.fn(() => true),
    ckeditorCanUndo: vi.fn(() => false),
    ckeditorUndo: vi.fn(),
};
const toolbox = {
    canUndo: vi.fn(() => false) as (() => boolean) | undefined,
    undo: vi.fn(),
    updateMarkupAfterUndoOrRedo: vi.fn(),
};
let pageAvailable = true;
let toolboxAvailable = true;
vi.mock("../js/workspaceFrames", () => ({
    getEditablePageBundleExports: () => (pageAvailable ? page : null),
    getToolboxBundleExports: () => (toolboxAvailable ? toolbox : null),
}));

import {
    ckeditorUndoProvider,
    imageUndoProvider,
    origamiUndoProvider,
    registerLegacyUndoProviders,
    toolboxUndoProvider,
} from "./legacyUndoProviders";

describe("legacyUndoProviders", () => {
    beforeEach(() => {
        vi.clearAllMocks();
        page.origamiCanUndo.mockReturnValue(false);
        page.imageOperationCanUndo.mockReturnValue(false);
        page.ckeditorCanUndo.mockReturnValue(false);
        toolbox.canUndo = vi.fn(() => false);
        pageAvailable = true;
        toolboxAvailable = true;
    });

    it("registers the four in the order handleUndo consulted them", () => {
        const stack = new UndoStack();
        // Make every mechanism claim to have something, so the order alone decides.
        page.origamiCanUndo.mockReturnValue(true);
        toolbox.canUndo = vi.fn(() => true);
        page.imageOperationCanUndo.mockReturnValue(true);
        page.ckeditorCanUndo.mockReturnValue(true);
        registerLegacyUndoProviders(stack);

        stack.undo();
        expect(page.origamiUndo).toHaveBeenCalledTimes(1);
        expect(toolbox.undo).not.toHaveBeenCalled();

        page.origamiCanUndo.mockReturnValue(false);
        stack.undo();
        expect(toolbox.undo).toHaveBeenCalledTimes(1);
        expect(page.imageOperationUndo).not.toHaveBeenCalled();

        toolbox.canUndo = vi.fn(() => false);
        stack.undo();
        expect(page.imageOperationUndo).toHaveBeenCalledTimes(1);
        expect(page.ckeditorUndo).not.toHaveBeenCalled();

        page.imageOperationCanUndo.mockReturnValue(false);
        stack.undo();
        expect(page.ckeditorUndo).toHaveBeenCalledTimes(1);
    });

    describe("the markup update after an undo that rewrites an editable (BL-16558)", () => {
        it("follows the reader tools' undo", () => {
            toolboxUndoProvider.undo();
            expect(toolbox.undo).toHaveBeenCalledTimes(1);
            expect(toolbox.updateMarkupAfterUndoOrRedo).toHaveBeenCalledTimes(
                1,
            );
        });

        it("follows CKEditor's undo", () => {
            ckeditorUndoProvider.undo();
            expect(page.ckeditorUndo).toHaveBeenCalledTimes(1);
            expect(toolbox.updateMarkupAfterUndoOrRedo).toHaveBeenCalledTimes(
                1,
            );
        });

        it("does not follow origami's or the image undo, which rewrite no editable", () => {
            origamiUndoProvider.undo();
            imageUndoProvider.undo();
            expect(toolbox.updateMarkupAfterUndoOrRedo).not.toHaveBeenCalled();
        });

        it("survives CKEditor's undo running with no toolbox frame", () => {
            toolboxAvailable = false;
            expect(() => ckeditorUndoProvider.undo()).not.toThrow();
            expect(page.ckeditorUndo).toHaveBeenCalledTimes(1);
        });
    });

    describe("canUndo", () => {
        it("is false for every provider when the frames are not there yet", () => {
            pageAvailable = false;
            toolboxAvailable = false;
            for (const p of [
                origamiUndoProvider,
                toolboxUndoProvider,
                imageUndoProvider,
                ckeditorUndoProvider,
            ]) {
                expect(p.canUndo(), p.name).toBe(false);
            }
        });

        it("tolerates a toolbox bundle with no canUndo, because C# polls it on a timer", () => {
            toolbox.canUndo = undefined;
            expect(toolboxUndoProvider.canUndo()).toBe(false);
        });

        it("reports each mechanism's own answer", () => {
            page.origamiCanUndo.mockReturnValue(true);
            expect(origamiUndoProvider.canUndo()).toBe(true);
            toolbox.canUndo = vi.fn(() => true);
            expect(toolboxUndoProvider.canUndo()).toBe(true);
            page.imageOperationCanUndo.mockReturnValue(true);
            expect(imageUndoProvider.canUndo()).toBe(true);
            page.ckeditorCanUndo.mockReturnValue(true);
            expect(ckeditorUndoProvider.canUndo()).toBe(true);
        });
    });
});
